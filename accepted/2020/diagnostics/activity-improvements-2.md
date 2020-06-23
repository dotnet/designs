# Improve Activity API usability and OpenTelemetry integration (Part 2)

We have exposed some [Activity APIs to improve the API usability and to integrate with OpenTelemetry](https://github.com/dotnet/designs/pull/98) (OT) so Activity can be a suitable replacement for the OT Span type described by the [OT Specs](https://github.com/open-telemetry/opentelemetry-specification/blob/master/specification/trace/api.md#span). We have received feedback regarding some OT blocked scenarios which need to be addressed in the Activity APIs and unblock the OT SDK implementation.

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

## Activity.Kind Setter

Activity.Kind is already exposed property but we have exposed it as a read-only property. We got OT adapters scenario that listen to the old created activities (by asp.net and Http clients). As the old Activities didn't support Kind property, the adapter needs to set this property before sending the activity to the sampling in the OT SDK. The proposal here is to expose the setter of this property.

```c#
    public partial class Activity : IDisposable
    {
        // The setter was private and now we are making it public
        public ActivityKind Kind { get; set; } = ActivityKind.Internal;
    }
```

## ActivityAttributesCollection

OT has the concept of [Attributes](https://github.com/open-telemetry/opentelemetry-specification/blob/master/specification/trace/api.md#set-attributes) which is a list of key-value pairs mapping a string to some value. The value can be of different types (e.g. numeric, bool, string, arrays...etc.). 

> ```json
>      'x-forwarded-for': 'foo.bar'
>      'timeout': 5
> ```

The attributes are used in multiple places in the OT (e.g. in Span, Event, and Link). Activity support Tags which similar concept but Tags is limited to string value types and not supporting any other types `IEnumerable<KeyValuePair<string, string?>> Tags`.  We'll need to extend the Tags concept in our APIs to support the OT scenarios. We'll do that by introducing the ActivityAttributesCollection and extending Activity class to support adding different types of attribute values.

ActivityAttributesCollection is needed to ensure the OT specs behavior is implemented and avoid API users to use different type of collections (e.g. Dictionary<>, List<>...etc.) which not conforming with the OT specs. The OT specs require the attributes always stored in order and define behavior when adding values for keys which already existed in the list, it will override the old value. In addition it define the behavior what should happen when using null values.

```c#
    public class ActivityAttributesCollection : IReadOnlyCollection<KeyValuePair<string, object>>
    {
        public ActivityAttributesCollection() { }
        public ActivityAttributesCollection(IEnumerable<KeyValuePair<string, object>>) { }

        // If the key was not set before, it will get added to the collections with the input value.
        // If the key was set before, the mapped value of this key will be updated with the new input value.
        public void Set(string key, int value) { }

        // interfaces implementation
        public int Count { get; }
        public IEnumerator<KeyValuePair<string, object>> GetEnumerator() { ... }
        IEnumerator IEnumerable.GetEnumerator() { ... }
    }
```

## Activity Attributes

As mentioned earlier, Activity has Tags property which returns the list of the activity tags. Tags are a list of `KeyValuePair<string, string>`. Tags are proven to be not enough for OT scenarios as we need to support more value types (numeric, bool, arrays...etc.). Activity today has the method `public Activity AddTag(string key, string? value)`. The proposal here is to add `SetAttribute()` which can set attributes value for a string key. 

In general, Attributes will be considered a superset of Tags. 

```C#
    public partial class Activity : IDisposable
    {
        public void SetAttribute(string key, object value) { }
        
        public ActivityAttributesCollection Attributes { get; }
    }
```

`Activity.Tags` behavior is not going to change and will always return the list of tags/attributes which has string typed values only. while the new API `Activity.Attributes' is going to return all attributes with all different type values.

## Proposal Changing already exposed APIs in previous preview releases

As we are introducing the ActivityAttributesCollection, we can take advantage of that now and modify some of the exposed APIs to use this collection instead. Also, we are renaming Attributes to Tags for consistency inside BCL APIs.

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
        public ActivityEvent(string name, ActivityAttributesCollection? tags)
        public ActivityEvent(string name, DateTimeOffset timestamp, ActivityAttributesCollection? tags)
        public ActivityAttributesCollection? Attributes { get; }
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
        public ActivityLink(ActivityContext context, ActivityAttributesCollection? tags)
        public ActivityAttributesCollection? Attributes { get; }
    }
```

## Handle ActivitySource.StartActivity(..., string parentId,....) case

When API users call `ActivitySource.StartActivity(..., string parentId,....) `, we eventually call all listeners callback `ActivityListener.GetRequestedDataUsingParentId` and we pass the parent Id string to that callback. parent Id string can be a regular string or it can be constructed from the parent context (trace Id, span Id, and the trace flags). The feedback we got from OT is this design will require the implementers to try to extract the context from the parent Id. if the context cannot be extracted, the passed information will be kind not useful for the OT scenarios (the parent Id can be useful in other scenarios though). That is mean the burden will be on the listener implementer and will require them to implement `ActivityListener.GetRequestedDataUsingParentId` while not be used in some cases when cannot extract the context. 

The proposed change here is we'll have `ActivitySource.StartActivity(..., string parentId,....)` try to extract the context from the input parent Id. If it succeed to do that, it will call the listener callback `ActivityListener.GetRequestedDataUsingContex` instead and `ActivityListener.GetRequestedDataUsingParentId` will not get called. If it fail to construct the context, it will continue calling `ActivityListener.GetRequestedDataUsingParentId` if the listener implemented it. Listener implementers now have the choice to implement `ActivityListener.GetRequestedDataUsingParentId` only if they cares about parent Ids that don't include context information. 

There is no APIs change here as all design change is more on the internal implementation details. 

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
