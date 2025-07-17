# Integrating a 'dotnet' Bootstrapper with Visual Studio (PATH Issues)

## Introduction

How can we enable an `nvm` or `rustUp` like experience for `dotnet` that manages user directory installs of zip/tar SDKs? In this document, the interface to do so will be aliased as the `bootstrapper`, and we will exclusively address issues related to the PATH environment variable and how such a tool could work with VS.

One of the core differences in our ecosystem compared to other toolchains is the inclusion of .NET in VS. The MSI 'admin' .NET Installers, which are included with Visual Studio, set the System `PATH` on Windows to the Program Files directory. The system `PATH` always takes precedence over the user `PATH`. Consequently, the existing behavior can block a user level `PATH`, and in this sense, a `PATH` that points to a user specific folder. Below are proposals to remedy this situation.

Please see the `Technical Details` section for more context.

## Proposal

### Update the Installers / Visual Studio

Since user installs and admin installs don't work well together (only one can be the default), we would allow users to opt in to (or out of) a 'user' SDK mode, where the user install takes precedence over the 'admin' install. That could happen when they run the `bootstrapper` to do their first local install. This 'mode' would tell the system-wide installers (both `pkg` and `msi`) to not set the system `PATH`. Enabling the 'user' mode would require admin privelleges.

In this scenario:

1. When the user interfaces with the `bootstrapper`, to install an SDK, they are prompted about the change to `local` installs. They are also informed about the impact this has on VS. IT administrators can set a Policy Key to block this gesture.

2. On Windows, if the user chooses to switch to user SDK mode, the installer will elevate, remove the admin dotnet path from the system PATH, and set a registry key which prevents further admin installs of dotnet from modifying the system path.

> 📓 Technical Details:
> A registry key is preferred over an environment variable because it is an admin-protected context. We don't want user-local controllable behavior to influence/break the behavior of an administrative installer, as that is a security concern. A policy key may also be used, but a policy key is essentially equivalent to a registry key in this context.
>
> On Mac (Linux does not have a machine wide PATH problem to the same extent, though we may need to ask to configure .rc shell files), we could set this via a protected sentinel file.

3. Under step 2, the `PATH` is updated to a dotnet application host (dotnet.exe) that contains the local installs managed by `bootstrapper`. Visual Studio and the other installers will no longer set the System `PATH` until the registry key or sentinel change is reverted. The option to undo this would be available, by removing the key, perhaps via an `uninstall all` or `reset` CLI feature from `bootstrapper`. The exact semantics of such are not pertinent here.

4. The existing `global.json` `paths` feature would still enable other scenarios like for Visual Studio to specify any other SDK, such as an admin location SDK. However, environment variables are not yet supported, so a hard-coded drive letter would be necessary. That is not ideal, and we would push for a change to environment variable support if technically secure.

5. The user is prompted to see if they would like to install the local versions of the administrative installs currently managed by Visual Studio/MU/Package Manager, as well as the update channel they prefer for those installs (ala global.json `rollFoward` values, as described in the cli-experience document.)

> 📓 Technical Details: Admin Install Detection
> We can check the registry key hive as Visual Studio includes a reference count of which SDKs it manages and the file system admin locations in other scenarios. This prevents disrupting Visual Studio functionality as the local installs would match all of the SDKs that it had previously depended upon. This does consume extra disk space. If the user installs a newer SDK, Visual Studio would use that SDK, which is consistent with current behavior.

> 📓 Technical Details: Update
> Upon the action of step 5, we would need to take on the responsibility of updating these SDKs. Managing a daemon is resource-intensive, so we decided against this. SDL requirements specify a required update policy; we cannot hook into MU and IT administrators rely on administrative install behaviors. One approach is to mimic the update behavior of Visual Studio upon every interaction with `bootstrapper` by suggesting updates and asking the user to specify an update cadence when the new local mode is applied, whenever the user runs `dotnet`. The update logic can be a hook added to commands such as `build` asynchronously, and should be toggle-able via an admin setting (registry key). Visual Studio would need to trigger this check as well to maintain compliance, since it can be launched before our tooling would get to take any update action.

#### Advantages and Disadvantages

The advantage of this is that dotnet installations created by `bootstrapper` would work even if Visual Studio updates. Another advantage of changing the behavior based on a user action is that this change would not break existing installations of VS or existing code/tooling scenarios without user interaction. It's then possible to educate the user about their decision to change the PATH behavior and install what is necessary to not disrupt VS.

