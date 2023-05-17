## Implementation

Creating a workload set version should be a lightweight process, which involves creating a NuGet packages and corresponding installers to deliver the mapping from the workload set version to the workload manifest versions.  The .NET SDK assets and installers, workload manifests, and workload packs should already be built and should not need to be created when building a workload set.

### Current workload release schedule

Releases of .NET (both the SDK and runtime) typically align with Visual Studio releases and/or patch Tuesday.  A .NET release needs to be built and validated ahead of the deadline to insert into Visual Studio or to be submitted to Microsoft Update.  It takes time to build and validate a release of .NET, so we allow for roughly 2 weeks in the schedule for a release to build, validate, and if necessary fix any critical issues and re-build.  This means that components that ship in the .NET SDK need to be inserted into it roughly 2 weeks before the Visual Studio or Microsoft Update deadline, which is itself some time before the actual release.

Workloads such as ios, android, and maui which are built outside of the .NET Runtime do not currently have to meet the .NET SDK insertion deadlines.  This is because they are inserted into Visual Studio separately from the .NET SDK.  So when Visual Studio is installed, it installs the .NET SDK as well as the workloads that were inserted into Visual Studio.

Non Visual Studio installs of the .NET SDK do not include workloads themselves in the .NET SDK package or installer.  In this case the .NET SDK only hase workload manifests.  Because the final versions of workloads are not inserted into the .NET SDK, these manifests (which we call "baseline manifests") are not the final versions and do not refer to the workloads that are actually released.  When installing a workload, the default behavior of the .NET SDK is to first look for the latest workload manifests and install them before installing the workload.  So as long as the workload packages are released publicly (on NuGet.org) on release day, installing a workload for a non-VS install of the .NET SDK will also install the right workload versions, even though the baseline manifest versions built into that SDK are not the final release versions.

### Release schedule updates

Workload sets will be created in a separate  repo (possibly dotnet/workloadversions).  This repo will have the versions for all the workload manifests, either via Arcade dependency flow (for workloads that build as part of .NET) or via manual updates.  The build will create a json file in the rollback file format from these versions, and create NuGet packages, MSIs, and (in the future) other installers to deliver this json file as a workload set.

To insert a final workloads into Visual Studio, the versions in the workloadversions repo should be updated and a new build of that repo produced.  Then the updated workload set artifacts should be inserted into Visual Studio together with the final workloads.

We intend that building the workloadversions repo should be quick (~1 hour) so that it should not have much schedule impact on when workloads can be inserted into Visual Studio.

### Workload set packages

The Workload set NuGet package for file-based installs will be named `Microsoft.NET.Workloads.<Feature band>`.  Similar to the workload manifest NuGet packages the json file will be included in a `data` subfolder in the NuGet package.  The version number of the package will be the version of the workload set.

### Workload set disk layout

Workload version information will be stored in `dotnet\sdk-manifests\<Feature Band>\workloadsets\<Workloads Version>`, for example `dotnet\sdk-manifests\8.0.200\workloadsets\8.0.201`.   In this folder will be one or more `.json` files which will specify the versions of each manifest that should be used.  These `.json` files will use the same format as rollback files currently use.  For example, the following file would specify that the android manifest should be version 33.0.4 from the 8.0.200 feature band, while the mono toolchain manifest should be version 8.0.3 from the 8.0.100 band:

```json
{
  "microsoft.net.sdk.android": "33.0.4/8.0.200",
  "microsoft.net.workload.mono.toolchain": "8.0.3/8.0.100"
}
```

### Workload manifest layout

The layout of the workload manifests on disk will change.  Currently, they go in a path which includes the feature band but not the version number of the manifest, such as `dotnet\sdk-manifests\8.0.200\microsoft.net.sdk.android`.  This will change to also include the manifest version in the path, for example `dotnet\sdk-manifests\8.0.200\microsoft.net.sdk.android\33.0.4`.  This will allow multiple workload sets to be installed side-by-side, and for a global.json file to select which one should be used.

Currently, the manifests for each feature band update in-place, and the corresponding MSIs are upgradeable.  With the new design, each manifest version will not have an MSI upgrade relationship to other versions of that manifest.  This should also help fix issues that occur when we try to roll back to manifests prior to those installed by Visual Studio.  Now, the prior versions of the manifest can be installed side-by-side with the versions installed by Visual Studio, and the `workloadsets` files (possibly together with glebal.json) will determine which manifests are used.

### Baseline workload sets

The final workload set for a release will be produced in the workloadversions repo.  However, the installer repo will also produce a workload set for the baseline workload manifests that are included in the standalone SDK.  The version assigned to this workload set will depend on whether the SDK being built is using a "stabilized" version number.  If so, then the baseline workload set version will be `<SDK Version>-baseline.<Build Number>`, for example `8.0.201-baseline.23919.2`.  Otherwise, the workload set version will use the same version number as the .NET SDK, for example `8.0.100-preview.4.23221.6`.  The baseline workload set will be included in the layout for file-based builds of the .NET SDK, and packaged up as an MSI which is bundled in the standalone installer.

### Workload behavior when no workload patch information is installed

In some cases, internal builds of Visual Studio may not have workload set information.  In this case, when the SDK needs to display a workload set version, one will be generated using the .NET SDK feature band, the pre-release specifier `manifests`, and a hash of the manifest IDs and versions.  For example, `8.0.200-manifests.9e7f4b93`.

### Workload resolver updates

Because the `workloadsets` folder will be next to the other workload manifest folders, the resolver will be updated to ignore this folder when looking for workload manifests.  Older versions of the resolver should not be impacted, because they will not be looking in the newer version band folder.  This does mean that the resolver changes should go into Visual Studio as soon as possible so that full Framework MSBuild can work with the new manifests.

### Workload manifest garbage collection


### Rolling back and saving workload versions


### Managing workload set GC roots


### Workload updates

Currently, the .NET SDK tries to keep workloads updated.  By default, it will update to newer workload manifests whenever you install a workload.  It will also check once a day to see if updated workloads are available, and if they are it will print out a message suggesting you update workloads when you run basic commands such as `dotnet build`.

With the new system, instead of checking for updates to each manifest separately, the SDK will check for a new workload patch version.  When updating to that workload patch, the patch will be downloaded and installed, and then the manifest versions from the patch will be downloaded and installed.

### Visual Studio installed SDKs

When the .NET SDK is installed by Visual Studio, it is usually best if the workloads are also installed via Visual Studio.  That way they will get updated together with updates to Visual Studio and the .NET SDK.  If the .NET SDK is installed by Visual Studio, and someone installs workloads from the command line (for example via `dotnet workload install`), then the workloads will stop working when Visual Studio updates the .NET SDK.

Because of this, we will change CLI workload commands such as `dotnet workload install`, `dotnet workload restore`, and `dotnet workload update` to check if the current .NET SDK has been installed (only) by Visual Studio.  If so, those commands will generate an error recommending that the workloads be installed using the Visual Studio installer.  The commands will also provide an option to override the error and allow installing workloads from the command line if needed.

### Telemetry

We would like to better understand whether people are using SDKs installed by Visual Studio, standalone installs, or both.  We will add this information to the telemetry that we send.  To support this we will update the .NET SDK MSI to conditionally include a `.vs` or `.standalone` file in the SDK folder to identify how it was installed.
