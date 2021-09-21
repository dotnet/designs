# Add ability to register different install location for each architecture

With [dotnet/designs#217](https://github.com/dotnet/designs/pull/217) there are plans
to support multiple architectures on the same machine. For example
the Arm64 and x64 architectures on Windows and macOS. This document describes proposed
changes to the configuration and how host components use it to allow for
multiple architectures on a single machine.

## Current state (as of .NET 5)

The current state is in detailed described in the last change proposal in
[install-locations](https://github.com/dotnet/designs/blob/main/accepted/2020/install-locations.md).

.NET is typically installed into a "global install location". Usually this is
the default install location (`C:\Program Files\dotnet` on Windows,
`/usr/share` on Linux, `/usr/local/share` on macOS),
but it can be changed via global install location registration mechanism.
The registration mechanism uses registry on Windows and configuration file
`/etc/dotnet/install_location` on Linux and macOS.

.NET hosts have a hardcoded algorithm how to find the global install location.
The registration mechanism is queried first and only if it doesn't provide
a value, the host goes to the default install location.

The global install location is used for looking up frameworks in framework
dependent applications, and for looking up SDKs when invoking CLI commands.

* When the `dotnet` executable is used (also called the "muxer") the install location
is specified via the location of the muxer being used. This is typically done
implicitly by invoking the muxer on the `PATH`, but can be done explicitly
by calling the muxer using a fully specified file path.
* When an application is executed and it uses an application executable
(also called the "apphost") the user can specify install location via `DOTNET_ROOT`,
otherwise the apphost will use the global install location lookup.

### Current state on Windows

On Windows the multi-arch situation already exists with x86 and x64 architectures.
Each installs into a different location and is registered accordingly.
The registration mechanism in registry includes the architecture,
the key path is `HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\<arch>\InstallLocation`.

This means that x86 apphost looks only for x86 install location and similarly
x64 apphost looks only for x64 install location. Adding another architecture
is very simple and will continue to work the same way.

On Windows the apphost for the native architecture (so x64 on x64, x86 on x86, ...)
reads the `DOTNET_ROOT` environment variable. The non-native x86 apphost on x64
OS reads the `DOTNET_ROOT(x86)` environment variable. If the inspected
environment variable is set, its value is used, effectively overriding
any other mechanism of finding the install location.

### Current state on Linux and macOS

On Linux and macOS there's no existing multi-arch support. There's also no
default global location which is architecture specific. Part of the discussion
in [dotnet/designs#217](https://github.com/dotnet/designs/pull/217) is about
where should each architecture install to.
For the purposes of this document we'll just assume that each architecture has
its own unique location (it doesn't matter exactly what it will be).

The registration mechanism currently doesn't allow for multi-arch configurations.
The configuration file in `/etc/dotnet/install_location` only contains a single
line, which is the registered location path. There's nothing else in this file.

Already shipped apphost (.NET Core 3.1 and .NET 5) only knows about one install location
and only about one registration mechanism. The current proposal is to use
the existing default install location for the native architecture (that is arm64)
and have a new location for the non-native architecture (x64). Existing apphost
is always x64 (.NET hasn't yet shipped Arm64 release with this) and as such
will not work when using just the default install location knowledge.

On Linux and macOS the apphost only reads the `DOTNET_ROOT`, in all cases.

## New .NET 6 behavior

### New default install location

The current design is to use a different location for each architecture,
so apphost should hardcode the knowledge of each of the default locations.
This means specifically that x64 apphost on macOS will need to be aware
whether it's running on arm64 OS and change the default install location.
Similarly for Windows.

Note that we can't change downlevel apphosts and thus we need to rely on
the global registration mechanism to redirect these apphosts to the correct
location.

### No changes to registration on Windows

The existing registration mechanism on Windows is already flexible enough
to handle multiple architectures on a single machine. There's no need
to change anything.

### Evolve the registration on Linux and macOS

Add the ability to register architecture specific install location
on Linux and macOS. This will be done by adding architecture specific configuration
files next to the existing `/etc/dotnet/install_location`.

#### Current format

| File name | Content |
| --- | --- |
| `/etc/dotnet/install_location` | `/path/to/install/location` |

Single configuration file `/etc/dotnet/install_location` which contains
a single line which is the path to the product.

#### New format

| File name | Content |
| --- | --- |
| `/etc/dotnet/install_location` | `/path/to/install/location` |
| `/etc/dotnet/install_location_<arch1>` | `/path/to/arch1/install/location` |
| `/etc/dotnet/install_location_<arch2>` | `/path/to/arch2/install/location` |

* The arch-less file contains location for downlevel product.
Apphost from .NET Core 3.1 and .NET 5 will only read this file.
.NET 6 apphost will use this only if architecture specific file is not present.
* All other files are architecture specific with the name of the architecture
(`x86`, `x64`, `arm32`, `arm64`) appended to the file name (all lower case).
.NET 6+ apphosts will read the architecture specific file first and use its content
if present.

All files are of the same format, single line which contains the path to the product.

Installers should write architecture specific files even on systems with only
one architecture. The arch-less file should only be added to support downlevel apphost.

### Installer behavior and impact on host

The registration mechanism is most important for running applications via apphost.
If running apphost works in all configurations it will also make
`dotnet run` work in almost all configurations, since that invokes apphost
if the app is configured to build one.
It can also affect `dotnet test`. Currently we only use apphost to run tests
on Windows, to solve the x86/x64 differences. We could extend this to other platforms
and architectures as well. In that case, `dotnet test` would not need to have
any specific knowledge of where to find the install location, it would just
invoke the right apphost.

On all platforms the apphost will have a default location hardcoded
and so if the installer puts the product into that location it doesn't have to
register that installation. But it would be cleaner if it did so anyway.
Currently we always register the installation on Windows, but we don't do so
on Linux and macOS.

#### Windows installer behavior

Pre-.NET 6 Windows installer writes registry keys for all architectures
(even though it only installs one of them). Ideally the installer would
only write the key for the architecture it's actually installing.

#### Linux/macOS installer behavior

Pre-.NET 6 Linux/macOS installer doesn't write the `install_location` file.

Starting with .NET 6, installers should write architecture specific file
with the product location, in the format `install_location_<arch>`. So for example
`install_location_x64` (all lower case). The content of the file should be
a single line with the path to the product.

*Note: If installers can write the file, they should also read it and validate
that they don't overwrite it with different information.*

#### Support for downlevel apphost

In order to support running downlevel apphost apps on multi-arch non-Windows
architectures (currently only applies to macOS), the installer for
the architecture which .NET supports in versions lower than .NET 6
should also write the product location to the arch-less location file
`install_location`. This is needed for downlevel applications to correctly
find the product.

For example this should be present on macOS arm64 systems if both x64 and arm64
products are installed.

| File name | Content |
| --- | --- |
| `install_location` | `/usr/local/share/dotnet/x64` |
| `install_location_arm64` | `usr/local/share/dotnet` |
| `install_location_x64` | `usr/local/share/dotnet/x64` |

* 3.1 and 5 apphosts will only read the arch-less file `install_location`,
which points to x64 install. Since 3.1 and 5 are only supported on x64 on macOS,
that is the right location.
* arm64 6 apphost will react to the `install_location_arm64`
* x64 6 apphost will react to the `install_location_x64`

*Note: All supported downlevel installers (3.1) will need to be updated
to install the product into a different location and to always write
the arch-less configuration file `install_location`.*

### Evolve environment variable overrides

Similarly to the registration mechanism, the environment variable mechanism
doesn't support multi-arch on Linux and macOS at all. And the existing support
on Windows only works for x86 and x64.

#### Proposed architecture specific environment variables

Apphost should first recognize architecture specific environment variable
in the format `DOTNET_ROOT_ARCH` (all upper case).
For example, `DOTNET_ROOT_X64` or `DOTNET_ROOT_ARM64`.
The same mechanism will be used on all OSes. This means that x86 apphost
on Windows will recognize `DOTNET_ROOT_X86` (it will also recognize
`DOTNET_ROOT(x86)` as it does today for backward compat reasons).

If the architecture specific environment variable is not set all apphosts
should fallback to the default `DOTNET_ROOT` environment variable.

By far the most common use case for environment variable overrides
is usage of private install locations. In that case it's almost always
the case that only one architecture is needed. For these cases the existing
`DOTNET_ROOT` can be used. We will keep recommending to use `DOTNET_ROOT`
unless the specific scenario requires multiple architecture support (like our SDK).

For example on arm64 macOS, the setup could be:

```console
DOTNET_ROOT_ARM64=/usr/local/share/dotnet
DOTNET_ROOT_X64=/usr/local/share/dotnet/x64
DOTNET_ROOT=/usr/local/share/dotnet/x64
```

Similarly to the configuration file above, this guarantees correct behavior
for both .NET 6 (which will read the architecture specific variables) as well as
for .NET 3.1 (which will read the non-architecture-specific variable only).

## Alternative designs

### One configuration file with multiple lines

In this case the `/etc/dotnet/install_location` file format is extended to support
multiple lines with architecture prefixes, like:

```console
/usr/local/share/dotnet/x64
arm64=/usr/local/share/dotnet
x64=/usr/local/share/dotnet/x64
```

The advantage is less files in the configuration folder and arguably easier to
understand configuration for humans.

The main downside is added complexity to the product and installers.

Apphosts would have to contain a parser for this format and implement additional
logic to handle duplicate entries and such.

But more importantly installers would have to be able to read and edit this file
which is rather challenging given the currently used installer technologies
(which basically rely on shell scripts).

### Use existing format for architecture specific environment variables

Since there's already support for `DOTNET_ROOT(x86)` on Windows extend the same
pattern to all architectures and OSes. So for example `DOTNET_ROOT(arm64)`
and so on.

We don't like this pattern as it feels too Windows-centric and would look
weird on non-Windows platforms.
