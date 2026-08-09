## Implementation

Creating a workload set version should be a lightweight process, which involves creating NuGet packages and corresponding installers to deliver the mapping from the workload set version to the workload manifest versions.  The .NET SDK assets and installers, workload manifests, and workload packs should already be built and should not need to be created when building a workload set.

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

### Workload set package versions

The (possibly 4-part) workload set version will be what is displayed by the .NET CLI and can be specified in places such as the command line and global.json files, but it won't be used as a literal package verison for any NuGet packages.  NuGet package versions will remain semantic versions.  For the NuGet packages that represent the workload set, there will be a mapping from the workload set version to both the package ID and version, where the feature band is part of the package ID, freeing up space in the version for the additional interim release number.

Since the minor version of the SDK version (and hence workload set version) is expected to remain 0, for the workload set package versions we will omit that part of the version and shift the other parts over.  For example, for workload set 8.0.203, the workload set package version would be 8.203.0.  For workload set 8.0.203.1, the workload set package version would be 8.203.1.  Because the SDK feature band is part of the workload set package ID, this mapping will still work even if we do end up having a minor release of the SDK in the future.

| SDK Version | Workload set version | Workload set Package ID | Workload set package version |
|-------------|----------------------|-------------------------|------------------------------|
| 8.0.200     | 8.0.200              | Microsoft.Net.Workloads.8.0.200 | 8.200.0 |
| 8.0.201     | 8.0.201              | Microsoft.Net.Workloads.8.0.200 | 8.201.0 |
| 8.0.203     | 8.0.203.1            | Microsoft.Net.Workloads.8.0.200 | 8.203.1 |
| 9.0.100-preview.2 | 9.0.100-preview.2.39041  | Microsoft.Net.Workloads.9.0.100-preview.2 | 9.100.0-preview.2.39041 |
| 8.0.201     | 8.0.201.1-preview    | Microsoft.Net.Workloads.8.0.200 | 8.201.1-preview |
| 8.0.201     | 8.0.201.1-preview.2    | Microsoft.Net.Workloads.8.0.200 | 8.201.1-preview.2 |
| 8.0.201-servicing.23015    | 8.0.201-servicing.23015  | Microsoft.Net.Workloads.8.0.200 | 8.201.0-servicing.23015 |


### Workload set disk layout

Workload version information will be stored in `dotnet\sdk-manifests\<Feature Band>\workloadsets\<Workload Set Version>`, for example `dotnet\sdk-manifests\8.0.200\workloadsets\8.0.201`.   In this folder will be one or more files ending in `.workloadset.json` which will specify the versions of each manifest that should be used.  These `.json` files will use the same format as rollback files currently use.  For example, the following file would specify that the android manifest should be version 33.0.4 from the 8.0.200 feature band, while the mono toolchain manifest should be version 8.0.3 from the 8.0.100 band:

```json
{
  "microsoft.net.sdk.android": "33.0.4/8.0.200",
  "microsoft.net.workload.mono.toolchain": "8.0.3/8.0.100"
}
```

Note that the workload set version that is part of the path is the user-facing, possibly 4-part workload set version, not the workload set package version.  The file-based installation code will need to apply the mapping when installing the workload sets, as will the MSI authoring code in dotnet/arcade.

### Workload manifest layout

The layout of the workload manifests on disk will change.  Currently, they go in a path which includes the feature band but not the version number of the manifest, such as `dotnet\sdk-manifests\8.0.200\microsoft.net.sdk.android`.  This will change to also include the manifest version in the path, for example `dotnet\sdk-manifests\8.0.200\microsoft.net.sdk.android\33.0.4`.  This will allow multiple workload sets to be installed side-by-side, and for a global.json file to select which one should be used.

Currently, the manifests for each feature band update in-place, and the corresponding MSIs are upgradeable.  With the new design, each manifest version will not have an MSI upgrade relationship to other versions of that manifest.  This should also help fix issues that occur when we try to roll back to manifests prior to those installed by Visual Studio.  Now, the prior versions of the manifest can be installed side-by-side with the versions installed by Visual Studio, and the `workloadsets` files (possibly together with global.json) will determine which manifests are used.

### Baseline workload sets

The final workload set for a release will be produced in the workloadversions repo.  However, the installer repo will also produce a workload set for the baseline workload manifests that are included in the standalone SDK.  The version assigned to this workload set will depend on whether the SDK being built is using a "stabilized" version number.  If so, then the baseline workload set version will be `<SDK Version>-baseline.<Build Number>`, for example `8.0.201-baseline.23919.2`.  Otherwise, the workload set version will use the same version number as the .NET SDK, for example `8.0.100-preview.4.23221.6`.  The baseline workload set will be included in the layout for file-based builds of the .NET SDK, and packaged up as an MSI which is bundled in the standalone installer.

### Workload behavior when no workload patch information is installed

In some cases, internal builds of Visual Studio may not have workload set information.  In this case, when the SDK needs to display a workload set version, one will be generated using the .NET SDK feature band, the pre-release specifier `manifests`, and a hash of the manifest IDs and versions.  For example, `8.0.200-manifests.9e7f4b93`.

