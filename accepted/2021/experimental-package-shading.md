# Experiment: NuGet Package Shading

(intro and scenarios to be added)

## Usage

### Package authors

A *package author* may enable shading on any package reference as long as its assets are configured such that it is effectively private. Any other package references that have a shaded package reference as a transitive dependency must also be shaded. When the project is restored, shaded copies of the packages are created and swapped in. When the project is packed, the shaded assets are embedded in the resulting package.

### Package consumers

Shading is superficially invisible to consumers of a package that has shaded dependencies. They may notice the shaded assets in the build logs or output directory, and their app size may increase, but they do not need to be aware that shading exists. From the consumer's perspective, a shaded dependency is no different than any other private asset.

## Behavior

### Private assets

This shading mechanism is built upon the existing NuGet concept of *private assets*. A shaded asset is inherently private and must be treated the same way as other private assets. Handling shaded assets using the private assets mechanisms reuses existing behavior and concepts, and should result in a consistent experience.

Shading may be enabled on any package reference that is effectively a *private package reference* i.e. all assets consumed from the package are consumed as private assets, and the reference is not exposed transitively. A future proposal will explore making private package references a first class concept.

### Shaded packages

To ensure consistency in package assets, shading applies to an entire package rather than individual assets or asset types. The core of this feature is a mechanism that can take a package and synthesize a *shaded package* by copying the original package and renaming the copy it and its assets to have a new identity: the *shaded name*.

The shaded name is a mangled name specific to the context in which the package is shaded and is designed such that the shaded package's assets do not collide at runtime with assets from other shaded copies and from the original unshaded package.

The name mangling is an implementation detail, and although it is deterministic to allow for deterministic builds, it may change in future versions of the .NET SDK. Developers should not depend on the mangling format or specific mangled IDs outside of references created by the .NET SDK itself.

When a shaded package has a dependency on a package that is also shaded, its assets will be processed so that any references to the dependency's unshaded assets are updated to reference the shaded assets.

### Shade-on-restore

During NuGet restore, any package reference in the graph that is shaded will be replaced with a reference to a shaded package created locally in the intermediate output directory.

A package may only be shaded when all other packages in the graph that depend on it are also shaded. Their assets must be shaded in order to be rewritten to reference its shaded assets.

### Rename safety

Renaming a package's assets so that multiple renamed versions can be used at runtime is not something that can be performed safely. Some assets may inherently have singleton behavior, and assets may embed the original name in ways that cannot automatically be detected and updated, for example when using reflection to load an assembly by name.

The shading tools will detect known unsafe patterns and warn when assets cannot be renamed safely, for example calls to [`AssemblyLoadContext.Load`](https://docs.microsoft.com/en-us/dotnet/api/system.runtime.loader.assemblyloadcontext.load?view=net-5.0) with values that cannot be determined statically. However, the shading tools will not be able to detect all problematic cases. Authors of packages with shaded dependencies are expected to test their package thoroughly and verify that it works correctly with shaded dependencies.

A particularly problematic case is when assets are not inherently unsafe, but are used by the consumer in an unsafe way. For example, types from a library with shaded dependencies may get serialized in a way that embeds a shaded shaded assembly's shaded ID into the serialization output. This is unlikely to occur in practice as  reflection based serialization of private fields is generally considered problematic, but it represents an example of the kinds of problems that cannot easily be detected automatically.

### Controlling shading

Shading will be controlled by a boolean parameter named `ShadePrivateAssets` will alter the behavior of NuGet restore in .NET SDK based projects. Any package reference for which `ShadePrivateAssets` is `true` will be shaded: replaced with a reference to a synthetic package created by copying the package and renaming it and its assets. The name of this parameter is based on the existing `PrivateAssets` package reference metadata that causes assets to be consumed by the project and not surfaced transitively to referencing packages. Certain types of private assets such as runtime libraries are embedded in any package created by running the Pack target on the project.

The `ShadePrivateAssets` parameter exists both as an MSBuild property and as MSBuild item metadata. The property defaults to an empty value, and the metadata defaults to the property value. Empty values are interpreted as `False`. The property can be used to set the behavior for all package references in a project, and the metadata can set or override the behavior for individual package reference items.

Shading will only be performed on private package references. Package references may be made private via setting the the existing `PrivateAssets`, `IncludeAssets` and `ExcludeAssets` package reference metadata such that compile-time assets and the package reference itself do not transitively escape the project that contains the private reference. Attempting to enable shading for non-private package references via MSBuild metadata will cause a restore error, and non-private package references ignore the MSBuild property.

> *NOTE:* Creating a private reference currently requires a deep understanding of `PrivateAssets`, `IncludeAssets` and `ExcludeAssets`. A future proposal will make it easier to mark a package reference as private and verify that types from private references are not surfaced in public API.

To allow deterministic builds, the assets in a synthetic shaded package are renamed using a deterministic ID created by mangling the name of the project and the original ID of the package: `__Shaded_{ProjectName}_{OriginalPackageId}`. The ID of the synthetic package is an internal implementation detail and may be subject to change, but this does not matter as it should never end up in any artifacts created from the project.

Only direct package references may be shaded. Shaded direct package references may be unified with transitive package references, but only when those transitive package references are transitive via other shaded package references.