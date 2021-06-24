# Annotating APIs as unsupported on specific platforms

**Owner** [Immo Landwerth](https://github.com/terrajobst)

For .NET 5, we're building a feature which [annotates platform-specific
APIs][platform-checks]. The goal is to provide warnings when developers
accidentally call platform-specific APIs, thus limiting their ability to run on
multiple platforms, or if the code might run on earlier platform that doesn't
support the called API yet.

This feature made the following assumption:

* An unmarked API is considered to work on all OS platforms.
* An API marked with `[SupportedOSPlatform("...")]` is considered only portable
  to the specified OS platforms (please note that the attribute can be applied
  multiple times).

This approach works well for cases where the API is really calling an
OS-specific API. Good examples include the Windows Registry or Apple-specific
graphic APIs. This is very different from browser support for specific HTML and
CSS features: those generally aren't browser specific, which is why browser
checks never worked well for the web. But for OS specific APIs, asking whether
you're actually running on that OS is very workable. The only fragile aspect
here is getting the version number right, but the analyzer enforces that.

However, this approach doesn't work well for APIs that represent concepts that
aren't OS specific, such as threading or I/O. This is a problem for OS platforms
that are more constrained, such as Blazor WebAssembly, which runs in a browser
sandbox and thus can't freely create threads or open random files on disk.

One could, in principle, apply the `[SupportedOSPlatform("...")]` attribute for
all other OS platforms that do support them, but this has several downsides:

1. The so marked APIs are now considered platform-specific, which means that
   just calling the API from portable code will now always be flagged by the
   analyzer.
2. Every time .NET adds another operating system all call sites guarding calls
   to these APIs now also have to check for this new OS.

In this spec, we're proposing a mechanism by which we can mark APIs as
unsupported.

## Scenarios and User Experience

### Building a Blazor WebAssembly app

Abelina is building a Blazor WebAssembly app. She's copy & pasting some code
from here ASP.NET Core web app that calls into an LDAP service using
`System.DirectoryServices.Protocols`.

When doing so, she receives the following warning:

> "LdapConnection" isn't supported for platform "browser"

After doing some further reading Abelina decided to put the LDAP functionality
behind a web API that she calls from her Blazor WebAssembly app.

### Building a class library

Darrell works with Abelina and is tasked with moving some of the LDAP
functionality into a .NET Standard class library so that they can share the code
between their .NET Framework desktop app and their ASP.NET Core web API.

When he compiles the class library he gets no warnings about unsupported Blazor
APIs.

### Building a class library for use in Blazor WebAssembly

Abelina and Darrel decide to centralize some of the functionality they use for
Razor views, specifically around URI and string processing.

When consuming this library from Blazor WebAssembly they observe a
`PlatformNotSupported` exception. Upon further analysis, it seems they had a
dependency on `System.Drawing` that they weren't aware off. To avoid this, they
decide to enable the analyzer to check for APIs that are unsupported when
running inside a browser:

```xml
<ItemGroup>
    <SupportedPlatform Include="browser" />
</ItemGroup>
```

## Requirements

### Goals

* Be able to mark APIs that are unsupported on a particular OS
* Allows platforms to be considered supported by default with the ability to add
  or remove platforms from the project file and SDK.
* By default, Blazor WebAssembly is only considered supported when building a
  Blazor WebAssembly app. For regular class libraries (`netstandard` or
  `net5.0`) the default assumes that it's not supported and thus won't generate
  any warnings. However, the developer must still be able to include Blazor
  WebAssembly manually.
* The annotations for unsupported APIs can express that an API was only
  unsupported for a specific version.

### Non-Goals

* Allowing to express that APIs are unsupported in specific versions of the .NET
  platform.

## Design

### Attribute

The spec for [platform-checks] propose a set of attributes, specifically:

* `SupportedOSPlatform`
* `ObsoletedInOSPlatform`
* `UnsupportedOSPlatform`

The semantics of these attributes are updated to become as follows:

* An API that doesn't have any of these attributes is considered supported by
  all platforms.
* If either `[SupportedOSPlatform]` or `[UnsupportedOSPlatform]` attributes are
  present, we group all attributes by OS platform identifier:
    - **Allow list**. If the lowest version for each OS platform is a
      `[SupportedOSPlatform]` attribute, the API is considered to *only* be
      supported by the listed platforms and unsupported by all other platforms.
    - **Deny list**. If the lowest version for each OS platform is a
      `[UnsupportedOSPlatform]` attribute, then the API is considered to *only*
      be unsupported by the listed platforms and supported by all other
      platforms.
    - **Inconsistent list**. If for some platforms the lowest version attribute
      is `[SupportedOSPlatform]` while for others it is
      `[UnsupportedOSPlatform]`, the analyzer will produce a warning on the API
      definition because the API is attributed inconsistently.
* Both attributes can be instantiated without version numbers. This means the
  version number is assumed to be `0.0`. This simplifies guard clauses, see
  examples below for more details.
* `[ObsoletedInOSPlatform]` continuous to require a version number.
* `[ObsoletedInOSPlatform]` by itself doesn't imply support. However, it doesn't
  make sense to apply `[ObsoletedInOSPlatform]` unless that platform is
  supported.

These semantics result in intuitive attribute applications with flexible rules.

Let's consider the example where in .NET 5 we mark an API as being unsupported
in `Windows`. This will look as follows:

