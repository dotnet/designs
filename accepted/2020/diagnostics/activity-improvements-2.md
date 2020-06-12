# Improve Activity API usability and OpenTelemetry integration (Part 2)

We have exposed some [Activity APIs to improve the API usability and to integrate with OpenTelemetry](https://github.com/dotnet/designs/pull/98) (OT) so Activity can be a suitable replacement for the OT Span type described by the [OT Specs](https://github.com/open-telemetry/opentelemetry-specification/blob/master/specification/trace/api.md#span). We have received feedback regarding some OT blocked scenarios which need to be addressed in the Activity APIs and unblock the OT SDK implementation.

This document is listing the proposed changes and the rationale for the proposal. **As we still receiving more feedback and there are some issues under discussion, it is expected this documents will get updated before we do the full design review for the proposed changes.**

## ActivityContext.IsRemote

We already exposed the ActivityContext type as described by the OT specs. There is a property inside this type called `IsRemote` which we didn't include in the first place because it was under discussion with the OT spec committee. It has been confirmed this property is needed and will stay as part of the OT specs.

[ActivityContext.IsRemote](https://github.com/open-telemetry/opentelemetry-specification/blob/master/specification/trace/api.md#spancontext) is a Boolean flag which returns true if the ActivityContext was propagated from a remote parent. When creating children from remote spans, their IsRemote flag MUST be set to false.

```c#
    public readonly partial struct ActivityContext : IEquatable<ActivityContext>
    {
        // This constructor already exist but we are adding extra defaulted parameter for isRemote.
        public ActivityContext(ActivityTraceId traceId, ActivitySpanId spanId, ActivityTraceFlags traceFlags, string? traceState = null, isRemote = false) {...}

        // This is the new property
        public bool IsRemote { get; }
    }
```

Activity class will need to expose such property too to be able to set it as part of the included context. This is needed by the OT asp.net and Http client OT adapters. The adapters listen to the Activities created old way using `new Activity(...)` as it happen in asp.net and Http client. That means such activities will never set IsRemote correctly as it doesn't know anything about it. The adapters will need detect if such activity created from remote parent and set this value accordingly.

```c#
    public partial class Activity : IDisposable
    {
        public bool IsRemote { get; set; }
    }
```

## Activity.Kind Setter

Activity.Kind is already exposed property but we have exposed it as a read-only property. We got OT adapters scenario that listen to the old created activities (by asp.net and Http clients). As the old Activities didn't support Kind property, the adapter needs to set this property before sending the activity to the sampling in the OT SDK. The proposal here is to expose the setter of this property.

```c#
    public partial class Activity : IDisposable
    {
        // The setter was private and now we are making it public
        public ActivityKind Kind { get; set; } = ActivityKind.Internal;
    }
```

## ActivityTagsCollection

OT has the concept of [Attributes](https://github.com/open-telemetry/opentelemetry-specification/blob/master/specification/trace/api.md#set-attributes) which is a list of key-value pairs mapping a string to some value. The value can be numeric, bool, string types. Also, the value can be an array of one of these basic types.

> ```json
>         'x-forwarded-for': 'foo.bar'
>         'timeout': 5
> ```

There is OT proposal tracked by the [PR](https://github.com/open-telemetry/opentelemetry-specification/pull/596) to support the nested attributes. If this get accepted in OT, it will be easy to support it using ActivityTagsCollection.

> ```json
> 'http': {
>     'headers': [
>         'x-forwarded-for': 'foo.bar'
>         'keep-alive': 'timeout=5'
>     ]
> }
> ```

The attributes are used in multiple places in the OT (e.g. in Span, Event, and Link). Activity support Tags which similar concept but Tags is limited to string value types and not supporting any other types `IEnumerable<KeyValuePair<string, string?>> Tags`.  We'll need to extend the Tags concept in our APIs to support the OT scenarios. We'll do that by introducing the ActivityTagsCollection and extending Activity class to support adding different types of tags value.

ActivityTagsCollection is needed for the following reasons:

- We need to avoid having the consumers of our APIs to use any other types (e.g. Dictionary<>, List<>...etc.) which can cause issues like not keeping the items in order or not conforming to the OT specs which suggest specific behaviors (e.g. when adding null value, should remove the entry from the list)
- It is currently proposed to support nested attributes. ActivityTagsCollection will make it easy to do so at anytime is needed.

```c#
    public class ActivityTagsCollection : IReadOnlyCollection<KeyValuePair<string, object>>
    {
        public ActivityTagsCollection() { }
        public ActivityTagsCollection(IEnumerable<KeyValuePair<string, object>>) { }

        public void Add(string key, int value) { }
        public void Add(string key, double value) { }
        public void Add(string key, bool value) { }
        public void Add(string key, string value) { }
        public void Add(string key, int [] value) { }
        public void Add(string key, double [] value) { }
        public void Add(string key, bool [] value) { }
        public void Add(string key, string [] value) { }

        // The following API is pending the approval in OT spec first before we add it in .NET.
        public void Add(string key, ActivityTagsCollection value) { }

        // interfaces implementation
        public int Count { get; }
        public IEnumerator<KeyValuePair<string, object>> GetEnumerator() { ... }
        IEnumerator IEnumerable.GetEnumerator() { ... }
    }
```

## Activity.Tags

As mentioned earlier, Activity has Tags property which returns the list of the activity tags. Tags are a list of `KeyValuePair<string, string>`. Tags are proven to be not enough for OT scenarios as we need to support more value types (numeric, bool, numeric array, and nested tags). Activity today has the method `public Activity AddTag(string key, string? value)`. The proposal here is to add more `AddTag` overload accepting more types and add a new getter property return the list of all typed tags.

```C#
    public partial class Activity : IDisposable
    {
        public void AddTag(string key, int value) { }
        public void AddTag(string key, double value) { }
        public void AddTag(string key, bool value) { }
        public void AddTag(string key, int [] value) { }
        public void AddTag(string key, double [] value) { }
        public void AddTag(string key, bool [] value) { } // how interesting this one?
        public void AddTag(string key, string [] value) { }

        // The following API is pending the approval in OT spec first before we add it in .NET.
        public void AddTag(string key, ActivityTagsCollection value) { }

        // We cannot change Tags property so we are introducing a new property with a new name
        public ActivityTagsCollection TypedTags { get; }
    }
```

***<u>OpenQuestion</u>***

We need to decide about the behavior of the old property `Activity.Tags` when adding tags of non-string types values. The options are

- Ignore all non-string typed values and return only the string type value tags.
- Convert the non-string type values to string before returning it.

I am inclining to the first option to avoid handling ToString with more complex types (e.g. arrays or ActivityTagsCollection).

## Proposal Changing already exposed APIs in previous preview releases

As we are introducing the ActivityTagsCollection, we can take advantage of that now and modify some of the exposed APIs to use this collection instead. Also, we are renaming Attributes to Tags for consistency inside BCL APIs.

### ActivityEvent

```C#
    // Already exposed APIs
    public readonly struct ActivityEvent
    {
        public ActivityEvent(string name, IEnumerable<KeyValuePair<string, object>>? attributes)
        public ActivityEvent(string name, DateTimeOffset timestamp, IEnumerable<KeyValuePair<string, object>>? attributes)
        public IEnumerable<KeyValuePair<string, object>> Attributes { get; }
    }

    // change it to the following:
    public readonly struct ActivityEvent
    {
        public ActivityEvent(string name, ActivityTagsCollection? tags)
        public ActivityEvent(string name, DateTimeOffset timestamp, ActivityTagsCollection? tags)
        public ActivityTagsCollection? Tags { get; }
    }
```

### ActivityLink

```C#
    // Already exposed APIs
    public readonly partial struct ActivityLink  : IEquatable<ActivityLink>
    {
        public ActivityLink(ActivityContext context, IEnumerable<KeyValuePair<string, object>>? attributes)
        public IEnumerable<KeyValuePair<string, object>>? Attributes { get; }
    }

    // change it to the following:
    public readonly partial struct ActivityLink  : IEquatable<ActivityLink>
    {
        public ActivityLink(ActivityContext context, ActivityTagsCollection? tags)
        public ActivityTagsCollection? Tags { get; }
    }
```

## Automatic Trace Id  generation in case of null parent

Sampler like probabilistic sampler can depend on the trace id for the sampling decision. Usually before creating any Activity using ActivitySource, the `ActivityListener.GetRequestedDataUsingContext` callback will get called which usually get implemented by the samplers. The parent context is sent as a parameter to this callback and the sampler gets the trace id from this parent context to perform the sampling action. When there is no parent, we send the default context which is nulling the trace id. The samplers depending on trace id are not going to be able to do the sampling at that time. The samplers even cannot generate and use a trace id because it cannot inform the Activity creation process to use the generated trace Id when creating the Activity object.
There are a couple of proposals how to address this issue in the .NET runtime. The idea is the runtime need to generate the trace Id and send it to the samplers. If the sampler using the passed trace id and decide to sample in this case, the Activity which will get created because of that sampler decision will be using the generated trace Id in its context.

Solution options:

- Before calling `ActivityListener.GetRequestedDataUsingContext`, there will be a check if we don't have a parent, then we generate a random trace Id and use it in the context sent to the callback. If decided to sample in, we all use this trace Id during the Activity object creation. This option doesn't need any API changes and will be fully handled inside the implementation.
- Add a new property to the `ActivityCreationOptions` struct (e.g. `NullParentTraceId`) which will carry the trace id that can be used by the sampler when detecting no parent context. obviously this will need API change.

I am inclining to the first solution for the following reasons:

- Exposing extra property can confuse the API consumers to know exactly when this property is needed or even they should care about it.
- Adding such property to `ActivityCreationOptions` will increase the size of this struct which we are trying to make it compact for perf reasons.
- I am not seeing a strong reason why the first solution wouldn't be good enough.
