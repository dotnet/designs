using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

#nullable enable

namespace Throttling
{
    #region Rate limit APIs
    public interface IResourceLimiter
    {
        // An estimated count of resources.
        long EstimatedCount { get; }

        // Fast synchronous attempt to acquire resources.
        // Set requestedCount to 0 to get whether resource limit has been reached.
        bool TryAcquire(long requestedCount, out Resource resource);

        // Wait until the requested resources are available.
        // Set requestedCount to 0 to wait until resource is replenished.
        // An exception is thrown if resources cannot be obtained.
        ValueTask<Resource> AcquireAsync(long requestedCount, CancellationToken cancellationToken = default);
    }

    // Represent an aggregated resource (e.g. a resource limiter aggregated by IP)
    public interface IAggregatedResourceLimiter<TKey>
    {
        // an inaccurate view of resources
        long EstimatedCount(TKey resourceID);

        // Fast synchronous attempt to acquire resources
        // Set requestedCount to 0 to get whether resource limit has been reached
        bool TryAcquire(TKey resourceID, long requestedCount, out Resource resource);

        // Wait until the requested resources are available
        // Set requestedCount to 0 to wait until resource is replenished
        // If unsuccessful, throw
        ValueTask<Resource> AcquireAsync(TKey resourceID, long requestedCount, CancellationToken cancellationToken = default);
    }

    public struct Resource : IDisposable
    {
        public object? State { get; init; }

        private Action<Resource>? _onDispose;

        public Resource(object? state, Action<Resource>? onDispose)
        {
            State = state;
            _onDispose = onDispose;
        }

        public void Dispose()
        {
            _onDispose?.Invoke(this);
        }

        public static Resource NoopResource = new Resource(null, null);
    }

    public static class ResourceLimiterExtensions
    {
        public static bool TryAcquire(this IResourceLimiter limiter, out Resource resource)
        {
            return limiter.TryAcquire(1, out resource);
        }

        public static ValueTask<Resource> AcquireAsync(this IResourceLimiter limiter, CancellationToken cancellationToken = default)
        {
            return limiter.AcquireAsync(1, cancellationToken);
        }
        public static bool TryAcquire<TKey>(this IAggregatedResourceLimiter<TKey> limiter, TKey resourceId, out Resource resource)
        {
            return limiter.TryAcquire(resourceId, 1, out resource);
        }

        public static ValueTask<Resource> AcquireAsync<TKey>(this IAggregatedResourceLimiter<TKey> limiter, TKey resourceId, CancellationToken cancellationToken = default)
        {
            return limiter.AcquireAsync(resourceId, 1, cancellationToken);
        }
    }

    // Concurrency limit
    internal class LocalConcurrencyLimiter : IResourceLimiter
    {
        private long _resourceCount;
        private readonly long _maxResourceCount;
        private object _lock = new object();
        private ManualResetEventSlim _mre; // How about a FIFO queue instead of randomness?

        // an inaccurate view of resources
        public long EstimatedCount => Interlocked.Read(ref _resourceCount);

        public LocalConcurrencyLimiter(long resourceCount)
        {
            _resourceCount = resourceCount;
            _maxResourceCount = resourceCount;
            _mre = new ManualResetEventSlim();
        }

        // Fast synchronous attempt to acquire resources
        public bool TryAcquire(long requestedCount, out Resource resource)
        {
            resource = Resource.NoopResource;
            if (requestedCount < 0 || requestedCount > _maxResourceCount)
            {
                return false;
            }

            if (requestedCount == 0)
            {
                // TODO check if resources are exhausted
            }

            if (EstimatedCount > requestedCount)
            {
                lock (_lock) // Check lock check
                {
                    if (EstimatedCount > requestedCount)
                    {
                        Interlocked.Add(ref _resourceCount, -requestedCount);
                        resource = new Resource(null, resource => Release(requestedCount));
                        return true;
                    }
                }
            }

            return false;
        }

