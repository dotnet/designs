# Rate limits

**Owner** [John Luo](https://github.com/juntaoluo) | [Sourabh Shirhatti](https://github.com/shirhatti)

## Scenarios and User Experience

Excerpt from https://microsoft.sharepoint.com/:w:/r/teams/NETRateLimitingInterface/_layouts/15/Doc.aspx?sourcedoc=%7B557C73D8-F2A2-4799-82D0-6A5C13F58E4F%7D&file=Rate%20Limiting%20interface_CBB%20Perspective.docx&nav=eyJjIjo2MDMwNjMzMzZ9&action=default&mobileredirect=true&cid=fb8aa981-06d2-4c69-9bbd-ddfb278e2eb7

> Outages caused when system activities exceed the system’s capacity is a leading concern in system design. The ability to handle system activity efficiently, and graceful limit the rate of activities executing before the system is under stress is a fundamental principle in system resiliency. This applies not only to interactions between 2 or more disparate systems but also to the interactions occurring within all layers of an individual system instance. .NET does not have a standardized means for expressing and managing limiting logic needed to produce a resilient system. This adds complexity to designing and developing resilient software in .NET by introducing an easy vector for competing rate limiting logic and anti-patterns. A standardized interface in NET for limiting activities will make it easier for developers to build resilient systems for all scales of deployment and workload. This document outlines a standard rate limiting interface with the goal of adding it to .NET.

### Use cases for self-replenishing, aka **Time based Limits**

A commonly used strategy to throttle operations is to specify a time-based rate limit. An example of this would be limiting a service to serve X requests per second and rejecting any new requests when the limit is reached.

The benefit of a conceptually simple limit is that they can provide protection with minimal overhead. It is also simple to use since the limiter updates itself when additional permit is available after a specific period of time has elapsed without any input from the user. For example, the limiter itself will reset the number of requests allowed to be processed after each second.

This simple implementation also lends itself to rate limiting among different services or machines. A use case that was requested was querying a Redis store to determine whether a rate limit is satisfied or exceeded.

### Use cases for non self-replenishing, aka **Concurrency based Limits**

A different strategy to throttle operations is to specify a concurrency-based limit that describes underlying physical resources. An example would be to specify only X number of requests can be processed at a time based where X may be calculated based on the number of cores.

These types of limits may enable more efficient use of underlying resources. For example, to ensure that ASP.NET Core stops accepting requests that would otherwise lead to thread-pool starvation, a concurrency limit can be specified. This is better than a rate limit since it can be possible to process a higher number of short-running requests as long as the concurrency limit is not exceeded at any particular time.

However, these limits may be more complex and require that the users to explicitly release the permits obtained upon completion of their operation. Given the trade-off between consistency and speed, it is also likely to be poorly suited to limiting resource usage among different services or machines.

### Unified abstraction

We want to use a single abstraction to represent both types of limits. This is because for many applications, there's a desire to represent both limits in a transparent way. For example, in ASP.NET Core we want to allow users to register a set of limits before processing a specific request and only proceed if all limits are satisfied. These limits may be rate or concurrency based or even based on some ambient system state. In order to represent all of these scenarios in a way that can be easily queried, a unified API seems more appropriate, rather than two separate APIs for self replenishing  and non self-replenishing rate limits and have applications track these rate limiters independently.

### Composition with BCL types

There are two main reasons to introduce this API in the BCL.

#### Shared abstractions and implementation for the entire stack

By introducing the new API in the BCL, we maximize the ability to share rate limiter across BCL types in additional to usage in ASP.NET Core and user code. For example, rate limiters can be used to limit traffic in BCL types, ASP.NET Core middlewares and service implementations.

#### Integration with BCL types for ease of usage

There are various ways to compose usage of the proposed rate limiting APIs and BCL types. For example, there is a trade-off between coupling and ease of usage. Consider the options:

```c#
// No coupling
var channel = Channel.CreateBounded<string>(5);
var limiter = new SampleRateLimiter(maxPermits: 5, newPermitPerSecond: 5);

if (limiter.Acquire().IsAcquired) {
    channel.Writer.TryWrite("New message");
}

// Coupling
var limiter = new SampleRateLimiter(maxPermits: 5, newPermitPerSecond: 5);
// This creates a LimitedChannel<T>. Its operations are limited by the limiter specified,
// which may be rate/concurrency based. LimitedChannel<T> with a concurrency limiter would
// be similar to a BoundedChannel if the limiter's queue is disabled, see design section
// for details on limiter's queue.
var channel = Channel.CreateLimited<string>(limiter);

channel.Writer.TryWrite("New message");
```

The benefit of tighter coupling with the BCL types is that it allows for simpler usage. In the former case, the user need to explicitly check the limiter every time an operation to the channel writer is requested. In the latter case, the channel writer can be used in the same manner as today and the channel writer itself will perform the check on the rate limiter on behalf of the user.

### Handling bursts

Another common concern that rate limiters need to handle are traffic bursts. A method for handling these scenarios is to implement a queuing strategy for the limiters which will register incoming requests and complete the requests when permits become available. Note, that this feature may or may not be supported depending on the rate limiter implementation. Conceptually, the rate limiter can operate in 3 states, see [default limiter implementations](#default-limiter-implementations) on how this is achieved.

1. Normal operation (rate limit not exceeded): The rate limiter will allow the operation to continue as long as the rate limit is not exceeded.
2. Soft cap exceeded (rate limit exceeded, queue limit not exceeded): The rate limiter can queue additional requests which will be allowed to proceed when permits become available.
3. Hard cap exceeded (rate limit exceeded and queue limit exceeded): The rate limiter will reject any additional requests.

With the queueing strategy, the rate limiter can also be used to shape traffic and smooth out bursts. For example, if a rate limiter with a limit of 5 requests/second and a queue of 25 can handle a burst of 30 requests which will be converted to a sequence of 5 requests/second over 6 seconds.

Note that the hard cap is useful in guaranteeing that queuing is not unbounded. In the case where a system is overwhelmed it is better to immediately reject requests and try again at a later time instead of potentially waiting in the queue without a realistic chance of being allowed to process in a reasonable amount of time.

### Goals

These are the goals we would like to achieve in .NET 6.0. The rate limit abstractions and implementations are planned to target `netstandard2.0` and be shipped in OOB preview packages from AspLabs (i.e. not included in Microsoft.NETCore.App).

#### Rate limit abstractions

Users will interact with this component in order to obtain decisions for rate or concurrency limits. This abstraction require explicit release sematics to accommodate non self-replenishing  limiter. This component encompasses the Acquire/WaitAsync mechanics (i.e. check vs wait behaviours) and default implementations will be provided for select accounting method (fixed window, sliding window, token bucket, concurrency). The return type is a `RateLimitLease` which indicates whether the acquisition is successful and manages the lifecycle of the aquired permits.

#### Rate limit implementations

In addition to defining the abstractions, we would also like to ship some default implementations of common types of rate limiters. Details on how the functionality described here are achieved can be found in the [implementation section](#concrete-implementations).

1. Fixed window rate limiter

This is the simplest type of window based rate limiter. It counts the number of permits acquired in a time span. If the permit limit is reached, additional calls to `Acquire` will return a `RateLimitLease` with a `RateLimitLease.IsAcquired = false` and additional calls to `WaitAsync` will wait if queuing is enabled until the queue limit. Permits are replenished when the current window has elapsed.

1. Sliding window rate limiter

This is another type of window based rate limiter. Counts for the number of permits acquired is maintained for time segments. A window is defined as multiple time segments and the rate limit is considered to be exceeded if the total across the segments in the window is exceeded. If the rate limit is exceeded, additional calls to `Acquire` will return a `RateLimitLease` with a `RateLimitLease.IsAcquired = false` and additional calls to `WaitAsync` will wait if queuing is enabled until the queue limit. The window is updated when the current time segment has elapsed.

As an example consider the following example:

```
Settings: window time span = 3s, number of segments per window = 3, rate limit = 10

Time:   |1s |2s |3s |4s |
Count:  [3] [4] [3] [1]
        | Window 1  |
            | Window 2  |
```

In the above example, permits are exhaused in Window 1 since the total count is 10 and no additional permits can be acquired in the 3rd second. On the 4th second, Window 2 now has a count 8 and can accept additional permit acquisition requests.

1. Token bucket rate limiter

This rate limiter uses the [token bucket algorithm](https://en.wikipedia.org/wiki/Token_bucket). The functionality will be similar to the two implementation described above but will be configured differently.

See a [proof of concept implementation](#token-bucket-limiter-with-queue-poc).

4. Concurrency limiter

This limiter keeps track of the number of concurrent permits in use. Conceptually, it will function in a similar way to a Semaphore/SemaphoreSlim but adapted to the proposed API. Like the rate limits above, it will also keep a queue of pending acquisition requests if configured to do so.

See a [proof of concept implementation](#concurrency-limiter-with-random-queue-poc).

#### Adoption in ASP.NET Core and others

We are committed to using these primitives an ASP.NET Core to ship a request limit middleware. We also plan to improve our existing server limits in Kestrel HTTP Server in the future (post .NET 6). We will likely also ship an extensions package that provides support for a redis based rate limiter. Finally, there is likely to be further integration of these types in YARP and there has been interest in these primitives from Azure teams such as ATS and ACR.

### Non-Goals

#### Rate limit adoption in the BCL

Two BCL types where adoption of the rate limiters are beneficial and relatively simple are:

- System.Threading.Channels
- System.IO.Pipelines

Prototypes are illustrated in the [proof of concepts](#dotnetruntime-pocs) but are not commited to for .NET 6.

#### HttpClient, Streams, Sockets

Although we anticipate that there will be applications in many .NET areas and components such as HttpClient, Streams and Sockets, these scenarios will be explored but not committed for adoption in .NET 6 since they may be more complex. The adoption of the rate limiting APIs in these areas can happen independently of this proposal in the future.

#### Partial Acquisition and Release

Current design does not allow for partial acquisition or release of permits. In other words, either all of the permits requested are acquired or nothing is acquired. Likewise, when the operation is complete, all permits acquired via the limiter are released together. Though there are theoretically potential use cases for partial acquisitions and releases, no concrete examples has been identified in ASP.NET Core or .NET. In case these functionalities are needed, additional APIs can be added indepently from the APIs proposed here.

For example, this will likely involve a modified `RateLimitLease` (expand details for API)

<details>

```c#
public abstract class RateLimitLease : IDisposable
{
    // This represents whether permit lease acquisition was successful
    public abstract bool IsAcquired { get; }

    // A metadata for permit count needs to be added to report the number of permits held by this lease
    // Extension methods to convert value of well known metadata to specific types.
    public abstract bool TryGetMetadata(MetadataName metadataName, [NotNullWhen(true)] out object? metadata);

    // Release a specific amount of permits, if possible
    public void Release(int releaseCount);

    // Release remaining permits
    public abstract void Dispose();
}
```

</details>


#### Aggregated limiters

A separate abstraction different from the one proposed here is required to support scenarios where aggregated limiter are needed.

For high cardinality limiters such as rate limit by IP, we don't want to have a rate limit per bucket (i.e. one rate limiter per IP Address). As such, we need an API where you can pass in a id. This is in contrast to simpler limiters where a key is not necessary, such as a n requests/second limit, where requiring a default key to be passed in becomes awkward. Hence the simpler API proposed here is preferred.

However, we have not yet found any use cases for aggregated limiters in dotnet/runtime and hence it's not part of this proposal. If we eventually deem that such aggregated limiters are needed in the BCL as well, it can be added independently from the APIs proposed here.

Sample API:

```c#
// Represent an aggregated rate limit (e.g. a rate limiter aggregated by IP)
public abstract class AggregatedRateLimiter<TKey>
{
    // Estimated available permits
    public abstract int GetAvailablePermits(TKey id);

    // Fast synchronous attempt to acquire permits
    public abstract RateLimitLease Acquire(TKey id, int permitCount);

    // Wait until the requested permits are available
    public abstract ValueTask<RateLimitLease> WaitAsync(TKey id, int permitCount, CancellationToken cancellationToken = default);
}
```

See a [proof of concept implementation](#ip-address-aggregated-resource-limiter-using-a-local-concurrentdictionary-as-a-storage-mechanism).

## Stakeholders and Reviewers

- Owned by ASP.NET Core
- Code will eventually be a new area in dotnet/runtime
  - Tentatively System.Threading.RateLimiting
- Reviewers
  - David Fowler, Eric Erhardt, Stephen Halter

## Design

### Abstractions

The main abstraction API will consist of the `RateLimiter` and the associated `RateLimitLease`. The main responsibility of the `RateLimiter` is to allow the user to determine whether it's possible to proceed with an operation. The `RateLimitLease` struct is returned by the `RateLimiter` to represent if the acquisition was successful and the permit lease that were obtained, if successful.

```c#
namespace System.Threading.RateLimiting
{
  public abstract class RateLimiter
  {
    // An estimated count of available permits. Potential uses include diagnostics.
    public abstract int GetAvailablePermits();

    // Fast synchronous attempt to acquire permits
    // Set permitCount to 0 to get whether permits are exhausted
    public RateLimitLease Acquire(int permitCount = 1);

    // Implementation
    protected abstract RateLimitLease AcquireCore(int permitCount);

    // Wait until the requested permits are available or permits can no longer be acquired
    // Set permitCount to 0 to wait until permits are replenished
    public ValueTask<RateLimitLease> WaitAsync(int permitCount = 1, CancellationToken cancellationToken = default);

    // Implementation
    protected abstract ValueTask<RateLimitLease> WaitAsyncCore(int permitCount, CancellationToken cancellationToken = default);
  }

  public abstract class RateLimitLease : IDisposable
  {
    // This represents whether lease acquisition was successful
    public abstract bool IsAcquired { get; }

    // Method to extract any general metadata. This is implemented by subclasses
    // to return the metadata they support.
    public abstract bool TryGetMetadata(string metadataName, out object? metadata);

    // This casts the metadata returned by the general method above to known types of values.
    public bool TryGetMetadata<T>(MetadataName<T> metadataName, [MaybeNullWhen(false)] out T metadata);

    // Used to get a list of metadata that is available on the lease which can be dictionary keys or static list of strings.
    // Useful for debugging purposes but TryGetMetadata should be used instead in product code.
    public abstract IEnumerable<string> MetadataNames { get; }

    // Virtual method that extracts all the metadata using the list of metadata names and TryGetMetadata().
    public virtual IEnumerable<KeyValuePair<string, object?>> GetAllMetadata();

    // Follow the general .NET pattern for dispose
    public void Dispose() { Dispose(true); GC.SuppressFinalize(this); }
    protected virtual void Dispose(bool disposing);
  }

  // Curated set of known MetadataName<T>
  public static class MetadataName : IEquatable<MetadataName>
  {
    public static MetadataName<TimeSpan> RetryAfter { get; } = Create<TimeSpan>("RETRY_AFTER");
    public static MetadataName<string> ReasonPhrase { get; } = Create<string>("REASON_PHRASE");

    public static MetadataName<T> Create<T>(string name) => new MetadataName<T>(name);
  }

  // Wrapper of string and a type parameter signifying the type of the metadata value
  public sealed class MetadataName<T> : IEquatable<MetadataName<T>>
  {
    public MetadataName(string name);
    public string Name { get; }
  }
}
```

The `Acquire` call represents a fast synchronous check that immediately returns whether there are enough permits available to continue with the operation and atomically acquires them if there are, returning `RateLimitLease` with the value `RateLimitLease.IsAcquired` representing whether the acquisition is successful and the lease itself representing the acquired permits, if successful. The user can pass in a `permitCount` of 0 to check whether the permit limit has been reached without acquiring any permits. The default value for the `permitCount` is set to 1 since that's a common use scenario where limiters are used to impose a limit the number of requests/operations.

`WaitAsync`, on the other hand, represents an awaitable request to check whether permits are available. If permits are available, obtain the permits and return immediately with a `RateLimitLease` representing the acquired permits. If the permits are not available, the caller is willing to pause the operation and wait until the necesasary permits become available. The user can also pass in a `requestCount` of 0 to signal that the caller wants to check whether the rate limit has been reached and if the limit is reached, wait until more permits become available. Again, the default value for the `permitCount` is set to 1 since that's a common use scenario where limiters are used to impose a limit the number of requests/operations. If the queue limit is reached, additional call to `WaitAsync` will result in `RateLimitLease.IsAcquired = false` being returned.

`Acquire` and `WaitAsync` also follow the template method pattern and will invoke `AcquireCore` and `WaitAsyncCore` after validating parameters. The implementations will override `RateLimitLease AcquireCore(int permitCount)` and `ValueTask<RateLimitLease> WaitAsyncCore(int permitCount, CancellationToken cancellationToken = default)` to provide the actual limiter functionality.

In terms of design, these APIs are similar to System.Threading.Channels.ChannelWriter, for example:
- `RateLimiter.Acquire(...)` vs `ChannelWriter.TryWrite(...)`
- `RateLimiter.WaitAsync(0, ...)` vs `ChannelWriter.WaitToWriteAsync(...)`
- `RateLimiter.WaitAsync(N, ...)` vs `ChannelWriter.WriteAsync(...)`

Note that the `WaitAsync` call is not opinionated on how to order the incoming acquisition requests. This is up to the implementation and will be further elaborated in the [implementation section](#concrete-implementations).

`GetAvailablePermits()` is envisioned as a flexible and simple way for the limiter to communicate the status of the limiter to the user. This count is similar in essence to `SemaphoreSlim.CurrentCount`. This count can also be used in diagnostics to track the usage of the rate limiter. Finally this count can be used in a best effort scenario where the user tries to check that a certain amount of permits are availble before trying to acquire those permits, though no atomicity is guaranteed between checking this count and calling `Acquire` or `WaitAsync`.

The abstract class `RateLimitLease` is used to facilitate the release semantics of rate limiters. That is, for non self-replenishing, the returning of the permits obtained via Acquire/WaitAsync is achieved by disposing the `RateLimitLease`. This enables the ability to ensure that the user can't release more permits than was obtained.

The `RateLimitLease.IsAcquired` property is used to express whether the acquisition request was successful. `bool TryGetMetadata(string metadataName, out object? metadata)` is implemented by subclasses to allow for returning additional metadata as part of the rate limit decision. A curated list of well know names for commonly used metadata is provided via `MetadataName` which keeps a list of `MetadataName<T>`s which are wrappers of `string` and a type parameter indicating the value type. `bool TryGetMetadata<T>(MetadataName<T> metadataName, [MaybeNullWhen(false)] out T metadata)` casts the `object? metadata` to the type that's indicated by the `T` of `MetadataName<T>`. The `MetadataNames` property is used as a way to query for all the metadata names available on the lease and the `GetAllMetadata()` method is used to get all the metadata key value pairs. To optimize performance, implementations will need to pool `RateLimitLease`.

For a sample limiter implemenation that uses state, see this [proof of concept](#rate-limiter-wrapper-with-reason-phrases). For alternative designs, see [alternative designs](#state-field-on-ratelimitlease).

#### Common usage patterns

In an ASP.NET Core endpoint, usage of specific `RateLimiter`s will look like the following. Note that in these cases, the rate limiter is not known to be a rate or concurrency limiter and therefore the release mechanics must be followed.

```c#
RateLimiter limiter = new SomeRateLimiter(options => ...)

// Synchronous checks
endpoints.MapGet("/acquire", async context =>
{
    // Check limiter using `Acquire` that should complete immediately
    using var lease = limiter.Acquire();
    // RateLimitLease was successfully obtained, the using block ensures
    // that the lease is released upon processing completion.
    if (lease.IsAcquired)
    {
        await context.Response.WriteAsync("Hello World!");
    }
    else
    {
        // Permit lease acquisition failed, send 429 response
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        return;
    }
}

// Async checks
endpoints.MapGet("/waitAsync", async context =>
{
    // Check limiter using `WaitAsync` which may complete immediately
    // or wait until permits are available. Using block ensures that
    // the lease is released upon processing completion.
    using var lease = await limiter.WaitAsync();
    if (lease.IsAcquired)
    {
        await context.Response.WriteAsync("Hello World!");
    }
    else
    {
        // Permit lease acquisition failed, send 429 response
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        return;
    }
}
```

For additional usage samples, checkout the [proof of concepts](#proof-of-concepts).

### Concrete implementations

#### Default limiter implementations

Additional public APIs are needed to configure specific limiter implementations listed in [the goals](#rate-limit-implementations). The options for configuring these implementations will be described here without going into implementation details.

```c#
namespace System.Threading.RateLimiting
{
    // Limiter implementations
    public sealed class FixedWindowRateLimiter : RateLimiter { }
    public sealed class SlidingWindowRateLimiter : RateLimiter { }
    public sealed class TokenBucketRateLimiter : RateLimiter { }
    public sealed class ConcurrencyLimiter : RateLimiter { }

    // This specifies the behaviour of `WaitAsync` When PermitLimit has been reached
    public enum PermitsExhaustedMode
    {
        EnqueueIncomingRequest,
        PushIncomingRequest
    }

    // Limiter options common to default runtime implementations
    internal abstract class RateLimiterOptions
    {
        // Specifies the maximum number of permits for the limiter
        public int PermitLimit { get; set; }
        // Permits exhausted mode, configures `WaitAsync` behaviour
        public PermitsExhaustedMode PermitsExhaustedMode { get; set; }
        // Queue limit when queuing is enabled
        public int QueueLimit { get; set; }
    }

    // Window based rate limiter options
    public class FixedWindowRateLimiterOptions : RateLimiterOptions
    {
        // Specifies the duration of the window where the PermitLimit is applied
        public TimeSpan Window { get; set; }
    }

    public class SlidingWindowRateLimiterOptions : FixedWindowRateLimiterOptions
    {
        // Specifies the number of segments the Window should be divided into
        public int SegmentsPerWindow { get; set; }
    }

    public class TokenBucketRateLimiterOptions : RateLimiterOptions
    {
        // Specifies the period between replenishments
        public TimeSpan ReplenishmentPeriod { get; set; }
        // Specifies how many tokens to restore each replenishment
        public int TokensPerPeriod { get; set; }
    }

    public class ConcurrencyLimiterOptions : RateLimiterOptions { }
}
```

The implementation of the four limiters are omitted here since there's no additional public API other than the ones defined by `RateLimiter`.

While proposed API above illustrates the hierarchy of the limiter options the public option fields will be discussed individually here in order they appear.

1. `FixedWindowRateLimiterOptions`

- `PermitLimit`

This limit is common to all limiters proposed here. It represents a maximum number of permits that can be acquired. However, the exact definition will be different between each limiter. For example, for `FixedWindowRateLimiterOptions` and `SlidingWindowRateLimiterOptions` this will mean the maximum number of permits or requests allowed in a given window. For `TokenBucketRateLimiterOptions` this would mean the maximum number of tokens that the bucket can hold. For `ConcurrencyLimiterOptions` this would mean the maximum number of concurrent permits that can be used at any time.

- `PermitsExhaustedMode`

This enum is used to configure the behaviour of `WaitAsync` to also allow for waiting for acquisition requests when `PermitLimit` is reached. There are currently 2 identified behaviour, queue in FIFO order (`EnqueueIncomingRequest`), queue in LIFO order (`PushIncomingRequest`). `PermitsExhaustedMode` value and `QueueLimit` is used together to define `WaitAsync` queuing behaviour.

- `QueueLimit`

When queuing is enabled, the behaviour of `WaitAsync` is controlled by this value and `PermitsExhaustedMode`. There are 3 possible states the limiter can be in. Note "permit count" here represents the sum of 1. outstanding permits (i.e. permits that have been already acquired) + 2. queued count (i.e. sum of all requested counts currently queued) 3. requested permits (i.e. value passed in via `permitCount`).

1. permit count <= `PermitLimit`: `WaitAsync` succeeds and will return a `RateLimitLease` representing the acquired permits immediately
2. `PermitLimit` < permit count <= `PermitLimit`+ `QueueLimit`: `WaitAsync` will enqueue or push the pending request until more permits become available.
3. permit count > `PermitLimit`+ `QueueLimit`: `WaitAsync` will return `RateLimitLease.IsAcquired = false` immediately if `EnqueueIncomingRequest` is configured. If `PushIncomingRequest` is configured, the oldest registration in the stack is removed and finished with `RateLimitLease.IsAcquired = false`.

Side note:

There are technically more queue behaviours that are possible which can be computed as the cross product of {`Queue`, `Stack`} X {`FailOldest`, `FailNewest`, `FailIncoming`}. However, it is unclear whether all combinations are useful and the two most sensible options are encoded in the `PermitsExhaustedMode.EnqueueIncomingRequest` and `PermitsExhaustedMode.PushIncomingRequest` enums.

- `Window`

This represents the time span where the `PermitLimit` is to be applied.

1. `SlidingWindowRateLimiterOptions`

- `PermitLimit` - see `FixedWindowRateLimiterOptions`
- `PermitsExhaustedMode` - see `FixedWindowRateLimiterOptions`
- `QueueLimit` - see `FixedWindowRateLimiterOptions`
- `TickEventHandler` - see `FixedWindowRateLimiterOptions`
- `Window` - see `FixedWindowRateLimiterOptions`
- `SegmentsPerWindow`

This represents how many segments each window is to be divided into.

1. `TokenBucketRateLimiterOptions`

- `PermitLimit` - see `FixedWindowRateLimiterOptions`
- `PermitsExhaustedMode` - see `FixedWindowRateLimiterOptions`
- `QueueLimit` - see `FixedWindowRateLimiterOptions`
- `TickEventHandler` - see `FixedWindowRateLimiterOptions`
- `ReplenishmentPeriod`

This specifies how often replenishment occurs.

- `TokensPerPeriod`

This specifies how many tokens are restored each time replenishment occurs.

4. `ConcurrencyLimiterOptions`

- `PermitLimit` - see `FixedWindowRateLimiterOptions`
- `PermitsExhaustedMode` - see `FixedWindowRateLimiterOptions`
- `QueueLimit` - see `FixedWindowRateLimiterOptions`

#### Channels

Instead of modifying the existing `BoundedChannel` and `UnboundedChannel`, the proposal here is to create a new `RateLimitedChannel` along with a `RateLimitedChannelOptions`.

```c#
namespace System.Threading.Channels
{
    public sealed class RateLimitedChannelOptions : ChannelOptions
    {
        public RateLimiter WriteLimiter { get; set; }
    }

    internal sealed class RateLimitedChannel<T> : Channel<T> { }

    public static class Channel
    {
        public static Channel<T> CreateRateLimited<T>(LimitedChannelOptions options)
    }
}
```

The `RateLimitedChannel` represents a superset of the functionality of `BoundedChannel` allowing for configuration with both concurrency and rate limits. As a general overview, the `WriteLimiter` is queried on `TryWrite`, `WriteAsync` and `WaitToWriteAsync`. When a written item is enqueued to `_items`, a corresponding `RateLimitLease` is also added. Upon dequeue from `_items` from the `ChannelReader` or upon unblocking a reader, `RateLimitLease.Dispose` is invoked to release permits back to the write limiter. NOte that the `RateLimitedChannel` will only limit the producer side, i.e. writes. For details on proof of concept for the implementation, see [dotnet/runtime PoCs](#dotnetruntime-pocs).

#### Pipelines

TBD

## Proof-of-concepts

### Customized rate limiter based on ambient state that won't ship in box

e.g. The limiter fails when CPU usage is above 90%

```c#
class CPUUsageLimiter : RateLimiter
{
    private PerformanceCounter _cpuUsageCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
    private float _threshold;

    public CPUUsageLimiter(float threshold)
    {
        _threshold = threshold;
    }

    public override int GetAvailablePermits() => (int)_cpuUsageCounter.NextValue();

    protected override RateLimitLease AcquireCore(int permitCount)
    {
        if (cpuCounter.NextValue() > _threshold)
        {
            return CPULease.Fail;
        }
        return CPULease.Success;
    }

    protected override async ValueTask<RateLimitLease> WaitAsyncCore(int permitCount, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (cpuCounter.NextValue() <= _threshold)
            {
                return CPULease.Success;
            }
            await Task.Delay(1000); // Wait 1s before rechecking.
        }
    }

    private class CPULease : RateLimitLease
    {
        public static readonly RateLimitLease Success = new CPULease(true);
        public static readonly RateLimitLease Fail = new CPULease(false);

        public CPULease(bool isAcquired)
        {
            IsAcquired = isAcquired;
        }

        public override bool IsAcquired { get; }

        public override IEnumerable<string> MetadataNames => Enumerable.Empty<string>();

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = null;
            return false;
        }

        protected override void Dispose(bool disposing) { }
    }
}
```

### Rate limiter wrapper with reason phrases

```c#
class RateLimiterWrapperReasonPhrase : RateLimiter
{
    private string _failureReasonPhrase;
    private RateLimiter _innerLimiter;

    public RateLimiterWrapperReasonPhrase(string reasonPhrase, int requestPerSecond)
    {
        _failureReasonPhrase = reasonPhrase;
        _innerLimiter = new TokenBucketRateLimiter(requestPerSecond: requestPerSecond); // wrapping to reduce verbosity in this sample.
    }

    public override int GetAvailablePermits() => _innerLimiter.GetAvailablePermits();

    protected override RateLimitLease AcquireCore(int permitCount)
    {
        var innerLease = _innerLimiter.Acquire(permitCount);
        if (innerLease.IsAcquired)
        {
            return innerLease;
        }

        return new WrappedFailedLimitLease(_failureReasonPhrase, innerLease);
    }

    protected override async ValueTask<RateLimitLease> WaitAsyncCore(int permitCount, CancellationToken cancellationToken = default)
    {
        var innerLease = await _innerLimiter.WaitAsync(permitCount, cancellationToken);

        if (innerLease.IsAcquired)
        {
            return innerLease;
        }

        return new WrappedFailedLimitLease(_failureReasonPhrase, innerLease);
    }

    private class WrappedFailedLimitLease : RateLimitLease
    {
        private readonly string _reasonPhrase;
        private readonly RateLimitLease _innerLease;

        public WrappedFailedLimitLease(string reasonPhrase, RateLimitLease innerLease)
        {
            _reasonPhrase = reasonPhrase;
            _innerLease = innerLease;
        }

        public override bool IsAcquired { get; }

        public override IEnumerable<string> MetadataNames => throw new NotImplementedException();

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (metadataName == "reasonPhrase")
            {
                metadata = _reasonPhrase;
                return true;
            }

            return _innerLease.TryGetMetadata(metadataName, out metadata);
        }

        protected override void Dispose(bool disposing)
        {
            _innerLease.Dispose();
        }
    }
}

// Usage scenario

// API Controller:
[ApiController]
[RequestLimit(new RateLimiterWrapperReasonPhrase(reasonPhrase: "Too many booking requests", requestPerSecond: 100))]
public class BookingController : ControllerBase
{
    [RequestLimit(new RateLimiterWrapperReasonPhrase(reasonPhrase: "Too many hotel booking requests", requestPerSecond: 5))]
    public IActionResult BookHotel()
    {
        ...
    }

    [RequestLimit(new RateLimiterWrapperReasonPhrase(reasonPhrase: "Too many car rental booking requests", requestPerSecond: 5))]
    public IActionResult BookCarRental()
    {
        ...
    }
}

// Middleware that handles the limiters
public async Task Invoke(HttpContext context)
{
    var endpoint = context.GetEndpoint();
    var limiters = endpoint?.Metadata.GetOrderedMetadata<RateLimiter>();

    var leases = new Stack<RateLimitLease>();
    try
    {
        foreach (var limiter in limiters)
        {
            var lease = limiter.Acquire();
            if (!lease.IsAcquired)
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                if (lease.TryGetMetadata(new MetadataName("reasonPhrase", out var reasonPhrase)))
                {
                    await context.Response.WriteAsync(reasonPhrase as string);
                }
                return;
            }
            leases.Push(lease);
        }

        await _next.Invoke(context);
    }
    finally
    {
        while (leases.TryPop(out var lease))
        {
            lease.Dispose();
        }
    };
}
```

### Token bucket limiter with queue PoC

Proof of concept at: https://github.com/dotnet/aspnetcore/blob/41d5bb475a89b91bf097aefd232258dee4c8839b/src/Middleware/RequestLimiter/src/RateLimiter.cs

### Concurrency limiter with queue PoC

Proof of concept at: https://github.com/dotnet/aspnetcore/blob/52e0b9cb7e44109fecef90bbdaa2bd5193ebc6e7/src/ResourceLimits/src/ConcurrencyLimiter.cs

### IP Address aggregated rate limiter using a local ConcurrentDictionary as a storage mechanism

Proof of concept at: https://github.com/dotnet/aspnetcore/blob/9d02c467d85c54675fa71735f7c3f7f01637557f/src/Middleware/RequestLimiter/src/IPAggregatedRateLimiter.cs

Usage pattern:

```c#
var limiter = new IPAggregatedRateLimiter(permitLimit: 5, newPermitsPerSecond: 5);
endpoints.MapGet("/acquire", async context =>
{
    // Check limiter using `Acquire` that should complete immediately
    using var lease = limiter.Acquire(context, 1);
    if (lease.IsAcquired)
    {
        // RateLimitLease was successfully obtained, the using block ensures
        // that the lease is released upon processing completion.
        await context.Response.WriteAsync("Hello World!");
    }
    else
    {
        // RateLimitLease acquisition failed, send 429 response
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        return;
    }
}
```

### dotnet/runtime PoCs

- https://github.com/JunTaoLuo/runtime/compare/resource-limit...dotnet:resource-limit

This branch contains sample implementations of `RateLimiter` APIs described in this proposal in dotnet/runtime and application in `System.Threading.Channels`

Usage Highlights:

```c#
var limitedChannel = Channel.CreateRateLimited<string>(new RateLimitedChannelOptions { WriteLimiter = RateLimiter(500 /*bytes per second*/ ) });
```

### dotnet/aspnetcore PoCs

- https://github.com/dotnet/aspnetcore/compare/johluo/request-limit-middleware

This branch contains sample usages of the `RateLimiter` in ASP.NET Core in a request limiter middleware.

Usage Highlights:

```c#
// Controller
[RateLimit(requestPerSecond: 10)]
public class HomeController : Controller
{
    public IActionResult Index()
    {
        return View();
    }
}

// This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILogger<Startup> logger)
{
    // This middleware handles the enforcement of limiters
    app.UseRateLimiter();

    app.UseEndpoints(endpoints =>
    {
        endpoints.MapGet("/instance", async context =>
        {
            await Task.Delay(5000);
            await context.Response.WriteAsync("Hello World!");
        }).EnforceRateLimit(new RateLimiter(2, 2));

        endpoints.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}");
    }
}
```

## Alternative designs

### Separate abstractions for rate and concurrency limits

A design where time based limits and concurrency limits were expressed by separate abstractions was considered. The design more clearly express the intended use pattern where time based limits do not need to return a `RateLimitLease` and does not possess release semantics. Contrasted with the proposed design, the release semantics for rate limits will no-op.

However, this design has the drawback for consumers of limiters since there are two possible limiter types that can be specified by the user. To alleviate some of the complexity, a wrapper for time based limits was considered. However, the complexity of this design was deemed undesirable and a unified abstraction for time based and concurrency limits was preferred.


### A struct instead of class for RateLimitLease

This approach was considered since allocating a new `RateLimitLease` for each acquisition request is considered to be a performance bottleneck. The design evolved to the following:

```c#
// Represents a permit lease obtained from the limiter. The user disposes this type to release the acquired permits.
public struct RateLimitLease : IDisposable
{
  // This represents whether permit acquisition was successful
  public bool IsAcquired { get; }
  // This represents the count of permits obtained in the lease
  public int count { get; }
  // This represents additional metadata that can be returned as part of a call to Acquire/AcquireAsync
  // Potential uses could include a RetryAfter value or an error code.
  public object? State { get; }
  // Private fields to be used by `onDispose()`, this is not a public API shown here for completeness
  private RateLmiter? _rateLimiter;
  private Action<RateLimiter?, int, object?>? _onDispose;
  // Constructor which sets all the readonly values
  public RateLimitLease(
    bool isAcquired,
    int count,
    object? state,
    RateLimiter? rateLimiter,
    Action<RateLimiter?, int, object?>? onDispose);
  // Return the acquired permits, calls onDispose with RateLimiter
  // This can only be called once, it's an user error if called more than once
  public void Dispose();
}
```

However, this design became problematic with the consideration of including a `AggregatedRateLimiter<TKey>` which necessitates the existence of another struct `RateLimitLease<TKey>` with a private reference to the `AggregatedRateLimiter<TKey>`. This bifurcation of the return types of `Acquire` and `WaitAsync` between the `AggregatedRateLimiter<TKey>` and `RateLimiter` make it very difficult to consume aggregated and simple limiters in a consistent manner. Additional complexity in definiting an API to store and retrieve additional metadata is also a concern, see below. For this reason, it is better to make `RateLimitLease` a class instead of a struct and require implementations to pool if optimization for performance is required.

Additional concerns that needed to be resolved for a struct `RateLimitLease` are elaborated below:

#### Permit as reference ID

There was alternative proposal where the struct only contains a reference ID and additional APIs on the `RateLimiter` instance is used to return permits and obtain additional metadata. This is equivalent to the `RateLimiter` internally tracking outstanding permit leases and allow permit release via `RateLimiter.Release(RateLimitLease.ID)` or obtain additional metadata via `RateLimiter.TryGetMetadata(RateLimitLease.ID, MetadataName)`. This shifts the need to pool data structures for tracking idempotency of `Dispose` and additional metadata to the `RateLimiter` implementation itself. This additional indirection doesn't resolve the bifurcation issue mentioned previously and necessitates additional APIs that are hard to use and implement on the `RateLimiter`, as such this alternative is not chosen.

#### RateLimitLease state

The current proposal uses a `object State` to communicate additional information on a rate limit decision. This is the most general way to provide additional information since the `RateLimiter` can add any arbitrary type or collections via `object State`. However, there is a tradeoff between the generality and flexibility of this approach with usability. For example, we have gotten feedback from ATS that they want a simpler way to specify a set of values such as RetryAfter, error codes, or percentage of permits used. As such, here are several design alternatives.

##### Interfaces

One option to support access to values is to keep the `object State` but require limiters to set a state that implements different Interfaces. For example, there could be a `IRateLimiterRetryAfterHeaderValue` interface that looks like:

```c#
public interface IRateLimiterRetryAfterHeaderValue
{
    string RetryAfter { get; }
}
```

Consumers of the `RateLimiter` would then check if the `State` object implements the interface before retrieving the value. It also puts burdens on the implementers of `RateLimiters` since they should also define a set interfaces to represent commonly used values.

##### Property bags

Property bags like `Activity.Baggage` and `Activity.Tags` are very well suited to store the values that were identified by the ATS team. For web work loads where these values are likely to be headers and header value pairs, this is a good way to express the `State` field on `RateLimitLease`. Specifically, the type would be either:

Option 1: `IReadonlyDictionary<string,string?> State`

However, there is a drawback here in terms of generality since it would mean that we are opinionated about the type of keys and values as strings. Alternatively we can modify this to be:

Option 2: `IReadonlyDictionary<string,object?> State`

This is slightly more flexible since the value can be any type. However, to use these values, the user would need to know ahead of time what the value for specific keys are and downcast the object to whatever type it is. Going one step further:

Option 3: `IReadonlyDictionary<object,object?> State`

This gives the most flexibility in the property bag, since we are no longer opinionated about the key type. But the same issue with option 2 remains and it's unclear whether this generality of key type would actually be useful.

##### Feature collection

Another way to represent the `State` would be something like a [`IFeatureCollection`](https://docs.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.features.ifeaturecollection?view=aspnetcore-5.0). The benefit of this interface is that while it is general enough to contain any type of value and that specific implementations can optimize for commonly accessed fields by accessing them directly (e.g. https://github.com/dotnet/aspnetcore/blob/52eff90fbcfca39b7eb58baad597df6a99a542b0/src/Http/Http/src/DefaultHttpContext.cs).

### A `bool` returned by `TryAcquire` to indicate success/failure and throw for `WaitAsync` to indicate failure

An earlier iteration proposed the following API instead:

```c#
namespace System.Threading.RateLimiting
{
  public abstract class RateLimiter
  {
    // An estimated count of permits. Potential uses include diagnostics.
    abstract int GetAvailablePermits();

    // Fast synchronous attempt to acquire permits.
    // Set requestedCount to 0 to get whether permit limit has been reached.
    abstract bool Acquire(int requestedCount, out RateLimitLease lease);

    // Wait until the requested permits are available.
    // Set requestedCount to 0 to wait until permits are replenished.
    // An exception is thrown if permits cannot be obtained.
    abstract ValueTask<RateLimitLease> WaitAsync(int requestedCount, CancellationToken cancellationToken = default);
  }

  public struct RateLimitLease: IDisposable
  {
    // This represents additional metadata that can be returned as part of a call to TryAcquire/WaitAsync
    // Potential uses could include a RetryAfter value.
    public object? State { get; init; }

    // Constructor
    public RateLimitLease(object? state, Action<RateLimitLease>? onDispose);

    // Return the acquired permits
    public void Dispose();

    // This static field can be used for rate limiters that do not require release semantics or for failed concurrency limiter acquisition requests.
    public static RateLimitLease NoopSuccess = new RateLimitLease(null, null);
  }
```

This was proposed since the method name `TryAcquire` seemed to convey the idea that it is a quick synchronous check. However, this also impacted the shape of the API to return `bool` by convention and return additional information via out parameters. If a limiter wants to communicate a failure for a `WaitAsync`, it would throw an exception. This may occur if the limiter has reached the hard cap. The drawback here is that these scenarios, which may be frequent depending on the scenario, will necessitate an allocation of an `Exception` type.

Another alternative was identified with `WaitAsync` returning a tuple, i.e. `ValueTask<(bool, RateLimitLease)> WaitAsync(...)`. The consumption pattern would then look like:
```c#
(bool successful, RateLimitLease lease) = await WaitAsync(1);
if (successful)
{
    using lease;
    // continue processing
}
else
{
    // limit reached
}
```

## FAQs

### Concurrency limiter is just a Semaphore in disguise?

While a concurrency limiter is similar in concept to a Semaphore since both protects underlying resources by limiting how many has access at a time, there are several differences between the two in terms of how they are used.

1. The concurrency limiter allows for obtaining more than one permit at a time

This can be useful in scenarios where the resource being protected can be obtained in chunks other than 1. For example, there is a limit of 100 bytes that can be used and to process a particular request requires the use of 5 bytes. Semaphores on the other hand can only wait for resources one at a time. Checking a counter under a lock is possible and can achieve the effect of `Acquire` but cannot replicate `WaitAsync` without some sort of waiting (e.g. spin wait, mre, etc) mechanism.

2. The concurrency limiter only allows the user to "release" the permits that were obtained

The permits obtained through concurrency limiters imply ownership. Since the user interacts with the `RateLimitLease` instead of being able to directly call a `Release(...)` method, the user cannot release more permits than was obtained through the limiter. This is different from a Semaphore where releasing without first obtaining any permits is possible.

### How "allocating" will rate limiters be?

The design of the interface may incur allocations for example, when permits are acquired successfully on concurrency limiters. In these cases, it's up to the limiter implementation to pool to reduce allocations. Also, when the soft cap is reached an queuing is enabled, allocations of registrations used to track completion (i.e. when permits become available) will potentially result in additional allocations.

### Why is there a configurable queuing behaviour when `TAcquire` and `WaitAsync` are available to distinguish between synchronous checks and awaitable checks?

The `Acquire` and `WaitAsync` captures the user's intent on whether they are willing to wait for permits to be availble. The queuing behaviour is set on the `RateLimiter` to indicate the limiter's capabilities in terms of queuing pending acquisition requests. Queuing takes place if the user's intent is to wait and the limiter's is capable of queuing acquisition requests. In all other cases, the response from the limiter will be immdediate, be it successful or not.

## Additional design concepts and notes

There are additional design notes captured in https://github.com/aspnet/specs/tree/main/design-notes/ratelimit including: reference designs from current implementations by other Azure teams, layer composition illustrating how RateLimiters may be used in services and frameworks, and relationships between different components such as policies, rate limiters and external storage.
