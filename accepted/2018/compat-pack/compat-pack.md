# Windows Compatibility Pack

**Owner** [Immo Landwerth](https://github.com/terrajobst) | [Wes Haggard](https://github.com/weshaggard)

When we shipped .NET Core 1.x as well as .NET Standard 1.x, we were hoping to be
able to use that as an opportunity to remove legacy technologies and deprecated
APIs. Since then, we've learned that no matter how attractive the new APIs and
capabilities of .NET Core are: if your existing code base is large enough, the
benefits of the new APIs are often dwarfed by the sheer cost of re-implementing
and/or adapting your existing code. The *Windows Compatibility Pack* is about
providing a good chunk of these technologies so that building .NET Core
applications as well as .NET Standard libraries becomes much more viable for
existing code.

This is a logical extension of [.NET Standard 2.0][ns20] in which we
significantly increased the API set so that porting existing code becomes much
easier and code largely just compiles as-is. However, we also didn't want to
complicate .NET Standard by adding large API sets that can't work across all
platforms. That's why we haven't, for instance, added the Windows registry or
reflection emit APIs. The *Windows Compatibility Pack* will sit above .NET
Standard [1] and is thus free to provide access to technologies that are Windows
only.

Providing more APIs for class libraries that target .NET Standard also helps
with the compatibility mode we've added in .NET Standard 2.0. It allows you to
reference existing .NET Framework binaries. This helps with the transition
period where many packages still aren't available for .NET Standard or .NET
Core. However, the compatibility mode doesn't change physics: it only bridges
differences in the assembly factoring between .NET Framework and .NET Standard.
It cannot give you access to APIs that don't exist on the .NET implementation
you're running on. The *Windows Compatibility Pack* helps by extending the set
of APIs that are covered by the compatibility mode.

The *Windows Compatibility Pack* is especially useful for customers that want to
move .NET Core but plan to stay on Windows as a first step. In that scenario,
not being able to use Windows-only technologies is only a migration hurdle with
zero architectural benefit.

[ns20]: https://github.com/dotnet/announcements/issues/24


*[1] "Sitting" above .NET Standard means the APIs is available for .NET
Standard, i.e. the developer needs to add reference to it separately. That's in
contrast to the API being part of .NET Standard itself.*

## Scenarios and User Experience

### Partially moving to .NET Core

John needs to evolve Fabrikam's asset management application so that they can
move part of it to the cloud. The current architecture is a full on-prem
solution, using a WPF desktop application with an ASP.NET WebAPI backend, both
running on .NET Framework.

Fabrikam decided that they are quite happy with the desktop application and want
to continue to leverage WPF. But they decide it's best to leverage ASP.NET Core
for the web API backend, as this gives them more flexibility in their choice of
server operating system as well as isolated deployments using Docker.

![](scenarios-fabrikam-migration-01.png)

Both, the frontend and the backend, share code with utility functionality and
business logic (logging, entities, rules). John concludes that the easiest to
share code across their tiers is by making it target .NET Standard 2.0. This
will also allow them to reuse those pieces in an upcoming Xamarin-based mobile
client.

![](scenarios-fabrikam-migration-02.png)

However, John's first goal is bringing up the application with the current on-
prem setup, making sure everything continues to work as-is. John starts by
creating a project for the shared code, targeting .NET Standard 2.0. Given that
the code was mostly low-level and general purpose, everything just compiled as-
is. He then continues by creating an ASP.NET Core web application for his web
API backend. After performing the ASP.NET Core specific changes (which was
mostly changing namespace names) he then faces some APIs that don't exist in
.NET Core. He runs API Port on this existing application and it points him to
the *Windows Compatibility Pack* to gain access to the missing APIs. His project
is now compiling and he starts validating the new setup.

Aspects:

* Part of the application runs on .NET Framework (desktop/classic ASP.NET)
* Other parts move to .NET Core on Windows (console/ASP.NET Core)
* Common code is shared via .NET Standard

### Building a cross-platform library

Julia owns the the Fabrikam rule engine that they use for automating business
rules. They would like to evolve it so that it can be used across all their
applications, which include the asset management which was recently moved to
.NET Core and now runs on a Linux machine in Azure.

In order to optimize for performance, the rule engine uses both build time as
well as runtime code generation. During build time they are using CodeDOM to
generate entity classes out of XML. At runtime they use Reflection Emit to speed
up evaluation of expressions in their rule engine.

Neither CodeDOM nor Reflection Emit APIs are available in .NET Standard 2.0.
During her investigation, Julia is pleased to find that these are available as
individual packages to allow her to extend the capabilities without. This also
allows her to check that her component only depends on APIs that work on their
Linux-based backend as well.

```XML
<ItemGroup>
  <PackageReference Include="System.CodeDom" Version="4.4.0" />
  <PackageReference Include="System.Reflection.Emit" Version="4.3.0" />
<ItemGroup>
```

Aspects:

* Using the stripped down packages that only includes APIs that work on the
  operating systems you care about.

### Building .NET Standard libraries that light-up on Windows

Scott has to fix a bug in Fabikam's logging infrastructure that doesn't honor
the logging path as configured by their desktop application. For historical
reasons, the configuration is stored in the registry. Instead of rewriting how
configuration is persisted by the desktop application, Scott decides it's best
to check the registry for the key, and, if present, use that to set the logging
path. Since logging is part of their code base, Scott guards the registry call
behind an operating system check:

```CSharp
private static string GetLoggingPath()
{
    // If we're on Windows and the desktop app has configured a logging path, we'll use that.
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Fabrikam\AssetManagement"))
        {
            if (key?.GetValue("LoggingDirectoryPath") is string configuredPath)
                return configuredPath;
        }
    }

    // We're either not on Windows or no logging path was configured, so we'll use the location
    // for non-roaming user-specific data files.
    var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    return Path.Combine(appDataPath, "Fabrikam", "AssetManagement", "Logging");
}
```

Aspects:

* Building a cross-platform library that is expected to work everywhere
* Windows-only APIs are used behind a guard

## Requirements

### Goals

* **Provide commonly used functionality as NuGet packages**. We'll use usage
  data we collect from various sources (such as NuGet, API Port, the .NET
  Framework compatibility lab) to create a prioritized list.

* **Provide API that are binary compatible with .NET Framework**. This means we
  provide the existing APIs in such a way that (1) you can reference existing
  .NET Framework binaries and exchange types from the compatibility pack with it
  and (2) that the .NET Standard libraries can be consumed from a .NET Framework
  project and be able to exchange types there as well.

* **Provide NuGet packages that are usable from both .NET Standard 2.0 as well
  as .NET Core 2.0**. We generally expect customers to create .NET Standard
  libraries using the compatibility pack and share it between .NET Framework and
  .NET Core applications running on Windows. While this doesn't mean that these
  APIs will actually work on non-Windows operating systems (many will throw
  `PlatformNotSupportedException`) we must guarantee that the APIs will be
  present on all operating systems. This allows customers to perform an
  operating system check to decide whether they call it, rather than having to
  use multi-targeting and `#if`.

* **Provide a single NuGet package that includes the entire set**. When
  migrating existing code, it's often desirable to start with a "maximum set" to
  see whether that will solve most of the porting issues. Then, the developer
  can fine tune and assess how much is Windows specific and how that flies with
  the cross-platform requirements they might have.

* **Provide a way to use cross-platforms APIs without depending on Windows
  specific APIs**. Since some of the APIs provided in the compatibility pack
  will work cross-platform, it should be possible to reference those without
  also pulling in the Windows-specific subset. This ensure cross-platform
  libraries can be built with much more confidence. However, we're not going to
  subset types. In other words, if part of a component is Windows specific,
  we're not going to extract parts of the types that aren't.

### Non-Goals

* **Provide desktop application models**. We don't plan on including WinForms,
  WPF, or System.Web-based ASP.NET support. If you depend on those, our
  recommendation is to stay on .NET Framework.

* **Extend .NET Standard to include these additional APIs**. We don't plan on
  revving the .NET Standard version just to include these APIs. The reason being
  that (1) it makes these APIs effectively unavailable until all platforms
  implement it, which will take a while considering we just brought all
  platforms to .NET Standard 2.0 and (2) many of these APIs are Windows-specific
  and we believe taking that dependency should be an explicit step. That being
  said, it's possible that a later version of .NET Standard will include a
  subset of these APIs, provided they are largely general purpose and not
  Windows specific.

* **Guarding & capability checks**. We don't have a good experience for
  capability checks today. Ideally, developers wouldn't have to use platform
  checks and instead using capability APIs (such as `Registry.IsSupported` as
  opoosed to checking for Windows). However, since the compatibility pack is
  about exposing existing APIs capability APIs are not feasible as this would
  require adding new APIs on existing types, requiring a later (unshipped)
  version of .NET Framework/.NET Core/.NET Standard.

* **Perform unnatural acts to make more APIs cross-platform capable**. For
  instance, we're not going to subset types in order to increase the reach of
  legacy components that were designed with Windows in mind. Nor are we going to
  re-implement or emulate Windows technologies on Linux, for example,
  implementing the registry over the file system. Our goal is to make things
  work that can work and allow the rest to be used on Windows using a platform
  guard. Emulating technologies is at best a leaky abstraction and at worst very
  expensive with marginal gains.

## Design

### Package

* Package ID: [Microsoft.Windows.Compatibility](https://www.nuget.org/packages/Microsoft.Windows.Compatibility)
* Package must install both into .NET Core 2.0 as well as .NET Standard 2.0
* Package will be a meta package, allowing developers to reference individual
  components

### Existing Packages

I've computed the set of all the existing packages that contain types that ship
in .NET Framework. I've then annotated which one of these packages are already
part of .NET Core (which includes .NET Standard), and whether they are already
referenced from the [compatibility pack](https://github.com/dotnet/corefx/blob/master/pkg/Microsoft.NETFramework.Compatibility/Microsoft.NETFramework.Compatibility.pkgproj#L16).

The proposal is to add all the packages that aren't part of .NET Core. The
result in contained in this [spreadsheet](compat-pack.xlsx).

### Missing Functionality

We use the `netfx-compat-pack` label on GitHub to track issues asking for adding
missing .NET Framework functionality to .NET Core.

https://github.com/dotnet/corefx/issues?q=is%3Aopen+is%3Aissue+label%3Anetfx-compat-pack

Current status:

| Status      | ID                                                     |                                                       | Remaining work   | UWP?      |
|-------------|--------------------------------------------------------|-------------------------------------------------------|------------------|-----------|
| In progress |                                                        | Updated Shims package                                 | Authoring        |           |
| In progress | [14529](https://github.com/dotnet/corefx/issues/14529) | System.Runtime.Caching                                | Test, Pkg        | no        |
| In progress | [14762](https://github.com/dotnet/corefx/issues/14762) | System.Management (WMI)                               | Tests, Pkg       | no        |
| In progress | [3906](https://github.com/dotnet/corefx/issues/3906)   | System.Diagnostics.PerformanceCounter                 | PR               | no        |
| In progress | [2089](https://github.com/dotnet/corefx/issues/2089)   | System.DirectoryServices                              | Pkg              | no        |
| In progress | [2089](https://github.com/dotnet/corefx/issues/2089)   | System.DirectoryServices.Protocols                    | Tests, Pkg       | no        |
| In progress | [2089](https://github.com/dotnet/corefx/issues/2089)   | System.DirectoryServices.AccountManagement            | Tests, Pkg       | no        |
| In progress |                                                        | System.ServiceModel.Syndication                       | Tests, Pkg       | ?         |
| Done        | [6913](https://github.com/dotnet/corefx/issues/6913)   | System.Diagnostics.EventLog                           |                  | no        |
| Done        | [11545](https://github.com/dotnet/corefx/issues/11545) | System.Drawing                                        |                  | no        |
| In progress | [11857](https://github.com/dotnet/corefx/issues/11857) | System.ComponentModel.Composition (MEF1)              | Build, Test, Pkg |           |
| Done        | [13035](https://github.com/dotnet/corefx/issues/13035) | System.Data.Odbc                                      |                  | no        |
| Done        | [19771](https://github.com/dotnet/corefx/issues/19771) | System.Data.DatasetExtensions                         |                  | yes       |
| Done        | [6024](https://github.com/dotnet/corefx/issues/6024)   | System.ServiceProcess.ServiceController & ServiceBase |                  | no        |
| Maybe       | [5766](https://github.com/dotnet/corefx/issues/5766)   | System.Xaml                                           |                  | --        |
| Maybe       | [8723](https://github.com/dotnet/corefx/issues/8723)   | System.ServiceModel (WCF) Message Security            |                  | --        |
| Declined    | [6920](https://github.com/dotnet/corefx/issues/6920)   | System.Addin                                          |                  | --        |
| Declined    | [2394](https://github.com/dotnet/corefx/issues/2394)   | System.Activities (WF)                                |                  | --        |

The up-to-date table can be found on our [internal SharePoint site][status].

[status]: https://microsoft.sharepoint.com/teams/netfx/corefx/_layouts/OneNote.aspx?id=%2Fteams%2Fnetfx%2Fcorefx%2FDocuments%2FCoreFx%20Notes&wd=target%28Release.one%7C7F154601-98C6-468E-AF7D-237C4794031A%2FCompat%20Pack%7C08AF2F40-DBC5-4608-B5D4-9A84A4232A12%2F%29

## Q & A

### Why does the compatibility pack have to install into .NET Standard?

To make it easy to share code between .NET Framework and other .NET
implementations, such as .NET Core, UWP, and Xamarin.

### Why should one be able to reference Windows-only APIs from .NET Standard?

Because it allows for runtime light-up (as opposed to forcing developers to use
build-time specialization using multi-targeting). Ideally, we'd expose
capability APIs (as opposed to doing OS checks) but this requires adding new
APIs to .NET Framework as well. It will take time to get there.

### How would we avoid the pit of failure when Windows-only APIs are used on non-Windows machine?

We should extend & productize the [platform-compat tool](https://github.com/dotnet/platform-compat)
to detect those cases eagerly.

### Why isn't there a meta package with only the cross-platform technologies?

We felt it wasn't necessary. If the demand exist, we'll do it at a later time.
For now, we expect customers to solve this by referencing the individual
packages directly.

### Why haven't we flattened the package similar to Microsoft.NETCore.App?

For three reasons:

1. The individual packages already exist
2. We believe customers want to reference them individually for control
3. We cannot fully flatten the package regardless. We also want to bring in tech
   that isn't legacy (such as SQL Client). Fully flattening would mean that the
   only way to get those were through a compatibility pack which would send a
   strong signal that these technologies would be legacy. We could flatten the
   legacy ones only, but it seems more complicated to understand if the pack is
   partially flattened than if it is pure meta package.

### Why is it called Windows Compatibility Pack?

Originally, we used the name *.NET Framework Compatibility Pack* but we changed
it to the *Windows Compatibility Pack* to underline the notion we're not
providing all of the .NET Framework as part of it.