        // Wait until the requested resources are available
        public ValueTask<Resource> AcquireAsync(long requestedCount, CancellationToken cancellationToken = default)
        {
            if (requestedCount < 0 || requestedCount > _maxResourceCount)
            {
                throw new InvalidOperationException();
            }

            if (EstimatedCount > requestedCount)
            {
                lock (_lock) // Check lock check
                {
                    if (EstimatedCount > requestedCount)
                    {
                        Interlocked.Add(ref _resourceCount, -requestedCount);
                        return ValueTask.FromResult<Resource>(new Resource(null, resource => Release(requestedCount)));
                    }
                }
            }

            // Handle cancellation
            while (true)
            {
                _mre.Wait(cancellationToken); // Handle cancellation

                lock (_lock)
                {
                    if (_mre.IsSet)
                    {
                        _mre.Reset();
                    }

                    if (EstimatedCount > requestedCount)
                    {
                        Interlocked.Add(ref _resourceCount, -requestedCount);
                        return ValueTask.FromResult<Resource>(new Resource(null, resource => Release(requestedCount)));
                    }
                }
            }
        }

        private void Release(long releaseCount)
        {
            // Check for negative requestCount
            Interlocked.Add(ref _resourceCount, releaseCount);
            _mre.Set();
        }
    }

    // Represents one resource counted locally, ID is ignored
    internal class LocaRateLimiter : IResourceLimiter
    {
        private long _resourceCount;
        private readonly long _maxResourceCount;
        private readonly long _newResourcePerSecond;
        private Timer _renewTimer;
        private readonly ConcurrentQueue<RateLimitRequest> _queue = new ConcurrentQueue<RateLimitRequest>();

        public long EstimatedCount => Interlocked.Read(ref _resourceCount);

        public LocaRateLimiter(long resourceCount, long newResourcePerSecond)
        {
            _resourceCount = resourceCount;
            _maxResourceCount = resourceCount; // Another variable for max resource count?
            _newResourcePerSecond = newResourcePerSecond;

            // Start timer, yikes allocations from capturing
            _renewTimer = new Timer(Replenish, this, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        public bool TryAcquire(long requestedCount, out Resource resource)
        {
            resource = Resource.NoopResource;
            if (Interlocked.Add(ref _resourceCount, -requestedCount) >= 0)
            {
                return true;
            }

            Interlocked.Add(ref _resourceCount, requestedCount);
            return false;
        }

        public ValueTask<Resource> AcquireAsync(long requestedCount, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Add(ref _resourceCount, -requestedCount) >= 0)
            {
                throw new InvalidOperationException();
            }

            Interlocked.Add(ref _resourceCount, requestedCount);

            var registration = new RateLimitRequest(requestedCount);

            if (WaitHandle.WaitAny(new[] {registration.MRE.WaitHandle, cancellationToken.WaitHandle}) == 0)
            {
                return ValueTask.FromResult<Resource>(Resource.NoopResource);
            }

            cancellationToken.ThrowIfCancellationRequested();

            throw new InvalidOperationException();
        }

        private static void Replenish(object? state)
        {
            // Return if Replenish already running to avoid concurrency.
            var limiter = state as LocaRateLimiter;

            if (limiter == null)
            {
                return;
            }

            if (limiter._resourceCount < limiter._maxResourceCount)
            {
                var resourceToAdd = Math.Min(limiter._newResourcePerSecond, limiter._maxResourceCount - limiter._resourceCount);
                Interlocked.Add(ref limiter._resourceCount, resourceToAdd);
            }

            // Process queued requests
            var queue = limiter._queue;
            while(queue.TryPeek(out var request))
            {
                if (Interlocked.Add(ref limiter._resourceCount, -request.Count) >= 0)
                {
                    // Request can be fulfilled
                    queue.TryDequeue(out var requestToFulfill);

                    if (requestToFulfill == request)
                    {
                        // If requestToFulfill == request, the fulfillment is successful.
                        requestToFulfill.MRE.Set();
                    }
                    else
                    {
                        // If requestToFulfill != request, there was a concurrent Dequeue:
                        // 1. Reset the resource count.
                        // 2. Put requestToFulfill back in the queue (no longer FIFO) if not null
                        Interlocked.Add(ref limiter._resourceCount, request.Count);
                        if (requestToFulfill != null)
                        {
                            queue.Enqueue(requestToFulfill);
                        }
                    }
                }
                else
                {
                    // Request cannot be fulfilled
                    Interlocked.Add(ref limiter._resourceCount, request.Count);
                    break;
                }
            }
        }

        private class RateLimitRequest
        {
            public RateLimitRequest(long count)
            {
                Count = count;
                MRE = new ManualResetEventSlim();
            }

            public long Count { get; }

            public ManualResetEventSlim MRE { get; }
        }
    }

    // Represents multiple resource counted by redis, ID is used as key
    internal class InMemoryRateLimiter : IAggregatedResourceLimiter<string>
    {
        private readonly long _newResourcePerSecond;
        private IRateQuota<string> _store;

