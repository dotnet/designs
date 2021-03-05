# Tracking Platform Dependencies

**Dev**: [Matt Thalman](https://github.com/mthalman)

There are a set of .NET-related customer workflows that require the use of platform libraries contained within the operating system. These are divided into two categories:

* Execution of a .NET process: Runtime components defer some of their logic to platform libraries. Examples include ICU libraries for globalization support, GNU C Library, and others.
* Building .NET from source code: The toolchain required to build the .NET source consists of platform dependency prerequisites on the build machine. Examples include CMake, curl, Python, and others.

The problem is that these dependencies vary based on usage (i.e. a dependency may only be needed in a particular environment or scenario), they change as the product and the dependent ecosystem evolve, and the documentation is often out-of-date with the product. This leaves the community with few options other than an iterative and error-prone approach of attempting to execute their workflow and finding what breaks due to missing dependencies (e.g. https://github.com/dotnet/runtime/issues/36888#issuecomment-633220620, https://github.com/dotnet/dotnet-docker/issues/1767). Even worse is when there are cases where the community has discovered _incompatible_ dependencies in environments that are supported (e.g. https://github.com/dotnet/runtime/issues/38755).

We need to provide a better user experience for those developers that continually encounter issues of missing platform dependencies in their systems. By providing .NET developers access to self-service tools that consume an up-to-date and comprehensive model of platform dependency information, we can help these developers to accurately configure their systems.

This design provides a uniform description of all platform dependencies for .NET. It collects the dependency data into a machine-readable format that serves as the complete model for reasoning about .NET's platform dependencies.

> In this context, platform dependency refers to a pre-existing artifact in the operating environment that satisfies the usage of a .NET component. For example, in order to run a .NET application in a Linux environment, the GNU C Library must exist.

By providing a means for .NET contributors to describe the product's platform dependencies -- both runtime and source build toolchain -- in a common model format, it can be used to satisfy other downstream workflows that require the data. Contributors also need to understand what their responsibilities are in order to maintain the model and avoid having its contents go stale. This design provides a developer workflow that describes what actions need to be taken by contributors in a variety of scenarios.

## Scenarios and User Experience

### Keep other product assets up-to-date

A .NET product area owner has files that need to list out the Linux packages that .NET depends on. Examples of such product areas include the following: 

* Docker: A Dockerfile that [installs](https://github.com/dotnet/dotnet-docker/blob/203e3e7c2cbb6a4ac73290c603ef02d249128500/src/runtime-deps/3.1/buster-slim/amd64/Dockerfile#L7-L14) dependent packages
* Linux packages:  A deb package that [lists](https://github.com/dotnet/installer/blob/7f8f4a894abc5f666c2bfa45ce6e7c6fe9ace71b/src/redist/targets/packaging/deb/dotnet-debian_config.json#L31-L38) the packages it depends on.
* Docs: User documentation that [lists](https://github.com/dotnet/docs/blob/b9a09326817f9acf4dab825f62ae0a3fb2427a30/docs/core/install/linux-debian.md#dependencies) the Linux packages that are necessary to have installed for .NET.

They want to ensure this list is up-to-date with .NET's requirements. They author a tool which reads from .NET's platform dependency model and compares it against their package list to ensure it aligns for a given platform and version.

### Determine vulnerability exposure

A security incident has been disclosed in a Linux package that could potentially affect a platform dependency that .NET has. The response team needs to have an understanding of what components of .NET have a dependency on the Linux package so that the appropriate mitigation can be implemented. They search through .NET's platform dependency model to determine which .NET components are affected and on which platforms.

### Dependency-tracking tools

The .NET team wants to provide users with self-service tools so that they can know, a priori, what platform dependencies their specific application has. This allows them to configure the systems that will host their application with confidence. The developers creating these tools need a single source of truth from which to derive .NET's platform dependencies. They author the tools to read from the platform dependency model to get this information.

### Dependency change alerting

A .NET contributor makes a change to the NativeMethods.cs file in their project. They create a PR with their changes. A bot alerts the contributor to the potential need to update .NET's platform dependency model in response to the change in the NativeMethods.cs file. The bot alerts them by adding a special label to the PR and a comment that provides instructions on next steps they should follow.

### Platform EOL

Debian 9 goes EOL and a maintainer of the platform dependency model needs to clean up the model to remove references to Debian 9 since those references are now obsolete. They run a CLI command to update the model automatically:

```console
dotnet-deps platform remove debian.9
```

## Requirements

### Goals

* Common schema capable of describing both runtime and toolchain dependencies.
* Model that limits repetition to make maintenance easier.
* File format that is machine-readable to allow for automated transformation into other output formats.
* Ability to describe:
    * Dependencies for multiple platform types (Linux, Windows, MacOS)
    * Dependencies from various granularities of .NET components such as shared frameworks and NuGet packages.
    * Dependencies that exist outside of canonical scenarios, such as opting into specific features like diagnostics.
* Workflow that .NET contributors can easily follow for maintaining the model.

### Non-Goals

* Any dependency that is included in the deployment of the application is outside the scope of this design. NuGet packages are an example of this. A .NET application's dependency on a .NET package, and any assets contained in those packages (managed, native, or otherwise), is explicitly included in the deployment of the application itself. Therefore, the operating environment is not required to be pre-configured with those specific assets; it'll get them naturally through the deployment of the application. However, a NuGet package may have its own platform dependency (e.g. a Linux package) that is not physically contained in the NuGet package; such a platform dependency would be in scope with this design. The concern addressed here is solely focused on what the operating environment must be pre-configured to contain in order to operate on .NET scenarios.

## Stakeholders and Reviewers

* ASP.NET Core
    * [Kevin Pilch](https://github.com/Pilchie)
* CoreCLR
    * [Jeff Schwartz](https://github.com/jeffschwMSFT)
* Acquisition & Deployment
    * [Dan Leeaphon](https://github.com/dleeapho)
* Docs
    * [Andy De George](https://github.com/adegeo)
* Libraries
    * [Dan Moseley](https://github.com/danmoseley)
* Mono
    * [Marek Safar](https://github.com/marek-safar)
* Release
    * [Jamshed Damkewala](https://github.com/jamshedd)
* SDK
    * [Marc Paine](https://github.com/marcpopMSFT)

## Design

### Schema

```csharp
using System.Collections.Generic;

namespace Microsoft.DotNet.Dependencies.Platform
{
    // Root of the model
    public class PlatformDependencyModel
    {
        // Major.Minor.Patch version of .NET release the dependencies are associated with
        public string DotnetReleaseVersion { get; set; }

        // Set of available dependency usages that can be referenced by a platform dependency.
        // A dependency usage is a descriptor of how a dependency is used by a component (e.g. default, diagnostics, localization).
        // This maps the name of the name of the usage to its description.
        public IDictionary<string, string> DependencyUsages { get; }
        
        // Set of top-level platforms whose dependencies are described
        public IList<Platform> Platforms { get; }
    }

    // A supported platform
    public class Platform
    {
        // Rid identifying the platform
        public string Rid { get; set; }

        // Child platforms (e.g. version-specific, arch-specific)
        // Child platforms inherit the characteristics of their parents but can override them
        public IList<Platform> Platforms { get; }

        // Set of components and their dependencies specific to this platform
        public IList<Component> Components { get; }
    }

    // A logical component within .NET that encapsulates a set of platform dependencies
    public class Component
    {
        // Name of the component
        public string Name { get; set; }

        // Type of the component
        public ComponentType Type { get; set; }

        // Set of platform dependencies this component has
        public IList<PlatformDependency> Dependencies { get; }
    }

    // Types of .NET components
    public enum ComponentType
    {
        // A shared framework (e.g. Microsoft.NETCore.App, Microsoft.AspNetCore.App)
        SharedFramework,

        // A NuGet package (e.g. System.Drawing.Common)
        NuGetPackage
    }

    // Description of a platform dependency
    public class PlatformDependency
    {
        // Name of the dependency artifact
        public string Name { get; set; }

        // Type of the dependency
        public DependencyType Type { get; set; }

        // Minimum version of the dependency artifact
        public string? MinimumVersion { get; set; }

        // A value indicating in what manner the dependency is used
        public string Usage { get; set; }

        // Reference to the dependency from the nearest parent platform that this dependency overrides
        public DependencyRef? Overrides { get; set; }
    }

    // Types of dependencies
    public enum DependencyType
    {
        // An executable file
        Executable,
        
        // A library file (e.g. Windows DLL, Linux shared object library)
        Library,

        // A Linux package contained in a repository
        LinuxPackage
    }

    // A reference to a dependency
    public class DependencyRef
    {
        // Name of the referenced dependency
        public string Name { get; set; }

        // Type of the referenced dependency
        public DependencyType DependencyType { get; set; }
    }
}
```

#### Platform

Each platform has its own way of identifying assets that .NET is dependent upon. For example, the Open SSL library is labeled as openssl-libs in Fedora and libssl in Debian.  For that reason, platform dependencies can't be described without identifying which platform they are for. The model handles this by describing platforms as a top-level concept to organize dependencies. Since many dependencies apply to multiple versions of a given platform, the ability to describe a versionless platform, as well as more specific versioned platforms if necessary, is possible. This is done by associating a platform with a rid. Rids are hierarchical allowing more specific platforms to be targeted which provides an ideal mechanism to be able to flexibly describe the scope of dependencies. In the same way that rids are hierarchical, so too are the platforms in this model. This allows for less repetition in the actual model content and enables platforms with more specificity to append or amend dependencies from their parent platform. Some examples of this:

* A dependency is taken on a new asset that is only available in a newer version of the platform.
* A dependency name is different in a newer version of the platform which often happens when the version of the asset is contained in its name (e.g. libicu).

#### Component

Within each platform can be described a set of components. These components represent logical parts of .NET that are delivered as a unit. Examples of these are shared frameworks (Microsoft.NETCore.App, Microsoft.AspNetCore.App, etc) and NuGet packages.

#### Platform Dependency

Each component describes the dependencies it has for the platform it is contained within. A dependency is identified by its name and type (Linux distro package, DLL).

A key piece of metadata that gives context to the dependency is the "usage" field. This field is set to one of the well-known values that describes the scenario in which this dependency applies. Here are some examples of usage values:

* default: Indicates that the dependency applies to a canonical app scenario (i.e. Hello World).  Note that this is specifically about a canonical app and not intended to be a description of required dependencies in the absolute sense. An example of this is libicu. While libicu is not required to run an app if it has been set to use invariant globalization, the default/canonical setting of a .NET app is that invariant globalization is set to false in which case libicu is necessary. This is why the term "default" is used rather than something like "required".
* diagnostics: Indicates the dependency should be used in scenarios where diagnostic tools are being used such as with LTTng-UST.
* httpsys: Indicates the dependency should be used for ASP.NET Core apps that are configured to use the HTTP.sys web server.
* localization: Indicates the dependency should be used for localization/globalization scenarios such as with tzdata.

Dependencies are able to override the content of another dependency that is inherited from its platform hierarchy. The "overrides" field identifies the name and type of the target dependency to be overridden.  Any other fields that are set in the dependency will override the corresponding values from the referenced dependency.

#### Example Model

```json
{
  "dotnetReleaseVersion": "6.0.0",
  "dependencyUsages": {
    "default": "Used by default or for canonical scenarios",
    "diagnostics": "Used for diagnostic scenarios such as tracing or debugging",
    "httpSys": "Used for ASP.NET Core apps that are configured to use the HTTP.sys web server",
    "localization": "Used for locale and culture scenarios"
  },
  "platforms": [
    {
      "rid": "debian",
      "components": [
        {
          "name": "Microsoft.NETCore.App",
          "type": "Framework",
          "platformDependencies": [
            {
              "name": "libc6",
              "dependencyType": "LinuxPackage",
              "usage": "default"
            },
            {
              "name": "libgcc1",
              "dependencyType": "LinuxPackage",
              "usage": "default"
            },
            {
              "name": "libgssapi-krb5-2",
              "dependencyType": "LinuxPackage",
              "usage": "default"
            },
            {
              "name": "libicu57",
              "dependencyType": "LinuxPackage",
              "usage": "default"
            },
            {
              "name": "liblttng-ust0",
              "dependencyType": "LinuxPackage",
              "usage": "diagnostics"
            },
            {
              "name": "libssl1.1",
              "dependencyType": "LinuxPackage",
              "usage": "default"
            },
            {
              "name": "libstdc++6",
              "dependencyType": "LinuxPackage",
              "usage": "default"
            },
            {
              "name": "tzdata",
              "dependencyType": "LinuxPackage",
              "usage": "localization"
            },
            {
              "name": "zlib1g",
              "dependencyType": "LinuxPackage",
              "usage": "default"
            }
          ]
        },
        {
          "name": "System.DirectoryServices.Protocols",
          "type": "NuGetPackage",
          "platformDependencies": [
            {
              "name": "libldap-2.4-2",
              "dependencyType": "LinuxPackage",
              "usage": "default"
            }
          ]
        }
      ],
      "platforms": [
        {
          "rid": "debian.10",
          "components": [
            {
              "name": "Microsoft.NETCore.App",
              "type": "Framework",
              "platformDependencies": [
                {
                  "name": "libicu63",
                  "overrides": {
                    "name": "libicu57",
                    "dependencyType": "LinuxPackage"
                  }
                }
              ]
            }
          ]
        }
      ]
    }
  ]
}
```

The model above uses the Debian Linux distro as an example. It shows the `Microsoft.NETCore.App` shared framework as having a base set of dependencies but for Debian 10, the `libicu57` package dependency is overriden to be `libicu63` since that is the version available in Debian 10. It also shows that the `System.DirectoryServices.Protocols` NuGet package has a dependency on `libldap-2.4-2`.

### File Storage

Dependencies will change over time from release to release. For that reason, it is reasonable to consider the platform dependency model to be a release artifact, tied to a particular release. Precedent already exists for this kind of artifact with the [releases.json](https://github.com/dotnet/core/blob/master/release-notes/5.0/releases.json) file. In order to provide a consistent experience for consuming release artifacts, this design roughly follows that pattern used for the releases.json file.

The dependency model will be represented as a JSON file and be stored in the [release-notes folder](https://github.com/dotnet/core/tree/master/release-notes) for each release. For example, the 6.0.0 release would have the dependency model located at https://github.com/dotnet/core/blob/master/release-notes/6.0/6.0.0/6.0.0-platform-dependencies.json; a preview release would be located at https://github.com/dotnet/core/blob/master/release-notes/6.0/preview/6.0.0-preview.1-platform-dependencies.json. This design deviates from the releases.json file which is stored at major/minor folder level instead of the patch folder for a couple reasons:

* There is no dependency data that is common to all patch releases. While there could end up being commonality in the data, it is not an inherent feature.
* From a human-readability standpoint, the size of the json file would be quite large and difficult to navigate as patch releases accumulate.

## Engineering Workflow

It is necessary for all .NET contributors to participate in the maintenance of the platform dependency model. The scope of .NET is too large to have a single person or small set of people actively monitor for changes to the dependencies. It needs to be a distributed effort where the maintainers that are familiar with that area of .NET can keep an eye on things.

There will also be a small group charged with maintaining the platform dependency tracking system as a whole. This group will be responsible for approving updates to the model and acting as points-of-contact for any questions from contributors.

Luckily, the burden of maintenance is expected to be low since dependencies don't often change. However, there are a few scenarios that do cause change which are described further below.

### Maintenance Scenarios

In all of the scenarios below, the person introducing or discovering the dependency change should file an issue in the [dotnet/core](https://github.com/dotnet/core) repo describing the change. Such issues should be labeled as `area-dependency`. This is necessary in order to notify stakeholder teams (e.g. installers, container images, documentation) of such changes.

#### New Dependency

New platform dependencies can be introduced by making a code change that has the dependency or including a new component into .NET (e.g. shared framework, NuGet package). When such a dependency is added, the contributor should also update the appropriate platform dependency model file with the necessary information that describes the dependency across all supported platforms.

##### Change Detection

In order to avoid the reliance upon contributors to recognize when they've changed a dependency, a more automated solution would be preferable. This can be done by defining a GitHub bot that checks for files in PRs containing `NativeMethods` or `Interop` in their name. If such a file is detected, a label is added to the PR alerting the submitter that they should evaluate their changes for changes to the dependencies. While not a fool-proof solution, it should provide coverage for the vast majority of dependencies. Work is still required by the submitter to make the appropriate changes to the platform dependency model but the GitHub bot helps alert them when there is potentially action that is needed.

#### Dependency Name Change

The platform dependency model describes dependencies by name and those names are subject to change from upstream sources.

> A classic example of this is the [ICU library](http://site.icu-project.org/). In many Linux distros, the ICU library's package contains the version number in the name (e.g. libicu57). When a Linux distro updates to use a new version of the ICU library, they typically remove the previous version from the package repository. Thus, any dependency described for the ICU library needs to account for the name change.

We will rely on manual discovery to determine when the name of a package has changed since the typical scenario in which a name change is detected happens when a build breaks. When a dependency name change is discovered, the person discovering the change should file an issue in dotnet/core as described [above](#maintenance-scenarios).

#### New Platform Support

When support for a new platform is added to the product, the platform dependency model of future releases needs to be updated include this platform and all its supported versions.

#### Platform End of Support

When support for a platform is removed from the product, the platform dependency model of future releases needs to be updated remove this platform.

### Selecting a Model File

The first thing a maintainer needs to do in order to edit the dependency model is to know which model file to update.

In cases where a new dependency is added or support is added for a new platform, the maintainer should select the dependency model associated with the upcoming release in the https://github.com/dotnet/core/tree/master/release-notes location. In all likelihood, there will not yet be a folder for the upcoming release so one can be added as the placeholder for the future release notes and define the dependency model file there.

In all other cases, it is the past release dependency files that need to be updated. For example, if support for Debian 10 ends, all dependency model files for all past releases that contain the `debian.10` rid should be updated to have that platform removed.

### CLI Tool

Because there can be numerous dependency model files that need to be updated for certain scenarios -- and to reduce human error -- a CLI tool will be created in order to perform certain operations across multiple dependency model files. This will be intended for scenarios that would otherwise be repetitive busywork and can easily be automated. Examples of such operations include [dependency name changes](#dependency-name-change) and [platform removal](#platform-end-of-support).

#### CLI Syntax

##### Override a dependency name

```console
dependency override [--path <path>] <dependency-type> <source-rid> <source-dependency-name> <target-rid> <target-dependency-name>
```

Options:

* `path`: Root path location where dependency model files will be searched and updated. Default is the current working directory.

Arguments:

* `dependency-type`: type of the dependency to override
* `source-rid`: rid of the dependency to override
* `source-dependency-name`: name of the dependency to override
* `target-rid`: rid of the platform to contain the dependency override
* `target-dependency-name`: new name of the dependency

Example:

Overrides the libicu package from the default of libicu57 for all Debian platforms to libicu63 for Debian 10:

```console
dotnet-deps dependency override LinuxPackage debian libicu57 debian.10 libicu63
```

##### Remove a platform

```console
platform remove [--path <path>] [--force] <rid>
```

Options:

* `path`: Root path location where dependency model files will be searched and updated. Default is the current working directory.
* `force`: Forces the platform to be removed even if it has child platforms.

Arguments:

* `rid`: rid of the platform to remove

Example:

Removes the debian.10 platform:

```console
dotnet-deps platform remove debian.10
```
