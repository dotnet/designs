# .NET 6.0 Target Frameworks

**PM** [Immo Landwerth](https://github.com/terrajobst)

In [.NET 5.0][net5.0] we defined a new syntax for target frameworks (TFM) that
makes it unnecessary for both tools and humans to use a decoder ring, such as
the .NET Standard version table, to figure out what is compatible with what. In
this document, we're listing which TFMs we're adding in .NET 6.0.

## Scenarios and User Experience

### Using an existing library in Mac Catalyst

Sophia is building an application for Mac Catalyst. From her experience with
Xamarin iOS she knows the library `Xamarin.FFImageLoading` and wants to use it.
The package installs successfully, but upon building she gets the following
warning:

> Package 'Xamarin.FFImageLoading' was restored using 'xamarin.ios' instead of
> the project target framework 'net6.0-maccatalyst'. This package may not be
> fully compatible with your project.

To find out more, she visits the package's project site, which is on GitHub. She
decides to file an issue to ask whether the library will add support for Mac
Catalyst, but for the time being she's unblocked and can continue to develop her
app.

## Requirements

### Goals

* Support the definition of frameworks relevant for Xamarin development
* Mac Catalyst is able to consume libraries built for Xamarin iOS, even though
  the binary might not work. The developer should get a warning, but the tools
  shouldn't block it.

### Non-Goals

* Re-design the TFM syntax

## Stakeholders and Reviewers

* MSBuild / SDK team
* NuGet team
* Project system team
* Xamarin team

## Design

| TFM                | Compatible With                          |
|--------------------|------------------------------------------|
| net6.0             | (subsequent version of net5.0)           |
| net6.0-windows     | (subsequent version of net5.0-windows)   |
| net6.0-android     | xamarin.android                          |
|                    | (+everything else inherited from net6.0) |
| net6.0-ios         | xamarin.ios                              |
|                    | (+everything else inherited from net6.0) |
| net6.0-macos       | xamarin.mac                              |
|                    | (+everything else inherited from net6.0) |
| net6.0-maccatalyst | xamarin.ios                              |
|                    | (+everything else inherited from net6.0) |
| net6.0-tvos        | xamarin.tvos                             |
|                    | (+everything else inherited from net6.0) |

### Compatibility rules

The [built-in compatibility rules][net5.0] of the `netX.Y-platform` TFMs make a
TFM automatically compatible with:

1. Itself
2. Earlier versions of itself
3. The corresponding `netX.Y` TFM
4. Earlier versions of the corresponding `netX.Y` TFM

The `net5.0` TFM has the additional twist that it's compatible with
`netcoreapp3.1` (and earlier), `netstandard2.1` (and earlier) as well as .NET
Framework (`net4x` and earlier, but under a warning). The .NET Framework
compatibility is achieved as a *fallback*, that is, it's only used when nothing
else applies.

We don't want to model the Xamarin TFMs as a fallback but instead want them to
apply **before** `netcoreapp` and `netstandard`. Usually the existence of the
Xamarin TFM implementation is to bait a `netstandard` API and switch the
platform specific implementation. Most of the Plugin type packages use this
pattern and in fact the `netstandard` "implementation" is more like a reference
assembly with just stubs that throw at runtime. `Xamarin.Essentials` is a good
example of this. The same rational can be applied to `netcoreapp` where this
would logically be the server implementation (or maybe a .NET Core 3.x desktop
implementation).

A `net6.0-maccatalyst` project referencing a NuGet package should behave as
follows:

* It should prefer `xamarin.ios` assets over `netcoreapp` and `netstandard`
  assets.
    - However, it should still prefer `net5.0`/`net6.0` assets over
      `xamarin.ios`.
    - It also shouldn't be compatible with any other existing Xamarin TFM (such
      as `xamarin.mac`)
* Generate NuGet warning [NU1701] when a `xamarin.ios` asset is being used
    - Package 'packageId' was restored using 'xamarin.ios' instead the project
      target framework 'net6.0-maccatalyst'. This package may not be fully
      compatible with your project.
