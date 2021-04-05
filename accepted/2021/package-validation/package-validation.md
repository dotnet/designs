# Package Validation

**Owner** [Immo Landwerth](https://github.com/terrajobst)

With .NET Core & Xamarin we have made cross-platform a mainstream requirement
for library authors. However, we lack validation tooling which can result in
packages that don't work well which in turn hurts our ecosystem. This is
especially problematic for emerging platforms where adoption isn't high enough
to warrant special attention by library authors.

The tooling we provide as part of the SDK has close to zero validation that
multi-targeted packages are well-formed. For example, a package that
multi-targets for .NET Core 3.1 and .NET Standard 2.0 needs to ensure that code
compiled against the .NET Standard 2.0 binary can run against the binary that is
produced for .NET Core 3.1. In the dotnet/runtime repo we have tooling that
ensures that this is the case, but this can't easily be used by our customers.
We have seen this issue in the wild, even with 1st parties, for example, the
Azure AD libraries.

Another common issue in the .NET ecosystem is that binary breaking changes
aren't well understood. Developers tend to think of "does the source still
compile" as the bar to determine whether an API is compatible. However, at the
ABI level certain changes that are fine in C# aren't compatible at the level of
IL, for example, adding a defaulted parameter or changing the value of a
constant. In the dotnet/runtime repo we have tooling at allows to validate that
our current API shape is backwards compatible with the API we shipped before.

This document outlines a set of features that will allow library developers to
validate that their packages are well-formed and they they didn't make
unintentional breaking changes. This is achieved by productizing our internal
tooling that we're planning to use ourselves as well.

## Scenarios and User Experience

## Validation between .NET Standard and .NET Core

Finley is working on a client library for a cloud services. They need to support
both .NET Framework and .NET Core customers. They started with just targeting
.NET Standard 2.0 but now they realize they want to take advantage of the new
UTF8 string feature in .NET 7. In order to do that, they are multi-targeting for
`netstandard2.0` and `net7.0`.

Finley has written the following code:

```C#
#if NET7_0_OR_GREATER
    public void DownloadLogs(Utf8String area)
    {
        DownloadLogs(area.AsSpan());
    }
#else
    public void DownloadLogs(string area)
    {
        var utf8Bytes = Encoding.UTF8.GetBytes(area);
        DownloadLogs(utf8Bytes);
    }
#endif

    private void DownloadLogs(ReadOnlySpan<byte> areaUtf8)
    {
        // Do network call
    }
```

When Finley builds the project it fails with the following error:

> error SYSLIB1234: The method 'DownloadLogs(string)' exists for .NET Standard
> 2.0 but does not exist for .NET 7.0. This prevents consumers who compiled
> against .NET Standard 2.0 to run on .NET 7.0.

Finley understands that they shouldn't exclude `DownloadLogs(string)` but
instead just provide an additional `DownloadLogs(Utf8String)` method for .NET
7.0 and changes the code accordingly:

```C#
#if NET7_0_OR_GREATER
    public void DownloadLogs(Utf8String area)
    {
        // ...
    }
#endif

    public void DownloadLogs(string area)
    {
        // ...
    }
```

### Validation against previous version

Skylar works on the `AdventureWorks.Client` NuGet package. They want to make
sure that they don't accidentally make breaking changes so they configure their
project to instruct the package validation tooling to run API compatibility on
the previous version of the package:

```XML
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net5.0</TargetFramework>
  </PropertyGroup>

  <PropertyGroup>
    <ValidatePackageAgainstPreviousVersion>True</ValidatePackageAgainstPreviousVersion>
  </PropertyGroup>

</Project>
```

A few weeks later, Skylar is tasked with adding support for a connection timeout
to their library. The `Connect` method looks like this right now:

```C#
public static Client Connect(string url)
{
    // ...
}
```

Since a connection timeout is an advanced configuration setting they reckon they
can just add an optional parameter:

```C#
public static Client Connect(string url, TimeSpan? timeout = default)
{
    // ...
}
```

However, when they rebuild the package they are getting an error:

> error SYSLIB1234: The method 'Connect(string)' exists in the previous version
> of the NuGet package (AdventureWorks.Client 1.2.3) but no longer exists in the
> current version being built (1.2.4). This is a breaking change.

Skylar realizes that while this is not a source breaking change, it's a binary
breaking change. They solve this problem by adding an overload instead:

```C#
public static Client Connect(string url)
{
    return Client(url, Timeout.InfiniteTimeSpan);
}

public static Client Connect(string url, TimeSpan timeout)
{
    // ...
}
```

### Making an intentional breaking change

Armani is working on version 0.7 of their library. While they don't want to make
accidental breaking changes, they do want to make changes based on user feedback.

In version 0.6 they had a `Connect()` method that accepted a timeout as an
`int`. Users complaint that timeouts in .NET are nowadays generally modelled
using `TimeSpan` instead. After making the necessary code change, rebuilding the
library results in an error:

> error SYSLIB1234: The method 'Connect(string, int)' exists in the previous
> version of the NuGet package (Fabrikam.Client 0.6.3) but no longer exists in
> the current version being built (0.7.0). This is a breaking change.

Since Armani's library is still an 0.x, which, according to SemVer, doesn't
promise a stable API, they briefly consider turning off the breaking change
validation entirely. However, Armani feels that even though it's 0.x they still
don't want to make accidental breaking change so they decide to just suppress
this specific instance of the breaking change:

```C#
[SuppressMessage("Interoperability", "SYSLIB1234")]]
public static Client Connect(string url, TimeSpan timeout)
{
    // ...
}
```

## Requirements

### Goals

* Performance should be good so that basic package validation can be on by
  default (when the project generates a package), but it must be possible to
  turn it off as well
* Comparing to previous version should be opt-in because it requires downloading
  additional files
* Developers need to be able to make intentional breaking changes while still
  validating that they didn't make other unintentional breaking changes.

### Non-Goals

* Having zero impact on performance (as that's not feasible)

## Stakeholders and Reviewers

* Libraries team
* NuGet team
* SDK team

## Design

### API Compat Tooling

The API compat tooling is described in a separate [spec][api-compat-spec].

### Diagnostic IDs

***OPEN QUESTION**: Should we introduce a new prefix, such as `APC` (ApiCompat)
or `PKG` (packaging) or should we reuse `CA`/`SYSLIB`?*

## Q & A

[api-compat-spec]: https://github.com/dotnet/designs/pull/177