# .NET 5 Minimum OS Versioning

**Owner** [Mikayla Hutchinson](https://github.com/mhutch)

# Introduction

It is common for apps and libraries to support running on older versions of the OS than the version against which they were compiled. This pattern involves using runtime checks to guard against calling APIs that are not available, and is encouraged and widely used on the Xamarin platforms (iOS, Android, Mac etc).

This document proposes a set of standard mechanisms and patterns for OS version compatibility annotations and runtime API checks that can be used across all .NET platforms to increase consistency of developer experience.

# OS Versions

There are three different OS version values that may be relevant for apps and libraries: the *OS API Version*, the *OS Target Version*, and the *OS Minimum Version*. It is important to distinguish and clarify these versions, as they have very different purposes.

## OS API Version

The OS API Version is part of the target framework, e.g. `net5.0-ios15.0` has an OS API version of `15.0`. It affects which framework reference assemblies the app is compiled against. It does not affect which OS versions the app can run on (for well behaved platforms where backwards ABI compatibility is preserved), and hence app developers should target the latest OS API version by default. There are some exceptions: an app project may target an older OS API version to allow the project to continue to build on older versions of the SDK that do not support the more recent API versions, or a developer may wish to limit the API surface shown in IntelliSense so that it’s easier to avoid newer APIs. The latter case could perhaps be better supported in future by IDE experiences such filtering IntelliSense by platform version.

For libraries that are distributed as NuGet packages, it is useful to be able to multi-target across multiple API versions. This allows NuGet packages to access features available in newer API versions while continuing to ship updates for consumption by apps and libraries that (for whatever reason) target older API versions. By making the OS API version part of the target framework moniker (TFM), we take advantage of all the existing TFM-based mechanisms for multi-targeting builds against multiple versions of the API, selecting multi-targeted builds of a library from NuGet packages, and validating binary compatibility of references.

The OS API Version is expected to have a clear and obvious mapping to an OS version. For example, the API binding for iOS 15.0 would have an API version of 15.0. However, small increases in the last two components of the API version are permitted for revisions to the bindings, for example API version 15.0.1 might be a revised binding for iOS 15.0.

Per the [.NET 5 TFM spec](https://github.com/dotnet/designs/blob/main/accepted/2020/net5/net5.md), the MSBuild targets will extract this value from the TFM and assign it to the `TargetPlatformVersion` property.

## OS Target Version

The OS Target Version is used on certain platforms to enable backwards compatibility behaviors so that old apps continue to run correctly when API behaviors change in newer OS versions. For example, when an app that targeted Android 9.0 was run on Android 10.0, the OS might “quirk” the API behavior to continue to match the 9.0 behavior, but apps that targeted 10.0 or newer would get the newer API behavior.

Handling of the OS target Version is out of the scope of this document. Although it would be good to have a standard pattern in how this is defined across platforms, it’s not a universal concept, and doesn’t easily map into useful tooling. It’s also an app-level setting, and not useful for libraries except perhaps for warning of potential incompatibility, but that would have a very high false positive rate.

> NOTE: Using the existing `TargetPlatformVersion` property to represent the API version may be confusing for platforms such as Android that allow having different values for the target version and API version.

## OS Minimum Version

The OS Minimum Version is the minimum version of the OS that an app or library supports running on. If it is not specified, it should default to the version equivalent to the OS API Version. If an app or library author specifies an OS Minimum Version, they are declaring that they are taking responsibility for ensuring that the app can run on that version and higher by using runtime checks to guard calls to potentially unavailable APIs. This allows apps and libraries to progressively “light up” features on newer OS versions while continuing to run downlevel.

We will make this concept first class and propagate the value throughout the build system and NuGet consistently, which will enable us to build analyzers and other experiences to help developers follow this pattern. This tooling will be detailed later in this document.

# API Annotations

To enable tooling to provide a good experience, assemblies should be annotated with information about the availability of APIs.

Availability annotations are intended to be used both in third-party assemblies and in assemblies that are part of .NET itself. The most straightforward usage is in binding assemblies, such as the iOS and Android platform bindings, where they directly reflect the availability of the underlying native API. However, they may be used by higher level APIs in platform-specific assemblies (e.g. `net5.0-ios15`) or platform-neutral assemblies (e.g. `net5.0`) to indicate that a method or class implementation depends on APIs from a particular OS version or is unavailable on certain OSes.

Earlier versions of this section proposed a form of these annotation attributes based on the Xamarin.iOS/Xamarin.Mac attributes, but it has been superseded by a further refined and generalized form defined in the [attributes section of the Platform Checks spec](../platform-checks/platform-checks.md#attributes).

These availability attributes omit the `Deprecated` availability kind from the Xamarin attributes as it’s redundant, and rename `Introduced` to `Supported` to better reflect their alternate purpose of annotating multi-platform assemblies. They also omit the ability to differentiate availability based on architecture for reduction of scope, though this could be added if needed in future.

> ***NOTE*** If and when C# support target typing for enums, we could add convenience overloads that would improve readability:
>
>```csharp
>[Introduced (.macOS, 10, 1)]
>[Obsoleted (.macOS, 10, 8)]
>[Removed (.macOS, 10, 12)]
>```

> ***NOTE***: These annotations are only needed on reference assemblies, as they are not used at runtime. They should be omitted or stripped from the implementation assemblies that are shipped as part of .NET, and it is recommended that third -party assemblies do so too. If any remain in a compiled app, they should be removed by the linker.

# Runtime Checks

The runtime avalaibility check APIs originally proposed in this section have been superseded by the APIs defined in the [platform guards section of the Platform Checks spec](../platform-checks/platform-checks.md#platform-guards).

> ***NOTE***: The linker, JIT and/or AOT compiler could treat these method as intrinsics with constant values, which would enable it to eliminate unnecessary fallback code when consuming portable libraries or libraries with a lower OS minimum version than the app itself.

# MSBuild Properties

The `Microsoft.Net.Sdk` targets will extract the OS API Version component from the `TargetFramework` into the `TargetPlatformVersion` MSBuild property and burn it into an assembly attribute. Tools that use this value are expected to access it from the MSBuild property or from the assembly attribute.

Project files may specify a `SupportedOSPlatformVersion` property, and if they do not it will default to OS Version equivalent to the OS API version specified in the `TargetPlatformVersion` value. The `SupportedOSPlatformVersion` must not be higher than the `TargetPlatformVersion`, and this will be validated at the start of the build.

> ***NOTE***: In an earlier version of this spec the MSBuild property was called `MinimumOSVersion`. It was changed to `SupportedOSPlatformVersion` to make its correspondance to the updated availability attributes clearer.

The SDK targets uses the `TargetPlatformVersion` property to provide a default `SupportedOSPlatformVersion` value if none is explicitly provided. If this 1:1 mapping is not valid (for example a bindings revision that is independent of the OS version) then the default must be overridden by platform tools authors.

It is recommended that platforms define more specifically named versions of this property of the form `{PlatformIdentifier}MinimumVersion`, for example `IOSMinimumVersion` or `WindowsMinimumVersion`, and that their targets assign this value to `SupportedOSPlatformVersion` if it is set. This will simplify use of the property when multi-targeting.

```xml
<PropertyGroup Condition="'$(TargetFrameworkIdentifier)'=='android'">
  <SupportedOSPlatformVersion Condition="'$(SupportedOSPlatformVersion)'==''">$(AndroidMinimumVersion)</SupportedOSPlatformVersion>
</PropertyGroup>
```

For these per-platform properties work work in platform-neutral assemblies, the workload targets should also map them to `SupportedOSPlatformVersion` items.

```xml
<ItemGroup Condition="'$(AndroidMinimumVersion)'!=''">
  <SupportedOSPlatformVersion Include="android" Version="$(AndroidMinimumVersion)" />
</ItemGroup>
```

The SDK targets will use these items to generate [assembly attributes](#assembly-attribute).

> ***NOTE***: `{PlatformIdentifier}MinimumVersion` was chosen over `Minimum{PlatformIdentifier}Version` as it it's more easily searchable and sorts/group more easily with other OS-specific properties.

> ***NOTE***: UWP projects already have a `TargetPlatformMinVersion` property. However, the TFM spec uses `TargetPlatformVersion` to represent the *API* version, which is a different *kind* of version than the OS version, so here we instead use `OSMinimumVersion` to indicate the distinction and for alignment with the `OSTargetVersion` on platforms where that is relevant.

# Assembly Level Minimum Version

We cannot provide useful errors or warnings at an assembly level when a project references a library with a higher minimum version, as it may guard usage of that library with runtime checks.

However, it still useful to burn the minimum platform version into the assembly or NuGet package as a a baseline value for the classes and members that are not explicitly annotated. This is done with by applying the `SupportedOSPlatformAttribute` at assembly level.

Ite may be useful to display the assembly level minimum version as an information value, for example in the NuGet package manager or on NuGet.org, although those are out of the scope of this document.

## Assembly Attribute Generation

The MSBuild step that generates and injects TFM assembly attributes will be updated to generate this attribute when both the `SupportedOSPlatformVersion` and `TargetPlatformIdentifier` properties are set.

It will also generate this attribute from the `SupportedOSPlatformVersion` items defined in the [MSBuild properties](#msbuild-properties) section. This will allow developers to use platform minimum version properties such as `AndroidMinimumVersion` and `IOSMinimumVersion` in platform-neutral assemblies (i.e. assemblies that target a platform-neutral TFM such as `net5.0`).

Like the API annotation attributes, the `OSMinimumVersionAttribute` attribute is not needed in implementation assemblies as it is not used at runtime.

## NuGet Manifest

When generating a NuGet package using MSBuild, any platform minimum version constraints expressed in the project file will be listed in the `.nuspec` manifest in a `<platformInfo>` child of the `<metadata>` element. This element and all of its children are optional. They expose the same information as the assembly annotation attributes, but avoid the need for consumers to read the assembly metadata. This could potentially be validated and/or automated by NuGet.

Within the `<platformInfo>` element, there are child elements corresponding to the TFM short names (used by NuGet as the folder name) of the reference assemblies in the NuGet. If the element represents a platform-specific TFM (e.g. `net5.0-ios14.0` or `net6.0-android9.0`) then it must have a `minimumVersion` attribute representing the minimum platform version for that platform:

```xml
<metadata>
  <platformInfo>
    <referenceAssembly target="net5.0-ios15" osMinimumVersion="13.0" />
    <referenceAssembly target="net5.0-mac10.15" osMinimumVersion="10.11" />
  </platformInfo>
</metadata>
```

A reference assembly for a platform-neutral TFM (e.g. `net5.0` or `net6.0`) may still need to express platform-specific minimum versions if the implementations are platform-specific (aka ‘bait and switch’). As before, the element matches the TFM of the reference assembly, but this time there is no `minimumVersion`. Instead there are sub-elements that provide `minimumVersion` values for platforms using the `TargetPlatformIdentifier` of those platforms to identify them:

```xml
<metadata>
  <platformInfo>
    <referenceAssembly target="net6.0">
      <platform id="ios" osMinimumVersion="13.0">
      <platform id="mac" osMinimumVersion="11.0">
    </net6.0>
  </platformInfo>
</metadata>
```

Using these mechanisms, a NuGet package can declare minimum platform versions for any or all of its reference assemblies.

# IDE Experience

Quick Info and IntelliSense tooltips will display the minimum platform version for any member with a minimum platform version higher than the current project’s minimum platform version.

We may also want to consider some kind of sorting, grouping or colorization based hints in the IntelliSense list.

# Analyzers

The availability analyzers described in the [Platform Checks spec](../platform-checks/platform-checks.md#analyzers) will detect unguarded calls to APIs with a minimum platform version higher than the current project’s minimum platform version.

# See Also
- [Swift](https://docs.swift.org/swift-book/ReferenceManual/Attributes.html) `[@available](https://docs.swift.org/swift-book/ReferenceManual/Attributes.html)` [attribute](https://docs.swift.org/swift-book/ReferenceManual/Attributes.html)
- [Xamarin.iOS/Mac](https://docs.microsoft.com/en-us/dotnet/api/objcruntime.availabilitybaseattribute?view=xamarin-ios-sdk-12) `AvailabilityBaseAttribute` and its subclasses
