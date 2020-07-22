# Annotating APIs as unsupported on specific platforms

**PM** [Immo Landwerth](https://github.com/terrajobst)

For .NET 5, we're building a feature which [annotates platform-specific
APIs][platform-checks]. The goal is to provide warnings when developers
accidentally call platform-specific APIs, thus limiting their ability to run on
multiple platforms, or if the code might run on earlier platform that doesn't
support the called API yet.

This feature made the following assumption:

* An unmarked API is considered to work on all OS platforms.
* An API marked with `[MinimumOSPlatform("...")]` is considered only portable to
  the specified OS platforms (please note that the attribute can be applied
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

One could, in principle, apply the `[MinimumOSPlatform("...")]` attribute for
all other OS platforms that do support them, but this has several downsides:

1. The so marked APIs are now considered platform-specific, which means that
   just calling the API from portable code will now always be flagged by the
   analyzer.
2. Every time .NET adds another operating system all call sites guarding calls
   to these APIs now also have to check for this new OS.

In this spec, we're proposing a mechanism by which we can mark APIs as
unsupported.

## Scenarios and User Experience

* Building a Blazor WebAssembly app
    - Using an API that is unsupported will be flagged by the analyzer.
* Building a class library
    - Building a regular class library (targeting `netstandard` or `net5.0`)
      will not complain about APIs that are unsupported in Blazor.
* Building a class library for use in Blazor WebAssembly
    - Developer can modify the project file to explicitly indicate that they
      want to see issues with APIs that are unsupported in Blazor.

## Requirements

### Goals

* Be able to mark APIs that are unsupported on a particular OS
* Allows platforms to be considered supported by default with the ability to add
  or remove platforms from the project file and SDK.
* Blazor WebAssembly is only considered supported when building a Blazor
  WebAssembly app. For regular class libraries (`netstandard` or `net5.0`) the
  default assumes that it's not supported and thus won't generate any warnings.
  However, the developer must still be able to manually include Blazor
  WebAssembly manually.
* The annotations for unsupported APIs can express that an API was only
  unsupported for a specific version.

### Non-Goals

## Design

### Attribute

```C#
namespace System.Runtime.Versioning
{
    // Rename RemovedInOSPlatformAttribute to
    // UnsupportedOSPlatformAttribute
    [AttributeUsage(AttributeTargets.Assembly |
                    AttributeTargets.Class |
                    AttributeTargets.Constructor |
                    AttributeTargets.Event |
                    AttributeTargets.Method |
                    AttributeTargets.Module |
                    AttributeTargets.Property |
                    AttributeTargets.Struct,
                    AllowMultiple=true, Inherited=false)]
    public sealed class UnsupportedOSPlatformAttribute : OSPlatformAttribute
    {
        public UnsupportedOSPlatformAttribute(string platformName);
    }
```

The semantics of this attribute are as follows:

* Marks an assembly, module, or API as unsupported on a given OS platform.
* The `platformName` can contain no version number, a single version number, or
  a range:
    - `browser`. Marks the API as unsupported on all version of `browser`. This
      will be used for APIs that can't be supported on a particular OS platform.
    - `ios14.0`. Marks the API as unsupported starting with iOS 14.0. This will
      be used for OS platform APIs that were removed/disabled.
    - `windows0.0-10.0.19041`. Marks the API as unsupported for 10.0.19041 and
      earlier. This will be used for APIs that were unsupported before but can
      be supported starting with a later version of the OS. This is different
      from applying `[MinimumOSPlatform("windows10.0.19041")]` in that the
      presence of this attribute doesn't cause the API to be considered
      unsupported on other platforms.

### Analyzer behavior

The existing analyzer that handles `[MinimumOSPlatform]` will be modified to
handle `[UnsupportedOSPlatform]` (previously named `[RemovedInOSPlatform]`)
with the semantics listed above.

Let's look at a few examples of annotations what the corresponding guard clauses
are that would prevent the analyzer from flagging usage of the API.

```C#
// The API is supported everywhere except when running in a browser
[UnsupportedOSPlatform("browser")]
public extern void Api();

public void Api_Usage()
{
    if (!RuntimeInformation.IsOSPlatform("browser"))
    {
        Api();
    }
}
```

```C#
// On Windows, the API was unsupported up to and including version 10.0.19041.
// The API is considered supported everywhere else without constraints.
[UnsupportedOSPlatform("windows0.0-10.0.19041")]
public extern void Api();

public void Api_Usage()
{
    if (!RuntimeInformation.IsOSPlatformOrEarlier("windows10.0.19041"))
    {
        Api();
    }
}
```

```C#
// The API is only supported on iOS. There, it started to be supported in
// version 12.0 and stopped being supported in version 14.
[MinimumOSPlatform("iOS12.0")]
[UnsupportedOSPlatform("ios14.0")]
public extern void Api();

public void Api_Usage()
{
    if (RuntimeInformation.IsOSPlatformOrLater("iOS12.0") &&
        RuntimeInformation.IsOSPlatformOrEarlier("ios14.0"))
    {
        Api();
    }
}
```

### Hardcoded APIs in the analyzer

### Build configuration for platforms

In order to indicate which platforms the analyzer should by warn about, we're
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

These items needs to be passed to invocation of CSC the analyzer configuration.
AFAIK they only supported properties today, but we can have a target that
converts these items into a semicolon separated list of platforms.

Using items instead of a property makes it easier for the developer to add or
remove from the set:

When building a class library that is also supposed to also work in Blazor
WebAssembly, a developer can add the following to their project file:

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

[platform-checks]: ../platform-check/platform-checks.md
