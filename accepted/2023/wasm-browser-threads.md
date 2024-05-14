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

For other possible design options we considered [see below](#alternatives-and-details---as-considered-2023-sep).

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

# Alternatives and details - as considered 2023 Sep
See https://gist.github.com/pavelsavara/c81ef3a9e4000d67f49ddb0f1b1c2284