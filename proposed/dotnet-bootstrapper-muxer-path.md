# `dotnet install` interface as `DNVM` and `PATH` Management

The idea of a .NET bootstrapper ala `nvm` is proposed with a designed CLI in [`dotnet-bootstrapper-cli-experience.md`](./dotnet-bootstrapper-cli-experience.md). This document expands upon how this application could be implemented.

## Interfacing as `dotnet`

In some sense, having the .NET SDK be able to manage itself and update itself would be convenient for users. Other toolchains provide a separate management executable (`nvm` as opposed to `node`, and similar for `rustUp`). This motivates the idea of having a bootstrapper ala `nvm` or `dnvm` that interfaces as `dotnet`, to run `dotnet install`, as specified in [`dotnet-bootstrapper-cli-experience.md`](./dotnet-bootstrapper-cli-experience.md). In future reference of this document, `dotnet` will be used, but the name of the root command is up for debate. Particularly so, because having `dotnet install` utilize local zip installs is a departure from many existing conventions.

The question if we were to build this experience of interfacing as `dotnet`, is how? And how would it be acquired? Several methods have been suggested.

### Add a binary that lives alongside the muxer/dotnet.exe

A bootstrapper executable, say, `dotnet-bootstrapper.exe` could live either beside `dotnet.exe` or in `DOTNET_HOME` as a standalone AOT executable. The `dotnet.exe` already shells out to the .NET SDK if the SDK is available for unknown commands. If the executable is in `DOTNET_HOME`, it may have to be signature verified every time, because `dotnet.exe` can run under an administrator context. When calling this bootstrapper executable, administrator privileges should be revoked.

This bootstrapper would be acquired in the same fashion as `nvm`:
Via a website with a simple install script that downloads the executable.

The .NET SDK would also shell out to the bootstrapper executable for commands it does not know, aka `dotnet install`, `dotnet uninstall`, etc. This way the bootstrapper could automatically update itself, say for example when the `releases.json` url changed, this could be a breaking change requiring a self-update to function.

From a security context, it may be better to bundle this alongside the host in the same directory, for this would share the same security context.

In theory, this is 'doable' for .NET 10 in terms of changing the muxer to know about a specified bootstrapper location, which would enable these scenarios outside of just .NET 11.


### Create a C# Muxer

The muxer would become a bootstrapper and the muxer itself. It would cease to become C++ and become ported C# code that is run as native AOT code. This is high-risk and also likely still lower performance than a C++ application.


## PATH Issue

Visual Studio and the MSI or 'global' .NET Installer set the System `PATH` on Windows and not the user level `PATH`. The system `PATH` always takes precedence over the user `PATH`. If `dnvm` sets the user path to enable a folder of local .NET SDKs to be discovered by the `dotnet.exe` application host at `DOTNET_ROOT`, via the `PATH`, those installs would become 'undetectable' to the runtime, debugger, etc when Visual Studio would update and modify the System `PATH`.

This is also applicable to `source-built` SDKs and the `pkg` installer.

There have been several methods suggested to handle this. None of them are ideal.

### Fail on Windows if System PATH set, if `paths` in `global.json` is not set

In this approach, we prioritized defined and replicable behavior over the easier UX behavior. System administrator installs are created in predictable locations, especially those from Visual Studio and/or package manager feeds.

What we could do is look for those feeds per install operation and decline to do anything when a user tries to run `dotnet install`. The error message would point the user towards leveraging the `sdk: paths []` option in their repository's `global.json` file, which would specify to the host or `dotnet.exe` where to look for the installs of the .NET SDK or .NET runtimes. The host is already aware of this as of .NET 10.

This is subject to backlash, especially as only 2 to 6% of users are on a `global.json` according to internal metrics.

However, there is precedence in the sense that `nvm` is a Linux and macOS only tool, from an official standpoint. An alternative would be to make `dotnet install` only applicable to Linux and macOS. In this case, I would be more strongly opposed to using `dotnet` as the root command, and instead leaning towards making this an 'opinionated' installer that resolves the Linux and macOS scenario. This creates a split between OS support.

### Update the Installers / VS to Set the User PATH

The Windows official guidance is to set the system level PATH, not to set the global `PATH`. However, there are some possible issues with setting the system `PATH`. For one, we believe that the Windows API for setting the user path does a `COM` broadcast to every single open window on the OS and waits for a response. `chrome.exe`, for example, hangs for 1 minute when receiving this broadcast, which means setting the API for setting the system PATH can take a minute, mostly dead waiting.

The second issue is that this is a large breaking change, many users rely on the system PATH to be set by the installers/VS at this time. Existing user tooling may break VS if we change this behavior. We can set an environment variable such as `DOTNET_USE_USER_PATH` if a bootstrapper is installed, to tell the MSI/VS/Source-Build installers to not set the system path to narrow this scope, but this still introduces complexities with VS and the tool, and could be another justification to not interface as `dotnet`.


### Multi-level-lookup

`DOTNET_MULTILEVEL_LOOKUP` was designed to enable or disable the host/muxer/`dotnet.exe`'s ability to find SDKs or Runtimes in other locations. We could re-enable the muxer to look specifically at the directory of local installs that are placed by the bootstrapper or `dotnet`, as proposed to be `DOTNET_HOME` in the other specification, [`dotnet-bootstrapper-cli-experience.md`](./dotnet-bootstrapper-cli-experience.md).

This would enable the lookup to work without modification to the `PATH`. We could add back this feature to a limited extent. However, `DOTNET_MULTILEVEL_LOOKUP` was deprecated and the feature removed in .NET 7 due to the user confusion it caused when it would find global SDKs in a user local context, as well as for performance reasons. The `host` team expressed concern that this feature caused a lot of grief when it was available. In one way of looking at it, by having some installers set the system path, the installers are fighting one another, so this is an 'installer' problem.


### First-Run Experience

The SDK, or another tool, could try to replicate the behavior of `DOTNET_MULTILEVEL_LOOKUP` when selected. This approach would require there to be an SDK that is sufficient for the use case according to the muxer, so I'm not sure about this one.

### Require Elevation

We could detect if a global installation is present like in the `Fail` category, but instead of failing, require elevation to continue, and then set the system `PATH`. However, this would cause Visual Studio to fail, if those SDKs did not meet the requirements for its installation. In this case, it may be possible to 'move' or re-install those SDKs demanded by Visual Studio into the local directory. Whether VS could run under those local SDKs or not would require more investigation. Of course, the tool could also support administrator installation and uninstallation. But that is rapidly expanding the scope of the toolchain and not the original intention of the design.
