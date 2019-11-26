# Easy Acquisition of .NET Framework Targeting Packs

**PM** [Immo Landwerth](https://github.com/terrajobst) |
**Dev** [Daniel Plaisted](https://github.com/dsplaisted)

This proposal is about enabling acquisition of the .NET Framework targeting
packs via NuGet so that compiling code for a specific version of the .NET
Framework doesn't require a Windows machine with a machine-wide installation of
the corresponding targeting pack.

The .NET Framework supports targeting different versions of the platform
regardless of the .NET Framework version that is installed on the developer's
machine. This allows developers to target a lower version than what they use
themselves. This is especially useful for in-place updates where the older
version of the framework cannot be installed side-by- side.

That mechanism is called *targeting packs*, which is simply a set of assemblies
that describe the public APIs of a particular version of the .NET Framework.
Since those assemblies are only used by the compiler, they don't need to contain
any code, which we call [reference assemblies](https://www.youtube.com/watch?v=EBpY1UMHDY8).

Unfortunately, the targeting packs aren't available by themselves; they are
redistributed as part of the .NET Framework Developer Packs which bundle both
the runtime as well as the targeting pack. This design ensures that developers
can never be in a state where they have a targeting pack that is higher than the
.NET Framework version they have installed. That design made sense coming from a
Visual Studio-first perspective but doesn't serve us well today:

* **It's not cross-platform friendly**. The .NET Framework Developer Packs are
  only available for Windows which makes the targeting packs indirectly Windows-
  only too.

* **It's not CI friendly**. Requiring a machine-wide install isn't friendly for
  cloud-hosted CI machines as developers typically lack permissions to install
  software.

This document proposes to offer the targeting packs via NuGet that they can be
acquired in a non-impactful way. It builds on the following existing specs:

* [Phillip Carter](https://github.com/cartermp): [Add Target Packs on NuGet](https://github.com/dotnet/core/pull/64)
* [Daniel Plaisted](https://github.com/dsplaisted): [Reference Assembly NuGet packages](https://gist.github.com/dsplaisted/83d67bbcff9ec1d0aff1bea1bf4ad79a)
* [Prototype](https://github.com/dsplaisted/ReferenceAssemblyPackages)

## Scenarios and User Experience

* Build a project which is multi-targeted to .NET Core or .NET Standard as well
  as .NET Framework on Mac OS or Linux using the .NET CLI

* Build a project targeting Mono (which uses the same Target Framework
  Identifier as .NET Framework) using the .NET CLI

## Requirements

### Goals

* Doesn't require running an installer
* Works cross-platform
* Can be used in SDK-style multi-targeting projects
* Prefer the centralized targeting pack if it's present. First, this ensures we
  don't change the performance characteristic of exiting solutions but also
  allows working around some issue due to build extensions by installing the
  targeting pack.
* Supports `<PreserveCompilationContext>` so that ASP.NET Razor compilation
  still works correctly even without the targeting pack installed.

### Non-Goals

* Supporting NuGet-based acquisition of the .NET Framework runtime
* Supporting NuGet-based acquisition from non-SDK-style projects
* Support pre-.NET Framework 4.5 targeting packs
* Support non-.NET Framework targeting packs (Xamarin, Unity)
  - More specifically, this feature will not include targeting packs for APIs
    that are specific to Xamarin, Mono, or Unity such as Xamarin.iOS.dll,
    Mono.Posix.dll.

## Design

Daniel has created a [prototype](https://github.com/dsplaisted/ReferenceAssemblyPackages):

* We have an aggregate package that itself is empty and just depends on
  different packages split by TFM.
* This allows downloading only the assemblies for a specific version while also
  having a single package which simplifies installation outside of SDK- style
  projects.

### Details

* We need to fix ASP.NET Core Razor compilation to locate reference assemblies
  without their custom locater that assumes the only place to look for is the
  global TP location.
  - See [this bug for details](https://github.com/dotnet/sdk/issues/2054)
* There will be separate NuGet packages with reference assemblies for each
  version of .NET Framework. This means that projects targeting a single version
  of .NET Framework don't need to download and spend disk space on the reference
  assemblies and intellisense files for all the other possible versions of .NET
  Framework. It also means that when a new version of .NET Framework is
  released, the reference assemblies for the previous versions don't need to be
  re-shipped in an updated package along with the new assemblies.
* There will be a single "metapackage" that can be referenced, that will have
  conditional dependencies on each of the version-specific reference assembly
  packages.
* The metapackage will automatically be referenced by the .NET SDK when required
  (ie on non-Windows OS's or missing targting pack). On Windows, we will make
  the .NET SDK continue to rely on the reference assemblies from the targeting
  packs, if and only if, it's present. Otherwise we fall pack to the package-
  based reference. This ensures existing code continues to build the same way.
* For the package IDs, I suggest the following:
  - For the metapackage: `Microsoft.NETFramework.ReferenceAssemblies`
  - For the version-specific packages:
    `Microsoft.NETFramework.ReferencesAssemblies.net462` (where net462 is
    replaced with the corresponding NuGet short framework Identifier)
* The NuGet packages will include a `.targets` file that sets the
  `TargetFrameworkRootPath` to a path within the NuGet package. This will allow
  the existing MSBuild logic in the `GetReferenceAssemblyPaths` target and other
  logic such as automatically referencing the facades if necessary to continue
  to work as normal.
    - We may need to set `FrameworkPathOverride` as well to avoid other weird
      issues like the explicit `mscorlib` reference.
* The logic in the `.targets` file in the NuGet packages will only set the
  `TargetFrameworkRootPath` if the `TargetFrameworkIdentifier` and
  `TargetFrameworkVersion` properties match the reference assemblies that the
  package provides.
* The reference assembly packages should include the `.xml` intellisense
  documentation files. I believe the `.xml` documentation files have to be in
  the same folder as the reference assemblies, so there isn't a way to deliver
  them as a separate package.
* If we want to support localized intellisense, we would need to create a
  separate set of packages and corresponding meta-package for each language
  supported. The IDs of these packages could follow the pattern
  `Microsoft.NETFramework.ReferenceAssemblies.pt-br`. The SDK could use a
  property to select which language's metapackage to reference.
* The reference assembly packages should not show up as dependencies of "normal"
  packages. Thus, the reference assembly packages should set
  `developmentDependency` to true in it's metadata. Likewise, when the .NET SDK
  automatically references the reference assembly metapackage, it should use
  `PrivateAssets="All"`.
* The reference assembly packages should include the same layout of files that
  are installed by the targeting packs under `C:\Program Files (x86)\Reference
  Assemblies\Microsoft\Framework`. This should be rooted at the path specified
  by the `TargetFrameworkRootPath` property in the package. For example, if the
  `.targets` file, which is in the `build` folder of the NuGet package, sets the
  `TargetFrameworkRootPath` to `$(MSBuildThisFileDirectory)`, then the .NET
  4.6.2 reference assembly package should have the reference assemblies and
  intellisense files in the `build\.NETFramework\v4.6.2` folder, as well as have
  the `Facades`, `PermissionSets`, and `RedistList` folders with corresponding
  files under that folder.
* The version number of each package should start at 1.0.0. When a new version
  of the .NET Framework is released, a corresponding reference assembly package
  (versioned at 1.0.0) should be released, and a new metapackage that includes
  the additional dependency for the newly supported version should be released.
  The new version of the metapackage should have it's minor version incremented.
  If we need to fix an issue with the packages, we can ship new versions with
  the patch version incremented, and a new metapackage with dependencies on the
  patched packages.
* We need to determine what license to use for these packages.
* We will also need to determine the details of the package metadata, such as
  the description, project URL, icon, etc.

## Q & A