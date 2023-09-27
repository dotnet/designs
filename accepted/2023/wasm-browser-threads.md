# Multi-threading on a browser

## Goals
 - CPU intensive workloads on dotnet thread pool
 - enable blocking .Wait APIs from C# user code on all threads
     - Current public API throws PNSE for it
     - This is core part on MT value proposition.
     - If people want to use existing MT code-bases, most of the time, the code is full of locks. 
     - People want to use existing desktop/server multi-threaded code as is.
 - allow HTTP and WS C# APIs to be used from any thread
     - Underlying JS object have thread affinity
 - JSImport/JSExport interop in maximum possible extent
 - don't change/break single threaded build. †

## Lower priority goals
 - try to make it debugging friendly
 - implement crypto via `subtle` browser API
 - allow calls to synchronous JSExport from UI thread (callback)

<sub><sup>† Note: all the text below discusses MT build only, unless explicit about ST build.</sup></sub>

## Context - Problems
**1)** If you have multithreading, any thread might need to block while waiting for any other to release a lock.
     - locks are in the user code, in nuget packages, in Mono VM itself
     - there are managed and un-managed locks
     - in single-threaded build of the runtime, all of this is NOOP. That's why it works on UI thread.

**2)** UI thread in the browser can't synchronously block
    - you can spin-lock but it's bad idea.
        - Deadlock: when you spin-block, the JS timer loop and any messages are not pumping. But code in other threads may be waiting for some such event to resolve.
        - It eats your battery
        - Browser will kill your tab at random point (Aw, snap).
        - It's not deterministic and you can't really test your app to prove it harmless.
    - Firefox (still) has synchronous XHR which could be captured by async code in service worker
        - it's deprecated legacy API
        - but other browsers don't and it's unlikely they will implement it
        - there are deployment and security challenges with it
    - all the other threads/workers could synchronously block
    - if we will have managed thread on the UI thread, any `lock` or Mono GC barrier could cause spin-wait
        - in case of Mono code,  we at least know it's short duration
        - we should prevent it from blocking in user code

**3)** JavaScript engine APIs and objects have thread affinity. 
    - The DOM and few other browser APIs are only available on the main UI "thread"
        - and so, you need to have C# interop with UI, but you can't block there.
    - HTTP & WS objects have affinity, but we would like to consume them (via Streams) from any managed thread
    - Any `JSObject`, `JSException` and `Task` have thread affinity
        - they need to be disposed on correct thread. GC is running on random thread

**4)** State management of JS context `self` of the worker.
    - emscripten pre-allocates poll of web worker to be used as pthreads. 
        - Because they could only be created asynchronously, but `pthread_create` is synchronous call
        - Because they are slow to start
    - those pthreads have JS context `self`, which is re-used when mapped to C# thread pool
    - when we allow JS interop on a managed thread, we need a way how to clean up the JS state

**5)** Blazor's `renderBatch` is using direct memory access

## Define terms
- UI thread
    - this is the main browser "thread", the one with DOM on it
    - it can't block-wait, only spin-wait
- "sidecar" thread - possible design
    - is a web worker with emscripten and mono VM started on it
    - doing this allows all managed threads to allow blocking wait
- "deputy" thread - possible design
    - is a web worker and pthread with C# `Main` entrypoint
    - doing this allows all managed threads to allow blocking wait
- "managed thread"
    - is a thread with emscripten pthread and Mono VM attached thread and GC barriers
- "main managed thread"
    - is a thread with C# `Main` entrypoint running on it
    - if this is UI thread, it means that one managed thread is special
        - see problems **1,2**
- "managed thread pool thread"
    - pthread dedicated to serving Mono thread pool
- `JSSynchronizationContext`
- `JSObject`
- `Promise`/`Task`
- `JSWebWorker`

## Implementation options (only some combinations are possible)
- how to deal with blocking C# code on UI thread
    - **A)** pretend it's not a problem (this we already have)
    - **B)** move user C# code to web worker
    - **C)** move all Mono to web worker
- how to deal with blocking in synchronous JS calls from UI thread
    - **D)** pretend it's not a problem (this we already have)
    - **E)** throw PNSE when synchronous JSExport is called on UI thread
    - **F)** dispatch calls to synchronous JSExport to web worker and spin-wait on JS side of UI thread.
- how to implement JS interop between managed main thread and UI thread (DOM)
    - **G)** put it out of scope for MT, manually implement what Blazor needs
    - **H)** pure JS dispatch between threads, [comlink](https://github.com/GoogleChromeLabs/comlink) style
    - **I)** C/emscripten dispatch of infrastructure to marshal individual parameters
    - **J)** C/emscripten dispatch of method binding and invoke, but marshal parameters on UI thread
    - **K)** pure C# dispatch between threads
- how to implement JS interop on non-main web worker
    - **L)** disable it for all non-main threads
    - **M)** disable it for managed thread pool threads
    - **N)** allow it only for threads created as dedicated resource `WebWorker` via new API
    - **O)** enables it on all workers (let user deal with JS state)
