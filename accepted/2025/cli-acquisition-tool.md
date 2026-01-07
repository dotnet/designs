# .NET SDK Acquisition and Management Tool

- [Scenarios and User Experience](#scenarios-and-user-experience)
- [Requirements](#requirements)
- [Stakeholders and Reviewers](#stakeholders-and-reviewers)
- [Design](#design)
- [Q \& A](#q--a)
- [Milestones](#milestones)
  - [Proof of Concept](#proof-of-concept)
  - [Internal Preview](#internal-preview)
  - [Public Preview](#public-preview)
  - [GA](#ga)
- [Other Concerns](#other-concerns)
  - [Aspire](#aspire)
  - [VS Project System](#vs-project-system)
  - [Cross-cutting requirements](#cross-cutting-requirements)

**Owner** [Chet Husk](https://github.com/baronfel) | [Daniel Plaisted](https://github.com/dsplaisted)

Users of the .NET SDK today face a wide array of choices when getting our tooling, with multiple distribution channels that release at different cadences.
While these options serve specific needs, several important user cohorts are not well served and have asked for a more unified approach to getting started with our tooling.

This proposal introduces a standalone install and SDK management tool (tentatively named `dnup`) that provides a unified interface for acquiring and managing .NET tooling across all supported platforms, focusing on user-local installations that can be managed (largely) without elevation.

This tool is especially critical for users that are

* new users, especially those from the Node or Rust ecosystems where tools like `nvm` and `rustup` are common
* Linux users who typically use packages from their distro, as not all distro maintainers support feature bands beyond 1xx (though this _may_ change with the advent of the VMR in .NET 10).
* macOS users, who can access all feature bands but have no way of cleaning up old installations (ignoring the dotnet-core-uninstall tool)
* users who manually perform _any_ SDK installations and therefore are responsible for clean up
* Users that need to manage CI/CD environments consistently across multiple platforms

## Scenarios and User Experience

### Brand-new learner

Sasha wants to learn C# and develop applications on her Mac.
She searches and is led to a 'getting started' style page that directs her to download our bootstrapper.
She navigates to `get.dot.net` and clicks the prominent link for `dnup-init`.
She then clicks the downloaded executable, which runs the bootstrap process where she is prompted through an interactive setup:

```bash
Where should we install .NET to? *(~/Library/Application Support/dotnet)*
> <enter>
What version should we install? *(latest)*
> <enter>
Should we set up your environment/PATH automatically? *(Y/n)*
> <enter>
Installing .NET SDK 10.0.100 to ~/Library/Application Support/dotnet
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
The agent talks to an acquisition MCP server that lets him know he has two older .NET versions that have known vulnerabilities and asks him if he wants to update - this MCP server may be powered in part by asking `dnup` to check and return information about available updates, or the LLM could call out to `dnup` directly for this same information.
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
- **Awareness of vulnerabilities**: Surface information on support status, vulnerabilities/CVEs, and other data to help users make an update decision
- **IDE Integration**: Enable seamless integration with VS Code, Visual Studio, and other development environments
- **Cross-platform Consistency**: Provide consistent experience across Windows, macOS, and Linux
- **global.json Support**: Detect and respect project-specific SDK version requirements
- **Self-maintenance**: Keep the bootstrapper tool itself up to date

### Non-Goals

- **Package Manager Integration/Global Installations**: Will not support installers beyond .zip/.tar installs initially
- **Replace dotnet.exe**: The bootstrapper will not be dotnet.exe itself
- **NuGet/Tools Support**: Tools are not in scope for `dnup` - instead they would belong to a Native AOT `dnx` or Native AOT'd `dotnet` CLI
- **SDK Size Reduction**: Size reduction will be tackled separately, focused on deduplication and factoring out slices of functionality

## Stakeholders and Reviewers

- **.NET SDK Team**: Core implementation and CLI design
- **VS Code C# Extension Team**: Integration scenarios and user experience
- **Visual Studio Team**: Ensuring compatibility with existing VS workflows
- **Aspire Team**: Initial consumer - hide the `dotnet` toolchain to simplify onboarding for polyglot developers
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

This configuration will is shell-specific, so the tool will need to detect the user's shell and modify the appropriate profile file (e.g. `.bashrc`, `.zshrc`, etc.) to set the `DOTNET_ROOT` and `PATH` variables.
We have some prior art in the `dotnet` CLI codebase for detecting the shell that is in use, which can be reused here.
The different shells (and to some extent different OS platforms) have different ways of distributing such changes, so the tool will need to know about those to be a bit opinionated about how it makes these changes.

This shell-specific configuration will need to be independently run via a command, because the user may choose a different shell after the initial setup.

Post-MVP we may need to consider cross-architecture toolchain support. In that scenario `dnup` would set not only the `DOTNET_ROOT` and `PATH` variables, but also the relevant `DOTNET_ROOT_<ARCH>` variables required to light up support in the `dotnet` muxer for detecting and using cross-arch tools.

### Install locations

These installs will be located at a default _user-local hive_ instead of a globally-reachable system hive.
This will allow users to manage new tooling versions without needing to elevate.
The tool will support being configured to install to other locations via configuration file, so that simple use of the tool remains consistent.

### Install provenance

For MVP, the data about installs will be sourced from the release manifests that are published on a regular cadence by the .NET Releases team.
These manifests, and the release artifact data and signatures they contain, will enable the tool to confirm the provenance of any toolsets downloaded to the users' computer.

Before stable release this source, structure, and signature validation needs of the manifest data will be made configurable and documented, so that users can point the tool at other sources of data if they wish.

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

We intend to distribute `dnup` in slightly different ways based on the user's platform. The common case will be navigating to a website with a memorable name, like `get.dot.net` (which already exists but you get the idea). This site would do basic platform detection, and on Windows it would present a download for the matching arch-specific Windows executable, while on macOS and Linux it would present a small shell script that bootstraps the download and execution of the appropriate binary for that platform. The [Aspire](https://github.com/dotnet/aspire/tree/main/eng/scripts) product has a similar approach, though more detailed than we may need (at least for MSBuild).

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
    - auto-acquire missing Runtimes for a workspace
    - add new Runtime to a project and ensure it's available locally
- Auto-acquire .NET Runtimes for framework-dependent .NET Tools
- Breaking out the .NET SDK into smaller components to enable pay-as-you-go behaviors
- Easier installation and testing of nightly/beta/per-PR builds of the .NET toolchain

### When can I use this?

The SDK team plans to release an initial version of `dnup` covering environment setup and basic install functionality as soon as possible. We then want to gather feedback from users and iterate on the feature set until the tool is a consistent, one-stop-shop for acquiring and managing .NET tooling of all kinds.

The end goal is to stabilize the core workflows and experiences enough to justify the tool being as assumed part of the `dotnet` CLI's tooling capabilities - we want the SDK to be able to rely on `dnup` and use it to orchestrate more complex workspace-wide gestures like a toolset-level `dotnet restore` kind of operation that gets all of the SDKs and Runtimes needed for a workspace.

## Milestones

This is a large effort, and there are different areas of the work that will progress at different speeds. In addition, for compliance reasons we need to ensure that the tool is secure and robust before we can recommend it for broad usage. As a result, we expect to have multiple milestones along the way. Each of these milestones will likely be usable by motivated users, but we will not recommend them for broad usage until we reach the Public Preview milestone.

* **Proof of Concept**
  * at this phase we will be able to do the core actions, like installing publicly-released SDKs
* **Internal Preview**
  * at this phase we'll be able to do more lifecycle management, onboarding, checking for updates
  * we may be missing crypto validation or other security features that would prevent a general public preview
  * this is the phase where we should receive core end user feedback and iterate on the main UX
* **Public Preview**
  * at this phase we'll have all of the security related requirements and will be ready for broad usage
* **General Availability**
  * fit-and-finish work, documentation, telemetry, etc will all be present before we reach this milestone
  * blocking feedback from earlier previews will be addressed
    * if feedback from public preview is overwhelmingly negative we may end up stopping the effort overall in favor of other approaches to solving the acquisition/management problems

More details on these proposed milestones will likely vary, but may look like:

### Proof of Concept

* can download a platform-specific `dnup` binary from a GitHub release or other central location
* can use `dnup` to install a specific public version of the .NET SDK to a (configurable) user-local location
* can immediately use that user-local install

### Internal Preview

* can also use `global.json` information to derive the version(s) of the SDK to install
* can check for updates to installed SDKs via `dnup update --check` or similar
* installs are tracked by `dnup` for future management scenarios

### Public Preview

* settle on the name for the tool
* uninstall of installed SDK
* signature validation of manifests and downloaded artifacts
* interactive UX, prompting, progress

### GA

* self-update of the `dnup` binary is implement
* telemetry is implemented and documented
* public documentation is created
* public download url/script/mechanism is up
* the `dnup update` command is fully implemented

## Other Concerns

These milestones are _people_-focused. We have other use cases:

### Aspire

The Aspire CLI wants to use `dnup` to manage its .NET toolchain,
hiding the complexity of .NET from users who may not be familiar
with it. However, they do not want to shell out to another process
to do so. Aspire will vendor `dnup` as a library and call into it
directly to perform the same operations that the CLI does. This
means that we need to create the library interface for the core
operations quickly and provide an implementation that they can
consume. The library interface and implementation _must_ be
AOT-compatible, just like `dnup` itself.

Aspire is ok with evolution of this interface over time,
so we shouldn't be constrained by binary compat concerns until the
GA milestone.

### VS Project System
Use dnup mostly for an update check at the moment. In the future, lean into the
support for dnup more, using it to power in-editor notifications and actions
that end up driving dnup-based update commands. The IDE and the CLI are
managing the same logical set of installs at this point.

### Cross-cutting requirements

#### Security
Everything to do with checking updates/downloading packages/verifying
downloaded things should be done securely, pointed at our manifests and signed
artifacts by default. A user should be able to specify alternative manifest download locations, and likely as a result alternative certificates to use for verifying the manifest and artifact content.
These alternative configuration details should be settable by CLI arguments,
environment variables, and (post-MVP) configuration files - ideally a unified
dotnet toolchain configuration file.

#### Usability
For human users, the above interactions should be interactive where possible.
Progress should be reported for long-running operations, required decisions
should accept both direct CLI arguments as well as interactive prompts. The
interface should strive to be terminal screen-reader friendly by using layout/
semantic color in reasonable ways. (We have some design docs from `azd` here
with guidelines).

All VT-code/interactivity should be able to be disabled via CLI argument and/or environment variables like NO_COLOR, etc.

All commands should support changing their output based on CLI arguments or implicitly-discoverable signals like environment variables. We should be able to have the same command support
* vivid interactive UX
* plaintext non-interactive UX
* structured output (json, maybe tabular?)
* an LLM-friendly format such as markdown
and those formats should be directly specifiable _or_ inferred from environmental characteristics.

#### Telemetry

We should collect usage/feature-based telemetry for key scenarios. This collection should adhere to our existing telemetry guidelines, and be opt-out-able via a simple mechanism (env var). We should not collect PII or sensitive information - anything potentially sensitive should be hashed before being sent in alignment with our existing telemetry practices.

* installs/inits of dnup
* installs of particular channels - what channels are users requesting?
* rates of global.json usage - how many users are pinning/controlling versions, and what kinds of bounds are they expressing?
* how many disparate installs are users managing? (meaning, how many different versions of the SDK are they managing, and how many different 'roots' are they managing them in - one user-local root, installs-per-repo, etc)

#### CLI Update

`dnup` needs to be able to update itself (or at least recognize when a new version is available and let the user know how to go get it).
