 # Improve Activity API usability and OpenTelemetry integration

[Github issue](https://github.com/dotnet/runtime/issues/31373) has past discussion.

**PM** @shirhatti, anksr

**Dev** @tarekgh

.NET has long had [System.Diagnostics.Activity](https://docs.microsoft.com/en-us/dotnet/api/system.diagnostics.activity?view=netcore-3.1) and [System.Diagnostics.DiagnosticListener](https://docs.microsoft.com/en-us/dotnet/api/system.diagnostics.diagnosticlistener?view=netcore-3.1) to support [distributed tracing](https://docs.microsoft.com/en-us/azure/azure-monitor/app/distributed-tracing) scenarios. Code that receives, processes, and transmits requests creates Activity objects which have correlation ids. These Activities are published via DiagnosticListener to telemetry monitoring agents such as Application Insights running in the same process. The telemetry agent can then log the information to a remote store where it can be aggregated and visualized to help developers understand the overall flow of work in a distributed system.

Today distributed tracing instrumentation is predominantly only added to a few critical libraries such as ASP.NET's Kestrel web server or the HttpClient implementation in the BCL. Even if other libraries or app developers wanted to add this instrumentation to their code, the current design makes it difficult - we want to fix that.  We have also been participating in the [OpenTelemetry](https://opentelemetry.io/) project which is defining a multi-language multi-vendor standard for how distributed tracing should work. Our goal is to ensure that .NET integrates smoothly. For app developers this means that you can add a reference to the OpenTelemetry NuGet package and all the Activity-based instrumentation in your codebase, your 3rd party libraries, and the runtime will light up and get collected. For library authors this means that you can write to a platform standard API that doesn't force any specific dependency on the library users, but lights up in a predictable way when OpenTelemetry (or any other agent) is present.

We expect to achieve this by additions to the Activity object model and a new way to listen to the Activity events (e.g. Start and Stop) which created from specific source or listen to all Activities created from all sources. These additions should provide stronger contracts between in-proc producers and consumers of distributed tracing data, a richer set of information that Activity can represent, and streamlined calling patterns for typical scenarios. It is also our mutual goal with the OT community to eliminate the current OT Telemetry Span type and use Activity as the .NET representation of OT's Span concept.

## Scenarios and User Experience

#### 1. A developer wants a portion of code to be annotated with a named block in a distributed trace Gantt chart

The developer needs to wrap their region of code in a using statement that creates the Activity and assigns it a name. It is possible that the activity reference will be null if there is nothing in the process listening for this telemetry or if only a sub-set of telemetry is being sampled.

```C#
    static ActivitySource source =  new ActivitySource("MyLibrary.Subcomponent", "1.0.0.0");
    ...
    using (Activity? activity = source.StartActivity("FooOperation")
    {
        // optionally the activity can be enriched with extra data
        activity?.AddTag("userId", userId);
        activity?.AddTag("fooParam", foo);

        // the developer's original code goes here
    }
```

### 2. Monitoring agent wants to listen to activities created from specific ActivitySource

A monitoring agent can create a listener which represents their subscription to activity callbacks like this.

```C#
ActivityListener listener = new ActivityListener
{
    ActivityStopped = (activity) => { ... },
    ActivityStarted = (activity) => { ... },
    ShouldListenTo = (activitySource) => ...,
    GetRequestedDataUsingParentId = (ref ActivityCreationOptions<string> options) => ...,
    GetRequestedDataUsingContext = (ref ActivityCreationOptions<ActivityContext> options) => ... 
};
ActivitySource.AddActivityListener(listener);

...
    
listener.Dispose();  
```

It is up to the listener to do extract information from the Activity object and respond to the callbacks however they like, probably by serializing a message to a remote service that will aggregate the telemetry.

[TODO]: We could flesh out what we expect it look like end-to-end to use this with OpenTelemetry and perhaps some UI? It would help newcomers understand but I don't think it is a blocking issue for the current set of collaborators.

Requirements:
=============

### Goals

#### 1. All Activities can be collected

The current design of DiagnosticListener and the conventions used by existing instrumented libraries expect the collection agent to have significant knowledge about each instrumented library. We need to make it possible for a general purpose collection agent to collect distributed trace telemetry despite having none of this advance knowledge. This also implies that any developer can add new Activity instrumentation to their app or library and the agent is capable or recording it automatically.

#### 2. Activity collection can be filtered with a predicate on name

We are assuming that most Activity information is desirable by default, but the monitoring agent should be able to filter out noisy or problematic instrumentation. The agent can efficiently decide if it wants to listen to Activities which created from specific source/Component (ActivitySource).

#### 3. Activity needs API representations of OpenTelemetry Span concepts

What .Net calls Activity, OpenTelemetry calls Span. The OT span can express a broader set of information than Activity currently can. We want to create parity so that developers who want to emit certain OpenTelemetry data points can still use Activity APIs to accomplish that.

#### 4. API usability

Some of the existing patterns of API usage are awkward and don't appeal to many developers. We'd like to simplify usage wherever possible so that the APIs feel more approachable to a broader set of .Net developers.

#### 5. Back compat

Any scenarios that currently work using Activity and/or DiagnosticListener must continue to work with no code changes. Also the performance for these scenarios can't meaningfully regress. Our new Activity listening mechanism should be faster than conventional listening.

### Non-goals

1. Creating interfaces so that custom implementations of Activity can be created (OpenTelemetry does this with Span)
2. Supporting isolated monitoring domains that allows one set of instrumentation producers and consumers to be segregated from others within the same process (OpenTelemetry can do this via multiple instances of the top-level Tracer object)
3. Matching OpenTelemetry specification naming or behaviors that would directly conflict with .Net's existing precedent for Activity. We'll do our best to enable the use-cases OpenTelemetry is trying to achieve via some other means in these cases.


Design
=================

[This is copied from Tarek's writeup in [the existing issue](https://github.com/dotnet/runtime/issues/31373) ]

## Rationale and Use Cases

The proposal here is mainly targeting closing the gap between the .NET Libraries and OpenTelemetry (OT) tracing APIs (mainly the OT Span class). We also enhance and simplify the usage of the Activity for easier code patterns. Here are the details of this proposal:

- .NET Libraries have the `Activity` class which the object allows libraries and apps to publish tracing information. OT currently under development and not released yet. OT currently proposing the `Span` class which mostly used for the same purpose of Activity. OT Span supports more properties than Activity which allows more tracing scenarios. This proposal is offering the additions needed to be added to the Activity class which helps to address the same scenarios that Span does and possibly allow OT to fully get rid of Span to reduce the confusion which developers can run into to decide if need to use Activity or use Span. We are introducing the following types and properties which will be used inside the Activity class:
  - ActivityContext which conforms to the w3c TraceContext specification. The context contains the trace Id, Span Id, Trace state and Trace flags.
  - ActivityLink which can point to ActivityContexts inside a single Trace or across different Traces. Links can be used to represent batched operations where Activity was initiated by multiple initiating Activities each representing a single incoming item being processed in the batch.
  - ActivityKind describes the relationship between the Activity, its parents, and its children in a Trace.
  - ActivityEvent to attach specific event to teh Activity. Events have a time associated with the moment when they are added to the Activity.
  - Allow attaching any object to the Activity object through the `Set/GetCustomProperty` for the sake of the extendability without manually sub-classing Activity or maintaining a dictionary mapping activity to the other objects. Examples of that, OT implementation still depends on Activity and maintains a table mapping from Activity to Span. Another example is some teams were working around the lack of missing Links and Contexts features and manually doing extra work to include these features. Although we are adding the Links and Contexts, we can get more future scenarios to extend Activity (e.g. attaching the status codes/exceptions).

- The current usage pattern of the Activity is not neat. here is an example of the current usage:

```C#
var activityListener = new DiagnosticSource("Azure.Core.Http");

// Outer check to see if anyone is subscribed
if (activityListener.IsEnabled())
{
    Activity activity = null;
    // Check if anyone cares about activity
    if (activityListener.IsEnabled("Azure.Core.Http.Request"))
    {
        activity = new Activity("Azure.Core.Http.Request");
        activity.AddTag..
        activityListener.StartActivity(activity); // this does string concat and allocates every time
    }

    ...

    if (activity != null)
    {
        activityListener.StopActivity(activity);
    }
}
```

Note in the pattern, the code trying to avoid creating the Activity object if there are no listeners interested to listen to such Activity creation. Also, the code later has to check if the activity object is created and then stop it.
We are simplifying the pattern to something like:

```C#
    static ActivitySource source =  new ActivitySource("Azure.Core.Http", "4.0.0.0");
    ...

    // StartActivity can return null if no listener is enabled. Activity now is Disposable too which automatically stop the activity if it is created and started.
    using (Activity? activity = source.StartActivity("Request"))
    {
        activity?.AddTag..
    }
```

- Last, we are introducing ActivitySource type which can be used to efficiently listening to Activities created from specific component/sources or all sources. It is now reasonable for a listener such as OpenTelemetry to observe Activity instrumentation from any code component in the app by the `ActivitySource.AddActivityListener()`. Previously OpenTelemetry would have needed appropriate  knowledge of the strings that would be used for DiagnosticSource name and IsEnabled() callbacks for each Activity it wanted to receive callbacks for. When trying to create a new Activity object through the new introduced factory methods `ActivitySource.StartActivity`, the listener will have the opportunity to get a callback to decide if interested in having the Activity object get created or it can be sampled out to avoid the object creation if nobody else is listening or other listeners are not interested in such activity too.
  When calling `ActivitySource.AddActivityListener()`, the provided callback `ListenToSource(activitySource)` will get called for every ActivitySource previously created and still active to decide if need to listen to Activities created from such ActivitySource. Also, this callback will get called for every future created ActivitySource.

Here is example of how to create a listener for Activities events.

```C#
ActivityListener listener = new ActivityListener
{
    ActivityStopped = (activity) => { ... },
    ActivityStarted = (activity) => { ... },
    ShouldListenTo = (activitySource) => ...,
    GetRequestedDataUsingParentId = (ref ActivityCreationOptions<string> options) => ...,
    GetRequestedDataUsingContext = (ref ActivityCreationOptions<ActivityContext> options) => ... 
};

ActivitySource.AddActivityListener(listener);

...
    
listener.Dispose();  
```

## Proposed API

### ActivityKind

```C#
namespace System.Diagnostics
{
    // Kind describes the relationship between the Activity, its parents, and its children in a Trace.
    public enum ActivityKind
    {
        // Indicates that the Activity represents an internal operation within an application, as opposed to an operation with remote parents or children.
        Internal = 0,

        /// Server activity represents request incoming from external component.
        Server = 1,

        /// Client activity represents outgoing request to the external component.
        Client = 2,

        /// Producer activity represents output provided to external components.
        Producer = 3,

        /// Consumer activity represents output received from an external component.
        Consumer = 4,
    }
}
```

 ### ActivityContext

```C#
namespace System.Diagnostics
{
    /// <summary>
    /// ActivityContext representation conforms to the w3c TraceContext specification. It contains two identifiers
    /// a TraceId and a SpanId - along with a set of common TraceFlags and system-specific TraceState values.
    /// </summary>
    public readonly struct ActivityContext : IEquatable<ActivityContext>
    {
        public ActivityContext(ActivityTraceId traceId, ActivitySpanId spanId, ActivityTraceFlags traceFlags, string? traceState = null);
        public ActivityTraceId TraceId { get; }
        public ActivitySpanId SpanId { get; }
        public ActivityTraceFlags TraceFlags { get; }
        public string? TraceState { get; }

        public static bool operator ==(ActivityContext left, ActivityContext right);
        public static bool operator !=(ActivityContext left, ActivityContext right) ;
        public bool Equals(ActivityContext value);
        public override bool Equals(object? obj);
        public override int GetHashCode();
    }
}
```

### ActivityEvent

```C#
namespace System.Diagnostics
{
    /// A text annotation associated with a collection of attributes.
    /// Event has name and optionally has timestamps and attributes
    public readonly struct ActivityEvent
    {
        /// Initializes a new instance of the <see cref="ActivityEvent"/> class.
        public ActivityEvent(string name) : this(name, DateTimeOffset.UtcNow, s_emptyAttributes);
        public ActivityEvent(string name, DateTimeOffset timestamp) : this(name, timestamp, s_emptyAttributes);
        public ActivityEvent(string name, IEnumerable<KeyValuePair<string, object>>? attributes);
        public ActivityEvent(string name, DateTimeOffset timestamp, IEnumerable<KeyValuePair<string, object>>? attributes);

        public string Name { get; }
        public DateTimeOffset Timestamp { get; }
        public IEnumerable<KeyValuePair<string, object>>? Attributes { get; }
    }
}
```

 ### ActivityLink

```C#
namespace System.Diagnostics
{
    /// Activity may be linked to zero or more other <see cref="ActivityContext"/> that are causally related.
    /// Links can point to ActivityContexts inside a single Trace or across different Traces.
    /// Links can be used to represent batched operations where a Activity was initiated by multiple initiating Activities,
    /// each representing a single incoming item being processed in the batch.
    public readonly struct ActivityLink : IEquatable<ActivityLink>
    {
        public ActivityLink(ActivityContext context, IEnumerable<KeyValuePair<string, object>>? attributes = null);
        public ActivityContext Context { get; }
        public IEnumerable<KeyValuePair<string, object>>? Attributes { get; }
        
        public override bool Equals(object? obj);
        public bool Equals(ActivityLink value);
        public static bool operator ==(ActivityLink left, ActivityLink right);
        public static bool operator !=(ActivityLink left, ActivityLink right);
        public override int GetHashCode();
    }
}
```

### ActivityDataRequest

```C#
namespace System.Diagnostics
{
    /// Used by ActivityListener to indicate what amount of data should be collected for this Activity
    /// Requesting more data causes greater performance overhead to collect it.
    public enum ActivityDataRequest
    {
        /// The Activity object doesn't need to be created
        None,

        /// The Activity object needs to be created. It will have Name, Source, Id and Baggage.
        /// Other properties are unnecessary and will be ignored by this listener.
        PropagationData,

        /// The activity object should be populated with all the propagation info and also all other
        /// properties such as Links, Tags, and Events. Activity.IsAllDataRequested will return true.
        AllData,

        /// The activity object should be populated the same as the AllData case and additionally
        /// Activity.IsRecorded is set true. For activities using W3C trace ids this sets a flag bit in the
        /// ID that will be propagated downstream requesting that trace is recorded everywhere.
        AllDataAndRecorded
    }
}
```

### ActivityCreationOptions

```c#
namespace System.Diagnostics
{
    public readonly struct ActivityCreationOptions<T>
    {
        public ActivityCreationOptions(ActivitySource source, string name, T parent, ActivityKind kind = ActivityKind.Internal, IEnumerable<KeyValuePair<string, string?>> tags = null, IEnumerable<ActivityLink> links = null)
        public ActivitySource Source { get; }
        public string Name { get; }
        public ActivityKind Kind { get; }
        public T Parent { get; }
        public IEnumerable<KeyValuePair<string, string?>> Tags { get; }
        public IEnumerable<ActivityLink> Links { get; }
    }
}
```

### ActivityListener

```C#
namespace System.Diagnostics
{
    public delegate ActivityDataRequest GetRequestedData<T>(ref ActivityCreationOptions<T> options);

    public sealed class ActivityListener : IDisposable
    {
        public ActivityListener() { }
        public Action<Activity> ActivityStopped { get; set; }
        public Action<Activity> ActivityStarted { get; set; }
        public Func<ActivitySource, bool> ShouldListenTo { get; set; }
        public GetRequestedData<string> GetRequestedDataUsingParentId { get; set; }
        public GetRequestedData<ActivityContext> GetRequestedDataUsingContext { get; set; }
        public void Dispose() { }
    }    
}
```

### ActivitySource

```C#
namespace System.Diagnostics
{
    /// ActivitySource is the type that help create and start Activity and allow listening to the Activity events too.
    public sealed class ActivitySource : IDisposable
    {
        /// Construct an ActivitySource object with the input name and component version
        public ActivitySource(string name, string version);

        /// Returns the ActivitySource name.
        public string Name { get; }

        /// Returns the ActivitySource version.
        public string Version { get; }

        /// Check if there is any listeners for this ActivitySource.
        /// This property can be helpful to tell if there is no listener, then no need to create Activity object
        /// and avoid creating the objects needed to create Activity (e.g. ActivityContext)
        /// Example of that is http scenario which can avoid reading the context data from the wire.
        public bool HasListeners();

        /// Creates a new Activity object if there is any listener to the Activity, returns null otherwise.
        public Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal);

        /// Creates a new <see cref="Activity"/> object if there is any listener to the Activity events, returns null otherwise.
        public Activity? StartActivity(
                            string name,
                            ActivityKind kind,
                            ActivityContext parentContext, 
                            IEnumerable<KeyValuePair<string, string?>>? tags = null,
                            IEnumerable<ActivityLink>? links = null,
                            DateTimeOffset startTime = default);

        /// Creates a new <see cref="Activity"/> object if there is any listener to the Activity events, returns null otherwise.
        public Activity? StartActivity(
                            string name,
                            ActivityKind kind,
                            string parentId,
                            IEnumerable<KeyValuePair<string, string?>>? tags = null,
                            IEnumerable<ActivityLink>? links = null,
                            DateTimeOffset startTime = default);

        /// Dispose the ActivitySource object and remove the current instance from the global list. empty the listeners list too.
        public void Dispose();

        /// Add a listener to the Activity starting and stopping events.
        public static void AddActivityListener(ActivityListener listener) { }
    }
```

### Activity

```C#
namespace System.Diagnostics
{
    public partial class Activity : IDisposable // added IDisposable
    {
        ....
        public ActivityKind Kind { get; } = ActivityKind.Internal;
        public string DisplayName { get; set; }
        public ActivitySource Source { get; }

        public Activity AddEvent(ActivityEvent e);
        public IEnumerable<ActivityEvent> Events { get; }

        public IEnumerable<ActivityLink> Links { get; }

        public ActivityContext Context { get; }

        public bool IsAllDataRequested { get; set;}

        public void SetCustomProperty(string propertyName, object? propertyValue);
        public object? GetCustomProperty(string propertyName);

        public void Dispose();
        protected virtual void Dispose(bool disposing);
    }
}

```

Q & A
================

[TODO] There is a bunch of discussion in https://github.com/dotnet/runtime/issues/31373 that hasn't been replicated here. If those discussions continue it is probably worthwhile to put conclusions (with needed context here)



Q\) It would be great if we put some guidance here regarding the Activity naming convention

[Noah] Agreed. It wasn't an area I focused on much in that experiment, but we will need to have a plan for it.







#### Outdated

The questions below came from an earlier version of the proposal. I think they are largely moot now given changes to the proposal in the interim and the context is gone but keeping it here just in case.



Q\) How do you envision the listener registration? I guess it is unlikely that people will use ActivitySource.AddListener as it is hard to find all the sources.

[Noah] This ActivityListener ([https://github.com/noahfalk/ActivitiesExperiment/blob/master/ActivitiesExample/Usage/OTel/ActivityListener.cs\#L12]) was an example of code that OpenTelemetry could write to listen to all sources. I don't feel sold on it either FWIW, but it didn't seem that hard to me to enumerate all the sources.

 

Q\) Does this mean we might need a "meta-registry for non-default registries"? Would you give one scenario?

[Noah] My only intent in exposing a non-default registry was to allow people to make a test of their telemetry and run those tests in parallelized unit test framework. I was not anticipating that people would use it in their production scenarios and none of the events registered a non-default registry would be captured by OpenTelemetry.

 

Q\) Would you consider something like IActivityListener.Start and IActivityListener.Stop?

[Noah] Did you mean rename ActivityScopeStarted -\> Start and ActivityScopeStopped -\> Stop or were you thinking of new methods that have some other behavior? In general I think we should be open to consider anything at this stage : ) This is by no means a final or near-final design.

 

Q\) I think the simplest approach for a developer is to create/start/stop Activity.

[Noah] It is simpler but so far I have been discouraged from it because of performance implications. I'm open to alternate opinions though. In high performance scenarios I'd expect most telemetry to be filtered/sampled away so the cost that we have pay to execute the instrumentation code prior to the sampling/filtering decision is the hot path. Ideally we'd like that path to be as close to zero cost as possible. The amortized CPU cost to allocate an Activity object and call Start()/Stop() is \~700ns. ASP.Net has a goal to have a tech empower Fortunes scenario running at 70,000ns per request so every invocation of this pattern represents 1% of the total request CPU budget. Some optimizations are possible, but the only way I see to significantly speed it up is to use an API that has different semantics. In particular Activity Start/Stop is required to allocate an Activity object, compute timestamps, and register the Activity in async-local storage always but an alternate API wouldn't need to have that constraint.

 

Q\) Making Activity as IDisposable and calling Stop on Dispose might also be a user-friendly approach which .NET team should consider.

[Noah] Oddly I thought Activity already did that but when I went to check I saw it does not. I agree it's certainly worth considering if we decided to be content with the current level of performance we get from Activity.
