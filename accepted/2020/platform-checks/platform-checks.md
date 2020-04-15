# Annotating platform-specific APIs and detecting its use

**PM** [Immo Landwerth](https://github.com/terrajobst)

With .NET Core, we've made cross-platform development a mainline experience in
.NET. Even more so for library authors that use [.NET 5][net5-tfms]. We
generally strive to make it very easy to build code that works well across all
platforms. Hence, we avoid promoting technologies and APIs that only work on one
platform by not putting platform-specific APIs in the default set that is always
referenced. Rather, we're exposing them via dedicated packages that the
developer has to reference manually.

Unfortunately, this approach doesn't address all concerns:

* **It's hard to reason about transitive platform-specific dependencies**. A
  developer doesn't necessarily know that they took a dependency on a component
  that makes their code no longer work across all platforms. For example, in
  principle, a package that sounds general purpose could depend on something
  platform-specific that now transitively ties the consuming application to a
  single platform (for example, an IOC container that depends on the Windows
  registry). Equally, transitively depending on a platform-specific package
  doesn't mean that the application doesn't work across all platforms -- the
  library might actually guard the call so that it can provide a slightly better
  experience on a particular platform.

* **We need to stay compatible with existing APIs**. Ideally, types that provide
  cross-platform functionality shouldn't offer members that only work on
  specific platforms. Unfortunately, the .NET platform is two decades old and
  was originally designed to be a great development platform for Windows. This
  resulted in Windows-specific APIs being added to types that are otherwise
  cross-platform. A good example are file system and threading types that have
  methods for setting the Windows access control lists (ACL). While we could
  provide them elsewhere, this would unnecessarily break a lot of code that
  builds Windows-only applications, e.g. WinForms and WPF apps.

* **OS platforms are typically targeted using the latest SDK**. Even when the
  project is platform specific, such as `net5.0-ios` or `net5.0-android`, it's
  not necessarily clear that the called API is actually available. For .NET
  Framework, we used *reference assemblies* which provides a different set of
  assemblies for every version. However, for OS bindings this approach is not
  feasible. Instead, applications are generally compiled against the latest
  version of the SDK with the expectations that callers have to check the OS
  version if they want to run on older versions. See [Minimum OS
  Version][os-minimum-version] for more details.

.NET has always taken the position that providing platform-specific
functionality isn't a bug but a feature because it empowers developers to build
applications that feel native for the user. That's why .NET has rich interop
facilities such as P/Invoke.

At the same time, developers really dislike the notion of "write once and test
everywhere" because it results in a delayed feedback loop.

Fortunately .NET has also taken the position that developer productivity is key.
This document proposes:

1. A mechanism to annotate APIs as being platform-specific, including the
   version they got introduced in, the version they got removed in, and the
   version they got obsoleted in.

2. A Roslyn analyzer that informs developers when they use platform-specific
   APIs from call sites where the API might not be available.

This work draws heavily form the experience of the [API Analyzer] and improves
on it by making it a first-class platform feature.

## Scenarios and User Experience

### Lighting up on later versions of the OS

Miguel is building Baby Shark, a popular iOS application. He started with .NET
that supported iOS 13 but Apple just released iOS 14, which adds the great
`NSFizBuzz` API that gives his app the extra pop.

After upgrading the app and writing the code, Miguel decides that while the
feature is cool, it doesn't warrant cutting of all his customers who are
currently still on iOS 13. So he edits the project file and sets
`TargetPlatformMinVersion` to 13.

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net5.0-ios14.0</TargetFramework>
    <TargetPlatformMinVersion>13.0</TargetPlatformMinVersion>
  </PropertyGroup>

  ...

</Project>
```

After doing this, he gets a diagnostic in this code:

> 'NSFizzBuff' requires iOS 14 or later.

```C#
private static void ProvideExtraPop()
{
    NSFizzBuff();
}
```

He invokes the Roslyn code fixer which suggests to guard the call. This results
in the following code:

```C#
private static void ProvideExtraPop()
{
    if (RuntimeInformation.IsOSPlatformOrLater(OSPlatform.iOS, 14))
        NSFizzBuff();
}
```

### Detecting obsoletion of OS APIs

After upgrading to iOS 14 Miguel also got the following diagnostic:

> 'UIApplicationExitsOnSuspend' has been deprecated since iOS 13.1

Miguel decides that he'll tackle that problem later as he needs to ship now,so
he invokes the generic "Suppress in code" code fixer which surrounds his code
with `#pragma warning disable`.

### Detecting removal of OS APIs

A few year after his massively successful fizz-buzzed Baby Shark, Apple releases
iOS 15. After upgrading Miguel gets the following diagnostic:

> 'UIApplicationExitsOnSuspend' has been removed since iOS 15.0

Doh! After reading more details, he realizes that he can just stop calling this
API because Apple automatically suspends apps leaving the foreground when they
don't require background execution. So he deletes the code that calls the API.

### Finding usage of platform-specific APIs

Alejandra is working at Fabrikam, where she's tasked with porting their asset
management system to .NET 5. The application consists of several web services, a
desktop application, and a set of libraries that are shared.

She begins by porting the core business logic libraries to .NET 5. After
porting, she's getting a diagnostic in the code below:

> 'DirectorySecurity' is only supported on 'Windows'

```C#
private static string CreateLoggingDirectory()
{
    var appDataDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    var loggingDirectory = Path.Combine(appDataDirectory, "Fabrikam", "AssetManagement", "Logging");

    // Restrict access to the logging directory

    var rules = new DirectorySecurity();
    rules.AddAccessRule(
        new FileSystemAccessRule(@"fabrikam\log-readers",
                                 FileSystemRights.Read,
                                 AccessControlType.Allow)
    );
    rules.AddAccessRule(
        new FileSystemAccessRule(@"fabrikam\log-writers",
                                 FileSystemRights.FullControl,
                                 AccessControlType.Allow)
    );

    Directory.CreateDirectory(loggingDirectory, rules);

    return loggingDirectory;
}
```

### Add platform-specific code without becoming platform-specific

After reviewing the code she realizes that this code only makes sense for cases
where the application is installed on Windows. When running on iOS and Android,
the machine isn't in shared environment so restricting the log access doesn't
make much sense. She modifies the code accordingly:

```C#
private static string GetLoggingPath()
{
    var appDataDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    var loggingDirectory = Path.Combine(appDataDirectory, "Fabrikam", "AssetManagement", "Logging");

    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        // Just create the directory
        Directory.CreateDirectory(loggingDirectory);
    }
    else
    {
        // Create the directory and restrict access using Windows
        // Access Control Lists (ACLs).

        var rules = new DirectorySecurity();
        rules.AddAccessRule(
            new FileSystemAccessRule(@"fabrikam\log-readers",
                                     FileSystemRights.Read,
                                     AccessControlType.Allow)
        );
        rules.AddAccessRule(
            new FileSystemAccessRule(@"fabrikam\log-writers",
                                     FileSystemRights.FullControl,
                                     AccessControlType.Allow)
        );

        Directory.CreateDirectory(loggingDirectory, rules);
    }

    return loggingDirectory;
}
```

After that, the diagnostic disappears automatically.

## Requirements

### Goals

* Developers get diagnostics when they inadvertently use platform-specific APIs.
    - All diagnostics are provided on the command line as well as in the IDE
    - The developer doesn't have to resort to explicit suppressions to indicate
      intent. Idiomatic gestures, such as guarding the call with a platform
      check or marking a method as platform-specific, automatically suppress
      these diagnostics. However, explicit suppressions via the usual means
      (`NoWarn`, `#pragma`) will also work.
* The analyzer helps library authors to express their intent so that their
  consumers can also benefit from these diagnostics.
    - Library authors can annotate their platform-specific APIs without taking a
      dependency.
    - Library consumers don't have to install a NuGet package to get diagnostics
      when using platform-specific APIs
* The analyzer must be satisfy the quality bar in order to be turned on by
  default
    - No false positives, low noise.

### Non-Goals

* Modelling partially portable APIs (that is APIs are applicable to more than
  one platform but not all platforms). Those should be modeled as capability
  APIs, as as `RuntimeInformation.IsDynamicCodeSupported` for ref emit. This
  would be a separate analyzer.
* Shipping the platform-specific analyzer and/or annotations out-of-band.

## Design

### Attribute

OS bindings and platforms-specific BCL APIs will be annotated using a set of
custom attributes:

```C#
namespace System.Runtime.InteropServices
{
    public partial struct OSPlatform
    {
        // Existing properties
        // public static OSPlatform FreeBSD { get; }
        // public static OSPlatform Linux { get; }
        // public static OSPlatform OSX { get; }
        // public static OSPlatform Windows { get; }
        public static OSPlatform Android { get; }
        public static OSPlatform iOS { get; }
        public static OSPlatform macOS { get; }
        public static OSPlatform tvOS { get; }
        public static OSPlatform watchOS { get; }
    }

    public partial static class RuntimeInformation
    {
        // Existing API
        // public static bool IsOSPlatform(OSPlatform osPlatform);

        // Check for the OS with a >= version comparison
        // Used to guard APIs that were added in the given OS release.
        public static bool IsOSPlatformOrLater(OSPlatform osPlatform, int major);
        public static bool IsOSPlatformOrLater(OSPlatform osPlatform, int major, int minor);
        public static bool IsOSPlatformOrLater(OSPlatform osPlatform, int major, int minor, int build);
        public static bool IsOSPlatformOrLater(OSPlatform osPlatform, int major, int minor, int build, int revision);

        // Allows checking for the OS with a < version comparison
        // Used to guard APIs that were obsoleted or removed in the given OS release.
        public static bool IsOSPlatformEarlierThan(OSPlatform osPlatform, int major);
        public static bool IsOSPlatformEarlierThan(OSPlatform osPlatform, int major, int minor);
        public static bool IsOSPlatformEarlierThan(OSPlatform osPlatform, int major, int minor, int build);
        public static bool IsOSPlatformEarlierThan(OSPlatform osPlatform, int major, int minor, int build, int revision);
    }
}

namespace System.Runtime.Versioning
{
    public abstract class OSPlatformVersionAttribute  : Attribute
    {
        protected OSPlatformVersionAttribute (string osPlatform,
                                              int major,
                                              int minor,
                                              int build,
                                              int revision);
        public string PlatformIdentifier { get; }
        public int Major { get; }
        public int Minor { get; }
        public int Build { get; }
        public int Revision { get; }
    }

    [AttributeUsage(AttributeTargets.Assembly |
                    AttributeTargets.Class |
                    AttributeTargets.Constructor |
                    AttributeTargets.Event |
                    AttributeTargets.Method |
                    AttributeTargets.Module |
                    AttributeTargets.Property |
                    AttributeTargets.Struct,
                    AllowMultiple=false, Inherited=true)]
    public sealed class AddedInOSPlatformVersionAttribute  : OSPlatformVersionAttribute
    {
        public AddedInOSPlatformVersionAttribute (string osPlatform,
                                          int major,
                                          int minor,
                                          int build,
                                          int revision);
    }

    [AttributeUsage(AttributeTargets.Assembly |
                    AttributeTargets.Class |
                    AttributeTargets.Constructor |
                    AttributeTargets.Event |
                    AttributeTargets.Method |
                    AttributeTargets.Module |
                    AttributeTargets.Property |
                    AttributeTargets.Struct,
                    AllowMultiple=false, Inherited=true)]
    public sealed class RemovedInOSPlatformVersionAttribute  : OSPlatformVersionAttribute
    {
        public RemovedInOSPlatformVersionAttribute (string osPlatform,
                                            int major,
                                            int minor,
                                            int build,
                                            int revision);
    }

    [AttributeUsage(AttributeTargets.Assembly |
                    AttributeTargets.Class |
                    AttributeTargets.Constructor |
                    AttributeTargets.Event |
                    AttributeTargets.Method |
                    AttributeTargets.Module |
                    AttributeTargets.Property |
                    AttributeTargets.Struct,
                    AllowMultiple=false, Inherited=true)]
    public sealed class ObsoletedInOSPlatformVersionAttribute  : OSPlatformVersionAttribute
    {
        public ObsoletedInOSPlatformVersionAttribute (string osPlatform,
                                              int major,
                                              int minor,
                                              int build,
                                              int revision);
        public string Url { get; set; }
    }
}
```

### Platform context

To determine the platform context of the call site the analyzer must consider
the following two configurations:

1. **Call-site**. Application of `AddedInOSPlatformVersionAttribute` to the containing
   member, type, module, or assembly.

2. **Project** The project TFMs and `TargetPlatformMinVersion`. If the project
   has an platform-specific TFM (such as `net5.0-ios13.0`) we consider it to be
   on the specified platform. If `TargetPlatformMinVersion` is set, we assume
   that version, otherwise we assume the version as specified by the TFM.

If (2) is platform neutral (e.g. `net5.0`), the platform context is entirely
determined by (1).

If (2) is platform-specific it is combined with (1):

* If the platforms between (1) and (2) are disjoint (such as iOS and Windows),
  the information from (1) is discarded.
* Otherwise the platform context's minimum version is the maximum of the
  versions determined by (1) and (2).

### Diagnostics

The analyzer can produce three diagnostics:

1. '{API}' requires {OS} {OS version} or later.
2. '{API}' has been deprecated since {OS} {OS version}.
3. '{API}' has been removed since {OS} {OS version}.

### Automatic suppression via platform guards

Diagnostic (1) can be suppressed via a call to
`RuntimeInformation.IsOSPlatformOrLater()`. This should work for for simple cases
where the call is contained inside an `if` check but also when the check causes
control flow to never reach the call:

```C#
public void Case1()
{
    if (RuntimeInformation.IsOSPlatformOrLater(OSPlatform.iOS, 13))
        AppleApi();
}

public void Case2()
{
    if (!RuntimeInformation.IsOSPlatformOrLater(OSPlatform.iOS, 13))
        return;

    AppleApi();
}
```

Diagnostics (2) and (3) are analogous except the guard is provided by
`RuntimeInformation.IsOSPlatformEarlierThan()`.

### Code Fixers

* Suggest wrapping statement in platform guard
* Suggest annotating the method with an attribute

All diagnostics can be suppressed by surround the call site using a platform
guard. The fixer should offer surrounding the call with an `if` check using the
appropriate platform-guard. It's not necessary that the end result compiles. It
is the user's responsibility to adjust control flow and data flow to ensure
variables are declared and initialized appropriately. The value that the fixer
provides here is educational and avoids having to lookup the correct version
numbers.

For diagnostic (1) we should also offer a fixer that allows it to be marked as
platform specific by applying `AddedInOSPlatformVersionAttribute`. If the
attribute is already applied, it should offer to change the platform and
version.

## Q & A

### How does this relate to platform-compat/API Analyzer?

We have an existing project [dotnet/platform-compat], available as the
[Microsoft.DotNet.Analyzers.Compatibility] NuGet package, but there are several drawbacks:

* The analyzer isn't on by default
* It carries a database of platform-specific framework APIs, which doesn't scale
  to 3rd parties
* Suppression mechanism is somewhat lacking

This is intended to become the productized version of
[Microsoft.DotNet.Analyzers.Compatibility].

### What set of version numbers are you going to use?

Specifically, are we going to use the "marketing" version numbers or what the
system returns as version numbers?

This is related to the problem that some operating systems, such as Windows,
have quirked the OS API to lie to the application in order to make it compatible
(because way too often apps get version checks wrong).

The version is part of the reason why we don't expose a version number but
instead perform the checking on behalf of the user. In general, the
implementation will do whatever it needs to get the real version number.

As far as the reference assembly is concerned, the expectation is that the
bindings will be annotated with the version information as documented by the
native operating system SDK.

### What about `Environment.OSVersion`?

Should the analyzer consider checks again `Environment.OSVersion`?

No. In fact, we should obsolete `Environment.OSVersion`. Two reasons:

1. `Environment.OSVersion` doesn't return the correct version number today (it's
   shimmed). We could change but that would just further the lifetime of a
   broken API. Instead, we should push folks to
   `RuntimeInformation.IsOSPlatformOrLater()`.
2. It makes the analyzer more complex and more prone to false positives

[dotnet/platform-compat]: https://github.com/dotnet/platform-compat
[API Analyzer]: https://devblogs.microsoft.com/dotnet/introducing-api-analyzer/
[Microsoft.DotNet.Analyzers.Compatibility]: https://www.nuget.org/packages/Microsoft.DotNet.Analyzers.Compatibility
[net5-tfms]: https://github.com/dotnet/designs/blob/master/accepted/2020/net5/net5.md
[os-minimum-version]: https://github.com/dotnet/designs/pull/97
