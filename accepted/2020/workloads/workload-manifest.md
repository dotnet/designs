# .NET SDK Workload Manifests

# Overview

.NET SDK workload manifests describe workloads that are available to be installed in the .NET SDK.  They provide version mappings that allow MSBuild targets and other SDK tools to resolve the packs that comprise these workloads from the SDK installation directory, and can be used to acquire, repair, and update the workloads and their components.

This document describes the format and versioning of the manifest. The installation and update experience will be described in other documents.

# Definitions
## SDK Band

.NET SDK versions take the form 3.1.100, 3.1.101, 3.1.200. The first two components of this version represent the major and minor version of the runtime in the SDK. The third component (the “hundreds”) is a more complex: the first digit represents the feature release of the SDK, and the last two digits represent the patch level of the SDK and runtime combined.

In the context of this document we will refer to an *SDK band* as the common portion of the version of all the SDK releases that differ only by patch level. For example, 3.1.100 and 3.1.105 are in the 3.1.100 band, while 3.2.100 is in the 3.2.100 band and 3.1.203 is in the 3.1.200 band.

## SDK Pack

An SDK pack is a unit of packaging for the assets that make up the SDK. Packs have an ID and version and are installed into folders in the SDK directory.

Packs will be installed using CLI tooling or by a native installer such as the Visual Studio installer. It is likely that packs will be distributed via NuGet at some point, so it is recommended that pack authors reserve NuGet IDs for their packs even if other distribution methods are used initially.

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

Manifests follows a similar pattern to SDK versioning: each SDK band has a corresponding manifest band. Each SDK uses only the manifest band corresponding to its own band. Manifest versions however are completely independent of SDK patch levels; an SDK should use the latest available versions of the manifests for its band.

To make this relationship more explicit, the SDK band for a manifest is encoded into the manifest’s ID e.g. `Microsoft.NET.Sdk.Android.SdkManifest-5.0.200`. The “version” of a manifest package can then be then simply a monotonic integer value.

## Pack Versioning

A manifest contains a list of pack IDs and their corresponding versions. A single version is defined for each pack: the most up-to-date version of that pack that is expected to be used with that SDK band.

This intentionally separates pack versions from component definitions. A pack may be included in multiple workloads, and this separation ensures that all workloads get a consistent version of each pack. There’s also a semantic separation: the workload definition list describes which packs are included in each workload, while the pack version list describes the set of pack versions that are supported together.

There are some cases where a single workload may want to contain side-by-side multiple versions of a single pack, or multiple components may want different versions of a single pack. This may apply, for example, to major/minor runtime versions, For these cases an aliasing mechanism is provided: a meaningful alias may be defined and mapped to a different version of a pack that is already included in the list. For example, `Foo-1.x` could be defined as an alias to version `1.0.5` of pack `Foo`, while `Foo` itself could be mapped to `2.1.4`. This is similar to how Linux libraries support side-by-side.

# Pack Kinds

The following kind of packs are permitted in manifests: targeting and runtime packs, SDK packs, library packs, template packs, and tools packs.

All packs are expected to be installed into the `dotnet` folder.

## Targeting and Runtime Packs

