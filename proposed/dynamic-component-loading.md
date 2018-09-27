# Dynamic component loading

## Problem statement
.NET Core runtime has only limited support for dynamically loading assemblies which have additional dependencies or possibly collide with the app in any way. Out of the box only these scenarios really work:
* Assemblies which have no additional dependencies other than those found in the app itself (`Assembly.Load`)
* Assemblies which have additional dependencies in the same folder and which don't collide with anything in the app itself (`Assembly.LoadFrom`)
* Loading a single assembly in full isolation (from the app), but all its dependencies must come from the app (`Assembly.LoadFile`)

Other scenarios are technically supported by implementing a custom `AssemblyLoadContext` but doing so is complex.  
Additionally, there's no inherent synergy with the .NET Core SDK tooling. Components produced by the SDK can't be easily loaded at runtime.

The goal of this feature is to provide an easy-to-use way to dynamically load a component with its dependencies.

## Scenarios
List of few scenarios where dynamic loading of full components is required:
* MSBuild tasks - all tasks in MSBuild are dynamically loaded. Some tasks come with additional dependencies which can collide with each other or MSBuild itself as well.
* Roslyn analyzers - similar to MSBuild tasks, the Roslyn compiler dynamically loads analyzers which are separate components with potentially conflicting dependencies.
* XUnit loading tests - the test runner acts as an app and the test is loaded dynamically. The test can have any number of dependencies. Finding and resolving those dependencies is challenging.
* ASP .NET's `dotnet watch` - ability to dynamically reload an app without restarting the process. Each version of the app is inherently in collision with any previous version. The old version should be unloaded.

In most of these cases the component which is to be loaded dynamically has a non-trivial set of dependencies which are unknown to the app itself. So the loading mechanism has to be able to resolve them.