        public InMemoryRateLimiter(long newResourcePerSecond, IRateQuota<string> store)
        {
            _store = store;
            _newResourcePerSecond = newResourcePerSecond;
        }

        public long EstimatedCount(string resourceID) => _store.GetTally(resourceID);

        public bool TryAcquire(string resourceID, long requestedCount, out Resource resource)
        {
                resource = Resource.NoopResource;
            if (_store.IncrementTally(resourceID, requestedCount) <= _newResourcePerSecond)
            {
                return true;
            }

            return false;
        }

        public async ValueTask<Resource> AcquireAsync(string resourceID, long requestedCount, CancellationToken cancellationToken = default)
        {
            if (requestedCount > _newResourcePerSecond)
            {
                throw new InvalidOperationException();
            }

            while (true)
            {
                if (_store.IncrementTally(resourceID, requestedCount) <= _newResourcePerSecond)
                {
                    return Resource.NoopResource;
                }

                await Task.Delay(1000);
            }
        }
    }

    public interface IRateQuota<TKey>
    {
        // Read the resource count for a given resourceId
        long GetTally(TKey resourceId);

        // Update the resource count for a given resourceId
        long IncrementTally(TKey resourceId, long amount);
    }

    // Implementation could include local batching/caching/synchornization logic
    public class InMemoryRateLimitCountStore : IRateQuota<string>
    {
        private MemoryCache _localCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        private object _lock = new object();

        public long GetTally(string resourceId)
        {
            if (_localCache.TryGetValue(resourceId, out long count))
            {
                return count;
            }
            return 0;
        }

        public long IncrementTally(string resourceId, long amount)
        {
            lock (_lock)
            {
                var count = GetTally(resourceId);

                if (count == 0)
                {
                    return _localCache.Set(resourceId, 1, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(1), Size = 1 });
                }

                return _localCache.Set(resourceId, count + 1);
            }
        }
    }

    #endregion

    #region Rule engine APIs
    public interface IRateLimitPolicy
    {
        // Maybe a extension point for updating setting values?
    }

    public class ReadRateLimitPolicy : IRateLimitPolicy
    {
        public long ResourcePerSecond { get; } = 5;
    }

    public class DefaultRateLimitPolicy : IRateLimitPolicy
    {
        public long InitialResourceCount { get; } = 10;
        public long ResourcePerSecond { get; } = 5;
    }

    public interface IRateLimitPolicyRule
    {
        bool ApplyLimit(HttpContext context);

        ValueTask<Resource> ApplyLimitAsync(HttpContext context);
    }

    public interface IRateLimitPolicyRule<TContext>
    {
        bool ApplyLimit(TContext context);

        ValueTask<Resource> ApplyLimitAsync(TContext context);
    }

    public class ReadRateLimitByIPPolicyRule : IRateLimitPolicyRule
    {
        private readonly ReadRateLimitPolicy _policy = new ReadRateLimitPolicy();
        private readonly IAggregatedResourceLimiter<string> _limiter;

        public ReadRateLimitByIPPolicyRule(IRateQuota<string> store)
        {
            _limiter = new InMemoryRateLimiter(_policy.ResourcePerSecond, store);
        }

        public bool ApplyLimit(HttpContext context)
        {
            if (context.Request.Path != "/read")
            {
                return true;
            }

            return _limiter.TryAcquire($"{nameof(ReadRateLimitPolicy)}:{context.Connection.RemoteIpAddress?.ToString()}", out _);
        }

        public ValueTask<Resource> ApplyLimitAsync(HttpContext context)
        {
            if (context.Request.Path != "/read")
            {
                return ValueTask.FromResult<Resource>(Resource.NoopResource);
            }

            return _limiter.AcquireAsync($"{nameof(ReadRateLimitPolicy)}:{context.Connection.RemoteIpAddress?.ToString()}");
        }
    }

    public class DefaultRateLimitPolicyRule : IRateLimitPolicyRule
    {
        private readonly DefaultRateLimitPolicy _policy = new DefaultRateLimitPolicy();
        private readonly LocaRateLimiter _limiter;

        public DefaultRateLimitPolicyRule()
        {
            _limiter = new LocaRateLimiter(_policy.InitialResourceCount, _policy.ResourcePerSecond);
        }

        public bool ApplyLimit(HttpContext context)
        {
            return _limiter.TryAcquire(out _);
        }

