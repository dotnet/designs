# Improving .NET SDK Selection

## Background

The .NET ecosystem has long needed better SDK selection capabilities similar to tools like `nvm` or `rustup`. Previous efforts explored this through the [dotnet bootstrapper path proposal](https://github.com/dotnet/designs/blob/a87cfdf032351fd7e7b449a11edeba1b92ba53b1/proposed/dotnet-bootstrapper/dotnet-bootstrapper-path.md), which investigated how to integrate a 'dotnet' bootstrapper with Visual Studio while managing PATH environment variable conflicts.

An attempt to improve SDK selection was made by modifying the SDK resolution path via [dotnet/runtime PR #118092](https://github.com/dotnet/runtime/pull/118092). However, this change was [reverted in PR #120472](https://github.com/dotnet/runtime/pull/120472) due to a critical issue: when uninstalling a newer .NET version, the PATH entry to `dotnet.exe` would be incorrectly removed even when older .NET versions remained installed. For example, if .NET 9.0 was installed first, then .NET 10 RC1, uninstalling .NET 10 RC1 would remove the PATH to `dotnet.exe` entirely, breaking the remaining .NET 9.0 installation.

This revert highlighted the complexity of PATH management in multi-version .NET environments and left gaps in flexibility and user experience. The goal now is to provide a more robust and configurable approach for SDK selection across platforms that avoids these PATH management pitfalls.

## Related Documentation

*   [.NET Environment Variables](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-environment-variables) - Official documentation for .NET environment variables
*   [global.json Overview](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json) - Documentation for global.json configuration
*   [.NET Installation Guide](https://learn.microsoft.com/en-us/dotnet/core/install/) - Official installation documentation
*   [SDK Resolution in .NET](https://github.com/dotnet/designs/blob/main/accepted/2019/sdk-version-selection.md) - Design document for SDK version selection

***

## Non-Goals

*   **SDK Location Reporting API:** This proposal does not include an API or mechanism for the .NET host or `dotnetup` to report back the location where the SDK is installed. Such functionality for programmatic SDK discovery will be covered in a separate design effort.

***

## Proposal: DOTNET_ROOT_REDIRECT

Introduce two new environment variables that allow redirecting SDK loading from a specific source location to a target location:

- `DOTNET_ROOT_REDIRECT_SOURCES`: Specifies the source SDK installation path to redirect from (e.g., `C:\Program Files\dotnet`)
- `DOTNET_ROOT_REDIRECT_TARGET`: Specifies the target SDK installation path to redirect to (e.g., `C:\Users\Daniel\AppData\Local\dotnet`)

### Behavior

*   These environment variables only affect SDK loading behavior when running `dotnet` commands that require SDK resolution.
*   Direct execution of applications via `dotnet foo.dll` is **not affected** to avoid impacting runtime behavior.
*   If the host is launched from the source path specified in `DOTNET_ROOT_REDIRECT_SOURCES`, SDK resolution will use the path specified in `DOTNET_ROOT_REDIRECT_TARGET`.
*   If the host is launched from any other location (e.g., a manually extracted .zip), normal SDK resolution behavior applies without redirection.
*   This approach avoids the confusion that could occur when users download a .zip directly and navigate to the folder with dotnet.exe, as redirection only applies to specific source paths.

### Example

```bash
# Set up redirection from admin install to user-local install
set DOTNET_ROOT_REDIRECT_SOURCES=C:\Program Files\dotnet
set DOTNET_ROOT_REDIRECT_TARGET=C:\Users\Daniel\AppData\Local\dotnet

# When running from C:\Program Files\dotnet\dotnet.exe:
# - SDK commands (dotnet build, dotnet new, etc.) use SDKs from C:\Users\Daniel\AppData\Local\dotnet
# - Runtime commands (dotnet foo.dll) continue to use C:\Program Files\dotnet

# When running from a manually extracted zip or other location:
# - Normal SDK resolution applies, no redirection occurs
```

***

## Platform-Specific Behavior for dotnetup

The below will define the behavior when using `dotnetup use` or `dotnetup install` with the redirect approach.

### Windows

*   Set both:
    *   `DOTNET_ROOT_REDIRECT_SOURCES=C:\Program Files\dotnet`
    *   `DOTNET_ROOT_REDIRECT_TARGET=%LOCALAPPDATA%\dotnet` (typically `C:\Users\{username}\AppData\Local\dotnet`)
*   **User Experience:**
    *   Detect if a global installation exists in `C:\Program Files\dotnet`.
    *   Set up redirection to user-local SDK installations managed by `dotnetup` in `%LOCALAPPDATA%\dotnet`.

### Non-Windows

#### macOS
*   Set both:
    *   `DOTNET_ROOT_REDIRECT_SOURCES=/usr/local/share/dotnet`
    *   `DOTNET_ROOT_REDIRECT_TARGET=~/Library/Application Support/dotnet`
*   Additionally:
    *   Modify the **global PATH** if it points to `/usr/local/share/dotnet`, redirecting it to `~/Library/Application Support/dotnet`.

#### Linux
*   Set both:
    *   `DOTNET_ROOT_REDIRECT_SOURCES=/usr/lib/dotnet` (or `/usr/lib64/dotnet` on 64-bit systems)
    *   `DOTNET_ROOT_REDIRECT_TARGET=~/.local/share/dotnet`
*   Additionally:
    *   Modify the **global PATH** if it points to the system-wide SDK location, redirecting it to `~/.local/share/dotnet`.

***

## `dotnetup use` Command

The `dotnetup use` command provides a streamlined way to switch SDKs by setting up redirection from admin installations to user-local installations.

### Features

*   Set up `DOTNET_ROOT_REDIRECT_SOURCES` and `DOTNET_ROOT_REDIRECT_TARGET` environment variables to redirect SDK resolution from global installations to user-local installations.
*   `--repo` flag:
    *   Creates a new `global.json` file or modifies an existing one to specify the SDK path for the repository.

#### Examples

```bash
# Set up redirection to use user-local SDKs managed by dnup
# This will redirect from C:\Program Files\dotnet to %LOCALAPPDATA%\dotnet
dotnetup use

# Set up redirection to a specific SDK version in the default user-local location
dotnetup use 10.0.101

# Set a custom SDK path for a specific repository
dotnetup use --repo /path/to/repo ~/.local/dotnet-sdks/9.0.100
```

***

## DOTNET\_SDK\_VERSION and --sdk-version

The host supports:

*   [`DOTNET_SDK_VERSION` environment variable](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-environment-variables#dotnet_sdk_version).
*   [`--sdk-version` command-line option](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet#options).

### Purpose

*   Specify an exact SDK version for quick switching.
*   Useful for:
    *   Testing scenarios.
    *   Continuous Integration (CI) pipelines.

#### Example

```bash
dotnet --sdk-version 10.0.101
```

***

## Implementation Considerations

### Host Changes Required
*   **Environment Variable Support:** The .NET host must be modified to recognize and process the `DOTNET_ROOT_REDIRECT_SOURCES` and `DOTNET_ROOT_REDIRECT_TARGET` environment variables during SDK resolution.
*   **Source Path Matching:** The host must check if it was launched from a path that matches `DOTNET_ROOT_REDIRECT_SOURCES` before applying redirection.
*   **Scope Limitation:** Redirection should only apply to SDK operations, not runtime operations (e.g., `dotnet foo.dll`).

### dotnetup Tool Requirements
*   **Cross-Platform Compatibility:** Ensure `dotnetup` works consistently across Windows, macOS, and Linux.
*   **Environment Variable Management:** Ability to set and manage the redirect environment variables appropriately for each platform.

***

## Security and Validation Considerations

*   **Path Validation:** The `DOTNET_ROOT_REDIRECT_TARGET` environment variable should be validated to ensure it points to a legitimate .NET SDK installation.
*   **Source Path Verification:** The `DOTNET_ROOT_REDIRECT_SOURCES` path should be validated to prevent malicious redirection attempts.
*   **Permission Checks:** Ensure that the target path is accessible and contains valid SDK installations.
*   **Fallback Behavior:** If the target path becomes unavailable or invalid, the system should gracefully fall back to the original SDK resolution behavior.

***

## Future Enhancements

*   **Host Installation:** `dotnetup` could install the **.NET 11 host** if not present.
*   **Backport Potential:** Backport this feature to **.NET 10** at a later date.
*   **Windows PATH Enhancement:** `dotnetup` could offer an elevated mode to remove the global `Program Files\dotnet` PATH entry, allowing user-level PATH modifications to take precedence across all command prompts.
    *   **Note:** This global PATH entry would be restored whenever .NET updates are installed to the global location, requiring users to re-run the elevated removal after each update.
*   **IDE Integration:** Future integration with Visual Studio Code, Visual Studio, and JetBrains Rider to automatically detect and use project-specific SDK configurations.
*   **SDK Auto-Discovery:** Automatic detection and listing of available SDK versions from both global and user-local installations.

***

## Summary Table

| Feature           | Windows                              | Non-Windows                           |
| ----------------- | ------------------------------------ | ------------------------------------- |
| Env Vars          | `DOTNET_ROOT_REDIRECT_SOURCES`, `DOTNET_ROOT_REDIRECT_TARGET` | `DOTNET_ROOT_REDIRECT_SOURCES`, `DOTNET_ROOT_REDIRECT_TARGET` |
| Redirection Scope | SDK operations only                  | SDK operations only                   |
| Fallback Behavior | Normal resolution for non-matching paths | Normal resolution for non-matching paths |

***

## Alternative Proposals

### DOTNET_SDK_ROOT Approach

An earlier approach considered introducing a `DOTNET_SDK_ROOT` environment variable to specify the SDK path for the host to use, similar to the global.json SDK path feature. However, this approach had significant drawbacks:

**Issues with DOTNET_SDK_ROOT:**
*   **Global Impact:** The environment variable would affect all .NET operations, including cases where users download a .zip directly and navigate to the folder with dotnet.exe, leading to confusing behavior where it would use a different SDK than expected.
*   **Version Compatibility:** Risk of users setting the environment variable with older .NET hosts that don't support it, leading to unexpected behavior.

**Why DOTNET_ROOT_REDIRECT is preferred:**
*   **Targeted Redirection:** Only redirects from specific source paths (like admin installations), avoiding confusion with manually extracted SDKs.
*   **Scoped to SDK Operations:** Only affects SDK operations, not runtime operations like `dotnet foo.dll`.

### Terminal Profile Guidance Approach

Provide documentation and tooling guidance for customers to create terminal profiles that modify their PATH to point to user-local SDK installations.

**How it would work:**
*   Document best practices for setting up shell profiles (`.bashrc`, `.zshrc`, PowerShell profiles) to modify PATH
*   Microsoft could still modify the VS Developer Command Prompt and DevKit terminal creation to use appropriate SDK paths
*   Users would be responsible for setting up their own terminal environments

**Issues with this approach:**
*   **User Burden:** Places the responsibility on users to configure their terminals correctly
*   **Inconsistent Experience:** Different users would have different setups, making support more difficult
*   **Limited IDE Integration:** IDEs that don't use custom terminal profiles would still face PATH precedence issues
*   **Platform Complexity:** Different shells and platforms require different configuration approaches

### Installer Configuration to Disable Global PATH

Configure .NET installers to not set the global PATH, preventing conflicts with user-local installations.

**How it would work:**
*   Add a registry key or configuration option that prevents .NET installers from modifying the system PATH
*   Users who want user-local SDKs would set this configuration
*   All .NET discovery would then depend on explicit PATH configuration or environment variables

**Issues with this approach:**
*   **Breaking Change for Other Users:** Would break .NET functionality for other users on the same machine who expect the global installation to work
*   **Administrative Risk:** Requires system-wide configuration changes that affect all users
*   **Support Complexity:** Creates two different installation behaviors that need to be supported
*   **Visual Studio Impact:** Could break Visual Studio's dependency on global .NET installations

### Directory-Specific global.json Modification

Have `dotnetup use` automatically create or modify a `global.json` file in the current working directory to specify SDK paths.

**How it would work:**
*   `dotnetup use <version>` would create/modify `global.json` in the current directory
*   The `global.json` would include SDK version and path specifications
*   Each directory could have its own SDK configuration

**Issues with this approach:**
*   **Directory Pollution:** Creates `global.json` files in many directories, potentially conflicting with project requirements
*   **Limited Scope:** Only works within directories that have been specifically configured
*   **Git/Source Control Issues:** `global.json` files with local paths would not be appropriate for source control
*   **Workspace Confusion:** Different directories would behave differently, making the development environment unpredictable

### Global SDK Modification Approach

Have `dotnetup` modify the globally installed SDK location rather than creating separate user-local installations.

**How it would work:**
*   `dotnetup` would replace or modify the SDK installations in the global location (e.g., `Program Files\dotnet`)
*   All users and applications would use the modified global installation
*   Version management would happen at the system level

**Issues with this approach:**
*   **Administrative Privileges Required:** Modifying global installations requires elevation on most systems
*   **Multi-User Conflicts:** Different users on the same machine might need different SDK versions
*   **System Stability Risk:** Modifying system-wide installations could break other applications or Visual Studio
*   **Rollback Complexity:** Difficult to safely revert changes if issues occur
*   **Enterprise Environment Issues:** Would conflict with IT policies that manage system-wide software installations

***

### Example global.json

```json
{
  "sdk": {
    "version": "10.0.101",
    "path": "${HOME}/.dotnet/sdks/10.0.101"
  }
}
```

> **Note:** Environment variable expansion in `global.json` files would still be valuable and could be implemented independently of the main DOTNET_ROOT_REDIRECT proposal.

