# Build and Publish tweaks in the .NET Core SDK

In .NET Core 3, we are making Build output more similar to the Publish output.  In .NET Core 2.x and prior, dependencies from NuGet packages are not copied into the Build output.  At runtime, they are loaded from the NuGet package cache.  This meant that the Build output was not portable to other machines.

For .NET Core 3, the Build output includes an app's dependencies.  The Build and Publish output from the base .NET SDK should be the same.  Additional operations that are not part of the inner-loop development cycle can be part of Publish, such as copying content for Web apps, trimming unused dependencies, or "linking" the app to create a single file executable.

This document proposes some changes to the SDK behavior that will help us unify the Build and Publish logic.  Some of these could be breaking changes, but we believe the impact will be low.

## `Publish="False"` on `PackageReference` should apply to Build

If a `PackageReference` has `Publish="False"` metadata, then assets from that package will not be included in the Publish output.  In current previews of .NET Core 3, those assets will be included in the Build output but not the Publish output.  However, when targeting previous versions of .NET Core, no assets from NuGet packages would be included in the Build output.  Also, we are not aware of a scenario where you would want to specify that assets should not be in the Publish output, but would still want them in the Build output.

So we propose that assets from a `PackageReference` with `Publish="False"` should be excluded from the Build output as well as the Publish outptu.

## `PrivateAssets="All"` on `PackageReference` should not imply `Publish="False"`

Setting `PrivateAssets="All"` on a `PackageReference` prevents that package from flowing to consuming projects.  This means that if a NuGet package is created from the project via Pack, the package with `PrivateAssets="All"` won't be included as a package dependency.  This is typically used for packages which are used as part of the build process of a library, but are not dependencies once the library has been compiled.  (See also "development dependency" packages.)

However, `PrivateAssets="All"` by default also excludes assets from the package from the publish output.  It's not entirely clear why these two behaviors were linked to the same metadata, but it originates from the pre-1.0 project.json days.

We think now is a good time to take the breaking change to separate these concepts.  The main use of PrivateAssets is to control whether assets flow to consuming projects, and the affect on Publish behavior doesn't seem to be documented.

## Runtime package store target manifests should apply to Build

A [runtime package store](https://docs.microsoft.com/en-us/dotnet/core/deploying/runtime-store) is a known set of packages that exist in a target environment that don't need to be included in the app's publish output for framework-dependent apps.  Manifests which describe the packages that will be available in a runtime store can be specified via the `--manifest` parameter to `dotnet publish`, or via the `TargetManifestFiles` MSBuild property.  Packages specified in these manifests will not be included in the Publish output.

We would like to also exclude the same package assets from the Build output.  This will help us unify the Build and Publish output.  The build output should still be runnable on the dev machine whether the runtime store is installed or not, as the NuGet package cache should still be listed in the additionalProbingPaths of the runtimeconfig.dev.json file, so the assets should be loaded from there.

