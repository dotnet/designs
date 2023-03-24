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

It is possible to write multi-targeting conditions in terms of the `TargetFramewrk` property, for example `<SupportedOSPlatformVersion Condition="'$(TargetFramework)' == 'net8.0-ios'">11.0</SupportedOSPlatformVersion>`.  This is fairly simple to do, but when the project is retargeted (especially if done from the Visual Studio project properties UI) it is easy to forget to update the conditions.  This will "break" the conditions in the sense that they will never apply.

A better pattern is to use a condition based on an individual component of the target framework.  In the .NET SDK targets, the `TargetFramework` property gets broken down into four separate properties: `TargetFrameworkIdentifier`, `TargetFrameworkVersion`, `TargetPlatformIdentifier`, and `TargetPlatformVersion`.  So you could have a condition like `<SupportedOSPlatformVersion Condition="'$(TargetPlatformIdentifier)' == 'ios'">11.0</SupportedOSPlatformVersion>`.

However, that does not always work.  The reason for this is that the `TargetFramework` property gets broken down into component properties in the .NET SDK targets, which are evaluated after the body of the project where the condition would usually go.  So there's an ordering issue where the property you would want to use for the comparison isn't set yet when you want to read it.

To complicate matters, the target framework properties *are* set when evaluating conditions on items and `ItemGroup`s.  This is because MSBuild evaluation has multiple passes so all properties are evaluated before items, regardless of the relative evaluation position of the properties to the items.  This is complicated to explain we don't want normal MSBuild users to have to know about this, so we try to avoid taking advantage of this.  Otherwise people would see conditions on items, apply the same pattern to properties, and not understand why it doesn't work.

So, the currently recommended solution is to use [built-in MSBuild functions](https://learn.microsoft.com/en-us/visualstudio/msbuild/property-functions?view=vs-2022#msbuild-targetframework-and-targetplatform-functions) to parse out the components of the TargetFramework property for comparison: `<SupportedOSPlatformVersion Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'ios'">11.0</SupportedOSPlatformVersion>`.  There's also an `IsTargetFrameworkCompatible` function which is useful in some cases.

## Proposed implementation

The new behavior will be a modification to MSBuild that will automatically parse the `TargetFramework` property into the component properties when needed.  It would work as follows:

- When reading the value of a property, if:
  - The property is one of `TargetFrameworkIdentifier`, `TargetFrameworkVersion`, `TargetPlatformIdentifier`, or `TargetPlatformVersion`
  - The current property value is unset / empty
  - The `MSBuildAutomaticallyParseTargetFramework` property is set to `True`
- Then MSBuild would read the `TargetFramework` property, and if it was non-empty call the appropriate NuGet target framework parsing function to get the value.  It would not store the computed value of the property for future reads, simply return the computed value for the current property value read.
- The .NET SDK would set the `MSBuildAutomaticallyParseTargetFramework` property to `True` in its `.props` files to enable the new behavior for Sdk-style projects.

This would mean that multi-targeted projects could use simple conditions based on the `TargetFrameworkIdentifier`, `TargetPlatformIdentifier`, etc. for both properties and items.