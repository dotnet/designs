# .NET Optional SDK Workloads

In .NET 5.0, we will add support for iOS, Android, and Web assembly projects. Up until .NET 5.0, we’ve delivered all supported workloads via a monolithic SDK. As the supported workloads of the .NET SDK grow (and we want them to), it is no longer tenable to deliver an "all-in-one / one-size-fits-all" SDK distribution. There are many challenges to a large monolithic SDK, with product build time and distribution size being the most significant. Instead, all new workloads will be built and delivered separately from the SDK, and will be acquirable via your favorite installation tool (like the Visual Studio Installer, a Linux package manager, or the .NET CLI). In the fullness of time, we intend for all .NET workloads to follow this pattern, resulting in a very small and focused SDK.

While the monolithic SDK model has become a liability, it was an important design point for .NET Core 2.x and 3.x. This approach enabled us to deliver two key value propositions with the SDK: providing a coherent set of tested workloads (which reduced our test matrix/burden), and enabling offline scenarios (`dotnet new` + `dotnet run` work by default -- for in-box templates -- with no internet). Going forward, it is important to retain those values, even as we move to a fundamentally different SDK composition model.

As part of .NET Core 3.1, we enabled [source build](https://github.com/dotnet/source-build) for .NET. From the beginning of the .NET Core project, we've wanted to enable the open source community to "do anything" with .NET source code, and for distro owners to be able to ingest .NET (via their own build mechanisms and policies) into their package archive. We are now seeing the first indications that distro maintainers will take advantage of .NET source build, now that it is functional. It is critical that .NET source build capabilities are extended to include the new optional workload model so that the community can build all workloads for their scenarios. In fact, we hope that source build will become simpler and more efficient with this model.

This proposal is intended for components that are part of or directly associated with a target framework, like Xamarin or Windows Forms, and the build tools associated with them. It is not intended for higher-level libraries. However, some higher-level libraries may benefit from similar composition and acquisition experiences. As it makes sense and is possible, we will offer similar capabilities for higher-level libraries over time.

## Detailed designs

This document is an overview. The following documents detail specific design topics:

* Manifest spec (still coming)
* [.NET 5 OS API Versioning](../minimum-os-version/minimum-os-version.md)
* [MSBuild SDK Resolvers and optional workloads in .NET 5](workload-resolvers.md)

## Goals

The goals for this proposal follow:

* Users can download the workloads that they need (like just ASP.NET Core, or just Xamarin, or both).
* It is easy to configure downloading a minimal set of files (build-only components) for environments like CI/CD servers, to limit resources used (wire cost, compute (for decompression), and disk I/O).
* Build errors are intuitive and helpful when developers are missing workloads for the projects they build.
* It is possible to install workloads in terms of project files, a kind of "workload restore" so that users don't need to write scripts to discover the specific workloads required. This is critical for a service like GitHub Actions.
* Users can update workloads without necessarily needing to install a newer version of the SDK. This includes being notified that workload updates are available.
* Users can install and interact with workloads for an SDK via any supported installation tool, such as the VS Installer, a Linux package manager, or the CLI (interaction between installers is described later).

## High-level approach

The proposal can be broken into two main topics: acquisition, and composition.

* *Acquisition* describes the capability and mechanism to acquire a workload, in part or in full, via an installation tool.
* *Composition* describes file formats, pack types, on-disk locations, and other mechanisms that define a workload, its constituent parts and associated versions, which enables those parts to be acquired, updated, and used as a coherent set.

Both of these topics have been satisfied by the monolithic SDK, to date, and need to be reconsidered given the plan to add new, optional, workloads. A new approach to composition and acquisition will be discussed in the following sections.

The key learning of .NET Core 1.x was that the .NET platform needs a separate versioning model than the one offered for NuGet library packages. A case in point is that you can update a single target framework value in a project, from `netcoreapp2.1` to `netcoreapp3.1`, to update all dependent platform components automatically (like for ASP.NET Core). The use of a single version for the platform makes it easy to update a project, and results in a stable configuration that is (for the most part) unaffected by package references.

This means that optional components will not be exposed as NuGet packages that require package references in project files, or that are acquired with or participate in package restore. Instead, a new system will be developed that is an evolution of the SDK composition system. In short, we will evolve the physically monolithic SDK to a logically monolithic one. This approach will enable us to deliver SDK UX with .NET 5 that is similar in spirit to the .NET Core 3.1 SDK, with the acknowledgment that there is some regression in functionality (you don't get the kitchen and its sink by default).

Since the first .NET Core release, we have delivered various types of "packs" as specialized packages on NuGet feeds ([example from 1.x and 2.x era](https://www.nuget.org/packages/runtime.win-x64.Microsoft.NETCore.App/); [example from 3.x+ era (unlisted)](https://www.nuget.org/packages/Microsoft.NETCore.App.Runtime.linux-x64)). We use NuGet for this purpose because many tools and services understand NuGet packages, it includes a concept of feeds (which can be private or public), and offers a global CDN (in the case of NuGet.org). These packs, however, are not intended to be directly referenced by project files, but are known to the .NET Core SDK as an implementation detail. We intend workloads to follow a similar model, with the difference that the .NET SDK will not have detailed built-in knowledge of packs, and that workloads will be separately acquirable and updatable from the SDK.

### Composition

Workloads will be composed and distributed as a set of packs. Packs are just a compressed set of files, like the [ASP.NET Core runtime]((https://www.nuget.org/packages/Microsoft.AspNetCore.App.Runtime.linux-arm64)). They are not discoverable on their own, nor do they describe the other packs that may work with or require or the various scenarios in which they can be used. Packs can includes runtimes, templates, MSBuild tasks, tools, and anything else that is required to build and run projects.

Today, there are many hard-coded [versions and packages described in MSBuild targets](https://github.com/dotnet/core-sdk/blob/edbdf82041abff369ed64e5d0d6fd590e75b5328/src/redist/targets/GenerateBundledVersions.targets) in the SDK that are used to compose the product. Moving forward, we will externalize this compositional information into a set of workload manifest files. Each workload will be defined by a manifest (likely distributed as its own small pack), and use it to describe the set of versioned packs that it offers. MSBuild and potentially other tools will be updated to discover and read these manifests, to the end of honoring them in the build and any other relevant operations.

The target framework will be used as the primary currency in project files that establish a link to a workload. As part of a related effort, [target frameworks are being updated in .NET 5](https://github.com/dotnet/designs/blob/master/accepted/2020/net5/net5.md) to include operating system information. This change will enable a direct link to OS-specific workloads. For example, a project that targets `net5.0-android` will need the .NET 5 Android workload (Xamarin.Android). A project that multi-targets `net5.0-android` and `net5.0-ios` will also need the .NET 5 iOS workload (Xamarin.iOS).

The set of TFM to workload mappings defined by the workloads. The SDK will carry a copy of this mapping, but will defer to information carried as part of installed workloads when they present newer information.

Each workload will expose a set of bundles of packs that a users can use. One of these bundles will be the default one you get to satisfy a TFM. Additional bundles will offer additional functionality. For example, Android AOT support would be exposed as an additional bundle, not provided by the default bundle. These additional bundles can be referenced as a required dependency of a project as a project property or installed manually via the CLI or a supported installation tool.

### Acquisition

Workloads will be acquired in two primary ways: via a supported installation tool or the .NET CLI.

Note: The term "supported installation tool" is intended to describe an installation system that exposes components and manages their dependencies. For example, Visual Studio Installer, Docker, and APT do that, while MSI and PKG don't.

Install tools must follow these rules for workloads it installs:

* The workload manifest is included as a required dependency for all variants of the workload (some packs might be optional).
* All referenced packs are included, matching the version specified in the workload manifest.

Install tools that do not install the manifest or that do not install all the files required by the manifest will create undefined environments that will produce undefined results for users. It may be possible to detect these error cases in .NET tools, and provide the appropriate feedback to users, but likely not in all cases.

The compositional model can be considered fragile. It is likely that we will need to expose a CLI command to validate a .NET Core installation/environment, in order to produce a detailed log that can be examined and shared. 

The .NET CLI will expose a more extensive set of commands for interacting with workloads. The CLI may allow installing more than just workloads, such that the noun it exposes is not `workload` but something more general (TBD; `bundle` is used as the stand-in for a better term in the following examples). In this parlance, a workload is one kind of bundle.

* `dotnet bundle install [bundle]` -- Installs a bundle by name.
* `dotnet bundle restore [project]` -- Installs one or more workloads in terms of the dependent workloads found in one or more referenced projects. One can think of restore installing the default bundle for a workload. The workload may expose other opt-in bundles.
* `dotnet bundle update [bundle | project]` -- Updates a bundle, in terms of a project, by name, or as part of updating all known bundles.

We don't intend for the .NET CLI-based installation to be mix-and-matched with a package tool (like APT or Yum). Instead, we are thinking that an installation tool would provide a mapping file that mapped its packages to workloads, so that `dotnet bundle restore` could provide meaningful error messages that direct users to install workloads via the installation tools. We still need to spend more time defining interactions between the .NET CLI and installation tools.

Workloads will be updatable. The SDK will include a mechanism to discover if a workload manifest pack has been updated. An updated manifest could represent bug fixes or new features (like updated bindings for an operating system update). If a manifest has been updated, the user will be notified, and have the opportunity to acquire the new version. Updates will not be automatic. This is roughly similar to the combination of `apt-get update` and `apt-get upgrade` with the [Debian package manager](https://en.wikipedia.org/wiki/APT_(software)).

### SDK and workload versioning

Another benefit of the existing monolithic SDK is that the workloads it includes are known to be compatible with core SDK components, like the C# compiler, NuGet, MSBuild, and the SDK targets. Over time, we have developed the concept of an SDK feature bands. These are represented by SDK version hundreds bands like `3.1.100` and `3.1.200`. These feature bands align with Visual Studio versions, like `16.4` and `16.5`. We do this because those same core SDK components are shared between Visual Studio and the .NET SDK. In addition, new major versions of those components, possibly containing a new language version, can be delivered in feature bands. We expect that workload manifests (and possibly the actual workload) will need to be re-published for each SDK feature band.

## Timing

This project has a very large scope, and requires changing how multiple large bodies of software work. We are planning the following multi-release progression to phase the work.

* .NET 5.0
  * Define manifest format and packaging.
  * Enable Xamarin and Web assembly as workloads.
  * Distribute these workloads only via Visual Studio (Windows and Mac).
  * Expose few if any .NET CLI verbs for acquisition.
* .NET 6.0+
  * Expose the full set of .NET CLI verbs for acquisition.
  * Separate out / expose WPF, Windows Forms and ASP.NET Core as workloads
  * Enable acquisition of all workloads via the .NET CLI, and at least ASP.NET Core and Web Assembly workloads available via Linux package managers.
    * Define and implement interaction with installation tools.
