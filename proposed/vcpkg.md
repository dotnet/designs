# Using vcpkg in .NET projects and the VMR

**Owner** [Jeremy Koritzinsky](https://github.com/jkoritzinsky)

## Scenarios and User Experience

In .NET today, we handle native dependencies and native dependency flow by a few different mechanisms:

- Vendoring dependencies, aka including a copy of the source with our applied patches in the product repository.
- Git submodules pointing to the source repo of the dependency.
- NuGet packages that flow pre-built between our repositories.

For many of our dependencies, these build options work decently well. However, these mechanisms have a few drawbacks:

- Vendoring code increases the size of our repositories and increases build times.
- Submodules aren't cloned by default and have a worse UX than vendoring code, and cannot support patches.
- NuGet packages are not easy to consume from native CMake projects, so we end up having to wire up MSBuild to consume them and construct paths to pass to CMake.

Additionally, [the VMR design documents on external source](https://github.com/dotnet/arcade/blob/main/Documentation/UnifiedBuild/VMR-Strategy-For-External-Source.md) defines that we shouldn't use submodules directly in our build.

For native dependencies, we could alternatively use Vcpkg, a cross-platform C++ package manager provided by another team at Microsoft.
Vcpkg would make it easier for us to both consume native dependencies in our projects, as many C++ libraries that we may consider using are already available in vcpkg.
It would also make it easier for us to contribute our own native projects, like the nethost library, back to vcpkg in an official manner.

## Requirements

- We must be able to version and patch dependencies ourselves.
- We must be able to produce private patches that we can consume without public publishing.
- We must be able to consume dependencies from vcpkg in our projects.
- It must be possible to use vcpkg in offline build scenarios.
- It must be possible to use vcpkg in the VMR builds.
- It must be possible to change a dependency to use a distro/system package instead of a vcpkg dependency

### Goals

- Enable developers of .NET to consume native dependencies from vcpkg.

### Non-Goals

- Replace all existing native dependency mechanisms.
- Initially, using vcpkg from MSBuild is considered out of scope.
- Using vcpkg in .NET 9 is out of scope.

## Stakeholders and Reviewers

- @jkoritzinsky
- @mmitche
- @jkotas
- @AaronRobinsonMSFT
- @wtgodbe

## Design

### Acquiring vcpkg

Arcade should provide functionality in the scripts in `eng/common` to acquire the vcpkg tool.
vcpkg will be able requested by an entry in the global.json file for a repository.
The Arcade build scripts will define which version of vcpkg is used.

As we will be using our own custom registry, Arcade can do a sparse checkout of the vcpkg repository to reduce the amount of data that needs to be downloaded, excluding the default registry.

To support offline builds, the vcpkg repo and a matching version of the [vcpkg-tool repository](https://github.com/microsoft/vcpkg-tool) will be included as a submodule in the source-build-externals repository.

### Custom registry

We can either create a new repository for the registry or use the source-build-externals repository as a vcpkg registry as well. This registry will, similar to the `dotnet-public` NuGet feed, contain all of the public packages that we want to consume from within our projects. However, unlike the `dotnet-public` NuGet feed, we will need to modify the portfiles for the packages that we want to consume to not use online sources in an offline build. This likely means that for each package, we'll need to create a submodule in source-build-externals or another repository for each package that we want to consume.

Alternatively, we could build an alternative mechanism in the VMR that pre-clones all dependencies when updating the VMR. This option seems more expensive, and as such we should avoid it if possible.

#### Private patches

We can use the `internal/*` branches of the internal mirror of whichever repository we choose to use for the registry to handle private patches.

When we need to create a patch, we would update the `internal/*` branch to point to private forks of the repos we want to patch or to contain the patch files for the patches we want to apply.
When we create the internal branch mirrors, we will commit a change that changes each vcpkg manifest to point at the `internal/*` branch of the registry repository instead of the public branch.

After a release, we would need to ensure that we don't push the reference to the internal registry to the public repos. Since the public repositories wouldn't be able to access the internal registry, our CI lanes should prevent this problem.

### Using vcpkg

All repositories that use vcpkg must use the manifest mode of vcpkg to declare their dependencies. Classic mode is not supported.

### Source-build partners and conditionally system dependencies

For some dependencies, we may want to use a system-installed version of the dependency in source-build scenarios.
For these cases, our source-build partners can remove the dependencies from the vcpkg manifest.
Since the CMake integration with vcpkg uses the standard `find_library` and `find_package` CMake commands, the system dependencies will be found instead of the vcpkg dependencies with no change to the CMake source code.

### Flowing native dependencies with binary caching

vcpkg has a binary caching feature that can use NuGet.
We should be able to utilize this feature for flowing native dependencies that are consumed by native code (such as `dotnet/llvm-project` assets) to avoid having to run the build through MSBuild to locate the dependencies or rebuilding the dependencies in each repository. This is not something that needs to be done immediately, but is something we can consider in the future instead of our existing package flow model.

## Q & A

- Why use vcpkg instead of another package manager?
    - Vcpkg is already used by many teams at Microsoft, and is already supported by many of the dependencies that we may want to consume.
    - Vcpkg is cross-platform.
    - Vcpkg allows us to define custom target triples so we can add support for our packages for our targets.
    - Vcpkg allows CMake consumers to consume dependencies as if they were installed on the machine instead of requiring custom CMake logic to find dependencies. Supporting changing a package between a vcpkg and system dependency would require more work with other package managers.
- What is vcpkg's minimum toolset requirements? Do these conflict with the requirements for .NET?
    - Vcpkg requires CMake 3.14 or newer. We require 3.20 or newer, so we should be fine.
    - Vcpkg requires a C++ compiler that supports C++17. Although we don't use C++17, all of the compilers we support do support C++17.
- Which packages would be migrated to use vcpkg?
    - In dotnet/runtime, we would migrate the following dependencies:
        - libunwind
          - Given that we already have to support a system-provided version of libunwind, this is a good first product dependency to migrate
    - In dotnet/runtime, we could migrate the following dependencies:
        - Rapidjson
        - zlib
        - zlib-intel
        - brotli
    - With vcpkg binary caching, we could flow dependencies from the following repos through vcpkg for non-VMR scenarios:
        - dotnet/llvm-project
        - dotnet/icu
        - > For VMR scenarios, we could either "install" these packages in a folder or consume them as source instead of using vcpkg.
    - If we switch to zlib-ng from zlib and zlib-intel, we would consume zlib-ng through vcpkg.
    - In dotnet/aspnetcore we would migrate the following dependencies if we were to support vcpkg + MSBuild:
        - GoogleTest
        - IISLib (we could pull this from microsoft/IIS.Common with a package we define. A package doesn't currently exist in the vcpkg registry for it.)
    - Some libraries that we're looking at productizing in the future have these dependencies, which we'd also use vcpkg for:
        - GoogleTest
        - Google Benchmark
        - microsoft/WIL
    - In the future, we may use microsoft/GSL, which we would use vcpkg for.