The disadvantage is that Visual Studio / other tooling would no longer be able to find their own SDKs once this mode was enabled, so the SDK update that is included with VS would not take effect without additional action. The SDK included with VS is an administrative install, which is managed by MU. This means MU would also lose authority to update VS's SDKs; MU (Microsoft Update) and other related IT update mechanisms are tied to MSI or administrative installs, so there is no easy way to hook a local installation mechanism into that toolchain. Our suggestion there would be for IT Admins to block the local mode.

A disadvantage of this setup is that older .NET Installers that do not respect the setting may still interfere with `bootstrapper` by modifying the system `PATH`. (For example, the `pkg` installer uses `tee` which overwrites the `PATH` file rather than just appending to it.) While we can potentially service these installers, older versions would still could this issue if they don't use the updated component. Addressing this comprehensively would require modifying the application host or `dotnet.exe`, which is not achievable within the .NET 10 timeframe and would need to be targeted for .NET 11. The advantage is that the application host always runs on the latest version, which would enable it to handle older installers appropriately.

> 📓 Technical Detail: VS Behavior with Local Installs:
>
> Finally, we should consider where else VS may be impacted. Some code within Visual Studio is hard-coded to use the administrative installs, such as the template engine. The template engine is a standalone component, and it is ok for this tool to run with the admin logic.

## Other options considered

### Allow user and admin installs to work together

Instead of switching to a mode where only user installs are available, we could have a way that both user installs and admin installs would be accessible.  This would reduce the impact to Visual Studio, essentially installing a user install of .NET would be no different than installing the same version as an admin install.  In either case, installing a new version would simply make an additional SDK version available, without removing access to any versions already installed.

A feature called `DOTNET_MULTILEVEL_LOOKUP` was deprecated and removed in .NET 7 due to user confusion (when it would find global SDKs in a user local context) and for performance reasons. The `host` team and others indicated that this feature presented significant challenges when it was available. When installers set conflicting paths, it creates coordination issues between different installation mechanisms, so it may be better to view this as an installer problem like above.

We believe we could add a new feature that would allow local and admin installs to be used together, but that would avoid some of the issues that multi-level lookup had, since we have control over an interactive experience to inform the user about the behavior. However, it would still add complexity and given the negative experience around multi-level lookup, it might not completely mitigate all the issues. So, we propose to go with the simpler option of allowing the user to switch to a mode where user installs are used instead of admin installs.  If we get significant feedback that users would like to have both types available together, we can consider adding support for that.

### Require `paths` in `global.json`

In this approach, we prioritize defined and replicable behavior. System administrator installs are created in predictable locations, especially those from Visual Studio and/or package manager feeds.

What we could do is look for those installations and decline to do anything when a user tries to run `bootstrapper` if they are present on disk. The error message would point the user toward leveraging the `sdk: paths []` option in their project or repositories `global.json` file, which would specify to the host or `dotnet.exe` where to look for the installs of the .NET SDK. The host is already aware of this as of .NET 10.

> 📓 Usage Statistic:
> Only ~4% of users are on a `global.json` according to internal metrics, so this gesture will create additional work when using `dotnet` for a large number of customers.

The downside of this approach is that it creates an additional step for a significant number of .NET users. Requiring extra configuration files to function properly presents a usability challenge and increases the barrier to entry for new users. However, the `paths` feature itself provides considerable value for repository-specific SDK management, which is why it has already been implemented and made available. Another downside, however, is the lack of environment variable support. This means we cant use something like `LOCALAPPDATA` or `ProgramFiles`.

### Fail on Windows

One option would be to limit `bootstrapper` functionality on Windows to avoid potential conflicts with Visual Studio. This could be implemented by not shipping it on Windows, thereby preventing users from encountering errors. While this approach doesn't address the issue on macOS, the impact there is less significant since Visual Studio is not a factor. VS Code does set the System `PATH` on Mac, but adapting it to not rely on this would be more straightforward.

If `bootstrapper` is included in the `SDK`, it would become part of the Windows experience by default, requiring appropriate error handling.

There is precedent for platform-specific tooling, as the official `nvm` tool is only available for Linux and macOS. However, implementing a platform-specific approach would create an inconsistent developer experience across operating systems.

### Implement a `use` Command

We could modify the host to accept a specific installation to use. This could be enabled via `bootstrapper use 9.0.1xx`, which would create a folder containing the local install or use some other mechanism to specify to the host an exact install of the SDK to use in the context of the specified terminal. The `global.json` can already accomplish similar functionality by pinning an SDK and setting the `paths` feature, so the added value of this approach would need careful evaluation.

### Shadow `dotnet` Commands

The tooling could be implemented to call into `dotnet.exe` while specifying a `DOTNET_ROOT` that leverages the local installs. This would require `bootstrapper` to use a different name than `dotnet`. While this approach preserves existing scenarios and avoids conflicts with Visual Studio, it would require users to learn new commands and workflows, potentially creating confusion with the existing `dotnet` command structure.

