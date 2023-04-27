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

## Workloads versions

Currently, we use a 3 part version number for the .NET SDK, for example 8.0.100.  Typically we release a patch each month, for example 8.0.101, 8.0.102, etc.

We will create a workloads version number that encapsulates all the workload manifest versions that are released together.  A workloads version is essentially a mapping from the workloads version to the different versions for each workload manifest.  For public releases, the workloads version will use the same version number as the .NET SDK.  For example, when .NET SDK 8.0.101 releases, there will be a corresponding workloads version 8.0.101 that corresponds to the versions of the workloads that were released together with that .NET SDK.

## Baseline workload versions

.NET SDK workloads have similar shipping deadlines as the .NET SDK.  That means that the .NET SDK itself can't include final versions of workloads which are built outside of the .NET build, as building and signing off on those workloads happens in parallel with .NET build and signoff.  So the .NET SDK ships with *baseline* workload manifests, which are mainly used to enable the .NET SDK to list which workloads are available, and to know if a project requires a workload which isn't installed.  By default, when a workload is installed via the .NET CLI, the .NET SDK will first look for and update to the latest workload manifests for the current feature band before installing workloads.

When we build the .NET SDK, we will assign a workloads version to the baseline workload manifest versions that are included in that SDK.  For releases with stabilized version numbers, we need a different workloads version number for the baseline workload manifest versions than for the stabilized version number for the .NET SDK.  In that case, we will use `baseline` as a the first semantic version pre-release identifier, followed by build number information.  For example, the baseline workloads version for 8.0.201 could be `8.0.201-baseline.23919.2`.

There are various ways we could display this baseline version number:

- `8.0.201-baseline.23919.2`
- `8.0.201-baseline`
- `8.0.201 (Baseline)`

For non-stabilized builds of the .NET SDK, we will use the same (prerelease) version number for the workloads version as we do for the .NET SDK version.

QUESTION: Will it be OK for the `baseline` versions to be semantically less than `preview` and `rc` versions?

## Workload versions in Visual Studio

Visual Studio includes the .NET SDK, but rather than including baseline manifest versions for the .NET SDK workloads, it includes the final workload manifests for a given release.  These manifests are inserted into Visual Studio together with the workloads.

For public releases of Visual Studio, the workloads version information should be created and inserted into Visual Studio together with the workloads.  However, internal builds of Visual Studio may not have an assigned workloads version.  In that case, the .NET SDK will use the same basic logic as it currently does to select manifests.  This involves finding the latest installed manifest for the current feature band, and if an expected manifest isn't found for the current feature band, then falling back to previous feature bands.  In this case, when a workloads version is needed, the .NET SDK will create a version using the .NET SDK feature band, the pre-release specifier `manifests`, and a hash of the manifest IDs and versions.  For example, `8.0.200-manifests.9e7f4b93`.

## Experience

`dotnet --version` would continue to print the SDK version, as it does today:

```
> dotnet --version
8.0.201
```

We will add a new `dotnet workload --version` command to print the workloads version:

```
> dotnet workload --version
8.0.201
```

We will also update `dotnet --info` and `dotnet workload --info` to display the workloads version.  For example:

```
> dotnet --info
.NET SDK:
 Version:              8.0.201
 Commit:               <commit>
 Workloads version:    8.0.201

<further information>

> dotnet workload --info
 Workloads version:     8.0.201
 [wasm-tools]
   Installation Source: SDK 8.0.100-preview.4
   Manifest Version:    8.0.0-preview.4.23181.9/8.0.100-preview.4
   Manifest Path:       C:\git\dotnet-sdk-main\.dotnet\sdk-manifests\8.0.100-preview.4\microsoft.net.workload.mono.toolchain.current\WorkloadManifest.json
   Install Type:        FileBased
```

When updating workloads, console output will include the old and new workloads versions:

```
> dotnet workload install wasm-tools
Checking for updated workloads version...
Updating workloads version from 8.0.201-baseline.23919.2 to 8.0.201...
Installing workload manifest microsoft.net.sdk.android version 34.0.0-preview.4.230...
<Further workload manifest update messages>
Installing pack Microsoft.NET.Runtime.WebAssembly.Sdk version 8.0.0-preview.4.23181.9...
<Further workload pack installation manifests>
Garbage collecting for SDK feature band(s) 8.0.200...

Successfully updated workloads version from 8.0.201-baseline.23919.2 to 8.0.201.
Successfully installed workload(s) wasm-tools.
```

The proposed [workload history](https://github.com/dotnet/sdk/pull/30486) command will also display the workloads versions.

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
A workload update (version 8.0.202) is available. Run `dotnet workload update` to install the latest workloads.
```

We will add a new `--version` option to `dotnet workload update` to allow installing (or downgrading to) a specific workloads version:

```
> dotnet workload update --version 8.0.202
Updating workloads version from 8.0.201 to 8.0.202...
<Manifest and pack installation / garbage collection messages>
Successfully updated workloads version from 8.0.201 to 8.0.202.
```

## Specifying workload versions with global.json

We will add support for specifying the workloads version in global.json.  For example:

```json
{
  "sdk": {
    "workloadsVersion": "8.0.201"
  }
}
```

This would force the SDK to use workloads version `8.0.201`, and would error if that version was not installed.

We will support side-by-side workload version installations.  If 8.0.202 is installed, we would support running `dotnet workload install --version 8.0.201` to install that version of the workloads.  After installing the earlier version, the .NET SDK would still by default use the latest installed workloads version.  An earlier workloads version would only be used if it was specified in a global.json file.

NOTE: We may not implement the global.json workloads version support in the same release as the rest of the changes described in this design.

## Development build behavior

Currently, when the .NET SDK looks for workload updates, it looks for the latest available version of each individual manifest.  We will change this so that it will look for the latest "workloads version", which is a package that we create and publish.

However, for development builds of the .NET SDK, we may want to retain the old behavior of updating each manifest individually.  This would allow the latest workloads to be used with a development build even if they hadn't flowed into the installer build and we hadn't created a "workloads version" package.

We will add a `--update-manifests-separately` parameter to the `dotnet workload update` and `install` commands, which will opt in to the old update behavior.  We may also make this the default behavior for development builds, though there may not be a good way to tell whether a given build is a "development" build or a build that will be released as an official public preview.  If development builds aren't signed the same way as full preview builds are, we may be able to key off of that.