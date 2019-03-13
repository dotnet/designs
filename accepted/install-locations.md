# Install location search for .NET Core

.NET Core has several cases where it needs to search for the install location of the runtime and SDKs. This document described the proposed algorithm for how this search will be done on all the supported platforms.

## .NET Core install layout
The content of a .NET Core install location always follows the same layout. The directory structure is the same on all platforms, file names may differ to conform with the platform specific requirements. (Below sample uses file names for Windows):
* `./dotnet.exe` - the "muxer" which is used to execute apps and issue dotnet CLI commands.
* `./host/fxr/<version>/hostfxr.dll` - this library contains all the logic to resolve framework references and select SDK. Regardless of application, the latest available version is used.
* `./shared` - contains shared frameworks.
  * `./Microsoft.NETCore.App/<version>` - the root framework which contains the .NET Core runtime. This framework contains `hostpolicy.dll` which is the entry point called by the `hostfxr` to run the app. Applications can choose which version to use.
  * `./<FrameworkName>/<version>` - any other shared framework. Applications can choose which version to use.
* `./sdk/<version>` - only installed when .NET Core SDK is installed - contains the SDK tools. Users can choose which version to use.


## Installation types
.NET Core can be installed in several ways, each provides a different location and expected behavior. Each such location will contain the .NET Core install layout described above.

### Global install to default location
This is the most common case where an installer installs the runtime (and potentially SDK) to a global default location accessible to everything on the system (and by all users).
* Windows
  * 32-bit on 64-bit OS - `%ProgramFiles(x86)%\dotnet`
  * Otherwise - `%ProgramFiles%\dotnet`
* Linux - the host searches in `/usr/share/dotnet` as a fallback, but the installer may pick a different default based on the requirements of the specific distro/configuration. In such case the install is effectively just like the global install to custom location described below. If correctly registered the host will search the custom location first.
* macOS - `/usr/local/share/dotnet`

*This type of install is supported by .NET Core 2.2 and earlier.*

### Global install to custom location
.NET Core is installed globally for the machine but into a non-default (custom) location. The location of the install is stored in platform specific configuration so that it can be found by everything on the system (and by all users).

This type of install should effectively behave the same as install to the default location. Can be used to put .NET Core on a different drive to save space on the system drive, or to serve other purposes.

*This type of install will for sure be supported by .NET Core 3.0 on Windows, other cases are not yet committed.*

### Per-user install
.NET Core installation which is per-user, thus doesn't require admin/root access to the machine. Otherwise it should behave just like a global install, but only accessible to the user which installed it.

There should be a default location specific for each platform - up for discussion.

There would also be a platform specific way to store this location in some kind of configuration so that it can be found by all apps for the given user.

*This type of install is not yet planned for any specific .NET Core release.*

### Private install (also called x-copy install)
.NET Core is "installed" by simply creating the right layout on the disk with the right files, there's no system registration mechanism attached to it (typically called x-copy deployment).

This can be used for
* Project-local .NET Core SDK/runtime (for example most dotnet repos use this)
* Experimentation

As such it's actually desirable that creating such installation doesn't affect anything which didn't opt into using it.

*This type of install is supported by .NET Core 2.2 and earlier.*

### Self-contained apps
Technically speaking self-contained apps also "install" a private copy of the runtime, but they don't use the same layout and such installation is only usable by the app itself. So for the purposes of this document, self-contained apps are largely out of scope.


## Components
These are the components of .NET Core which must be aware of the install location:
* The host (muxer (`dotnet`), `apphost`, `comhost`, ...) - the host needs to be able to find the location to search for
  * Latest version of `hostfxr` to use.
  * Shared frameworks
  * SDKs
* Installer - the installer needs to put the files into the right layout and register the install location with the system configuration (if applicable)
* Applications
  * If the application is using the muxer (`dotnet`) it needs to be able to find it. This applies to tools like MSBuild or VS as well.
  * Certain tools (installers, development tools) may need to determine if .NET Core is installed and which versions are available.


