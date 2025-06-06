# dotnet tool exec and dnx

**Owner**: [Marc Paine](https://github.com/marcpopMSFT)

## `dotnet tool exec` Summary

This proposal introduces a new .NET CLI command, `dotnet tool exec`, which enables users to launch a .NET tool directly by specifying its package ID. The command will ensure the tool is installed (downloading the latest version to the NuGet cache if necessary) and then execute it with the provided arguments. Optionally, users can specify a particular version of the tool to run.

This feature streamlines the process of running tools, removing the need for explicit installation or manifest management for ad-hoc tool usage.

## Motivation

Currently, running a .NET tool requires a two-step process: installing the tool (either globally or locally) and then invoking it. This is cumbersome for scenarios where a tool is only needed temporarily or for a single use. By providing a single command to install (if needed) and execute a tool, we improve developer productivity and lower the barrier to using .NET tools.

This also enables scenarios such as:

- Running a tool in CI/CD pipelines without polluting the global or local tool manifest.
- Quickly trying out tools without a permanent install.
- Scripting and automation where ephemeral tool usage is desired.

## CLI Parameters

The `dotnet tool exec` command will support the following parameters:

| Parameter                | Description                                                                                  |
|--------------------------|----------------------------------------------------------------------------------------------|
| `<package-id>`           | The NuGet package ID of the tool to execute.                                                 |
| `--version <version>`    | (Optional) The specific version of the tool to run. If not specified, the latest is used.    |
| `--prerelease`           | (Optional) Allows installing and running prerelease versions.                                |
| `--framework <tfm>`      | (Optional) Specifies the target framework to use for the tool.                               |
| `--allow-roll-forward`   | (Optional) Allow a .NET tool to roll forward to newer versions of the .NET runtime if the runtime it targets isn't installed. |
| `--configfile <FILE>`    | (Optional) The NuGet configuration file to use.                                             |
| `--source <SOURCE>`      | (Optional) Replace all NuGet package sources to use during installation with these.          |
| `--add-source <ADDSOURCE>`| (Optional) Add an additional NuGet package source to use during installation.               |
| `--ignore-failed-sources`| (Optional) Treat package source failures as warnings.                                        |
| `--interactive`          | (Optional) Allows the command to stop and wait for user input or action (for example to complete authentication). |
| `--yes`                  | (Optional) Overrides confirmation prompt with "yes" value.                                 |
| `-y`                     | (Optional) Overrides confirmation prompt with "yes" value.                                 |
| `-- [tool arguments]`    | Arguments to pass to the tool being executed.                                                |

**Example usage:**

```sh
dotnet tool exec dotnetsay --version 1.2.3 -- Hello, world!
```

Note: The version option would work the same as other dotnet tool commands supporting both an exact version and * wildcard matching

## Detailed Design

- When invoked, `dotnet tool exec` will:
  1. Check if the specified tool is already installed locally and run it as a local tool
  2. Check if the specified tool and version are present in the NuGet cache.
  3. If not present, ask the user for permission download and install the tool package to the NuGet cache.
    3a. No permission is requested if the tool is already installed locally or in the NuGet cache
    3b. A command line parameter can provide the user approval for automated execution (ie -y / --yes)
  4. Launch the tool with the provided arguments.
- The tool is not added to any manifest and is not available globally or locally after execution.
- The command supports passing all arguments after `--` directly to the tool.
- Tools executed via `dotnet tool exec` are always installed to the NuGet cache, leveraging existing NuGet cache policies and avoiding the need for a manifest. This simplifies the process and ensures ephemeral tool usage.

## Considerations

- The tool is ephemeral and not discoverable via `dotnet tool list`.
- Caching behavior follows standard NuGet cache policies.
- There should be minimal impact to the state of the machine beyond the install to the NuGet cache.

### Calling by Command vs. Package

There is the potential of supporting calling a tool by its command name rather than the package ID. This approach could improve usability, but it is potentially problematic because .NET tools do not have a standard naming convention for commands, and there are existing protections on nuget.org to guard against spelling errors and package squatting that apply only to package IDs. Further discussion and feedback are needed to determine the best approach for this scenario.

We could support a standard naming convention for the package like "dotnet-<command>" as that's used in dotnet-ef and many other tools but that would open the question of what to do about existing tools.

---

## `dnx` Summary

This proposal introduces a new launcher script, `dnx`, which acts as a thin wrapper for `dotnet tool exec`. The script will be installed as `dnx.cmd` on Windows and as an executable `dnx` (no extension) on non-Windows platforms, and will reside alongside the `dotnet` host executable.

When invoked, the `dnx` script will forward all arguments to a .NET CLI command `dotnet dnx` which will initially just be an alias for `dotnet tool exec`, enabling a familiar and concise entry point for running .NET tools directly. The script will not contain any additional logic, ensuring consistency and simplifying maintenance across platforms.

## Motivation

The `dnx` command provides a short, memorable alias for running .NET tools, inspired by the historical DNX (DotNet Execution Environment) command. It simplifies tool invocation, especially in cross-platform scripts and documentation, and provides a consistent experience across operating systems.

This also helps with:

- Reducing typing and cognitive load for frequent tool users.
- Providing a migration path for users familiar with the legacy `dnx` command.
- Enabling easier scripting and automation.

## Detailed Design

- On Windows, `dnx.cmd` will be installed next to `dotnet.exe`.
  - For administrative installations, the script will be delivered via a standalone, upgradeable MSI installer included in the .NET SDK bundle, similar to the approach used for the host. As a versionless file, the most recently executed installer (including repairs) will take precedence.
- On non-Windows platforms, an executable file named `dnx` (no extension) will be installed next to the `dotnet` host and marked as executable.
- The script will invoke `dotnet dnx` with all arguments passed through unchanged.
  - This will allow us to make future changes to the logic within the SDK which is versioned rather than the script which is versionless and could end up easily being overwritten
- No additional logic or parameter parsing is performed by the script.
- All logic is handled in the .NET SDK CLI logic, not the shell script, to ensure maintainability and platform consistency.

**Example usage:**

```sh
dnx dotnetsay -- Hello from dnx!
```

## Non-goals

- Supporting other execution modes such as `dotnet run` or `dotnet exec` via `dnx` is out of scope for this proposal, but may be considered in the future.
- The `dnx` script does not manage tool installation or manifest files beyond what `dotnet tool exec` provides.
- Any additional logic or performance improvements (such as AOTing the DNX command) may be considered in the future to reduce overhead and improve performance.

---