* Should only use compatible `net5.0` based TFMs, namely `net5.0`, `net6.0`, and
  `net6.0-maccatalyst`.
    - Specifically, it should not accept `net6.0-ios`. The expectation is that
      moving forward libraries that want to work on iOS and Mac Catalyst should
      use `net6.0` or multi-target for `net6.0-ios` and `net6.0-maccatalyst`

> ***OPEN QUESTION** Should `xamarin.ios` take precedence over the new
> cross-platform `net5.0` (and higher) as well? On the one hand, it seems that
> people who upgrade their package for .NET 5+ would also use the new TFMs. On
> the other hand, it doesn't seem unlikely for Xamarin authors to support both
> the new as well as the old TFMs for a while. That might include
> `net5.0`/`net6.0` for bait & switch scenarios, where preferring them over
> `xamarin.ios` would be undesirable.

## APIs

We need to add platform detection APIs for Mac Catalyst:

```C#
namespace System
{
    public partial class OperatingSystem
    {
        public static bool IsMacCatalyst();
        public static bool IsMacCatalystVersionAtLeast(int major, int minor = 0, int build = 0);
    }
}
```

## Platform support annotations

It seems the Xamarin team wants to have a single assembly with bindings for the
iOS platforms (e.g. Mac Catalyst and iOS). The APIs should be annotated with
`[SupportedOSPlatform]` and don't need any `[UnsupportedOSPlatform]` (the
absence of a platform is interpreted as the platform not being supported). For
instance:

```C#
public class SomeType
{
    [SupportedOSPlatform("ios8.0")]
    [SupportedOSPlatform("maccatalyst13.0")]
    public void MethodSupportedOn_iOS_and_Mac_Catalyst(); 

    [SupportedOSPlatform("ios8.0")]
    public void MethodSupportedOn_iOS_Only(); 

    [SupportedOSPlatform("maccatalyst13.0")]
    public void MethodSupportedOn_Mac_Catalyst_Only(); 
}
```

However, from a user experience it's irrelevant if the Xamarin team provides the
bindings in separate libraries or in a single library because there is no TFM
that developers can use to build a single binary that can work in both Mac
Catalyst and iOS. A developer either has to target Mac Catalyst (via
`net6.0-maccatalyst`) or iOS (via `net6.0-ios`). Of course, the developer can
multi-target for both but that means two binaries are being produced which means
the Xamarin bindings could also be provided as separate libraries.

