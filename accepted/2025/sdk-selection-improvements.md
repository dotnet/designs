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

## Proposal: DOTNET\_SDK\_ROOT

Introduce a new environment variable:  
`DOTNET_SDK_ROOT`  
This variable specifies the SDK path for the host to use, similar to the **[global.json SDK path feature](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json#paths)** introduced in **.NET 10**.

### Behavior

*   If `global.json` includes an SDK path setting, **prefer that path over the environment variable**.
    *  The host should set DOTNET_SDK_ROOT when set via `global.json` so child processes can depend on that behavior (see other discussions on DOTNET_HOST_PATH)
*   If neither is set, fall back to the default global SDK path.

***

## Platform-Specific Behavior for dotnetup

The below will define the behavior when using `dotnetup use` or `dotnetup install`.

> **Important Note:** If the PATH is reset to point to an older .NET host that doesn't support the `DOTNET_SDK_ROOT` environment variable, there is a risk that customers may set `DOTNET_SDK_ROOT` expecting it to override SDK selection, but the older host will ignore this variable and continue using its default SDK resolution logic. This could lead to unexpected behavior where the intended SDK is not used despite the environment variable being set.

### Windows

*   Set both:
    *   `DOTNET_SDK_ROOT`
    *   `DOTNET_ROOT`
*   Modify the **PATH** in the current window to point to the selected SDK.
*   **User Experience:**
    *   Detect if the global path points to `Program Files/dotnet` and the customer doesn't have the .NET 11 host which supports the SDK root feature.
    *   Notify the user:
        > This configuration will only work in this window. Use `dotnetup use` each time you open a new command prompt. This leaves gaps in the scenario with IDE features like the DevKit debugger that relies on the PATH

**Technical Note:** On Windows, the global (system-wide) PATH environment variable takes precedence over the user PATH. Since .NET installers typically add `Program Files\dotnet` to the global PATH, we can only modify the current window's PATH without requiring elevation. The global PATH entry will override any user-level PATH modifications in new command prompts.

***

### Non-Windows

*   Set:
    *   `DOTNET_SDK_ROOT`
    *   `DOTNET_ROOT`
*   Additionally:
    *   Modify the **global PATH** if it points to the global SDK, redirecting it to the **user-local SDK**.

***

## `dotnetup use` Command

The `dotnetup use` command provides a streamlined way to switch SDKs for the current shell or repository.

### Features

*   Switch SDK for the current shell session as well as set the user environment variables as above.
*   `--repo` flag:
    *   Creates a new `global.json` file or modifies an existing one to specify the SDK path for the repository.

#### Examples

```bash
# Switch to an SDK in the default dnup user-local location
# Windows: %LOCALAPPDATA%\dotnet, macOS: ~/Library/Application Support/dotnet
dotnetup use

# Switch to a specific SDK version in the default location
dotnetup use 10.0.101

# Switch to an SDK installed in a custom location for current shell
dotnetup use ~/.dotnet/sdks/10.0.101

# Set a custom SDK path for a specific repository
dotnetup use --repo /path/to/repo ~/.local/dotnet-sdks/9.0.100

# Switch to an SDK from a shared team location
dotnetup use /shared/team/dotnet-sdks/10.0.101
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
*   **Environment Variable Support:** The .NET host must be modified to recognize and process the `DOTNET_SDK_ROOT` environment variable during SDK resolution.
*   **Variable Expansion:** Support for environment variable expansion (e.g., `${HOME}`, `%USERPROFILE%`) in `global.json` SDK paths.
*   **Precedence Logic:** Implementation of the precedence order: `global.json` SDK path > `DOTNET_SDK_ROOT` > default global SDK path.

### dotnetup Tool Requirements
*   **Cross-Platform Compatibility:** Ensure `dotnetup` works consistently across Windows, macOS, and Linux.
*   **Shell Integration:** Support for various shells (PowerShell, cmd, bash, zsh, fish) for environment variable setting.

***

## Security and Validation Considerations

*   **Path Validation:** The `DOTNET_SDK_ROOT` environment variable and `global.json` SDK paths should be validated to ensure they point to legitimate .NET SDK installations during dotnetup operations.
*   **Permission Checks:** On Windows, elevation requests for PATH modification should include clear user consent and explanation of the changes.
*   **Fallback Behavior:** If no SDK is found on the SDK path, the system should gracefully fall back to the default global SDK location.

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
| Env Vars          | `DOTNET_SDK_ROOT`, `DOTNET_ROOT`     | `DOTNET_SDK_ROOT`, `DOTNET_ROOT`      |
| PATH Modification | Current window only                  | Global PATH if pointing to global SDK |
| UX Notification   | Yes (Program Files/dotnet detection) | No                                    |

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

> **Note:** The .NET host currently does not support environment variable expansion in `global.json` files or the `DOTNET_SDK_ROOT` environment variable. Host support for these features would need to be implemented for this proposal to function as described.

