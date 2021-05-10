# Add ability to register different install location for each architecture

With [dotnet/sdk#16896](https://github.com/dotnet/sdk/issues/16896) there are plans
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

On Windows the native apphost (so x64 on x64, x86 on x86, ...) reads the `DOTNET_ROOT`
environment variable. And non-native x86 apphost on x64 OS reads the `DOTNET_ROOT(x86)`
environment variable. If the inspected environment variable is set its value
is used effectively overriding any other mechanism of finding the install
location.

### Current state on Linux and macOS

On Linux and macOS there's no existing multi-arch support. There's also no
default global location which is architecture specific. Part of the discussion
in [dotnet/sdk#16896](https://github.com/dotnet/sdk/issues/16896) is about
where should each architecture install to.
For the purposes of this document we'll just assume that each architecture has
its own unique location (it doesn't matter exactly what it will be).

The registration mechanism currently doesn't allow for multi-arch configurations.
The configuration file in `/etc/dotnet/install_location` only contains a single
line, which is the registered location path. There's nothing else in this file.

Already shipped apphost (.NET Core 3.1 and .NET 5) only knows about one install location
and only about one registration mechanism. The current proposal is to use
the existing default install location for the native architecture (that is Arm64)
and have a new location for the non-native architecture (x64). Existing apphost
is always x64 (.NET hasn't yet shipped Arm64 release with this) and as such
will not work when using just the default install location knowledge.

On Linux and macOS the apphost only reads the `DOTNET_ROOT`, in all cases.

## Proposal for .NET 6

### New default install location

The current design is to use a different location for each architecture,
so apphost should hardcode the knowledge of each of the default locations.
This means specifically that x64 apphost on macOS will need to be aware
whether it's running on Arm64 OS and change the default install location.
Similarly for Windows.

### No changes to registration on Windows

The existing registration mechanism on Windows is already flexible enough
to handle multiple architectures on a single machine. There's no need
to change anything.

### Evolve the registration on Linux and macOS

Add the ability to register architecture specific install location
on Linux and macOS. This will be done by changing the format of the existing
configuration file `/etc/dotnet/install_location`.

#### Current format

```console
/path/to/install/location
```

#### New format

```console
[/path/to/install/location]
<arch1>=/path/to/arch/install/location
<arch2>=/path/to/arch2/install/location
```

* The first line of the file can still be just a path (no architecture prefix).
Apphost from .NET Core 3.1 and .NET 5 will only read this line and nothing
else in the file (already the case). .NET 6 apphost will use this only if
architecture specific location is not specified. Only the first line can
omit the architecture prefix.
* All other lines are prefixed with the name of the architecture
(`x86`, `x64`, `arm32`, `arm64`) followed by the equal sign `=` followed
by the absolute path of the install location for that architecture.

Each architecture should be specified no more than once (apphost will
use only the first occurrence). Installers should write architecture
specific lines (with arch prefix) even on systems with only one architecture.
The un-prefixed first line should only be added to support downlevel apphost.

This will mean adding more complex parsing logic into the host, but there
has to be some addition to support multi-arch on non-Windows platforms.

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
and so if the installer puts the layout into that location it doesn't have to
register that installation. But it would be cleaner if it did so anyway.
Currently we always register the installation on Windows, but we don't seem
to do so on Linux and macOS.

*Note: If installers write the file, they should also read it and validate
that they don't overwrite it with different information. It's unclear yet
if this is necessary and in which situations.*

#### Support for downlevel apphost

In order to support running downlevel apphost apps on multi-arch non-Windows
architectures (currently only applies to macOS), the installer of .NET 6 and above
should write the configuration file in a specific shape, for example (on macOS arm64):

```console
/usr/local/share/dotnet/x64
arm64=/usr/local/share/dotnet
x64=/usr/local/share/dotnet/x64
```

With the above proposed changes this would guarantee that all apphosts
would work if the right frameworks are installed.

* 3.1 and 5 apphost would only read the first line, which points to x64 install.
Since 3.1 and 5 are only supported on x64 on macOS, that is the right location.
* arm64 6 apphost would react to the `arm64` location
* x64 6 apphost would react to the `x64` location

### Evolve environment variable overrides

Similarly to the registration mechanism, the environment variable mechanism
doesn't support multi-arch on Linux and macOS at all. And the existing support
on Windows only works for x86 and x64.

#### Proposed architecture specific environment variables

Apphost should first recognize architecture specific environment variable
in the format `DOTNET_ROOT_ARCH`. So for example `DOTNET_ROOT_x64` or `DOTNET_ROOT_ARM64`.
The same mechanism would be used on all OSes, this means that x86 apphost
on Windows would recognize `DOTNET_ROOT_X86` (it would also recognize
`DOTNET_ROOT(x86)` as it does today for backward compat reasons).

If the architecture specific environment variable is not set all apphosts
should fallback to the default `DOTNET_ROOT` environment variable.

By far the most common use case for environment variable overrides
is usage of private install locations. In that case it's almost always
the case that only one architecture is needed. For these cases the existing
`DOTNET_ROOT` can be used. Going forward we may recommend to switch
to architecture specific environment variables, but it may not be necessary.

In cases where multiple architectures are potentially supported (like our SDK)
the architecture specific environment variables should be used.

For example on arm64 macOS, the setup could be:

```console
DOTNET_ROOT_ARM64=/usr/local/share/dotnet
DOTNET_ROOT_X64=/usr/local/share/dotnet/x64
DOTNET_ROOT=/usr/local/share/dotnet/x64
```

Similarly to the configuration file above this guarantees correct behavior
for both .NET 6 (which will read the architecture specific variables) as well
for .NET 3.1 (which will read the non-architecture-specific variable only).

## Alternative designs

### Configuration file per architecture

In this case x64 apphost would look for its configuration in `/etc/dotnet/install_location_x64`.
And `arm64` apphost would look into `/etc/dotnet/install_location_arm64`.
Both would fallback to the existing `/etc/dotnet/install_location` if the arch-specific
one doesn't exist.

The effect of this is almost identical to the proposed solution.
The only downside is that this doesn't follow to "native first" approach
where the native architecture gets the existing "nice" names. It also adds more
files into `/etc/dotnet` which doesn't feel necessary.

### Use existing format for architecture specific environment variables

Since there's already support for `DOTNET_ROOT(x86)` on Windows extend the same
pattern to all architectures and OSes. So for example `DOTNET_ROOT(arm64)`
and so on.

We don't like this pattern as it feels too Windows-centric and would look
weird on non-Windows platforms.
