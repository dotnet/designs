## Background

**Owner** [Rich Lander](https://github.com/richlander) | [Nick Guerrera](https://github.com/nguerrera)

The objectives here are:

1. Summarize the new approach of FrameworkReferences, targeting packs and runtime packs
2. Establish details such as package naming, layout and versioning


## Historical approaches

To date, there have effectively been two different approaches to framework references:

**1. Traditional targeting packs**
* Used by "classic" frameworks (.NET Framework, Silverlight, Windows Phone, Windows 8)
* Contain only reference assemblies, xml documentation, and framework list
* Installed to C:\\Program Files (x86)\\Reference Assemblies\\Microsoft\\\[Target Framework\]\\\[Version\]
* Delivered via windows installers, chained in to Visual Studio

**2. Framework NuGet packages**
* Used by "modern" frameworks (.NET Core, .NET Standard, UWP)
* Provide both reference assemblies and implementation as standard nuget assets
* Downloaded via nuget restore if not already in fallback folder/cache

And each have had strengths and weaknesses:

**Traditional targeting pack strengths**
* Fast build performance
* Obvious separation of platform vs. ecosystem
* Globally resolveable without project context (depended upon by certain VS features)

**Traditional targeting pack weaknesses**
* Windows-only acquisition
* Admin-only acquisition
* Manual installation
* Infamous error experience when not installed
* No self-contained deployment strategy

**Framework NuGet package stregnths**
* Cross-platform/cross-IDE acquisition
* Non-admin acquisition
* Seamless acquisition: just clone, restore, and build
   * Users can target a minimum patch version of a .NET Core shared framework


**Framework Nuget package weaknesses**
* Slow build (restore) performance
* Extremely complex NuGet graph that leaks to customers
* Large size on disk
* Difficult to understand distinction between platform and ecosystem
* Difficult to predict when network access is required
* Requires project context for resolution


## Summary of New Approach

We introduce a third, hybrid approach to framework references that aims to capture the strengths of the earlier approaches while mitigating weaknesses.

In this new approach, framework reference assemblies will be commonly resolved from a global location alongside `dotnet`. This is similar in spirit to the classic targeting packs in `C:\Program Files\Reference Assemblies`, but cross-platform and compatible with non-admin SDK deployment.

On top of that, we layer a fallback to acquiring the same assets via NuGet. This will be used so that you can easily build projects targeting frameworks that are older than the SDKs you have installed.

Finally, we add a new `FrameworkReference` concept to the build, to represent the use of a group of assemblies that version with the target framework.


## Targeting, Runtime, and AppHost Packs

We use the term "pack" for a collection of files used by the build. A pack can either be deployed globally alongside `dotnet` or wrapped in a NuGet package.

There are three categories of packs:

1. Targeting packs - reference assemblies, documentation, and other design-time assets
2. Runtime packs - runtime assets for self-contained publish
3. AppHost packs - app executable template to generate native app executable


## Globally Installed Packs

Targeting packs (and AppHost packs) will be bundled with the SDK, and available to be installed globally via native installers. This bundling is directly analagous to how the shared frameworks are bundled with the SDK or installable individually.

A new `packs/` folder will be added next to `dotnet` alongside `shared/` and `sdk/`. Only the smaller targeting packs and apphost pack will be bundled here, and the larger runtime packs will always be acquired via NuGet.

```
dotnet[.exe]
    shared/    <-- shared framework implementation for execution
        Microsoft.NETCore.App/3.0.0/...
        Microsoft.ASPNetCore.App/3.0.0/...

    packs/     <-- shared framework design-time assets for compilation
        Microsoft.NETCore.App.Ref/3.0.0/...
        Microsoft.NETCore.App.Host.win-x64/3.0.0/...
        Microsoft.ASPNetCore.App.Ref/3.0.0/...
        Microsoft.WindowsDesktop.App.Ref/3.0.0/...
        NETStandard.Library.Ref/2.1.0/...

    sdk/      <--  build toolsets
        3.0.100/
```

Note that there are no .nuspecs,.nupkgs,.sha512,.metadata,.p7s files in the packs/ tree as it does not serve as a NuGet fallback folder or source. When assets are consumed from this global location, NuGet is not involved in any way. Note that that as NuGet requirements change over time (e.g. package signing), the contents of a fallback folder or source need to change to meet them, and we do not want to have to service older offline packs for compatibility with newer SDKs.

Furthermore, just like `sdk/*` and `shared/*`, `packs/*` content is either included in the sdk .zip or installed via a native installer with appropriate ref-counting. This is in stark, deliberate contrast to the `sdk/NuGetFallbackFolder` that gets content unzipped from each SDKs .lzma, and that only grows over time no matter what you uninstall. The .lzma will be eliminated entirely and replaced by these packs.


## Packs Delivered via NuGet

The global scheme above allows for offline builds in common cases. For example, by bundling .NET Core 3.0 targeting packs with .NET Core 3.0.* SDKs, you can install the 3.0.* SDK and build framework-dependent 3.0 projects without pulling anything from the network. However, if you have projects that require a targeting pack that is not globally installed, then the SDK will instruct NuGet to download during restore and use it from the package cache instead of the global location. For this, a new NuGet package type -- `DotnetPlatform` is introduced that:

* cannot be installed into a project via a standard package reference
* cannot depend on other packages
* cannot be depended upon by other packages
* is not subject to standard nuget resolution rules

Furthermore, when NuGet is used, the assets acquired are not cataloged in project.assets.json as normal assets that are subject to NuGet resolution rules. Insetead, the build will use the same logic as the global/offline case and simply redirect it to a downloaded folder in the NuGet package cache.

This will be implemented using a new "download only package" feature from NuGet: https://github.com/NuGet/Home/issues/7339


### Package names

The naming requirements are:

1. Must not overlap with traditional framework NuGet package names
2. Given a shared framework name, one can derive the targeting pack package name
3. Given a shared framework name + RID, one can derive the runtime pack and apphost pack name

**Proposal**
 * Targeting pack packages: \<shared framework name\>.Ref
 * Runtime pack packages: \<shared framework name\>.Runtime.\<rid\>
 * AppHost pack packages: \<shared framework name\>.Host.\<rid\>

**Examples**
 * Microsoft.NETCore.App.Ref ("base" targeting pack for base .NET Core)
 * Microsoft.ASPNetCore.App.Ref (targeting pack for ASP.NET Core)
 * Microsoft.WindowsDesktop.App.Ref (targeting pack for WPF/WinForms on .NET Core)
 * NETStandard.Library.Ref (targeting pack for .NET Standard)
 * Microsoft.NETCore.App.Runtime.linux-x64 ("base" runtime pack for .NET Core for Linux OS and x64 CPU)
 * Microsoft.NETCore.App.Host.linux-x64 (apphost pack for .NET Core for Linux OS and x64 CPU)
 * Microsoft.ASPNetCore.App.Runtime.osx-x64 (runtime pack for ASP.NET Core for Mac OS and x64 CPOU)
 * Microsoft.WindowsDesktop.App.Runtime.win-x86 (runtime pack for WPF/Winforms on .NET Core for Windows OS and x64 CPU)


### Package Versions

Runtime pack package versions will be 1:1 with .NET Core Runtime / shared framework versions. This follows from the fact that runtime packs contain shared framework implementation and thus must change whenever the implementation changes.

Targeting pack package versions will generally not increase past major.minor.0 where major.minor matches the the corresponding two-part TFM. For example, when there is a 3.0.1 .NET Core runtime, the targeting pack will likely remain at 3.0.0. This follows from the fact that targeting packs represent public API surface, which must not change in a patch version of the runtime. (With that said, we can reserve the right to modify the targeting pack in a patch release to fix a severe bug. In the rare event that this occurs, the targeting pack patch version could be incremented past 0.)

During the prerelease phase of a new major.minor version of the runtime, the targeting pack will version 1:1 with the runtime. This is necessary as different prerelease versions will have different surface area.

For example,

* .NET Core SDK 3.0.100-preview with 3.0.0-preview runtime
    * packs/Microsoft.NETCore.App.Ref/3.0.0-preview
    * shared/Microsoft.NETCore.App/3.0.0-preview

* .NET Core SDK 3.0.100 with 3.0.0 runtime
    * packs/Microsoft.NETCore.App.Ref/3.0.0
    * shared/Microsoft.NETCore.App/3.0.0

* .NET Core SDK 3.0.101 with 3.0.1 runtime
    * packs/Microsoft.NETCore.App.Ref/3.0.0
    * shared/Microsoft.NETCore.App/3.0.1


### Package Layout

Since the packages will have a special type and not be installable into projects in the usual way, we do not need to use folders in the nupkg like `ref/<TFM>` etc. However, it was decided to use the same conventions where there is overlap to convey the intent. So, reference assemblies will still be in `ref/<TFM>`. Implmentation assemblies in runtime packs will still be in `runtimes/rid/lib/<TFM>`, etc. Additional data files that have no analog in the traditional nuget packages will use a new `data/` folder.

## FrameworkReference

A `FrameworkReference` is a new MSBuild item that represents a reference to a well-known *group* of framework assemblies that are versioned with the project's `TargetFramework`.

For .NET Core, there are two use cases:

* Reference to entire shared framework
``` xml
 <FrameworkReference Include="Microsoft.NETCore.App" />
 <FrameworkReference Include="Microsoft.AspNetCore.App" />
 <FrameworkReference Include="Microsoft.WindowsDesktop.App" />

 ```

* Reference to named subset or "profile" of a shared framework
``` xml
<FrameworkReference Include="Microsoft.WindowsDesktop.App.WindowsForms" />
<FrameworkReference Include="Microsoft.WindowsDesktop.App.WPF" />
```

A FrameworkReference can be added to a project in the following ways.

  1. Implicit `<FrameworkReference Include="...">` in MSBuild SDK.
  2. Transitively through project references
  3. Transitively through package references
  3. Explicit `<FrameworkReference Include="..." />` in user project file

The implicit framework references broken down by MSBuild SDK for netcoreapp3.0 are as follows:

 1. Microsoft.NET.Sdk
    * "Microsoft.NETCore.App"

 2. Microsoft.NET.Sdk.Web
    * "Microsoft.AspNetCore.App"
    * "Microsoft.NETCore.App" (via chained Microsoft.NET.Sdk)

 3. Microsoft.NET.Sdk.WindowsDesktop
    * "Microsoft.WindowsDesktop.App" if both $(UseWPF) and $(UseWindowsForms) are true
    * "Microsoft.WindowsDesktop.App|WPF if only $(UseWPF) is true
    * "Microsoft.WindowsDesktop.App|WindowsForms" if only $(UseWindowsForms) is true
    * "Microsoft.NETCore.App" (via chained Microsoft.NET.Sdk)

Just as with the older framework package reference, `$(DisableImplicitFrameworkReferences)` will be honored and prevent the SDKs from adding these implicit FrameworkReferences. There will also be a `$(DisableTransitiveFrameworkReferences)`, which would mirror existing `$(DisableTransitiveProjectReferences)`.

Transitivity will be achieved via recording when a `ProjectReference` or `PackageReference` needs a `FrameworkReference` in the assets file during NuGet restore: https://github.com/NuGet/Home/issues/7342


### FAQ: Why not just use Reference?

Other parts of the system are hard-wired to 1 Reference:1 Assembly. Like PackageReferences, FrameworkReferences will resolve each FrameworkReference down to multiple, constituent References before ResolveAssemblyReferences runs. There are too many assemblies in Microsoft.NETCore.App and Microsoft.AspNETCore.App to productively reference individually.


### FAQ: Why not just use PackageReference with an implicit version?

In short, because we tried it already in .NET Core 2.x, and it did not work very well. Framework asset resolution implemented via standard nuget package resolution is a leaky abstraction:

* It's confusing when the package is upgraded and either:
  1. The app can no longer run on unpatched machines
  2. Framework assemblies are suddenly included in your app.

* It's even more confusing to encounter a package downgrade.

Here is a sampling of issues:

* https://github.com/dotnet/cli/issues/9628
* https://github.com/aspnet/Home/issues/3281
* https://github.com/aspnet/Home/issues/3250
* https://github.com/aspnet/Home/issues/3257
* https://github.com/aspnet/Home/issues/3245
* https://github.com/aspnet/Home/issues/3241
* https://github.com/aspnet/Mvc/issues/7946
* https://github.com/dotnet/cli/issues/9519
* https://github.com/aspnet/websdk/issues/369
* https://github.com/dotnet/corefx/issues/30573
* https://github.com/dotnet/core/issues/1746
* https://github.com/dotnet/core/issues/1712
* https://github.com/dotnet/core/issues/1720
* https://github.com/aspnet/Docs/issues/7532
* https://github.com/cloudfoundry/dotnet-core-buildpack/issues/188
* https://github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql/issues/641

Furthermore, the correct resolution of assets from a FrameworkReference is different from a PackageReference. To see this, imagine if .NET Framework `Reference`s were transitive and implemented as `PackageReferences`.

Now imagine this graph:

* A (net45):
   * `<PackageReference Include="mscorlib" Version="4.5.0">`
   * `<PackageReference Include="System" Version="4.5.0">`
   * `<ProjectReference Include="B.csproj" />`

* B (net40):
   * `<PackageReference Include="mscorlib" Version="4.0.0">`
   * `<PackageReference Include="System" Version="4.0.0">`
   * `<PackageReference Include="System.xml" Version="4.0.0">`

* Applying standard nuget transitivity, A gets references:
  * B
  * mscorlib, 4.5.0
  * System, 4.5.0
  * System.Xml, 4.0.0 <-- Oops!

In this example, for A to use B, System.Xml is needed, but it makes no sense to use .NET 4.0 System.Xml in .NET 4.5. So A must unify to .NET 4.5 System.Xml. In the case of System and mscorlib, this happened because A also references them directly. This breaks down when a framework asset is acquired transitively.


### FAQ: If NuGet writes FrameworkReferences to assets file, shouldn't it also be responsible for determining FrameworkReference assets and listing them in assets file?

Writing FrameworkReferences to the assets file is morally equivalent to writing References as frameworkAssemblies. The process of consuming them will be nearly the same: instead of raising Reference items from the assets file, we raise FrameworkReference. NuGet does not need to know anything about them other than their names.

Abstractly, FrameworkReferences are not directly coupled to packages. As outlined above, they will commonly be resolved without any packages being used. The assets are only downloaded in packages to cover the scenario of building for a downlevel TFM where you don't have the targeting packs installed globally.

This allows the SDK to be fully in control of how FrameworkReferences are resolved to files on disk without baking more concepts into NuGet.


## Global resolution

There is one weakness of historical framework NuGet packages that was listed in the introduction, but not adressed in the plan above: "Requires project context for resolution." The issue there is that reference assemblies may only be pulled down by a NuGet restore operation, which is still tied to a project. In general, you cannot just ask for the reference assemblies for a given TFM / shared framework unless that targeting pack is globally installed. Furthermore, Visual Studio has a Global Design Time Assembly Resolution (GDTAR) service that relies on being able to do just that. Platforms based on packages such as UWP and .NET Core cannot provide this service naturally, which blocks certain VS features from working correctly and there is concern that some of the features that will need to be brought up for .NET Core 3. WPF/WinForms may run into this. For UWP, design-time scenarios, this was an issue and a workaround that is not considered maintainable was instituted.

At the beginning of .NET Core 3 development, when this document was originally written, it was thought that this would need to be addressed somehow, but it did not actually materialize as a blocker for the WPF/Winforms design-time scenarios, and so this was not addressed. It is possible that this will need to be revisited in a future release.
