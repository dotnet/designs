In .NET 6, we will be delivering iOS and Android support as optional SDK workloads.  In the future, additional SDK components (possibly including ASP.NET and Windows Forms and WPF) will become optional SDK workloads.  This proposal describes how we will support installing these optional workloads.
 
Related links:
 
- [.NET Optional SDK Workloads](../../2020/workloads/workloads.md) - Overview
- [MSBuild SDK Resolvers and optional workloads in .NET 5](../../2020/workloads/workload-resolvers.md) - Describes where workload files are laid out on disk and how they are resolved
- [Workload Manifests](../../2020/workloads/workload-manifest.md)
 
# Installer Abstraction Layer (IAL).

The .NET SDK can be installed via a variety of different installers.  Workloads will typically be installed using the same type of installer as the .NET SDK, so the workload installation (as well as update and uninstall) process needs to support various types of installers.

In order to support these different installers, the .NET SDK will have an "installer abstraction layer" (IAL) which will allow common installation code to work with the different types of installers.  (NOTE: We've previously called this a Platform Abstraction Layer (PAL), so some documents may continue to refer to it as such.)

The IAL is an abstraction that is internal to the .NET SDK.  It is not intended as a public extensibility mechanism to allow supporting arbitrary installers, and we may make breaking changes to its API as needed.

# Supported installation types

We plan to (eventually) support the following installation types.  Some of them may not be supported in .NET 6 if we don't have workloads in .NET 6 that are supported on the corresponding platforms.

## File based installation

In this implementation of the IAL, the .NET SDK itself manages workload installations directly by writing files to the dotnet folder.  No package management or external installation technology is used. This is generally for copies of the .NET SDK which were not installed via an OS installer, for example those that are installed via the dotnet-install scripts, or via unzipping archives of the .NET SDK.

Other terms we have at times used for this installation type: .NET SDK managed, xcopy, universal installer, files on disk, NuGet installer

## Visual Studio based installation

This is an installation of the .NET SDK installed and managed by the Visual Studio installer.  The .NET SDK workloads will have corresponding Visual Studio workloads or components, which will install workload packs via Windows MSIs.  Note that the Visual Studio workloads or components should also include the necessary Visual Studio components (designers, project system, etc) to support the workload, in addition to the .NET SDK workload packs.

## MSI based installation

This is an installation of the .NET SDK installed on Windows via the .NET SDK installer bundle, which is the .exe which can be downloaded on the .NET SDK download pages.  The bundle installs the same MSIs for the .NET SDK as Visual Studio does, and the workload installer will likewise install the same MSIs for workload packs as the Visual Studio installer.

The same dotnet folder may have different versions of the SDK installed by both the Visual Studio installer and the Standalone installer, which may share the same versions of some components.  For the .NET SDK, the underlying MSIs are ref-counted so that they are uninstalled when there are no more installations that depend on them.  We will need to make sure that workload installation and management also works well when there are different install types coexisting.

## Package manager installation

We have not yet settled on how workload installation will work when the .NET SDK is installed via a Linux package manager such as APT or RPM.

## For .NET 6

The only workload that will be supported on Linux in .NET 6 will be Blazor AoT.  For .NET 6, we will recommend that developers using a .NET SDK installed via a package manager run `dotnet workload install` under `sudo`.  This will use the file based installation mechanism, ie it will download the workload packs and copy them to the .NET SDK folder.

For source-built .NET, we should not be copying non source-built files into the source-built .NET SDK folder.  For that case, we support [installing workloads to a user-local folder](https://github.com/dotnet/sdk/issues/18104) that does not require admin permission to write to.

## For .NET 7

For .NET 7, we will probably want a better option for package managers in general.  This will likely be one of the following options:

- Create workload packages for each package manager we support.  The workloads could be installed via the package managers, and `dotnet workload install` would either shell out to the appropriate package manager to install or print a message instructing the user to install the workloads via the package manager.
- Use user-local workload installs for all package manager based installs of the .NET SDK.

# Choosing the installer abstraction

Before any installation operation is started, the .NET SDK will need to choose which installer abstraction will be used and instantiate it.  To do so, it will look for marker files in the `{DOTNET ROOT}/metadata/workloads/{sdk-band}/installertype/` folder.  If an `msi` file is present, the msi based installer will be used.  Otherwise, the file based installer will be used.

This means that the installer packages that we create for the .NET SDK will need to include these files.  For Windows, both Visual Studio and the standalone .NET SDK installer will include an `msi` file in this folder.

On Mac OS, we will always use the file based installer.  There will be packages for the initial installation of the .NET SDK, but whenever the SDK is installing or updating workloads it will simply do so by copying files around on disk.

# Installation operations

## Unit of installation

Workloads are made up of workload packs.  For some installers, the *unit of installation* in the underlying installation technology will map to an entire workload.  For example, there will be a checkbox in the Visual Studio installer UI corresponding to a whole .NET SDK workload.  For other installers, the *unit of installation* will be workload packs, and it will be up to the .NET SDK to manage the relationships between the installed packs and the workloads that are supposed to be installed.  This will be the case for the file based and MSI based installers.

## Workload pack tracking and installation cleanup

Workload artifacts should not be left over after uninstalling or updating workloads.  This is complicated by the fact that the same workload pack may be used by different workloads, and even different .NET SDK feature bands.  It will be the responsibility of the installer abstraction implementation to manage this.  

For installer abstraction implementations that wrap installers where the workload will appear in a user-visible fashion (for example as a checkbox in the Visual Studio installer, an entry in Windows Add/Remove programs, or as a package installed on a Linux OS), the installer abstraction implementation will generally rely on the underlying installer technology to manage the cleanup of artifacts which may be shared with other workload installations.

Installer abstraction implementations that install workload packs individually will also need to remove those packs when they are no longer needed.  After a workload installation operation (which includes updating or uninstalling workloads), the installer abstraction implementation should remove workload packs which are no longer needed.  To support this, they will generally need to keep a record of which workload packs have been installed for which SDK feature band, along with whether the pack is still in use for that band.  This information should be kept in a form that is likely to be forward and backwards compatible across different SDK versions.  First, the workload pack installation records for any SDK feature bands which are no longer installed can be removed.  Then any workload pack installation records which are not currently used by an installed workload for the current band can be marked as not in use.  Then, any workload packs that don't have any installation records marked as in use can be uninstalled.

## SDK update process
When the .NET SDK is updated to a new version, developers may expect for the same workloads to be installed for the new version as were installed for the old version.  We probably won't be able to do this for all installer technologies.  Nevertheless, the ideal behavior would be this:

- For in-place updates to the .NET SDK, we should also update the workloads when we update the SDK
  - For SDK installations managed by Visual Studio, in-place updates can include updating to a new SDK feature band.  In that case the workload installation is managed by Visual Studio, so the workloads will be updated as we want with the plans we have today
- When installing a new version of the .NET SDK side-by-side with an existing version, we should default to installing the same workloads that were installed for the previous version.  The developer should be able to override this, for example by unchecking the corresponding checkboxes in an installer UI

Given that for some installers we won't be able to automatically, we should make it easy to install the same workloads that were installed for a previous version of the .NET SDK.  If there are workloads that were previously installed, we should also print a message as part of the first run experience telling the developer how to update them.

We will use the `dotnet workload update --from-previous-sdk` command for this.  This command will use the workloads installation records from the most recent SDK feature band prior to the current one that has any workload installation records, and install those workloads.

We will [print a message on first run](https://github.com/dotnet/sdk/issues/19060) suggesting updating the workloads in the following cases:

- A new feature band of the SDK was installed.  This will be if the following are both true:
  - There are no workload installation records for the current SDK feature band
  - There are workload installation records for any SDK feature band prior to the current one
  - In this case, the message should suggest running `dotnet workload update --from-previous-sdk`
- A patch to the SDK was installed which updated the baseline workload manifest.  This is likely to happen for workloads that are shipped in sync with the .NET SDK, such as Blazor.  We will detect this by:
  - From IAL: Get list of workload installation records for the current SDK
  - Map these via the workload manifests to workload packs that should be installed
  - Check if all those packs are installed.  If any aren't then print the the message
  - In this case, the message should suggest running `dotnet workload update`

## Updating the workload manifests

The workload manifests describe available workloads and what workload packs make up each workload.

A baseline version of each manifest will be included in the .NET SDK, which will allow the SDK to notify the user if a workload is needed to build a project.  However, these baseline manifest versions will not necessarily have the latest workload pack versions.  This is so that workloads don't have to insert each build into the .NET SDK, effectively linking the build graphs of all the workloads with the .NET SDK build graph.

Because the manifests may be out of date (especially the baseline manifest), the workload manifests will by default be updated to the latest version when installing or updating workloads.

There may be separate [advertising manifests](../../2020/workloads/workload-manifest.md#advertising), which represent a version of the workload manifest that is available but may not have been installed.  The advertising manifests will be acquired via a NuGet package and stored in `~/.dotnet/sdk-advertising/{sdk-band}/{manifest-id}/`.  The installed manifests will be in the .NET SDK installation folder, and may be installed via the native installation technology that is in use for the SDK install (for example, there will be an MSI that installs each manifest in the Program Files folder on Windows).

Updating the workload manifests will consist of updating the advertising manifests to the latest version, and then installing the advertised manifests into the dotnet folder.  To update the advertising manifest, the following process will be used for each workload manifest:

- Each workload manifest should be delivered in a NuGet package, as described in the [workload manifest spec](../../2020/workloads/workload-manifest.md#packaging).
- Download the latest version of the workload manifest NuGet package using NuGet
  - The package should be extracted into the `~/.dotnet/sdk-advertising/{sdk-band}/{manifest-id}/` folder.

*Possible optimization: For some installers, it will be necessary to have a native installation package that includes the workload manifest.  For example, there will need to be an MSI which installs the manifest files to the `dotnet` folder in `Program Files`.  To avoid downloading the same manifest twice, we could have the IAL be involved in downloading the workload manifest package, and extracting the information in it as the advertising manifest.  However, this could add significant complexity, and the manifests should be small, so we won't plan to do this optimization initially.*

To install updated manifests, the following process will be used (for each workload manifest):

- Check if advertising manifest is newer than the currently installed version (via the `version` field in the manifest, which should match the NuGet package version)
  - If the installed version of the workload manifest is greater than or equal to the advertised version, then it is already up-to-date, and we are done with the update process for this manifest
- Tell IAL: Install workload manifest (given ID and version)

## Workload installation process
 
- Input: list of workloads to install
- With rollback:
  - Unless `--skip-manifest-update` parameter has been specified
    - From IAL: Get list of installed workloads
    - Add list of currently installed workloads to the list of workloads to install (since with updated manifest, there may be updated packs to install)
    - Update workload advertising manifests
    - Install updated workload manifests
  - Ask installation IAL: is the unit of installation workloads or workload packs?
    - If unit of installation is workloads:
      - Tell IAL: Install the workloads
    - If unit of installation is workload pack:
      - Read workload manifest, find list of packs that make up workloads to be installed
      - Tell IAL: Install workload packs
  - Tell IAL: Record which workloads have been installed (per sdk feature band)
    - This is for upgrades and garbage collection
- Tell IAL: Run garbage collection (if unit of installation is packs)

## Workload uninstallation process
- Input: list of workloads to uninstall
- Ask IAL: Is the unit of installation workloads or workload packs?
  - If unit of installation is workloads:
    - Tell IAL: Uninstall workload list
  - If unit of installation is workload pack:
    - Tell IAL: Remove specified workloads from installation record
    - Tell IAL: Run garbage collection to remove leftover workload packs

## Workload update process
- Update workload advertising manifests
- From IAL: Get list of workloads installed from workload installation record
- If there are no workload installation records for the current band, then print message suggesting using the `--from-previous-sdk` option and quit since there's nothing else to do.
- With rollback:
  - Install updated workload manifests
  - Run workload installation process for list of workloads that were installed
- If unit of installation is workload pack:
  - Run garbage collection to remove leftover workload packs
- If unit of installation is workload:
  - The IAL should handle uninstalling previous workloads from the same SDK feature band (or treating the install of a newer version as a patch of the previous ones)

# Installer Abstraction API

- Get *unit of installation*: workloads or packs
- If *unit of installation* is workloads
  - Install workload
    - Optionally using offline installation cache folder
    - Should support transaction / rollback
  - Download workload artifacts to offline cache folder (for a given workload)
    - This should support using the currently installed workload manifests, or another set of manifest, which may be for a future SDK feature band.  This is so that VS for Mac can use the currently installed version of the SDK to pre-download the assets for an updated SDK.
  - Uninstall workload
  - List installed workloads
  - Not part of the API, but important for implementation: How to map from SDK workload ID to underlying installation ID and version
- If *unit of installation* is workload pack
  - Install workload pack
    - Optionally using offline installation cache folder
    - Should support transaction / rollback
  - Download workload pack artifacts to offline cache folder
    - This should support using the currently installed workload manifests, or another set of manifest, which may be for a future SDK feature band.
  - Garbage collect installed workload packs
- Install workload manifest (given a workload manifest ID and version)
  - Should support transaction / rollback
  - This may involve downloading a package or installer for the workload manifest
  - Should optionally support using offline installation cache folder
  - End result should be that the manifest under `{DOTNET ROOT}/sdk-manifests/{sdk-band}/` is installed / updated
- Does IAL support installing or just giving instructions on how to install?
  - Initial installer implementations all support installation directly, so this is not needed in the first version of the installer abstraction API
- Workload installation records
  - List installed workloads (SDK feature band)
  - Write installation record (workload ID, SDK feature band)
  - Delete installation record (workload ID, SDK feature band)
  - List feature bands that have workload installation records

# Installer implementations

## File based IAL Implementation
 
- Unit of installation: Workload packs
- Install workload pack
  - Check if workload pack is already in the dotnet folder
    - Depending on package type, this may be a check for a folder or a nupkg file on disk
    - Note that we don't check the workload pack installation record here
    - If the workload pack is already installed, then just write the workload pack installation record, and skip the rest
  - Download NuGet package
    - NuGet package type should be DotnetPlatform (will this prevent projects / packages from incorrectly taking a dependency on them?)
    - NuGet package ID and version will be exactly the same as the workload pack ID and version
    - If an offline installation cache folder was specified, we'll look for the NuGet package there first before trying to download it
    - Otherwise, we'll use the NuGet APIs to download the NuGet package to a temporary folder `{DOTNET ROOT}/metadata/temp/`
  - Determine target folder for NuGet package.  This depends on the pack type, and can be determined via the workload resolver
  - For Library and Template packs, copy nupkg to target folder
  - For other pack types, copy contents of `data/` folder in .nupkg archive to target folder
  - Write a workload pack installation record (see below)
  - To roll back an installation action, we simply delete the folder or nupkg file that we copied into the dotnet folder, and delete the workload pack installation record file
  - Cleanup: whether an install action is committed or rolled back, we will delete the nupkg file from the TEMP folder (if it wasn't already there when we started)
- Download to offline cache
  - This will use the same download mechanics as when installing a workload pack.  However, the target folder will be different (passed in via an argument) and the pack won't be installed to the dotnet folder
- Garbage collect: See below
- Install workload manifest
  - Calculate workload manifest package ID: `{ManifestID}.Manifest-{sdk-band}`
  - Download workload manifest package
  - Copy contents of `data` folder from package to `{DOTNET ROOT}/sdk-manifests/{sdk-band}/{manifest-id}/`
    - To support rollback: first copy existing contents to temporary folder
    - To rollback: delete folder in SDK and copy contents back from temporary folder
- Read / write workload installation record
  - Installation records should be empty files with workload ID name under `{DOTNET ROOT}/metadata/workloads/{sdk-band}/installedworkloads/`
  - Read: List files in band directory
  - Write: Create file
  - Delete: Delete file

### Workload Pack installation records and garbage collection

A workload pack installation record will be a folder with the path `{DOTNET ROOT}/metadata/workloads/installedpacks/v1/{pack-id}/{pack-version}/`.  The `v1` element in the path gives us some flexibility to change the format in a later version if we need to.  A reference count for an SDK feature band will be a `{sdk-band}` file under the installation record folder.  When there are no SDK feature band reference count files under a workload pack installation record folder, then the workload pack can be deleted from disk, and then the whole workload pack installation record folder can be deleted.

Thus, the garbage collection process will be as follows:

- Get installed SDK versions and map them to SDK feature bands (ie 6.0.200 or 6.0.205 would map to 6.0.200)
- Get installed workloads for current SDK feature band
- Using current workload manifest, map from currently installed workloads to all pack IDs and versions that should be installed for the current band
- For each pack / version combination where there is an installation record folder (ie `{DOTNET ROOT}/metadata/workloads/installedpacks/v1/{pack-id}/{pack-version}/`):
  - Delete any reference count files that correspond to a band that is not an installed SDK
  - If the pack ID and version is not in the list that should be installed for the current band, and there is a reference count file for the current band, then delete that reference count file
  - If there are now no reference count files under the workload pack installation record folder, then the workload pack can be garbage collected
    - Delete the workload pack file or folder from disk
    - Delete the workload pack installation record folder: `{DOTNET ROOT}/metadata/workloads/installedpacks/v1/{pack-id}/{pack-version}/`
    - Also delete the `{pack-id}` folder if there are no longer any `{pack-version}` folders under it

### Setting Unix file permissions

Some workload packs will include executable tools.  These will need to be set as executable on Unix-style operating systems.  Since NuGet packages don't natively support encoding Unix file permissions, the code that extracts the workload pack files from the NuGet packages will support a `data/UnixFilePermissions.xml` file which will describe the file permissions.  It will set the file permissions for any files listed there.  The file format will be as follows:

```xml
<FileList>
  <File Path="data/Darwin/mono" Permission="755"/>
  <File Path="data/Darwin/other" Permission="755"/>
</FileList>
```
 
## MSI based IAL Implementation

- Unit of installation: Workload packs
- Install workload pack
  - The MSI will be wrapped in a NuGet package
    - NuGet package ID will be `{pack-id}.Msi.{installer-platform}`, version will match the workload pack version
    - The installer-platform will be x64 or x86 (and possibly eventually arm64 and more).  It refers to which Program Files directory the assets will be installed to, not what architecture the assets inside the MSI are for (they will usually be architecture-neutral, and if they differ there will be different WorkloadPackIDs and the manifest RID aliases will handle that).
  - If MSI NuGet package is not in an offline cache, then download it (to temporary folder) via NuGet API
  - If MSI isn't installed, then 
    - Extract MSI from .nupkg to the MSI package cache
    - Install MSI
  - Add reference count for workload pack in registry.  Create `Dependents\Microsoft.NET.Sdk,{sdk-band}` under MSI reference counting key
    - If we don't have a direct way to get the DependencyProviderKey which is part of the MSI reference counting key, we can look it up as it is stored as a value under the workload pack installation record key (`HKLM\SOFTWARE\Microsoft\dotnet\InstalledPacks\{pack-id}\{pack-version}`)
  - If MSI NuGet package was downloaded, then delete it from the temporary folder
  - Rollback:
    - Delete registry key for reference count
    - If MSI was installed as part of this operation
      - Uninstall MSI
      - Delete MSI from MSI package cache (this only happens for rollback, the MSI is left in the package cache if the install is successful)
    - If MSI NuGet package was downloaded, then delete it from the temporary folder
- Download to offline cache
  - Download the same MSI NuGet package that would be downloaded for workload pack install
- Garbage collect: See below
- Install workload manifest
  - Acquire workload manifest MSI
    - MSI will be wrapped in a NuGet package
      - NuGet package ID will be `{manifest-id}.Manifest-{sdk-band}.Msi.{installer-platform}
    - Download workload manifest MSI NuGet package (to temporary folder)
  - Extract MSI to the package cache
  - Install MSI
  - Add reference count to workload manifest MSI: `Dependents\Microsoft.NET.Sdk,{sdk-band}` under workload manifest MSI reference counting key
  - Rollback:
    - Delete registry key for reference count
    - Uninstall MSI
    - Delete MSI from MSI package cache
    - If MSI NuGet package was downloaded, then delete it from the temporary folder
- Read / write workload installation record
  - Workload installation records will be stored as a key in the registry: `HKLM\SOFTWARE\Microsoft\dotnet\InstalledWorkloads\Standalone\{host-architecture}\{sdk-band}\{workload-id}`
    - host-architecture is the processor architecture for the current process, such as x86 or x64
  - Read: List keys under `SdkFeatureBand` key
  - Write: Create key
  - Delete: Delete key

### Elevation

Installing MSIs requires Administrator permissions.  If a workload installation command is run from a non-admin command prompt, then a UAC prompt should be displayed to elevate to Administrator permissions.

To show the UAC prompt, a separate process must be launched.  So if an operation requires admin permissions, the MSI based IAL implementation should elevate and show the UAC prompt by launching a new dotnet process with admin permissions.  The elevated process should establish a connection with the non-elevated process (likely using connection information passed via a command line argument) which allows the IAL implementation to send commands that require elevation to the elevated process.  The elevated process should remain active until the top-level workload operation is complete, in order to avoid having to elevate multiple times for a single user workload action.

### Workload pack and manifest MSIs and NuGet packages
There will be MSIs for both workload packs and workload manifests.  Both of these will also be wrapped up in NuGet packages to allow them to be downloaded via NuGet.  The MSIs and NuGet packages for workload packs and workload manifests will share the following properties:

- NuGet packages
  - There should be a single MSI to install in the `data/` folder of the NuGet package.  The IAL implementation doesn't need to know the exact filename.
  - There should be an .xml file with the same name as the MSI in the `data/` folder with information about the MSI.
    - Product Code
    - Upgrade Code
    - Product Version
    - Provider key (Dependency Provider Key)
    - Size required on disk
- MSIs
  - The MSI should create a reference counting key in the registry: `HKLM\Software\Classes\Installer\Dependencies\{DependencyProviderKey}`


### Workload pack MSIs
- DependencyProviderKey should include the Pack ID, full semantic version of the workload pack, and InstallerPlatform.  The full semantic version should be used since multiple packs with the same ID but different versions can be installed side-by-side and should have separate reference counts
  - `InstallerPlatform` won't be part of the path to the reference counting key, but 32-bit processes will end up reading / writing under the WOW6432Node key
- The MSI should create a workload pack installation record: `HKLM\SOFTWARE\Microsoft\dotnet\InstalledPacks\{pack-id}\{pack-version}`.  It should also create the following values under the workload pack installation record:
  - DependencyProviderKey: To link the workload pack installation record with the ref counting key
  - Product Code, Upgrade Code, and Product Version: To support uninstallation


### Workload manifest MSIs
- MSI should upgrade previous versions of the workload manifest MSI for the same workload manifest ID and SDK Feature Band
  - This is different than most MSIs for the .NET SDK, such as the workload packs, which install side-by-side.
  - In order to properly support upgrading, the semantic version number for the workload manifest needs to be mapped to a MSI version, preserving the version ordering present in the original semantic version.  As there are only 32 significant bits between the MSI Major, Minor, and Build numbers, it's not possible to do this for arbitrary semantic versions.  The mapping we use should ensure that for all public releases and public previews, the ordering is preserved correctly, and should make a best effort to satisfy this for daily builds.
- DependencyProviderKey should include the Manifest ID, SDK Feature Band, and the InstallerPlatform.  It should not include the workload manifest version, as updated versions of the manifest are installed in-place and should not be ref-counted separately

Reference counting for the workload manifest MSIs will work as follows:

- The baseline workload manifest MSI will be included in the standalone bundle.  Thus, when the bundle is installed, a reference from the bundle will be written under the Dependencies key
- When an updated manifest is installed by the MSI based IAL implementation, it will write another reference under the Dependencies key (of the form `Microsoft.NET.Sdk,{sdk-band},{host-architecture}`)
- If an updated manifest is installed by VS, it will also write a different reference under the dependencies key (of the form `VS.{GUID}`)
- When the standalone bundle is uninstalled, it will remove its reference count key, and then uninstall the MSI if a newer MSI hasn't been installed and there are no other reference counts
- The custom bundle finalizer will remove the reference created by the MSI based IAL implementation, and uninstall the MSI if there are no other references

### Garbage collection

The garbage collection process for the MSI based IAL will be similar to the file based IAL implementation, just with a different method of storing the workload and workload pack installation records.

- Get installed SDK versions and map them to SDK feature bands
- Get installed workloads for current SDK feature band
- Using current workload manifest, map from currently installed workloads to all pack IDs and versions that should be installed for the current band
- Iterate over installed workload pack records (in registry under ``HKLM\SOFTWARE\Microsoft\dotnet\InstalledPacks\{pack-id}\{pack-version}`)
  - Read the `DependencyProviderKey` value under the workload pack installation record key
  - Read workload pack MSI reference count keys, which will have the path `HKLM\Software\Classes\Installer\Dependencies\{DependencyProviderKey}\Dependents\Microsoft.NET.Sdk,{sdk-band}`
  - Delete any reference count keys corresponding to an SDK band which is no longer installed
  - If the pack ID and version is not in the list that should be installed for the current band, then delete the reference count key for the current band (if it exists)
  - If there are now no reference count keys for the workload pack MSI, then the workload pack can be garbage collected
    - Uninstall the MSI
    - Uninstalling the MSI should delete the workload pack ref counting key and the workload pack installation record key in the registry, as they were created by the MSI when it was installed
    - Delete the MSI from the package cache

