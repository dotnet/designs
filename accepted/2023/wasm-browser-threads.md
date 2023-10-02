# Multi-threading on a browser

## Goals
- CPU intensive workloads on dotnet thread pool
- enable blocking `Task.Wait` and `lock()` like APIs from C# user code on all threads
    - Current public API throws PNSE for it
    - This is core part on MT value proposition.
    - If people want to use existing MT code-bases, most of the time, the code is full of locks.
    - People want to use existing desktop/server multi-threaded code as is.
- allow HTTP and WS C# APIs to be used from any thread despite underlying JS object affinity
- JSImport/JSExport interop in maximum possible extent
- don't change/break single threaded build. †

## Lower priority goals
- try to make it debugging friendly
- sync C# to async JS
    - dynamic creation of new pthread
    - implement crypto via `subtle` browser API
    - allow lazy `[DLLImport]` to download from the server
    - implement synchronous APIs of the HTTP and WS clients. At the moment they throw PNSE.
- sync JS to async JS to sync C#
    - allow calls to synchronous JSExport from UI thread (callback)
- don't prevent future marshaling of JS [transferable objects](https://developer.mozilla.org/en-US/docs/Web/API/Web_Workers_API/Transferable_objects), like streams and canvas.

<sub><sup>† Note: all the text below discusses MT build only, unless explicit about ST build.</sup></sub>

## Key idea in this proposal

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
    - Firefox (still) has synchronous `XMLHttpRequest` which could be captured by async code in service worker
        - it's [deprecated legacy API](https://developer.mozilla.org/en-US/docs/Web/API/XMLHttpRequest/Synchronous_and_Asynchronous_Requests#synchronous_request)
        - [but other browsers don't](https://wpt.fyi/results/service-workers/service-worker/fetch-request-xhr-sync.https.html?label=experimental&label=master&aligned) and it's unlikely they will implement it
        - there are deployment and security challenges with it
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

## Define terms
- UI thread
    - this is the main browser "thread", the one with DOM on it
    - it can't block-wait, only spin-wait
- "sidecar" thread - possible design
    - is a web worker with emscripten and mono VM started on it
    - for Blazor rendering MAUI/BlazorWebView use the same concept
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
- "comlink"
    - in this document it stands for the pattern
        - dispatch to another worker via pure JS means
        - create JS proxies for types which can't be serialized, like `Function`
    - actual [comlink](https://github.com/GoogleChromeLabs/comlink)
        - doesn't implement spin-wait
    - we already have prototype of the similar functionality
        - which can spin-wait

## Implementation options (only some combinations are possible)
- how to deal with blocking C# code on UI thread
    - **A)** pretend it's not a problem (this we already have)
    - **B)** move user C# code to web worker
    - **C)** move all Mono to web worker
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
- where to implement sync-to-async: crypto/DLLImport/HTTP APIs/
    - **r)** out of scope
    - **s)** in the UI thread
    - **t)** in a dedicated web worker
    - **z)** in the sidecar or deputy
- where to marshal JSImport/JSExport parameters/return/exception
    - **u)** could be only values types, proxies out of scope
    - **v)** could be on UI thread (with deputy design and Mono there)
    - **w)** could be on sidecar (with double proxies of parameters via comlink)
    - **x)** could be on sidecar (with comlink calls per parameter)

# Interesting combinations

## (8) Minimal support
- **A,D,G,L,P,S,U,Y,a,f,h,l,n,p,v**
- this is what we [already have today](#Current-state-2023-Sep)
- it could deadlock or die,
- JS interop on threads requires lot of user code attention
- Keeps problems **1,2,3,4**

## (9) Sidecar + no JS interop + narrow Blazor support
- **C,E,G,L,P,S,U,Z,c,d,h,m,o,q,u**
- minimal effort, low risk, low capabilities
- move both emscripten and Mono VM sidecar thread
- no user code JS interop on any thread
- internal solutions for Blazor needs
- Ignores problems **1,2,3,4,5**

## (10) Sidecar + only async just JS proxies UI + JSWebWorker + Blazor WASM server
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

## (11) Sidecar + async & sync just JS proxies UI + JSWebWorker + Blazor WASM server
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

## (12) Deputy + managed dispatch to UI + JSWebWorker + with sync JSExport
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

## (13) Deputy + emscripten dispatch to UI + JSWebWorker + with sync JSExport
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

## (14) Deputy + emscripten dispatch to UI + JSWebWorker + without sync JSExport
- **B,F,J,N,R,T,U,W,a/b/c,d+f,i,l,n,s,v**
- is variation of **(13)**
    - without support for synchronous JSExport
- Solves **1,2** for managed code
    - emscripten main loop stays responsive
    - unless there is sync `JSImport`->`JSExport` call
- Avoids **2** for JS callback
    - by throwing PNSE
- Solves **3,4,5**

# Details

## JSImport and marshaled JS functions
- both sync and async could be called on all `JSWebWorker` threads
- both sync and async could be called on main managed thread (even when running on UI)
    - unless there is loop back to blocking `JSExport`, it could not deadlock

## JSExport & C# delegates
- async could be called on all `JSWebWorker` threads
- sync could be called on `JSWebWorker`
- sync could be called on from UI thread is problematic
    - with spin-wait in UI in JS it has **2)** problems
    - with spin-wait in UI when emscripten is there could also deadlock the rest of the app
        - this means that combination of sync JSExport and deputy design is dangerous

