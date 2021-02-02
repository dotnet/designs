# .NET 6.0 Target Frameworks

**PM** [Immo Landwerth](https://github.com/terrajobst)

In [.NET 5.0][net5.0] we defined a new syntax for target frameworks (TFM) that
allows us to avoids tools and humans to use a decoder ring, such as the .NET
Standard version table, to figure out what is compatible with what. In this
document, we're listing which TFMs we're adding in .NET 6.0.

## Scenarios and User Experience

TBD

## Requirements

### Goals

* Support the definition of frameworks relevant for Xamarin development

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
| net6.0-watchos     | xamarin.watchos                          |
|                    | (+everything else inherited from net6.0) |

### Compatibility rules

The [built-in compatibility rules][net5.0] of the `netX.Y-platform` TFMs make a
TFM automatically compatible with:

1. Itself
2. Earlier versions of itself
3. The corresponding `netX.Y` TFM
4. Earlier versions of the corresponding `netX.Y` TFM

The `net5.0` TFM has the additional twist that it's compatible with
`netcoreapp3.2` (and earlier), `netstandard2.1` (and earlier) as well as .NET
Framework (`net4.x` and earlier, but under a warning). The .NET Framework
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

> ***OPEN QUESTION**: Should `net6.0-maccatalyst` also have a compatibility
> relationship with any of the **existing** Xamarin TFMs, such as `xamarin.mac`
> or `xamarin.tvos`?*
>
> ***OPEN QUESTION**: Should `net6.0-maccatalyst` also have a compatibility
> relationship with any of the **new** Xamarin TFMs, such as `net6.0-mac`?*
>
> ***OPEN QUESTION**: Should `net6.0-maccatalyst` generate a warning when assets
> from `xamarin.ios` are being used?

## Q & A

### Why didn't we name it `net6.0-macos-catalyst`?

We didn't want a secondary dash because it implies some relationship with
`net6.0-macos`. Also, in the past we talked about supporting prefix-based
compatibility rules with more levels, using a dash for this new TFM would make
it harder to add support for this later.

### Why didn't we name it `net6.0-catalyst`?

Looks like [Apple refers to it as "Mac Catalyst"](https://developer.apple.com/mac-catalyst/).

### Why did we make `net6.0-maccatalyst` compatible with the existing Xamarin TFMs?

It's true that for native code existing binaries largely don't work in Mac
Catalyst and that developers are generally expected to recompile. However, the
Xamarin team believes that enough managed code would just work that makes this
compat relationship useful. This follows the principle of the .NET Framework
compatibility mode which is:

> Instead of blocking things that might not work, unblock things that could
> work.

[net5.0]: ../../2020/net5/net5.md
