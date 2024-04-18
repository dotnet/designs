# Multi-threading on a browser

## Table of content
- [Goals](#goals)
- [Key ideas](#key-ideas)
- [State April 2024](#state-2024-april)
- [Design details](#design-details)
- [State September 2023](#state-2023-sep)
- [Alternatives](#alternatives---as-considered-2023-sep)

# Goals
- CPU intensive workloads on dotnet thread pool.
- Allow user to start new managed threads using `new Thread` and join it.
- Add new C# API for creating web workers with JS interop. Allow JS async/promises via external event loop.
- enable blocking `Task.Wait` and `lock()` like APIs from C# user code on all threads
    - Current public API throws PNSE for it
    - This is core part on MT value proposition.
    - If people want to use existing MT code-bases, most of the time, the code is full of locks.
    - People want to use existing desktop/server multi-threaded code as is.
- allow HTTP and WS C# APIs to be used from any thread despite underlying JS object affinity.
- Blazor `BeginInvokeDotNet`/`EndInvokeDotNetAfterTask` APIs work correctly in multithreaded apps.
- JSImport/JSExport interop in maximum possible extent.
- don't change/break single threaded build. †

## Lower priority goals
- try to make it debugging friendly
- sync C# to async JS
    - dynamic creation of new pthread
    - implement crypto via `subtle` browser API
    - allow MonoVM to lazily download DLLs from the server, instead of during startup.
    - implement synchronous APIs of the HTTP and WS clients. At the moment they throw PNSE.
- sync JS to async JS to sync C#
    - allow calls to synchronous JSExport from UI thread (callback)
- don't prevent future marshaling of JS [transferable objects](https://developer.mozilla.org/en-US/docs/Web/API/Web_Workers_API/Transferable_objects), like streams and canvas.
- offload CPU intensive part of WASM startup to WebWorker, so that the pre-rendered (blazor) UI could stay responsive during Mono VM startup.

## Non-goals
- interact with JS state on `WebWorker` of managed threads other than UI thread or dedicated `JSWebWorker`

<sub><sup>† Note: all the text below discusses MT build only, unless explicit about ST build.</sup></sub>

# Key ideas

Move all managed user code out of UI/DOM thread, so that it becomes consistent with all other threads.

## Context - Problems
**1)** If you have multithreading, any thread might need to block while waiting for any other to release a lock.
- locks are in the user code, in nuget packages, in Mono VM itself
- there are managed and un-managed locks
- in single-threaded build of the runtime, all of this is NOOP. That's why it works on UI thread.

**2)** UI thread in the browser can't synchronously block
- that means, "you can't not block" UI thread, not just usual "you should not block" UI
    - `Atomics.wait()` throws `TypeError` on UI thread
- you can spin-wait but it's bad idea.
    - Deadlock: when you spin-block, the JS timer loop and any messages are not pumping.
        - But code in other threads may be waiting for some such event to resolve.
        - all async/await don't work
        - all networking doesn't work
        - you can't create or join another web worker
        - browser dev tools UI freeze
    - It eats your battery
    - Browser will kill your tab at random point (Aw, snap).
    - It's not deterministic and you can't really test your app to prove it harmless.
- all the other threads/workers could synchronously block
    - `Atomics.wait()` works as expected
- if we will have managed thread on the UI thread, any `lock` or Mono GC barrier could cause spin-wait
    - in case of Mono code, we at least know it's short duration
    - we should prevent it from blocking in user code

**3)** JavaScript engine APIs and objects have thread affinity.
- The DOM and few other browser APIs are only available on the main UI "thread"
    - and so, you need to have C# interop with UI, but you can't block there.
- HTTP & WS objects have affinity, but we would like to consume them (via Streams) from any managed thread
- Any `JSObject`, `JSException` and `Promise`->`Task` have thread affinity
    - they need to be disposed on correct thread. GC is running on random thread

**4)** State management of JS context `self` of the worker.
- emscripten pre-allocates pool of web worker to be used as pthreads.
    - Because they could only be created asynchronously, but `pthread_create` is synchronous call
    - Because they are slow to start
- those pthreads have stateful JS context `self`, which is re-used when mapped to C# thread pool
- when we allow JS interop on a managed thread, we need a way how to clean up the JS state

**5)** Blazor's `renderBatch` is using direct memory access

**6)** Dynamic creation of new WebWorker requires async operations on emscripten main thread.
- we could pre-allocate fixed size pthread pool. But one size doesn't fit all and it's expensive to create too large pool.

