## 'dotnet' Bootstrapper PATH Issue

How can we enable an `nvm` or `rustUp` like experience for `dotnet`? In this document, the command to do so will be aliased as `b` for bootstrapper, and we will only discuss PATH concerns.

One of the core differences in our ecosystem compared to other toolchains is the inclusion of .NET in VS. Visual Studio and the MSI or 'global' .NET Installer set the System `PATH` on Windows and not the user level `PATH`. The system `PATH` always takes precedence over the user `PATH`.

If `b` sets the user path to enable a folder of local .NET SDKs to be discovered by the `dotnet.exe` application host at `DOTNET_ROOT`, via the `PATH`, those installs would become 'undetectable' to the runtime, debugger, etc when Visual Studio would update and modify the System `PATH`. Which, it does:  a core problem with the existing `dnvm` tool is that when `VS` automatically updates, `dnvm` breaks in the sense that its installs are no longer found, as VS updates the `PATH`.

On the other hand, if `b` set the system `PATH`, it would break `VS`, as Visual Studio would no longer find its installs.

An additional concern would be the `source-built` SDKs and the `pkg` installer.

There have been several methods suggested to handle this, which are listed below. None of them are ideal.

## Existing Tooling Behavior

The .NET Installers currently set the System Level `PATH` on Windows if the installer matches the native architecture. These installers are also leveraged via VS, so their setup also sets the system level `PATH`.

https://github.com/dotnet/runtime/blob/main/src/installer/pkg/sfx/installers/host.wxs

The source-built SDKs installed via a package manager are installed to `/usr/lib/dotnet` or `/usr/lib64/dotnet` when using the built-in feeds. The `dotnet` exectuable is found because a `symlink` is added to represent `dotnet` in `/usr/bin/dotnet` which points to the `lib` folder. In this sense, the `PATH` does not need to be updated, and it does not get updated. (Previous behavior installed to `/usr/share/dotnet`.) There is no distinction between a system and user level `PATH`.

When utilizing `snap`, they are installed as a `symlink` to `snap/bin/dotnet` to `/usr/bin/snap`. The `PATH` is not modified as `snap/bin` is included, and snap knows how to resolve the `dotnet` executable.

On Mac, there is a 'system' `PATH` defined in `/etc/paths` and `etc/paths.d/`, which the `path_helper` utility reads. The `pkg` installer sets the System `PATH` if the installer matches the native architecture.

https://github.com/dotnet/runtime/blob/b79c4fbf284a6b002f46b1fc95ee39e545687515/src/installer/pkg/sfx/installers/osx_scripts/host/postinstall#L25

 These environment variables apply across core system utilities and developer tooling. `zsh` uses `~./zshrc` as a bash_profile to load user level `PATH` settings and other contexts. It is generally considered bad practice to edit a user's `SHELL` profile without permission.

## How to set the `PATH`:

There are some possible issues with setting the user `PATH`.
The C# API for doing so is slow, which is documented in this issue:

https://github.com/dn-vm/dnvm/issues/201

The Windows API for setting the user path does a `COM` broadcast to every single open window on the OS and waits for a response. `chrome.exe`, for example, hangs for 1 minute when receiving this broadcast, which means setting the API for setting the system PATH can take a minute of dead-waiting.


### Update the Installers / Visual Studio :star:

One option is to set the machine into a 'local' SDK mode that users could opt into. This 'mode' would tell the system-wide installers (both `pkg` and `msi`) not to system `PATH`. The advantage of this is that dotnet installations created by `b` would work even if Visual Studio updates. Another advantage of changing the behavior based on a user action is that this change would not break existing installations of VS or existing code/tooling scenarios without user interaction. The disadvantage is that Visual Studio / other tooling would no longer be able to find its SDKs once this mode was enabled.

In this scenario:
1. When the user interfaces with the tool `b`, to install an SDK, they are prompted about the change to `local` installs.
2. On Windows, a registry key is set in `HKLM` (the registry hive for local user contexts). `b` must elevate to enable this registry edit when prompting the user.