## Servicing considerations
The algorithm which determines where to search, is to some degree contained in all the components mentioned above. Changes to the algorithm (for example introduction of a new default location or new install type) need to be applied to all of these components.

Unfortunately some of these components are hard to service:
* The muxer relies on the caller to locate it, then it's shared by all versions installed in any given location. Updating it is troublesome as it typically require a system reboot (may be in use by running applications).
* The `hostfxr` is relatively easy to service since the installer places a newer version side-by-side and relies on the host to pick the latest available version. The complexity comes from the fact that it has to work with all versions of the runtime/sdk.
* App-contained hosts (`apphost`, `comhost`, `ijwhost`, `nethost`) these are libraries which are shipped with the apps and thus fully owned/serviced by the app. They need to know how to find install locations to locate shared frameworks.

The biggest problem is with the app-contained hosts which we can't service in any way. So introducing new install types or changing the default location can be breaking for apps using old hosts.

The problem already exists since the `apphost` has been shipped in .NET Core 2.1 with support for framework dependent apps. Fortunately this feature is not widely used yet.

In .NET Core 3.0 the situation will be very different. Framework dependent `apphost` is the default for apps. Scenarios requiring the other hosts are also likely to be relatively common (COM, IJW, native hosting). So the hosts which we ship with .NET Core 3.0 will be spread across all machines running .NET Core (not just development but production as well) without any ability to service them directly.

This means that .NET Core 3.0 is the last chance to get the algorithms for install locations modified in "breaking ways". Future release will very likely have to be 100% backward compatible in this regard.


## Proposal for 3.0

### Globally registered install location :new:
This is by far the most common case and in 3.0 we want to provide a way to globally install to a custom location. The typical case will still be to install into the default location, but custom location should be possible. Either location should behave the same.

To achieve this the install location must be recorded in some system-wide configuration in a fixed location which is well-known.

* Windows - the installer will store the install location in 32-bit registry (for both 32-bit and 64-bit installations):
  * `HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\<arch>\InstallLocation` - where `<arch>` is one of the `x86`, `x64`, `arm` or `arm64`.
  Note that this registry key is "redirected" that means that 32-bit processes see different copy of the key then 64bit processes. So it's important that both installers and the host access only the 32-bit view of the registry.
* Linux and macOS - the installer will store the install location in a text file
  * `/etc/dotnet/install_location` - the file will contain a single line which is the path.

The muxer (`dotnet.exe`) is registered on the system-wide `PATH` environment variable.

### Per-user registered install location :new:
This will allow installs to per-user directories (no need for admin/root access). It's to be decided if we will actually create an installer which does this. Once installed the per-user install should take precedence over the global one, but otherwise should be available to all processes running under the respective user.

To achieve this the install location must be recorded in some per-user configuration in a fixed location which is well-known.

* Windows - the installer will store the install location in registry:
  * `HKCU\SOFTWARE\dotnet\Setup\InstalledVersions\<arch>\InstallLocation` - where `<arch>` is one of the `x86`, `x64`, `arm` or `arm64`.
  Note that this key is "shared" meaning both 32-bit and 64-bit processes see the same values.
* Linux and macOS - the installer will store the install location in a text file
  * `$HOME/.dotnet/install_location` - the file will contain a single line which is the path.

The muxer (`dotnet`) is registered on the per-user `PATH` environment variable and in such a way that it wins over any globally installed one.

### Private install location
This allows users to install private copy of .NET Core into any directory. This copy should not be used by anything unless explicitly asked for.

There is not installer for this scenario, these installs are produced by simply copying the right directory structure into a custom location.

*This mechanism already exists in .NET Core 2.2.*

