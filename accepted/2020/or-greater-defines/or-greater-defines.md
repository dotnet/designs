# OR_GREATER preprocessor symbols for TFMs

**PM** [Immo Landwerth](https://github.com/terrajobst)

Today, projects automatically define `#if` symbols based on the friendly TFM
representation. This includes both versionless- as well as version-specific
symbols. For .NET 6, we plan to add `XXX_OR_GREATER` variants that are
accumulative in nature:

TFM                 | Existing | Proposed Additions
--------------------|----------|--------------------------------------------
`net5.0`            | `NET5_0` | `NET5_0_OR_GREATER`
`net6.0`            | `NET6_0` | `NET6_0_OR_GREATER`, `NET5_0_OR_GREATER`

The goal is to align the semantics between package folder names and `#if`
without breaking existing code.

The new symbols will make it easier to write code that doesn't need to be
updated each time the project's `TargetFramework`/`TargetFrameworks` property is
changed. For example, let's say you want a new code path that is triggered for
.NET 6.0 and greater. This could either be a new API you want to call or relying
on a new behavior. Today, you need to write code like that but you need to
consider what version TFMs your project is targeting. Let's say you target .NET
Framework 4.6.1, .NET Core 3.1, and .NET 5.

Today, you'd need to write code like this:

```C#
void M()
{
#if NET461
    LegacyNetFxBehavior();
#elif NETCOREAPP3_1
    LegacyNetCoreBehavior();
#elif NET
    Net5OrGreaterBehavior();
#else
    #error Unhandled TFM
#endif
}
```

Please note that order is important here. You first handle all the legacy TFMs
and then you handle the new behavior. To avoid breaking that code once you add
target .NET 6, you use the versionless TFM for .NET 5 and greater platforms
(`NET`). However, the code still needs to be revised when you add lower versions
of existing TFMs.

With this feature, you'll be able to express it like this, which is a lot more
intuitive:

```C#
void M()
{
#if NET5_0_OR_GREATER
    Net5OrGreaterBehavior();
#elif NETCOREAPP3_1_OR_GREATER
    LegacyNetCoreBehavior();
#elif NET461_OR_GREATER
    LegacyNetFxBehavior();
#else
    #error Unhandled TFM
#endif
}
```

The idea is to start from newest to oldest.

This also makes the code more resilient for cases where you add new frameworks
with lower versions later. For example, if you start targeting .NET Core 3.0,
the former code would go down the path for .NET 6 while the latter will fail
with "Unhandled TFM", which is a lot more logical for the person having to
maintain the code.

## Scenarios and User Experience

### Multi-targeting for devices to provide a cross-platform API

Joletta is building a cross-platform library for geolocation. Since there is no
built-in API for accessing geolocation, she needs to call different APIs for
different platforms, so she uses `#if` to write different code, depending on the
OS she is building for, plus a .NET Standard implementation that just throws
`PlatformNotSupportedException`.

Since she needs to provide custom implementations per OS, she wants to fail
whenever a new TFM is added:

```C#
public static class Gps
{
    public static GpsCoordinate GetCoordinates()
    {
#if ANDROID
        return CallAndroidApi();
#elif IOS
        return CallIOSApi();
#elif WINDOWS
        return CallWindowsApi();
#elif NETSTANDARD
        throw new PlatformNotSupportedException();
#else
        #error Provide implementation
#endif
    }
}
```

### When running on a later version of .NET, use a newer API

Zachariah is building a .NET imaging library. In .NET 5, we have added enough
hardware intrinsics so that he can implement the blur functionality in a much
faster way. However, he still wants to support .NET Standard 2.0, so he decides
to use `#if` to multi-target for .NET 5.

Since he checks for the minimum version of the framework that introduced the
API, adding a new version wouldn't change this code, so he simply puts the
fallback logic for the slow version in the `#else` branch, with no call to
`#error`.

```C#
public class Picture
{
    public void Blur(float radius)
    {
#if NET5_0_OR_GREATER
        FastBlurUsingHardwareIntrinsics();
#else
        SlowBlurUsingSimpleMath();
#endif
    }
}
```

## Requirements

### Goals

* The new symbols don't change semantics of existing code
* The new symbols allow expressing a `>=` relationship with version numbers
* The new symbols apply to both frameworks as well as platforms
* The new symbols are defined for all frameworks that are relevant for package
  authors today, which includes:
    - .NET 5
    - .NET Core
    - .NET Framework
    - .NET Standard

### Non-Goals

* Making existing code more resilient to TFM changes

## Design

The SDK will define `XXX_OR_GREATER` variants for the following TFMs:

* **.NET Framework**
    - Applies to `net1.0`-`net4x` and the `NETFRAMEWORK`/`NET` symbols
    - For example, `net4.8` will define `NETFRAMEWORK`, `NET48`,
      `NET48_OR_GREATER`, `NET472_OR_GREATER`, ... `NET20_OR_GREATER`.
* .NET Core
    - Applies to `netcoreappX.Y` and the `NETCOREAPP` symbol
    - For example, `netcoreapp3.1` will define `NETCOREAPP`, `NETCOREAPP3_1`,
      `NETCOREAPP3_1_OR_GREATER`, .., `NETCOREAPP1_1_OR_GREATER`.
* .NET 5 and later
    - Applies to `netX.Y` and the `NET` symbol
    - For example, `net6.0` will define `NET`, `NET6_0`, `NET6_0_OR_GREATER` and
      `NET5_0_OR_GREATER`.
    - This will also include the corresponding defines a .NET Core 3.1 successor
      would have gotten. For example, `net5.0` will also define `NETCOREAPP`,
      `NETCOREAPP3_1_OR_GREATER`, .., `NETCOREAPP1_0_OR_GREATER`.
    - This will *neither* define a `NETCOREAPP5_0` nor
      `NETCOREAPP5_0_OR_GREATER`.
* .NET 5 and later with operating systems
    - The OS flavors will also gain the `XXX_OR_GREATER` variants
    - For example, `net5.0-windows10.0.19222.0` will also define `WINDOWS`,
      `WINDOWS10_0_19041_0`, `WINDOWS10_0_19041_0_OR_GREATER`, etc.
* Xamarin
    - This covers the existing Xamarin offerings, the new .NET 6-based iOS and
      Android support is handled by the previous sections.
    - The existing Xamarin platforms don't have versioned preprocessor symbols
    - Thus, we won't be adding those

### Doc changes

The [documentation table][docs-table] in the [#if (C# reference)][docs] will
need to change as follows:

> | Target Frameworks | Symbols |
> | ------------------| ------- |
> | .NET Framework    | `NETFRAMEWORK`, `NET48`, `NET472`, `NET471`, `NET47`, `NET462`, `NET461`, `NET46`, `NET452`, `NET451`, `NET45`, `NET40`, `NET35`, `NET20`, `NET48_OR_GREATER`, `NET472_OR_GREATER`, `NET471_OR_GREATER`, `NET47_OR_GREATER`, `NET462_OR_GREATER`, `NET461_OR_GREATER`, `NET46_OR_GREATER`, `NET452_OR_GREATER`, `NET451_OR_GREATER`, `NET45_OR_GREATER`, `NET40_OR_GREATER`, `NET35_OR_GREATER`, `NET20_OR_GREATER` |
> | .NET Standard     | `NETSTANDARD`, `NETSTANDARD2_1`, `NETSTANDARD2_0`, `NETSTANDARD1_6`, `NETSTANDARD1_5`, `NETSTANDARD1_4`, `NETSTANDARD1_3`, `NETSTANDARD1_2`, `NETSTANDARD1_1`, `NETSTANDARD1_0`, `NETSTANDARD2_1_OR_GREATER`, `NETSTANDARD2_0_OR_GREATER`, `NETSTANDARD1_6_OR_GREATER`, `NETSTANDARD1_5_OR_GREATER`, `NETSTANDARD1_4_OR_GREATER`, `NETSTANDARD1_3_OR_GREATER`, `NETSTANDARD1_2_OR_GREATER`, `NETSTANDARD1_1_OR_GREATER`, `NETSTANDARD1_0_OR_GREATER` |
> | .NET 6 (and .NET Core) | `NET5_0`, `NETCOREAPP`, `NETCOREAPP3_1`, `NETCOREAPP3_0`, `NETCOREAPP2_2`, `NETCOREAPP2_1`, `NETCOREAPP2_0`, `NETCOREAPP1_1`, `NETCOREAPP1_0`, `NET6_0_OR_GREATER`, `NET5_0_OR_GREATER`, `NETCOREAPP3_1_OR_GREATER`, `NETCOREAPP3_0_OR_GREATER`, `NETCOREAPP2_2_OR_GREATER`, `NETCOREAPP2_1_OR_GREATER`, `NETCOREAPP2_0_OR_GREATER`, `NETCOREAPP1_1_OR_GREATER`, `NETCOREAPP1_0_OR_GREATER` |
>
> **Notes**:
>
> * Versionless symbols are defined regardless of the version you're targeting.
> * Version-specific symbols are only defined for the version you're targeting.
> * The `XXX_OR_GREATER` symbols are defined for the version you're targeting
>   and all earlier versions.

[docs-table]: https://github.com/dotnet/docs/blob/master/includes/preprocessor-symbols.md
[docs]: https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/preprocessor-directives/preprocessor-if#remarks

## Q & A

### Why don't we just define the version-specific defines for all old versions too?

We have tried [that design][net5-preprocessor] in the .NET 5 timeframe and
[ended up backing it out][net5-issue] because it was considered too surprising.

[net5-preprocessor]: https://github.com/dotnet/designs/blob/main/accepted/2020/net5/net5.md#preprocessor-symbols
[net5-issue]: https://github.com/dotnet/docs/issues/20692

The basic problem was that starting with .NET 5, version-specific defines would
have behaved differently from the past. That is, when you target `net5.0`, both
`NETCOREAPP3_1` and `NET5_0` would be defined, and when targeting `net6.0`,
`NETCOREAPP3_1`, and `NET5_0` would be defined as well.

Due to a lot of confusion and concern we ended up not shipping this design.
