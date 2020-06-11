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
        public ActivityContext(ActivityTraceId traceId, ActivitySpanId spanId, ActivityTraceFlags traceFlags, string? traceState = null, isRomte = false) {...}
    
        // This is the new property 
        public bool IsRemote { get; }
    }
```

Activity class will need to expose such property too to be able to set it as part of the included context. This is needed by the OT asp.net and Http client OT adapters.

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

OT has the concept of [Attributes](https://github.com/open-telemetry/opentelemetry-specification/blob/master/specification/trace/api.md#set-attributes) which is a list of key-value pairs mapping a string to some value. the value can be numeric, bool, string types. Also, the value can be an array of one of these basic types. 

> ```json
>         'x-forwarded-for': 'foo.bar'
>         'timeout': 5
> ```

The attributes can be nested too. There is opened [OT spec PR](https://github.com/open-telemetry/opentelemetry-specification/pull/596) for supporting this scenario.

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
- Need to support the nested attributes. ActivityTagsCollection will make it easy to do so.

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
        public void AddTag(string key, ActivityTagsCollection value) { }

        // We cannot change Tags property so we are introducing a new property with a new name
        public ActivityTagsCollection TypedTags { get; }
    }
```

## Proposal Changing already exposed APIs in previous preview releases

As we are introducing the ActivityTagsCollection, we can take advantage of that now and modify some of the exposed APIs to use this collection instead. 

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
        public ActivityEvent(string name, ActivityTagsCollection? attributes)
        public ActivityEvent(string name, DateTimeOffset timestamp, ActivityTagsCollection? attributes)
        public ActivityTagsCollection? Attributes { get; }
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
        public ActivityLink(ActivityContext context, ActivityTagsCollection? attributes)
        public ActivityTagsCollection? Attributes { get; }
    }
```