If the muxer (`dotnet`) is used then it must be invoked directly from the private install location (it's not registered on `PATH` in any way).  
If other hosts are used then the private install location must be specified in environment variable:
* `DOTNET_ROOT`
* `DOTNET_ROOT(x86)` for 32-bit process running on 64-bit Windows.


## Host behavior for 3.0

### `hostfxr` search
`dotnet` only looks right next to itself.

All other hosts will search for the first location which exists in this order:
* the directory where the host resides (this is important mostly for `apphost` as self-contained apps will have the `hostfxr` in the same directory).
* `DOTNET_ROOT` (or `DOTNET_ROOT(x86)`) environment variable, in the `host/fxr/<version>` subdirectory.
* :new: per-user registered install location, in the `/host/fxr/<version>` subdirectory
  * Windows - `HKCU\SOFTWARE\dotnet\Setup\InstalledVersions\<arch>\InstallLocation`
  * Linux/macOS - `$HOME/.dotnet/install_location`
* :new: global registered install location, in the `/host/fxr/<version>` subdirectory  
  **We've committed to support this on Windows for .NET Core 3.0**
  * Windows - `HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\<arch>\InstallLocation` (32-bit view)  
  * Linux/macOS - `/etc/dotnet/install_location`  
* default global location, in the `/host/fxr/<version>` subdirectory
  * Windows (32-bit process on 64-bit Windows) - `%ProgramFiles(x86)%\dotnet`
  * Windows (everything else) - `%ProgramFiles%\dotnet`
  * Linux - `/usr/share/dotnet`
  * macOS - `/usr/local/share/dotnet`

In each case with the `<version>` subdirectory, the latest available version as per SemVer 2.0 rules will be selected. Note that the algorithm picks the first location which exists, and only then looks for the `/host/fxr/<version>` subdirectory, and if that doesn't exist, or the library is missing it will fail.

### Framework and SDK search
All hosts use the same logic.

Both framework and SDK search uses the same logic, the only difference is that frameworks are in the `share` subdirectory, while SDKs are in the `sdk` subdirectory of the install location.
In both cases all locations listed below are searched and the complete list of all available SDKs/Frameworks is combined before making a choice which one will be used (based on version requirements).

Search locations:
* the location of the `hostfxr` used. See the `hostfxr` search algorithm above. This means that `DOTNET_ROOT` has effect on framework and SDK search as well.
* If multi-level lookup is enabled (by default it is, can be disabled via `DOTNET_MULTILEVEL_LOOKUP=0` environment variable)
  * :new: per-user registered install location
    * Windows - `HKCU\SOFTWARE\dotnet\Setup\InstalledVersions\<arch>\InstallLocation`
    * Linux/macOS - `$HOME/.dotnet/install_location`
  * :new: global registered install location  
    **We've committed to support this on Windows for .NET Core 3.0**
    * Windows - `HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\<arch>\InstallLocation` (32-bit view)  
    * Linux/macOS - `/etc/dotnet/install_location`
  * default global location, in the `/host/fxr/<version>` subdirectory
    * Windows (32-bit process on 64-bit Windows) - `%ProgramFiles(x86)%\dotnet`
    * Windows (everything else) - `%ProgramFiles%\dotnet`
    * :new: Linux - `/usr/share/dotnet`
    * :new: macOS - `/usr/local/share/dotnet`

*Note: The above means that on Linux/macOS in 2.2 the multi-level lookup was effectively non-functional, as there were no search paths for it. Part of the 3.0 changes is to change that and make Linux/macOS work similarly to Windows in this case as well.*


## Open questions

### Support for per-user installation
So far there's no support for per-user installation. It would make sense to add support for this as we may need it in the future. For example VS Code per-user installation can't install .NET Core and thus .NET Core doesn't work in it out of the box. A system wide install of .NET Core is required and has to be done as a separate step which requires admin/root access.

If there's at least some sense of future scenarios for this we should add the support for this into the hosts. As we won't get a chance to really update the algorithms in the hosts due to servicing problems described above.

We can not ship any installers like this in .NET Core 3.0, just have support for it in the hosts. Later on when we get the scenario we can ship a new installer and everything will just work.

#### Default location for per-user installation
If we decide to provide per-user installation it should have a good default location.
* Windows - `%HOMEPATH%\.dotnet\<arch>` is probably the best. `%HOMEPATH%\.dotnet` already exists and is used for example for global tools and NuGet fallback cache.
* Linux - `$HOME/.dotnet` probably. The location already exists and stores global tools.
* macOS - `$HOME/.dotnet` (or whatever is the equivalent where we store global tools).

Note that the host would not look for the default per-user location, it would solely rely on the registration mechanism described above, so the default per-user location is really only a convenience feature of the installer.


### Make muxer less special
As mentioned above, the muxer has certain specific behavior:
* It only looks next to itself when searching for `hostfxr`.
* It ignores `DOTNET_ROOT` environment variable.

#### Framework and SDK search
I think the muxer should use `DOTNET_ROOT` to resolve frameworks and SDKs just like all the other hosts (the actual implementation mechanism aside). The current discrepancy is simply unexpected and makes certain scenarios unnecessarily complex (for example VS can't really use private installs because of this, since it always uses the muxer from path).

In itself this change has no real downside. It has a small potential for being breaking: If an application uses the muxer and also has the `DOTNET_ROOT` set, previously the environment variable would be ignored and thus the app would only find frameworks/SDKs in the global locations. After the change to muxer, the muxer would suddenly also find frameworks/SDKs in the custom location.
* For frameworks this is very little risk. The only observable change would be to an app which for example requires framework version `2.0.0`, but the machine only has `2.1.0` in the global location. If the custom location in `DOTNET_ROOT` would have for example `2.0.1` then after the change the app would start using the private `2.0.1` instead. In theory this is for the better as that framework better fulfills the app requirements.
* For SDKs this is of a slightly higher risk. If the custom location in `DOTNET_ROOT` contains SDK with higher version than that available in global location, the default selection would be the higher version from the custom location. If the app uses the SDK to create new applications this might also change the TFM of the created apps. SDKs should be backward compatible though, so the newer SDK should work fine on older apps anyway.

#### `hostfxr` search
Where the muxer should search for `hostfxr` is a more complicated issue.
As of 2.2 only the location right next to the muxer is considered. This effectively means that `hostfxr` should always be equal or newer version than the muxer itself.

Note that search for `hostfxr` only looks for first available location. Unlike frameworks/SDKs which search all available locations at once. So for `hostfxr` the algorithm will not find the absolute highest version from all locations, only the highest version from the first available location.

If the muxer doesn't change and only uses `hostfxr` next to itself:
* Combined with multi-level lookup and the above suggested support for `DOTNET_ROOT` would mean that potentially lower version `hostfxr` will try to work with higher version frameworks, and specifically higher version `hostpolicy`. This is a case which already exists in 2.2, but is probably not very common. It introduces strict backward compatibility requirements on `hostpolicy` interface. We already treat both `hostfxr` and `hostpolicy` APIs as public and thus needing backward compat behavior, this just makes it a bit more demanding.
* Due to the above, potential new features supported by higher versions would not be available in this scenario as the `hostfxr` would not support those.

If the muxer changes then the question is how much:
* Currently we only support using muxer which has `hostfxr` next to itself, otherwise it always fails. With the change that would no longer be absolutely necessary.
* It can use the exact same algorithm as `apphost` uses, which would make it consistent, but it would expand the current behavior quite a bit (not only `DOTNET_ROOT` but also globally registered locations would be considered).
* Alternatively it could only use `DOTNET_ROOT` and its location (`DOTNET_ROOT` would need to be preferred for it to make a difference).
* Another consideration is reacting to multi-level lookup in `hostfxr` search as well. This would unify the probing logic for `hostfxr` with that for frameworks and SDKs. This is the only option which would effectively guarantee the usage of highest available version of `hostfxr` and thus effectively remove the problem of using older `hostfxr` with newer `hostpolicy`.

Looking for recommendations.


### Describe targeting packs
In .NET Core 3.0 aside from the shared frameworks (which are effectively also runtime packs) there's also the notion of targeting packs. The document might mention them, where they live and if the installation types have any effect on their behavior.