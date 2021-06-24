# Cross-Platform Performance Monitoring Design

**Owner** [Brian Robbins](https://github.com/brianrob)

### Introduction ###

This document describes the end-to-end scenarios and platform support required to enable cross-platform performance monitoring of the .NET runtime, framework, and .NET applications.

Important Points:

 - This proposal is designed to provide a mechanism for managed code to consume both managed and native tracing events (GC, JIT, ThreadPool, Tasks, EventSource, EventCounters, etc.).
 - This proposal leaves the existing EventSource/EventListener model in place and simply adds a mechanism to allow EventListeners to subscribe to events that are emitted from the .NET runtime instead of from managed code via an EventSource (GC, Jit ThreadPool, etc.)
 - Diagnostic features that layer on top of EventSource/EventListener will continue to work and will gain the benefit of having additional tracing data made available to them directly from the runtime.

The concept of performance counters will be used in this document, but counters are only one mechanism included in this design.

### End-to-End Scenarios ###

#### Consuming Raw Trace Data In-Process ####
This scenario allows developers to add code to their applications that can consume and if desired, redirect trace data.  A canonical example of this solution would be to redirect raw trace data to a Graphite database for consumption out-of-process.

#### Consuming Aggregated Trace Data (Counters) In-Process ####
This scenario allows developers to request aggregated trace data in-process.  This aggregate data is essentially what people call "performance counters".  The scenario is almost identical to consuming raw trace data with one difference - the aggregation occurs in-process before the data is provided to the consumer.  This can be useful if the goal of the user is to minimize overhead in favor of less granular data.

### Producing Tracing Data ###
This proposal does not create any new ways to produce tracing data.  All tracing data emitted by an EventSource (this includes EventCounters) automatically flows to an EventListener.

### Consuming Tracing Data ###
Consumption of tracing data, including aggregated counter data, will be done via the existing EventListener class.  The difference is simply that native events will also be consumable via EventListener.

### Components Involved ###

- EventSource / EventListeners
- Native runtime events
- EventCounters
- EventPipe

### Work Required ###

- Enable the EventPipe on all platforms. Today it's only available on Linux.
	- Windows
		- Replace ETW-generated functions with EventPipe script-generated functions that direct events to the EventPipe.
		- Add support for external loggers to EventPipe (use ETW, LTTng, and BPF as examples).
		- Modify EventSource to write to ETW and the EventPipe or run everything through the EventPipe.  NOTE: Running everything through the EventPipe also means needing to support Register/Unregister and callbacks from the OS that get routed to the provider.
	- OSX
		- Replace FireEtw stubs with EventPipe script-generated functions.
		- OPEN ISSUE: I think there is more to be updated.
	- All Platforms
		- Implement the rest of the IsEnabled macros to reduce the cost when tracing is disabled.
- Enable EventPipe to keep track of what logging system (ETW, EventListener, etc.) requested which events and dispatch appropriately.
	- Each instance will be represented by an object that knows what it requested.
	- Have a top-level bitmask that knows whether an event has been requested by anyone and use that to decide whether an event is on.
	- At a minimum, once an event has been requested by anyone, use an observer pattern to dispatch the event to each consumer, and allow them to deliver it if they want (or ignore it).  We can also be smarter and only dispatch to the systems that requested the event (think of each of these logging systems as sessions).
- Update native runtime events to use the EventData serialization format.
	- This is fairly straight forward and just requires updating the script and serialization functions.  The purpose of this is to ensure that all events have a consistent serialization format and that the format easily allows for access to individual payload items.  Once a particular logging system has taken over it can choose to convert the payload into a different format. 
- Enable TraceLogging support in EventPipe.
	- This is required for use of EventCounters.  EventCounters log counter update events using TraceLogging.
	- OPEN ISSUE: What work is required here?
- Enable the EventPipe to target EventListeners as dispatch targets.
	- Allow the EventListener to pinvoke into the runtime and register itself as a target.
	- Implement a GC safe buffering mechanism to allow native events to be dispatched to managed code.
- Plumb payload names for all events through the EventPipe.
	- This is required to fill in the PayloadNames field of EventWrittenEventArgs (EventListener support).
	- Right now, EventSource specifies a metadata blob.
	- CONSIDER: Make EventSource pass a strongly typed object representing the payload fields.
- Extra Credit: Move the LTTng implementation behind the EventPipe
	- Right now, calls to LTTng sit right next to the EventPipe.  They should be moved behind the EventPipe just like ETW (above).
	- This isn't specifically required for performance counters, but can/should be done especially if it cleans up the code or makes things easier.
