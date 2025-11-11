# .NET Runtime Profiling Roadmap

We want .NET apps to run fast - astoundingly, shockingly fast. The team devotes [huge effort optimizing](https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-10/) so that apps run quicker using fewer resources in every .NET update. However performance isn't just a concern for runtimes or compilers. Creating a top performing app requires efficiency across the entire stack. Profiling tools are how we help developers and AI agents understand the full picture to craft complete optimized applications. We strive to make profiling approachable for beginners and comprehensive for experts. Ultimately our goal is that every developer achieves beautiful performance.

This doc explores some history on our approach to profiling so far, the current state of affairs, and the trajectory we anticipate for the future.

## Profiling scenarios

This doc focuses on a few main scenarios:

The first profiling scenario for many developers is understanding why a .NET application is not performing well by running a client app profiling tool such as Visual Studio, PerfView, dotTrace, or VTune to collect and analyze a performance trace. The application is usually only monitored for a short time and the analysis can go deep on few key resources such as CPU, memory, or IO usage. Sometimes these tools use readily available hardware counters or other built-in instrumentation to produce the data, other times they inject their own instrumentation. In some cases users invoke two separate tools, one to collect the data and another to analyze it. This allows collecting traces on any platform .NET supports and then moving to a 2nd machine, often Windows, where the visualization tools can run. While these tools are often used directly by human developers today we increasingly expect AI agents to be involved producing and analyzing the data in the future.

'Production profiling' or 'Continuous profiling' is a more recent evolution of Application Performance Monitoring (APM) tools. It offers some of the detailed analysis possible with client app profilers but operates passively over long time periods and usually stores the tracing data in some remote file store or database. Unlike client profiling tools, these scenarios can also aggregate together data from multiple machines offering service-wide or fleet-wide analysis. For data, these tools collect callstacks to correlate events back to code and they will collect high frequency usage samples for CPU, wall clock time, IO, and/or allocations. The results are then displayed via web based UIs.

Lastly there is benchmarking where the performance of some piece of code is analyzed to report pre-determined measurements such as time taken or memory used. Benchmarks are often automated in a harness such as BenchmarkDotNet to compare different variations of the code or the environment. The benchmarking might get used for one-off exploratory analysis or for longer term performance regression testing. Sometimes the profiling traces are also processed programmatically using our APIs so that teams or AI agents can do customized analysis.

.NET's profiling capabilities readily support a wider set of observability scenarios but to keep discussion focused those are out-of-scope for this doc. Even though they are not the focus here .NET provides rich mechanisms for these adjacent scenarios such as logging, metrics, distributed tracing, IL instrumentation, and telemetry generally.

## Early .NET investments (pre-2014)

