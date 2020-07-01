# Improve Activity API usability and OpenTelemetry integration (Part 2)

We have exposed [Activity APIs to improve the API usability and to integrate with OpenTelemetry](https://github.com/dotnet/designs/pull/98) (OT) to have `Activity` be suitable replacement for the OT Span type described by the [OT Specs](https://github.com/open-telemetry/opentelemetry-specification/blob/master/specification/trace/api.md#span). We have received feedback regarding some OT blocked scenarios which need to be addressed in the Activity APIs and unblock the OT SDK implementation.

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

## Activity Attributes

OT has the concept of [Attributes](https://github.com/open-telemetry/opentelemetry-specification/blob/master/specification/trace/api.md#set-attributes) which is a list of key-value pairs mapping a string to some value. The value can be of different types (e.g. numeric, bool, string, arrays...etc.). 

> ```json
>   'x-forwarded-for': 'foo.bar'
>   'timeout': 5
> ```

The attributes are used in multiple places in the OT (e.g. in Span, Event, and Link). Activity support Tags which similar concept but Tags is limited to string value types and not supporting any other types `IEnumerable<KeyValuePair<string, string?>> Tags`.  Tags are proven to be not enough for OT scenarios as we need to support more value types (numeric, bool, arrays...etc.). Activity today has the method `public Activity AddTag(string key, string? value)`. The proposal here is to add `SetAttribute()` which can set attributes value for a string key. 

In general, Attributes will be considered a superset of Tags. 

```C#
    public partial class Activity : IDisposable
    {
        public void SetAttribute(string key, object value) { }
        
        public IEnumerator<KeyValuePair<string, object>> Attributes { get; }
    }
```

`Activity.Tags` behavior is not going to change and will always return the list of tags/attributes which has string typed values only. while the new API `Activity.Attributes' is going to return all attributes with all different type values.

## ActivityAttributesCollection

As discussed in previous section OT has the concept of `Attributes` which we proposed supporting it in `Activity` class.  `Attributes` is used in some other types in OT too, `ActivityEvent` and `ActivityLink`. The users of these types will need to create the Attributes collection and pass it to the type constructors. 

OT define some specific behavior for the `Attributes` collection: 

- The collection items has to be ordered as it get added. 
- Adding item which has a key equal to a previously added item's key, will update the value of the existing item. in other word, the collections shouldn't include 2 items with same key.
- Adding item with `null` value will delete the item which has key equal to the input item's key.

Obviously if we didn't provide a collection behaving as the OT spec require, every user of our APIs will need to implement this behavior manually. Or will use other collection (e.g. `Dictionary<string, object>` or `List<T>`) and at that time the behavior will be wrong and we can run into problems when not matching OT specs.

The proposal here is to provide ActivityAttributesCollection which will help ensuring the OT behavior and make it easy for the users to handle the `Attributes` in general. 

```c#
    public class ActivityAttributesCollection : IReadOnlyCollection<KeyValuePair<string, object>>
    {
        public ActivityAttributesCollection() { }
        public ActivityAttributesCollection(IEnumerable<KeyValuePair<string, object>> attributes) { }

        // If the key was not set before, it will get added to the collections with the input value.
        // If the key was set before, the mapped value of this key will be updated with the new input value.
        public void Set(string key, object value) { }

        // interfaces implementation
        public int Count { get; }
        public IEnumerator<KeyValuePair<string, object>> GetEnumerator() { ... }
        IEnumerator IEnumerable.GetEnumerator() { ... }
    }
```

## Proposal Changing already exposed APIs in previous preview releases

As we are introducing the ActivityAttributesCollection, we can now modify some of the exposed APIs to use this collection instead.

### ActivityEvent

```C#
    // Already exposed APIs
    public readonly struct ActivityEvent
    {
        public ActivityEvent(string name, IEnumerable<KeyValuePair<string, object>>? attributes)
        public ActivityEvent(string name, DateTimeOffset timestamp, IEnumerable<KeyValuePair<string, object>>? attributes)
    }

    // change it to the following:
    public readonly struct ActivityEvent
    {
        public ActivityEvent(string name, ActivityAttributesCollection? attributes)
        public ActivityEvent(string name, DateTimeOffset timestamp, ActivityAttributesCollection? attributes)
    }
```

### ActivityLink

```C#
    // Already exposed APIs
    public readonly partial struct ActivityLink  : IEquatable<ActivityLink>
    {
        public ActivityLink(ActivityContext context, IEnumerable<KeyValuePair<string, object>>? attributes)
    }

    // change it to the following:
    public readonly partial struct ActivityLink  : IEquatable<ActivityLink>
    {
        public ActivityLink(ActivityContext context, ActivityAttributesCollection? attributes)
    }
```

## Automatic Trace Id  generation in case of null parent

Sampler like probabilistic sampler can depend on the trace id for the sampling decision. Usually before creating any Activity using ActivitySource, the `ActivityListener.GetRequestedDataUsingContext` callback will get called which usually get implemented by the samplers. The parent context is sent as a parameter to this callback and the sampler gets the trace id from this parent context to perform the sampling action. When there is no parent, we send the default context which is nulling the trace id. The samplers depending on trace id are not going to be able to do the sampling at that time. The samplers even cannot generate and use a trace id because it cannot inform the Activity creation process to use the generated trace Id when creating the Activity object.
There are a couple of proposals how to address this issue in the .NET runtime. The idea is the runtime need to generate the trace Id and send it to the samplers. If the sampler using the passed trace id and decide to sample in this case, the Activity which will get created because of that sampler decision will be using the generated trace Id in its context.

The proposal here is to expose the `ActivityListener` property

```c#
    public sealed class ActivityListener : IDisposable // already existing type
    {
        public bool PregenerateNewRootId { get; set;} // other names suggestion: GenerateTraceIdForRootContext, GenerateTraceIdForNullParent 
    }
```

Before calling the listener, if we have a null parent context and this new property is set to true, we'll generate a trace Id and set it in the parent context send it to the listener. later if decided to create a new `Activity` object as a result of listener response, we'll use the generated trace id with the new created `Activity` object.

We are making this as opt-in option in the listener to avoid any performance issue with the default scenarios which not caring about getting the parent trace Id when having null parent. Generating the trace id will require some memory allocation.

## Handle ActivitySource.StartActivity(..., string parentId,....) case

***<u>This is implementation details and no APIs change. can safely ignored by the design reviewer</u>***

When API users call `ActivitySource.StartActivity(..., string parentId,....) `, we eventually call all listeners callback `ActivityListener.GetRequestedDataUsingParentId` and we pass the parent Id string to that callback. parent Id string can be a regular string or it can be constructed from the parent context (trace Id, span Id, and the trace flags). The feedback we got from OT is this design will require the implementers to try to extract the context from the parent Id. If the context cannot be extracted, the passed information will be kind not useful for the OT scenarios (the parent Id can be useful in other scenarios though). That is mean the burden will be on the listener implementer and will require them to implement `ActivityListener.GetRequestedDataUsingParentId` while not be used in some cases when cannot extract the context. 

The proposed change here is when calling `ActivitySource.StartActivity(..., string parentId,....)` and the listener implementing the callback `ActivityListener.GetRequestedDataUsingParentId`, we'll call this callback as we do today as the listener showing the intend to receive and handle the Parent Id. If the listener didn't implement  `ActivityListener.GetRequestedDataUsingParentId` and implementing `ActivityListener.GetRequestedDataUsingContex` then we'll try convert the parent Id to context. If the conversion succeed well call `ActivityListener.GetRequestedDataUsingContex` otherwise we'll not call any listeners callbacks.
There is no APIs change here as all design change is more on the internal implementation details. 