- how to dispatch calls to the right JS thread context
    - **P)** via `SynchronizationContext` before `JSImport` stub, synchronously, stack frames
        - this is written by user. Complex, async, MT stuff.
    - **Q)** via `SynchronizationContext` inside `JSImport` C# stub
    - **R)** via `emscripten_dispatch_to_thread_async` inside C code of ``
- how to implement GC/dispose of `JSObject` proxies
    - **S)** per instance: synchronous dispatch the call to correct thread via `SynchronizationContext`
    - **T)** per instance: async schedule the cleanup
    - at the detach of the thread. We already have `forceDisposeProxies`
    - could target managed thread be paused during GC ?
- where to instantiate initial user JS modules (like Blazor's)
    - **U)** in the UI thread
    - **V)** in the deputy/sidecar thread
- where to instantiate `JSHost.ImportAsync` modules
    - **W)** in the UI thread
    - **X)** in the deputy/sidecar thread
    - **Y)** allow it only for dedicated `JSWebWorker` threads
    - **Z)** disable it
    - same for `JSHost.GlobalThis`, `JSHost.DotnetInstance`
- how to implement Blazor's `renderBatch`
    - **a)** keep as is, wrap it with GC pause, use legacy JS interop on UI thread
    - **b)** extract some of the legacy JS interop into Blazor codebase
    - **c)** switch to Blazor server mode. Web worker create the batch of bytes and UI thread apply it to DOM
- where to create HTTP+WS JS objects
    - **d)** in the UI thread
    - **e)** in the managed main thread
    - **f)** in first calling managed thread
        - install `JSSynchronizationContext` even without `JSWebWorker` ?
- how to dispatch calls to HTTP+WS JS objects
    - **g)** try to stick to the same thread via `ConfigureAwait(false)`.
        - doesn't really work. `Task` migrate too freely
    - **h)** via C# `SynchronizationContext`
    - **i)** via `emscripten_dispatch_to_thread_async`
    - **j)** via `postMessage`
    - **k)** same whatever we choose for `JSImport`
    - note there are some synchronous calls on WS
- where to create the emscripten instance
    - **l)** could be on the UI thread
    - **m)** could be on the "sidecar" thread
- where to start the Mono VM
    - **n)** could be on the UI thread
    - **o)** could be on the "sidecar" thread
- where to run the C# main entrypoint
    - **p)** could be on the UI thread
    - **q)** could be on the "deputy" or "sidecar" thread
- where to implement subtle crypto
    - **r)** out of scope
    - **s)** in the UI thread
    - **t)** is a dedicated web worker

# Interesting combinations

