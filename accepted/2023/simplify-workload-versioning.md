# Simplify Workload Versioning

.NET SDK Workloads are optional components of the .NET SDK.  When designing workloads, one of the goals was to be able to update workloads separately from the .NET SDK.  As a result, workloads version and update separately from the .NET SDK.  This has made it hard to understand what versions of workloads are installed or available.  Complicating this is the fact that workloads themselves don't directly have a version.  Workload manifests, which can have a many-to-many relationship with workloads, have versions.  These versions can be displayed with the `dotnet workload update --print-rollback` command, which displays output in the following format:

```
==workloadRollbackDefinitionJsonOutputStart==
{
  "microsoft.net.sdk.android": "33.0.26/7.0.100",
  "microsoft.net.sdk.ios": "16.2.1030/7.0.100",
  "microsoft.net.sdk.maccatalyst": "16.2.1030/7.0.100",
  "microsoft.net.sdk.macos": "13.1.1030/7.0.100",
  "microsoft.net.sdk.maui": "7.0.59/7.0.100",
  "microsoft.net.sdk.tvos": "16.1.1527/7.0.100",
  "microsoft.net.workload.mono.toolchain.net6": "7.0.3/7.0.100",
  "microsoft.net.workload.mono.toolchain.net7": "7.0.3/7.0.100",
  "microsoft.net.workload.emscripten.net6": "7.0.3/7.0.100",
  "microsoft.net.workload.emscripten.net7": "7.0.3/7.0.100"
}
==workloadRollbackDefinitionJsonOutputEnd==
```

This is very complicated and doesn't make it easy to understand workload versions.

## Goals

- Introduce a single version number that represents all workload versions for the the .NET SDK
- Workloads can continue to release outside the normal .NET SDK release schedule
- Deadlines for workload completion and insertion do not change from current processes
- Workload updates should require minimal (or no) additional validation of non-workload scenarios

## Workload patch versions

Currently, we use a 3 part version number for the .NET SDK, for example 8.0.100.  Typically we release a patch each month, for example 8.0.101, 8.0.102, etc.

We will create a workload patch version number that encapsulates all the workload manifest versions that are released together.  We will represent the workload patch version by adding an additional component to the .NET SDK version.  The versions of the workloads that release on the same release date as the .NET SDK will be number `.0`.  Subsequent workload updates before the next .NET SDK patch will be numbered `.1`, `.2`, etc.  For example, the full workload patch version for the initial 8.0 release would be `8.0.100.0`, and if there was a workload update before the first .NET SDK patch, the updated workload patch version would be `8.0.100.1`.

A workload patch is essentially a mapping from the workload patch version to the different versions for each workload manifest.  Creating a workload patch should be a lightweight process, which involves creating a NuGet packages and corresponding installers to deliver this mapping.  The .NET SDK assets and installers, workload manifests, and workload packs should already be built and should not need to be created when building a workload patch.

## Experience

`dotnet --version` would continue to print the SDK version, as it does today:

```
> dotnet --version
8.0.201
```

We will add a new `dotnet workload --version` command to print the workload patch version:

```
> dotnet workload --version
8.0.201.2
```

We will also update `dotnet --info` and `dotnet workload --info` to display the workload patch version.  For example:

```
> dotnet --info
.NET SDK:
 Version:              8.0.201
 Commit:               <commit>
 Workloads version:    8.0.201.2

<further information>

> dotnet workload --info
 Workloads version:     8.0.201.2
 [wasm-tools]
   Installation Source: SDK 8.0.100-preview.4
   Manifest Version:    8.0.0-preview.4.23181.9/8.0.100-preview.4
   Manifest Path:       C:\git\dotnet-sdk-main\.dotnet\sdk-manifests\8.0.100-preview.4\microsoft.net.workload.mono.toolchain.current\WorkloadManifest.json
   Install Type:        FileBased
```

When updating workloads, console output will include the old and new workload patch versions:

```
> dotnet workload install wasm-tools
Checking for updated workloads version...
Updating workloads version from 8.0.201.0 to 8.0.201.2...
Installing workload manifest microsoft.net.sdk.android version 34.0.0-preview.4.230...
<Further workload manifest update messages>
Installing pack Microsoft.NET.Runtime.WebAssembly.Sdk version 8.0.0-preview.4.23181.9...
<Further workload pack installation manifests>
Garbage collecting for SDK feature band(s) 8.0.200...

Successfully updated workloads version from 8.0.201.0 to 8.0.201.2.
Successfully installed workload(s) wasm-tools.
```

The proposed [workload history](https://github.com/dotnet/sdk/pull/30486) command will also display the workload patch versions.

The .NET SDK will periodically (once a day, by default) check for updated workload versions in order to notify users if there is an update available.  Currently, commands such as `dotnet build` print out the following message:

```
Workload updates are available. Run `dotnet workload list` for more information.
```

The `dotnet workload list` command, in turn, prints out the following message:

```
Updates are available for the following workload(s): wasm-tools. Run `dotnet workload update` to get the latest.
```

We will modify both commands to print the same message:

```
A workload update (version 8.0.201.2) is available. Run `dotnet workload update` to install the latest workloads.
```

We will add a new `--version` option to `dotnet workload update` to allow installing (or downgrading to) a specific workloads version:

```
> dotnet workload update --version 8.0.201.0
Updating workloads version from 8.0.201.2 to 8.0.201.0...
<Manifest and pack installation / garbage collection messages>
Successfully updated workloads version from 8.0.201.2 to 8.0.201.0.
```

## Specifying workload versions with global.json

We will add support for specifying the workloads version in global.json.  For example:

```json
{
  "sdk": {
    "version": "8.0.201.0",
    "rollForward": "disable"
  }
}
```

This would force the SDK to use workloads version `8.0.201.0`, and would error if that version was not installed.

We will support side-by-side workload version installations.  If 8.0.201.2 is installed, we would support running `dotnet workload install --version 8.0.201.0` to install that version of the workloads.  After installing the earlier version, the .NET SDK would still by default use the latest installed workloads version.  An earlier workloads version would only be used if it was specified in a global.json file.

NOTE: Various tools (such Azure DevOps and [GitHub actions](github.com/actions/setup-dotnet)) read global.json in order to install the right version of the .NET SDK.  Those tools would need to be updated ignore the fourth section of the SDK version number.

NOTE: We may not implement the global.json workloads version support in the same release as the rest of the changes described in this design.

