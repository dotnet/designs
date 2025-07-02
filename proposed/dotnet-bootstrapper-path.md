## 'dotnet' Bootstrapper PATH Issue

How can we enable an `nvm` or `rustUp` like experience for `dotnet`? In this document, the command to do so will be aliased as `b` for bootstrapper, and we will only discuss PATH concerns.

One of the core differences in our ecosystem compared to other toolchains is the inclusion of .NET in VS. Visual Studio and the MSI or 'global' .NET Installer set the System `PATH` on Windows and not the user level `PATH`. The system `PATH` always takes precedence over the user `PATH`.

If `b` sets the user path to enable a folder of local .NET SDKs to be discovered by the `dotnet.exe` application host at `DOTNET_ROOT`, via the `PATH`, those installs would become 'undetectable' to the runtime, debugger, etc when Visual Studio would update and modify the System `PATH`. Which, it does:  a core problem with the existing `dnvm` tool is that when `VS` automatically updates, `dnvm` breaks in the sense that its installs are no longer found, as VS updates the `PATH`.

On the other hand, if `b` set the system `PATH`, it would break `VS`, as Visual Studio would no longer find its installs.

An additional concern would be the `source-built` SDKs and the `pkg` installer.

There have been several methods suggested to handle this, which are listed below. None of them are ideal.

## Existing Tooling Behavior

The .NET Installers currently set the System Level `PATH` on Windows. These installers are also leveraged via VS, so their setup also sets the system level `PATH`.

The source-built SDKs installed via a package manager are installed to `/usr/lib/dotnet` or `/usr/lib64/dotnet` when using the built-in feeds. The `dotnet` exectuable is found because a `symlink` is added to represent `dotnet` in `/usr/bin/dotnet` which points to the `lib` folder. In this sense, the `PATH` does not need to be updated, and it does not get updated. (Previous behavior installed to `/usr/share/dotnet`.) There is no distinction between a system and user level `PATH`.

When utilizing `snap`, they are installed as a `symlink` to `snap/bin/dotnet` to `/usr/bin/snap`. The `PATH` is not modified as `snap/bin` is included, and snap knows how to resolve the `dotnet` executable.

On Mac, there is a 'system' `PATH` defined in `/etc/paths` and `etc/paths.d/`, which the `path_helper` utility reads. These environment variables apply across core system utilities and developer tooling. `zsh` uses `~./zshrc` as a bash_profile to load user level `PATH` settings and other contexts. It is generally considered bad practice to edit a user's `SHELL` profile without permission.

## How to set the `PATH`:

There are some possible issues with setting the user `PATH`.
The C# API for doing so is slow, which is documented in this issue:

https://github.com/dn-vm/dnvm/issues/201

The Windows API for setting the user path does a `COM` broadcast to every single open window on the OS and waits for a response. `chrome.exe`, for example, hangs for 1 minute when receiving this broadcast, which means setting the API for setting the system PATH can take a minute of dead-waiting.


### Update the Installers / VS to Set the User PATH :star:

The second issue is that this is a large breaking change, many users rely on the system PATH to be set by the installers/VS at this time. Existing user tooling may break VS if we change this behavior. We can set an environment variable such as `DOTNET_USE_USER_PATH` if a bootstrapper is installed, to tell the MSI/VS/Source-Build installers to not set the system path to narrow this scope, but this still introduces complexities with VS and the tool, and could be another justification to not interface as `dotnet`.


### Multi-level-lookup

`DOTNET_MULTILEVEL_LOOKUP` was designed to enable or disable the host/muxer/`dotnet.exe`'s ability to find SDKs or Runtimes in other locations. We could re-enable the muxer to look specifically at the directory of local installs that are placed by the bootstrapper or `b`, as proposed to be `DOTNET_HOME` in the other specification, [`dotnet-bootstrapper-cli-experience.md`](./dotnet-bootstrapper-cli-experience.md).

This would enable the lookup to work without modification to the `PATH`. We could add back this feature to a limited extent. However, `DOTNET_MULTILEVEL_LOOKUP` was deprecated and the feature removed in .NET 7 due to the user confusion it caused when it would find global SDKs in a user local context, as well as for performance reasons. The `host` team expressed concern that this feature caused a lot of grief when it was available. In one way of looking at it, by having some installers set the system path, the installers are fighting one another, so this is an 'installer' problem.

### Fail on Windows if System PATH set, if `paths` in `global.json` is not set

In this approach, we prioritize defined and replicable behavior over the easier UX behavior. System administrator installs are created in predictable locations, especially those from Visual Studio and/or package manager feeds.

What we could do is look for those feeds per install operation and decline to do anything when a user tries to run `b install`. The error message would point the user towards leveraging the `sdk: paths []` option in their repository's `global.json` file, which would specify to the host or `dotnet.exe` where to look for the installs of the .NET SDK or .NET runtimes. The host is already aware of this as of .NET 10.

This is subject to backlash, especially as only 2 to 6% of users are on a `global.json` according to internal metrics.

However, there is precedence in the sense that `nvm` is a Linux and macOS only tool, from an official standpoint. An alternative would be to make `dotnet install` only applicable to Linux and macOS. In this case, I would be more strongly opposed to using `dotnet` as the root command, and instead leaning towards making this an 'opinionated' installer that resolves the Linux and macOS scenario. This creates a split between OS support.

### First-Run Experience

The SDK, or another tool, could try to replicate the behavior of `DOTNET_MULTILEVEL_LOOKUP` when selected. This approach would require there to be an SDK that is sufficient for the use case according to the muxer, so I'm not sure about this one.

### Require Elevation

We could detect if a global installation is present like in the `Fail` category, but instead of failing, require elevation to continue, and then set the system `PATH`. However, this would cause Visual Studio to fail, if those SDKs did not meet the requirements for its installation. In this case, it may be possible to 'move' or re-install those SDKs demanded by Visual Studio into the local directory. Whether VS could run under those local SDKs or not would require more investigation. Of course, the tool could also support administrator installation and uninstallation. But that is rapidly expanding the scope of the toolchain and not the original intention of the design.