The [targeting and runtime packs](https://github.com/dotnet/designs/pull/50) introduced in .NET Core 3.0 are permitted in workloads. Targeting packs contain the framework assemblies that a project compiles against at build time, and runtime packs contain the framework and runtime binaries used to execute the project. They install into `dotnet/packs/{pack-id}/{pack-version}` and are resolved at build time using `FrameworkReference` MSBuild items.

Framework references are expected to be added to a project automatically by the manifest’s MSBuild targets based on the TFM. There will be a mechanism that allows the targets to omit the pack version from the framework reference. Targets may add multiple targeting or runtime pack references, for example when building a `net5.0-ios` project they could add a reference to a pack containing the `net5.0` reference assemblies and a pack containing the reference assemblies for the iOS platform bindings.

## SDK Packs

SDK packs are MSBuild SDK NuGet packages, but they are installed to `dotnet/packs/{pack-id}/{pack-version}`. They are expected to be automatically referenced by the targets.

## Library Packs

Library packs are normal NuGet packages. When a library pack is installed, the nupkg package file is placed in `dotnet/library-packs/` but are *not* extracted. This location is used as a directory feed by NuGet.

If ones of these packages gets used, it will be extracted into the global packages folder. If the SDK is updated and the library pack is removed or replaced with a newer version, projects that have already been created and restored will continue to be able to use the extracted copy.

This is intended to be used to pre-download NuGet packages referenced by templates so that a workload can work offline after installation. It should be used sparingly i.e. only for NuGets referenced by the core templates with default or popular options.

## Template Packs

Template packs are NuGet packages to be used by the dotnet templating engine. When a template pack is installed installed, the nupkg package file is placed in `dotnet/template-packs/` but is *not* extracted. Visual Studio and `dotnet new` are expected to automatically detect when templates nupkgs are installed and uninstalled to this folder and update their template hives automatically.


## Tools Packs

Tools packs are .NET global tools packages. Tools packs are installed to `dotnet/tools-packs/{package-id}/{package-version}/` and can be invoked via `dotnet` e.g. invoking `dotnet foo`, the runtime will use global.json and the manifest to determine the appropriate version of `dotnet-foo` to run.

Tools packs are intended to be used for tools that are exposed part of the development experience. Tools that are only expected to be invoked by build targets should be distributed in an SDK pack.

# Manifest Files

## Composition

Multiple manifests may be present in a .NET SDK band. Each manifest has its own ID and they are versioned independently. Components and packs may not be duplicated in multiple manifests within a band, though manifests may reference components and packs in other manifests that are expected to be installed.

Manifests are not an arbitrary extensibility mechanism; they are intended to provide some flexibility in the distribution and composition of a coherent whole. For example, there could be a *dotnet* manifest used to update in-box shared runtimes, a *xamarin-ios* manifest used to describe and update the Xamarin.iOS optional components, and a *uwp* manifest used to describe and update the UWP optional components. These could all be produced from separate repositories using separate build and release processes.

At any given point in time, the latest available version of all the manifests for an SDK series must always be able to be combined into a single consistent whole (in memory, not persisted to disk).

Each manifest may have a `WorkloadManifest.targets` MSBuild file beside it that is unconditionally imported into all projects. This targets file contains conditioned imports to add automatic referenced to SDK packs based on project properties, and its usage and recommended patterns are described in more detail in the [workload resolvers spec](/workload-resolvers.md).

## Packaging

Manifests are packaged and distributed as NuGet packages. They will use the existing package type called `DotnetPlatform` so they cannot be accidentally used as a `PackageReference`. The manifest’s `sdk-manifest.json` and `WorkloadTargets.targets`  are in the root folder of the NuGet. The ID of the NuGet is `{manifest-id}-{sdk-band}` and the version of the NuGet is the manifest version.

As manifests are distributed as NuGets, it’s possible to use preview versions of workloads by subscribing to preview feeds.

## Installation

Manifests are installed to `dotnet/sdk/{sdk-band}-manifests/{manifest-id}/` in an unpacked form. These are called the *installed manifests*.

The dotnet SDK would be expected to include baseline versions of the installed manifests for all manifests that are known and supported. Although any components listed in this baseline manifest need to work with that SDK version, it does not need to be fully up-to-date: it exists primarily for listing workloads that are available to be installed, and for its ID to be known by the SDK when fetching updated versions from NuGet.

Any workload management operation (install, uninstall, repair update) must be a transactional operation that also updates the installed manifests to the latest available versions for that SDK band.

## Advertising

The .NET tooling will automatically and opportunistically download updated versions of all manifests for the current SDK band and unpack them to `~/.dotnet/sdk-advertising/{sdk-band}/{manifest-id}/`. These user-local updated copies of the manifest are known as *advertising manifests.* The advertising manifests are used when listing workloads that are available to be installed, and for the build tooling to produce warnings that newer versions of installed components are available*.* They are **not** used in pack resolution.

# Format

Manifests are json files. Comments are supported, both `//` and `/* */` styles.

The toplevel is a JSON object, containing the following keys:

| Key | Type | Value | Required |
|--|--|--|--|
| `version` | int | The version of the manifest. Must match the version of the NuGet package that contains the manifest. | Yes |
| `description` | string | Description of the content and/or purpose of the manifest. This is primarily for commenting and/or diagnostic purposes and is not expected to be surfaced in the UX. | No |
| `workloads` | object | Workload definitions keyed by workload ID. | No |
| `packs` | object | Pack definitions keyed by pack ID. | No |
| `data` | object | Allows manifests to include arbitrary data without risk of conflict. | No |

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

Note that `extends` is functionally a dependency system and a way to factor out common sets of packages from workloads. By analogy to package managers such as apt-get and NuGet, workloads are metapackages that only permit unversioned dependencies and packs are packages that are only installable transitively.

## Pack Definitions

Pack definitions take the following form:

| Key | Type | Value | Required |
|--|--|--|--|
| `kind` | string | Type of the pack. Valid values are `sdk`, `framework`, `library`, `template`, `tool`. | Yes |
| `version` | string | Version of the pack. | Yes |
| `alias-to` | object | Alias this definition to the provided package ID | No |

The `alias-to` key allows aliasing this pack definition to a different underlying pack ID on each host platform. The keys are host platform RIDs, and the values are pack IDs. The `"*"` platform ID may be used to alias a pack on all platforms.

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

Another use of aliases is to install multiple version bands of the same pack by synthesizing new versioned pack IDs:

```json
"foo.framework": {
    "version": "2.0.4",
    "kind": "framework",
    "alias-to": {
        "*": "foo.framework",
    }
},
"foo.framework.1": {
    "version": "1.3.2",
    "kind": "framework",
    "alias-to": {
        "*": "foo.framework",
    }
},
```

A workload or framework reference that referenced `"foo.framework"` would get `"foo.framework"` version `2.0.4`, while a workload or framework reference that referenced `"foo.framework.1"` would get `"foo.framework"` version `1.3.2`,

## Example Platform SDK Manifest

Here is a *hypothetical* example manifest. It's not prescriptive but demonstrates concepts an patterns that can be used by platform implementations.

```json5
{
    "version": 5,
    "workloads": {
        // this is a dev workload that would typically be installed by a developer getting
        // started with this platform. it's composed of several smaller build workloads
        // plus a template pack for creating projects
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
            // will also resolve xamarin-android-build, but let's be explicit
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
        // this is a convenience for devs who want to pre-install everything
        //
        // in practice there would probably be a whole load more of these
        // and the runtime packs to support various device architectures
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

It is strongly recommended that updates *within* an SDK feature band only contain low-risk servicing updates, not new features. This means that developers can feel safe installing servicing updates. They know that it's not going to allow them to accidentally make the project depend on APIs that will break it for other members on their team who are not as up to date.

However, there is a major exception to this: OS releases do not coincide with .NET releases, so we need to be able to release platform API updates between .NET releases.

The solution is to ship two versions of the platform APIs, runtime, build logic, and tools, and have the build logic determine which to use [based on the platform API version](workload-resolvers.md#side-by-side-sdk-support-for-new-os-releases). This essentially makes these platform API updates opt-in on the project level: projects can express their dependency on the updated SDK or workload, and build system can then provide better errors when the SDK has not been updated appropriately.

> ***NOTE:*** Projects depending on any new SDK functionality (e.g. those that might appear in 3.1.200 series compared to 3.1.100 series) would have similar issues, and those dependencies can be handled (in a limited fashion) with global.json. However, platform updates are expected to be far more impactful than other changes that might be shipped in SDK updates, may happen between SDK series releases, and can be expressed in a more clear and comprehensible way using the OS platform versions, so we address them specifically.

## Side-by-Side Manifest Pattern

The pattern is to duplicate the build workloads and the packs they use, and add versioned suffixes to one of the copies of each, for example `ios-14` and `ios`. Note that duplicate packs may be created using aliases to synthesize packs that don't correspond to real NuGet IDs.

The version numbers of the *unversioned packs* are then updated, and the `WorkloadManifest.targets` is updated to use different SDK packs depending on the `TargetPlatformVersion` MSBuild property.

Here is an manifest snippet. It only uses a single pack, but concrete examples would have versioned copies of most of their packs and workloads.

```json5
"workloads": {
    "ios-build-14": {
        "kind": "build",
        "packs": [
            "Xamarin.iOS.Framework.14.0"
        ]
    },
    "ios-build": {
        "kind": "build",
        "packs": [
            "Xamarin.iOS.Framework"
        ]
    },
    "ios": {
        "description": "Create, build and run iOS apps",
        "packs": [
            "Xamarin.iOS.Framework",
            "Xamarin.iOS.Templates",
        ],
        "extends": [ "ios-build" ]
    }
},
"packs": {
    "Xamarin.iOS.Templates": {
        "version": "15.0.0",
        "kind": "template"
     },
    "Xamarin.iOS.Framework": {
        "version": "15.0.2",
        "kind": "framework"
     },
    "Xamarin.iOS.Framework.14.0": {
        "version": "14.2.3",
        "kind": "framework",
        "alias-to": {
            "*": "Xamarin.iOS.Framework"
        }
    }
}
```

The only purpose of these side-by-side versions is to make it so that servicing updates to an SDK series do not unexpectedly bring in API changes. They are not intended for long term use. **When a new SDK series is released, the versioned workloads and their packs should be removed from the manifest**.

As demonstrated in this example, template packs should not be side-by-side versioned within an SDK band. Templates should always target the newest framework, automatically opting newly created projects into the newer of the side-by-side versions of the workload. If there is a developer scenario where specifying the framework is important then it should be exposed as a parameter on the templates rather than a separate template.

## Expected SDK Resolution Behavior

The MSBuild logic in the `WorkloadManifest.targets` that imports SDKs packs based on the `TargetPlatformVersion` MSBuild property should use it as a **lower bound** on the pack OS API version, and implement the following behaviors:

**If multiple versions of the pack in the manifest comply with the version bound**, use the pack with the lowest version that complies with the platform API version bound. This means that when newer side by side packs become available in an SDK series, the project will not automatically use a newer version, but the MSBuild property can be used to opt in.

**If any version of the pack in the manifest is higher than the version bound**, emit a warning telling the developer that a newer OS API version is available, and explaining how to update the MSBuild property. This will guide developers to keep the value current.

**If no version of the pack in the manifest complies with the version bound**, emit an error telling the developer that their .NET SDK is too old.

For example, if the SDK contains 14.0 and 15.0 APIs:

- Project specifies 13.0 API: use 14.0 API, and emit a warning that newer API is being used
- Project specifies 14.0 API: use 14.0 API, and emit a warning that newer API is available
- Project specifies 15.0 API: use 15.0 API
- Project specifies 16.0 API: emit an error that the SDK must be updated

We will need to develop a standard pattern or helper logic for this to make sure it’s implemented consistently.

# Appendix: SDK Feature Releases for Platform Updates

An alternate option that was considered was to make SDK feature releases when platform API updates are needed. For example, if a new version of iOS was released after 5.0.100, we could make a 5.0.200 SDK release with the updated platform API.

This has the advantage that it does not introduce any new mechanisms. However, it has several major disadvantages:

- Platform API updates require making an SDK series release, which could be expensive
- No compatibility checks and error experience when building project that uses newer APIs on SDK series that only has older APIs
- Requires centralized coordination
- Not scalable to nontrivial number of platforms and updates

