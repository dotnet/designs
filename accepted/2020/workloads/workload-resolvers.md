# MSBuild SDK Resolvers and optional workloads in .NET 5

In .NET 5, we will add support for iOS and Android.  The .NET SDK (formerly known as the .NET Core SDK) will be able to build projects targeting iOS and Android.  However, the .NET SDK will not be a monolithic SDK with support for all possible project types.  Rather, the iOS and Android support (and eventually more pieces) will be delivered as optional SDK workloads which may or may not be installed.  This proposal covers how we will use MSBuild SDK resolvers to hook up the build logic from workloads at build time, and to handle scenarios where a workload required to build a project is not installed.

## Links

- [Target Framework Names in .NET 5](https://github.com/dotnet/designs/blob/master/accepted/2020/net5/net5.md)
- [NET SDK workloads overview](https://github.com/dotnet/designs/blob/workloads/accepted/2020/workloads/workloads.md) ([PR](https://github.com/dotnet/designs/pull/100))

## Experiences

- Projects using workloads just work if the workloads are installed
- When opening a project in Visual Studio where the project depends on a workload that is not installed, Visual Studio will use the in-product acquisition experience to pop up a dialog box that notifies the developer what workloads are required, where they can click through to install the workload.
- When building a project from the command line where the project depends on a workload that is not installed, a friendly error message will be generated specifying which workloads are required, and how to install them (ie by running the VS installer)
- Improve existing experiences involving SDK resolvers
  - Improve experience in VS when opening a project with a global.json that specifies an SDK that can't be found
  - When using the NuGet package MSBuild SDK resolver, the UI should not hang while the NuGet package is acquired

The following may not be delivered in .NET 5, but the design should support it:

- A developer can run `dotnet bundle restore` in order to install all the workloads required by a project and its transitive project references.

## Project files

For .NET 5, iOS, Android, and WPF/Windows Forms project will all use the same `Microsoft.NET.Sdk` MSBuild SDK in the project file.  The `TargetFramework` will be used to specify the operating system, and a project targeting Windows will use the `UseWPF` or `UseWindowsForms` properties to opt in to WPF and/or Windows Forms.  For example, we plan for an Android project to look something like this:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net5.0-android42</TargetFramework>
  </PropertyGroup>

</Project>
```

## Workload guidance

The bulk of functionality from workloads will be contained in workload "packs", which will be the unit of delivery for workloads.  This will include MSBuild targets and tasks.  The assets from the packs will be laid out under the dotnet root folder, probably in `packs/<Pack ID>/<Pack Version>` or a similar folder.

Workloads will also provide a manifest package which describes which packs are necessary for which workloads.  The manifest will specify the name and version for each pack that is available for a given .NET SDK release band, as well as which packs are required for each logical workload.  The manifest for workloads will be present even when the workloads it describes are not installed.

### Workload Manifest Targets Files

In addition to the manifest file itself (likely in json format), the workload manifest package will include a `WorkloadManifest.targets` file which should import the necessary .targets files from the workload pack if a project depends on the workload.  The `WorkloadManifest.targets` files from all workload manifests will be imported into all projects that use Microsoft.NET.Sdk.

The `WorkloadManifest.targets` should use MSBuild Sdk imports to import .targets files from an workload pack.  This should be done conditionally based on properties that determine whether the workload should be used.  For example, the Android workload manifest targets file might look like this:

```xml
<Import Project="Sdk.targets" Sdk="Xamarin.Android.Sdk"
        Condition="'$(TargetPlatformIdentifier)' == 'android'" />
```

This indicates that the `Sdk.targets` file should be imported from the `Xamarin.Android.Sdk` workload pack if the `TargetPlatformIdentifier` is `android`.  The version of the `Xamarin.Android.Sdk` workload pack to use will be the one defined in the workload manifest json file.

Because the workload manifest targets files will be imported by all projects, it is important to try to guard against bugs in these targets from causing issues with projects that don't intend to use the workload at all.  One way of reducing risk can be to split the logic into multiple targets files, where the targets files with more complex logic are only imported from the entry point workload manifest targets file based on a simple condition.  For example:

```xml
<!-- WorkloadManifest.targets (entry point): -->

<Project>
    <Import Project="Xamarin.Manifest.Android.Framework.targets" Condition="'$(TargetPlatformIdentifier)'=='android'" />
    <Import Project="Xamarin.Manifest.iOS.Framework.targets" Condition="'$(TargetPlatformIdentifier)'=='ios'" />
</Project>

<!-- Xamarin.Manifest.Android.Framework.targets -->

<Project>
    <Import Project="Sdk.targets" Sdk="Xamarin.Android.Sdk" />
    <Import Project="Sdk.targets" Sdk="Xamarin.Android.Aot.Sdk" Condition="'$(UseAot)'=='true'" />
    <!--
    Import any other sdks based on project state.
    -->
</Project>
```

In this example, the iOS and Android workloads both share the same workload manifest.  In addition to `WorkloadManifest.targets`, the manifest package includes separate targets files specific to iOS and Android.  The more complex logic for which workload packs to import from is guarded behind a simple `TargetPlatformIdentifier` check in the `WorkloadManifest.targets`.

### Side-by-side SDK support for new OS releases

Optional workloads may version semi-independently from the .NET SDK.  Workload owners may want to support side-by-side tooling for new OS versions, in order to reduce risk of introducing regressions in a patch level change.

The multiplexing between different versions of an OS SDK can be implemented in the `WorkloadManifest.targets` file that is part of the workload manifest, or other .targets it imports.  For example, the `Xamarin.Manifest.iOS.Framework.targets` file imported by `WorkloadManifest.targets` in the previous example could look like this:

```xml
<Project>
    <Import Project="Sdk.targets" Sdk="Xamarin.iOS.Sdk" Condition="$([MSBuild]::VersionLessThan($(TargetPlatformVersion), '15.0'))" />
    <Import Project="Sdk.targets" Sdk="Xamarin.iOS.Sdk.15.0" Condition="$([MSBuild]::VersionGreaterThanOrEquals($(TargetPlatformVersion), '15.0'))" />
</Project>
```

This would import the iOS targets from the Xamarin.iOS.Sdk.15.0 workload pack if the `TargetPlatformVersion` is 15.0 or higher, and from the Xamarin.iOS.Sdk workload pack otherwise.

### Workload props files

Workloads may also provide MSBuild logic in an `AutoImport.props` file in the Sdk folder of the workload pack which will be imported before the body of the project file.  We expect this to be use for default globs, for example to specify that all .xaml files should be included as `Page` items.

However, `AutoImport.props` files from workloads are subject to restrictions and must be carefully authored.  This is because since they need to be imported before the body of the project is evaluated, they can't be included conditionally based on the properties in the project that define whether the workload is in use.  So the `AutoImport.props` files from all workload packs will be imported for all projects using Microsoft.NET.Sdk.

MSBuild uses multiple phases for evaluation, and properties and imports are evaluated in a phase before items.  This means it is possible to have conditions on item or `ItemGroup` elements in a .props file that depend on properties that are defined in the body of the project or in .targets files.

So workloads may include MSBuild items in the `AutoImport.props` file if they are appropriately conditioned to only activate when the workload is in use.  They should not set MSBuild properties, as there is no way to set those properties only if the workload is in use.  If workload wants to set a default value for a property, then it should do so in its .targets file with a condition to set the property if it is not already set.

Note that in contrast to the `WorkloadManifest.targets` file, which is part of the workload manifest package, the `AutoImport.props` files are part of the workload packs (under the Sdk folder).  This is so that they can be imported only if the workload is installed, rather than imported for all workloads whether they are installed or not.  This reduces the impact of a bug in an `AutoImport.props` file, and means that if there is an issue in the .props file, it can be worked around by uninstalling the workload.

As an example, here is a slightly simplified version of what the .props file for WPF might look like:

```xml
<Project>
  <ItemGroup Condition=" ('$(_IncludeWPFGlobs)' == 'true') ">
    
    <ApplicationDefinition Include="App.xaml"
                           Condition="'$(EnableDefaultApplicationDefinition)' != 'false' And
                                      Exists('$(MSBuildProjectDirectory)/App.xaml') And '$(MSBuildProjectExtension)' == '.csproj'"
                           Generator="MSBuild:Compile" />

    <ApplicationDefinition Include="Application.xaml"
                           Condition="'$(EnableDefaultApplicationDefinition)' != 'false' And
                                       Exists('$(MSBuildProjectDirectory)/Application.xaml') And '$(MSBuildProjectExtension)' == '.vbproj'"
                           Generator="MSBuild:Compile" />


    <Page Include="**/*.xaml"
          Exclude="$(DefaultItemExcludes);$(DefaultExcludesInProjectFolder);@(ApplicationDefinition)"
          Condition="'$(EnableDefaultPageItems)' != 'false'"
          Generator="MSBuild:Compile" />

    <None Remove="**/*.xaml"
          Condition="'$(EnableDefaultApplicationDefinition)' != 'false' And '$(EnableDefaultPageItems)' != 'false'" />
  </ItemGroup>
</Project>
```

The `AutoImport.props` files should keep their top-level conditions as simple as possible, so the .targets for a workload should set a property that the `AutoImport.props` file uses for its condition:

```xml
<PropertyGroup Condition=" ('$(TargetPlatformIdentifier)' == 'windows') And
                         ('$(EnableDefaultItems)' == 'true') And ('$(UseWPF)' == 'true') And 
                         ('$(_TargetFrameworkVersionValue)' != '$(_UndefinedTargetFrameworkVersion)') And 
                         ('$(_TargetFrameworkVersionValue)' >= '$(_WindowsDesktopSdkTargetFrameworkVersionFloor)')">

  <_IncludeWPFGlobs>true</_IncludeWPFGlobs>
</PropertyGroup>
```

## Implementation

### MSBuild and resolver features

We will add the following capabilities to MSBuild [SdkResolvers](https://github.com/microsoft/msbuild/blob/master/src/Framework/Sdk/SdkResolver.cs):

- Return any number of SDK paths (zero, one, or many)
- Return MSBuild items and properties to add to the evaluation result

### New resolver behavior

We will create an MSBuild SDK resolver to handle workloads.  Its behavior should be the following:

- Look up the Name of the requested SDK in the workload manifests, matching to the workload pack IDs
- If a matching workload pack is found in a workload manifest, then get its version number, and use that version number to generate the path where that workload pack would be installed.  This should be the following, or similar: `<DOTNET ROOT>/packs/<PACK ID>/<PACK VERSION>/Sdk`
- If the workload pack is installed (ie the path (except the final `Sdk`) exists), then return the generated path
- If the workload pack is not installed, then the returned `SdkResult` should:
  - Have `Success` set to true
  - Return no SDK paths
  - Include a `MissingWorkloadPack` item to add to the evaluation result.  The identity should be the name of the workload pack / MSBuild SDK that was requested.  It should have `Version` metadata set to the version that was read from the workload manifest.

However, if the requested SDK name is `Microsoft.NET.SDK.WorkloadAutoImportPropsLocator`, then the resolver should instead behave as follows:

- Look in all workload manifests for all listed workload packs
- Find all installed workload packs from those manifests
- From the installed packs, find all of the packs that have an `Sdk/AutoImport.props` file in them
- Return the Sdk path for all of the packs that had the `Sdk/AutoImport.props` file in them

### SDK behavior

The .NET SDK (Microsoft.NET.Sdk) should:

- Set the `WorkloadManifestRoot` property to a path where workload manifest packages for the current version band will be laid out.
  - This should be `<DOTNET ROOT>/workloadmanifests/<VERSION BAND>/`
  - For example, `c:\Program Files\dotnet\workloadmanifests\5.0.100\`
  - Workload manifest packages should be laid out in folders under the root corresponding to the name of the workload manifest, but should not include a version in the path.  They should be updated in-place.  This allows the SDK to import all of the active `WorkloadManifest.targets` files with a simple wildcard import
- Include the following .targets import:

```xml
<Import Project="$(WorkloadManifestRoot)/*/WorkloadManifest.targets" />
```
- Run target to generate appropriate build error if there are any `MissingWorkloadPack` items
    - This should run early in the build, probably by hooking to run before the `_CheckForInvalidConfigurationAndPlatform` target
    - The target should (via a task) look at the workload manifests to determine which workload need to be installed to supply the workload packs which are missing.  The error message generated should include this list of workloads that need to be installed.

- Include the following import in one of its .props files:

```xml
<Import Project="AutoImport.props" Sdk="Microsoft.NET.SDK.WorkloadAutoImportPropsLocator">
```

### VS behavior

When the VS project system loads a project, it should check the evaluation result for `MissingWorkloadPack` items.  If there are any, then it should raise the VS event to veto the project load and trigger the in-product acquisition experience for those workloads.

In the same way that the SDK error generation does, the project system should map from the missing workload packs to the missing workloads via the workload manifests.  Additionally, it will need to map from the workload IDs that the .NET SDK uses to the right IDs that VS will use to install the workloads.  That mapping should either be defined in the workload manifest or in another file that is part of the same packgae.

QUESTION: Will it be OK to put this mapping of .NET workload IDs to VS workload/component IDs in the workload manifest packages?  Or do we need to supply it some other way?

### `dotnet bundle restore` (query project and references for workloads that are required)

When we add support for `dotnet bundle restore`, we will need to be able to run a target that will gather all of the workloads required by a project and its project references.  We will implement this via a new entry point target that walks the project graph and collects the `MissingWorkloadPack` items from each one.

## Other experience improvements

The additional features we plan to add to MSBuild SDK resolvers will also enable us to make the following improvements in experiences that are not directly related to workloads.

## NuGet SDK Resolver

MSBuild includes a [NuGet-based MSBuild SDK resolver](https://docs.microsoft.com/en-us/visualstudio/msbuild/how-to-use-project-sdk?view=vs-2019#how-project-sdks-are-resolved).  It will be used if a version number is specified for the SDK, either in the SDK attribute or in a global.json file.

The MSBuild evaluation blocks on the NuGet restore of the SDK packages, which can include network operations.  This can cause a poor experience in VS, as it can happen on the UI thread.

We can fix this by applying the same pattern in the NuGet SDK resolver as we use for missing workload packs.  By default, the NuGet SDK resolver would continue to work as it does today.  However, in Visual Studio it would be set to a mode which would disable acquisition of the NuGet packages.  In this mode, if an SDK NuGet package wasn't already available locally, the resolver would not download the package, but would add a `MissingMSBuildSDK` item with the name and version of the SDK that needs to be downloaded.

The project system would check for `MissingMSBuildSDK` items after the project is evaluated.  If there are any, the project system would not load the project normally.  It would launch an async acquisition process to download the NuGet packages, while showing appropriate UI (for example a spinning progress bar, or "loading..." text by the project in Solution Explorer).  When the package acquisition finishes, the project system would reload the project.

## Global.json failures

When global.json specifies an SDK that isn't available, the resolver returns an error.  In Visual Studio, this currently comes up as a dialog box when loading the project, or as the following in the Solution output:

```
C:\git\repro\new\ConsoleTest\ConsoleTest.csproj : error  : The project file cannot be opened by the project system, because it is missing some critical imports or the referenced SDK cannot be found.

Detailed Information:
Unable to locate the .NET Core SDK. Check that it is installed and that the version specified in global.json (if any) matches the installed version.
```

We would like to change the experience for this in Visual Studio.  If the requested SDK from global.json is not found, we'd like to fall back to using the SDK that would have been resolved if there was no global.json.  This will allow the project to successfully load and be browsed in Visual Studio.  We would then fail the build with an error indicating that the requested SDK wasn't available.

We can do this by having the MSBuild SDK resolver fall back to the default SDK resolution when it fails to find the version requested in global.json.  In that case, it can set a property or add an item in the returned SdkResult.  Then the .NET SDK can have a target that will check for that item or property and fail the build if it is set.
