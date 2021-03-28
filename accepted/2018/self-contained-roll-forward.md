# Rolling self-contained .NET Core app deployments forward to the latest patch

**Owner** [Daniel Plaisted](https://github.com/dsplaisted)

## Goal

Self-contained deployments of .NET Core apps should roll forward to the latest patch version of the .NET Core runtime.  Framework-dependent deployments should by default target the ".0" patch version of the .NET Core runtime, and will be rolled forward to the latest patch version available on the machine when they are run.

## Motivation

The behavior stated in the goal is a design that we arrived at after several iterations and various discussions.  We ended up [disabling it](https://github.com/dotnet/sdk/pull/1574) due to [issues with how it interacts](https://github.com/dotnet/sdk/issues/1570) with NuGet restore that we hadn't foreseen.  The aim of this proposal is to re-enable the roll-forward logic as described in the goal while mitigating the issues encountered with NuGet restore.

The desired behavior is described in https://github.com/dotnet/designs/issues/3, and was also [discussed](https://github.com/dotnet/sdk/issues/983) [elsewhere](https://github.com/dotnet/sdk/pull/1222).  Briefly, we want Framework-dependent deployments to target a `<major>.<minor>.0` version of the .NET Core runtime.  This is because at runtime these apps will roll forward to the latest patch version of the `<major>.<minor>` .NET Core runtime installed on the machine, and we have other mechanisms for installing patches for the shared runtime.  Targeting the latest patch at build time for framework-dependent apps [causes problems](https://github.com/dotnet/sdk/issues/860#issuecomment-286252275) because when a new patch is released, deployed apps immediately start breaking until the target environment is updated with the new patch.

Self-contained deployments, on the other hand, have no mechanism for updating the .NET Core runtime they use besides rebuilding and re-deploying the app.  Thus, by default, we would like for self-contained deployments to use the latest patch version of the .NET Core runtime that is available.

## Obstacles

The version of the .NET Core runtime that an app targets is determined by the version of the implicit package reference to the Microsoft.NETCore.App package, and is thus determined at restore time and encoded in the assets file.  However, it may not be known at restore time whether the app will be published as a framework-dependent or self-contained deployment.  Indeed, in the mainline scenarios as described in our documentation, it will not be known at restore time.  This resulted in the following behaviors in the original implementation of roll-forward, which caused us to back it out.

- If a restore operation is automatically run before publish, then this restore could modify the assets file (as opposed to being a no-op restore).  In addition to slowing down the publish operation, this restore could change the version of the Microsoft.NETCore.App package that is referenced, and it might add or remove runtime identifiers (RIDs) from the assets file.  The next time a "normal" restore happens, it would revert the assets file to the original state and also take more time as it won't be a no-op restore.
- If a restore operation was not automatically run before publish, then the deployment would target the wrong patch of .NET Core.  A self-contained deployment might use a `.0` patch version, or (more rarely) a framework-dependent deployment might roll forward to the latest patch.

## Proposal

- Implement the desired roll-forward behavior for self-contained apps
- Run a separate restore operation before publish for self-contained apps, which will use a different path for the assets file.  This will need to be implemented in `dotnet publish`, Visual Studio, and VS for Mac
- In cases where the restore operation has not been run as part of publish (for example `msbuild /t:publish`), add logic to detect that the restored version of Microsoft.NETCore.App differs from the version we would have selected in the current context, and generate an error

## Technical Background

A self-contained deployment is associated with a [runtime identifier, or RID](https://docs.microsoft.com/en-us/dotnet/core/rid-catalog).  The RID for a self-contained deployment is specified in the `RuntimeIdentifier` MSBuild property.  By default, if the `RuntimeIdentifier` property is set, then the publish operation will create a self-contained deployment.  However, it is possible to set the `SelfContained` property to false, which will create a [RID-specific framework-dependent deployment](https://github.com/dotnet/sdk/pull/1053).

In any case, mainline scenarios (as described in our [deploying with the CLI](https://docs.microsoft.com/en-us/dotnet/core/deploying/deploy-with-cli) and [deploying with VS](https://docs.microsoft.com/en-us/dotnet/core/deploying/deploy-with-vs) documentation) do not involve specifying the `RuntimeIdentifier` property directly.  Rather, the `RuntimeIdentifiers` property (notice this is plural as opposed to singular) is used in the project file to specify the list of RIDs for which self-contained deployments may be created, and the target RID is specified at publish time via the `-r` command-line option or via a dropdown in the Visual Studio publish UI.  Furthermore, it is a goal to support publishing a self-contained app for a specific RID without having to list that RID in the `RuntimeIdentifiers` property of the project.

The assets file is the result of the restore operation.  The `targets` section contains information about what assets should be used from which packages for each "target".  A target in this context is either a Target Framework Moniker (TFM) or the combination of a TFM and RID.  Examples of target values from the assets file are `.NETCoreApp,Version=v2.0`, `.NETCoreApp,Version=v2.0/win7-x64`, or `.NETFramework,Version=v4.6/win7-x86`.  There will always be a RID-less target in the assets file for each target framework in the project.  If any RIDs are specified via the `RuntimeIdentifiers` or `RuntimeIdentifier` properties, then there will also be targets in the assets file for each combination of target framework and RID.

The assets from the RID-less targets in the assets file are used to compile the app.  When publishing a self-contained app, the assets from the corresponding RID-specific target from the assets file will be included to be used at run time.  If the RID-specific assets are needed but the RID-specific target isn't found in the assets file, an error will be generated: `Assets file doesn't have a target for '.NETCoreApp,Version=v2.0/win7-x86'. Ensure that restore has run and that you have included 'netcoreapp2.0' in the TargetFrameworks for your project. You may also need to include 'win7-x86' in your project's RuntimeIdentifiers.`

## Proposed implementation

### Roll-forward

The .NET SDK targets implicitly add a package reference to Microsoft.NETCore.App when the target framework is `netcoreapp`.  The version of the package that is referenced is specified by the `RuntimeFrameworkVersion` property.  The .NET Core SDK targets will encode the latest patch version of .NET Core corresponding to each minor release of .NET Core.  If the `SelfContained` property is true (which it will be by default if a `RuntimeIdentifier` is specified), then the `RuntimeFrameworkVersion` will be set to the latest patch version for the minor version specified in the `TargetFramework`.  Otherwise, the major and minor version from the `TargetFramework` will be used, and 0 will be used for the patch version.  In either case, if the `RuntimeFrameworkVersion` was already explicitly set, it won't be modified.

A check will be added in the task that reads the assets file that will validate that the version of Microsoft.NETCore.App referred to in the assets file matches the version from the `RuntimeFrameworkVersion` property.  If not, an error will be generated.  The currently proposed text for the error is: `The project was restored using Microsoft.NETCore.App version 2.0.0, but with current settings, version 2.0.6 would be used instead. To resolve this issue, make sure the same settings are used for restore and for subsequent operations such as build or publish. Typically this issue can occur if the RuntimeIdentifier property is set during build or publish but not during restore.`

### Restore for publish

Tools which perform a publish (this includes the `dotnet` command line, Visual Studio, and Visual Studio for Mac) will run a restore operation before publishing.  If publishing a self-contained app, a the `MSBuildProjectExtensionsPath` will be overridden (for both the restore and publish operations) so that the restore output (the assets file and generated .props and .targets files) will be written to and read from this folder.

The `MSBuildProjectExtensionsPath` to use for publish will be generated by appending `publish\{Rid}` to the original `MSBuildProjectExtensionsPath`.

We should modify the `--no-restore` flag for `dotnet publish` to either:

- Not be supported (ie error out) if the RID is specified via the `-r` option
- Be supported, but not override the `MSBuildProjectExtensionsPath` as would normally be the case with the new logic.  It would use the assets file from a normal restore or build, but would often result in an error that the assets file doesn't have a section for the specified RID, or that the version of Microsoft.NETCore.App in the assets file doesn't match the version that was selected for the self-contained publish.

### Defining "latest"

As proposed, the .NET Core SDK targets will have a hard-coded list of the latest patches for each minor .NET Core release.  This couples SDK releases with runtime patches and means we would have to release an updated SDK each time there was a new runtime patch if we want self-contained apps to roll forward to it-- and that developers would need to install the new SDK in order to roll forward to the latest patch.

Thus, we'd prefer to have a different method of selecting the latest patch version.  A possibility that has been discussed is that it should be based on the latest patch version installed on the current machine.  We intend to discuss further how we could obtain the version number of the latest patch version installed on a machine.  Challenges include:

- We need to be able to do this from full MSBuild running on the full .NET Framework, so we can't just call an API in .NET Core to determine this
- We may need to obtain this version number at MSBuild evaluation time, as opposed to in a target where we can run arbitrary code

Because of these challenges, we expect to go forward with using a hard-coded list of patches in the SDK targets for the first iteration of this feature, and pursue further improvements later.

### ASP.NET Core

In .NET Core 2.1, ASP.NET Core is treated as a shared framework.  The dependency is represented by a `PackageReference` to `Microsoft.AspNetCore.App`.  A framework dependent app will roll forward to the latest installed version of ASP.NET Core the same way it will roll forward to the latest .NET Core runtime.  We would also like the behavior for self-contained ASP.NET Core apps to be similar to how the .NET Core runtime is handled- ie they should deploy the "latest" patch version of ASP.NET Core.

The difference is that the reference to `Microsoft.NETCore.App` is implicit, while the reference to `Microsoft.AspNetCore.App` is an explicit `PackageReference` in the project.  In order to support rolling the version of ASP.NET forward, by default this `PackageReference` will not have a `Version` attribute on it.  There will be code in the Web SDK targets which will add the version number to this `PackageReference` item following the same versioning rules as the implicit reference to `Microsoft.NETCore.App` will use.

## Alternative proposals

- Have a way for restore to use a different version in a PackageReference for RID-less and RID-specific targets
- Write different version to FDD runtimeconfig.json than package version
- Use a different assets for self-contained (probably fails with combination of multi-targeting and conditional `RuntimeIdentifier`)
- Add a property that enables roll-forward to latest patch version.  This would be in addition to the current proposal, and would allow opting in to roll-forward behavior without having to burn a `RuntimeFrameworkVersion` in your project.

## Notes

- `publish --no-build` - For this to work, you will need to have built with the RID specified (ie `dotnet build -r {rid}`)