### Configure Visual Studio to set DOTNET_ROOT

We could modify Visual Studio to check if `DOTNET_HOME` is in the `PATH`, and that the `paths` option in `global.json` is not set, then have Visual Studio set its process tree's `DOTNET_ROOT` to be the admin location. This would preserve Visual Studio's behavior while allowing it to use the appropriate SDK context. Care would need to be taken to not override any custom DOTNET_ROOT setting.

#### Advantages and Disadvantages

While this approach addresses some security concerns related to the admin/local boundary, it creates a situation where local installs performed via the CLI would not work with Visual Studio, potentially causing user confusion. This also may cause our tooling to break other IDEs and require action from JetBrains and others, which is a large downside and why this is at the bottom of the list.

## Technical Details

If `bootstrapper` sets the user path to enable a folder of local .NET SDKs to be discovered by the `dotnet.exe` application host at `DOTNET_ROOT`, via the `PATH`, those installs would become undetectable to the runtime, debugger, etc. when Visual Studio updates and modifies the System `PATH`. With changes in .NET 7+, `DOTNET_MULTI_LEVEL_LOOKUP` is no longer enabled, and once an install is detected in one location, the lookup does not fall back. Thus, the break to `bootstrapper` occurs because when Visual Studio automatically updates, it modifies the `PATH`, which can cause the `bootstrapper`'s managed installs to no longer be found.

On the other hand, if `bootstrapper` set the system `PATH`, it would modify the behavior of Visual Studio, as Visual Studio would no longer find its installs. This could be considered a breaking change to Visual Studio. In this scenario, Visual Studio may begin to run on a different SDK than the one it shipped with, based on whichever SDKs the user has installed that can be used with the user project (based on the major.minor version of .NET and host resolution logic for the specific context.) This is already possible today by installing a different administrative SDK that is newer than the one Visual Studio has. However, it is not possible to install a downgrade of an administrative install in the specific version-set without uninstalling the SDK owned by Visual Studio first. This also means that updates of the SDK that Visual Studio includes would no longer take effect within Visual Studio, as the update mechanism would need to be handled by `bootstrapper`.

An additional concern would be the `source-built` SDKs and the `pkg` installer.

### Existing Tooling Behavior

The .NET Installers currently set the System Level `PATH` on Windows if the installer matches the native architecture. These installers are also leveraged via VS, so their setup also sets the system level `PATH`.

https://github.com/dotnet/runtime/blob/main/src/installer/pkg/sfx/installers/host.wxs

The source-built SDKs installed via a package manager are installed to `/usr/lib/dotnet` or `/usr/lib64/dotnet` when using the built-in feeds. The `dotnet` executable is found because a `symlink` is added to represent `dotnet` in `/usr/bin/dotnet` which points to the `lib` folder. In this case, the `PATH` does not need to be updated, and it does not get updated. (Previous behavior installed to `/usr/share/dotnet`.) There is no distinction between a system and user level `PATH`.

When utilizing `snap`, they are installed as a `symlink` to `snap/bin/dotnet` to `/usr/bin/snap`. The `PATH` is not modified as `snap/bin` is included, and snap knows how to resolve the `dotnet` executable.

On Mac, there is a 'system' `PATH` defined in `/etc/paths` and `etc/paths.d/`, which the `path_helper` utility reads. The `pkg` installer sets the System `PATH` if the installer matches the native architecture. This file should be owned by `root`.

https://github.com/dotnet/runtime/blob/b79c4fbf284a6b002f46b1fc95ee39e545687515/src/installer/pkg/sfx/installers/osx_scripts/host/postinstall#L25

These environment variables apply across core system utilities and developer tooling. `zsh` uses `~./zshrc` as a bash_profile to load user level `PATH` settings and other contexts. It is generally considered bad practice to edit a user's `SHELL` profile without permission, but it can be a good practice to interactively ask for permission to modify the profile for the user.

## How to set the `PATH`:

There are some possible issues with setting the user `PATH`.
The .NET API for doing so is slow, which is documented in this issue:

https://github.com/dn-vm/dnvm/issues/201

The Windows API for setting the user path does a `COM` broadcast to every single open window on the OS and waits for a response. `chrome.exe`, for example, hangs for 1 minute when receiving this broadcast, which means setting the API for setting the system PATH can take a minute of dead-waiting.

https://github.com/dotnet/runtime/blob/a87fd8bc2fe1bfa3a96d85b05d20a8341c1eb895/src/libraries/System.Private.CoreLib/src/System/Environment.Win32.cs#L58
