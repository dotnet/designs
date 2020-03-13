# .NET 5 OS API Versioning

# Introduction

The format for [.NET 5 target framework monikers (TFMs)](https://github.com/dotnet/designs/pull/92) includes a version component that represents a revision of the operating system (OS) binding APIs, for example `net5.0-ios15.0` would include a binding to the iOS 15.0 APIs. However, it is a common pattern for apps and libraries to support running on older versions of the OS by using runtime checks to guard their calls so that they do not call APIs that are not available. This applies today for the Xamarin platforms (iOS, Android, Mac etc) and for UWP.

This document proposes standard mechanisms and patterns for runtime API checks and compatibility that can be used across all .NET platforms to increase consistency of developer experience.

# OS Versions

There are three different OS version values that may be relevant for apps and libraries: the *OS API Version*, the *OS Target Version*, and the *OS Minimum Version*. It is important to distinguish and clarify these versions, as they have very different purposes.

## OS API Version

The OS API Version is part of the target framework, and affects which framework reference assemblies the app is compiled against. It does not affect which OS versions the app can run on (for well behaved platforms where backwards ABI compatibility is preserved). The primary reason for an app project to target an old OS API version is to allow the project to continue to build on older versions of the SDK that do not support the more recent API versions. A secondary reason may be to limit the API surface shown in IntelliSense so that it’s easier to avoid newer APIs, though this use case could perhaps be better supported in future by IDE experiences such allowing filtering IntelliSense by platform version.

However, for libraries that are distributed as NuGets, it is useful to be able to multi-target across multiple API versions. This allows NuGets to access features available in newer API versions while continuing to ship updates for consumption by apps and libraries that (for whatever reason) target older API versions.

By making the OS API version part of the target framework moniker (TFM), we take advantage of all the existing TFM-based mechanisms for multi-targeting builds against multiple versions of the API, selecting multi-targeted builds of a library from NuGets, and validating binary compatibility of references.

The OS API version has major, minor, servicing and binding revision components. This takes the following form:

```
{major}[.{minor}[.{servicing}]][-r{bindingRevision}]
```

 When a component is not specified, this is equivalent to it being zero. The major, minor and servicing components of the version should match the OS version. The binding revision may be incremented if the OS binding is updated independently of OS updates. For example, if `net5.0-ios15.0` is released with some missing bindings, it could be followed by a release `net5.0-ios15.0-r1` that adds them.

## OS Target Version

The OS Target Version is used on certain platforms to enable backwards compatibility behaviors so that old apps continue to run correctly when API behaviors change in newer OS versions. For example, when an app that targeted Android 9.0 was run on Android 10.0, the OS might “quirk” the API behavior to continue to match the 9.0 behavior, but apps that targeted 10.0 or newer would get the newer API behavior.

For now, handling of this version is left out of the scope of this document. Although it would be good to have a standard pattern in how this is defined across platforms, it’s not a universal concept, and doesn’t easily map into useful tooling. It’s also an app-level setting, and not useful for libraries except perhaps for warning of potential incompatibility, but that would have a very high false positive rate.

## OS Minimum Version

The OS Minimum Version is the minimum version of the OS that an app or library supports running on. If it is not specified, it should default to the OS API Version. If an app or library author specifies an OS Minimum Version, they are declaring that they are taking responsibility for ensuring that the app can run on that version and higher by using runtime checks to guard calls to potentially unavailable APIs. This allows apps and libraries to progressively “light up” features on newer OS versions while continuing to run downlevel.

We will make this concept first class and propagate the value throughout the build system and NuGet consistently, which will enable us to build analyzers and other experiences to help developers follow this pattern. This tooling will be detailed later in this document.

# API Annotations

To enable tooling to provide a good experience, the reference assemblies must be annotated with information about the availability of the APIs, as is current done for Xamarin Apple platforms.

The following attributes are proposed for the `System.Runtime.Versioning` namespace. They are based on the Xamarin attributes, but they do not have the `Deprecated` availability kind (it’s redundant), they rename the `Unavailable` availability kind to `Removed` (for consistency), and they do not have the ability to differentiate availability based on architecture (reduction of scope).

```csharp
[AttributeUsage(AttributeTargets.All, AllowMultiple=true)]
public abstract class PlatformAvailabilityAttribute
{
    internal PlatformAvailabilityAttribute(
    string platformIdentifier,
    int majorVersion, int minorVersion, int servicingVersion, int bindingRevision
    string message
    ) {}
    public AvailabilityKind AvailabilityKind { get; }
    public string Message { get; }
    public string PlatformIdentifier { get; }
    public Version OSVersion { get; }
    public int BindingRevision { get; }
}

public enum AvailabilityKind
{
    Introduced,
    Obsoleted,
    Removed
}
```

The platform string is expected to have the same values as used in the `TargetPlatformIdentifier` portion of platform-specific TFMs. Having it in the availability attributes may seem redundant as it will also be burned into assembly attributes for platform-specific reference assemblies. However, explicitly specifying the attribute is useful for platform-agnostic `net5.0` reference assemblies that have methods that are only available on some platforms e.g. due to having platform-specific implementations via bait-and-switch. This may also improve source readability when multi-targeting as it will remove the need to use `#if` around the annotations.

Constants for the platform identifiers could perhaps be exposed on a `PlatformIdentifier` type.

The values declared on this attribute are expected to be accessible by extracting constructor arguments from metadata. However, for readability it’s expected to be applied using subclasses with more specific names. To enforce this, it has an internal constructor and its subclasses are sealed.

The concrete subclasses are `IntroducedAttribute`, `ObsoletedAttribute` and `RemovedAttribute` subclasses, which take of the following form:

```csharp
public sealed class IntroducedAttribute : AvailabilityAttribute
{
    public IntroducedAttribute(
    string platform,
    int majorVersion,
    int minorVersion = 0,
    int servicingVersion = 0,
    int bindingRevision = 0,
    string message = null
    ) : base(platform, majorVersion, minorVersion, servicingVersion, bindingRevision, message) {}
}
```

These attributes are used as follows:

```csharp
[Introduced(PlatformIdentifier.macOS, 10, 2)]
[Deprecated(PlatformIdentifier.macOS, 10, 9)]
[Removed(PlatformIdentifier.macOS, 10, 12, 1)]
public class Foo : NSObject { ... }
```

The purpose of keeping ‘removed’ APIs and annotating them with `RemovedAttribute` is to allow apps and libraries to continue to use these APIs in fallback paths for older OS versions. This is a pattern that exists on Apple platforms.

If and when C# support target typing for enums, we could add convenience overloads that would improve readability:

```csharp
[Introduced (.macOS, 10, 1)]
[Obsoleted (.macOS, 10, 8)]
[Removed (.macOS, 10, 12)]
```

> NOTE: These attributes would only be needed on reference assemblies. They would not be needed at runtime. They could be removed by the linker, and we could also strip them from the implementation assemblies that we ship ourselves.

# Runtime Checks

> NOTE: See [this issue](https://github.com/dotnet/runtime/issues/33331) for a more active/current discussion of runtime check APIs.

The following method will be added to the `RuntimeInformation` class in the `System.Runtime.InteropServices` namespace:

```csharp
public static bool CheckOSVersion(
    int majorVersion, int minorVersion = 0, int servicingVersion = 0
);
```

It is used as follows:

```csharp
if (RuntimeInformation.CheckOSVersion(15)) {
    // use 15.0+ APIs
} else {
    // gracefully degrade
}
```

There will be an additional overload that checks the OS as well as the OS version, for use in portable libraries:

```csharp
public static bool RuntimeInformation.CheckOS(
    string platformIdentifier,
    int majorVersion, int minorVersion = 0, int servicingVersion = 0
);
```

As with the availability attributes, there could be an additional overload that used enums and target typing to improve readability.

> NOTE: The linker, JIT and/or AOT compiler could treat these method as intrinsics with constant values, which would enable it to eliminate unnecessary fallback code when consuming portable libraries or libraries with a lower OS minimum version than the app itself.

# MSBuild Properties

The `Microsoft.Net.Sdk` targets will extract the OS API Version component from the `TargetFramework` into the `TargetPlatformVersion` MSBuild property and burn it into an assembly attribute. Tools that use this value are expected to access it from the MSBuild property or from the assembly attribute.

Project files may specify a `MinimumPlatformVersion`, and if they do not it will default to the `TargetPlatformVersion` value. The `MinimumPlatformVersion` must not be higher than the `TargetPlatformVersion`, and this will be validated at the start of the build.

It is recommended that platforms define more specifically named versions of this property of the form `Minimum{PlatformIdentifier}Version`, for example `MinimumIOSVersion` or `MinimumWindowsVersion`, and that their targets assign this value to `MinimumOSVersion` if it is set. This will simplify use of the property multi-targeting.

> NOTE: would `{PlatformIdentifier}MinimumVersion` be better? It would sort/group more easily with other OS-specific properties.

> NOTE: Using the existing `TargetPlatformVersion` property to represent the API version may be confusing for platforms such as Android that allow having different values for the target version and API version..

# Assembly Level Minimum Version

We cannot provide useful errors or warnings at an assembly level when a project references a library with a higher minimum version, as it may guard usage of that library with runtime checks. 

However, it still useful to burn the minimum platform version into the assembly or NuGet package as a a baseline value for the classes and members that are not explicitly annotated. It can also be displayed as an information value in the user experience, for example in the NuGet package manager or on NuGet.org, although those are out of the scope of this document.

## Assembly Attribute

The minimum OS version will be burned into the assembly at build time using an assembly attribute. This attribute will be in the `System.Runtime.Versioning` namespace and will take the following form:

```csharp
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple=true)]
public sealed class OSMinimumVersion
{
    public OSMinimumVersion(
    string platformIdentifier,
    int majorVersion, int minorVersion, int servicingVersion
    ) {}
    public string PlatformIdentifier { get; }
    public Version Version { get; }
}
```

As with `PlatformAvailabilityAttribute` , the `PlatformIdentifier` property is included so that platform-agnostic assemblies can participate in platform minimum version annotations and checks.

The MSBuild step that generates and injects TFM assembly attributes will be updated to generate this attribute when the `MinimumPlatformVersion` and `TargetPlatformIdentifier` properties are set.

Platform-neutral assemblies, i.e. assemblies that target a platform-neutral TFM such as `net5`, will have to declare the attribute explicitly.

> NOTE: This seems a bit inconsistent. As a user, I’d expect `Minimum{OSName}Version` properties to work in platform-neutral assemblies too, but handling all those properties when building platform-neutral assemblies would be problematic as it would centralize the definitions of all of the platform-specific `MinimumVersion` properties into the `Microsoft.Net.Sdk` targets.
>
>Perhaps we could use items and metadata instead of platform-specific properties, e.g. `<PlatformInfo Include="ios" MinimumVersion="15.0" />`. For platform-specific projects, we could allow the simpler `MinimumPlatformVersion` property by mapping it into one of these items, e.g. `<PlatformInfo Include="$(TargetPlatformIdentifier)" Version="$(MinimumPlatformVersion)" Condition="'$(TargetPlatformIdentifier)' != ''" />`.

## NuGet Manifest

When generating a NuGet package using MSBuild, any platform minimum version constraints expressed in the project file will be listed in the `.nuspec`  manifest in a `<platformInfo>` child of the `<metadata>` element. This element and all of its children are optional. They expose the same information as the assembly annotation attributes, but avoid the need for consumers to read the assembly metadata. This could potentially be validated and/or automated by NuGet..

Within the `<platformInfo>` there are child elements corresponding to the TFM short names (used by NuGet as the folder name) of the reference assemblies in the NuGet. If the element represents a platform-specific TFM (e.g. `net5-ios14.0` or `net6-android9.0`) then it must have a `minimumVersion` attribute representing the minimum platform version for that platform:

```xml
<metadata>
    <platformInfo>
    <net5-ios15.0 minimumVersion="13.0" />
    <net5-mac10.15 minimumVersion="10.11" />
    </platformInfo>
</metadata>
```

A reference assembly for a platform-neutral TFM (e.g. `net5` or `net6`) may still need to express platform-specific minimum versions if the implementations are platform-specific (aka ‘bait and switch’). As before, the element matches the TFM of the reference assembly, but this time there is no `minimumVersion`. Instead there are sub-elements that provide `minimumVersion` values for platforms using the `TargetPlatformIdentifier` of those platforms to identify them:

```xml
<metadata>
    <platformInfo>
    <net6>
        <ios minimumVersion="13.0">
        <mac minimumVersion="11.0">
    </net6>
    </platformInfo>
</metadata>
```

Using these mechanisms, a NuGet package can declare minimum platform versions for any or all of its reference assemblies.

# Availability Inheritance

Members and nested types that do not have explicit availability annotations will inherit the values from their containing types. Types that do not have `Introduced` annotations will be treated as if they were introduced in the minimum platform version of the assembly itself.

# IDE Experience

Quick Info and IntelliSense tooltips will display the minimum platform version for any member with a minimum platform version higher than the current project’s minimum platform version.

We may also want to consider some kind of sorting, grouping or colorization based hints in the IntelliSense list.

# Analyzers

There will be a Roslyn analyzer that will squiggle calls to any method or property with a minimum platform version higher than the current project’s minimum platform version, unless either of the following is true:


- the call is guarded by a `CheckOS` or `CheckOSVersion` call against a version that is greater than or equal to the member’s minimum platform version
- the caller member has a minimum platform version that is equal to or higher than the callee

# See Also

- [Swift](https://docs.swift.org/swift-book/ReferenceManual/Attributes.html) `[@available](https://docs.swift.org/swift-book/ReferenceManual/Attributes.html)` [attribute](https://docs.swift.org/swift-book/ReferenceManual/Attributes.html)
- [Xamarin.iOS/Mac](https://docs.microsoft.com/en-us/dotnet/api/objcruntime.availabilitybaseattribute?view=xamarin-ios-sdk-12) `AvailabilityBaseAttribute` and its subclasses