**7)** There could be pending HTTP promise (which needs browser event loop to resolve) and blocking `.Wait` on the same thread and same task/chain. Leading to deadlock.

# State 2024 April

## What was implemented in Net9 - Deputy thread design

For other possible design options we considered [see below](#Alternatives).

- Introduce dedicated web worker called "deputy thread"
    - managed `Main()` is dispatched onto deputy thread
- MonoVM startup on deputy thread
    - non-GC C functions of mono are still available
- Emscripten startup stays on UI thread
    - C functions of emscripten
    - download of assets and into WASM memory
- UI/DOM thread
    - because the UI thread would be mostly idling, it could:
    - render UI, keep debugger working
    - dynamically create pthreads
    - UI thread stays attached to Mono VM for Blazor's reasons (for Net9)
        - it keeps `renderBatch` working as is, bu it's far from ideal
        - there is risk that UI could be suspended by pending GC
        - It would be ideal change Blazor so that it doesn't touch managed objects via naked pointers during render.
        - we strive to detach the UI thread from Mono
- I/O thread
    - is helper thread which allows `Task` to be resolved by UI's `Promise` even when deputy thread is blocked in `.Wait`
- JS interop from any thread is marshaled to UI thread's JavaScript
- HTTP and WS clients are implemented in JS of UI thread
- There is draft of `JSWebWorker` API 
    - it allows C# users to create dedicated JS thread
    - the `JSImport` calls are dispatched to it if you are on the that thread 
    - or if you pass `JSObject` proxy with affinity to that thread as `JSImport` parameter.
    - The API was not made public in Net9 yet
- calling synchronous `JSExports` is not supported on UI thread
    - this could be changed by configuration option but it's dangerous.
- calling asynchronous `JSExports` is supported
- calling asynchronous `JSImport` is supported
- calling synchronous `JSImport` is supported without synchronous callback to C#
- Strings are marshaled by value
    - as opposed to by reference optimization we have in single-threaded build
- Emscripten VFS and other syscalls
    - file system operations are single-threaded and always marshaled to UI thread
- Emscripten pool of pthreads
    - browser threads are expensive (as compared to normal OS)
    - creation of `WebWorker` requires UI thread to do it
    - there is quite complex and slow setup for `WebWorker` to become pthread and then to attach as Mono thread.
    - that's why Emscripten pre-allocates pthreads
    - this allows `pthread_create` to be synchronous and faster

# Design details

## Define terms
- UI thread
    - this is the main browser "thread", the one with DOM on it
    - it can't block-wait, only spin-wait
- "sidecar" thread - possible design
    - is a web worker with emscripten and mono VM started on it
    - there is no emscripten on UI thread
    - for Blazor rendering MAUI/BlazorWebView use the same concept
    - doing this allows all managed threads to allow blocking wait
- "deputy" thread - possible design
    - is a web worker and pthread with C# `Main` entrypoint
    - emscripten startup stays on UI thread
    - doing this allows all managed threads to allow blocking wait
- "managed thread"
    - is a thread with emscripten pthread and Mono VM attached thread and GC barriers
- "main managed thread"
    - is a thread with C# `Main` entrypoint running on it
    - if this is UI thread, it means that one managed thread is special
        - see problems **1,2**
- "managed thread pool thread"
    - pthread dedicated to serving Mono thread pool
- "comlink"
    - in this document it stands for the pattern
        - dispatch to another worker via pure JS means
        - create JS proxies for types which can't be serialized, like `Function`
    - actual [comlink](https://github.com/GoogleChromeLabs/comlink)
        - doesn't implement spin-wait
    - we already have prototype of the similar functionality
        - which can spin-wait

## Proxies - thread affinity
- all proxies of JS objects have thread affinity
- all of them need to be used and disposed on correct thread
    - how to dispatch to correct thread is one of the questions here
- all of them are registered to 2 GCs
    - `Dispose` need to be schedule asynchronously instead of blocking Mono GC
        - because of the proxy thread affinity, but the target thread is suspended during GC, so we could not dispatch to it, at that time.
        - the JS handles need to be freed only after both sides unregistered it (at the same time).
- `JSObject`
    - have thread ID on them, so we know which thread owns them
- `JSException`
    - they are a proxy because stack trace is lazy
    - we could eval stack trace eagerly, so they could become "value type"
        - but it would be expensive
- `Task`
    - continuations need to be dispatched onto correct JS thread
    - they can't be passed back to wrong JS thread
    - resolving `Task` could be async
- `Func`/`Action`/`JSImport`
    - callbacks need to be dispatched onto correct JS thread
    - they can't be passed back to wrong JS thread
    - calling functions which return `Task` could be aggressively async
        - unless the synchronous part of the implementation could throw exception
        - which maybe our HTTP/WS could do ?
        - could this difference be ignored ?
- `JSExport`/`Function`
    - we already are on correct thread in JS, unless this is UI thread
    - would anything improve if we tried to be more async ?
- `MonoString`
    - we have optimization for interned strings, that we marshal them only once by value. Subsequent calls in both directions are just a pinned pointer.
    - in deputy design we could create `MonoString` instance on the UI thread, but it involves GC barrier

## JSWebWorker with JS interop
- is proposed concept to let user to manage JS state of the worker explicitly
    - because of problem **4**
- is C# thread created and disposed by new API for it
- could block on synchronization primitives
- could do full JSImport/JSExport to it's own JS `self` context
- there is `JSSynchronizationContext`` installed on it
    - so that user code could dispatch back to it, in case that it needs to call `JSObject` proxy (with thread affinity)
- this thread needs to throw on any `.Wait` because of the problem **7**

## HTTP and WS clients
- are implemented in terms of `JSObject` and `Promise` proxies
- they have thread affinity, see above
    - typically to the `JSWebWorker` of the creator
- but are consumed via their C# Streams from any thread.
    - therefore need to solve the dispatch to correct thread.
        - such dispatch will come with overhead
        - especially when called with small buffer in tight loop
    - or we could throw PNSE, but it may be difficult for user code to
        - know what thread created the client
        - have means how to dispatch the call there
    - other unknowing users are `XmlUrlResolver`, `XmlDownloadManager`, `X509ResourceClient`, ...
- because we could have blocking wait now, we could also implement synchronous APIs of HTTP/WS
    - so that existing user code bases would just work without change
    - this would also require separate thread, doing the async job
    - we could use I/O thread for it

## Performance
As compared to ST build for dotnet wasm:
- the dispatch between threads (caused by JS object thread affinity) will have negative performance impact on the JS interop
- in case of HTTP/WS clients used via Streams, it could be surprizing
- browser performance is lower when working with SharedArrayBuffer
- Mono performance is lower because there are GC safe-points and locks in the VM code
- startup is slower because creation of WebWorker instances is slow
- VFS access is slow because it's dispatched to UI thread
- console output is slow because it's POSIX stream is dispatched to UI thread, call per line

# State 2023 September
 - we already ship MT version of the runtime in the wasm-tools workload.
 - It's enabled by `<WasmEnableThreads>true</WasmEnableThreads>` and it requires COOP HTTP headers.
 - It will serve extra file `dotnet.native.worker.js`.
 - This will also start in Blazor project, but UI rendering would not work.
 - we have pre-allocated pool of browser Web Workers which are mapped to pthread dynamically.
 - we can configure pthread to keep running after synchronous thread_main finished. That's necessary to run any async tasks involving JavaScript interop.
 - legacy interop has problems with GC boundaries.
 - JSImport & JSExport work
 - There is private JSSynchronizationContext implementation which is too synchronous
 - There is draft of public C# API for creating JSWebWorker with JS interop. It must be dedicated un-managed resource, because we could not cleanup JS state created by user code.
 - There is MT version of HTTP & WS clients, which could be called from any thread but it's also too synchronous implementation.
 - Many unit tests fail on MT https://github.com/dotnet/runtime/pull/91536
 - there are MT C# ref assemblies, which don't throw PNSE for MT build of the runtime for blocking APIs.

# Alternatives - as considered 2023 Sep
- how to deal with blocking C# code on UI thread
    - **A)** pretend it's not a problem (this we already have)
    - **B)** move user C# code to web worker
    - **C)** move all Mono to web worker
    - **D)** like **A)** just move call of the C# `Main()` to `JSWebWorker`
- how to deal with blocking in synchronous JS calls from UI thread (like `onClick` callback)
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
    - **f)** in first calling `JSWebWorker` managed thread
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
- where to implement sync-to-async: crypto/DLL download/HTTP APIs/
    - **r)** out of scope
    - **s)** in the UI thread
    - **t)** in a dedicated web worker
    - **z)** in the sidecar or deputy
- where to marshal JSImport/JSExport parameters/return/exception
    - **u)** could be only values types, proxies out of scope
    - **v)** could be on UI thread (with deputy design and Mono there)
    - **w)** could be on sidecar (with double proxies of parameters via comlink)
    - **x)** could be on sidecar (with comlink calls per parameter)

## Interesting combinations

### (8) Minimal support
- **A,D,G,L,P,S,U,Y,a,f,h,l,n,p,v**
- this is what we [already have today](#Current-state-2023-Sep)
- it could deadlock or die,
- JS interop on threads requires lot of user code attention
- Keeps problems **1,2,3,4**

### (9) Sidecar + no JS interop + narrow Blazor support
- **C,E,G,L,P,S,U,Z,c,d,h,m,o,q,u**
- minimal effort, low risk, low capabilities
- move both emscripten and Mono VM sidecar thread
- no user code JS interop on any thread
- internal solutions for Blazor needs
- Ignores problems **1,2,3,4,5**

### (10) Sidecar + only async just JS proxies UI + JSWebWorker + Blazor WASM server
- **C,E,H,N,P,S,U,W+Y,c,e+f,h+k,m,o,q,w**
- no C or managed code on UI thread
    - this architectural clarity is major selling point for sidecar design
- no support for blocking sync JSExport calls from UI thread (callbacks)
    - it will throw PNSE
- this will create double proxy for `Task`, `JSObject`, `Func<>` etc
    - difficult to GC, difficult to debug
- double marshaling of parameters
- Solves **1,2** for managed code.
- Avoids **1,2** for JS callback
    - emscripten main loop stays responsive only when main managed thread is idle
- Solves **3,4,5**

### (11) Sidecar + async & sync just JS proxies UI + JSWebWorker + Blazor WASM server
- **C,F,H,N,P,S,U,W+Y,c,e+f,h+k,m,o,q,w**
- no C or managed code on UI thread
- support for blocking sync JSExport calls from UI thread (callbacks)
    - at blocking the UI is at least well isolated from runtime code
    - it makes responsibility for sync call clear
- this will create double proxy for `Task`, `JSObject`, `Func<>` etc
    - difficult to GC, difficult to debug
- double marshaling of parameters
- Solves **1,2** for managed code
    - unless there is sync `JSImport`->`JSExport` call
- Ignores **1,2** for JS callback
    - emscripten main loop stays responsive only when main managed thread is idle
- Solves **3,4,5**

### (12) Deputy + managed dispatch to UI + JSWebWorker + with sync JSExport
- **B,F,K,N,Q,S/T,U,W,a/b/c,d+f,h,l,n,s/z,v**
- this uses `JSSynchronizationContext` to dispatch calls to UI thread
    - this is "dirty" as compared to sidecar because some managed code is actually running on UI thread
    - it needs to also use `SynchronizationContext` for `JSExport` and callbacks, to dispatch to deputy.
- blazor render could be both legacy render or Blazor server style
    - because we have both memory and mono on the UI thread
- Solves **1,2** for managed code
    - unless there is sync `JSImport`->`JSExport` call
- Ignores **1,2** for JS callback
    - emscripten main loop could deadlock on sync JSExport
- Solves **3,4,5**

### (13) Deputy + emscripten dispatch to UI + JSWebWorker + with sync JSExport
- **B,F,J,N,R,T,U,W,a/b/c,d+f,i,l,n,s,v**
- is variation of **(12)**
    - with emscripten dispatch and marshaling in UI thread
- this uses `emscripten_dispatch_to_thread_async` for `call_entry_point`, `complete_task`, `cwraps.mono_wasm_invoke_method_bound`, `mono_wasm_invoke_bound_function`, `mono_wasm_invoke_import`, `call_delegate_method` to get to the UI thread.
- it uses other `cwraps` locally on UI thread, like `mono_wasm_new_root`, `stringToMonoStringRoot`, `malloc`, `free`, `create_task_callback_method`
    - it means that interop related managed runtime code is running on the UI thread, but not the user code.
    - it means that parameter marshalling is fast (compared to sidecar)
        - this deputy design is major selling point #2
    - it still needs to enter GC barrier and so it could block UI for GC run shortly
- blazor render could be both legacy render or Blazor server style
    - because we have both memory and mono on the UI thread
- Solves **1,2** for managed code
    - unless there is sync `JSImport`->`JSExport` call
- Ignores **1,2** for JS callback
    - emscripten main loop could deadlock on sync JSExport
- Solves **3,4,5**

### (14) Deputy + emscripten dispatch to UI + JSWebWorker + without sync JSExport
- **B,F,J,N,R,T,U,W,a/b/c,d+f,i,l,n,s,v**
- is variation of **(13)**
    - without support for synchronous JSExport
- Solves **1,2** for managed code
    - emscripten main loop stays responsive
    - unless there is sync `JSImport`->`JSExport` call
- Avoids **2** for JS callback
    - by throwing PNSE
- Solves **3,4,5**

### (15) Deputy + Sidecar + UI thread
- 2 levels of indirection.
- benefit: blocking JSExport from UI thread doesn't block emscripten loop
- downside: complex and more resource intensive

### (16) Deputy without Mono, no GC barrier breach for interop
- variation on **(13)** or **(14)** where we get rid of per-parameter calls to Mono
- benefit: get closer to purity of sidecar design without loosing perf
    - this could be done later as purity optimization
- in this design the mono could be started on deputy thread
    - this will keep UI responsive during startup
- UI would not be mono attached thread.
- See [details](#Get-rid-of-Mono-GC-boundary-breach)

Related Net8 tracking https://github.com/dotnet/runtime/issues/85592

### (17) Emscripten em_queue in deputy, managed UI thread
- is interesting because it avoids cross-thread dispatch to UI
    - including double dispatch in Blazor's `RendererSynchronizationContext`
- avoids solving **1,2**
- low level hacking of emscripten design assumptions

### (18) Soft deputy
- keep both Mono and emscripten in the UI thread
- use `SynchronizationContext` to do the dispatch
- make it easy and default to run any user code in deputy thread
    - all Blazor events and callbacks like `onClick` to deputy
    - move SignalR to deputy
    - move Blazor entry point to deputy
- hope that UI thread is mostly idle
    - enable dynamic thread allocation
    - throw exceptions in dev loop when UI thread does `lock` or `.Wait` in user code

### (19) Single threaded build in a WebWorker
- this already works well in Net8
- when the developer is able to start dotnet in the worker himself and also handle all the messaging.
- there are known existing examples in the community

## Sidecar options
There are few downsides to them
- if we keep main managed thread and emscripten thread the same, pthreads can't be created dynamically
    - we could upgrade it to design **(15)** and have extra thread for running managed `Main()`
- we will have to implement extra layer of dispatch from UI to sidecar
    - this could be pure JS via `postMessage`, which is slow and can't do spin-wait.
    - we could have `SharedArrayBuffer` for the messages, but we would have to implement (another?) marshaling.

## Dispatching call, who is responsible
- User code
    - this is difficult and complex task which many will fail to do right
    - it can't be user code for HTTP/WS clients because there is no direct call via Streams
    - authors of 3rd party components would need to do it to hide complexity from users
- Roslyn generator: JSImport - if we make it responsible for the dispatch
    - it needs to stay backward compatible with Net7, Net8 already generated code
        - how to detect that there is new version of generated code ?
    - it needs to do it via public C# API
        - possibly new API `JSHost.Post` and `JSHost.Send`
        - or `JSHost.InvokeInTargetSync` and `JSHost.InvokeInTargetAsync`
    - it needs to re-consider current `stackalloc`
        - probably by re-ordering Roslyn generated code of `__arg_return.ToManaged(out __retVal);` before `JSFunctionBinding.InvokeJS`
    - it needs to propagate exceptions
- Roslyn generator: JSExport - can't be used
    - this is just the UI -> deputy dispatch, which is not C# code
- Mono/C/JS internal layer
    - see `emscripten_dispatch_to_thread_async` below

## Dispatching JSImport - what should happen
- when there is no extra code-gen flag
    - for backward compatibility, dispatch handled by user
    - assert that we are on `JSWebWorker` or main thread
    - assert all parameters affinity to current thread
- when generated with `JSImportAttribute.Affinity==UI`
    - dispatch to UI thread
    - assert all parameters affinity to UI thread
    - could be called from any thread, including thread pool
    - there is no `JSSynchronizationContext` in deputy's UI, use emscripten.
    - emscripten can't block caller
- when generated with `JSImportAttribute.Affinity==ByParams`
    - dispatch to thread of parameters
    - assert all parameters have same affinity
    - could be called from any thread, including thread pool

## Dispatching JSImport in deputy design - how to do it
- how to dispatch to UI in deputy design ?
    - A) double dispatch, C# -> main, emscripten -> UI
    - B) make whole dispatch emscripten only, implement blocking wait in C for emscripten sync calls.
    - C) only allow sync call on non-UI target
- how to obtain `JSHandle` of function in the target thread ?
    - there are 2 dimensions: the thread and the method
    - there are 2 steps:
    - A) obtain existing `JSHandle` on next call (if available)
        - to avoid double dispatch, this needs to be accessible
            - by any caller thread
            - or by UI thread C code (not managed)
    - B) if this is first call to the method on the target thread, create the target `JSHandle` by binding existing JS function
        - collecting the metadata is generated C# code
        - therefore we need to get the metadata buffer on caller main thread: double dispatch
        - store new `JSHandle` somewhere
- possible solution
    assign `static` unique ID to the function on C# side during first call.
    - A) Call back to C# if the method was not bound yet (which thread ?).
    - B) Keep the metadata buffer
        - make `JSFunctionBinding` registration static (not thread-static)
            - never free the buffer
        - pass the buffer on each call to the target
        - late bind `JSHandle`
        - store the `JSHandle` on JS side (thread static) associated with method ID
- TODO: double dispatch in Blazor

## Dispatching JSExport - alternatives
- when caller is UI, we need to dispatch back to managed thread
    - preferably deputy or sidecar thread
- when caller is `JSWebWorker`,
    - we are probably on correct thread already
    - when caller is callback from HTTP/WS we could dispatch to any managed thread
- callers are not from managed thread pool, by design. Because we don't want any JS code running there.

## Dispatching call - alternatives
- `JSSynchronizationContext` - in deputy design
    - this would not work for dispatch to UI thread as it doesn't have sync context
    - is implementation of `SynchronizationContext` installed to
    - managed thread with `JSWebWorker`
    - or main managed thread
    - it has asynchronous `SynchronizationContext.Post`
    - it has synchronous `SynchronizationContext.Send`
        - can propagate caller stack frames
        - can propagate exceptions from callee thread
    - when the method is async
        - we can schedule it asynchronously to the `JSWebWorker` or main thread
        - propagate result/exceptions via `TaskCompletionSource.SetException` from any managed thread
    - when the method is sync
        - create internal `TaskCompletionSource`
        - we can schedule it asynchronously to the `JSWebWorker` or main thread
        - we could block-wait on `Task.Wait` until it's done.
        - return sync result
    - this would not work in sidecar design
        - because UI is not managed thread there
- `emscripten_dispatch_to_thread_async` - in deputy design
    - can dispatch async call to C function on the timer loop of target pthread
    - doesn't block and doesn't propagate results and exceptions
    - from JS (UI) to C# managed main
        - only necessary for deputy/sidecar, not for HTTP
        - async
            - `malloc` stack frame and do JS side of marshaling
            - re-order `marshal_task_to_js` before `invoke_method_and_handle_exception`
                - pre-create `JSHandle` of a `promise_controller`
                - pass `JSHandle` instead of receiving it
            - send the message via `emscripten_dispatch_to_thread_async`
            - return the promise immediately
            - await until `mono_wasm_resolve_or_reject_promise` is sent back
                - this need to be also dispatched
                - how could we make that dispatch same for HTTP cross-thread by `JSObject` affinity ?
            - any errors in messaging will `abort()`
        - sync
            - dispatch C function
            - which will lift Atomic semaphore at the end
            - spin-wait for semaphore
            - stack-frame could stay on stack
        - synchronously returning `null` `Task?`
            - pass `slot.ElementType = MarshalerType.Discard;` ?
            - `abort()` ?
            - `resolve(null)` ?
            - `reject(null)` ?
    - from C# to JS (UI)
        - how to obtain JSHandle of function in the target thread ?
        - async
            - needs to deal with `stackalloc` in C# generated stub, by copying the buffer
        - sync
            - inside `JSFunctionBinding.InvokeJS`:
            - create internal `TaskCompletionSource`
            - use async dispatch above
            - block-wait on `Task.Wait` until it's done.
                - !! this would not keep receiving JS loop events !!
            - return sync result
        - implementation calls
            - `BindJSFunction`, `mono_wasm_bind_js_function` -  many out params, need to be sync call to UI
            - `BindCSFunction`, `mono_wasm_bind_cs_function` -  many out params, need to be sync call to UI
            - `ReleaseCSOwnedObject`, `mono_wasm_release_cs_owned_object` -  async message to UI
            - `ResolveOrRejectPromise`, `mono_wasm_resolve_or_reject_promise` -  async message to UI
            - `InvokeJSFunction`, `mono_wasm_invoke_bound_function` -  depending on signature, via FuncJS.ResMarshaler
            - `InvokeImport`, `mono_wasm_invoke_import` -  depending on signature, could be sync or async message to UI
            - `InstallWebWorkerInterop`, `mono_wasm_install_js_worker_interop` - could become async
            - `UninstallWebWorkerInterop`, `mono_wasm_uninstall_js_worker_interop` - could become async
            - `RegisterGCRoot`, `mono_wasm_register_root` -  could stay on deputy
            - `DeregisterGCRoot`, `mono_wasm_deregister_root` -  could stay on deputy
            - hybrid globalization, could probably stay on deputy
- `emscripten_sync_run_in_main_runtime_thread` - in deputy design
    - can run sync method in UI thread
- "comlink" - in sidecar design
    - when the method is async
        - extract GCHandle of the `TaskCompletionSource`
        - convert parameters to JS (sidecar context)
            - using sidecar Mono `cwraps` for marshaling
        - call UI thread via "comlink"
            - will create comlink proxies
        - capture JS result/exception from "comlink"
        - use stored `TaskCompletionSource` to resolve the `Task` on target thread
- `postMessage`
    - can send serializable message to web worker
    - can transmit [transferable objects](https://developer.mozilla.org/en-US/docs/Web/API/Web_Workers_API/Transferable_objects)
    - doesn't block and doesn't propagate exceptions
    - this is slow

## Spin-waiting in JS
- if we want to keep synchronous JS APIs to work on UI thread, we have to spin-wait
    - we probably should have opt-in configuration flag for this
    - making user responsible for the consequences
- at the moment emscripten implements spin-wait in wasm
    - See [pthread_cond_timedwait.c](https://github.com/emscripten-core/emscripten/blob/cbf4256d651455abc7b3332f1943d3df0698e990/system/lib/libc/musl/src/thread/pthread_cond_timedwait.c#L117-L118) and [__timedwait.c](https://github.com/emscripten-core/emscripten/blob/cbf4256d651455abc7b3332f1943d3df0698e990/system/lib/libc/musl/src/thread/__timedwait.c#L67-L69)
    - I was not able to confirm that they would call `emscripten_check_mailbox` during spin-wait
    - See also https://emscripten.org/docs/porting/pthreads.html
- in sidecar design - emscripten main is not running in UI thread
    - it means it could still pump events and would not deadlock in Mono or managed code
    - unless the sidecar thread is blocked, or CPU hogged, which could happen
    - we need pure JS version of spin-wait and we have OK enough prototype
- in deputy design - emscripten main is running in UI thread
    - but the UI thread is not running managed code
    - it means it could still pump events and would not deadlock in Mono or managed code
        - this deputy design is major selling point #1
    - unless user code opts-in to call sync JSExport
- it could still deadlock if there is synchronous JSImport call to UI thread while UI thread is spin-waiting on it.
    - this would be clearly user code mistake

## Blazor
- as compared to single threaded runtime, the major difference would be no synchronous callbacks.
    - for example from DOM `onClick`. This is one of the reasons people prefer ST WASM over Blazor Server.
- Blazor `renderBatch`
    - currently `Blazor._internal.renderBatch` -> `MONO.getI16`, `MONO.getI32`, `MONO.getF32`, `BINDING.js_string_to_mono_string`, `BINDING.conv_string`, `BINDING.unbox_mono_obj`
    - we could also [RenderBatchWriter](https://github.com/dotnet/aspnetcore/blob/045afcd68e6cab65502fa307e306d967a4d28df6/src/Components/Shared/src/RenderBatchWriter.cs) in the WASM
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

## WebPack, Rollup friendly
- it's not clear how to make this single-file
- because web workers need to start separate script(s) via `new Worker('./dotnet.js', {type: 'module'})`
    - we can start a WebWorker with a Blob, but that's not CSP friendly.
    - when bundled together with user code, into `./my-react-application.js` we don't have way how to `new Worker('./dotnet.js')` anymore.
- emscripten uses `dotnet.native.worker.js` script. At the moment we don't have nice way how to customize what is inside of it.
- for ST build we have JS API to replace our dynamic `import()` of our modules with provided instance of a module.
    - we would have to have some way how 3rd party code could become responsible for doing it also on web worker (which we start)
- what other JS frameworks do when they want to be webpack friendly and create web workers ?

## Subtle crypto
- once we have have all managed threads outside of the UI thread
- we could synchronously wait for another thread to do async operations
- and use [async API of subtle crypto](https://developer.mozilla.org/en-US/docs/Web/API/SubtleCrypto)

## Lazy DLL download
- once we have have all managed threads outside of the UI thread
- we could synchronously wait for another thread to do async operations
- to fetch another DLL which was not pre-downloaded

## Remove Mono from UI thread
- Get rid of Mono GC boundary breach
- see https://github.com/dotnet/runtime/issues/100411