A registry key is preferred over an environment variable because it is an admin-protected context. We don't want user-local controllable behavior to influence/break the behavior of an administrative installer, as that is a security concern. A policy key may also be used, but a policy key is essentially equivalent to a registry key in this context.

On OS X, we could set this via a protected sentinel file.

3. The `PATH` is updated to a host that contains the local installs managed by `b`. Visual Studio and the other installers will no longer set the System `PATH` unless the registry key is changed, which the option to undo this would be available, perhaps via an `uninstall all` or `reset` CLI feature from `b`.

4. The `global.json` `paths` feature would still enable other scenarios like for Visual Studio. Visual Studio is making efforts to migrate to remove the .NET SDK from its tooling. In this sense, it may be possible to enable both scenarios correctly when this happens.

If we tried this approach as a prototype, and the experience was not pleasant, that may allow us to consider another option with better understanding, such as MLL.

### Multi-level-lookup

`DOTNET_MULTILEVEL_LOOKUP` was designed to enable or disable the host/muxer/`dotnet.exe`'s ability to find SDKs or Runtimes in other locations. We could re-enable the muxer to look specifically at the directory of local installs that are placed by the bootstrapper or `b`, as proposed to be `DOTNET_HOME` in the other specification, [`dotnet-bootstrapper-cli-experience.md`](./dotnet-bootstrapper-cli-experience.md).

This would enable the lookup to work without modification to the `PATH`. We could add back this feature to a limited extent. However, `DOTNET_MULTILEVEL_LOOKUP` was deprecated and the feature removed in .NET 7 due to the user confusion it caused when it would find global SDKs in a user local context, as well as for performance reasons. The `host` team expressed concern that this feature caused a lot of grief when it was available. In one way of looking at it, by having some installers set the system path, the installers are fighting one another, so this is an 'installer' problem.

### Require `paths` in `global.json`

In this approach, we prioritize defined and replicable behavior over the easier UX behavior. System administrator installs are created in predictable locations, especially those from Visual Studio and/or package manager feeds.

What we could do is look for those installations per install operation and decline to do anything when a user tries to run `b`. The error message would point the user towards leveraging the `sdk: paths []` option in their project or repositories `global.json` file, which would specify to the host or `dotnet.exe` where to look for the installs of the .NET SDK. The host is already aware of this as of .NET 10.

Only 2 to 6% of users are on a `global.json` according to internal metrics, so this gesture will create additional work when using `dotnet`.

### Fail on Windows

`b` could simply not function on Windows due to concerns of breaking VS. The easiest way to do that is to not ship it on Windows, so users never experience a failure gesture. If `b` is included in the `SDK`, that is more difficult, and a failure message is likely necessary.

There is precedence in the sense that the official `nvm` tool is Linux and macOS only. However, this creates a fractured experience using `dotnet` based upon the OS, which is something we have tried to move away from.

### Require `use` Command

We could modify the host to accept a specific installation to use. This could be enabled via `b use 9.0.1xx`, which would create a folder containing the local install or use some other mechanism to specify to the host an exact install of the SDK to use in the context of the specified terminal. The `global.json` can already do this by pinning an SDK and setting the `paths` feature, so the value of this is murky.

### Shadow `dotnet` Commands

The tooling could be written such that it calls into the `dotnet.exe`, but specifies a `DOTNET_ROOT` that leverages the local installs. This would require `b` to use a different name than `dotnet`. This approach does not break any existing scenarios and is impervious to VS but it would require significant user and branding changes to adopt to this new pattern, which would also cannibalize the existing `dotnet` verbiage.

### Set the System PATH

We could detect if a global installation is present like in the `Fail` category, but instead of failing, require elevation to continue, and then set the system `PATH`. However, this would cause Visual Studio to fail, if those SDKs did not meet the requirements for its installation. In this case, it may be possible to 'move' or re-install those SDKs demanded by Visual Studio into the local directory. Whether VS could run under those local SDKs or not would require more investigation. Of course, the tool could also support administrator installation and uninstallation. But that is rapidly expanding the scope of the toolchain and not the original intention of the design.
