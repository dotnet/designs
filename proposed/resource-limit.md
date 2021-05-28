# Resource limits

**Owner** [John Luo](https://github.com/juntaoluo) | [Sourabh Shirhatti](https://github.com/shirhatti)

## Scenarios and User Experience

Excerpt from https://microsoft.sharepoint.com/:w:/r/teams/NETRateLimitingInterface/_layouts/15/Doc.aspx?sourcedoc=%7B557C73D8-F2A2-4799-82D0-6A5C13F58E4F%7D&file=Rate%20Limiting%20interface_CBB%20Perspective.docx&nav=eyJjIjo2MDMwNjMzMzZ9&action=default&mobileredirect=true&cid=fb8aa981-06d2-4c69-9bbd-ddfb278e2eb7

> Outages caused when system activities exceed the system’s capacity is a leading concern in system design. The ability to handle system activity efficiently, and graceful limit the rate of activities executing before the system is under stress is a fundamental principle in system resiliency. This applies not only to interactions between 2 or more disparate systems but also to the interactions occurring within all layers of an individual system instance. .NET does not have a standardized means for expressing and managing limiting logic needed to produce a resilient system. This adds complexity to designing and developing resilient software in .NET by introducing an easy vector for competing rate limiting logic and anti-patterns. A standardized interface in NET for limiting activities will make it easier for developers to build resilient systems for all scales of deployment and workload. This document outlines a standard resource limiting interface with the goal of adding it to .NET.

### Use cases for self-replenishing resources, aka **Rate Limits**

A commonly used strategy to throttle operations is to specify a time-based rate limit. An example of this would be limiting a service to serve X requests per second and rejecting any new requests when the limit is reached.

The benefit of a conceptually simple limit is that they can provide protection with minimal overhead. It is also simple to use since the limiter updates itself when additional resource is available after a specific period of time has elapsed without any input from the user. For example, the limiter itself will reset the number of requests allowed to be processed after each second.

This simple implementation also lends itself to limiting resources among different services or machines. A use case that was requested was querying a Redis store to determine whether a rate limit is satisfied or exceeded.

### Use cases for non self-replenishing resources, aka **Concurrency Limits**

A different strategy to throttle operations is to specify a concurrency-based limit that describes underlying physical resources. An example would be to specify only X number of requests can be processed at a time based where X may be calculated based on the number of cores.

These types of limits may enable more efficient use of underlying resources. For example, to ensure that ASP.NET Core stops accepting requests that would otherwise lead to thread-pool starvation, a concurrency limit can be specified. This is better than a rate limit since it can be possible to process a higher number of short-running requests as long as the concurrency limit is not exceeded at any particular time.

However, these limits may be more complex and require that the users to explicitly release the resources upon completion of their operation. Given the trade-off between consistency and speed, it is also likely to be poorly suited to limiting resource usage among different services or machines.

### Unified abstraction

We want to use a single abstraction to represent both types of limits. This is because for many applications, there's a desire to represent both limits in a transparent way. For example, in ASP.NET Core we want to allow users to register a set of limits before processing a specific request and only proceed if all limits are satisfied. These limits may be rate or concurrency based or even based on some ambient system state. In order to represent all of these scenarios in a way that can be easily queried, a unified API seems more appropriate, rather than two separate APIs for self replenishing resource and non self-replenishing resources and have applications track these resources independently.

### Composition with BCL types

There are two main reasons to introduce this API in the BCL.

#### Shared abstractions and implementation for the entire stack

By introducing the new API in the BCL, we maximize the ability to share resource limiter across BCL types in additional to usage in ASP.NET Core and user code. For example, rate limiters can be used to limit traffic in BCL types, ASP.NET Core middlewares and service implementations.

#### Integration with BCL types for ease of usage

There are various ways to compose usage of the proposed resource limiting APIs and BCL types. For example, there is a trade-off between coupling and ease of usage. Consider the options:

```c#
// No coupling
var channel = Channel.CreateBounded<string>(5);
var limiter = new SampleRateLimiter(resourceCount: 5, newResourcePerSecond: 5);

if (limiter.Acquire().IsAcquired) {
    channel.Writer.TryWrite("New message");
}

// Coupling
var limiter = new SampleRateLimiter(resourceCount: 5, newResourcePerSecond: 5);
// This creates a LimitedChannel<T>. Its operations are limited by the limiter specified,
// which may be rate/concurrency based. LimitedChannel<T> with a concurrency limiter would
// be similar to a BoundedChannel if the limiter's queue is disabled, see design section
// for details on limiter's queue.
var channel = Channel.CreateLimited<string>(limiter);

channel.Writer.TryWrite("New message");
```

The benefit of tighter coupling with the BCL types is that it allows for simpler usage. In the former case, the user need to explicitly check the limiter every time an operation to the channel writer is requested. In the latter case, the channel writer can be used in the same manner as today and the channel writer itself will perform the check on the resource limiter on behalf of the user.

### Handling bursts

Another common concern that resource limiters need to handle are traffic bursts. A method for handling these scenarios is to implement a queuing strategy for the limiters which will register incoming requests and complete the requests when resources become available. Note, that this feature may or may not be supported depending on the resource limiter implementation. Conceptually, the resource limiter can operate in 3 states, see [default limiter implementations](#default-limiter-implementations) on how this is achieved.

1. Normal operation (Resource limit not exceeded): The resource limiter will allow the operation to continue as long as the resource limit is not exceeded.
2. Soft cap exceeded (Resource limit exceeded, queue length not exceeded): The resource limiter can queue additional requests which will be allowed to proceed when resources become available.
3. Hard cap exceeded (Resource limit exceeded and queue length exceeded): The resource limiter will reject any additional requests.

With the queueing strategy, the resource limiter can also be used to shape traffic and smooth out bursts. For example, if a rate limiter with a limit of 5 requests/second and a queue of 25 can handle a burst of 30 requests which will be converted to a sequence of 5 requests/second over 6 seconds.

Note that the hard cap is useful in guaranteeing that queuing is not unbounded. In the case where a system is overwhelmed it is better to immediately reject requests and try again at a later time instead of potentially waiting in the queue without a realistic chance of being allowed to process in a reasonable amount of time.

### Goals

These are the goals we would like to achieve in .NET 6.0. The resource limit abstractions and implementations are planned to target `netstandard2.0` and be shipped in OOB packages from dotnet/runtime (i.e. not included in Microsoft.NETCore.App).

#### Resource limit abstractions

Users will interact with this component in order to obtain decisions for rate or concurrency limits. This abstraction require explicit release sematics to accommodate non self-replenishing resources. This component encompasses the Acquire/WaitAsync mechanics (i.e. check vs wait behaviours) and default implementations will be provided for select accounting method (fixed window, sliding window, token bucket, concurrency). The return type is a `ResourceLease` which indicates whether the acquisition is successful and manages the lifecycle of the aquired resources.

#### Resource limit implementations

In addition to defining the abstractions, we would also like to ship some default implementations of common types of resources limiters. Details on how the functionality described here are achieved can be found in the [implementation section](#concrete-implementations).

1. Fixed window rate limiter

This is the simplest type of rate limiter. It counts the number of resources acquired in a time span. If the resource limit is reached, additional calls to `Acquire` will return a `ResourceLease` with a `ResourceLease.IsAcquired = false` and additional calls to `WaitAsync` will wait if queuing is enabled until the queue limit. The resource is refreshed when the current window has elapsed.

2. Sliding window rate limiter

This is another type of rate limiter. Counts for the number of resources acquired in a time segment is kept. A window is defined as multiple time segments and the resource is considered to be exhausted if the total across the segments in the window is exceeded. If the resource is exhausted, additional calls to `Acquire` will return a `ResourceLease` with a `ResourceLease.IsAcquired = false` and additional calls to `WaitAsync` will wait if queuing is enabled until the queue limit. The window is updated when the current time segment has elapsed.

As an example consider the following example:

```
Settings: window time span = 3s, number of segments per window = 3, resource limit = 10

Time:   |1s |2s |3s |4s |
Count:  [3] [4] [3] [1]
        | Window 1  |
            | Window 2  |
```

In the above example, resource is exhaused in Window 1 since the total count is 10 and no additional resources can be acquired in the 3rd second. On the 4th second, Window 2 now has a count 8 and can accept additional resource acquisition requests.

3. Token bucket rate limiter

This rate limiter uses the [token bucket algorithm](https://en.wikipedia.org/wiki/Token_bucket). The functionality will be similar to the two implementation described above but will be configured differently.

See a [proof of concept implementation](#token-bucket-limiter-with-queue-poc).

4. Concurrency limiter

This limiter keeps track of the number of concurrent resources in use. Conceptually, it will function in a similar way to a Semaphore/SemaphoreSlim but adapted to the proposed API. Like the rate limits above, it will also keep a queue of pending acquisition requests if configured to do so.

See a [proof of concept implementation](#concurrency-limiter-with-random-queue-poc).

#### Adoption in ASP.NET Core and others

We are committed to using these primitives in ASP.NET Core to ship a concurrency and rate limit middleware as well as improving our existing server limits in Kestrel HTTP Server. We will likely also ship an extensions package that provides support for a redis based rate limiter. Finally, there is likely to be further adoption of these types in YARP and there has been interest in these primitives from Azure teams such as ATS and ACR.

### Non-Goals

#### Resource limit adoption in the BCL

Two BCL types where adoption of the resource limiters are beneficial and relatively simple are:

- System.Threading.Channels
- System.IO.Pipelines

Prototypes are illustrated in the [proof of concepts](#dotnetruntime-pocs) but are not commited to for .NET 6.

#### HttpClient, Streams, Sockets

Although we anticipate that there will be applications in many .NET areas and components such as HttpClient, Streams and Sockets, these scenarios will be explored but not committed for adoption in .NET 6 since they may be more complex. The adoption of the resource limiting APIs in these areas can happen independently of this proposal in the future.

#### Partial Acquisition and Release

Current design does not allow for partial acquisition or release of resources. In other words, either all of the request count is acquired or nothing is acquired. Likewise, when the operation is complete, all resources acquired via the limiter is released together. Though there are theoretically potential use cases for partial acquisitions and releases, no concrete examples has been identified in ASP.NET Core or .NET. In case these functionalities are needed, additional APIs can be added indepently from the APIs proposed here.

For example, this will likely involve a modified `ResourceLease` (expand details for API)

<details>

```c#
public struct ResourceLease : IDisposable
{
    // This represents whether resource acquisition was successful
    public bool IsAcquired { get; }

    // This represents the count of resources obtained in the lease
    public int Count { get; }

    // This represents additional metadata that can be returned as part of a call to Acquire/AcquireAsync
    // Potential uses could include a RetryAfter value or an error code.
    public object? State { get; }

    // Private fields to be used by `onDispose()`, this is not a public API shown here for completeness
    private ResourceLimiter? _resourceLimiter;
    private Action<ResourceLimiter?, int, object?>? _onDispose;

    // Constructor which sets all the readonly values
    public ResourceLease(
        bool isAcquired, 
        int Count, 
        object? state, 
        ResourceLimiter? resourceLimiter, 
        Action<ResourceLimiter?, int, object?>? onDispose);

    // Release a specific amount of resources, if possible

    public void Release(int releaseCount);

    // Return the acquired resources, calls onDispose with ResourceLimiter
    // This can only be called once, it's an user error if called more than once
    public void Dispose();
}
```

</details>


#### Aggregated limiters

A separate abstraction different from the one proposed here is required to support scenarios where aggregated resources are needed.

For high cardinality resources such as rate limit by IP, we don't want to have a rate limit per bucket (i.e. one rate limiter per IP Address). As such, we need an API where you can pass in a resourceID. This is in contrast to simpler resources where a key is not necessary, such as a n requests/second limit, where requiring a default key to be passed in becomes awkward. Hence the simpler API proposed here is preferred.

However, we have not yet found any use cases for aggregated limiters in dotnet/runtime and hence it's not part of this proposal. If we eventually deem that such aggregated limiters are needed in the BCL as well, it can be added independently from the APIs proposed here.

Sample API:

```c#
// Represent an aggregated resource (e.g. a resource limiter aggregated by IP)
public abstract class AggregatedResourceLimiter<TKey>
{
    // an inaccurate view of resources
    public abstract int EstimatedCount(TKey resourceID);

    // Fast synchronous attempt to acquire resources
    public abstract ResourceLease Acquire(TKey resourceID, int requestedCount);

    // Wait until the requested resources are available
    public abstract ValueTask<ResourceLease> WaitAsync(TKey resourceID, int requestedCount, CancellationToken cancellationToken = default);
}
```

See a [proof of concept implementation](#ip-address-aggregated-resource-limiter-using-a-local-concurrentdictionary-as-a-storage-mechanism).

## Stakeholders and Reviewers

- Owned by ASP.NET Core
- Code will be a new area in dotnet/runtime
  - Tentatively System.Threading.ResourceLimits
- Reviewers
  - David Fowler, Eric Erhardt, Stephen Halter

## Design

### Abstractions

The main abstraction API will consist of the `ResourceLimiter` and the associated `ResourceLease`. The main responsibility of the `ResourceLimiter` is to allow the user to determine whether it's possible to proceed with an operation. The `ResourceLease` struct is returned by the `ResourceLimiter` to represent if the acquisition was successful and the resources that were obtained, if successful.

```c#
namespace System.Threading.ResourceLimits 
{
    // Represents a limiter type that users interact with to determine if an operation can proceed
    public abstract class ResourceLimiter
    {
        // An estimated count of resources. Potential uses include diagnostics.
        abstract int EstimatedCount { get; }

        // Fast synchronous attempt to acquire resources.
        // Set requestedCount to 0 to get the limiter state, e.g. whether resource limit has been reached.
        abstract ResourceLease Acquire(int requestedCount);

        // Wait until the requested resources are available.
        // Set requestedCount to 0 to wait until resource is replenished.
        abstract ValueTask<ResourceLease > AcquireAsync(int requestedCount, CancellationToken cancellationToken = default);
    }

    // Represents a resource lease obtained from the limiter. The user disposes this type to release the acquired resources.
    public struct ResourceLease : IDisposable
    {
        // This represents whether resource acquisition was successful
        public bool IsAcquired { get; }

        // This represents the count of resources obtained in the lease
        public int Count { get; }

        // This represents additional metadata that can be returned as part of a call to Acquire/AcquireAsync
        // Potential uses could include a RetryAfter value or an error code.
        public object? State { get; }

        // Private fields to be used by `onDispose()`, this is not a public API shown here for completeness
        private ResourceLimiter? _resourceLimiter;
        private Action<ResourceLimiter?, int, object?>? _onDispose;

        // Constructor which sets all the readonly values
        public ResourceLease(
            bool isAcquired, 
            int Count, 
            object? state, 
            ResourceLimiter? resourceLimiter, 
            Action<ResourceLimiter?, int, object?>? onDispose);

        // Return the acquired resources, calls onDispose with ResourceLimiter
        // This can only be called once, it's an user error if called more than once
        public void Dispose();
    }
}
```

The `Acquire` call represents a fast synchronous check that immediately returns whether there are enough resources available to continue with the operation and atomically acquires them if there are, returning `ResourceLease` with the value `ResourceLease.IsAcquired` representing whether the acquisition is successful and the struct itself representing the acquired resources, if successful. The user can pass in a `requestCount` of 0 to check whether the resource limit has been reached without acquiring any resources.

`WaitAsync`, on the other hand, represents an awaitable request to check whether resources are available. If resources are available, obtain the resources and return immediately with a `ResourceLease` representing the acquired resources. If the resources are not available, the caller is willing to pause the operation and wait until the necesasary resources become available. The user can also pass in a `requestCount` of 0 but this semantically signifies that the caller wants to check whether the resource limit has been reached and if the limit is reached, want to wait until more resources become available. This method will throw upon: cancellation, error, or queue length exceeded ([see implementation section](#concrete-implementations)). For example, if the hard cap is reached, additional call to `WaitAsync` will result in `ResouceLease.IsAcquired = false` being returned.

In terms of design, these APIs are similar to System.Threading.Channels.ChannelWriter, for example:
- `ResourceLimiter.Acquire(...)` vs `ChannelWriter.TryWrite(...)`
- `ResourceLimiter.WaitAsync(0, ...)` vs `ChannelWriter.WaitToWriteAsync(...)`
- `ResourceLimiter.WaitAsync(N, ...)` vs `ChannelWriter.WriteAsync(...)`

Note that the `WaitAsync` call is not opinionated on how to order the incoming acquisition requests. This is up to the implementation and will be further elaborated in the [implementation section](#concrete-implementations).

`EstimatedCount` is envisioned as a flexible and simple way for the limiter to communicate the status of the limiter to the user. For example, an implementation may use this to track the number of resources that are still available (e.g. a resource limiter that counts down on resource acquisition). Another may use it to indicate the current number of resources in use (e.g. a resource limiter that counts up on resource acquisition). This count is similar in essence to `SemaphoreSlim.CurrentCount`. This count can also be used in diagnostics to track the usage of the resource. Finally this count can be used in a best effort scenario where the user tries to check that a certain amount of resource are availble before trying to acquire those resources, though no atomicity is guaranteed between checking this count and calling `Acquire` or `WaitAsync`. Note that while the abstraction is not opinionated about the definition of this count, each implementation can be specific with what this count means. It should also be noted that there is a trade-off between flexibility of this count against precision of this field's meaning.

The struct `ResourceLease` is used to facilitate the release semantics of resource limiters. That is, for non self-replenishing, the returning of the resources obtained via `Acquire/WaitAsync` is achieved by disposing the `ResourceLease`. This enables the ability to ensure that the user can't release more resources than was obtained.

The `ResourceLease.IsAcquired` property is used to express whether the acquisition request was successful. The `ResourceLease.Count` property is used to communicate the number of resource that were acquired.

We've also identified scenarios where the limiter may want to return additional information to the caller, such as reason phrases, response codes, percentage of rate saturation, retry after etc. For these cases, the object `ResourceLease.State` can be used to store the additional metadata. Note that the user will need to downcast the `State` in order to access it and therefore needs to know about the specific implementation of the `ResourceLimiter` in order to know the type of the `State`. For a sample limiter implemenation that uses state, see this [proof of concept](#resource-limiter-wrapper-with-reason-phrases). For alternatives to an `object` based `State`, see [alternative designs](#state-field-on-resourcelease).

We also plan on adding extension methods to simplify the common scenario where a count of 1 is used to represent 1 request.

```c#
    public static class ResourceLimiterExtensions
    {
        public static ResourceLease Acquire(this ResourceLimiter limiter)
        {
            return limiter.Acquire(1);
        }

        public static ValueTask<ResourceLease> WaitAsync(this ResourceLimiter limiter, CancellationToken cancellationToken = default)
        {
            return limiter.WaitAsync(1, cancellationToken);
        }
    }
```

#### Common usage patterns

In an ASP.NET Core endpoint, usage of specific `ResourceLimiter`s will look like the following. Note that in these cases, the resource limiter is not known to be a rate or concurrency limiter and therefore the release mechanics must be followed.

```c#
ResourceLimiter limiter = new SomeResourceLimiter(options => ...)

// Synchronous checks
endpoints.MapGet("/acquire", async context =>
{
    // Check limiter using `Acquire` that should complete immediately
    using var resourceLease = limiter.Acquire();
    // ResourceLease was successfully obtained, the using block ensures
    // that the resourceLease is released upon processing completion.
    if (resourceLease.IsAcquired)
    {
        await context.Response.WriteAsync("Hello World!");
    }
    else
    {
        // Resource lease acquisition failed, send 429 response
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        return;
    }
}

// Async checks
endpoints.MapGet("/waitAsync", async context =>
{
    // Check limiter using `WaitAsync` which may complete immediately
    // or wait until resources are available. Using block ensures that
    // the resource is released upon processing completion.
    using var resourceLease = await limiter.WaitAsync();
    if (resourceLease.IsAcquired)
    {
        await context.Response.WriteAsync("Hello World!");
    }
    else
    {
        // Resource lease acquisition failed, send 429 response
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        return;
    }
}
```

For additional usage samples, checkout the [proof of concepts](#proof-of-concepts).

### Concrete implementations

#### Default limiter implementations

Additional public APIs are needed to configure specific limiter implementations listed in [the goals](#resource-limit-implementations). The options for configuring these implementations will be described here without going into implementation details.

```c#
namespace System.Threading.ResourceLimits
{
    // Limiter implementations
    public sealed class FixedWindowRateLimiter : ResourceLimiter { }
    public sealed class SlidingWindowRateLimiter : ResourceLimiter { }
    public sealed class TokenBucketRateLimiter : ResourceLimiter { }
    public sealed class ConcurrencyLimiter : ResourceLimiter { }

    // This specifies the behaviour of `WaitAsync` When ResourceLimit has been reached
    public enum ResourceDepletedMode
    {
        EnqueueIncomingRequest,
        PushIncomingRequest
    }

    // Limiter options common to default runtime implementations
    internal abstract class ResourceLimiterOptions
    {
        // Specifies the maximum number of resources for the limiter
        public int ResourceLimit { get; set; }
        // Resource depleted mode, configures `WaitAsync` behaviour
        public ResourceDepletedMode ResourceDepletedMode { get; set; }
        // Queue limit when queuing is enabled
        public int MaxQueueLimit { get; set; }
    }

    // Rate limiter options
    internal abstract class RateLimiterOptions : ResourceLimiterOptions
    {
        // This can be used to externally trigger rate limit window/replenishment updates
        public EventHandler<DateTimeOffset> TickEventHandler { get; set; }
    }

    // Window based rate limiter options
    public class FixedWindowRateLimiterOptions : RateLimiterOptions
    {
        // Specifies the duration of the window where the ResourceLimit is applied
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

    public class ConcurrencyLimiterOptions : ResourceLimiterOptions { }
}
```

The implementation of the four limiters are omitted here since there's no additional public API other than the ones defined by `ResourceLimiter`.

While proposed API above illustrates the hierarchy of the limiter options the public option fields will be discussed individually here in order they appear.

1. `FixedWindowRateLimiterOptions`

- `ResourceLimit`

This limit is common to all limiters proposed here. It represents a maximum number of resources that can be used. However, the exact definition will be different between each limiter. For example, for `FixedWindowRateLimiterOptions` and `SlidingWindowRateLimiterOptions` this will mean the maximum number of resources or requests allowed in a given window. For `TokenBucketRateLimiterOptions` this would mean the maximum number of tokens that the bucket can hold. For `ConcurrencyLimiterOptions` this would mean the maximum number of concurrent resources that can be used at any time.

- `ResourceDepletedMode`

This enum is used to configure the behaviour of `WaitAsync` to also allow for waiting for acquisition requests when `ResourceLimit` is reached. There are currently 2 identified behaviour, queue in FIFO order (`EnqueueIncomingRequest`), queue in LIFO order (`PushIncomingRequest`). `ResourceDepletedMode` value and `MaxQueueLimit` is used together to define `WaitAsync` queuing behaviour.

- `MaxQueueLimit`

When queuing is enabled, the behaviour of `WaitAsync` is controlled by this value and `ResourceDepletedMode`. There are 3 possible states the limiter can be in. Note "resource count" here represents the sum of 1. outstanding resources (i.e. resources that have been already acquired) + 2. currently queued count (i.e. sum of all requested counts currently queued) 3. requested resources (i.e. value passed in via `requestedCount`).

1. resource count <= `ResourceLimit`: `WaitAsync` succeeds and will return a `ResourceLease` representing the acquired resources immediately
2. `ResourceLimit` < resource count <= `ResourceLimit`+ `MaxQueueLimit`: `WaitAsync` will enqueue or push the pending request until more resources become available.
3. resource count > `ResourceLimit`+ `MaxQueueLimit`: `WaitAsync` will return `ResouceLease.IsAcquired = false` immediately if `EnqueueIncomingRequest` is configured. If `PushIncomingRequest` is configured, the oldest registration in the stack is removed and finished with `ResouceLease.IsAcquired = false`.

Side note:

There are technically more queue behaviours that are possible which can be computed as the cross product of {`Queue`, `Stack`} X {`FailOldest`, `FailNewest`, `FailIncoming`}. However, it is unclear whether all combinations are useful and the two most sensible options are encoded in the `ResourceDepletedMode.EnqueueIncomingRequest` and `ResourceDepletedMode.PushIncomingRequest` enums.

- `TickEventHandler`

This event handler is used to allow a shared clock to trigger replenishments for multiple rate limiters instead of requiring each individual rate limiter to create a Timer to replenishing its resources.

- `Window`

This represents the time span where the `ResourceLimit` is to be applied.

1. `SlidingWindowRateLimiterOptions`

- `ResourceLimit` - see `FixedWindowRateLimiterOptions`
- `ResourceDepletedMode` - see `FixedWindowRateLimiterOptions`
- `MaxQueueLimit` - see `FixedWindowRateLimiterOptions`
- `TickEventHandler` - see `FixedWindowRateLimiterOptions`
- `Window` - see `FixedWindowRateLimiterOptions`
- `SegmentsPerWindow`

This represents how many segments each window is to be divided into.

3. `TokenBucketRateLimiterOptions`

- `ResourceLimit` - see `FixedWindowRateLimiterOptions`
- `ResourceDepletedMode` - see `FixedWindowRateLimiterOptions`
- `MaxQueueLimit` - see `FixedWindowRateLimiterOptions`
- `TickEventHandler` - see `FixedWindowRateLimiterOptions`
- `ReplenishmentPeriod`

This specifies how often replenishment occurs.

- `TokensPerPeriod`

This specifies how many tokens are restored each time replenishment occurs.

4. `ConcurrencyLimiterOptions`

- `ResourceLimit` - see `FixedWindowRateLimiterOptions`
- `ResourceDepletedMode` - see `FixedWindowRateLimiterOptions`
- `MaxQueueLimit` - see `FixedWindowRateLimiterOptions`

#### Channels

Instead of modifying the existing `BoundedChannel` and `UnboundedChannel`, the proposal here is to create a new `ResourceLimitedChannel` along with a `ResourceLimitedChannelOptions`.

```c#
namespace System.Threading.Channels
{
    public sealed class ResourceLimitedChannelOptions : ChannelOptions
    {
        public ResourceLimiter WriteLimiter { get; set; }
    }

    internal sealed class ResourceLimitedChannel<T> : Channel<T> { }

    public static class Channel
    {
        public static Channel<T> CreateResourceLimited<T>(LimitedChannelOptions options)
    }
}
```

The `ResourceLimitedChannel` represents a superset of the functionality of `BoundedChannel` allowing for configuration with both concurrency and rate limits. As a general overview, the `WriteLimiter` is queried on `TryWrite`, `WriteAsync` and `WaitToWriteAsync`. When a written item is enqueued to `_items`, a corresponding `ResourceLease` is also added. Upon dequeue from `_items` from the `ChannelReader` or upon unblocking a reader, `ResourceLease.Dispose` is invoked to release resources back to the write limiter. NOte that the `ResourceLimitedChannel` will only limit the producer side, i.e. writes. For details on proof of concept for the implementation, see [dotnet/runtime PoCs](#dotnetruntime-pocs).

#### Pipelines

TBD

## Proof-of-concepts

### Customized resource limiter based on ambient state that won't ship in box

e.g. The limiter fails when CPU usage is above 90%

```c#
class CPUUsageLimiter : ResourceLimiter
{
    private PerformanceCounter _cpuUsageCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
    private float _threshold;

    public CPUUsageLimiter(float threshold)
    {
        _threshold = threshold;
    }

    public int EstimatedCount { get; } => (int)_cpuUsageCounter.NextValue();

    public ResourceLease Acquire(int requestedCount)
    {
        if (cpuCounter.NextValue() > _threshold)
        {
            return FailedAcquisition;
        }
        return SuccessfulAcquisition;
    }

    public async ValueTask<ResourceLease> WaitAsync(int requestedCount, CancellationToken cancellationToken = default)
    {
        while(true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (cpuCounter.NextValue() <= _threshold)
            {
                return SuccessfulAcquisition;
            }
            await Task.Delay(1000); // Wait 1s before rechecking.
        }
    }
    
    public static ResourceLease SuccessfulAcquisition = new ResourceLease(true, 0, null, null, null);
    public static ResourceLease FailedAcquisition = new ResourceLease(false, 0, null, null, null);
}
```

### Resource limiter wrapper with reason phrases

```c#
class ResourceLimiterWrapperReasonPhrase : ResourceLimiter
{
    private string _failureReasonPhrase;
    private ResourceLimiter _innerLimiter;

    public ResourceLimiterWrapperReasonPhrase(string reasonPhrase, int requestPerSecond)
    {
        _failureReasonPhrase = reasonPhrase;
        _innerLimiter = new RateLimiter(requestPerSecond: requestPerSecond); // wrapping to reduce verbosity in this sample.
    }

    public int EstimatedCount { get; } => _innerLimiter.EstimatedCount;

    public ResourceLease Acquire(int requestedCount)
    {
        var innerResource = _inner.Acquire(requestedCount)
        if (innerResource.IsAcquired)
        {
            return innerResource;
        }

        var resourceLease = new ResourceLease(true, _failureReasonPhrase /*wrap inner state?*/, _ => innerResource.Dispose());
        return resourceLease;
    }

    public ValueTask<ResourceLease> WaitAsync(int requestedCount, CancellationToken cancellationToken = default)
        => _inner.WaitAsync(requestedCount, cancellationToken);
}

// Usage scenario

// API Controller:
[ApiController]
[RequestLimit(new ResourceLimiterWrapperReasonPhrase(reasonPhrase: "Too many booking requests", requestPerSecond: 100))]
public class BookingController : ControllerBase
{
    [RequestLimit(new ResourceLimiterWrapperReasonPhrase(reasonPhrase: "Too many hotel booking requests", requestPerSecond: 5))]
    public IActionResult BookHotel()
    {
        ...
    }

    [RequestLimit(new ResourceLimiterWrapperReasonPhrase(reasonPhrase: "Too many car rental booking requests", requestPerSecond: 5))]
    public IActionResult BookCarRental()
    {
        ...
    }
}

// Middleware that handles the limiters
public async Task Invoke(HttpContext context)
{
    var endpoint = context.GetEndpoint();
    var limiters = endpoint?.Metadata.GetOrderedMetadata<ResourceLimiter>();

    var resources = new Stack<ResourceLease>();
    try
    {
        foreach (var limiter in limiters)
        {
            var resourceLease = limiter.Acquire();
            if (!resourceLease.IsAcquired)
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                if (resourceLease.State is string reasonPhrase)
                {
                    await context.Response.WriteAsync(reasonPhrase);
                }
                return;
            }
            resources.Push(resourceLease);
        }

        await _next.Invoke(context);
    }
    finally
    {
        while (resources.TryPop(out var resourceLease))
        {
            resourceLease.Dispose();
        }
    };
}
```

### Token bucket limiter with queue PoC

Proof of concept at: https://github.com/dotnet/aspnetcore/blob/41d5bb475a89b91bf097aefd232258dee4c8839b/src/Middleware/RequestLimiter/src/RateLimiter.cs

### Concurrency limiter with random queue PoC

Proof of concept at: https://github.com/dotnet/aspnetcore/blob/52e0b9cb7e44109fecef90bbdaa2bd5193ebc6e7/src/ResourceLimits/src/ConcurrencyLimiter.cs

### IP Address aggregated resource limiter using a local ConcurrentDictionary as a storage mechanism

Proof of concept at: https://github.com/dotnet/aspnetcore/blob/9d02c467d85c54675fa71735f7c3f7f01637557f/src/Middleware/RequestLimiter/src/IPAggregatedRateLimiter.cs

Usage pattern:

```c#
var limiter = new IPAggregatedRateLimiter(resourceCount: 5, newResourcesPerSecond: 5);
endpoints.MapGet("/acquire", async context =>
{
    // Check limiter using `Acquire` that should complete immediately
    using var resourceLease = limiter.Acquire(context, 1);
    if (resourceLease.IsAcquired)
    {
        // ResourceLease was successfully obtained, the using block ensures
        // that the resource is released upon processing completion.
        await context.Response.WriteAsync("Hello World!");
    }
    else
    {
        // ResourceLease acquisition failed, send 429 response
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        return;
    }
}
```

### dotnet/runtime PoCs

- https://github.com/JunTaoLuo/runtime/compare/resource-limit...dotnet:resource-limit

This branch contains sample implementations of `ResourceLimiter` APIs described in this proposal in dotnet/runtime and application in `System.Threading.Channels`

Usage Highlights:

```c#
var limitedChannel = Channel.CreateLimited<string>(new LimitedChannelOptions { WriteLimiter = RateLimiter(500 /*bytes per second*/ ) });
```

### dotnet/aspnetcore PoCs

- https://github.com/dotnet/aspnetcore/compare/johluo/rate-limits

THis branch contains sample usages of the `ResourceLimiter` in ASP.NET Core consisting of applications in Kestrel HTTP Server and a limiter middleware.

Usage Highlights:

```c#
// Controller
[RequestLimit(requestPerSecond: 10)]
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
        }).EnforceLimit(new RateLimiter(2, 2));

        endpoints.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}");
    }
}
```

## Alternative designs

### Separate abstractions for rate and concurrency limits

A design where rate limits and concurrency limits were expressed by separate abstractions was considered. The design more clearly express the intended use pattern where rate limits do not need to return a `ResourceLease` and does not possess release semantics. In the proposed design, the release semantics for rate limits will no-op.

However, this design has the drawback for consumers of resource limits since there are two possible limiter types that can be specified by the user. To alleviate some of the complexity, a wrapper for rate limits was considered. However, the complexity of this design was deemed undesirable and a unified abstraction for rate and concurrency limits was preferred.

### A class instead of struct for ResourceLease

This approach allows for subclassing to include additional metadata instead of an `object? State` property on the struct. However, it was deemed that potentially allocating a new `ResourceLease` for each acquisition request is too much allocation and a struct was preferred.

### State field on ResourceLease

The current proposal uses a `object State` to communicate additional information on a resource limit decision. This is the most general way to provide additional information since the `ResourceLimiter` can add any arbitrary type or collections via `object State`. However, there is a tradeoff between the generality and flexibility of this approach with usability. For example, we have gotten feedback from ATS that they want a simpler way to specify a set of values such as RetryAfter, error codes, and number of resources still available or percentage of resources used. As such, here are several design alternatives.

#### Interfaces

One option to support access to values is to keep the `object State` but require limiters to set a state that implements different Interfaces. For example, there could be a `IResourceLimiterRetryAfterHeaderValue` interface that looks like:

```c#
public interface IResourceLimiterRetryAfterHeaderValue
{
    string RetryAfter { get; }
}
```

Consumers of the `ResourceLimiter` would then check if the `State` object implements the interface before retrieving the value. It also puts burdens on the implementers of `ResourceLimiters` since they should also define a set interfaces to represent commonly used values.

#### Property bags

Property bags like `Activity.Baggage` and `Activity.Tags` are very well suited to store the values that were identified by the ATS team. For web work loads where these values are likely to be headers and header value pairs, this is a good way to express the `State` field on `ResourceLease`. Specifically, the type would be either:

Option 1: `IEnumerable<KeyValuePair<string,string?>> State`

However, there is a drawback here in terms of generality since it would mean that we are opinionated about the type of keys and values as strings. Alternatively we can modify this to be:

Option 2: `IEnumerable<KeyValuePair<string,object?>> State`

This is slightly more flexible since the value can be any type. However, to use these values, the user would need to know ahead of time what the value for specific keys are and downcast the object to whatever type it is. Going one step further:

Option 3: `IEnumerable<KeyValuePair<object,object?>> State`

This gives the most flexibility in the property bag, since we are no longer opinionated about the key type. But the same issue with option 2 remains and it's unclear whether this generality of key type would actually be useful.

#### Feature collection

Another way to represent the `State` would be something like a [`IFeatureCollection`](https://docs.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.features.ifeaturecollection?view=aspnetcore-5.0). The benefit of this interface is that while it is general enough to contain any type of value and that specific implementations can optimize for commonly accessed fields by accessing them directly (e.g. https://github.com/dotnet/aspnetcore/blob/52eff90fbcfca39b7eb58baad597df6a99a542b0/src/Http/Http/src/DefaultHttpContext.cs).

### A `bool` returned by `TryAcquire` to indicate success/failure and throw for `WaitAsync` to indicate failure

An earlier iteration proposed the following API instead:

```c#
namespace System.Threading.ResourceLimits
{
  public abstract class ResourceLimiter
  {
    // An estimated count of resources. Potential uses include diagnostics.
    abstract long EstimatedCount { get; }

    // Fast synchronous attempt to acquire resources.
    // Set requestedCount to 0 to get whether resource limit has been reached.
    abstract bool TryAcquire(long requestedCount, out ResourceLease resource);

    // Wait until the requested resources are available.
    // Set requestedCount to 0 to wait until resource is replenished.
    // An exception is thrown if resources cannot be obtained.
    abstract ValueTask<ResourceLease> WaitAsync(long requestedCount, CancellationToken cancellationToken = default);
  }

  public struct ResourceLease: IDisposable
  {
    // This represents additional metadata that can be returned as part of a call to TryAcquire/WaitAsync
    // Potential uses could include a RetryAfter value.
    public object? State { get; init; }

    // Constructor
    public ResourceLease(object? state, Action<ResourceLease>? onDispose);

    // Return the acquired resources
    public void Dispose();

    // This static field can be used for rate limiters that do not require release semantics or for failed concurrency limiter acquisition requests.
    public static ResourceLease NoopSuccess = new ResourceLease(null, null);
  }
```

This was proposed since the method name `TryAcquire` seemed to convey the idea that it is a quick synchronous check. However, this also impacted the shape of the API to return `bool` by convention and return additional information via out parameters. If a limiter wants to communicate a failure for a `WaitAsync`, it would throw an exception. This may occur if the limiter has reached the hard cap. The drawback here is that these scenarios, which may be frequent depending on the scenario, will necessitate an allocation of an `Exception` type.

Another alternative was identified with `WaitAsync` returning a tuple, i.e. `ValueTask<(bool, ResourceLease)> WaitAsync(...)`. The consumption pattern would then look like:
```c#
(bool successful, ResourceLease resource) = await WaitAsync(1);
if (successful)
{
    using resource;
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

1. The concurrency limiter allows for obtaining more than one resource at a time

This can be useful in scenarios where the resource being protected can be obtained in chunks other than 1. For example, there is a limit of 100 bytes that can be used and to process a particular request requires the use of 5 bytes. Semaphores on the other hand can only wait for resources one at a time. Checking a counter under a lock is possible and can achieve the effect of `Acquire` but cannot replicate `WaitAsync` without some sort of waiting (e.g. spin wait, mre, etc) mechanism.

2. The concurrency limiter only allows the user to "release" the resources that were obtained

The resources obtained through concurrency limiters imply ownership. Since the user interacts with the `ResourceLease` struct instead of being able to directly call a `Release(...)` method, the user cannot release more resources than was obtained through the limiter. This is different from a Semaphore where releasing without first obtaining any resources is possible.

### How "allocating" will resource limiters be?

The design of the interface allows for zero allocation as long as the resource limit has not been reached (i.e. under the soft cap). In these cases only `ResourceLease` and `ValueTask<ResourceLease>` value types are created. When the soft cap is reached an queuing is enabled, allocations of registrations used to track completion (i.e. when resources become available) will potentially result in allocations.

### Why is there a configurable queuing behaviour when `TAcquire` and `WaitAsync` are available to distinguish between synchronous checks and awaitable checks?

The `Acquire` and `WaitAsync` captures the user's intent on whether they are willing to wait for the resource to be availble. The queuing behaviour is set on the `ResourceLimiter` to indicate the limiter's capabilities in terms of queuing pending acquisition requests. Queuing takes place if the user's intent is to wait and the limiter's is capable of queuing acquisition requests. In all other cases, the response from the limiter will be immdediate, be it successful or not.

## Additional design concepts and notes

There are additional design notes captured in https://github.com/aspnet/specs/tree/main/design-notes/ratelimit including: reference designs from current implementations by other Azure teams, layer composition illustrating how ResourceLimiters may be used in services and frameworks, and relationships between different components such as policies, resource limiters and external storage.
