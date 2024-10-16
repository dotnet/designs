# Annotating platform-specific APIs and detecting its use

**Owner** [Immo Landwerth](https://github.com/terrajobst)

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

This work draws heavily from the experience of the [API Analyzer] and improves
on it by making it a first-class platform feature.

## Scenarios and User Experience

### Lighting up on later versions of the OS

Miguel is building Baby Shark, a popular iOS application. He started with .NET
that supported iOS 13 but Apple just released iOS 14, which adds the great
`NSFizzBuzz` API that gives his app the extra pop.

After upgrading the app and writing the code, Miguel decides that while the
feature is cool, it doesn't warrant cutting of all his customers who are
currently still on iOS 13. So he edits the project file and sets
`SupportedOSPlatformVersion` to 13.

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net5.0-ios14.0</TargetFramework>
    <SupportedOSPlatformVersion>13.0</SupportedOSPlatformVersion>
  </PropertyGroup>

  ...

</Project>
```

After doing this, he gets a diagnostic in this code:

> 'NSFizzBuzz' requires iOS 14 or later.

```C#
private static void ProvideExtraPop()
{
    NSFizzBuzz();
}
```

He invokes the Roslyn code fixer which suggests to guard the call. This results
in the following code:

```C#
private static void ProvideExtraPop()
{
    if (OperatingSystem.IsIOSVersionAtLeast(14))
        NSFizzBuzz();
}
```

### Detecting obsoletion of OS APIs

After upgrading to iOS 14 Miguel also got the following diagnostic:

> 'UIApplicationExitsOnSuspend' has been deprecated since iOS 13.1

Miguel decides that he'll tackle that problem later as he needs to ship now, so
he invokes the generic "Suppress in code" code fixer which surrounds his code
with `#pragma warning disable`.

### Detecting removal of OS APIs

A few years after his massively successful fizz-buzzed Baby Shark, Apple releases
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

    if (!OperatingSystem.IsWindows())
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
  APIs, as as `RuntimeFeature.IsDynamicCodeSupported` for ref emit. This
  would be a separate analyzer.
* Shipping the platform-specific analyzer and/or annotations out-of-band.

## Design

### Attributes

OS bindings and platforms-specific BCL APIs will be annotated using a set of
custom attributes:

```C#
namespace System.Runtime.Versioning
{
    // Base type for all platform-specific attributes. Primarily used to allow grouping
    // in documentation.
    public abstract class OSPlatformAttribute : Attribute
    {
        private protected OSPlatformAttribute(string platformName);
        public string PlatformName { get; }
    }

    // Records the platform that the project targeted.
    [AttributeUsage(AttributeTargets.Assembly,
                    AllowMultiple=false, Inherited=false)]
    public sealed class TargetPlatformAttribute : OSPlatformAttribute
    {
        public TargetPlatformAttribute(string platformName);
    }

    // Records the minimum platform that is required in order to use the marked thing.
    //
    // * When applied to an assembly, it means the entire assembly cannot be called
    //   into on earlier versions. It records the SupportedOSPlatformVersion property.
    //
    // * When applied to an API, it means the API cannot be called from an earlier
    //   version.
    //
    // In either case, the caller can either mark itself with SupportedOSPlatformAttribute
    // or guard the call with a platform check.
    //
    // The attribute can be applied multiple times for different operating systems.
    // That means the API is supported on multiple operating systems.
    //
    // A given platform should only be specified once.

    [AttributeUsage(AttributeTargets.Assembly |
                    AttributeTargets.Class |
                    AttributeTargets.Constructor |
                    AttributeTargets.Enum |
                    AttributeTargets.Event |
                    AttributeTargets.Field |
                    AttributeTargets.Method |
                    AttributeTargets.Module |
                    AttributeTargets.Property |
                    AttributeTargets.Struct,
                    AllowMultiple = true, Inherited = false)]
    public sealed class SupportedOSPlatformAttribute : OSPlatformAttribute
    {
        public SupportedOSPlatformAttribute(string platformName);
    }
  
    // Marks APIs that were removed or are unsupported in a given OS version.
    //
    // Used by OS bindings to indicate APIs that are only available in
    // earlier versions.
    [AttributeUsage(AttributeTargets.Assembly |
                    AttributeTargets.Class |
                    AttributeTargets.Constructor |
                    AttributeTargets.Enum |
                    AttributeTargets.Event |
                    AttributeTargets.Field |
                    AttributeTargets.Method |
                    AttributeTargets.Module |
                    AttributeTargets.Property |
                    AttributeTargets.Struct,
                    AllowMultiple = true, Inherited = false)]
    public sealed class UnsupportedOSPlatformAttribute : OSPlatformAttribute
    {
        public UnsupportedOSPlatformAttribute(string platformName);
    }

    // Marks APIs that were obsoleted in a given operating system version.
    //
    // Primarily used by OS bindings to indicate APIs that should only be used in
    // earlier versions.
    [AttributeUsage(AttributeTargets.Assembly |
                    AttributeTargets.Class |
                    AttributeTargets.Constructor |
                    AttributeTargets.Event |
                    AttributeTargets.Method |
                    AttributeTargets.Module |
                    AttributeTargets.Property |
                    AttributeTargets.Struct,
                    AllowMultiple=true, Inherited=false)]
    public sealed class ObsoletedInOSPlatformAttribute : OSPlatformAttribute
    {
        public ObsoletedInPlatformAttribute(string platformName);
        public ObsoletedInPlatformAttribute(string platformName, string message);
        public string Message { get; }
        public string Url { get; set; }
    }
}
```

### Platform guards

In .NET Core 1.x and 2.x days, we added very limited ability for customers to
check which OS they were on. We originally didn't even expose
`Environment.OSVersion` and we built a new API
`RuntimeInformation.IsOSPlatform()`. The assumption was that doing OS checks is
exclusively done for P/invoke or interoperating with native code, which is why
`RuntimeInformation` was put in `System.Runtime.InteropServices`.

However, guarding calls to OS bindings or avoiding unsupported APIs in Blazor
isn't really a P/invoke scenario. It's seems counterproductive to force these
customers to import the `System.Runtime.InteropServices` namespace.

However, we do like the pattern of the `RuntimeInformation.IsOSPlatform()` API
int that it answers the question rather than indicating what it is. This design
allows for having both, very specific OS platforms, such as *Ubuntu* or classes
of platforms, such as *Linux* because the API can return `true` for both
`OSPlatform.Linux` and `OSPlatform.Ubuntu`.

To check for specific version, we're introducing analogous APIs where the caller
supplies the platform and version information and the API will perform the
check. We'll allow both string-based checks as well as checks for well-known
operating systems to aid in code completion.

**Note:** While the analyzer will support the generalized string-based versions
it assumes that the provided values are either constant or are variable that are
initialized in the same method.

```C#
namespace System
{
    // This type already exists, but it doesn't have any static methods.
    public partial class OperatingSystem
    {
        // Generalized methods

        public static bool IsOSPlatform(string platform);
        public static bool IsOSPlatformVersionAtLeast(string platform, int major, int minor = 0, int build = 0, int revision = 0);

        // Accelerators for platforms where versions don't make sense

        public static bool IsBrowser();
        public static bool IsLinux();

        // Accelerators with version checks

        public static bool IsFreeBSD();
        public static bool IsFreeBSDVersionAtLeast(int major, int minor = 0, int build = 0, int revision = 0);

        public static bool IsAndroid();
        public static bool IsAndroidVersionAtLeast(int major, int minor = 0, int build = 0, int revision = 0);

        public static bool IsIOS();
        public static bool IsIOSVersionAtLeast(int major, int minor = 0, int build = 0, int revision = 0);

        public static bool IsMacOS();
        public static bool IsMacOSVersionAtLeast(int major, int minor = 0, int build = 0, int revision = 0);

        public static bool IsTvOS();
        public static bool IsTvOSVersionAtLeast(int major, int minor = 0, int build = 0, int revision = 0);

        public static bool IsWindows();
        public static bool IsWindowsVersionAtLeast(int major, int minor = 0, int build = 0, int revision = 0);
    }
}
```

### Platform context

To determine the platform context of the call site the analyzer must consider
application of `SupportedOSPlatformAttribute` to the containing member, type,
module, or assembly. The first one will determine the platform context, in other
words the assumption is that more scoped applications of the attribute will
restrict the set of platforms.

**Note:** The analyzer doesn't need to consider MSBuild properties such as
`<TargetFramework>` or `<TargetPlatform>` because the value of the
`<TargetPlatform>` property is injected into the generated `AssemblyInfo.cs`
file, which will have an assembly-level application of
`SupportedOSPlatformAttribute`.

### Diagnostics

The analyzer can produce three diagnostics:

1. '{API}' requires {OS} {OS version} or later.
2. '{API}' has been deprecated since {OS} {OS version}.
3. '{API}' has been removed since {OS} {OS version}.

### Automatic suppression via platform guards

Diagnostic (1) can be suppressed via a call to
`OperatingSystem.IsXxxVersionAtLeast()`. This should work for for simple cases
where the call is contained inside an `if` check but also when the check causes
control flow to never reach the call:

```C#
public void Case1()
{
    if (OperatingSystem.IsIOSVersionAtLeast(13))
        AppleApi();
}

public void Case2()
{
    if (!OperatingSystem.IsIOSVersionAtLeast(13))
        return;

    AppleApi();
}
```

Diagnostics (2) and (3) are analogous except the guard is provided by
a check in the form of:

```C#
public void Case3()
{
    if (OperatingSystem.IsIOS() && !OperatingSystem.IsIOSVersionAtLeast(13))
      AppleApiThatWasRemovedOrObsoletedInVersion13();
}
```

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
platform specific by applying `SupportedOSPlatformAttribute`. If the
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

Should the analyzer consider checks again `Environment.OSVersion`? Maybe,
but it's not obvious what the operating system is.

We have fixed `Environment.OSVersion` to return the correct version numbers.
However, `Environment.OSVersion.Platform` returns an enum with largely outdated
platform. We consider the `Platform` bogus and should obsolete it.

In general, we discourage folks from using `Environment.OSVersion` unless they
need to identity a very specific OS version in order to workaround a bug.
Developers should use `OperatingSystem.IsXxxVersionAtLeast()` instead.

### What about APIs that don't work on specific operating systems?

We have a [extended this feature][platform-exclusion] to cover that.

[dotnet/platform-compat]: https://github.com/dotnet/platform-compat
[API Analyzer]: https://devblogs.microsoft.com/dotnet/introducing-api-analyzer/
[Microsoft.DotNet.Analyzers.Compatibility]: https://www.nuget.org/packages/Microsoft.DotNet.Analyzers.Compatibility
[net5-tfms]: https://github.com/dotnet/designs/blob/main/accepted/2020/net5/net5.md
[os-minimum-version]: https://github.com/dotnet/designs/pull/97
[platform-exclusion]: https://github.com/dotnet/designs/blob/master/accepted/2020/platform-exclusion/platform-exclusion.md