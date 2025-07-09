## 'dotnet' Bootstrapper PATH Issue

How can we enable an `nvm` or `rustUp` like experience for `dotnet`? In this document, the command to do so will be aliased as `b` for bootstrapper, and we will only discuss PATH concerns.

One of the core differences in our ecosystem compared to other toolchains is the inclusion of .NET in VS. Visual Studio and the MSI or 'global' .NET Installer set the System `PATH` on Windows and not the user level `PATH`. The system `PATH` always takes precedence over the user `PATH`.

If `b` sets the user path to enable a folder of local .NET SDKs to be discovered by the `dotnet.exe` application host at `DOTNET_ROOT`, via the `PATH`, those installs would become undetectable to the runtime, debugger, etc. when Visual Studio updates and modifies the System `PATH`. With changes in .NET 7+, `DOTNET_MULTI_LEVEL_LOOKUP` is no longer enabled, and once an install is detected in one location, the lookup does not fall back. Thus, the break to `b` occurs because when Visual Studio automatically updates, it modifies the `PATH`, which can cause the `b`'s managed installs to no longer be found.

On the other hand, if `b` set the system `PATH`, it would modify the behavior of Visual Studio, as Visual Studio would no longer find its installs. This could be considered a breaking change to Visual Studio. In this scenario, Visual Studio may begin to run on a different SDK than the one it shipped with, based on whichever SDKs the user has installed that can be used with the user project (based on the major.minor version of .NET and host resolution logic for the specific context.) This is already possible today by installing a different administrative SDK that is newer than the one Visual Studio has. However, it is not possible to install a downgrade of an administrative install in the specific version-set without uninstalling the SDK owned by Visual Studio first. This also means that updates of the SDK that Visual Studio includes would no longer take effect within Visual Studio, as the update mechanism would need to be handled by `b`.

An additional concern would be the `source-built` SDKs and the `pkg` installer.

There have been several methods suggested to handle this, which are listed below.

## Existing Tooling Behavior

The .NET Installers currently set the System Level `PATH` on Windows if the installer matches the native architecture. These installers are also leveraged via VS, so their setup also sets the system level `PATH`.

https://github.com/dotnet/runtime/blob/main/src/installer/pkg/sfx/installers/host.wxs

The source-built SDKs installed via a package manager are installed to `/usr/lib/dotnet` or `/usr/lib64/dotnet` when using the built-in feeds. The `dotnet` executable is found because a `symlink` is added to represent `dotnet` in `/usr/bin/dotnet` which points to the `lib` folder. In this case, the `PATH` does not need to be updated, and it does not get updated. (Previous behavior installed to `/usr/share/dotnet`.) There is no distinction between a system and user level `PATH`.

When utilizing `snap`, they are installed as a `symlink` to `snap/bin/dotnet` to `/usr/bin/snap`. The `PATH` is not modified as `snap/bin` is included, and snap knows how to resolve the `dotnet` executable.

On Mac, there is a 'system' `PATH` defined in `/etc/paths` and `etc/paths.d/`, which the `path_helper` utility reads. The `pkg` installer sets the System `PATH` if the installer matches the native architecture.

https://github.com/dotnet/runtime/blob/b79c4fbf284a6b002f46b1fc95ee39e545687515/src/installer/pkg/sfx/installers/osx_scripts/host/postinstall#L25

These environment variables apply across core system utilities and developer tooling. `zsh` uses `~./zshrc` as a bash_profile to load user level `PATH` settings and other contexts. It is generally considered bad practice to edit a user's `SHELL` profile without permission, but it can be a good practice to interactively ask for permission to modify the profile for the user.

## How to set the `PATH`:

There are some possible issues with setting the user `PATH`.
The .NET API for doing so is slow, which is documented in this issue:

https://github.com/dn-vm/dnvm/issues/201

The Windows API for setting the user path does a `COM` broadcast to every single open window on the OS and waits for a response. `chrome.exe`, for example, hangs for 1 minute when receiving this broadcast, which means setting the API for setting the system PATH can take a minute of dead-waiting.