## Declaring dependencies
The .NET Core SDK tooling produces `.deps.json` which is a dependency manifest for dotnet apps and components. It enables them to load dependencies from locations other than the base directory (ex: from packages, platform-specific publish directories, etc.).  
At application start .NET Core first builds a set of directories to look for binaries (called ProbingPaths) based on application/component base directory, framework directory, `.runtimeconfig.dev.json`, command line, servicing locations, etc. For more details see [host-probing](https://github.com/dotnet/core-setup/blob/master/Documentation/design-docs/host-probing.md). It then uses the `.deps.json` to locate dependencies in these paths.

For an application or component, the `.deps.json` file specifies:
* A set of dependencies and their assets
* Relative paths to locate them - relative paths in `.deps.json` file can be used to locate architecture-specific dependencies within the probing locations.

If the app depends on any frameworks, the `.deps.json` files of those framework are similarly processed.
Further details about the algorithm used for processing dependencies can be found in [assembly-conflict-resolution](
https://github.com/dotnet/core-setup/blob/master/Documentation/design-docs/assembly-conflict-resolution.md).

## Dynamic loading with dependencies
We propose to add a new public API which would dynamically load a component with these properties:
* Component is loaded in isolation from the app (and other components) so that potential collisions are not an issue
* Component can use `.deps.json` to describe its dependencies. This includes the ability to describe additional NuGet packages, RID-specific and/or native dependencies
* Component can chose to rely on the app for certain dependencies by not including them in its `.deps.json`
* Optionally such component can be enabled for unloading

Public API (early thinking):
```csharp
class Assembly
{
    public static Assembly LoadFileWithDependencies(string path);
}
```

At its core this is similar to `Assembly.LoadFile` but it supports resolving dependencies through `.deps.json`. Just like `Assembly.LoadFile` it provides isolation, but also for the dependencies.

```csharp
class AssemblyLoadContext
{
    public static AssemblyLoadContext CreateForAssemblyWithDependencies(
        string assemblyPath,
        AssemblyLoadContext fallbackContext,
        bool enableUnloading);
}
```

"Advanced" version which would return an ALC instance and not just the main assembly. Allows for additional changes to the `AssemblyLoadContext`, like registering an event handler to the `Resolving` event for example.  
Also adds the ability to specify:
* `fallbackContext` which is the `AssemblyLoadContext` to defer to when the assembly resolution is not possible in this current context. By default this is the `AssemblyLoadContext.Default` (the app's context). This allows for creating effectively parent-child relationships between the load contexts.
* `enableUnloading` which will mark the newly created load context to track references and to trigger unload for the load context and all the assemblies in it when possible.

## High-level description of the implementation
* Implement a new `AssemblyLoadContext` which will provide the isolation boundary and act as the "root" for the component. It can be enabled for unloading.  
* The new load context is initialized by specifying the full path to the main assembly of the component to load.  
* It will look for the `.deps.json` next to that assembly to determine its dependencies. Lack of `.deps.json` will be treated in the same way it is today for executable apps - that is all the assemblies in the same folder will be used as dependencies.
* Parsing and understanding of the `.deps.json` will be performed by the same host components which do this for executable apps (so same behavior/quirks/bugs, very little code duplication). Specifically `hostpolicy.dll` is the component which parses `.deps.json` during app startup. See [host-components](https://github.com/dotnet/core-setup/blob/master/Documentation/design-docs/host-components.md) for more details. If this functionality is required when running with a custom host, the said host would need to provide this functionality to the runtime.
* If the component has `.runtimeconfig.json` and/or `.runtimeconfig.dev.json` it will only be used to verify runtime version and provide probing paths.
* The load context will determine a list of assemblies similar to the TPA and list of resources and native search paths and remember them. These will be used to resolve binding events in the load context.

## Handling of various asset types
What happens with various asset types once the ALC decides to load it in isolation:
* Normal managed assembly (code) - The `.deps.json` parsing code will return list of full file paths. The ALC will just find it there and load it.  
Note that R2R images are handled by this as well since they are basically just a slightly different managed assembly. Also note that .NET Core can only load a given R2R image once as R2R. Any subsequent load of the same file will work, but it will be only used as a pure IL assembly (and thus require JITing). Loading the same file multiple times can occur if two load contexts decide to load the assembly in isolation.
* Satellite assemblies (resources) - Two possibilities:
    * Imitate app start behavior exactly - `.deps.json` only provides list of resource probing paths. ALC would then try to find the `<culture>/AssemblyName.dll` in each probing path and resolve the first match.
    * Use full paths - `.deps.json` resolution actually internally produces a list of full file paths and then trims it to just probing paths. The full file paths could be used by the ALC in a very similar manner to code assemblies.
* Native libraries - To integrate well the ALC would only get list of native probing paths from the `.deps.json`. It would then use new API to load native library given a probing path and a simple name. Internally this would call into the existing runtime behavior (which tries various prefix/suffix combinations and so on).

## Isolation strategies
There are several ways the new dynamic component loading can handle isolation:
* **Full isolation** - in this case a new load context is created and it always tries to resolve the bind operation first. If it can do so, it will load the dependency from the resolved location into the new load context. Only the dependencies which can't be resolved (typically framework assemblies) will be handed to the parent (Default) load context.
    * This provides full isolation to the component. Every dependency the component carries with it will be used from the component and loaded in isolation from the rest of the app. Basically avoids any potential collisions.
    * On the downside, this doesn't provide implicit sharing. Typically only framework assemblies would be shared in this scenario. Component would have to explicitly choose which assemblies to share, by not carrying them with it (this can be setup in project file by using `CopyLocal=false` for assembly references, similar option exists for project and NuGet references as well). This means that types used for communication between the app and the component would have to be explicitly shared by the component (via the exclusion of the assembly in the component). This is not done by the SDK by default, so it's easy to get this wrong and even with improved diagnostics will be relatively hard to debug.
* **Always load into default** - in this case all loads are done to the parent (Default) load context. In the extreme case a new load context is not really needed. Alternative could be that the load is attempted to the default load context (first by name, then by resolved file path). If that fails (should really only happen in case of a collision), the dependency would be loaded into the new load context in isolation.
    * Inherently shares as much as possible - avoids problems of sharing types used for communication.
    * Downside is that loading a component will "pollute" the default load context with assemblies from the component.
    * Loading multiple components with similar dependencies can easily lead to unpredictable results as what gets loaded where would depend on ordering.
    * Auto-upgrades - if the app uses a higher version of a given dependency, component which uses a lower version of the dependency will get the app's version - this can lead to incompatibilities.
* **Prefer default** - in this case a new load context is created. When it tries to resolve a dependency, it will first try to resolve it against the parent (Default) load context. If the parent can satisfy the dependency, it will be used. Otherwise if the dependency can't be resolved by the parent, the dependency will be resolved to a file path and loaded in isolation into the new load context.
    * Inherently shares all assemblies from the parent (Default) - avoids problems with sharing types used for communication.
    * There's no pollution of the parent context - no new files will be loaded into the parent context.
    * Auto-upgrade - components will auto-upgrade to the version of a dependency they share with the app (downgrade will not occur, in that case the dependency would be loaded in isolation). This can lead to incompatibilities.

Open questions:
* Which is the default behavior?
* Does the framework implement more than one behavior and lets users choose?
* In the "prefer default" behavior - what does it mean for the parent context to "satisfy" the dependency? Does it mean exact version match, or does it allow auto-upgrade (patch, minor or even major)? Do we even allow downgrades?

## Important implications and limitations
* Only framework dependent components will be supported. Self-contained components will not be supported even if there was a way to produce them.
* The host (the app) can be any configuration (framework dependent or self-contained). The notion of frameworks is completely ignored by this new functionality.
* All framework dependencies of the component must be resolvable by the app - simply put, the component must use the same frameworks as the app.
* Components can't add frameworks to the app - the app must "pre-load" all necessary frameworks.
* In all cases framework assemblies (and thus types) will be shared between the app and the component. Sharing of other assemblies depends on the isolation strategy used - see above.
* Pretty much all settings in `.runtimeconfig.json` and `.runtimeconfig.dev.json` will be ignored with the exception of runtime version (probably done through TFM) and additional probing paths.