```C#
[UnsupportedOSPlatform("windows")]
public void DoesNotWorkOnWindows();
```

Now let's say that in .NET 6 we made the API work in Windows, but only on
Windows `10.0.18362` (aka Windows 10 May 2019 Update). We'd update the API as follows:

```C#
[UnsupportedOSPlatform("windows")]
[SupportedOSPlatform("windows10.0.18362")]
public void DoesNotWorkOnWindows();
```

The presence of `SupportedOSPlatform` still makes no claim for other platforms
because the lowest version for Windows says the API is unsupported for Windows.

Similar to [platform-checks] this model still allows for OS vendors to obsolete
and remove APIs:

```C#
[UnsupportedOSPlatform("windows")]
[SupportedOSPlatform("windows10.0.18362")]
[ObsoletedInOSPlatform("windows10.0.18363")]
[UnsupportedOSPlatform("windows10.0.19041")]
public void DoesNotWorkOnWindows();
```

This describes an API that is supported by all platforms, except for Windows.
And on Windows, it's supported for Windows 10 18362 and 18363, but it was
obsoleted in 18363 and then became unsupported again in 19041.

Platform-specific APIs like the iOS and Android bindings are still expressible
the same way:

```C#
[SupportedOSPlatform("ios12.0")]
[ObsoletedInOSPlatform("ios13.0")]
[UnsupportedOSPlatform("ios14.0")]
[SupportedOSPlatform("ipados13.0")]
public void OnlyWorksOniOS();
```

This API is considered to only work on iOS and iPadOS because the lowest version
starts with `[SupportedOSPlatform]`.

### Analyzer behavior

The existing analyzer that handles `[SupportedOSPlatform]` and
`[UnsupportedOSPlatform]` will be modified to handle following the semantics as
outlined above.

We'll change the analyzer to consider these guard clauses as equivalent:

```C#
if (RuntimeInformation.IsPlatform(OSPlatform.Windows))
{
    WindowsSpecificApi();
}

if (OperatingSystem.IsOSPlatform("windows"))
{
    WindowsSpecificApi();
}

if (OperatingSystem.IsWindows())
{
    WindowsSpecificApi();
}

if (OperatingSystem.IsWindowsVersionAtLeast(0))
{
    WindowsSpecificApi();
}
```

The primary benefit for doing this is to support all the code that was written
since .NET Standard introduced the OS check APIs, which people used primarily to
guard Windows-specific APIs which are part of the otherwise OS-neutral
`netstandard` and `net5.0` TFMs.

Let's look at a few examples of annotations and what the corresponding guard
clauses are that would prevent the analyzer from flagging usage of the API.

```C#
// The API is supported everywhere except when running in a browser
[UnsupportedOSPlatform("browser")]
public extern void Api();

public void Api_Usage()
{
    if (!OperatingSystem.IsBrowser())
    {
        Api();
    }
}
```

```C#
// On Windows, the API was unsupported until version 10.0.19041.
// The API is considered supported everywhere else without constraints.
[UnsupportedOSPlatform("windows")]
[SupportedOSPlatform("windows10.0.19041")]
public extern void Api();

public void Api_Usage()
{
    if (!OperatingSystem.IsWindows() ||
         OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
    {
        Api();
    }
}
```

```C#
// The API is only supported on Windows. It doesn't say which version, which
// means it effectively says since dawn of time. We'll use this to annotate
// Windows-specific APIs that will work on any version that can run .NET.
[SupportedOSPlatform("windows")]
public extern void Api();

public void Api_Usage()
{
    if (OperatingSystem.IsWindows())
    {
        Api();
    }
}
```

```C#
// The API is only supported on iOS. There, it started to be supported in
// version 12 and stopped being supported in version 14.
[SupportedOSPlatform("ios12.0")]
[UnsupportedOSPlatform("ios14.0")]
public extern void Api();

public void Api_Usage()
{
    if (OperatingSystem.IsIOSVersionAtLeast("ios12.0") &&
       !OperatingSystem.IsIOSVersionAtLeast("ios14.0"))
    {
        Api();
    }
}
```

### Build configuration for platforms

In order to indicate which platforms the analyzer should warn about, we're
adding some metadata to MSBuild:

```XML
<ItemGroup>
    <SupportedPlatform Include="android" />
    <SupportedPlatform Include="ios" />
    <SupportedPlatform Include="linux" />
    <SupportedPlatform Include="macos" />
    <SupportedPlatform Include="windows" />
</ItemGroup>
```

The targets of the Blazor WebAssembly SDK would initialize this as follows:

```XML
<ItemGroup>
    <SupportedPlatform Remove="@(SupportedPlatform)" />
    <SupportedPlatform Include="browser" />
</ItemGroup>
```

These items needs to be passed to the invocation CSC as analyzer configuration.
AFAIK the only supported properties today, but we can have a target that
converts these items into a semicolon separated list of platforms.

Using items instead of a property makes it easier for the developer to add or
remove from the set:

When building a class library that is supposed to also work in the browser, a
developer can add the following to their project file:

```XML
<ItemGroup>
    <SupportedPlatform Include="browser" />
</ItemGroup>
```

This design also enables library developers to suppress warnings for platforms
that are normally supported by default:

```XML
<ItemGroup>
    <SupportedPlatform Remove="macos" />
</ItemGroup>
```

[platform-checks]: ../platform-checks/platform-checks.md
