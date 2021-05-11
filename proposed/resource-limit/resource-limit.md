# Resource limits

**Owner** [John Luo](https://github.com/juntaoluo) | [Sourabh Shirhatti Doe](https://github.com/shirhatti)
## Scenarios and User Experience

Excerpt from https://microsoft.sharepoint.com/:w:/r/teams/NETRateLimitingInterface/_layouts/15/Doc.aspx?sourcedoc=%7B557C73D8-F2A2-4799-82D0-6A5C13F58E4F%7D&file=Rate%20Limiting%20interface_CBB%20Perspective.docx&nav=eyJjIjo2MDMwNjMzMzZ9&action=default&mobileredirect=true&cid=fb8aa981-06d2-4c69-9bbd-ddfb278e2eb7

> Outages caused when system activities exceed the system’s capacity is a leading concern in system design.  The ability to handle system activity efficiently, and graceful limit the rate of activities executing before the system is under stress is a fundamental principle in system resiliency.  This applies not only to interactions between 2 or more disparate systems but also to the interactions occurring within all layers of an individual system instance.  ASP.NET Core does not have a standardized means for expressing and managing rate limiting logic needed to produce a resilient system.  This adds complexity to designing and developing resilient software in ASP.NET Core by introducing an easy vector for competing rate limiting logic and anti-patterns.   A standardized interface in ASP.NET Core for rate limiting activities will make it easier for developers to build resilient systems for all scales of deployment and workload.  This document outlines a standard rate limiting interface with the goal of adding it to ASP.NET Core.

## Requirements

### Goals

Users will interact with this component in order to obtain decisions for rate or concurrency limits. This abstraction require explicit release sematics in the style of Semaphores to accommodate non self-replenishing (i.e. concurrency) resources. This component encompasses the TryAcquire/AcquireAsync mechanics (i.e. check vs wait behaviours) and default implementatinos will be provided for select accounting method (fixed window, sliding window, token bucket, simple concurrency). The return type is a `ThrottledResource` which manages the lifecycle of the aquired resources. This API should allow for a simple implementation that keeps an internal count of the underlying resource but also allow the use of an external resource count storage.

For bucketized policies (for example, rate limit by IP), all bucket will be represented by one limiter. The complexity of how bucketing is computed for a request will live in the policy component which computes a resource ID.

We expect that the user can opt between different implementations of the proposed interface to choose between different rate limiting algorithms. For example, between an implementation that uses token bucket vs one that makes rate limiting decisions based on ambient environment such as CPU usage. Also, different implementations may choose to use a queue/stack/priorityqueue when waiting for resources to become available.

### Non-Goals

TDB

## Stakeholders and Reviewers

- .NET team (System.Threading)
- ASP.NET Core team

## Design

```c#
    public abstract class ResourceLimiter
    {
        // an inaccurate view of resources
        abstract long EstimatedCount { get; }

        // Fast synchronous attempt to acquire resources
        // Set requestedCount to 0 to get whether resource limit has been reached
        abstract bool TryAcquire(long requestedCount, out Resource resource);

        // Wait until the requested resources are available
        // Set requestedCount to 0 to wait until resource is replenished
        // If unsuccessful, throw
        abstract ValueTask<Resource> AcquireAsync(long requestedCount, CancellationToken cancellationToken = default);
    }

    // Represent an aggregated resource (e.g. a resource limiter aggregated by IP), no identified use within dotnet/runtime yet
    // Abstraction likely to be part of ASP.NET Core
    public abstract class AggregatedResourceLimiter<TKey>
    {
        // an inaccurate view of resources
        abstract long EstimatedCount(TKey resourceID);

        // Fast synchronous attempt to acquire resources
        // Set requestedCount to 0 to get whether resource limit has been reached
        abstract bool TryAcquire(TKey resourceID, long requestedCount, out Resource resource);

        // Wait until the requested resources are available
        // Set requestedCount to 0 to wait until resource is replenished
        // If unsuccessful, throw
        abstract ValueTask<Resource> AcquireAsync(TKey resourceID, long requestedCount, CancellationToken cancellationToken = default);
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
```

The `Resource` struct is designed to handle the releasing of the resource. This way, the user won't be able to release more resources than was obtained, unlike Semaphores. We've also identified scenarios where additional information need to be returned by the limiter, such as reason phrases, response codes, percentage of rate saturation, retry after etc. For these cases, the object `Resource.State` can be used to store the additional metadata.

The motivation behind the `TryAcquire` and `AcquireAsync` distinction is that there are use cases where a fast synchronous check is required and there are other cases where waiting until a resource is available is better suited. There are parallels for this in System.Threading.Channels with ChannelWriter.TryWrite vs ChannelWriter.WriteAsync. We envision `AcquireAsync` to also allow for waiting for pending acquisition requests (FIFO or LIFO) when resources are exhausted. In these cases, the behaviour is controlled with two limits:

1. Resource limit: this represents how many resource are available to be acquired immediately
2. Queue/Stack limit: this represents how many requests can be queued

This gives 3 scenarios :

1. Count + RequestedCount <= Resource limit: In this case `TryAcquire` will return `true` and `AcquireAsync` will return immediately
2. Resource limit < Count + RequestedCount <= Resource limit + Queue/Stack limit: In this case `TryAcquire` will return `false` and `AcquireAsync` will wait in a queue.
3. Count + RequestedCount > Resource limit + Queue/Stack limit: In this case `TryAcquire` will return `false` and `AcquireAsync` will throw an exception immediately.

In case we want to reduce the allocation of an `Exception` type in scenario 3, we can return a `bool` either as a tuple for `AcquireAsync`, i.e. `ValueTask<(bool, Resource)> AcquireAsync(long requestedCount, CancellationToken cancellationToken = default)`, or add a `bool` to the `Resource` struct.

The usage pattern in the former could be construed as more verbose:
```c#
(bool successful, Resource resource) = await AcquireAsync(1);
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

In the latter case, the `TryAcquire` method would probably be changed to `Resource TryAcquire(long requestedCount)` which doesn't match with the general pattern of `Try...` methods returning a bool.

The reason there are separate abstraction for simple and complex resources is to support different use scenarios:

1. For complex resources such as rate limit by IP, we don't want to have a rate limit per bucket (i.e. one rate limiter per remote IP). As such, we need an API where you can pass in a resourceID.
2. For simpler scenarios where a key is not necessary, such as a n requests/second limit, requiring a default key to be passed in becomes awkward. Hence a simpler API is preferred.

We plan on adding extension methods:

```c#

    public static class ResourceLimiterExtensions
    {
        public static bool TryAcquire(this ResourceLimiter limiter, out Resource resource)
        {
            return limiter.TryAcquire(1, out resource);
        }

        public static ValueTask<Resource> AcquireAsync(this ResourceLimiter limiter, CancellationToken cancellationToken = default)
        {
            return limiter.AcquireAsync(1, cancellationToken);
        }
        public static bool TryAcquire<TKey>(this AggregatedResourceLimiter<TKey> limiter, TKey resourceId, out Resource resource)
        {
            return limiter.TryAcquire(resourceId, 1, out resource);
        }

        public static ValueTask<Resource> AcquireAsync<TKey>(this AggregatedResourceLimiter<TKey> limiter, TKey resourceId, CancellationToken cancellationToken = default)
        {
            return limiter.AcquireAsync(resourceId, 1, cancellationToken);
        }
    }
```

Usage may look like:

```c#
endpoints.MapGet("/tryAcquire", async context =>
{
    if (_limiter.TryAcquire(out var resource))
    {
        using (resource)
        {
            await context.Response.WriteAsync("Hello World!");
        }
    }
    else
    {
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        return;
    }
}

endpoints.MapGet("/acquireAsync", async context =>
{
    try
    {
        using (await _limiter.AcquireAsync())
        {
            await context.Response.WriteAsync("Hello World!");
        }
    }
    catch (ResourceExhaustedException)
    {
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        return;
    }
}
```
### Reference Designs

Reference designs are redacted but can be found in https://github.com/aspnet/specs/tree/main/design-notes/ratelimit.

## Open questions

- Currently trying to keep the API as simple as possible.
  - Current design does not allow for partial acquisition, either all of the request count is acquired or nothing. The intent is to examine the estimated count if an appropriate amount to be passed to TryAcquire or Acquire async is desired.
- If there's a need to check the rate limit state, for example, if a softcap has been reached and waiting is likely to occur, call TryAcquire(0). Alternatively, await AcquireAsync(0) to wait until resource has been refreshed/released.
- There are many parallels with the API surface of Sempahore/Semphore slim. Is the overlap problematic?
- Naming seems to be highly controversial

<details>

## Related concepts

This portion covers higher level concepts of how the resource limits could be used in building services/middlewares and additional extensibility. These concepts will not be shipped in dotnet/runtime.

## Terms

### Implementation Layer

The different levels of the library hierarchy. Currently consists of Core, Rule Engine and Consumption layers.

### Core layer

This layer contains the core abstractions for rate limit concerns including the rate limiter itself and the interface for rate limit count storage.

This layer is planned to be part of the BCL and will likely include some default implementations (sliding window, fixed window, token bucket, etc) in-box. Implementations of the rate limit count store will likely not live in this layer.

### Rule Engine layer

This layer contains the functionalities that matches a request to the underlying rate limiters. This layer consists of the concepts such as policies, rules, and configuration.

This layer will be implemented using primitives from the Core Layer. Implementation examples include a new Rate Limit Middleware in ASP.NET Core, ACR, ATS and OneAccess.

### Consumption layer

This layer represent the user code. For example, an ASP.NET Core Web app or a service on Azure.

In simpler cases, this layer can directly use the Core layer, for example where the user wants to define a rate limit for a particular channel. In more complex scenarios, the user can opt into the more feature rich Rule Engine layer.

## Interactions between components

![Interactions between components](RateLimit.png "Interactions between components")

## Resource limit count store

### Role

This component's main responsibility is to store the actual count of the resouces. This component should allow for local storage such as an in-memory cache, or a remote storage such as redis. For remote storage, this component will likely need a local cache of the remote count also needs to account for the balance between optimism (i.e. speed) vs coherency (i.e. accuracy). The intention here is to have one store per storage type, for example, all limiters will share the same RedisRateLimitCounterStore instance.

### Extensibility

The user can configure different store implementation of the proposed interface to choose between different stores or synchronization strategies. For example, the user may choose to use a MoreOptimisticRedis store for FooLimiter(s) and a MoreCoherentRedis store for BarLimiter(s).

### Implementation Layer

This will be dependent on the IResourceLimiter implementation and will not be part of the the Core Layer.

### Reference Designs

Reference designs are redacted but can be found in https://github.com/aspnet/specs/tree/main/design-notes/ratelimit.

There is a potnetial to return results in addition to long in case additional metadata is stored in the count store. However, it's unclear at this point whether such use cases should be represented in the abstraction.

The reason all methods are sync instead of async, even in cases for remote stores such as Redis, is that we want to guide users into caching results for remote stores. The tradeoff is that implementers may simply write sync over async code.

To align with complex rate limiters, the type of `resourceID` is controlled via the type parameter `TKey`.

### Open discussions

Lack of async API might be unintuitive.

## Rate limit policy

### Role

The responsibilities of this component will include managing the settings for rate limits. this will likely entail initial resource counts, soft caps, hard caps, throttling levels, and replentishment parameters (frequency and amount) for self-renewing resources. If the values for these policies are to be controlled via an external source such as Configuration, this component must be set up to periodically update settings when the configuration source updates.

### Extensibility

I think the extensibility points will depend on the rule engine. Potential extensibility include whether the component should be driven by an external configuration source.

Given that this component will need to support being easily driven by configuration, it should be a simple POCO containing configuration values. I imagine that based on the rule engine there may be a set of common settings that are defined on a base `RateLimitPolicy` and depending on the limiter used, it may use a `SlidingWindowRateLimitPolicy` that extends `RateLimitPolicy`.

### Implementation layer

This is a concern of the rate limit rule engine which is above the Core layer.

### Reference Designs

Reference designs are redacted but can be found in https://github.com/aspnet/specs/tree/main/design-notes/ratelimit.

### API prototype

In ASP.NET Core, this will integrate with the routing system. Metadata referring to which limiter applies will be added to endpoints via attributes and/or extension methods on `IEndpointConventionBuilder`, etc.

### Open discussions

## Rate limit policy rule

### Role

The responsibilities of this component will entail the settings for rules that matches requests to individual rate limit policies. This may include specifying which types of requests the limits are applicable or how requests are to be bucketized. This component also may need to occasionally update if its rules are to be controlled via an external configuration source.

### Extensibility

I think the extensibility points will depend on the rule engine. Potential extensibility include whether the component should be driven by an external configuration source.

### Implementation layer

This is a concern of the rate limit rule engine which is above the Core layer.

### Reference Designs

Reference designs are redacted but can be found in https://github.com/aspnet/specs/tree/main/design-notes/ratelimit.

### API prototype

In ASP.NET Core, this concept will be represented by the configuration of endpoints. Dynamically configurable rate limiters will need to be applied to all endpoints and evaluation of applicability checked in the endpoint aware middleware.

### Open discussions

## Configuration and management of policies

### Role

This component handles the management of policies defined by the previous component. Specifically, it should maintain a collection of active policies as defined in code or via a configuration source. For an incoming request, this component will be queried to obtain the relevant rate limits and potentially have additional functionality to try acquiring them.

### Extensibility

TBD

### Implementation layer

This is a concern of the Rule Engine. This component is likely to be closely coupled to the rate limit policies.

### Reference Designs

Reference designs are redacted but can be found in https://github.com/aspnet/specs/tree/main/design-notes/ratelimit.

### API prototype

In ASP.NET Core, some of the settings (e.g. values on the policies) will be driven through configuration where as others (likely the policy rules) will be configured via endpoints and attributes.

### Open discussions

## Diagnostics

### Role

The idea here is to provide logs and diagnostic information to extract information such as what resources are throttled, how often resources are requested, etc.

### Extensibility

This will depend on what frameworks/extensions are used. For example M.E.Logging already allows configuration of different logging sinks.

### Implementation layer

This is a higher level concern and should be implemented in the layer where the Rate Limiter API is consumed. Additional logging could also be added in the implementation of the rate limit count storage.

### Reference Designs

Reference designs are redacted but can be found in https://github.com/aspnet/specs/tree/main/design-notes/ratelimit.

In ATS, here's a document of how to work with metrics and logs: https://eng.ms/docs/products/azure-common-building-blocks/azure-throttling-solution/reference/metrics-and-logs.

### API prototype

We'll have logs and diagnostics in the rate limit middleware.

### Open discussions

</details>