This is slightly different for cross-platform BCL APIs. Here we'd follow the
path we took for Blazor WebAssembly and mark APIs that don't work in Xamarin
with attributes like `[UnsupportedOSPlatform("iOS")].

## RIDs

We need to add RIDs for all these platforms:

* `net6.0-android`
    - `android`
    - `android-arm`
    - `android-arm64`
    - `android-x64`
    - `android-x86`
    - `android.21`
    - `android.21-arm`
    - `android.21-arm64`
    - `android.21-x64`
    - `android.21-x86`
    - `android.22`
    - `android.22-arm`
    - `android.22-arm64`
    - `android.22-x64`
    - `android.22-x86`
    - `android.23`
    - `android.23-arm`
    - `android.23-arm64`
    - `android.23-x64`
    - `android.23-x86`
    - `android.24`
    - `android.24-arm`
    - `android.24-arm64`
    - `android.24-x64`
    - `android.24-x86`
    - `android.25`
    - `android.25-arm`
    - `android.25-arm64`
    - `android.25-x64`
    - `android.25-x86`
    - `android.26`
    - `android.26-arm`
    - `android.26-arm64`
    - `android.26-x64`
    - `android.26-x86`
    - `android.27`
    - `android.27-arm`
    - `android.27-arm64`
    - `android.27-x64`
    - `android.27-x86`
    - `android.28`
    - `android.28-arm`
    - `android.28-arm64`
    - `android.28-x64`
    - `android.28-x86`
    - `android.29`
    - `android.29-arm`
    - `android.29-arm64`
    - `android.29-x64`
    - `android.29-x86`
    - `android.30`
    - `android.30-arm`
    - `android.30-arm64`
    - `android.30-x64`
    - `android.30-x86`
* `net6.0-ios`
    - `ios`
    - `ios-arm`
    - `ios-arm64`
    - `ios-x64`
    - `ios-x86`
    - `ios.8`
    - `ios.8-arm`
    - `ios.8-arm64`
    - `ios.8-x64`
    - `ios.8-x86`
    - `ios.9`
    - `ios.9-arm`
    - `ios.9-arm64`
    - `ios.9-x64`
    - `ios.9-x86`
    - `ios.10`
    - `ios.10-arm`
    - `ios.10-arm64`
    - `ios.10-x64`
    - `ios.10-x86`
    - `ios.11`
    - `ios.11-arm64`
    - `ios.11-x64`
    - `ios.12`
    - `ios.12-arm64`
    - `ios.12-x64`
    - `ios.13`
    - `ios.13-arm64`
    - `ios.13-x64`
* `net6.0-maccatalyst`
    - `maccatalyst-arm64`
    - `maccatalyst-x64`
    - `maccatalyst.13-arm64`
    - `maccatalyst.13-x64`
    - `maccatalyst.14-arm64`
    - `maccatalyst.14-x64`
* `net6.0-macos`
    - `osx`
    - `osx-arm64`
    - `osx-x64`
    - `osx.10.10`
    - `osx.10.10-arm64`
    - `osx.10.10-x64`
    - `osx.10.11`
    - `osx.10.11-arm64`
    - `osx.10.11-x64`
    - `osx.10.12`
    - `osx.10.12-arm64`
    - `osx.10.12-x64`
    - `osx.10.13`
    - `osx.10.13-arm64`
    - `osx.10.13-x64`
    - `osx.10.14`
    - `osx.10.14-arm64`
    - `osx.10.14-x64`
    - `osx.10.15`
    - `osx.10.15-arm64`
    - `osx.10.15-x64`
    - `osx.10.16`
    - `osx.10.16-arm64`
    - `osx.10.16-x64`
    - `osx.11.0`
    - `osx.11.0-arm64`
    - `osx.11.0-x64`
* `net6.0-tvos`
    - `tvos`
    - `tvos-arm64`
    - `tvos-x64`
    - `tvos.10`
    - `tvos.10-arm64`
    - `tvos.10-x64`
    - `tvos.11`
    - `tvos.11-arm64`
    - `tvos.11-x64`
    - `tvos.12`
    - `tvos.12-arm64`
    - `tvos.12-x64`
    - `tvos.13`
    - `tvos.13-arm64`
    - `tvos.13-x64`

## Q & A

### Why didn't we name it `net6.0-macos-catalyst`?

We didn't want a secondary dash because it implies some relationship with
`net6.0-macos`. Also, in the past we talked about supporting prefix-based
compatibility rules with more levels, using a dash for this new TFM would make
it harder to add support for this later.

### Why didn't we name it `net6.0-catalyst`?

Looks like [Apple refers to it as "Mac Catalyst"](https://developer.apple.com/mac-catalyst/).

### Why did we make `net6.0-maccatalyst` compatible with the existing Xamarin TFMs?

It's true that for native code existing binaries don't work in Mac
Catalyst and that developers are generally expected to recompile. However, the
Xamarin team believes that enough managed code would just work that makes this
compat relationship useful. This follows the principle of the .NET Framework
compatibility mode which is:

> Instead of blocking things that might not work, unblock things that could
> work.

### Why didn't we make `net6.0-ios` compatible with `net6.0-maccatalyst`?

The expectation is that moving forward libraries that want to work on iOS and
Mac Catalyst would use `net6.0` or multi-target for `net6.0-ios` and
`net6.0-maccatalyst`

[net5.0]: ../../2020/net5/net5.md
[NU1701]: https://docs.microsoft.com/en-us/nuget/reference/errors-and-warnings/nu1701