## Minimal support
- **A,D,G,L,P,S,U,Y,a,f,h,l,n,p** 
- this is what we [already have today](#Current-state-2023-Sep)
- it could deadlock or die, 
- JS interop on threads requires lot of user code attention
- Keeps problems **1,2,3,4**

## Sidecar + no JS interop + narrow Blazor support
- **C,E,G,L,P,S,U,Z,c,d,h,m,o,q**
- minimal effort, low risk, low capabilities
- move both emscripten and Mono VM sidecar thread
- no user code JS interop on any thread
- internal solutions for Blazor needs
- Ignores problems **1,2,3,4,5**

## Sidecar + only async just JS proxies UI + JSWebWorker + Blazor WASM server
- **C,E,H,N,P,S,U,W+Y,c,e+f,h+k,m,o,q**
- no C or managed code on UI thread
- no support for blocking sync JSExport calls from UI thread (callbacks)
    - it will throw PNSE
- this will create double proxy for `Task`, `JSObject`, `Func<>` etc
    - difficult to GC, difficult to debug
- double marshaling of parameters
- Avoids **1,2** for JS callback 
- Solves **1,2** for managed code.
    - emscripten main loop stays responsive
- Solves **3,4,5**

## Sidecar + async & sync just JS proxies UI + JSWebWorker + Blazor WASM server
- **C,F,H,N,P,S,U,W+Y,c,e+f,h+k,m,o,q**
- no C or managed code on UI thread
- support for blocking sync JSExport calls from UI thread (callbacks)
    - at blocking the UI is at least well isolated from runtime code
    - it makes responsibility for sync call clear
- this will create double proxy for `Task`, `JSObject`, `Func<>` etc
    - difficult to GC, difficult to debug
- double marshaling of parameters
- Ignores **1,2** for JS callback 
- Solves **1,2** for managed code
    - emscripten main loop stays responsive
    - unless there is sync `JSImport`->`JSExport` call
- Solves **3,4,5**

## Deputy + managed UI interop + JSWebWorker
- this uses `JSSynchronizationContext` to dispatch calls to UI thread
    - this is problematic because some managed code is actually running on UI thread
    - it needs to also use `SynchronizationContext` for `JSExport` and callbacks, to dispatch to deputy.
- blazor render could be both legacy render or Blazor server style
    - because we have both memory and mono on the UI thread
- Ignores **1,2** for JS callback 
- Solves **1,2** for managed code
    - emscripten main loop stays responsive
    - unless there is sync `JSImport`->`JSExport` call
- Solves **3,4,5**

## Deputy + UI bound interop + JSWebWorker
- this uses `emscripten_dispatch_to_thread_async` for `call_entry_point`, `complete_task`, `cwraps.mono_wasm_invoke_method_bound`, `mono_wasm_invoke_bound_function`, `mono_wasm_invoke_import`, `call_delegate_method` to get to the UI thread.
- it uses other `cwraps` locally on UI thread, like `mono_wasm_new_root`, `stringToMonoStringRoot`, `malloc`, `free`, `create_task_callback_method`
    - it means that interop related managed runtime code is running on the UI thread, but not the user code.
    - it means that parameter marshalling is fast (compared to sidecar)
    - it still needs to enter GC barrier and so it could block UI for GC run
- blazor render could be both legacy render or Blazor server style
    - because we have both memory and mono on the UI thread


## Blazor
- as compared to single threaded runtime, the major difference would be no synchronous callbacks.
    - for example from DOM `onClick`. This is one of the reasons people prefer ST WASM over Blazor Server.
    - but there is really [no way around it](#problem), because you can't have both MT and sync calls from UI.
- Blazor `renderBatch`
    - currently `Blazor._internal.renderBatch` -> `MONO.getI16`, `MONO.getI32`, `MONO.getF32`, `BINDING.js_string_to_mono_string`, `BINDING.conv_string`, `BINDING.unbox_mono_obj`
    - some of them need Mono VM and GC barrier, but could be re-written with GC pause and only memory read
- Blazor's [`IJSInProcessRuntime.Invoke`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.jsinterop.ijsinprocessruntime.invoke) - this is C# -> JS direction
    - TODO: which implementation keeps this working ? Which worker is the target ?
    - we could use Blazor Server style instead
- Blazor's [`IJSUnmarshalledRuntime`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.jsinterop.ijsunmarshalledruntime)
    - this is ICall `InvokeJS`
    - TODO: which implementation keeps this working ? Which worker is the target ?
- `JSImport` used for startup, loading and embedding: `INTERNAL.loadLazyAssembly`, `INTERNAL.loadSatelliteAssemblies`, `Blazor._internal.getApplicationEnvironment`, `receiveHotReloadAsync`
    - all of them pass simple data types, no proxies
- `JSImport` used for calling user JS code: `Blazor._internal.endInvokeDotNetFromJS`, `Blazor._internal.invokeJSJson`, `Blazor._internal.receiveByteArray`, `Blazor._internal.getPersistedState`
    - TODO: which implementation keeps this working ? Which worker is the target ?
- `JSImport` used for logging: `globalThis.console.debug`, `globalThis.console.error`, `globalThis.console.info`, `globalThis.console.warn`, `Blazor._internal.dotNetCriticalError`
    - probably could be any JS context

# Current state 2023 Sep
 - we already ship MT version of the runtime in the wasm-tools workload.
 - It's enabled by `<WasmEnableThreads>true</WasmEnableThreads>` and it requires COOP HTTP headers.
 - It will serve extra file `dotnet.native.worker.js`.
 - This will also start in Blazor project, but UI rendering would not work.
 - we have pre-allocated pool of browser Web Workers which are mapped to pthread dynamically.
 - we can configure pthread to keep running after synchronous thread_main finished. That's necessary to run any async tasks involving JavaScript interop.
 - GC is running on UI thread/worker.
 - legacy interop has problems with GC boundaries.
 - JSImport & JSExport work
 - There is private JSSynchronizationContext implementation which is too synchronous
 - There is draft of public C# API for creating JSWebWorker with JS interop. It must be dedicated un-managed resource, because we could not cleanup JS state created by user code.
 - There is MT version of HTTP & WS clients, which could be called from any thread but it's also too synchronous implementation.
 - Many unit tests fail on MT https://github.com/dotnet/runtime/pull/91536
 - there are MT C# ref assemblies, which don't throw PNSE for MT build of the runtime for blocking APIs.

## Task breakdown
- [ ] rename `WebWorker` API to `JSWebWorker` ?
- [ ] `ToManaged(out Task)` to be called before the actual JS method
- [ ] public API for `JSHost.<Target>SynchronizationContext` which could be used by code generator.
- [ ] reimplement `JSSynchronizationContext` to be more async
- [ ] implement Blazor's `WebAssemblyDispatcher` + [feedback](https://github.com/dotnet/aspnetcore/pull/48991)
- [ ] optinal: make underlying emscripten WebWorker pool allocation dynamic, or provide C# API for that.
- [ ] optinal: implement async function/delegate marshaling in JSImport/JSExport parameters.
- [ ] optinal: enable blocking HTTP/WS APIs
- [ ] optinal: enable lazy DLL download by blocking the caller
- [ ] optinal: implement crypto
- [ ] measure perf impact

Related Net8 tracking https://github.com/dotnet/runtime/issues/85592