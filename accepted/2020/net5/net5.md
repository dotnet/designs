<!-- markdownlint-disable MD026 -->
<!-- markdownlint-disable MD045 -->

# Target Framework Names in .NET 5

**Owner** [Immo Landwerth](https://github.com/terrajobst) |
[GitHub Issue](https://github.com/dotnet/runtime/issues/34173) |
[Video Presentation](https://youtu.be/kKH8NzvtENQ?t=694)

We'd like to drastically simplify the framework names (TFMs) developers must use
in project files and NuGet packages. This includes merging the concept of .NET 5
and .NET Standard while still being able to use `#if` to use OS-specific APIs.
This document explains the motivation and resulting developer experience.

.NET, as most technologies that are two decades old, has a lot of heritage,
especially in product naming and editions: .NET Framework, .NET Compact
Framework, Silverlight, .NET Micro Framework, .NET Portable Class Libraries,
.NET for Windows Store, .NET Native, .NET Core, .NET Standard... and that
doesn't even include what the Mono community built. While this evolution of .NET
can be explained (and was properly motivated) it created a massive tax: the
concept count. If you're new to .NET, where would you start? What is the latest
stack? You may say, "of course that's .NET Core" but how would anyone know that
by just looking at the names?

We've simplified the world with .NET Standard, in that class library authors
don't have to think about all the different "boxes" that represent different
implementations of .NET. It did that by unifying the *API surface* of the
various .NET implementations. Ironically, this resulted in us having to add yet
another box, namely .NET Standard.

To make the future saner, we must reduce the number of boxes. We don't want to
make .NET less flexible, but we want to reduce nonsensical differences that
purely resulted from us not being open source early enough. For example,
Mono/Xamarin/Unity are based on a different set of runtimes and frameworks than
the .NET Framework/Silverlight/UWP/.NET Core lineage. With .NET Standard, we
have started to remove the differences in the API surface. With .NET 5, the goal
is to converge these lineages onto a single product stack, thus unifying
their *implementations*.

![](pic01.png)

While we strive to provide an experience where you don't have to reason about
the different kinds of .NET, we still don't want to fully abstract away the
underlying OS, so you'll continue to be able to call OS specific APIs, be that
via P/Invokes, WinRT, or the Xamarin bindings for iOS and Android.

Now think about developers who start on this stack and can write any application
for any of the platforms that .NET provides support for. The branding we
currently have makes no sense to them. To find documentation and tutorials, the
only two things a developer should need to know is the name and version of their
technology stack.

Let's contrast this with some of the NuGet packages that developers have to
author for today's world:

![](pic02.png)

There are a lot of names and version numbers. Knowing who is compatible with who
is impossible without a decoder ring. We've simplified this greatly with .NET
Standard, but this still requires a table that maps .NET Standard versions to
.NET implementation versions.

The proposal is to reuse the existing `net` TFM and model OS-specific APIs on
top via a new syntax:

* `net5.0`. This TFM is for code that runs everywhere. It combines and replaces
  the `netcoreapp` and `netstandard` names. This TFM will generally only include
  technologies that work cross-platform (modulo pragmatic concessions, like we
  already did in .NET Standard).

* `net5.0-android`, `net5.0-ios`, and `net5.0-windows`. These TFMs represent OS
  specific flavors of .NET 5 that include `net5.0` plus OS-specific bindings.

NuGet should use this new syntax to automatically understand that `net5.0` can
be consumed from `net6.0-windows` (but not the other way around). More
importantly, this notation will also enable developers to intuitively understand
compatibility relationships because they are expressed by naming, rather than by
mapping tables. Yay!

## Scenarios and User Experience

### Vary implementation

Ida is working on a Xamarin Forms application that supports Android, iOS, and
Windows. Her application needs GPS information, but only a very limited set.
Since there is no portable GPS API, she writes her own little abstraction
library using multi-targeting.

By doing so, she's able to encapsulate the GPS access without having to
multi-target her entire application, just this one area.

```C#
public static class GpsLocation
{
    public static bool IsSupported
    {
        get
        {
#if ANDROID || IOS || WINDOWS
            return true;
#else
            return false;
#endif
        }
    }

    public static (double Latitude, double Longitude) GetCoordinates()
    {
#if ANDROID
        return AndroidAPI();
#elif IOS
        return AppleAPI();
#elif WINDOWS
        return WindowsAPI();
#else
        throw new PlatformNotSupportedException();
#endif
    }
}
```

### Vary API surface

Ada is a developer on SkiaSharp, a cross-platform 2D graphics API for .NET
platforms based on Google's Skia Graphics Library. The project is already using
multi-targeting to provide different implementations for different platforms. To
make it easier to use, she's adding a new `SkiaSharpImage` type, which
represents a bitmap and is constructed via OS-provided data types. Ada uses
`#if` to expose different constructors on different platforms:

```C#
public class SkiaSharpImage
{
#if ANDROID
    public SkiaSharpImage(Android.Media.Image nativeImage) { /* ... */  }
#endif

#if IOS
    public SkiaSharpImage(NSImage nativeImage) { /* ... */ }
#endif

#if WINDOWS
    public SkiaSharpImage(Windows.Media.BitmapImage nativeImage) { /* ... */ }
#endif
}
```

### Upgrading the OS bindings

Miguel is building Baby Shark, a popular iOS application. He started with .NET
that supported iOS 13 but Apple just released iOS 14. He downloads the updated
version of the .NET 5 SDK which now also includes support for iOS 14. In order
to gain access to the new APIs that Apple has added, Miguel opens his project
file which currently looks like this:

```XML
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net5.0-ios13.0</TargetFramework>
  </PropertyGroup>

  ...

</Project>
```

He modifies the `<TargetFramework>` to be `net5.0-ios14.0`.

### Lighting up on later OS versions

Miguel doesn't want to cut off his users who are currently on iOS 13, so he
wants to continue to have his application work on iOS 13 as well. To achieve
that, Miguel modifies the project file by adding `<SupportedOSPlatformVersion>`:

```XML
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net5.0-ios14.0</TargetFramework>
    <SupportedOSPlatformVersion>13.0</SupportedOSPlatformVersion>
  </PropertyGroup>

  ...

</Project>
```

However, since Miguel also uses the new `NSFizBuzz` API that Apple added in iOS
14, he also modifies his source code to check for the operating system version
before calling it:

```C#
public void OnClick(object sender, EventArgs e)
{
    if (OperatingSystem.IsIOSVersionAtLeast(14))
    {
        NSFizBuzz();  
    }
}
```

### Consuming a library with a higher `SupportedOSPlatformVersion`

After using `NSFizzBuzz` directly for a while, Miguel notices that these OS APIs
are a bit hard to use, so he looks for a .NET library. He finds
`Monkey.FizzBuzz`, which he tries to reference, which succeeds. However, when
building his application he gets the following warning:

> warning NU1702: Package 'Monkey.FizzBuzz' was restored using 'net5.0-ios14'
> and has 'SupportedOSPlatformVersion' of '14.0' while the project has a value of
> '13.0'. You should either upgrade your project to '14.0' or only make calls
> into the library after checking that the OS version is '14.0' or higher.

Since Miguel already guarded all method calls, he simply suppresses the warning.

### Consuming a library with a higher `TargetPlatformVersion`

After the success of using `Monkey.FizzBuzz` in his Baby Shark app, Miguel wants
to use it everywhere now, so he decides to use it in his existing Laserizer 5000
app. However, when he adds a reference to `Monkey.FizzBuzz`, he gets an error
from NuGet:

> error NU1202: Package 'Monkey.FizzBuzz' is not compatible with
> 'net5.0-ios13.0'. Package 'Monkey.FizzBuzz' supports: net5.0-ios14.0

So Miguel edits his project file by changing `net5.0-ios13.0` to
`net5.0-ios14.0` which fixes the error.

### Using a higher `TargetPlatformVersion` than the current SDK

Claire has the first release of the .NET 5 SDK that only ships bindings
for iOS 13. She clones Miguel's Baby Shark repo from GitHub and tries to
build it on her machine. Since Baby Shark targets `net5.0-ios14.0` she
gets a build error:

> error NETSDK1045: The current .NET SDK does not support targeting iOS 14.0.
> Either target iOS 13.0, or use a version of the .NET SDK that supports iOS
> 14.0. [BabyShark.csproj]

## Requirements

### Goals

* Use naming that aligns with product strategy
* Merge .NET Core and .NET Standard into a single concept
* Developers should be able to understand compatibility relationships without
  having to consult a mapping table.
* Provide compatibility with existing concepts and NuGet packages
* It would be great to get this into early previews of .NET 5
* Support multi-targeting different versions of the same OS
* Don't force multi-targeting for different versions of the same OS. It should
  be possible to produce a single binary that can use newer APIs when calls are
  properly guarded with an OS check.

### Non-Goals

* Replace TFMs or expand runtime identifiers (RIDs)

## Design

We'll have the following TFMs:

| TFM             | Compatible With                                            | Comments                          |
|-----------------|------------------------------------------------------------|-----------------------------------|
| net5.0          | net1..4 (with NU1701 warning)                              | No WinForms or WPF                |
|                 | netcoreapp1..3.1 (warning when WinForms/WPF is referenced) |                                   |
|                 | netstandard1..2.1                                          |                                   |
| net5.0-android  | xamarin.android                                            |                                   |
|                 | (+everything else inherited from net5.0)                   |                                   |
| net5.0-ios      | xamarin.ios                                                |                                   |
|                 | (+everything else inherited from net5.0)                   |                                   |
| net5.0-macos    | xamarin.mac                                                |                                   |
|                 | (+everything else inherited from net5.0)                   |                                   |
| net5.0-tvos     | xamarin.tvos                                               |                                   |
|                 | (+everything else inherited from net5.0)                   |                                   |
| net5.0-watchos  | xamarin.watchos                                            |                                   |
|                 | (+everything else inherited from net5.0)                   |                                   |
| net5.0-windows  | netcoreapp1..3.1                                           | WinForms + WPF                    |
|                 | (+everything else inherited from net5.0)                   |                                   |
| Tizen, Unity... | Will follow the Xamarin model                              |                                   |

### Precedences

We need to decide what precedences we want the new TFMs to have in relation to
the old ones as well as in relation with the new universally portable TFM
`net5.0` (and higher).

Let's look at two examples:

* **Example A**
    * Project targets `net6.0-ios`
    * Package offers
        * `net6.0`
        * `net5.0-ios`
* **Example B**
    * Project targets `net6.0-ios`
    * Package offers
        * `net6.0`
        * `xamarin.ios`

There are effectively two options:

1. Prefer platform-specific assets, regardless how old their version is.
2. Prefer the assets that are closer in version number.

Example | Option (1)    | Option (2)
--------|---------------|--------------
A       | `net5.0-ios`  | `net6.0`
B       | `xamarin.ios` | `net6.0`

We prefer Option (2). @mhutch described it best:

> I prefer the Option (2) because it allows re-converging to platform-neutral:
> in both A and B, if `net6.0` added functionality that meant that the iOS
> specific implementation was no longer necessary, then the package would not
> need to have `net6.0-ios` assets.
>
> If the `net5.0-ios` assembly has iOS-specific _functionality_, then when the
> package adds `net6.0` it should also add `net6.0-ios`, else it feels to me
> like a broken package.

@dsplaisted also added this:

> One of the main benefits of the target framework changes in .NET 5 and on is
> that it aligns the version numbers of the platform specific targets
> (`net5.0-ios`) with the portable target (`net5.0`). This makes it easy to
> understand the version compatibility rules. It also means that we can have a
> simple and easy understand rule for choosing the best asset that is more
> likely to produce a result that matches the intent of a package author.

And finally @ericstj added that the current model (where we prefer
platform-specific assets, regardless of version number) isn't helpful:

> Option (2) makes sense to me. I don't recall a place where we've actually
> preferred that RIDs had precedence over TFM. Usually it's a problem and
> results in bloated packages. I like reversing the precedence here.

This leads to these rules:

1. Select list of applicable assets
   * This will include `netX.Y`, `netX.Y-*`, and `xamarin.*` if they are
     considered compatible
   * This will not include assets that use an OS/OS version that is incompatible
     with the project.
2. Group `netX.Y*` by X.Y and sort groups from highest to lowest
3. If the first group contains both a `netX.Y` and a `netX.Y-*` or a
   `xamarin.*`, pick `netX.Y-*`. If multiple entries for `netX.Y-*` exist, pick
   the highest version.
4. If no groups were produced, pick the appropriate TFM based on existing rules
   (this covers .NET Standard, PCLs, and general asset target fallback).

Then intention is:

1. We slice the package assets to find the latest .NET version that is
   compatible with the project.
2. If the latest .NET version provides both a platform-specific- and a portable
   asset, we'll prefer the platform-specific asset.
3. If multiple platform-specific assets exist, we'll use the one with the
   highest compatible OS version.
4. Stay backwards compatible with existing .NET Standard rules.

### OS versions

The TFM will support an optional OS version, such as:

* `net5.0-ios`
* `net5.0-ios12.0`

A TFM without an OS version will be interpreted as the lowest OS version that
was supported by the corresponding `netX.Y` version.

> *Note: One assumption here is that developers can always compile for the
> latest OS API set and run on older OS versions so long they guard method calls
> correctly. This is how P/Invokes, WinRT, iOS, and Android bindings work
> today.*

For example, say that when .NET 5 ships, the version of iOS that we include
bindings for is iOS 13. That would mean that `net5.0-ios` and `net5.0-ios13.0`
mean the exact same thing. Now lets say that Apple ships a new iOS 14 before
.NET 6 is ready. We'd release a new version of the .NET 5 SDK that adds support
for iOS 14. On machines with that SDK `net5.0-ios` still means `net5.0-ios13.0`.

Please note that this mapping is specific for `net5.0`. When .NET 6 ships, we
could decide that `net6.0-ios` means `net6.0-ios14.0`. But even when you use the
.NET 6 SDK, when targeting .NET 5, `net5.0-ios` also still means
`net5.0-ios13.0`. So these mappings are immutable.

We have done this to simplify the experience for application models where the OS
version is largely irrelevant, for example WinForms and WPF. Whether or not the
template will put an OS version in the `<TargetFramework>` is up to each
application model. Based on conversations, it seems we'll be landing on this:

TFM             | Project file includes OS version
----------------|---------------------------------
net5.0-android  | Yes
net5.0-ios      | Yes
net5.0-macos    | Yes
net5.0-tvos     | Yes
net5.0-watchos  | Yes
net5.0-windows  | No

Please note that by being able to put the OS version into the TFM one can also
multi-target between OS versions:

```XML
<PropertyGroup>
  <TargetFrameworks>net5.0-ios13.0;net5.0-ios14.0</TargetFrameworks>
</PropertyGroup>
```

However, it's a bit misleading to think of the OS version number as the version
of the operating system you're running on. Rather, it's the operating system's
API you're compiling for.

People should really think of the TFM's OS version as the version number of the
.NET OS bindings. Under normal circumstances we'll rev that version in lock step
with the OS. This will make it easy to understand which version you need to gain
access to the APIs. However, there are cases where that's not possible. For
example, iOS API bindings aren't fully generated; they require manual work.
Depending on how large the API surface in a given OS update is we might decide
to finish the bindings for the most useful OS APIs first and add other bindings
later (which we have done in the past). If the OS normally ships APIs in
*major.minor* then we can use the third digit to indicate binding-only updates.
However, we don't control the OS version so in principle there is no guarantee
that they don't ship new APIs in third- or fourth digit updates.

Let's look at a concrete example. Assume that we shipped .NET 5 with iOS 13.0
support and Apple released an update to iOS with new APIs, say iOS 13.1. Let's
say it takes us a few updates to finish binding them all:

TFM                | Description
-------------------|----------------------------
`net5.0-ios13.0`   | The entirety of iOS 13.0
`net5.0-ios13.1.1` | First batch of bindings for iOS 13.1.
`net5.0-ios13.1.2` | Second batch of bindings for iOS 13.1.
`net5.0-ios13.1.3` | Third batch of bindings for iOS 13.1.

Now let's say that Apple decides to ship an iOS 13.1.1 with new APIs as well. As
you can see, we already used that number to indicate the first batch of 13.1
APIs. No biggie, we'd just bind these APIs in the next available train, which
happens to be `net5.0-ios13.1.4`.

We don't expect this to be too confusing because we generally encourage people
to compile against the latest available OS API set and use
`SupportedOSPlatformVersion` in order to run on older versions. The reference
assembly for the OS bindings will also include an attribute per API indicating
which OS version is required. These attributes, as well as
`SupportedOSPlatformVersion`, will always be set to actual OS versions.

So in practice we don't expect people having to match TFMs to specific OS
version but it's good to have numbers that are "close" enough to give project
maintainers some idea of what's available to them.

### Mapping to properties

These are the relevant MSBuild properties:

Property                              | Meaning                         | Examples
--------------------------------------|---------------------------------|---------------------------------
`TargetFramework` (TFM)               | The friendly name               | `net4`, `netcoreapp3.0`
`TargetFrameworkIdentifier` (TFI)     | The long name                   | `.NETFramework` or `.NETCoreApp`
`TargetFrameworkVersion` (TFV)        | The version number              | `v2`, `v3.0`, `v3.1`
`TargetFrameworkProfile` (TFP)        | The profile                     | `Client` or `Profile124`
`TargetPlatformIdentifier` (TPI)      | The OS platform                 | `iOS`, `Android`, `Windows`
`TargetPlatformVersion` (TPV)         | The OS platform version         | `12.0` or `13.0`
`SupportedOSPlatformVersion` (SOPV)   | The minimum OS platform version | `12.0` or `13.0`

We're going to map the TFMs as follows:

TF                 | TFI           | TFV      | TFP | TPI     | TPV | SOPV
-------------------|---------------|----------|-----|---------|-----|----------------
net4.X             | .NETFramework | v4.X     |     |         |     |
net5.0             | .NETCoreApp   | v5.0     |     |         |     |
net5.0-androidX.Y  | .NETCoreApp   | v5.0     |     | Android | X.Y | X.Y (defaulted)
net5.0-iosX.Y      | .NETCoreApp   | v5.0     |     | iOS     | X.Y | X.Y (defaulted)
net5.0-windowsX.Y  | .NETCoreApp   | v5.0     |     | Windows | X.Y | X.Y (defaulted)

Specifically:

* **We'll continue to use .NETCoreApp as the TFI**. This reduces the number of
  places build logic needs to change in MSBuild files (both built-in targets as
  well as third party targets deployed via NuGet packages).

* **net4x and earlier will continue to use .NETFramework as the TFI**. This
  means that `net4x` and `net5.0` aren't considered compatible by default, but
  the compatibility will continue to be provided by .NET Framework compatibility
  mode we introduced in .NET Standard 2.0. It's handled via
  `AssetTargetFallback` in NuGet restore which also means consumers from
  `net5.0` will continue to get a proper warning.

* **SupportedOSPlatformVersion is defaulted to TargetPlatformVersion**. However,
  the customer can override this in the project file to a lower version (using a
  higher version than `TargetPlatformVersion` should generate an error).

_**Open Issue**. Please note that `net5.0`+ will map the TFI to `.NETCoreApp`.
We need to announce this change so that package authors with custom .props and
.targets are prepared. Link to [DavKean's doc][parsing-guidance] on how to do
it._

_**Open Issue**. We should try to keep the TFI out of the .nuspec file. It seems
NuGet uses the long form `.NETFramework,Version=4.5` in the dependency groups.
We may want to change NuGet to allow the friendly name there as well and update
our packaging tools to re-write to friendly name on pack._

### Mapping to long names

As [mentioned earlier](#mapping-to-properties), TFMs have multiple formats. The
most common ones are the friendly name (`netstandard2.0`) and the long name
(`.NETStandard, Version=2.0`). NuGet also uses some other encodings (such as
`.NETStandard2.0`).

All places that have to refer to a NuGet asset need a naming encoding that can
represent the new TFMs which now also includes platform information. We'd like
to avoid using a long name like

```text
.NETCoreApp, Version=5.0, Platform=iOS, PlatformVersion=13.0
```

The reason being that the NuGet short name is essentially used to encode both a
target framework and a target platform, both of which already have a canonical
long name form.

Since the friendly name has become the primary name that developers author in
the developer experience (project file, NuGet folder names, etc) we'd like to
standardize on that encoding. Unfortunately, NuGet itself isn't consistent with
using the friendly name today. We generally don't want to break backwards
compatibility, that is existing frameworks should be encoded in the way they are
encoded today, be that friendly name, long name or some custom encoding.

However, for frameworks in the .NET 5 era (and later) we want to consistently
use the friendly name.

The same applies to public APIs (for example, NuGet or the dependency model) that
return framework names: .NET 5 era TFMs should use the friendly names, all other
TFMs should be returned in whatever encoding they are returned today.

Concretely, this means the following:

* `project.assets.json`
    * **Use friendly name for .NET 5 and higher**
    * Eventually we'd like to update the format so that the keys to the target
      are not necessarily parseable TFMs, but can be arbitrary strings defined
      in the `<TargetFrameworks>` property. That would mean we'd need to store
      the target framework and target platform information (and probably the
      RID) separately (see [NuGet/Home#5154](https://github.com/NuGet/Home/issues/5154)).
* `packages.lock.json`
    * **Use friendly name for .NET 5 and higher**
    * For other target frameworks use current encoding (i.e. long form)
    * This allows old and new tooling to be used in the same repo
* `.nuspec` files
    * **Use friendly name for .NET 5 and higher**
    * Use existing nuspec form (which is neither friendly- nor long name) for
      other targets
    * This will preserve compatibility with older clients, and can be considered
      mostly an implementation detail of NuGet
* `GetDotNetFrameworkName()` NuGet API
    * **Use friendly name for .NET 5 and higher**
* Package Manager UI in VS
    * [Use short form](#tfms-in-the-ui)

### Windows-specific behavior

_**Open Issue**. Review with WinForms/WPF & Windows folks._

* We need to define what the Windows version number is. It should probably be
  the minimum for WPF/WinForms (because that makes the most sense until we ship
  support for WinRT in .NET 5). Generally speaking, we expect UWP flavors to
  burn in into the project file, just like iOS and Android.

* When would the WinRT APIs be referenced? Should they show up by default
  (assuming the project specified they correct version for the WinRT bindings)
  or should there be an equivalent for `UseWindowsForms`? We probably don't want
  an opt-in for the foundational WinRT APIs but maybe for the UI layer.

### NuGet pack behavior

We need to update the .nuspec format to allow embedding target platform
information per TFM.

For that, I propose to add a `platforms` element under `metadata`.

> **Note** This is just meant to be a strawman for NuGet so we can talk about
> semantics. The NuGet team should specify the actual encoding. Once spec is
> available, we'll link it from here.

For each `netX.Y-{os}{version}`, it should contain a `platform` that ties the
TFM as specified to their corresponding `TargetPlatformVersion` and
`SupportedOSPlatformVersion` entries:

```xml
<package xmlns="...">
  <metadata>
    <id>ClassLibrary3</id>
    <version>1.0.0</version>
    <authors>ClassLibrary3</authors>
    <owners>ClassLibrary3</owners>
    <requireLicenseAcceptance>false</requireLicenseAcceptance>
    <description>Package Description</description>
    <platforms>
      <platform targetFramework="net5.0-ios"
                platformVersion="14.0"
                platformMinimumVersion="13.0" />
      <platform targetFramework="net6.0-ios14"
                platformVersion="14.0"
                platformMinimumVersion="14.0" />
      </platform>
    </platforms>
  </metadata>
</package>
```

We want to make sure that we preserve the TFM from the project file:

* **TFM doesn't contain an OS**. If the TFM is just the neutral `netX.Y` TFM
  then the `.nuspec`'s `<platform>` element shouldn't list the TFM. If there are
  no `<platform>` elements, the `<platforms>` element should be omitted as well.

* **TFM has an OS but no OS version**. If the user omits the OS version number
  from the project's `<TargetFramework>` property, all usages of the TFM should
  not contain an OS version number, including the project's output directory,
  the `lib` folder in the NuGet package, and other corresponding entries in the
  .nuspec. However, the effective OS version will be recorded in the `.nuspec`'s
  `<platform>` element that corresponds to the TFM.
  
* **TFM has both OS and OS version**. If the project did contain an OS version
  for `<TargetFramework>` this should also be reflected by the project's output
  directory, `lib` folder, and TFM references in the .nuspec.
  
This means that in some cases the `platformVersion` attribute will have
redundant information but it will also always be the source of truth. It allows
NuGet to know the OS version even if the OS version isn't included in the TFM.

For cases where someones wants to pack two `lib` folders like this:

* `net5.0-ios`
* `net5.0-ios13.0`

and `net5.0-ios`'s is mapped to be platform version `13.0`, the `pack` operation
should fail because both `lib` folders are representing the same target (the
same applies to other usages of the TFM in the `ref` and `runtime` folders).

### NuGet installation behavior

The TFM part before the dash will behave as today: projects can reference the
same or an older version of `netX.Y`, but not a newer version. Trying to do
should result in a package installation failure. The OS portion will follow the
same rules.

The rationale is the same for both: when the library author compiled for a
higher version, it had access to more APIs, which the consuming project doesn't
have. This can cause compilation errors due to unresolved types as well as
runtime errors due to unresolved members.

The world is a bit different for `SupportedOSPlatformVersion`. NuGet should allow
installation of packages whose `SupportedOSPlatformVersion` is higher than the
consuming project. The rationale here is that the consuming project might only
want to use the library on higher versions of the operating system and can
conditionally call the library code. The behavior should be similar to
`AssetTargetFallback` where installation of the package succeeds but each time
the project is built a warning is produced, which must be suppressible by using
the `<NoWarn>` property in the project file or on the `<PackageReference>` item.

### Valid platforms

One question is whether third parties can extend the TFM space with a new
platform (that is, the part after the dash) without having to rely on changes
in NuGet/MSBuild. We believe that supporting a new platform would require
MSBuild logic that is not part of the core .NET SDK. So we will do the
following:

- NuGet will not have a list of allowed platform names.  It will understand
the compatibility mappings (ie net5.0-android and higher is compatible with
xamarin.android), but will not otherwise have specific knowledge of each
possible platform.

- The .NET SDK will have logic to generate a build error if the
`TargetPlatformIdentifier` is something that is not supported.  This will
protect against typos in the platform part of the `TargetFramework` and
against seeming to build correctly when trying to target a platform that
is not supported.

- MSBuild logic outside of the .NET SDK will be able to communicate that
the `TargetPlatformIdentifier` is supported.  So the iOS and Android
workloads will set a property indicating that the corresponding platform
is supported, which will suppress the error in the core .NET SDK.  Likewise,
a third party MSBuild SDK could set the same property to enable another
platform.

- Comparison of platform identifiers should be case insensitive, but
normalized by the MSBuild logic that supports that platform.  So with a
`TargetFramework` of `net5.0-android`, NuGet should parse out the platform
as `Android`.  The core .NET SDK will then set the `TargetPlatformIdentifier`
to `android`.  The Android workload should then normalize the casing of the
`TargetPlatformIdentifier` property to `Android`.  Similarly, the iOS Workload
and Windows targets should normalize the `TargetPlatformIdentifier` to `iOS`
and `Windows`.

### What about .NET 10?

Due to the fact that we're planning to bump the major version every year, we
have to think about what will happen with version parsing in case of two digit
version numbers, such as `net10`. Since `net10` already has a meaning (.NET
Framework 1.0), we need to keep it that way. To avoid surprises, we'll by default
use dotted version numbers in project templates to push developers towards being
explicit.

Framework      | Identifier    | Version | Comment
---------------|---------------|---------|----------------------------------
net5           | .NETCoreApp   | 5.0     | Will work, but shouldn't be used.
net5.0         | .NETCoreApp   | 5.0     |
net10          | .NETFramework | 1.0     |
net10.0        | .NETCoreApp   | 10.0    |

### Preprocessor Symbols

Today, SDK-style projects automatically define `#if` symbols based on the
friendly TFM representation. This includes both versionless- as well as
version-specific symbols:

TFM             | Project-style | Automatic Defines
----------------|---------------|-------------------------------------
`net45`         | CSPROJ        |
`net45`         | SDK-style     | `NETFRAMEWORK`, `NET45`
`net48`         | CSPROJ        |
`net48`         | SDK-style     | `NETFRAMEWORK`, `NET48`
`netcoreapp3.1` | SDK-style     | `NETCOREAPP`, `NETCOREAPP3_1`

For .NET 5 and higher we plan to define the following symbols:

TFM                 | Automatic Defines
--------------------|-------------------------------------------------------
`netX.Y`            | `NETCOREAPP`, `NET`, `NETX_Y`
`netX.Y-iosA.B`     | `NETCOREAPP`, `NET`, `NETX_Y`, `IOS`, `IOSA_B`
`netX.Y-androidA.B` | `NETCOREAPP`, `NET`, `NETX_Y`, `ANDROID`, `ANDROIDA_B`
`netX.Y-windowsA.B` | `NETCOREAPP`, `NET`, `NETX_Y`, `WINDOWS`, `WINDOWSA_B`

Specifically:

* We continue to define `NETCOREAPP` for backwards compatibility with existing
  `#if` code.
* Moving forward we'll use `NET` as the versionless symbol (for .NET Framework
  we used `NETFRAMEWORK`, so no conflict there).
* We'll follow the rule-based creation in SDK-style projects which takes the
  friendly TFM name without the OS flavor, makes it upper-case and replaces
  special characters with an underscore.
* For OS flavors we'll do the same, i.e. create versionless as well as version
  specific symbols.

In order to make it easier to update code, especially when doing
multi-targeting, we should make them additive, so that when targeting `net6.0`
both `NET6_0` and `NET5_0` are defined. The same applies to OS bindings.
Examples:

* `net5.0`
    * `NETCOREAPP`, `NETCOREAPP3_1` (for backwards compatibility)
    * `NET`, `NET5_0`
* `net6.0`
    * (same as `net5.0`)
    * `NET6_0`
* `net5.0-ios13.0`
    * (same as `net5.0`)
    * `IOS`, `IOS13_0`
* `net5.0-ios14.0`
    * (same as `net5.0-ios13`)
    * `IOS14_0`

### What would we target?

Everything that is universal or portable to many platforms will target `net5.0`.
This includes most libraries but also ASP.NET Core and EF.

Platform-specific libraries would target platform-specific flavors. For example,
WinForms and WPF controls would target `net5.0-windows`.

Cross-platform application models (Xamarin Forms, ASP.NET Core) and bridge packs
(Xamarin Essentials) would at least target `net5.0` but might also additionally
target platform-specific flavors to light-up more APIs or features. This
approach is also called bait & switch.

### TFMs in the UI

There are some places in the IDE where targeting information is displayed:

![](pic03.png)

![](pic04.png)

![](pic05.png)

![](pic06.png)

Rule                                                                                                        | Affected UI
------------------------------------------------------------------------------------------------------------|-------------------------------------------------------------------
For most UI, we should use the TFM friendly name (for example, `netcoreapp3.1`, `net5.0`, or `net5.0-ios`). | Solution Explorer, Editor Context Switcher, Debug Context Switcher
For cases where we use branded display names, we should use the name .NET 5.                                | Project Properties

### Related work

To support multi-targeting end-to-end the TFM unification isn't enough. We also
need to unify the SDKs between .NET Core, Windows desktop, Xamarin, and
maybe ASP.NET Core. Without it, multi-targeting doesn't work without having to use
3rd party tools, such as Claire's
[MSBuild.Extras](https://github.com/novotnyllc/MSBuildSdkExtras).

We should also make it easier to catch cases where some APIs aren't universally
available anymore. This includes fundamentals like threading for platforms like
WASM but also version-to-version differences in OS API surface. We already have
[an analyzer](https://github.com/dotnet/platform-compat), but we need to expand
this and make it a key capability that is available out-of-the-box.

* [Making it easier to do TFM checks in MSBuild](https://github.com/microsoft/msbuild/issues/5171)

## Q & A

### Why can't we just have a single TFM for .NET 5?

Moving forward, we can assume that the base class library (BCL) of .NET is the
same across all environments of .NET. One can think of `net5.0` as .NET Standard
but with an implementation (.NET Core).

However, `net5.0` will not include .NET projections of OS APIs, such as:

* WinRT
* iOS bindings
* Android bindings
* Web assembly host APIs

We *could* make all APIs available everywhere. For example, we could create one
NuGet package per OS-platform and include two implementations, one that throws
and one that works. The package would use RIDs to select the correct
implementation, but it would have a uniform API surface. Callers of those
packages would use platform checks or catch `PlatformNotSupportedException`.

We believe this isn't the right approach for two reasons:

1. **Number of moving pieces**. Imagine what a simple class library would look
   like that just wants to provide an abstraction over a single concept, such as
   the GPS. It would transitively depend on all OS bindings. Regardless of the
   platform you're building your application for, the output folder would have
   to include all bindings across OS platforms, with all being throwing
   implementations except for the one that your app is targeting. While we could
   build tooling (such as a linker) that could remove this and replace the call
   sites with throw statements, it seems backwards to first create this mess and
   then rely on tooling to clean it up. It would be better to avoid this problem
   by construction.

2. **Versioning issues**. By making the OS bindings available on top of the .NET
   platform the consumer is now able to upgrade the .NET platform and the OS
   bindings independently, which makes it hard to explain what combinations are
   supported. In practice, the OS bindings will want to depend on .NET types
   which might change over time (think `Task<T>` or `Span<T>)`, so not every
   combination can work.

We believe it's much easier if we enable code to use multi-targeting (that is,
compile the same code for multiple platforms, like we do today.

### Is .NET 5 a superset of .NET Framework 4.x?

No, .NET 5 isn't a *superset* of .NET Framework. However, .NET 5 is
the *successor* of both .NET Core 3.x as well as .NET Framework 4.x. Starting
with .NET Core 3, you can build the same kind of workloads that you can with
.NET Framework. Some of the tech has changed, but that's the way it's [going to
be forever](https://devblogs.microsoft.com/dotnet/net-core-is-the-future-of-net/).
The remaining delta will never be closed. Existing apps can stay on .NET
Framework and will be supported, but we already said we'll no longer add new
features to it.

Thus, new apps should start on .NET Core. By branding it as .NET 5, this
recommendation is much more obvious to both existing customers and new
customers.

### ~~Why are the OS specific flavors not versioned by the OS?~~

*No longer applicable as we decided to allow that.*

There are a couple of reasons why this isn't desirable:

1. **It results a combinatorial explosion**. A TFM in the form of
   `net5.0-windows7` would (syntactically) make any combination of .NET and OS
   possible. This raises the question which combinations are supported, which
   puts us back into having to provide the customer with a decoder ring.

2. **It can make asset selection ill-defined**. Suppose the project is targeting
   `net7.0-windows10`. The package offers `net5.0-windows10.0` and
   `net6.0-windows7.0`. Now neither asset would be better.

3. **A single version isn't enough**. Logically, you need at least two version
   numbers to support OS targeting:

      * *Minimum OS API Level*. Consumers of the library must use an SDK that
        support this version, or a higher version. This allows library authors
        to use types from the OS bindings in their public API surface without
        causing type resolution issues for the consumer.

      * *Minimum OS version*. For OS calls, it's common to guard OS calls with
        version checks. This allows library authors to light up for later OS
        versions without having to produce multiple binaries. In order for that
        to be sound, a project generally cannot have lower version than whatever
        its libraries support, unless the consumer guards their calls into that
        library.
  
   Some platforms, such as Android, also have the notion of a *targeted OS
   version* that indicates to the OS what behavior the author tested for. When
   running on a later OS version, the OS might "quirk" the behavior to preserve
   backwards compatibility.

Developers will want to target different OS versions from a single code
base/NuGet package, but that doesn't mean they will need to use multi-targeting.
Multi-targeting is a very heavy hammer. Yes, many people are using it to target
different versions of .NET (for example, `net45` vs `net461`). But that's not
necessarily because `#if` is the better experience, it's because it's simply not
possible any other way, due to .NET runtime constraints (that is, assembly
references and type/member references need to be resolved by the JIT). This
problem doesn't exist for OS APIs. Developers can generally build a single
binary for multiple versions of an operating system. The calls to APIs
introduced in later versions must be guarded, but this is generally understood,
and we have some limited tooling already with plans to extend it.

### How can I ensure my NuGet package/project can express OS requirements?

We do want to allow the developer to express a minimum version they require for
the OS. It will likely be expressed as additional properties in the project file
that are also being persisted in the resulting NuGet package as well. It's not
decided on whether violating the perquisites will always be an error. For
example, we could decide to make *Minimum OS API Level* an error (because it
might result in compilation errors) while making *Minimum OS version* a warning
(because the consuming project could guard the method calls into the library).

There is a [separate doc][os-versioning] that we're working on.

### Why is there no TFM for Blazor?

Based on conversations with the Blazor team we decided to not create a TFM for
WASM in the browser. That's because Blazor wants agility where code can
transparently work on client and server. Thus, they will continue to use
`net5.0`. The browser-specific APIs will be delivered as a NuGet package, using
RIDs to provide a throwing and non-throwing implementation. Other parties will
have to do the same thing.

### ~~Why is the TFM called net5.0-browser and net5.0-wasm?~~

*No longer applicable as we won't have a TFM for Blazor.*

WASM isn't a platform (in the OS sense) as much as it is an instruction set
architecture, so it's better to think of WASM as something like x86/x64. So, it
might not make sense to define `net5.0-wasm`. Rather, it would make more sense to
define `net5.0-browser`. The rationale is that the browser will run WASM in a
sandboxed environment which equates to having different native API sets.

Any host that controls the JS runtime (for example, Node.js) could decide to expose
different/less constrained OS, which might give rise to other TFMs, such as
`net5.0-node`.

### What about native dependencies?

We don't plan to support varying API surface based on runtime characteristics
(x86/x64, AOT/JIT etc). This will continue to be supported via
the `runtime/<RID>` folder.

### Will the new TFMs simplify the project files too?

[@cartermp](https://github.com/cartermp) asked:

> Does specifying `net5.0-windows` obviate the current three things you need to
> specify?
>
> * netcoreapp3.x
> * Desktop SDK attribute
> * UseWindowsForms/UseWPF
>
> IOW are we going to have to support both formats moving forward? What about
> converting apps?

Generally, it does not. The idea I've heard is that all project types will be
unified to use `Sdk="Microsoft.NET.Sdk"` in order to make multi-targeting
easier. Customizations (for example, specific targets and references) would be brought
in via `UseXxx` properties, akin to how Windows Forms and WPF work in .NET Core
today. The reason is that in many cases the TFM alone isn't specific enough to
decide what kind of app are you building:

* `net5.0`. Is a class library/console app, an ASP.NET Core app, or a Blazor app?
* `net5.0-windows`. Are you building a Windows Forms app or a WPF app? Are you
  using both Windows Forms and WPF or just one?

The nice thing about properties is that they naturally compose. If certain
combinations aren't possible, they can be blocked relatively easily.

However, at this point it's still unclear whether the SDK unification will work
this way. One concern was that SDKs also bring in new item groups and might have
conflicting defaults for properties; this works today because the SDK can bring
in .props before the project file. When we rely on properties in the project
file, we need to bring those in the .targets (that is, the bottom of the project
file). While not impossible, this might force us to have knowledge in the base
SDK that can't be easily extended via optional components.
[@mhutch](https://github.com/mhutch) is working on a document specifically
around SDK convergence.

### Why is there no TFM for Linux?

The primary reason for OS specific TFMs is to vary API surface, not for varying
behavior. RIDs allow varying behavior and have support for various Linux
flavors. Specifically, TFMs aren't (primarily) meant to allow calling P/Invokes
under `#if`, most of the time that should be done by doing runtime checks or by
using RIDs. The primary reason for a TFM is to exclude large amounts of managed
representations for OS technologies (WinForms, WPF, Apple's NS APIs, Android
etc).

Also, Android, iOS, macOS, and Windows share that they offer a stable ABI so
that exchanging binaries makes sense. Linux is too generic of a concept for
that, it's basically just the kernel, which again boils down to the only thing
you can do is calling P/Invokes.

### Why is .NET 5.0's TFI still mapped to `.NETCoreApp`?

In MSBuild you can't easily do comparisons like:

```xml
<ItemGroup Condition="'$(TargetFramework)' >= 'net5.0'`">
```

because that would be a string comparison. Rather, you need to do a comparison
like this:

```xml
<ItemGroup Condition="'$(TargetFrameworkIdentifier)' == '.NETCoreApp' AND $([MSBuild]::VersionGreaterThanOrEquals($(TargetFrameworkVersion), '3.0'))">
```

And to check for things before a specific version do a comparison like this:

```xml
<ItemGroup Condition="'$(TargetFrameworkIdentifier)' == '.NETCoreApp' AND $([MSBuild]::VersionLessThan($(TargetFrameworkVersion), '3.0'))">
```

If we had used ``TargetFrameworkVersion`` instead of using the ``VersionLessThan`` and the ``VersionGreaterThanOrEquals`` msbuild functions we would see this error for both conditions:

``error MSB4086: A numeric comparison was attempted on "$(TargetFrameworkVersion)" that evaluates to "v3.0" instead of a number, in condition "'$(TargetFrameworkIdentifier)' == '.NETCoreApp' AND '$(TargetFrameworkVersion)' >= '3.0'".``
``error MSB4086: A numeric comparison was attempted on "$(TargetFrameworkVersion)" that evaluates to "v3.0" instead of a number, in condition "'$(TargetFrameworkIdentifier)' == '.NETCoreApp' AND '$(TargetFrameworkVersion)' &lt; '3.0'".``

By us mapping `net5.0` we break less of that code because existing code will
treat it correctly (i.e. as a future version of .NET Core) and also avoid
misclassification as .NET Framework.

[os-versioning]: https://microsoft.sharepoint.com/:w:/t/DotNetTeam/EavsPfFy7lFLh7eQrdMN8QwBl05cGLPwrSzJeT8vEu32mw?e=knNQ6W
[parsing-guidance]: https://github.com/dotnet/project-system/blob/master/docs/repo/coding-conventions.md#data