https://github.com/dotnet/runtime/blob/a87fd8bc2fe1bfa3a96d85b05d20a8341c1eb895/src/libraries/System.Private.CoreLib/src/System/Environment.Win32.cs#L58

### Update the Installers / Visual Studio

:star: A framing document and prototype made by the .NET SDK team relies on and suggests this proposal.

One option is to set the machine into a 'local' SDK mode that users could opt into when users engage with `b` to do their first local install. This 'mode' would tell the system-wide installers (both `pkg` and `msi`) to not set the system `PATH`. The advantage of this is that dotnet installations created by `b` would work even if Visual Studio updates. Another advantage of changing the behavior based on a user action is that this change would not break existing installations of VS or existing code/tooling scenarios without user interaction. It's then possible to educate the user about their decision to change the PATH behavior.

The disadvantage is that Visual Studio / other tooling would no longer be able to find its SDKs once this mode was enabled, so the SDK update that is included with VS would not take effect without additional action. The SDK included with VS is an administrative install, which is managed by MU. This means MU would also lose authority to update VS's SDKs; MU (Microsoft Update) and other related IT update mechanisms are tied to MSI or administrative installs, so there is no easy way to hook a local installation mechanism into that toolchain. This is addressed shortly.

In this scenario:

1. When the user interfaces with the program `b`, to install an SDK, they are prompted about the change to `local` installs. They are also informed about the impact this has on VS and can pick an update cadence.

2. On Windows, a registry key is set in `HKLM` (the registry hive for local user contexts). `b` must elevate to enable this registry edit when prompting the user.

A registry key is preferred over an environment variable because it is an admin-protected context. We don't want user-local controllable behavior to influence/break the behavior of an administrative installer, as that is a security concern. A policy key may also be used, but a policy key is essentially equivalent to a registry key in this context.

On OS X, we could set this via a protected sentinel file.

3. Under step 2, the `PATH` is updated to a dotnet application host (dotnet.exe) that contains the local installs managed by `b`. Visual Studio and the other installers will no longer set the System `PATH` unless the registry key or sentinel is changed, which the option to undo this would be available, by removing the key, perhaps via an `uninstall all` or `reset` CLI feature from `b`. The exact semantics of such are not pertinent here.

4. The existing `global.json` `paths` feature would still enable other scenarios like for Visual Studio to specify any SDK. In this sense, it is already possible to enable local .NET SDKs to work with VS.

5. The user is prompted to see if they would like to install the local versions of the administrative installs currently managed by Visual Studio/MU/Package Manager. We can check the registry key hive as Visual Studio includes a reference count of which SDKs it manages and the file system admin locations in other scenarios. This prevents disrupting Visual Studio functionality as the local installs would match all of the SDKs that it had previously depended upon. This does consume extra disk space. If the user installs a newer SDK, Visual Studio would use that SDK, which is consistent with current behavior.

Upon the action of step 5, we would ideally need to take on the responsibility of updating these SDKs. Managing a daemon is resource-intensive, so it may not be optimal. SDL requirements specify a required update policy; we cannot hook into MU and IT administrators rely on administrative install behaviors. One approach is to mimic the update behavior of Visual Studio upon every interaction with `b` by suggesting updates and asking the user to specify an update cadence when the new local mode is applied. IT administrators should be able to enforce a policy that requires a specific update cadence, and the provided update cadence should be compliant with SDL requirements, such as checking for updates on a daily cadence when the tool is used. Visual Studio would need to trigger this check as well to maintain compliance.

A disadvantage of this setup is that older .NET Installers that do not respect the setting would still interfere with `b` by modifying the system `PATH`. (For example, the `pkg` installer uses `tee` which overwrites the `PATH` file rather than just appending to it.) While we can potentially service these installers, older versions would still cause this issue. Addressing this comprehensively would require modifying the application host or `dotnet.exe`, which is not achievable within the .NET 10 timeframe and would need to be targeted for .NET 11. The advantage is that the application host always runs on the latest version, which would enable it to handle older installers appropriately.