### Rolling back and pinning workload versions

The following commands can be used to select workload versions which don't correspond to the latest workload set:

- `dotnet workload update --from-rollback`
- `dotnet workload update --from-history`
- `dotnet workload update --version`

When these commands run, they need to "pin" the workload versions so that the resolver will load the selected workload versions.  The information about what versions are pinned needs to be stored somewhere.  We will call the folder where it is stored the "workload install state folder".

- For MSI-based installs, it will be stored in `%PROGRAMDATA%\dotnet\workloads\<Feature Band>\InstallState`
- For file-based installs, it will be stored in `dotnet\metadata\workloads\<Feature Band>\InstallState`

If the workload versions are "pinned", there will be a `default.json` file in this folder.  The file will specify either a workload set version, or versions for each individual manifest:

```json
{
  "workloadVersion": "8.0.102"
}
```

```json
{
  "manifests":
  {
    "microsoft.net.sdk.android": "33.0.46/7.0.100",
    "microsoft.net.sdk.ios": "16.4.7054/7.0.100",
    //  etc.
  }
}
```

Normally the file should use a workload set version if possible, and only include manifest versions when a rollback file is used.  However, if there are manifests that are installed that are not part of the workload set (for example for Tizen), then the json file should include the workload set version and then the manifest versions for manifests that are not part of the workload set.

When workload versions are pinned, running `dotnet workload install` will not automatically do a workload update.

If `dotnet workload update` is run, then any pinned workload versions will be removed (the json file will be deleted), as the intent of the command is to update to the latest version.

TODO: Error if global.json specifies a workload set that isn't installed should recommend running `dotnet workload restore`.

### Workload resolver updates

Because the `workloadsets` folder will be next to the other workload manifest folders, the resolver will be updated to ignore this folder when looking for workload manifests.  Older versions of the resolver should not be impacted, because they will not be looking in the newer version band folder.  This does mean that the resolver changes should go into Visual Studio as soon as possible so that full Framework MSBuild can work with the new manifests.

The workload resolver should first look for a workload set version from a global.json file.  If not specified via global.json, then it should look in the workload install state folder for a workload set or workload manifest versions to use.  If not specified via the install state folder, then the resolver should use the latest workload set version installed for the feature band.  Finally, if no workload set is installed, the resolver should use the latest version of each manifest for the feature band.

### Installing workload set from global.json

`dotnet workload update` - If workload set is specified in global.json, this should install that workload set.  Also should register this global.json path in workload install state folder as a GC root.

- `dotnet workload restore` - global.json aware, same as update
- `dotnet workload install` - global.json aware
- `dotnet workload clean` - should look through all records of global.json, see if they are still there and up-to-date, delete as necessary before GC
- `dotnet workload roots` - (not sure about command name) List all registered global.json files and versions of workload sets they pin

When installing a workload, it is installed only for the active workload set version.  If you need it installed for other versions, you need to go to the corresponding folder with the global.json and run a workload restore or install command.  The alternative would be to install the workload for all workload sets, which doesn't seem desirable.

### Workload set / workload manifest garbage collection

- Garbage collection occurs within the scope of a feature band, reference counts are used across feature bands.
- Baseline workload sets should not be garbage collected.  They will be identified by convention as they should have a `baseline.workloadset.json` file
- Manifests installed with the SDK need to have feature band reference counts to prevent other feature bands from garbage collecting them.  This should happen normally for MSI-based installs, but file-based installs will need to ship with the appropriate marker files with the path `metadata/workloads/InstalledManifests/v1/<manifestId>/<manifestVersion>/<manifestFeatureBand>/<SDKFeatureBand>`.


### Workload updates

Currently, the .NET SDK tries to keep workloads updated.  By default, it will update to newer workload manifests whenever you install a workload.  It will also check once a day to see if updated workloads are available, and if they are it will print out a message suggesting you update workloads when you run basic commands such as `dotnet build`.

With the new system, instead of checking for updates to each manifest separately, the SDK will check for a new workload patch version.  When updating to that workload patch, the patch will be downloaded and installed, and then the manifest versions from the patch will be downloaded and installed.

### Visual Studio installed SDKs

When the .NET SDK is installed by Visual Studio, it is usually best if the workloads are also installed via Visual Studio.  That way they will get updated together with updates to Visual Studio and the .NET SDK.  If the .NET SDK is installed by Visual Studio, and someone installs workloads from the command line (for example via `dotnet workload install`), then the workloads will stop working when Visual Studio updates the .NET SDK.

Because of this, we will change CLI workload commands such as `dotnet workload install`, `dotnet workload restore`, and `dotnet workload update` to check if the current .NET SDK has been installed (only) by Visual Studio.  If so, those commands will generate an error recommending that the workloads be installed using the Visual Studio installer.  The commands will also provide an option to override the error and allow installing workloads from the command line if needed.

### Telemetry

We would like to better understand whether people are using SDKs installed by Visual Studio, standalone installs, or both.  We will add this information to the telemetry that we send.  To support this we will update the .NET SDK MSI to conditionally include a `.vs` or `.standalone` file in the SDK folder to identify how it was installed.

