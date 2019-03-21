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

Typically the muxer from this install is registered to be readily accessible without specifying full path. The exact mechanism is platform specific, for example on Windows this means that the muxer is added to the `PATH`.

*This type of install is supported on all versions of .NET Core.*

### Global install to custom location
.NET Core is installed globally for the machine but into a non-default (custom) location. The location of the install is stored in platform specific configuration so that it can be found by everything on the system (and by all users).

This type of install should effectively behave the same as install to the default location. Can be used to put .NET Core on a different drive to save space on the system drive, or to serve other purposes.
Typically the muxer from this install is registered to be readily accessible without specifying full path. The exact mechanism is platform specific, for example on Windows this means that the muxer is added to the `PATH`.

Installer behavior:
* There can only be one globally registered location.
* The installer should query the system for existing .NET Core global installs. If it finds any, it should only install to the already registered location, it should not install to a new location.

*Custom install locations are supported for .NET Core 3.0 on Windows, other cases are not yet committed.*

Global installs (both default or custom location) are registered in a well know location:
* Windows - registry `HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\<arch>\InstallLocation` (32-bit view only)
* Linux/macOS - file `/etc/dotnet/install_location.conf`

### Private install (also called x-copy install)
.NET Core is "installed" by simply creating the right layout on the disk with the right files, there's no system registration mechanism attached to it (typically called x-copy deployment). The most common way is to unzip a build into a directory.

This can be used for
* Project-local .NET Core SDK/runtime. Most typically used to have a stable environment for build and related tools to run with - regardless of what is available on the machine. (For example almost all dotnet repos use this approach.)
* Experimentation

As such it's actually desirable that creating such installation doesn't affect anything which didn't opt into using it.
Using this install must be intentional/explicit, either by using full path to invoke the muxer or by setting environment variable (`DOTNET_ROOT`).

*This type of install is supported on all versions of .NET Core.*

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

The problem already exists since the `apphost` has been shipped in .NET Core 2.2 with support for framework dependent apps. The feature is used only occasionally. The most common case is probably dotnet global tools (`dotnet tool install -g <tool>`) which create framework dependent `apphost` for each installed tool.

In .NET Core 3.0 framework dependent apps use an `apphost` by default, which makes this case far more prevalent. Scenarios requiring the other hosts are also likely to be relatively common (COM, IJW, native hosting). So the hosts which we ship with .NET Core 3.0 will be spread across all machines running .NET Core (not just development but production as well) without any ability to service them directly.

This means that .NET Core 3.0 is the last chance to get the algorithms for install locations modified in "breaking ways". Future release will very likely have to be 100% backward compatible in this regard.


## Proposal for 3.0

### Globally registered install location :new:
This is by far the most common case and in 3.0 we want to provide a way to globally install to a custom location. The typical case will still be to install into the default location, but custom location should be possible. Either location should behave the same and which one is used is solely decision of the installer.

Note that the installer should try to avoid creating two global locations as only one can be registered. So if there is already some version installed it should only add to that location and create a new one. The only way to get two global locations should be to first install 3.0 into custom location and then install 2.* which always installs to the default location.

To achieve the desired behavior the install location must be recorded in some system-wide configuration in a fixed location which is well-known.

* Windows - the installer will store the install location in 32-bit registry (for both 32-bit and 64-bit installations):
  * `HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\<arch>\InstallLocation` - where `<arch>` is one of the `x86`, `x64`, `arm` or `arm64`.
  Note that this registry key is "redirected" that means that 32-bit processes see different copy of the key then 64bit processes. So it's important that both installers and the host access only the 32-bit view of the registry.
* Linux and macOS - the installer will store the install location in a text file
  * `/etc/dotnet/install_location.conf` - the file will contain a single line which is the path.