Event Tracing for Windows (ETW) is a high performance event tracing mechanism built into the Windows kernel. It allows capturing hardware, OS, and application layer events combined in a single trace. The .NET runtime has built-in support for numerous [ETW events](https://learn.microsoft.com/dotnet/core/diagnostics/well-known-event-providers) as well the [EventSource managed APIs](https://learn.microsoft.com/dotnet/core/diagnostics/eventsource-getting-started) for .NET library and app developers to emit custom events. This allows any ETW profiler (such as [Visual Studio](https://learn.microsoft.com/visualstudio/profiling/?view=vs-2022) or [PerfView](https://github.com/microsoft/perfview)) to display rich .NET performance information. We also created the [TraceEvent APIs](https://github.com/microsoft/perfview/blob/main/documentation/TraceEvent/TraceEventLibrary.md) to help analyze ETW traces and the [ICorProfiler API](https://learn.microsoft.com/dotnet/framework/unmanaged-api/profiling/icorprofilerinfo-interface) to do low-level custom runtime instrumentation. These APIs help developers create a variety of custom profiling tools from automating ad-hoc analysis to rich products of their own.

## The pivot towards cross-platform and cloud-native (post-2014)

As .NET moved cross-platform so has our approach to profiling.  .NET is embracing a broader array of Linux standards, OS-neutral industry standards, and creating new OS-neutral .NET runtime standards. For generating the profiling events we preserved support for our existing EventSource instrumentation + added options to include data from newer OpenTelemetry-compatible instrumentation. No application code changes are required. For collecting the profiling data into a trace we invested into two parallel strategies:

### OS-neutral profiling

In .NET Core 3 we created a new tracing mechanism called [EventPipe](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/eventpipe) implemented inside the .NET runtime that fulfills a similar role as ETW. External tools send commands to start and stop EventPipe tracing sessions. Each session can be configured to collect different sets of events and then serialize and stream those events back to the tools which requested the session. The events are still generated by the same [EventSource API](https://learn.microsoft.com/dotnet/core/diagnostics/eventsource-getting-started) and they have the same content. We also built a canonical CLI tool, [dotnet-trace](https://learn.microsoft.com/dotnet/core/diagnostics/dotnet-trace), that users interact with to record traces. In place of ETW's undocumented .etl format we standardized a new open file format, [NetTrace](https://github.com/microsoft/perfview/blob/main/src/TraceEvent/EventPipe/NetTraceFormat.md). Trace analysis can be done by PerfView, Visual Studio, or other custom tools using the [TraceEvent library](https://github.com/microsoft/perfview/blob/main/documentation/TraceEvent/TraceEventLibrary.md). dotnet-trace can also [convert](https://learn.microsoft.com/dotnet/core/diagnostics/dotnet-trace#dotnet-trace-convert) NetTrace into a few other common formats. Taken all together these form an E2E scenario where traces can be collected on any OS .NET supports. This approach also requires no elevated privileges that are common in OS-native tracing systems. However a significant limitation of this approach is that it only captures events from the .NET runtime and managed code components, not kernel events or events from native libraries.

### OS-integrated profiling

Gathering a complete view of the application performance that extends down to the hardware requires deep integration with the operating system and we've done this for both Windows and Linux. Although profiling this way requires high privileges and has OS-specific idiosyncracies we believe this is a critical capability that developers sometime need. Our goal is high performance tracing for events from kernel-mode, user-mode native code, and user-mode .NET code. For all of these events we want complete cross-language symbolicated callstacks. On Windows we achieve this via ETW but on Linux there is a more diverse selection of tracing technologies. Initially we invested in the [PerfCollect script](https://learn.microsoft.com/dotnet/core/diagnostics/trace-perfcollect-lttng) which wrapped using [perf](https://www.man7.org/linux/man-pages/man1/perf.1.html) and [LTTng](https://lttng.org/) however we weren't fully satisfied by the experience. Coming in .NET 10 we are investing in [user_events](https://docs.kernel.org/trace/user_events.html), a new mechanism for capturing user-mode events that was contributed to the Linux kernel by Microsoft. As far as we know .NET is the first language to explore using this new tracing approach.

## Our roadmap forward

### In .NET 10 (November 2025)

Our high level two prong strategy of OS-neutral and OS-integrated profiling remains unchanged, but we are making significant investments to improve the robustness, capabilities, and performance of the OS-integrated prong on Linux.

We are updating the dotnet-trace tool so that it can be a CLI front-end for both the OS-neutral and OS-integrated profiling approaches. Users can run the
commands `dotnet-trace collect` and `dotnet-trace collect-linux` respectively. In particular for anyone using a Linux kernel supporting user_events (6.4+) and .NET 10 apps the new capabilities in dotnet-trace will offer a superior experience to PerfCollect. We are expecting:
- The same dotnet-trace acquisition experience as today (download from HTTP on-demand, or download at image generation time, or install as a dotnet global tool)
- No restart/no environment variables required to profile
- Configurable event collection including CPU sampling, context switches, all Linux tracepoints, all .NET runtime events, and all EventSource generated events
- All processes on the machine can be profiled simultaneously
- All events support callstacks including kernel frames, precompiled code user-mode frames, and jitted code user-mode frames
- Callstacks are symbolicated using a combination of native code symbols present on the machine producing the trace, jitted code symbols captured at trace generation time, and R2R symbols downloaded from Microsoft Symbol Server
- Traces are stored in the compact .nettrace format and are analyzable in PerfView, upcoming versions of Visual Studio, or any other tool using the TraceEvent library.

Behind the scenes the [dotnet-trace collect-linux](https://learn.microsoft.com/dotnet/core/diagnostics/dotnet-trace) command is building on the new [user_events](https://docs.kernel.org/trace/user_events.html) Linux kernel feature, integration with user_events in the .NET runtime, and a new OSS library [OneCollect](https://github.com/microsoft/one-collect). User_events allows user-mode code to register events with the Linux kernel and then emit instances of those events into the kernel tracing buffers. Although the newer kernel still needs to be adopted into many distros .NET chose to be a leading adopter because it offered several benefits:
- It allows defining new events via simple API calls while an application is running. Unlike [uprobes](https://www.kernel.org/doc/html/latest/trace/uprobetracer.html) or [USDT](https://www.brendangregg.com/perf.html#StaticUserTracing), user_events doesn't tie event identity to known addresses in precompiled binaries which makes it much better suited for jitted or interpretted languages.
- It records events using the same underlying [perf_event_open](https://www.man7.org/linux/man-pages/man2/perf_event_open.2.html) kernel buffers and tracing code which allows all the logic for callstacks, event subscription, and event serialization to be readily shared in collection tools.

The [OneCollect](https://github.com/microsoft/one-collect) library allows dotnet-trace to interact with the Linux kernel trace buffers directly rather than trying to script the interaction via's [perf](https://www.man7.org/linux/man-pages/man1/perf.1.html)'s command line interface. This is bringing us several benefits as well:
- It allows us to customize event processing, callstack symbolication, and serialization formats. For now it gives us the option to serialize into the NetTrace data format which is both space efficient and supported by our existing profiling tools. In the future we are looking forward to more format support and customized callstack symbolication to let us avoid some of the resource usage challenges posed by [perf-<pid>.map files](https://stackoverflow.com/questions/38277463/how-does-linuxs-perf-utility-understand-stack-traces).
- It allows us to do the custom configuration we need to enable .NET EventSource events.
- OneCollect can be built centrally which makes it much easier for a [global tool](https://learn.microsoft.com/dotnet/core/tools/global-tools-how-to-use) like dotnet-trace to consume. PerfCollect has a distro-specific acquisition step to acquire the various Linux tools it needs and this was a frequent failure point for users. Not all distros published the tools or used the same naming and packaging to publish them.

To complete the new OS-integrated profiling scenario we are also investing in [improvements to the NetTrace file format](https://github.com/microsoft/perfview/blob/main/src/TraceEvent/EventPipe/NetTraceFormat.md#version-5---6) and new features in TraceEvent and PerfView to better visualize the data being captured in Linux traces.

### In the future (After November 2025)

Our work in .NET 10 should certainly move the needle but there is still more to do. There are a couple broad areas of interest and investment.

#### More relevant diagnostic information

Developers want profilers to help them understand the full performance picture of their application. When there is information they can't access or make sense of then problems go unsolved and they sour on our development experience. We need to look at capturing the right kinds of data at the right times so that customers can solve a broad variety of problems. Some examples here:

##### Circular buffering and triggers

Tracing for some types of problems requires monitoring for a long time until a problem occurs, then capturing the events that shortly preceded the problem. When the events get too verbose its not feasible to store all of them on disk for days or weeks solely to ensure that a few seconds before a problem get recorded. To handle this we want to support storing trace data using an in-memory circular buffer, then a trigger identifies when a problem occurs and causes the events to get written to durable storage. Our canonical scenario is investigating GC performance issues where the trigger is a GC that takes too long or a GC heap that has grown too large. Once the infrastructure is in place to support triggering in general other particular kinds of triggers may get added over time. PerfView already supports this scenario on Windows with ETW but we want support on Linux as well.

##### Async stacks

.NET applications are increasingly using async code. The physical stack traces shown in the various analysis views often differ wildly from the source code level abstraction developers are reasoning with. This makes it challenging to understand and there is no general purpose way to reconstruct the async stacks from a trace that did not capture them. We need to support doing async stackwalks when events occur, storing those stacks into the trace, and visualizing them in profiling tools.

##### TraceId + SpanId enrichment for events

TraceId and SpanId are the industry standard distributed tracing identifiers to track a request through a distributed system. By capturing these identifiers in our events we can do request latency analysis, categorize resource usage by request, and correlate tracing events to other places where these IDs surface. For example the error page of failed web request might report these IDs allowing a service engineer to look up tracing information for that request. We currently have another identifier that serves a similiar purpose, [ActivityId](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/eventsource-activity-ids), however that one is only standardized within the ETW ecosystem.

#### Easy and reliable operation

Developers want to run profiling tools without worrying that it might cause the app to crash or run slowly. Nor do they want to discover during data analysis that the environment setup wasn't correct so they need to throw everything out and start over. Initially we should focus on eliminating places where we don't scale well to particular app behavior and unnecessary potential failure points where we expect extra developer work to prepare the profiling environment. After that we can look at safeguards against misconfiguration and stronger resource consumption limits.

##### Symbolicate jitted code stack frames without a perfmap file

Generating a perfmap file to store jitted code symbolic info has been problematic for applications that JIT very large amounts of code or run in environments with tight resource constraints. We should support some alternative mechanism to convey the symbol information, either via data inline in the trace stream or via IPC commands. Ideally these mechanisms should support bounded memory usage or memory usage that scales with the number of methods observed in trace callstacks rather than the total number of methods jitted by the application.

##### Symbolicate native code using symbols on Microsoft Symbol Server

For distros that we build ourselves such as Azure Linux we should index native symbol information on Microsoft Symbol Server and allow our analysis tools to download it from there. This alleviates the need to include native symbols locally on production machines or to capture that info within traces. This is a combination of build-time work for teams producing the Linux distro binaries to index them and work in the analysis tools to support downloading the symbols.

#### Support more profiling formats

Today there are relatively few tools that can read the .nettrace profiling format natively. We want to help .NET developers get access to a broader set of profiling tools by supporting additional formats.

##### Support OpenTelemetry's profiling format

OpenTelemetry has been working on a [standardized a profiling file format](https://github.com/open-telemetry/opentelemetry-specification/blob/main/oteps/profiles/0239-profiles-data-model.md) for a couple years. Once the format is stable we can offer it as an option for the collected trace and implement a parser for it in TraceEvent. .NET has had a lot of success with OpenTelemetry in other areas and we remain optimistic about the project. Although there may not be a lot of tooling for this format initially we expect over time this format will become an efficient and popular profiling data interchange format. Supporting it should give .NET developers access to a much wider set of profiling analysis tools.

##### Support simple structured text as an output format

Today our tools primarily deal with compressed binary formats for storing trace data which is efficient but not trivial to consume. We should support outputing textual forms of event data to the console, or converting our NetTrace format into text. This would make it far easier for humans to quickly view small amounts of data or transfer the data into other analysis tools or LLMs.

#### Better consumption by AI agents

As AI plays a larger role in the profiling space we want our tools to be controllable and consumable by AI agents being created by others. We already support command-line and programatic interfaces via dotnet-trace, TraceEvent, and the DiagnosticsClient libraries. Broader file format support should also make it more likely that .NET profiling data can be ingested by a broader range of AI powered tools. Overall the AI space is fast advancing so we need to experiment, monitor, and collaborate to understand what other changes will be beneficial.

#### Better Linux native tooling integration

For better integration with existing Linux tooling such as 'perf', we need ways to register .NET runtime events or EventSource events as available user_events. For some events this might be fully automatic and for others it might still require some additional step by the application developer or user collecting the trace. Investment here is TBD depending on user demand. For now this is more speculative than many of the other improvements on the roadmap but it might wind up being important.

#### Support profiling on WASM

As CoreCLR expands its reach into the WASM scenario we want EventPipe to support it as well. WASM has some unique limitations around single-threaded execution and external communication that will require more than just a straightforward port of the EventPipe code. We'd also like to support some form of hot-path analysis for PGO.

#### Observability scenarios other than performance measurement

All the technology and investment that powers our profiling also accrues benefits towards a much wider set of scenarios such as application health monitoring, analytics, cloud scale debugging via dynamic telemetry, real-time security monitoring, and production testing. While the .NET team doesn't expect to build any of these E2E scenarios on our own, we do want to create a robust and general purpose foundation that enables partners to build these experiences.


