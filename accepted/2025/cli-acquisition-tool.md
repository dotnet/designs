# .NET SDK Acquisition and Management Tool

**Owner** [Chet Husk](https://github.com/baronfel) | [Daniel Plaisted](https://github.com/dsplaisted)

Users of the .NET SDK today face a wide array of choices when getting our tooling, with multiple distribution channels that release at different cadences.
While these options serve specific needs, several important user cohorts are not well served and have asked for a more unified approach to getting started with our tooling.


This proposal introduces a standalone install and SDK management tool (tentatively named `dnup`) that provides a unified interface for acquiring and managing .NET tooling across all supported platforms, focusing on user-local installations that can be managed (largely) without elevation.

This tool is especially critical for users that are

* new users, especially those from the Node or Rust ecosystems
* Linux users who typically use packages from their distro, as not all distro maintainers support feature bands beyond 1xx
* macOS users, who can access all feature bands but have no way of cleaning up old installations (ignoring the dotnet-core-uninstall tool)
* users who manually perform _any_ SDK installations and therefore are responsible for clean up

## Scenarios and User Experience

### Brand-new learner

Sasha wants to learn C# and develop applications on her Mac.
She searches and is led to a 'getting started' style page that directs her to download our bootstrapper.
She navigates to `get.dot.net` and clicks the prominent link for `dnup-init`.
She then clicks the downloaded executable, which runs the bootstrap process where she is prompted through an interactive setup:

```bash
Where should we install .NET to? *(~/Library/Application Support/dnup)*
> <enter>
What version should we install? *(latest)*
> <enter>
Should we set up your environment/PATH automatically? *(Y/n)*
> <enter>
Installing .NET SDK 10.0.100 to ~/Library/Application Support/dnup
...............complete
Configuring environment to use dnup
...
.NET SDK 10.0.100 has been installed successfully!
Get started with `dotnet new console`
```

She accepts the defaults, and the latest release of the .NET 10 SDK is downloaded and extracted to the indicated directory.
Her local environment is modified to use this .NET install:
* her zsh profile is modified to add a line that sets `DOTNET_ROOT` to that install path,
* and her zsh profile is modified to add a line that sets `PATH` to include an entry to that install path.

She is then able to continue the tutorial by creating a new C# file and running it with `dotnet run app.cs`.

### Acquisition inside VS Code

Hector installs the C# DevKit extension on an otherwise-clean Windows machine.
The DevKit extension recognizes the lack of a .NET SDK install and prompts Hector for permission to install the latest one.
Hector accepts, and the DevKit extension downloads and runs the latest version of the dnup-init bootstrapper, instructing it to install the latest .NET 10 SDK to `%LOCALAPPDATA%\dotnet`, as well as updating Hector's user-level PATH.
DevKit then continues initialization and Hector is able to begin development.

### Acquisition with an LLM

Ricardo wants to check what .NET versions have security vulnerabilities and update them.
He asks his local GitHub Copilot which installed .NET versions are out of date.
The agent talks to an acquisition MCP server that lets him know he has two older .NET versions that have known vulnerabilities and asks him if he wants to update.
Then it calls `dnup update` to update to the latest version for each.

### Onboarding to a repository using .NET

Mei wants to contribute to a .NET library that she found on GitHub.
She clones the repository, but the repo uses a global.json to enforce a certain tooling version for all contributors be installed to a repo-local directory.
Since Mei doesn't have that version, she runs `dnup install` (or her editor does so on her behalf) and that version is downloaded and installed into her local repository copy.
After this install is done, the next time she launches her editor it detects the install and she begins work on the feature she's selected.

### Updating all installed toolchains

Gavin has a few different SDKs managed via `dnup install` on their machine in the following configuration:

- 8.0.404 - pinned by a `global.json` in `~/repos/mylib`
- 9.0.303 - installed by tracking the 'latest' channel from `dnup install`
- 10.0.100-preview.5 - installed tracking the 'preview' channel from `dnup install`

They want to make sure their machine has all of the latest releases, so they run `dnup update`, which discovers:

- there is a 8.0.405 version available
- there is a 9.0.304 version available
- there is a 10.0.100-preview.6 version available

Since 8.0.404 is pinned by a specific repo, that version is not updated/removed, but 9.0.303 and 10.0.100-preview.5 are updated to 9.0.304 and 10.0.100-preview.6 respectively.

## Requirements

### Goals

- **Unified SDK Management**: Provide a single tool for acquiring, updating, and managing .NET SDKs across all supported platforms
- **User-local Installation**: Focus on user-writable locations that can be managed without elevation after initial setup
- **New User Onboarding**: Streamline the getting-started experience, especially for users coming from Node.js or Rust ecosystems
- **Version Management**: Support multiple concurrent SDK versions with ability to pin specific versions for projects
- **Ease of updates**: Notify users when updates are available, and make it easy to get those updates
- **IDE Integration**: Enable seamless integration with VS Code, Visual Studio, and other development environments
- **Cross-platform Consistency**: Provide consistent experience across Windows, macOS, and Linux
- **global.json Support**: Detect and respect project-specific SDK version requirements
- **Self-maintenance**: Keep the bootstrapper tool itself up to date

### Non-Goals

- **Package Manager Integration/Global Installations**: Will not support installers beyond .zip/.tar installs initially
- **Replace dotnet.exe**: The bootstrapper will not be dotnet.exe itself
- **NuGet/Tools Support**: This is likely in scope of a standalone `dnx` or AOT'd `dotnet` CLI, and isn't the role of a bootstrapper
- **SDK Size Reduction**: Size reduction will be tackled separately, focused on deduplication and factoring out slices of functionality

## Stakeholders and Reviewers

- **.NET SDK Team**: Core implementation and CLI design
- **VS Code C# Extension Team**: Integration scenarios and user experience
- **Visual Studio Team**: Ensuring compatibility with existing VS workflows
- **Security Team**: Signature verification and SDL compliance
- **.NET Foundation**: Community feedback and adoption strategy
- **Documentation Team**: Getting started guides and migration documentation

## Design

### Bootstrapper CLI Design

The tool will follow the CLI design outlined in the [related PR](https://github.com/dotnet/designs/pull/335) and will include commands like

* install specific versions of the SDK tooling (10.0.100)
* install logically-named versions of the SDK tooling (10, LTS, etc)
* list the installed SDKs
* uninstall specific versions of the SDK tooling
* install the versions of components required by a given repo (as specified by global.json)
* provide update checks/out-of-date information to end users about the installs they have

### Initial setup

On first-run, `dnup-init` will configure the users' environment to support user-local hives.
* `DOTNET_ROOT` will be set to point to the `dnup` user-local root,
* `PATH` will have the `dnup` user-local root appended to it,
* on Windows `dnup-init` will request to elevate to set a Windows Registry key that will prevent global .NET installers from setting the global .NET Install location on the System `PATH` (which would always overwrite the user `PATH`).
  * this behavior _may_ be optional - we may choose to only run it by default if the user has a global .NET install already

This configuration will be necessity be shell-specific, so the tool will need to detect the user's shell and modify the appropriate profile file (e.g. `.bashrc`, `.zshrc`, etc.) to set the `DOTNET_ROOT` and `PATH` variables.
We have some prior art in the `dotnet` CLI codebase for detecting the shell that is in use, which can be reused here.
The different shells (and to some extent different OS platforms) have different ways of distributing such changes, so the tool will need to know about those to be a bit opinionated about how it makes these changes.

This shell-specific configuration will need to be independently run via a command, because the user may choose a different shell after the initial setup.

### Install locations

These installs will be located at a default _user-local hive_ instead of a globally-reachable system hive.
This will allow users to manage new tooling versions without needing to elevate.
he tool will support being configured to install to other locations via configuration file, so that simple use of the tool remains consistent.

### Install provenance

The data about installs will be sourced from the release manifests that are published on a regular cadence by the .NET Releases team.
These manifests, and the release artifact data and signatures they contain, will enable the tool to confirm the provenance of any toolsets downloaded to the users' computer.

### Update Management

The tool will enable update checks via these same manifests.
As highly-cacheable static files, they will be easy to check for updates on a regular cadence, and user interactions with the `dnup` tool (directly via a command like `dnup update --check` or similar, or implicitly via IDE or `dotnet` tooling calling such commands) will be managed on a default (but user definable) cadence such as `daily`, `weekly`, etc.

### Self-updates

`dnup` needs to be able to update itself as well, via a command similar to `dnup update --self`.
This command will check documented, signed release manifests for `dnup` and download + replace the current `dnup` binary with the new one.
Provenance and signature validation of the new binary will also happen to ensure the content is as expected.
This update capability should be able to be triggered manually or on an automatic cadence (e.g. every execution, once a day, etc.)

## Q & A

### How will I get `dnup`?

We intend to distribute `dnup` via a direct download initially.
The dotnet download web site and the Learn documentation for getting started with .NET are the primary candidates.
However, it is also common to distribute these kinds of bootstrappers via system package managers, so future efforts could include WinGet packages, Debian and Red Hat packages, a Homebrew cask, etc.
The tool itself will be a Native AOT tool that is completely standalone, so acquisition should be relatively simple to set up.

### How will this handle existing global installations?

`dnup` will detect existing global installations but focus on managing user-local installs.
Users with existing package manager installations will continue to use those tools. `dnup` will provide guidance when detecting unsupported installation types, and may offer to seed the user-local hive with the same versions so that there are no functionality gaps when starting with `dnup`.

### What about impact on services running under different accounts?

For users with services running under different accounts using `dotnet.exe`, setting user-specific PATH and DOTNET_ROOT may cause those services to fail.
We expect this to be a corner case since launching services that way is not recommended, but we need to document this publicly and gather feedback.

### How does this integrate safely with IDEs?

IDEs like Visual Studio Code often have a "bring your own" approach to SDKs and tooling.
This tool works hand in hand with that model - enabling users to more easily acquire whatever version of the SDK they need to work with.

Visual Studio has an execution model that is more tightly bound to the SDK.
While the .NET Runtime, MSBuild, and SDK teams have begun work to enable less coupling here (via the global.json `sdk.paths` feature, the `DOTNET_HOST_PATH` AppHost Switch, and MSBuild SDKResolver enhancements for .NET 10), there is still some coupling that could result in negatively impacting the end-user experience.

To help get ahead of this, we have begun work with the VS team to expand the set of cross-SDK/cross-VS version testing that is done in VS today.
We will aim to keep the SDK and VS maximally-compatible so that VS can support consuming the SDKs across a broader range than previously documented.

It's worth noting that this need for compatibility is not a problem unique to `dnup` - it exists today when users use locally-managed SDKs installed by Arcade, dotnet-install scripts, or other means.
_Where_ the SDK is resolved from is largely orthogonal to _how compatible_ that SDK is with the IDE.

### What future work does this enable?

- Unifying IDE-specific and disparate install components that exist today
  - Specifically components like the `dotnet-install` scripts and all downstream consumers of it (`actions/setup-dotnet`, `UseDotNet`)
  - The dotnet-core-uninstall tool, which is currently a separate tool that is not well integrated with the rest of the .NET ecosystem
  - The VS Code SDK Install extension, which does a lot of this same logic but is tied to VS Code only
- Enabling higher-level `dotnet`-CLI-driven workflows
    - auto-acquire missing Runtimes for a worksapce
    - add new Runtime to a project and ensure it's available locally
- Auto-acquire .NET Runtimes for framework-dependent .NET Tools
- Breaking out the .NET SDK into smaller components to enable pay-as-you-go behaviors
- Easier installation and testing of nightly/beta/per-PR builds of the .NET toolchain

### How would this tool interact with existing global installations?

`dnup` will detect existing global installations but focus on managing user-local installs.
Users with existing package manager installations will continue to use those tools.
`dnup` will provide guidance when detecting unsupported installation types, and may offer to seed the user-local hive with the same versions so that there are no functionality gaps when starting with `dnup`.
It _will not_ be able to manage global installations in the initial scope. This includes uninstallation of global installs, which will be left to the user to perform.
