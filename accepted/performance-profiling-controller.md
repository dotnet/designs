# Performance Profiling Controller Design #

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

 - Managed stack snapshot.


#### Control ####

#### Data Exposure ####
 - Collect a managed code trace for the target process.
 - Display the managed code stacks for all managed threads in the process.
 - 

#### Scenario # 1 ####

TBD

### Dependencies ###

TBD

### Components Involved ###

TBD

### Work Required ###

TBD