Finally, we should consider where else VS may be impacted. Some code within Visual Studio is hard-coded to use the administrative installs, such as the template engine. The template engine is a standalone component, and it is ok for this tool to run with the admin logic.

### Multi-level-lookup

`DOTNET_MULTILEVEL_LOOKUP` was designed to enable or disable the host/muxer/`dotnet.exe`'s ability to find SDKs or Runtimes in other locations. We could re-enable the muxer to look specifically at the directory of local installs that are placed by `b`, as proposed to be `DOTNET_HOME` in the other specification, [`dotnet-bootstrapper-cli-experience.md`](./dotnet-bootstrapper-cli-experience.md).

This would enable the lookup to work without modification to the `PATH`. We could add back this feature in a limited capacity. However, `DOTNET_MULTILEVEL_LOOKUP` was deprecated and removed in .NET 7 due to user confusion (when it would find global SDKs in a user local context) and for performance reasons. The `host` team has indicated that this feature presented significant challenges when it was available. When installers set conflicting paths, it creates coordination issues between different installation mechanisms. Providing a seamless experience using both installation types (local and global) together remains challenging without this modification.

### Require `paths` in `global.json`

In this approach, we prioritize defined and replicable behavior. System administrator installs are created in predictable locations, especially those from Visual Studio and/or package manager feeds.

What we could do is look for those installations and decline to do anything when a user tries to run `b` if they are present on disk. The error message would point the user toward leveraging the `sdk: paths []` option in their project or repositories `global.json` file, which would specify to the host or `dotnet.exe` where to look for the installs of the .NET SDK. The host is already aware of this as of .NET 10.

Only ~4% of users are on a `global.json` according to internal metrics, so this gesture will create additional work when using `dotnet` for a large number of customers.

The downside of this approach is that it creates an additional step for a significant number of .NET users. Requiring extra configuration files to function properly presents a usability challenge and increases the barrier to entry for new users. However, the `paths` feature itself provides considerable value for repository-specific SDK management, which is why it has already been implemented and made available.

### Fail on Windows

One option would be to limit `b` functionality on Windows to avoid potential conflicts with Visual Studio. This could be implemented by not shipping it on Windows, thereby preventing users from encountering errors. While this approach doesn't address the issue on macOS, the impact there is less significant since Visual Studio is not a factor. VS Code does set the System `PATH` on Mac, but adapting it to not rely on this would be more straightforward.

If `b` is included in the `SDK`, it would become part of the Windows experience by default, requiring appropriate error handling.

There is precedent for platform-specific tooling, as the official `nvm` tool is only available for Linux and macOS. However, implementing a platform-specific approach would create an inconsistent developer experience across operating systems.

### Implement a `use` Command

We could modify the host to accept a specific installation to use. This could be enabled via `b use 9.0.1xx`, which would create a folder containing the local install or use some other mechanism to specify to the host an exact install of the SDK to use in the context of the specified terminal. The `global.json` can already accomplish similar functionality by pinning an SDK and setting the `paths` feature, so the added value of this approach would need careful evaluation.

### Shadow `dotnet` Commands

The tooling could be implemented to call into `dotnet.exe` while specifying a `DOTNET_ROOT` that leverages the local installs. This would require `b` to use a different name than `dotnet`. While this approach preserves existing scenarios and avoids conflicts with Visual Studio, it would require users to learn new commands and workflows, potentially creating confusion with the existing `dotnet` command structure.

### Configure Visual Studio to set DOTNET_ROOT

We could modify Visual Studio to check if `DOTNET_HOME` is in the `PATH`, and that the `paths` option in `global.json` is not set, then have Visual Studio set its process tree's `DOTNET_ROOT` to be the admin location. This would preserve Visual Studio's behavior while allowing it to use the appropriate SDK context. Care would need to be taken to not override any custom DOTNET_ROOT setting. While this approach addresses some security concerns related to the admin/local boundary, it creates a situation where local installs performed via the CLI would not work with Visual Studio, potentially causing user confusion. This also may cause our tooling to break other IDEs and require action from JetBrains and others, which is a large downside.