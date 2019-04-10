# Annotating platform-specific APIs and detecting its use

**PM** [Immo Landwerth](https://github.com/terrajobst)

With .NET Core, we've made cross-platform development a mainline experience in
.NET. Even more so for library authors that use .NET Standard. We generally
strive to make it very easy to build code that works well across all platforms.
Hence, we avoid promoting technologies and APIs that only work on one platform
by not putting platform-specific APIs in the default set that is always
referenced. Rather, we're exposing them via dedicated packages that the
developer has to reference manually.

Unfortunately, this approach doesn't address all concerns:

* **It's hard to reason about transitive platform-specific dependencies**. A
  developer doesn't necessarily know that they took a dependency on a component
  that makes their code no longer work across all platforms. For example, in
  principle, a package that sounds general purpose could depend on something
  platform-specific that now transitively ties the consuming application to a
  single platform (for example, an IOC container that depends on the registry).
  Equally, transitively depending on a platform-specific package doesn't mean
  that the application doesn't work across all platforms -- the library might
  actually guard the call so that it can provide a slightly better experience on
  a particular platform.

* **We need to stay compatible with existing APIs**. Ideally, types that provide
  cross-platform functionality shouldn't offer members that only work on
  specific platforms. Unfortunately, the .NET platform is two decades old and
  was originally designed to be a great development platform for Windows. This
  resulted in Windows-specific APIs being added to types that are otherwise
  cross-platform. A good example are file system and threading types that have
  methods for setting the Windows access control lists (ACL). While we could
  provide them elsewhere, this would unnecessarily break a lot of code that
  builds Windows-only applications, e.g. WinForms and WPF apps.

* **.NET runs in more and more diverse environments**. It's tempting to think of
  platform-specificness as a fixed property of a given API. While that's true
  for many cases, this doesn't take into consideration that .NET runs in an
  increasing set of diverse environments, ranging from headless IOT devices over
  smartphones and tablets to PCs and data centers. And we're still extending
  this set, for example, we're currently working on Blazor that allows .NET code
  to run in the browser using web assembly. In fact, much of .NET already runs
  in environments we couldn't predict when .NET started. So even if we did a
  perfect job with API layering, it's very likely that we'll eventually ship a
  .NET implementation for a new environment where some APIs must throw.

.NET has always taken the position that providing platform-specific
functionality isn't a bug but a feature because it empowers developers to build
applications that feel native for the user. That's why .NET has rich interop
facilities such as P/Invoke.

At the same time, developers really dislike the notion of "write once and test
everywhere" because it increases the risk that one builds an architecture that
isn't suitable to ship the code in all the environments that are relevant for
the business.

Fortunately .NET has also taken the position that developer productivity is key.
Hence, we should provide a mechanism that allows developers to realize their
cross-platform goals with ease and confidence. This mechanism cannot purely rely
on constructive aspects, such as choosing the right dependencies, but also has
to handle cases where platform-specificity needs to change because .NET can now
run in more environments.

This document proposes a Roslyn analyzer that informs developers when they
accidentally use platform-specific APIs. For library authors this also includes
the ability to mark their own APIs as platform-specific so that upstream
consumers are also informed about their choices. This work draws heavily form
the experience of the [API Analyzer] and improves on it by making it a
first-class platform feature.

[API Analyzer]: https://devblogs.microsoft.com/dotnet/introducing-api-analyzer/

## Scenarios and User Experience

### Finding usage of platform-specific APIs

Alejandra is working at Fabrikam, where she's tasked with porting their asset
management system to .NET Core. The application consists of several web
services, a desktop application, and a set of libraries that are shared.

She begins by porting the core business logic libraries to .NET Standard. After
porting it to .NET Standard, she's getting a diagnostic in the code below:

> PC0001: 'Registry' is only supported on 'Windows'

```C#
private static string GetLoggingPath()
{
    using (var key = Registry.CurrentUser.OpenSubKey(@"SoftwareFabrikamAssetManagement"))
    {
        if (key?.GetValue("LoggingDirectoryPath") is string configuredPath)
            return configuredPath;
    }

    // This is either not running on Windows or no logging path was configured,
    // so just use the path for non-roaming user-specific data files.
    var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    return Path.Combine(appDataPath, "Fabrikam", "AssetManagement", "Logging");
}
```

### Add platform-specific code without becoming platform-specific

After reviewing the code she realizes that the only reason the registry APIs are
being called is to allow the Windows desktop client to override the default
logging location. She considers removing the code but this would make the
experience slightly worse for the existing Windows clients, so she decides to
instead wrap the code in a platform guard. If the code doesn't run on Windows,
it will simply use the default logging path:

```C#
private static string GetLoggingPath()
{
    // Verify the code is running on Windows.
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        using (var key = Registry.CurrentUser.OpenSubKey(@"SoftwareFabrikamAssetManagement"))
        {
            if (key?.GetValue("LoggingDirectoryPath") is string configuredPath)
                return configuredPath;
        }
    }

    // This is either not running on Windows or no logging path was configured,
    // so just use the path for non-roaming user-specific data files.
    var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    return Path.Combine(appDataPath, "Fabrikam", "AssetManagement", "Logging");
}
```

After that, the diagnostic disappears automatically.

### Wrapping platform-specific APIs

Miguel is an application developer. For his new product, *Gadgetifier 6000* he
needs to deal with setting permissions on files from various places in his
application. So he wants to provide an extension method that makes it easier to
do that. He's already using the `Mono.Posix` library but he'd like to integrate
that more tightly with the existing BCL APIs such as as `FileInfo`.

His sketch looks as follows:

```C#
public static class FileSystemExtensions
{
    public static void SetUnixPermissions(this FileInfo fileInfo, UnixFileAccessPermissions permissions)
    {
        if (fileInfo == null)
            throw new ArgumentNullException(nameof(fileInfo));

        var unixFileInfo = new UnixFileInfo(fileInfo.Name);
        unixFileInfo.FileAccessPermissions = permissions;
    }
}
```

While typing the code, he's getting the diagnostic

> PC0001: 'UnixFileInfo' is only supported on 'Linux', 'OSX'

"No kidding", he thinks. However, he realizes that he plans to use his library
from Windows and iOS as well. So he adds an `if` statement that performs
platform checks and throws `PlatformNotSupportedException` if the code is
neither running on Linux nor on OSX:

```C#
public static class FileSystemExtensions
{
    public static void SetUnixPermissions(this FileInfo fileInfo, UnixFileAccessPermissions permissions)
    {
        if (fileInfo == null)
            throw new ArgumentNullException(nameof(fileInfo));

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
            !RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            throw new PlatformNotSupportedException();

        var unixFileInfo = new UnixFileInfo(fileInfo.Name);
        unixFileInfo.FileAccessPermissions = permissions;
    }
}
```

This results in another diagnostic

> PC0001: The implementation of 'SetUnixPermissions()' only works on 'Linux',
> 'OSX'. You should mark it with the PlatformSpecificAttribute.

Once he does that, his code no longer shows any diagnostics:

```C#
public static class FileSystemExtensions
{
    [PlatformSpecific(nameof(OSPlatform.Linux))]
    [PlatformSpecific(nameof(OSPlatform.OSX))]
    public static void SetUnixPermissions(this FileInfo fileInfo, UnixFileAccessPermissions permissions)
    {
        if (fileInfo == null)
            throw new ArgumentNullException(nameof(fileInfo));

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
            !RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            throw new PlatformNotSupportedException();

        var unixFileInfo = new UnixFileInfo(fileInfo.Name);
        unixFileInfo.FileAccessPermissions = permissions;
    }
}
```

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
    - Library authors can annotate their libraries, even if they target older
      versions of .NET Standard or .NET Core.
    - Library consumers don't have to install a NuGet package to get diagnostics
      when using platform-specific APIs

### Non-Goals

* Shipping the platform-specific analyzer out-of-band.
    - ***OPEN ISSUE:** This might not work for .NET Standard*

## Design

### Attribute

To mark platform-specific APIs, we'll use the following attribute:

```C#
namespace System.Runtime.CompilerServices
{
    // Disallowed on:
    // - Delegate
    // - Field
    // - Enum
    // - GenericParameter
    // - Interface
    // - Parameter
    // - ReturnValue
    [AttributeUsage(AttributeTargets.Assembly |
                    AttributeTargets.Class |
                    AttributeTargets.Constructor |
                    AttributeTargets.Event |
                    AttributeTargets.Method |
                    AttributeTargets.Module |
                    AttributeTargets.Property |
                    AttributeTargets.Struct,
                    AllowMultiple=true, Inherited=true)]
    public sealed class PlatformSpecificAttribute : Attribute
    {
        public PlatformSpecificAttribute(string platform);
        public string Platform { get; }
    }
}
```

### Platform-specific APIs

First some terminology:

* **Platform-specific**. An API is considered *platform-specific* if the
  attribute `PlatformSpecificAttribute` is applied at least once. Multiple
  applications mean that the API is only supported for those platforms. If the
  API itself isn't marked, the containing type, module, and assembly are
  consulted.

* **Universally portable**. APIs that aren't considered platform-specific are
  called *universally portable*.

Semantics:

* **Effective platform set.** The set of supported platforms are only taken from
  the first API that is marked. For example, if a method is marked as
  Windows-specific but the containing type is marked as specific to both Windows
  and Linux, the method is considered Windows-only.

* **Binding `PlatformSpecificAttribute`**. When checking whether a given API is
  annotated with `PlatformSpecificAttribute`, the analyzer shall ignore
  accessibility and match the type by namespace qualified name. This allows
  library authors to include an internal copy of this type when they target
  older versions of .NET that don't provide this attribute.

### PC0001: Used API isn't universally portable

This diagnostic will be raised for usages of platform-specific APIs when the
calling code is considered universally portable. The diagnostic contains the
list of platforms supported by the API:

> PC0001: '\<API>' is only supported on \<Platform-List>.

### PC0002: Used API is less portable than the calling code

This diagnostic will be raised for usages of platform-specific APIs when the
calling code is also platform-specific, but less portable. The diagnostic
contains the list of platforms that are unsupported by the used API:

> PC0002: '\<API>' is not supported on \<Platform-List>.

### PC0003: The API should be declared as platform-specific

This diagnostic is raised when a method throws `PlatformNotSupportedException`
after checking for platforms that it isn't annotated for. The diagnostic
contains the list of missing annotations:

> PC0003: '\<API>' isn't supported on \<Platform-List> but isn't marked as such.

For example, the following code is only annotated to be Linux-specific but
throws when it's neither on Windows nor on Linux.

```C#
[PlatformSpecific(nameof(OSPlatform.Linux))]
public static void SomeMethod(this FileInfo fileInfo, UnixFileAccessPermissions permissions)
{
    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    {
        throw new PlatformNotSupportedException(); // (1) OK
    }

    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
        !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        throw new PlatformNotSupportedException(); // (2) DIAGNOSTIC
    }
}
```

### Automatic suppression via platform-guards

`PC0001` and `PC0002` shall not be raised for cases where the API usage is
nested inside an `if` statement that checks for platforms that the API is
supported on.

For example, consider that `SomeMethod()` is either considered universally
portable or portable to more platforms than just Windows and Linux. In that
case, you should get diagnostics for API usages (2) and (3) but not for (1).

```C#
public void SomeMethod()
{
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    {
        ApiSupportedByWindowsLinuxAndOSX(); // (1) OK
        ApiSupportedByWindows();            // (2) DIAGNOSTIC
    }

    ApiSupportedByWindows();                // (3) DIAGNOSTIC
}
```

### Automatic suppression in unreachable platform-guards

`PC0001` and `PC0002` shall not be raised for cases where the API usage is
nested inside an `if` statement that only checks for platforms that containing
code can never run on.

For example, in the code below `SomeMethod()` is annotated to be
Windows-specific, so the first platform-guard will never evaluate to true.
That's why API usage (1) should not generate a diagnostic but (2) should.

```C#
[assembly: PlatformSpecific(nameof(OSPlatform.Windows))]

public void SomeMethod()
{
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    {
        ApiSupportedByOSX(); // (1) OK
    }

    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        ApiSupportedByOSX(); // (2) DIAGNOSTIC
    }
}
```

### Automatic suppressions via annotations

`PC0001` and `PC0002` shall not be raised for cases where the API usage is
contained inside a method that is considered specific to a subset of the
platforms the API is supported for.

For example, in the following code you should get diagnostics for API usage (2)
but not for (1).

```C#
[PlatformSpecific(nameof(OSPlatform.Windows))]
[PlatformSpecific(nameof(OSPlatform.OSX))]
public void SomeMethod()
{
    ApiSupportedByWindowsLinuxAndOSX(); // (1) OK
    ApiSupportedByWindows();            // (2) DIAGNOSTIC
}
```

<!-- Consider proposing code fixers.

### Code Fixers

* Suggest wrapping statement in platform guard
* Suggest annotating the method with an attribute
* Suggest generating an internal attribute `PlatformSpecificAttribute` when the
  targeted framework doesn't define it.

-->
  
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

[dotnet/platform-compat]: https://github.com/dotnet/platform-compat
[Microsoft.DotNet.Analyzers.Compatibility]: https://www.nuget.org/packages/Microsoft.DotNet.Analyzers.Compatibility
