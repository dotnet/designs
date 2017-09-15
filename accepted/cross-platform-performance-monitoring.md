# Cross-Platform Performance Monitoring Design #

### Introduction ###

This document describes both the end-to-end scenario, and the platform support required to enable cross-platform performance monitoring.

The concept of performance counters will be covered in this document, but counters are not the only monitoring mechanism included in this design.

### End-to-End Scenarios ###

#### Consuming Raw Trace Data In-Process ####

#### Consuming Aggregated Trace Data (Counters) In-Process ####

#### OPEN ISSUE: Do we want to have an out-of-proc controlled solution or something that doesn't require the developer to opt-in? ####

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
	- This is fairly straight forward and just requires updating the script and serialization functions.
- Enable TraceLogging support in EventPipe.
	- This is required for use of EventCounters.  EventCounters log counter update events using TraceLogging.
	- OPEN ISSUE: What work is required here?
- Enable the EventPipe to target EventListeners as dispatch targets.
	- Allow the EventListener to pinvoke into the runtime and register itself as a target.
	- Implement a GC safe buffering mechanism to allow native events to be dispatched to managed code.
- 