        public ValueTask<Resource> ApplyLimitAsync(HttpContext context)
        {
            return _limiter.AcquireAsync();
        }
    }

    public interface IRateLimitManager
    {
        bool TryAcquireLimiters(HttpContext context); // Use another format for context
        ValueTask<bool> AcquireLimitersAsync(HttpContext context); // Use another format for context
    }

    public class RateLimitManager : IRateLimitManager
    {
        private IEnumerable<IRateLimitPolicyRule> _rules;

        public RateLimitManager(IEnumerable<IRateLimitPolicyRule> rules)
        {
            _rules = rules;
        }

        public bool TryAcquireLimiters(HttpContext context)
        {
            foreach (var rule in _rules)
            {
                if (!rule.ApplyLimit(context))
                {
                    return false;
                }
            }

            return true;
        }

        public async ValueTask<bool> AcquireLimitersAsync(HttpContext context)
        {
            foreach (var rule in _rules)
            {
                try {
                    await rule.ApplyLimitAsync(context);
                }
                catch {
                    return false;
                }
            }

            return true;
        }
    }

#endregion

    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            var localStore = new InMemoryRateLimitCountStore();
            services.AddSingleton<IRateLimitPolicyRule>(new ReadRateLimitByIPPolicyRule(localStore));
            services.AddSingleton<IRateLimitPolicyRule, DefaultRateLimitPolicyRule>();
            services.AddSingleton<IRateLimitManager, RateLimitManager>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            var rateLimiter = new LocaRateLimiter(5, 5);
            var concurrencyLimiter = new LocalConcurrencyLimiter(5);

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                // Specific endpoint, rate limit on / only
                endpoints.MapGet("/", async context =>
                {
                    // Direct usage
                    // No release since we know for sure it's a rate limiter
                    if (!rateLimiter.TryAcquire(out _))
                    {
                        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                        return;
                    }

                    if (concurrencyLimiter.TryAcquire(out var resource))
                    {
                        using (resource)
                        {
                            // process
                            await context.Response.WriteAsync("Hello World!");
                            return;
                            // resource released via dispose
                        }
                    }
                    else
                    {
                        // limit exceeded
                        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                        return;
                    }


                    // Via manager
                    var manager = context.RequestServices.GetRequiredService<RateLimitManager>();

                    if (!manager.TryAcquireLimiters(context))
                    {
                        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                        return;
                    }

                    // Via middleware
                    // This will be in a middleware
                    var rateLimitRules = context.GetEndpoint().Metadata.GetOrderedMetadata<IRateLimitPolicyRule>();

                    foreach (var rule in rateLimitRules)
                    {
                        if (!rule.ApplyLimit(context))
                        {
                            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                            return;
                        }
                    }

                    await context.Response.WriteAsync("Hello World!");
                }).EnforceRateLimit<ReadRateLimitByIPPolicyRule>()
                .EnforceRateLimit(new RateLimitInstance()); // Adds policy to metadata.


                // Wildcard endpoints, rate limit on /*
                endpoints.EnforceRateLimit<DefaultRateLimitPolicyRule>("/*"); // Adds policy to metadata.

                // Attributes on Controllers/Routes

                // Dynamic constraints (e.g. headers) should be examined in the middleware

                // Not ideal to recompute routes after config changes.
            });
        }

        public static void Channels() {
            // Option 1, separate rate limiter, not coupled
            var channel1 = Channel.CreateBounded<string>(5);
            var limiter1 = new LocaRateLimiter(resourceCount: 5, newResourcePerSecond: 5);

            if (limiter1.TryAcquire(out var resource)) {
                using (resource) {
                    channel1.Writer.TryWrite("New message");
                }
            }

            // Option 2, rate limiter on options, tightly coupled
            // This is the preferred option
            var limiter2 = new LocaRateLimiter(resourceCount: 5, newResourcePerSecond: 5);
            var channel2a = Channel.CreateBounded<string>(new BoundedChannelOptions(5) { WriteRateLimiter = limiter2 });
            var channel2b = Channel.CreateUnbounded<string>(new UnboundedChannelOptions() { WriteLimiter = limiter2 });

            channel2a.Writer.TryWrite("New message");
            channel2b.Writer.TryWrite("New message");

            // Option 3, rate limit on options
            // Internally Channel uses RateLimiter to measure rate
            var channel3 = Channel.CreateBounded<string>(new BoundedChannelOptions(5) { Rate = 2 });

            channel3.Writer.TryWrite("New message");
        }
    }
}
