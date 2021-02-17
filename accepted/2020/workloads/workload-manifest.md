# .NET SDK Workload Manifests

# Overview

.NET SDK workload manifests describe workloads that are available to be installed in the .NET SDK.  They provide version mappings that allow MSBuild targets and other SDK tools to resolve the packs that comprise these workloads from the SDK installation directory, and can be used to acquire, repair, and update the workloads and their components.

This document describes the format and versioning of the manifest. The installation and update experience will be described in other documents.

# Definitions
## SDK Band

[.NET SDK versions](https://docs.microsoft.com/en-us/dotnet/core/versions/#versioning-details) take the form 3.1.100, 3.1.101, 3.1.200. The first two components of this version represent the major and minor version of the runtime in the SDK. The third component (the “hundreds”) is more complex: the first digit represents the feature release of the SDK, and the last two digits represent the patch level of the SDK and runtime combined.

In the context of this document we will refer to an *SDK band* as the common portion of the version of all the SDK releases that differ only by patch level. For example, 3.1.100 and 3.1.105 are in the 3.1.100 band, while 3.2.100 is in the 3.2.100 band and 3.1.203 is in the 3.1.200 band.

## SDK Pack

An SDK pack is a unit of packaging for the assets that make up the SDK. Packs have an ID and version and are installed into folders in the SDK directory.

Packs will be installed using CLI tooling or by a native installer such as the Visual Studio installer. It is likely that packs will be distributed via NuGet at some point, so it is recommended that pack authors ensure the corresponding NuGet IDs are reserved for their packs (either via reserved prefixes or placeholder packs) even if other distribution methods are used initially.

SDK packs are used for construction, composition and distribution only, they are not exposed through the user experience.

## SDK Workload

An SDK workload is a semantic name describing a set of one or more SDK packs. When a developer wants to be able to build a particular kind of app, they would expect to install a workload. Workloads should correspond to user scenarios.

Here are some examples of SDK workloads and the packs that they might include:

| Workload ID | Content|
|--|--|
| xamarin-android | Framework, runtime, build logic, tools and templates for Xamarin.Android.|
| xamarin-forms | Templates for Xamarin.Forms and the Xamarin.Forms NuGet package used by the templates.<br><br>This would also include any packs necessary to build and run Xamarin.Forms projects. That could be done by depending on the xamarin-ios, xamarin-android and uwp components, or perhaps instead some kind of lightweight Xamarin.Forms host. |
| xamarin-forms-android | Templates for Xamarin.Forms and the Xamarin.Forms NuGet package used by the templates.<br><br>This would also include any packs necessary to build and run Xamarin.Forms projects on Android by depending on xamarin-android.|
| xamarin-android-llvm | The LLVM AOT compiler for Android, omitted by default from the xamarin-android component because it’s large and not all apps use it.<br><br>It may make sense for this to depend on the xamarin-android workload as it’s useless by itself.|

# Versioning

## Manifest Versioning

Manifest versioning broadly tracks SDK versioning: manifests are always associated with an SDK band, and each SDK instance uses only the manifests corresponding to its own band. This association only holds at the band level; manifest versions are completely independent of SDK patch levels. An SDK uses the latest available versions of the manifests available for its band.

To make this relationship more explicit, the SDK band for a manifest is encoded into the manifest’s ID e.g. `Microsoft.NET.Sdk.Android.SdkManifest-5.0.200`. The “version” of a manifest package can then be as simple as a monotonic integer value.

> NOTE: Workloads are completely independent of SDK servicing version e.g. SDK 6.0.100 will get the exact same workload manifests as SDK 6.0.106.

## Pack Versioning

A manifest contains a list of pack IDs and their corresponding versions. A single version is defined for each pack: the most up-to-date version of that pack that is expected to be used with that SDK band. For cases where multiple versions of a pack need to be made available within an SDK band, an aliasing system is defined.

This intentionally separates pack versions from component definitions. A pack may be included in multiple workloads, and this separation ensures that all workloads get a consistent version of each pack. There’s also a semantic separation: the workload definition list describes which packs are included in each workload, while the pack version list describes the set of pack versions that are supported together.

This is intended to support a model where code within the SDK such as MSBuild targets can refer to a pack by ID only, and does not need to embed the version. The version of the pack can then be resolved from a centralized location, the workload manifest.

There are cases where a single workload may need to contain multiple versions of a single pack side-by-side (SxS), or multiple components may need different versions of a single pack. This may apply, for example, to major runtime versions. For these cases an aliasing mechanism is provided: a meaningful alias may be defined and mapped to a different version of a pack that is already included in the list. For example, `Foo-1.x` could be defined as an alias to version `1.0.5` of pack `Foo`, while `Foo` itself could be mapped to `2.1.4`. These aliases IDs allow code within the SDK to refer to a multiple SxS versions of a pack in an intentional way without needing to embed the exact version. This is similar to how dynamic libraries on Linux libraries support SxS for multiple ABIs.

A practical example of aliasing is demonstrated in the [Side-by-Side Workload Pattern](#side-by-side-workload-pattern).

# Pack Kinds

The following kind of packs are permitted in manifests: targeting and runtime packs, SDK packs, library packs, template packs, and tools packs.

All packs are expected to be installed into the `dotnet` folder.

## Targeting and Runtime Packs

The [targeting and runtime packs](https://github.com/dotnet/designs/blob/master/accepted/2019/targeting-packs-and-runtime-packs.md) introduced in .NET Core 3.0 are permitted in workloads. Targeting packs contain the framework assemblies that a project compiles against at build time, and runtime packs contain the framework and runtime binaries used to execute the project. They install into `dotnet/packs/{pack-id}/{pack-version}` and are resolved at build time using `FrameworkReference` MSBuild items.

Framework references are expected to be added to a project automatically by the manifest’s MSBuild targets based on the TFM. There will be a mechanism that allows the targets to omit the pack version from the framework reference, so that all versions are centralized in the manifest. Targets may add multiple targeting or runtime pack references, for example when building a `net5.0-ios` project they could add a reference to a pack containing the `net5.0` reference assemblies and a pack containing the reference assemblies for the iOS platform bindings.

Manifest targets may choose to add additional framework referenced based on MSBuild properties such as the `UseWinforms` and `UseWPF` properties.

## SDK Packs

SDK packs are MSBuild SDK NuGet packages, but they are installed to `dotnet/packs/{pack-id}/{pack-version}`. They are expected to be automatically referenced by the targets as described in the [workload resolvers](https://github.com/dotnet/designs/blob/master/accepted/2020/workloads/workload-resolvers.md) specification.

## Library Packs

Library packs are normal NuGet packages. When a library pack is installed, the nupkg package file is placed in `dotnet/library-packs/` but are *not* extracted.

This location is used as a local feed by NuGet, not a fallback folder. If ones of these packages gets used, it will be extracted into the global packages folder. If the SDK is updated and the library pack is removed or replaced with a newer version, projects that have already been created and restored will continue to be able to use the extracted copy from the global packages folder.

This is intended to be used to pre-download NuGet packages referenced by templates so that a workload can work offline after installation. It should be used sparingly i.e. only for NuGets referenced by the core templates with default or popular options.

## Template Packs

Template packs are NuGet packages to be used by the dotnet templating engine. When a template pack is installed installed, the nupkg package file is placed in `dotnet/template-packs/` but is *not* extracted. Visual Studio and `dotnet new` are expected to automatically detect when templates nupkgs are installed and uninstalled to this folder and update their template hives automatically.

> NOTE: This may be changed to extract the nupkg package files if that works better for the template engine. This would be a purely internal change and would not affect workload owners or consumers at all.

## Tools Packs

Tools packs are .NET global tool packages. Tools packs are installed to `dotnet/tools-packs/{package-id}/{package-version}/` and can be invoked via `dotnet <toolname>`, e.g. `dotnet foo`.

When a tool is invoked via `dotnet <toolname>,` the runtime will use global.json to determine which SDK band to use, then will use that SDK band's workload manifests to determine the appropriate version of `dotnet-<toolname>` to run. If the tool is found in the manifest but is not installed, then it will print a message informing which workload must be installed.

Tools packs are intended to be used for tools that are exposed as part of the development experience. Tools that are only expected to be invoked by build targets should be distributed in an SDK pack.

# Manifest Files

## Composition

Multiple manifests may be present in a .NET SDK band and can be thought of fragments that combine to define the full set of available workloads and the packs that comprise them. Manifests are not an extensibility mechanism; they are intended to provide flexibility in the distribution and composition of a coherent whole.

Here are some examples of manifests that might compose together, but could all be produced from separate repositories using separate release processes and updated independently at different points in time without coordination:

* a *dotnet* manifest used to provide updated versions of in-box runtimes
* a *ios* manifest used to describe and update the iOS workloads and tooling
* an *ios-runtime* manifest containing runtime packs to be included in the iOS workloads defined in the *ios-runtime* manifest
* a *uwp* manifest used to describe and update the UWP optional components.

Each manifest has its own ID, and manifests are versioned independently. Workloads and packs may not be duplicated in multiple manifests within a band, though manifests may reference workloads and packs defined in other manifests that are expected to be present. The latest available version of all the manifests for an SDK band must always be able to be combined into a single consistent whole.

Manifests may also provide logic to import workload packs automatically via a `WorkloadManifest.targets` MSBuild file beside the manifest file, and all of these workload targets will be unconditionally imported into all projects. Workload targets files contain conditioned imports to add automatic referenced to SDK packs based on project properties, for example the Android workload targets could automatically import the Android tooling SDK pack into projects with a `TargetPlatform` value of `android`. Usage and recommended patterns are described in more detail in the [workload resolvers spec](/workload-resolvers.md).

## Packaging

Manifests are packaged and distributed as NuGet packages. They will use the existing package type called `DotnetPlatform` so they cannot be accidentally used as a `PackageReference`. The manifest’s `sdk-manifest.json` and `WorkloadManifest.targets` are in the root folder of the NuGet. The ID of the NuGet is `{manifest-id}-{sdk-band}` and the version of the NuGet is the manifest version.

As manifests are NuGets, they use the same distribution mechanisms as packs, allowing for a consistent experience. For example:
* a preview update for a workload could be made available as a NuGet feed containing an updated manifest for a stable SDK band
* manifests and packs could be copied to an air-gapped machine and set up as directory feed, making the full workload experience available offline

## Installation

Manifests are installed to `dotnet/sdk/{sdk-band}-manifests/{manifest-id}/` in an extracted form. These are called the *installed manifests* (and targets).

The dotnet SDK is expected to include baseline versions of the installed manifests and targets for all manifests that are known and supported.

The purpose of this *baseline manifest* is to:
* register the manifest ID so the SDK can fetch updated versions from NuGet
* provide names and descriptions of workloads to be displayed in any user experience that lists available workloads
* list the SDK packs that are referenced by the workload targets so that the workloads SDK resolver can determine that they are workload packs
* list the workloads that could be installed to satisfy missing workload packs

As long as none of these change, the baseline manifest in the SDK does not need to be be updated. This simplifies the release process as it means that manifest owners will rarely need to insert updated baseline manifests into the SDK. For example, if pack versions are updated, but no workloads are added, changed or removed, then the baseline manifest does not need to be updated. Any operation that would install packs will first download and install an updated version of the manifests from NuGet and use that instead.

To mantain installation coherence, any workload management operation (install, uninstall, repair or update) for an SDK band must be a transactional operation that:
* updates all the installed manifests for that SDK band to the latest available version
* updates all workloads and packs in that SDK band to match the updated manifests

## Advertising

The .NET tooling will automatically and opportunistically download updated versions of all manifests for the current SDK band and unpack them to `~/.dotnet/sdk-advertising/{sdk-band}/{manifest-id}/`. These user-local updated copies of the manifest are known as *advertising manifests.* The advertising manifests are used when listing workloads that are available to be installed, and for the build tooling to produce warnings that newer versions of installed components are available. They are **not** used in pack resolution or installation.

# Format

Manifests are json files. Comments are supported, both `//` and `/* */` styles.

The toplevel is a JSON object, containing the following keys:

| Key | Type | Value | Required |
|--|--|--|--|
| `version` | int | The version of the manifest. Must match the version of the NuGet package that contains the manifest. | Yes |
| `description` | string | Description of the content and/or purpose of the manifest. This is primarily for commenting and/or diagnostic purposes and is not expected to be surfaced in the UX. | No |
| `workloads` | object | Workload definitions keyed by workload ID. | No |
| `packs` | object | Pack definitions keyed by pack ID. | No |
| `data` | object | Allows manifests to include arbitrary key-value without risk of conflict. | No |

## Workload Definitions

Workload definitions take the following form:

| Key | Type | Value | Required |
|--|--|--|--|
| `abstract` | bool | If `true`, this workload can only be extended, and is never exposed directly as an installable workload. Default is `false`. | No |
| `kind` | string | Either `build` or `dev`. Default is `dev`. | No |
| `description` | int | User-visible description for the workload. | Yes if dev and non-abstract |
| `packs` | string array | IDs of the packs that are included in the workload. | No |
| `extends` | string array | IDs of workloads whose packs should be included in this workload. | No |
| `platforms` | string array | Limits the workload and workloads that extend it to only be shown and installed on these host platforms. The strings are RIDs. | False |

At least one of `extends` or `packs` is required. If a workload resolves to zero packs, which is possible when some packs are platform-specific, it is implicitly abstract. A workload may transitively include the same pack multiple times or extend the same workload multiple times, and they will be deduplicated. As a consequence, recursive `extends` references are technically permitted but redundant and although they may result in validation warnings they will not result in runtime errors.

The `kind` allows structuring workloads into smaller pieces so that their download and install footprint on CI is smaller. `build` workloads should contain only the packs that are used to build projects. They do not need descriptions as they are not expected to be shown in the UX - they will only be used via a CI-specific UX such as `dotnet workload restore --build-only`.

> *NOTE:* scenario-specific workload restore operations such as build-only restore have not yet been defined so this metadata is currently unused

Note that `extends` is functionally a dependency system and a way to factor out common sets of packages from workloads. By analogy to package managers such as apt-get and NuGet, workloads are metapackages that only permit unversioned dependencies and packs are packages that are only installable transitively.

## Pack Definitions

A pack definition maps a pack ID to a NuGet package, along with some additional metadata describing how it should be handled.

At minimum the type and version of the NuGet package must be provided. The NuGet package's ID defaults to the same as the pack ID.

Pack definitions take the following form:

| Key | Type | Value | Required |
|--|--|--|--|
| `kind` | string | Type of the pack. Valid values are `sdk`, `framework`, `library`, `template`, `tool`. | Yes |
| `version` | string | Version of the NuGet package. | Yes |
| `alias-to` | object | Optional platform-dependent NuGet package ID | No |

The `framework` pack kind is used for runtime packs and targeting packs. The workload system does not make a distinction between them at this time.

The NuGet package ID for a pack may be overridden in a platform-dependent way using *aliasing*. The optional `alias-to` value is a JSON object, where the keys are host platform RIDs, and the values are NuGet package IDs. The `"any"` RID may be used to alias a pack on all platforms.

For example, an SDK pack might contain a compiler that's a native executable and hence depends on the host platform:

```json
"foo.sdk.compiler": {
    "version": "1.0",
    "kind": "sdk",
    "alias-to": {
        "osx-x64": "foo.sdk.compiler.mac",
        "win-x64": "foo.sdk.compiler.windows"
    }
}
```

Workload definitions and SDK imports would refer to this as `"foo.sdk.compiler"`, but on Windows the pack that gets installed and resolved would be `"foo.sdk.compiler.windows"`. The `"foo.sdk.compiler.windows"` pack would be present on disk and its ID would be visible in resolved paths, but otherwise transparent and not able to be used directly.

Note that this is the _host_ architecture. If this compiler were a cross-compiler and thus had  a _target_ architecture, that would be expected to be the pack ID:

```json
"foo.sdk.compiler.ios-arm64": {
    "version": "1.0",
    "kind": "sdk",
    "alias-to": {
        "osx-x64": "foo.sdk.compiler.ios-arm64.mac-host",
        "win-x64": "foo.sdk.compiler.ios-arm64.windows-host"
    }
}
```

MSBuild targets can load an SDK pack based only on the `foo.sdk.compiler.{target-architecture}` pack ID, and aliasing will take care of mapping this to the appropriate host-and-target specific NuGet package.

Another use of aliases is to install multiple version bands of the same pack by synthesizing new versioned pack IDs:

```json
"foo.framework": {
    "version": "2.0.4",
    "kind": "framework"
},
"foo.framework.1": {
    "version": "1.3.2",
    "kind": "framework",
    "alias-to": {
        "any": "foo.framework",
    }
},
```

A workload or framework reference that referenced `"foo.framework"` would get `"foo.framework"` version `2.0.4`, while a workload or framework reference that referenced `"foo.framework.1"` would get `"foo.framework"` version `1.3.2`,

## Example Platform SDK Manifest

Here is a *hypothetical* example manifest. It's not prescriptive but demonstrates concepts and patterns that can be used by platform implementators.

```json5
{
    "version": 5,
    "workloads": {
        // this is a dev workload that would typically be installed
        // by a developer getting started with this platform. it's
        //composed of several smaller build workloads plus a template
        // pack for creating projects.
        //
        // a more experienced developer provisioning a new machine
        // might choose to instead restore an existing solution
        // or install a larger workload knowing they'd need more
        // of the optional pieces.
        "xamarin-android": {
            "description": "Create, build and run Android apps",
            "kind": "dev",
            "packs": [
                "Xamarin.Android.Templates",
            ],
            // on dev machines we expect to pre-install support
            // for common device architectures
            "extends": [
                "xamarin-android-build",
                "xamarin-android-build-armv7a",
                "xamarin-android-build-x86"
            ],
        },
        "xamarin-android-build": {
            "description": "Build and run Android apps",
            "packs": [
                "Xamarin.Android.Sdk",
                "Xamarin.Android.BuildTools",
                "Xamarin.Android.Framework",
                "Xamarin.Android.Runtime",
                "Mono.Android.Sdk"
            ]
        },
        // on CI machines, this will only be installed if
        // the app actually targets the armv7a architecture
        "xamarin-android-build-armv7a": {
            "kind": "build",
            "packs": [
                "Mono.Android.Runtime.Armv7a",
            ],
            // the dependency is likely redundant in practice as any
            // workload restore that resolves xamarin-android-build-armv7a
            // will also resolve xamarin-android-build, but let's be
            // explicit
            "extends": [ "xamarin-android-build" ],
        },
        // on CI machines, this will only be installed if
        // the app actually targets the x86 architecture
        "xamarin-android-build-x86": {
            "kind": "build",
            "packs": [
                "Mono.Android.Runtime.x86",
            ],
            "extends": [ "xamarin-android-build" ],
        },
        // this is an optional workload component that is only
        // expected to be installed for projects that use AOT
        "xamarin-android-aot": {
            "description": "Ahead of Time compilation for Xamarin.Android using LLVM",
            "packs": [ "Xamarin.Android.LLVM.Aot.armv7a" ],
            "extends": [ "xamarin-android" ]
        },
        // convenience for devs who want to pre-install everything
        //
        // in practice there might be a number of dev workloads
        // covering all of the common scenarios
        "xamarin-android-complete": {
            "description": "All Xamarin.Android-related components",
            "extends": [ "xamarin-android", "xamarin-android-aot" ]
        }
    },
    "packs": {
        // this has the bits for compiling an APK, generating interop code, etc
        "Xamarin.Android.Sdk": {
            "kind": "sdk",
            "version": "8.4.7"
        },
        "Xamarin.Android.Templates": {
            "kind" : "template",
            "version": "1.0.3"
        },
        // reference assemblies for net5-android bindings
        "Xamarin.Android.Framework": {
            "kind" : "framework",
            "version": "8.4"
        },
        // implementation assemblies net5-android bindings
        "Xamarin.Android.Runtime": {
            "kind" : "framework",
            "version": "8.4.7.4"
        },
        // targets and tools for taking IL assemblies and producing a set of
        // binaries that can run on android. this comes from dotnet/runtime,
        // and its tasks are invoked by the Xamarin.Android.Sdk targets that
        // handle packing and bindings-related tasks
        "Mono.Android.Sdk": {
            "kind" : "sdk",
            "version": "7.0.1"
        },
        // runtime binaries for x86 devices
        "Mono.Android.Runtime.x86": {
            "kind" : "framework",
            "version": "7.0.1"
        },
        // runtime binaries for x86 devices
        "Mono.Android.Runtime.Armv7a": {
            "kind" : "framework",
            "version": "7.0.1"
        },
        // build tools for Android that include native binaries that
        // are specific to the host platform.
        "Xamarin.Android.BuildTools": {
            "version" : "8.4.7",
            "kind": "sdk",
            "alias-to": {
                "osx-x64": "Xamarin.Android.BuildTools.MacHost",
                "win-x64": "Xamarin.Android.BuildTools.Win64Host"
            }
        },
        // this is also has host specific binaries. although it is an
        // "sdk" pack the MSBuild logic might be trivial and simply
        // set properties with the compiler location to be used by
        // the targets in Mono.Android.Sdk
        "Mono.Android.LLVM.Aot.armv7a": {
            "version" : "8.4.7",
            "kind": "sdk",
            "alias-to": {
                "osx-x64": "Mono.Android.LLVM.Aot.armv7a.MacHost",
                "win-x64": "Mono.Android.LLVM.Aot.armv7a.Win64Host"
            }
        }
    }
}
```

# Side-by-Side Platform Updates

In .NET 5, the [TFM is used to specify the .NET version](https://github.com/dotnet/designs/blob/master/accepted/2020/net5/net5.md) (e.g. .NET 5, .NET 6), and may also be used to specify a target platform (e.g. iOS, Android, WinUI) to access platform-specific API. If the TFM specifies a platform, it also implicitly or explicitly specifies a *Platform API Version*. Developers are generally expected to use the latest platform API version, specify a minimum OS version, and [use runtime checks to guard use of APIs from newer OS versions](minimum-os-version/minimum-os-version.md). Templates burn in the most recent OS API version so that new projects start out in the recommended configuration, warning-free and with access to the latest APIs.

.NET versioning policy requires that updates *within* an SDK feature band only contain low-risk servicing updates, not new features. This means that developers can feel safe installing servicing updates. They know that it's not going to allow them to accidentally make the project depend on APIs that will break it for other members on their team who are not as up to date.

However, there is one exception to this: OS releases do not coincide with .NET releases, so we need to be able to release platform updates between .NET releases: APIs, runtimes, build logic and tools.

The solution is to make these platform updates opt-in by shipping workloads for the updated version of the platform side-by-side with the existing workloads for the platform _within the same SDK band_, and having the `WorkloadManifest.targets` determine which to use [based on the platform API version](workload-resolvers.md#side-by-side-sdk-support-for-new-os-releases) expressed in the `TargetPlatformVersion` component of the project's `TargetFramework`. This makes these platform updates opt-in on the project level.

> ***NOTE:*** Projects depending on any new SDK functionality (e.g. those that might appear in 3.1.200 band compared to 3.1.100 band) would have similar issues, and those dependencies can be handled (in a limited fashion) with global.json. However, platform updates are expected to be far more impactful than other changes that might be shipped in SDK updates, may happen between SDK band releases, and can be expressed in a more clear and comprehensible way using the OS platform versions, so we address them specifically.

## Side-by-Side Workload Pattern

The side-by-side workload pattern is to duplicate the build workloads and the packs they use, and add versioned suffixes to one of the copies of each, for example `ios-14` and `ios`. In this example, `ios` is an *unversioned workload*, and `ios-14` is a *versioned workload*. The unversioned workload should be the newest version, i.e. the `ios` workload would be iOS 15 while the `ios-14` would be iOS 14.

Because projects do not explicitly depend on a workload, but get it indirectly via the manifest targets importing an SDK pack, the manifest targets can perform the switch automatically. Projects that haven't opted into the updated platform switch to the legacy `ios-14` workload and continue to get the exact same workload packs, while projects that opt in get the updated `ios` workload and updated workload packs.

The *versioned workloads* may need to reference older versions of some or all of the workload packs. This is done with a similar pattern to workload versioning, using *versioned packs* that mirror the *unversioned packs*.

For example, if the updated `ios` workload used the unversioned pack `iOS.Sdk`, which had been updated to version `15.0.1`, this would map to the NuGet package `iOS.SDK 15.0.1`. If the `iOS.Sdk-14` workload needed the NuGet package version `iOS.SDK 14.1.3`, it could define a pack ID `iOS.Sdk.14` that mapped to version `14.1.3` and was aliased to `iOS.Sdk`.

The versioned pack `iOS.Sdk.14` is synthesized - it doesn't correspond to a real NuGet ID but exists purely as a means to allow a versioned workload to reference an older version of a NuGet package.

The `WorkloadManifest.targets` would import the `iOS.Sdk` pack when the `<TargetPlatformVersion>` is `15.0`, and import the `iOS.Sdk.14` pack when the `<TargetPlatformVersion>` is `14.0`. If the pack was not present, workload restore would find and install the workload that could provide it.

Here is an manifest snippet demonstrating the above example. It only uses a single pack, but concrete examples would have versioned copies of most of their packs and workloads.

```json5
"workloads": {
    // versioned workload
    "ios-build-14": {
        "kind": "build",
        "packs": [
            "iOS.Sdk.14"
        ]
    },
    // unversioned workload
    "ios-build": {
        "kind": "build",
        "packs": [
            "iOS.Sdk"
        ]
    },
    // this workload does not need an versioned version
    // as the legacy workload version is only needed
    // to avoid unexpected changes with existing projects
    "ios": {
        "description": "Create, build and run iOS apps",
        "packs": [
            "Xamarin.iOS.Templates",
        ],
        "extends": [ "ios-build" ]
    }
},
"packs": {
    // there do not need be versioned templates
    // as new projects should always target the new version
    // but if there is some reason to target the old version
    // then this can be done with a template parameter
    "Xamarin.iOS.Templates": {
        "version": "15.0.0",
        "kind": "template"
     },
    // unversioned pack
    "iOS.Sdk": {
        "version": "15.0.1",
        "kind": "framework"
     },
     // versioned pack
    "iOS.Sdk.14": {
        "version": "14.1.3",
        "kind": "framework",
        "alias-to": {
            "any": "iOS.Sdk"
        }
    }
}
```

The only purpose of these side-by-side versions is to make it so that servicing updates to an SDK band do not unexpectedly bring in API changes. They are not intended for long term use. **When a new SDK band is released, it is recommended that the versioned workloads and their packs are removed from the manifest**. However, platform implementors who have a strong need to ship legacy packs and workloads versions into new SDK bands may use this side-by-side pattern to do so.

As demonstrated in this example, template packs should not be side-by-side versioned within an SDK band. Templates should always target the newest framework, automatically opting newly created projects into the newer of the side-by-side versions of the workload. If there is a developer scenario where specifying the framework is important then it should be exposed as a parameter on the templates rather than a separate template.

## Expected SDK Resolution Behavior

The MSBuild logic in the `WorkloadManifest.targets` that imports SDKs packs based on the `TargetPlatformVersion` MSBuild property should use it as a **lower bound** on the pack OS API version, and implement the following behaviors:

**If multiple versions of the pack in the manifest comply with the version bound**, use the pack with the lowest version that complies with the platform API version bound. This means that when newer side by side packs become available in an SDK band, the project will not automatically use a newer version, but the MSBuild property can be used to opt in.

**If any version of the pack in the manifest is higher than the version bound**, emit a warning telling the developer that a newer OS API version is available, and explaining how to update the MSBuild property. This will guide developers to keep the value current.

**If no version of the pack in the manifest complies with the version bound**, emit an error telling the developer that their .NET SDK is too old.

For example, if the SDK contains 14.0 and 15.0 APIs:

- Project specifies 13.0 API: use 14.0 API, and emit a warning that newer API is being used
- Project specifies 14.0 API: use 14.0 API, and emit a warning that newer API is available
- Project specifies 15.0 API: use 15.0 API
- Project specifies 16.0 API: emit an error that the SDK must be updated

As this logic accounts for side-by-side versions it applies not just to platforms that follow the recommendation to remove versioned workloads in each new SDK band, but also platforms that ship side-by-side workload versions on an ongoing basis.

We will need to develop a standard pattern or helper logic for this to make sure it’s implemented consistently.

# Appendix: SDK Feature Releases for Platform Updates

An alternate option that was considered was to release a new SDK with a higher feature band when platform API updates are needed. For example, if a new version of iOS was released after 5.0.100, we could make a 5.0.200 SDK release with the updated platform API.

This has the advantage that it does not introduce any new mechanisms. However, it has several major disadvantages:

- Platform API updates require making an SDK band release, which could be expensive
- No compatibility checks and error experience when building project that uses newer APIs on SDK series that only has older APIs
- Requires centralized coordination
- Not scalable to nontrivial number of platforms and updates

