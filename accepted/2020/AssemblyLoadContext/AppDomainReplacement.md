# .NET Core 3.0 AppDomain Replacement Design and Guidance

**Owner** [Steve MacLean](https://github.com/sdmaclea)

.NET Core chose to not fully support the `System.AppDomain` type as
implemented in .NET Framework

## Code Migration Use Cases

In order of increasing difficulty:

1. No changes required

    * .NET Framework application code which uses a single `AppDomain` can
    be ported to .NET Core without change. This is a common scenario
    enabled by .NET Standard 2.0 and is well supported.

2. Eliminate usage of multiple `AppDomain`s w/o adding multiple
`AssemblyLoadContext`s

    * If possible, consider migrating to a single `AppDomain` w/ single
    `AssemblyLoadContext`.  Multiple `AppDomain`s and IPC add complexity.
    They should be used only when strictly necessary.
    * This is a good choice if:
        * No security sandbox is required
        * No need to support loading multiple versions of assemblies.
        * No need to load multiple instances on an assembly to have
        multiple instances of static state.
        * No need to unload assemblies.

3. Migrate to Inter Process Communication (IPC)

    * Security features are no longer supported. Modern security
    theory makes it clear it is extremely difficult to create secure
    sandboxes within processes. .NET Core treats all code within a
    process with full trust. It relies on the OS or the hypervisor to
    provide all security features. All Multiple `AppDomain`
    implementations requiring security features should migrate to using
    IPC.
    * Running multiple runtimes in the same process is not supported.
    Apps requiring plugins running in other frameworks should consider
    IPC.
    * Consider using established IPC packages:
        * gRPC is highly recommended. It is based on the well
        respected Google Protocol Buffers. It supports multiple
        languages, cross language, and has good documentation.
        See:
            * [gRPC site](https://grpc.io/)
            * [gRPC C# Quick Start](https://grpc.io/docs/quickstart/csharp.html)
            * [gRPC C# Basics Tutorial](https://grpc.io/docs/tutorials/basic/csharp.html)
            * [gRPC C# API Reference](https://grpc.io/grpc/csharp/api/Grpc.Core.html)
            * [gRPC on github](https://github.com/grpc/grpc)
            * [Grpc.AspNetCore.Server](https://www.nuget.org/packages/Grpc.AspNetCore.Server)
        * Current gRPC limitations (wishlist):
                * Remoting centric
                * Uses server and http transport
                * No current support for named pipes or shared memory
            * Requires authoring cross platform `proto` files. Development
            is therefore not using .NET familiar technologies.
        * Other known remoting libraries
            * Oren Novotny has authored a library to help solve the
            common desktop app problem of launching only a single
            instance. The solution is using named pipes to provide
            remoting.
                * [Oren Novotny's SingleInstanceHelper](https://github.com/onovotny/SingleInstanceHelper)

4. Migrate to `System.Reflection.MetadataLoadContext` for inspection &
reflection only load

    * .NET Core does not support the ReflectionOnlyLoad feature of
    .NET Framework. ReflectionOnlyLoad was a feature for inspecting
    managed assemblies using the familiar Reflection api. The
    `MetadataLoadContext` class is the .NET Core replacement for this
    feature.
    * [`System.Reflection.MetadataLoadContext` API docs](https://docs.microsoft.com/dotnet/api/system.reflection.metadataloadcontext)
    * [`System.Reflection.PathAssemblyResolver` API docs](https://docs.microsoft.com/dotnet/api/system.reflection.pathassemblyresolver)
    * [`MetadataLoadContext` prototype design doc](https://github.com/dotnet/corefxlab/blob/master/docs/specs/typeloader.md)

5. Migrate to using single `AppDomain` with multiple `AssemblyLoadContext`s

    * This is an advanced scenario. It should not be used without
    seriously considering the benefits and costs of other solutions.
    * .NET 3.0 will make a good faith effort to help make this easier.
    * There are a few reasons for loading components/assemblies into
    multiple `AssemblyLoadContext`s:
        1. To allow assemblies to be unloaded when they are no longer
        needed. (**Consider** whether the memory savings is substantial
        enough to justify the complexity.)
        2. Allow for loading multiple copies of assemblies to allow
        different versions to be simultaneously loaded. (**Consider**
        resolving assembly versions at build time.  MSBuild will
        automatically do this if all components are built together as
        a single solution.)
        3. Allow for loading multiple copies of assemblies to have
        multiple instances of static state. (**Consider** removing usage
        of static state).
    * Successfully implementing this solution will require more
    design, development, debug, documentation, and maintenance.

## Multiple AssemblyLoadContext Usage

### Recommendations

+ Keep all framework code in the default `AssemblyLoadContext`.
+ Dynamically loaded application components should be set to use a
roll-forward policy allowing aggressive roll forward.  Using a policy of
latest major would allow the most aggressive roll forward.
    * [High level roll forward design](https://github.com/dotnet/designs/blob/master/accepted/runtime-binding.md)
    * [Roll forward detailed implementation design](https://github.com/dotnet/core-setup/blob/master/Documentation/design-docs/framework-version-resolution.md)
+ Carefully control shared types. Shared types are types which will be
used in the interfaces which cross `AssemblyLoadContext` boundaries. These
types must be loaded from the same `AssemblyLoadContext` to prevent type
mismatches. Load shared types into a common `AssemblyLoadContext`.
This will often be the `AssemblyLoadContext.Default`.
+ Carefully control which assembly is loaded into which
`AssemblyLoadContext`.  This may be best implemented by having an
appropriate directory structure which corresponds to `AssemblyLoadContext`
intended structure.
    + Main application, dependencies, and shared dependencies in the
    main folder. Main deps.json does not have references to plug-in
    private assemblies
    + Each dynamically loaded application component (plug-in) in its own
    folder.  Its private dependencies in the same folder.  No shared
    dependencies in the folder. Each plug-in deps.json does not refer to
    shared dependencies
    + **TBD** Tooling will need to be updated to support this
+ Use .NET Core 3.0 or newer

### Pain points
#### Pain points - Type equality

`AssemblyLoadContext` uses a simple shared type model.  Types are
equivalent at runtime if and only if:

+ Same `AssemblyLoadContext`
+ Same `Assembly` file
+ Same Namespace & type name

Types are shared by sharing assemblies across `AssemblyLoadContext`s. An
assembly is loaded into one `AssemblyLoadContext`, but can be used to
resolve references for Assemblies in multiple `AssemblyLoadContext`s.

To get it right, the developer must make sure shared types are only
defined once.

One approach would be to define an interface into an assembly.  Have the
application reference this assembly and load it into the default
assembly.  The plug-ins also reference this, but do not load into their
`AssemblyLoadContext`.

#### Pain points - Debugger difficult

Debugging this has been difficult.

+ The debugger has not worked well. Significant improvement expected for
3.0.
+ `AssemblyLoadContext`s are not named making it difficult to understand
how/why various `AssemblyLoadContext`s were created. Fixed for 3.0.
+ Exception messages have not been very useful. "Type A cannot be cast
to type A" is somewhat disconcerting. Improvement expected for 3.1.

`AppDomain`s made this easier by using automatic wrapping to make proxies
which used reflection to cross `AppDomain` boundaries. Using identical
types was not required.

#### Pain points - Implicit AssemblyLoadContext

Switching `AppDomain`s required deliberate effort. Due to the isolation
strategy, it was simple to determine which `AppDomain` was in use by any
thread.

For the `AssemblyLoadContext` solution, .NET Core attempts to infer which
`AssemblyLoadContext` is in use by inferring it from the callers context.
It looks at the caller, determines which assembly is being executed,
then determines which `AssemblyLoadContext` it belongs to.

While this may seem OK in theory, it is a poor design and is dangerous:

+ The behavior of the system API method changes based on how it is called.
+ Reflection may not behave the same as direct code.
+ Calling a sensitive API through a framework may not work correctly.
This is currently breaking the `System.Xaml` implementation when using
multiple `AssemblyLoadContext`s.

The preferred solution is to migrate from the context sensitive APIs.
Each of the context sensitive APIs have an equivalent API which can
explicitly load assembly dependencies into the intended
`AssemblyLoadContext`.

For legacy reasons we have also introduced
`AssemblyLoadContext.CurrentContextualReflectionContext` to allow
controlling the inferred `AssemblyLoadContext`.
* [AssemblyLoadContext.CurrentContextualReflectionContext design doc](https://github.com/dotnet/coreclr/blob/master/Documentation/design-docs/AssemblyLoadContext.ContextualReflection.md)

#### Pain points - Dependency resolution & plug-in tooling

.NET Core uses a different dependency resolution mechanism.

For plug-ins the dependency model is not mature. For 3.0 we will:

+ add the `AssemblyDependencyResolver` to allow plugins to be loaded
successfully
+ **TBD** update SDK tooling as necessary to allow creating dynamically
loaded components.
    * [For example](https://github.com/dotnet/sdk/issues/2662)
    * **TBD** tooling updates to support referencing shared assemblies which
should not be included. [The support already exists, but it's buggy:](https://github.com/dotnet/sdk/issues/2660)
+ **TBD** We should think about whether we want to add a
FrameworkDependencyResolver to allow loading frameworks into the
non-default `AssemblyLoadContext`

#### Pain points - Difficult to control Assembly loading

Careless loading of Assemblies creates duplicate types. Each
`AssemblyLoadContext` should only load assemblies which it requires.

See recommendation above to control directory structure.

#### Pain points - Documentation & Samples

There has been a general lack of tutorial documentation, API
documentation, and samples.

#### Pain points - Complexity

Complexity of this solution is high.  It is difficult to reason about
all the moving parts.

## .NET Core 3.0 Improvements

+ API Documentation
    * [`AssemblyLoadContext` api](https://docs.microsoft.com/dotnet/api/system.runtime.loader.assemblyloadcontext)
    * [`AssemblyDependencyResolver` api](https://docs.microsoft.com/dotnet/api/system.runtime.loader.assemblydependencyresolver)
    * [`System.Reflection.MetadataLoadContext` API docs](https://docs.microsoft.com/dotnet/api/system.reflection.metadataloadcontext)
    * [`System.Reflection.PathAssemblyResolver` API docs](https://docs.microsoft.com/dotnet/api/system.reflection.pathassemblyresolver)
+ Guidance documentation (In progress)
+ Tutorials and Samples
    * [AppWithPluginSample](https://docs.microsoft.com/dotnet/core/tutorials/creating-app-with-plugin-support)
    * [`System.Reflection.TypeLoader` documentation and design doc](https://github.com/dotnet/corefxlab/blob/archive/docs/specs/typeloader.md)
    * [Unloading Sample](https://github.com/dotnet/samples/tree/main/core/tutorials/Unloading)
    * [gRPC C# Quick Start](https://grpc.io/docs/quickstart/csharp.html)
    * [gRPC C# Basics Tutorial](https://grpc.io/docs/tutorials/basic/csharp.html)
+ `AssemblyDependencyResolver`
+ `AssemblyLoadContext` concrete class
    * [API Review](https://github.com/dotnet/corefx/issues/34791)
+ `AssemblyLoadContext` Name, Contexts, Assemblies
    * [API Review](https://github.com/dotnet/corefx/issues/34791)
+ `AssemblyLoadContext.CurrentContextualReflectionContext`
    * [AssemblyLoadContext.CurrentContextualReflectionContext design doc](https://github.com/dotnet/coreclr/blob/master/Documentation/design-docs/AssemblyLoadContext.ContextualReflection.md)
+ `AssemblyLoadContext` fix Satellite assembly search
    * [Issue](https://github.com/dotnet/coreclr/issues/20979)
+ SDK support for plug-in shared references
+ Improved debugger support for `AssemblyLoadContext`s

## .NET Core 3.1 Improvement Wishlist

+ Improved type equivalence exception messages
+ Improved assembly dependency resolution logs
