# Performance Profiling Controller Design #

**Dev** [Brian Robbins](https://github.com/brianrob)

### Introduction ###

This document describes the end-to-end scenarios and work required to build a performance profiling controller for .NET Core.  The controller is responsible for control of the profiling infrastructure and exposure of performance data produced by .NET performance diagnostics components in a simple and cross-platform way.

.NET Core 2.0+ has an existing built-in managed code profiler that is self-contained and requires no administrative privileges.  It is designed to provide managed code profiling capabilities in a cross-platform manner in a variety of deployment scenarios include constrained execution environments such as containers.

It is expected that the performance profiling controller will expose its functionality via HTTP.  The following are a (likely incomplete) set of advantages to this approach:
 - HTTP is easily consumable directly by users and tools.
 - The .NET stack already has significant investment in an HTTP stack that can be configured in many different ways for different scenarios. 
 - Choosing HTTP allows the controller to be exposed as narrowly or as broadly as the administrator of the application would like (no one --> anonymous users).
 - Cloud providers already have machinery in-place to expose HTTP endpoints which helps to avoid special deployment requirements.

### User-Facing Scenarios ###

#### Easily Gather a Performance Trace ####
The .NET Core profiler does not have an out-of-process mechanism to collect a trace.  The controller will be responsible for exposing this capability outside the process in an easily consumable way.

This will include configuration, enable/disable, and downloading of traces.

#### View a Snapshot of Performance Counters ####
The .NET runtime and libraries have a significant amount of instrumentation that is used to understand the behavior of each component.  These are used for both performance analysis and monitoring.

Some of this instrumentation should be aggregated inside of the runtime using EventCounters.  Rather than forcing users to consume this data by capturing a trace and then viewing it using an offline viewer, the controller can provide an up-to-date snapshot that can be constantly refreshed or downloaded via REST.

#### View Curated Diagnostic Data ####
There are times when specific types of data deserve a specific view.  These views can be implemented and exposed via the controller to provide ease of access to the data.

As an example, a nice way to understand the current behavior of the running process is to get a snapshot of the stacks of all running threads.  While this view doesn't fit into any of the previously introduced views, it is something that deserves its own view.

Other examples of curated views can include lock contention views, managed exception counts, GC statistics and JIT statistics.

### Dependencies ###
This functionality depends on the following components:

 - HTTP stack (OPEN ISSUE: What layer of ASP.NET?)
 - EventPipe
	 - Includes incoming expansion of support for all platforms.
 - EventCounters
 - EventSource
	 - Including TraceLogging support.

This functionality depends on the following scheduled work:

 - Required: Enable EventPipe to run on Windows and OSX.
 - Optional: Cross-platform performance counters - Required to support EventCounter / EvenSource views.

### Functionality Exposed Through Controller ###
The following represents a first cut at the initial functionality to be exposed through the controller.  This is an open area of discussion and the initial investment can be either increased or decreased based on desire.

#### REST APIs ####
 - Pri 1: Simple Profiling: Profile the runtime for X amount of time and return the trace.
 - Pri 1: Advanced Profiling: Start tracing (along with configuration)
 - Pri 1: Advanced Profiling: Stop tracing (the response to calling this will be the trace itself)
 - Pri 2: Get the statistics associated with all EventCounters or a specified EventCounter.

#### Browsable HTML Pages ####
 - Pri 1: Textual representation of all managed code stacks in the process.
	 - Provides an snapshot overview of what's currently running for use as a simple diagnostic report.
 - Pri 2: Display the current state (potentially with history) of EventCounters.
	 - Provides an overview of the existing counters and their values.
	 - OPEN ISSUE: I don't believe the necessary public APIs are present to enumerate EventCounters.

### Work Required ###

#### Runtime Work ####
 - Enable EventPipe to run on all supported platforms (Currently Linux, Add Windows and OSX).
 - Expose EventPipe profiling APIs in a way that up-stack components can consume them.
 - Expose the ability for EventPipe to provide a trace back to the controller in-memory.
	 - OPEN ISSUE: TBD on exactly how this happens.
	 - OPEN QUESTION: Can we use System.Diagnostics.StackTrace?
 - OPTIONAL: Support multiple EventPipe sessions to allow multiple consumers to gather trace data concurrently.
	 - Without this, there is a risk that multiple consumers will exist at the same time, and EventPipe needs to be able to handle this gracefully (it's OK to fail if we don't implement this). 

#### Controller Work ####
 - Create a library / NuGet package that contains the controller and can be attached to a new or existing in-process HTTP stack.
	 - OPEN QUESTION: What is the right HTTP stack component to depend upon.
 - Implement each of the REST APIs defined above.
 - Implement each of the browsable pages defined above.
 - Implement a mechanism to enable the controller without re-building the application.
	 - OPEN ISSUE: Is this required?
	 - OPEN QUESTION: Do we want this for scenarios where the controller may be enabled by someone other than the developer of the application?
 - Implement a mechanism to download / deploy the controller.
	 - If the controller is opt-in by the developer, this is just NuGet.
	 - OPEN ISSUE: If the controller can be injected into the app, how does it get downloaded?  SDK global tool?
	 - CONSIDER: ASP.NET dependency injection.
