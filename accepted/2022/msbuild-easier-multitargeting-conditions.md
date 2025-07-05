# Easier multi-targeting conditions in MSBuild

Multi-targeted projects are becoming more common in .NET, first with .NET Maui and now with (some) [Blazor projects in .NET 8](https://github.com/dotnet/designs/blob/main/accepted/2023/net8.0-browser-tfm.md).  Multi-targeted projects may need to have different properties, source files, package references, etc. for each targeted framework.

Today, it is pretty ugly to do this correctly.  Here is a snippet from the project you get when you create a new .NET Maui project:

```xml
<SupportedOSPlatformVersion Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'ios'">11.0</SupportedOSPlatformVersion>
<SupportedOSPlatformVersion Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'maccatalyst'">13.1</SupportedOSPlatformVersion>
<SupportedOSPlatformVersion Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'android'">21.0</SupportedOSPlatformVersion>
<SupportedOSPlatformVersion Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'windows'">10.0.17763.0</SupportedOSPlatformVersion>
<TargetPlatformMinVersion Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'windows'">10.0.17763.0</TargetPlatformMinVersion>
<SupportedOSPlatformVersion Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'tizen'">6.5</SupportedOSPlatformVersion>
```

We propose updating MSBuild to allow the following to work:


```xml
<SupportedOSPlatformVersion Condition="'$(TargetPlatformIdentifier)' == 'ios'">11.0</SupportedOSPlatformVersion>
<SupportedOSPlatformVersion Condition="'$(TargetPlatformIdentifier)' == 'maccatalyst'">13.1</SupportedOSPlatformVersion>
<SupportedOSPlatformVersion Condition="'$(TargetPlatformIdentifier)' == 'android'">21.0</SupportedOSPlatformVersion>
<SupportedOSPlatformVersion Condition="'$(TargetPlatformIdentifier)' == 'windows'">10.0.17763.0</SupportedOSPlatformVersion>
<TargetPlatformMinVersion Condition="'$(TargetPlatformIdentifier)' == 'windows'">10.0.17763.0</TargetPlatformMinVersion>
<SupportedOSPlatformVersion Condition="'$(TargetPlatformIdentifier)' == 'tizen'">6.5</SupportedOSPlatformVersion>
```

The new format is still not especially pretty, but is much easier for developers with basic MSBuild knowledge to understand, and to type correctly.

## Background

Projects set the target framework via the `TargetFramework` property.  Multi-targeted projects specify the list of target frameworks via the `TargetFrameworks` property, for example `<TargetFrameworks>net8.0-android;net8.0-ios;net8.0-maccatalyst</TargetFrameworks>`.  For multi-targeted projects, there is a separate "inner build" for each target framework in the list where the `TargetFramework` property is set.

It is possible to write multi-targeting conditions in terms of the `TargetFramework` property, for example `<SupportedOSPlatformVersion Condition="'$(TargetFramework)' == 'net8.0-ios'">11.0</SupportedOSPlatformVersion>`.  This is fairly simple to do, but when the project is retargeted (especially if done from the Visual Studio project properties UI) it is easy to forget to update the conditions.  This will "break" the conditions in the sense that they will never apply.

A better pattern is to use a condition based on an individual component of the target framework.  In the .NET SDK targets, the `TargetFramework` property gets broken down into four separate properties: `TargetFrameworkIdentifier`, `TargetFrameworkVersion`, `TargetPlatformIdentifier`, and `TargetPlatformVersion`.  So you could have a condition like `<SupportedOSPlatformVersion Condition="'$(TargetPlatformIdentifier)' == 'ios'">11.0</SupportedOSPlatformVersion>`.

However, that does not always work.  The reason for this is that the `TargetFramework` property gets broken down into component properties in the .NET SDK targets, which are evaluated after the body of the project where the condition would usually go.  So there's an ordering issue where the property you would want to use for the comparison isn't set yet when you want to read it.

To complicate matters, the target framework properties *are* set when evaluating conditions on items and `ItemGroup`s.  This is because MSBuild evaluation has multiple passes so all properties are evaluated before items, regardless of the relative evaluation position of the properties to the items.  This is complicated to explain and we don't want normal MSBuild users to have to know about this, so we try to avoid taking advantage of this.  Otherwise people would see conditions on items, apply the same pattern to properties, and not understand why it doesn't work.

So, the currently recommended solution is to use [built-in MSBuild functions](https://learn.microsoft.com/visualstudio/msbuild/property-functions?view=vs-2022#msbuild-targetframework-and-targetplatform-functions) to parse out the components of the TargetFramework property for comparison: `<SupportedOSPlatformVersion Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'ios'">11.0</SupportedOSPlatformVersion>`.  There's also an `IsTargetFrameworkCompatible` function which is useful in some cases.

## Proposed implementation

The new behavior will be a modification to MSBuild that will automatically parse the `TargetFramework` property into the component properties when needed.  It would work as follows:

- The .NET SDK would set the `MSBuildAutomaticallyParseTargetFramework` property to `True` in its `.props` files to enable the new behavior for Sdk-style projects.
- When reading the value of a property, if:
  - The `MSBuildAutomaticallyParseTargetFramework` property is set to `True`
  - The property to be read is one of `TargetFrameworkIdentifier`, `TargetFrameworkVersion`, `TargetPlatformIdentifier`, or `TargetPlatformVersion`
- Then, if the current property value is unset / empty
  - MSBuild would read the `TargetFramework` property, and if it was non-empty call the appropriate NuGet target framework parsing function to get the value.
  - It would not store the computed value of the property for future reads, simply return the computed value for the current property value read.
  - If the `TargetFramework` property was not set, then MSBuild will generate an evaluation error.  This is to help avoid situations where logic silently doesn't work (for example if you tried to set a property based on the `TargetFrameworkIdentifier` in `Directory.Build.props`, that would not work because the `TargetFramework` wouldn't be set).
- Otherwise, if the property to be read is set
  - MSBuild would still read the `TargetFramework` property and parse the appropriate value.  It would compare the parse result with the current value of the property.  If they do not match, it would generate an evaluation error
  - This is to ensure that the "magic" properties have a consistent value, whether that value comes from the new automatic parsing logic in MSBuild, the default parsing in the .NET SDK targets, or elsewhere.
  - This may cause errors when [TargetFramework](https://github.com/NuGet/Home/issues/5154) [aliasing](https://github.com/NuGet/Home/pull/12124) is used.  If TargetFramework aliasinng is being used and the resultant TargetFramework component properties are different than the result would be from a standard parse of the `TargetFramework`, then the aliasing logic should also turn off `MSBuildAutomaticallyParseTargetFramework`.
  - We may also need to special-case how the target platform is handled, as for any target framework that is not .NET 5 or higher, the target platform will default to Windows 7.0 (see the use of the `_EnableDefaultWindowsPlatform` property in the .NET SDK and MSBuild common targets).

This would mean that multi-targeted projects could use simple conditions based on the `TargetFrameworkIdentifier`, `TargetPlatformIdentifier`, etc. for both properties and items.