## Proxies - thread affinity
- all of them have thread affinity
- all of them need to be used and disposed on correct thread
    - how to dispatch to correct thread is one of the questions here
- all of them are registered to 2 GCs
    - maybe `Dispose` could be schedule asynchronously instead of blocking Mono GC
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
        - could this difference we ignored ?
- `JSExport`/`Function`
    - we already are on correct thread in JS, unless this is UI thread
    - would anything improve if we tried to be more async ?

## JSWebWorker with JS interop
- is proposed concept to let user to manage JS state of the worker explicitly
    - because of problem **4**
- is C# thread created and disposed by new API for it
- could block on synchronization primitives
- could do full JSImport/JSExport to it's own JS `self` context
- there is `JSSynchronizationContext`` installed on it
    - so that user code could dispatch back to it, in case that it needs to call `JSObject` proxy (with thread affinity)

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
- because we could have blocking wait now, we could also implement synchronous APIs of HTTP/WS
    - so that existing user code bases would just work without change
    - at the moment they throw PNSE
    - this would also require separate thread, doing the async job

## JSImport calls on threads without JSWebWorker
- those are
    - thread-pool threads
    - main managed thread in deputy design
- what should happen when it calls JSImport directly ?
- what should happen when it calls HTTP/WS clients ?
- we could dispatch it to UI thread
    - easy to understand default behavior
    - downside is blocking the UI and emscripten loops with CPU intensive activity
    - in sidecar design, also extra copy of buffers
- we could instead create dedicated `JSWebWorker` managed thread
    - more difficult to reason about
    - this extra worker could also serve all the sync-to-async jobs

# Dispatching call, who is responsible
- User code
    - this is difficult and complex task which many will fail to do right
    - it can't be user code for HTTP/WS clients because there is no direct call via Streams
    - authors of 3rd party components would need to do it to hide complexity from users
- Roslyn generator: JSImport - if we make it responsible for the dispatch
    - it needs to stay backward compatible with Net7, Net8 already generated code
        - how to detect that there is new version of generated code ?
    - it needs to do it via public C# API
        - possibly new API `JSHost.Post` or `JSHost.Send`
    - it needs to re-consider current `stackalloc`
        - probably by re-ordering Roslyn generated code of `__arg_return.ToManaged(out __retVal);` before `JSFunctionBinding.InvokeJS`
    - it needs to propagate exceptions
- Roslyn generator: JSExport - if we make it responsible for the dispatch
- Mono/C/JS internal layer
    - see `emscripten_dispatch_to_thread_async` below

# Dispatching JSImport - what should happen
- is normally bound to JS context of the calling managed thread
- but it could be called with `JSObject` parameters which are bound to another thread
    - if targets don't match each other throw `ArgumentException` ?
    - if it's called from thread-pool thread
        - which is not `JSWebWorker`
        - should we dispatch it by affinity of the parameters ?
    - if parameters affinity do match each other but not match current `JSWebWorker`
        - should we dispatch it by affinity of the parameters ?
        - this would solve HTTP/WS scenarios

# Dispatching JSExport - what should happen
- when caller is UI, we need to dispatch back to managed thread
    - preferably deputy or sidecar thread
- when caller is `JSWebWorker`,
    - we are probably on correct thread already
    - when caller is callback from HTTP/WS we could dispatch to any managed thread
- caller can't be managed thread pool, because they would not use JS `self` context

# Dispatching call - options
- `JSSynchronizationContext` - in deputy design
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
    - doesn't block and doesn't propagate exceptions
    - needs to deal with `stackalloc` in C# generated stub
        - probably by re-ordering Roslyn generated code
    - when the method is async
        - extract GCHandle of the `TaskCompletionSource`
        - copy "stack frame" and pass it to
        - asynchronously schedule to the target pthread via `emscripten_dispatch_to_thread_async`
        - unpack the "stack frame"
            - using local Mono `cwraps` for marshaling
        - capture JS result/exception
        - use stored `TaskCompletionSource` to resolve the `Task` on target thread
    - when the method is sync
        - inside `JSFunctionBinding.InvokeJS`:
        - create internal `TaskCompletionSource`
        - use async dispatch above
        - block-wait on `Task.Wait` until it's done.
        - return sync result
    - or when the method is sync
        - do something similar in C or JS
    - this would not work in sidecar design
        - because UI is not managed thread there
        - Mono cwraps are not available either
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
    - doesn't block and doesn't propagate exceptions
    - this is slow

## Performance
- the dispatch between threads (caused by JS object thread affinity) will have negative performance impact on the JS interop
- in case of HTTP/WS clients used via Streams, it could be surprizing

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

## Debugging
- VS debugger would work as usual
- Chrome dev tools would only see the events coming from `postMessage` or `Atomics.waitAsync`
- Chrome dev tools debugging C# could be bit different, it possibly works already. The C# code would be in different node of the "source" tree view

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

## Lazy DLLImport - download
- once we have have all managed threads outside of the UI thread
- we could synchronously wait for another thread to do async operations
- to fetch another DLL which was not pre-downloaded

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
- [ ] optional: make underlying emscripten WebWorker pool allocation dynamic, or provide C# API for that.
- [ ] optional: implement async function/delegate marshaling in JSImport/JSExport parameters.
- [ ] optional: enable blocking HTTP/WS APIs
- [ ] optional: enable lazy DLL download by blocking the caller
- [ ] optional: implement crypto
- [ ] measure perf impact

Related Net8 tracking https://github.com/dotnet/runtime/issues/85592