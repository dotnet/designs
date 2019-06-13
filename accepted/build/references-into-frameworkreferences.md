# Supporting `Reference` items to DLLs in `FrameworkReference` targeting packs

In .NET Core 3.0, we have added `FrameworkReference` items which refer to .NET Core shared frameworks (base .NET Core, ASP.NET Core, or WindowsDesktop).  At build time, the FrameworkReferences resolve to targeting packs which include the reference assemblies for the corresponding shared framework.  FrameworkReferences also make the corresponding shared framework available at runtime, either by writing the shared framework information to the runtimeconfig.json file for Framework-dependent apps, or by including the runtime in the output for self-contained apps.

Here, we propose to:

- Allow `Reference` items to resolve to reference assemblies inside targeting packs corresponding to the `FrameworkReference` items for a project
- Allow specifying that the references to assemblies from a FrameworkReference's targeting pack should not be referenced by default.

## Scenarios

### WPF Theme Assemblies

WPF has several different theme assemblies, such as PresentationFramework.Aero and PresentationFramework.Luna.  Each of these theme assemblies has types with the same name and namespace.  This means that if you reference more than one of these assemblies, you need to do so with an assembly alias in order to distinguish between the types from the different assemblies in code.

With this proposal, we would not reference any of the theme assemblies by default.  To use a single theme assembly, you would add a reference to it:

```
<Reference Include="PresentationFramework.AeroLite" />
```

To reference multiple theme assemblies, you would add references with aliases:

```
<Reference Include="PresentationFramework.AeroLite" Aliases="AeroLite" />
<Reference Include="PresentationFramework.Luna" Aliases="Luna" />
```

More discussion of the WPF Theme assemblies (and the inspiration for this proposal) is here: https://github.com/dotnet/sdk/issues/3265

### Turn off default references to assemblies in targeting pack

- Projects in CoreFx [would like](https://github.com/dotnet/sdk/issues/3295) to have a `FrameworkReference` to Microsoft.NETCore.App in order to get the App Host and the right data in the runtimeconfig.json file.  However, they can't include the normal references from the targeting pack, as they are building projects which are part of the targeting pack.
- WPF wants to explicitly choose all their references, in order to avoid accidentally taking a dependency on APIs such as LINQ.
- Libraries might want to reference specific assemblies in a shared framework without exposing the whole shared framework as API surface to their consumers.

The project would include something like this:

```xml
<FrameworkReference Update="Microsoft.WindowsDesktop.App" IncludeReferences="False" />
<Reference Include="System.Printing" />
```

## Implementation details

### ResolveAssemblyReference

The ResolveAssemblyReference task would be updated to understand the "new-style" targeting packs, and the FrameworkList files in them.  There would be an additional parameter to the task to pass in the resolved targeting packs and paths.  The SearchPaths parameter would also support an additional search path for the targeting packs.  There would be an additional resolver used by ResolveAssemblyReference which would resolve simple name References to the reference assemblies in the targeting pack.

### Conflict Resolution

Conflict resolution runs before ResolveAssemblyReference, however there may be conflicts involving references that will be resolved from a targeting pack by ResolveAssemblyReference.  If there is a simple name reference to an assembly which is in multiple targeting packs, then ResolveAssemblyReference needs to pick the best one (using similar logic to conflict resolution).  If there is a conflict between a simple name which will be resolved by ResolveAssemblyReference and another reference (either from a NuGet package or that has been resolved from a targeting pack already by ResolveTargetingPackAssets), then ResolvePackageFileConflicts needs to resolve the conflict.  It already does something similar for .NET Framework, by reading the FrameworkList to get the version information that a reference will resolve to.

### NuGet and transitivity

If default references for a FrameworkReference are disabled (ie `IncludeReferences="False"`), then the FrameworkReference needs to flow transitively (through projects and NuGet packages) in order for the right information to be written to the runtimeconfig.json.  However, the compilation references from that FrameworkReference should not flow transitively.  Possible ways to achieve this would include:

- A NuGet feature to communicate this information, something like `ExcludeAssets="Compile"` on framework references
- A different name for the FrameworkReference, such as something like `ExcludeCompile:Microsoft.WindowsDesktop.App`