The muxer is registered on the system in such a way that it is readily accessible without providing full path to it. The exact mechanism is platform specific and outside of the scope of this document. (For example on Windows it's added to `PATH`)

### Private install location
This allows users to install private copy of .NET Core into any directory. This copy should not be used by anything unless explicitly asked for.

There is not installer for this scenario, these installs are produced by simply copying the right directory structure into a custom location.

If the muxer (`dotnet`) is used then it must be invoked directly from the private install location (it's not registered on `PATH` in any way).  
If other hosts are used then the private install location must be specified in environment variable:
* `DOTNET_ROOT`
* `DOTNET_ROOT(x86)` for 32-bit process running on 64-bit Windows.

*Note: this type of install is supported in all versions of .NET Core and there are no changes proposed to how it's created and used.*


## Host behavior for 3.0

### `hostfxr` search
`dotnet` only looks right next to itself.

All other hosts will search for the first location which exists in this order:
* the directory where the host resides (this is important mostly for `apphost` as self-contained apps will have the `hostfxr` in the same directory).
* `DOTNET_ROOT` (or `DOTNET_ROOT(x86)`) environment variable, in the `host/fxr/<version>` subdirectory.
* :new: global registered install location, in the `/host/fxr/<version>` subdirectory  
  **We've committed to support this on Windows for .NET Core 3.0**
  * Windows - `HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\<arch>\InstallLocation` (32-bit view)  
  * Linux/macOS - `/etc/dotnet/install_location.conf`  
* default global location, in the `/host/fxr/<version>` subdirectory
  * Windows (32-bit process on 64-bit Windows) - `%ProgramFiles(x86)%\dotnet`
  * Windows (everything else) - `%ProgramFiles%\dotnet`
  * Linux - `/usr/share/dotnet`
  * macOS - `/usr/local/share/dotnet`

In each case with the `<version>` subdirectory, the latest available version as per SemVer 2.0 rules will be selected. Note that the algorithm picks the first location which exists, and only then looks for the `/host/fxr/<version>` subdirectory, and if that doesn't exist, or the library is missing it will fail.

The reason for including the default global location is so that 3.0 host can find 2.* installs since those are not registered in the system. All 3.0 and higher installs should be registered.

### Framework and SDK search
All hosts use the same logic.

Both framework and SDK search uses the same logic, the only difference is that frameworks are in the `share` subdirectory, while SDKs are in the `sdk` subdirectory of the install location.
In both cases all locations listed below are searched and the complete list of all available SDKs/Frameworks is combined before making a choice which one will be used (based on version requirements).

**Important:** There's a difference in search algorithm for `hostfxr` and for frameworks/SDKs. For `hostfxr` only the first available location is considered (and the highest version from that location is selected). For frameworks/SDKs **all** locations are considered and the best match for the requested version is selected from all those locations.

Search locations:
* the location of the `hostfxr` used. See the `hostfxr` search algorithm above. This means that `DOTNET_ROOT` has effect on framework and SDK search as well since the `hostfxr` search will consider it.  
*If the `hostfxr` is found in the `DOTNET_ROOT` location (should happen pretty much always when the variable is non-empty), then that location will be part of the search locations for frameworks/SDKs.*
* If multi-level lookup is enabled (by default it is, can be disabled via `DOTNET_MULTILEVEL_LOOKUP=0` environment variable), then the following locations are searched as well:
  * :new: global registered install location  
    * Windows - `HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\<arch>\InstallLocation` (32-bit view)  
    **We've committed to support this in .NET Core 3.0 (Windows)**
    * None on Linux/macOS
  * default global location, in the `/host/fxr/<version>` subdirectory
    * Windows (32-bit process on 64-bit Windows) - `%ProgramFiles(x86)%\dotnet`
    * Windows (everything else) - `%ProgramFiles%\dotnet`
    * None on Linux/macOS
* Else multi-level lookup is disabled, no other search locations are considered.

The reason for including the default global location is so that 3.0 host can find 2.* installs since those are not registered in the system. All 3.0 and higher installs should be registered.

*Note: As of .NET Core 2.2 Linux/macOS effectively doesn't support multi-level lookup as there are no additional search paths considered when it's turned on.*

The multi-level lookup feature is only useful for private installs (xcopy). Installs into global locations typically don't want/need multi-level lookup. Important scenarios:
* Muxer from global location - for example running `dotnet build`. This will already use the global location for framework/SDK search since it's the one next to the used `hostfxr`. Multi-level lookup in this case doesn't add any value.
* `apphost` relying on global location - this is basically the default case for .NET Core 3.0. Framework dependent `apphost` which loads `hostfxr` from global location. This case will also already use global location for framework/SDK search due to it using `hostfxr` from the global location. Multi-level lookup doesn't add any value.
* Muxer from a private install (xcopy) - typically running `dotnet` using full path. This will use `hostfxr` from the private install and thus will use the private install to search for frameworks/SDKs. Multi-level lookup in this case helps by also including global locations in the frameworks/SDKs search. Only needed if trying to run apps which require frameworks/SDKs not in the private install.
* `apphost` using private install through `DOTNET_ROOT`- similar to the above case of muxer from private install. In this case it's really only about framework search (`apphost` doesn't really support SDKs). Multi-level lookup can help in some special cases, but mostly is not desirable.

Open question:  
**Should we include the global registered and global default location in framework/SDK search on Linux/macOS?**
These are only used when multi-level lookup is enabled. The entire multi-level lookup features in its current form seems to be rather confusing and there are discussions around phasing it out. See dotnet/core-setup#3606.  
Possible options:
* Find a replacement solution which is better then multi-level lookup in its current form - dotnet/core-setup#3606. This is a separate discussion from this document.
* Leave Linux/macOS as-is - that is effectively not supporting multi-level lookup on these platforms.
* Enable multi-level on Linux/macOS in its current form - making it consistent with Windows.

Proposal:
**Leave it as-is on Linux/macOS.**  
Pros:
* Most important scenarios (as described above) will work just fine if we add the global locations into `hostfxr` search. The scenarios where multi-level lookup helps don't work on Linux/macOS today and we haven't got much feedback to enable it.
* Avoids extending the controversial multi-level lookup to Linux/macOS. Once we figure out the replacement story, that should be implemented on Linux/macOS, but there won't be any backward compat burden.

Cons:
* Inconsistent behavior between Windows and Linux/macOS

## Discussed questions

### Make muxer less special
Proposal is to leave muxer as is - don't use `DOTNET_ROOT` in it.

Leaving the below as interesting discussion...
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

### Support for per-user installation
This would in theory allow installation which would be registered in `PATH` (and similar mechanism) but would be per-user and thus not require admin/root rights to install.

This approach has severe security implications though:
* The hosts would need to consider the per-use location during their search of `hostfxr` and frameworks/SDKs.
* As such it would be possible for malicious code to register per-user location without admin rights.
* Later on tool running under admin rights would consider the per-user location and allow loading code from effectively arbitrary location into the context of elevated process.

As such there is no proposal to add any per-user installation type right now.