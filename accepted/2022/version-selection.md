# .NET 7 Version Selection Improvements

**Owner** [Rich Lander](https://github.com/richlander)

Each development platform is based on some composition model for platform versions (where they are installed, private vs global installation/visibility, how they are selected, ...). On one hand, there are platforms like Node.js that have a single version in scope at a time (tools like [`nvm`](https://github.com/nvm-sh/nvm) make that more manageable) and on the other we have .NET that allows for multiple versions in scope at a time (managed by the [`dotnet`](https://docs.microsoft.com/dotnet/core/tools/dotnet) tool and MSBuild). .NET offers a multiple ways of selecting a runtime or SDK version, however, there are some inconsistencies across the various gestures and missing capabilities. This document proposes improvements to .NET version selection.

Summary of significant changes:

- Disable multi-level lookup.
- Change default for `global.json` SDK version to a floor version, as opposed to the current pinning behavior.
- Enable controlling SDK version by environment variable (ENV).
- Enable proxying to a local/private SDK via the `PATH` SDK.
- Enable specifying a floor runtime version for an environment.

Note: The last two of those changes are more speculative (less likely to happen).

Let's start by considering some concrete customer scenarios to improve (some of which are overlapping).

## Dev inner loop consistency with lab builds

For many teams, infra builds are the center of their environment. Infra is intended to mean any of Continuous Integration (CI), Pull Request (PR), or Official builds. Teams typically put the most design and implementation effort into their infra builds, with the goal of establishing their most correct and reliable environment. This includes defining their SDK and runtime platform dependencies. In other words, if there is a bug that repros in CI but not on your local dev box, you still have a bug, while the opposite might not be true.

Most devs would prefer a local environment that matches their lab environment. The practice of working in matching environments (local vs lab and across teammates) increases productivity since it inherently avoids spurious local issues or surprise problems in PR builds. There is no good guidance or productized UX that would help teams establish matching environments.

[Ideally, devs would clone a repo](https://github.com/dotnet/sdk/issues/8254), `dotnet build` a solution file or double click on it (to be opened in Visual Studio) and the rest of establishing a matching environment would be taken care of. We're a ways off that. Let's consider what folks do today.

Many repos (including .NET repos) follow a pattern of running a script (often run as part of the build) that downloads a specific patch of the .NET SDK, installs it into a private directory (often just off the root of the repo) and then runs the build and tests for the repo. This process has a lot of benefits, primarily that everyone working on the repo (at least on the same branch) uses the same SDK version (whether they have the SDK installed or not). That patterns also makes it very quick and convenient to build a repo on a new machine.

Note: The standard script used by dotnet repos downloads private copy of SDK only if there is not one of the right version on your path (https://github.com/dotnet/runtime/blob/aec1f696a2bc54884dcbad589111d025267063f9/eng/common/tools.sh#L144-L145).

You can see this pattern with the following example (in a disposable docker container). It is straightforward and fast.

```bash
root@fdd8e1bb1253:/# git clone https://github.com/dotnet/iot
Cloning into 'iot'...
root@fdd8e1bb1253:/# cd iot/
root@fdd8e1bb1253:/iot# ./build.sh
Downloading 'https://dotnet.microsoft.com/download/dotnet/scripts/v1/dotnet-install.sh'
Attempting to install dotnet from public_location.
dotnet-install: Note that the intended use of this script is for Continuous Integration (CI) scenarios, where:
dotnet-install: - The SDK needs to be installed without user interaction and without admin rights.
dotnet-install: - The SDK installation doesn't need to persist across multiple CI runs.
dotnet-install: Attempting to download using primary link https://builds.dotnet.microsoft.com/dotnet/Sdk/6.0.100/dotnet-sdk-6.0.100-linux-x64.tar.gz
dotnet-install: Extracting zip from https://builds.dotnet.microsoft.com/dotnet/Sdk/6.0.100/dotnet-sdk-6.0.100-linux-x64.tar.gz
dotnet-install: Adding to current process PATH: `/iot/.dotnet`. Note: This change will be visible only when sourcing script.
dotnet-install: Installation finished successfully.
  Determining projects to restore...
  Restored /iot/eng/common/internal/Tools.csproj (in 5 ms).
  Determining projects to restore...
```

There are significant gaps with this approach:

- Only works well in the context of running repo scripts.
- Doesn't work well for interactive development with `dotnet` CLI, unless you add the private install root (`/iot/.dotnet` in the example) to the `PATH`. That works but isn't convenient for most users.
- If the global .NET is used accidentally (via the `PATH`), the dev either gets an error due a mismatch with a [repo-resident `global.json` file](https://github.com/dotnet/iot/blob/main/global.json) or it matches "enough" and works but isn't actually the right one and may have slightly different behavior. This may cause the dev to do one of several things, including installing the matching private version globally or editing `global.json`, both of which are not the best practice and a drain on productivity.
- Works poorly with Visual Studio or Visual Studio Code, where relying on an ephemeral `PATH` setting is awkward.
- [Multi-level lookup](https://github.com/dotnet/runtime/blob/main/docs/design/features/multilevel-sharedfx-lookup.md) causes a global SDK or runtime to be used, even when you think you are following the best practice to use a private one.

Ideally, there would be a way to lock a repo to a .NET version and location, using a setting similar to `DOTNET_ROOT`. However, environment variables (ENVs) are a poor choice since they are not convenient for using a solution or project file as-is and don't compose nicely with `PATH`. We need an experience that works via a sweet-spot combination of configuration and convention.

## Intuitive version selection

.NET version selection includes cases of being both overly strict and overly lax. Version selection should have a baseline intutitive behavior and provide (as appropriate) additional experiences to meet niche needs.

The most problematic behaviors today are:

- [Multi-level lookup](https://github.com/dotnet/sdk/issues/12353) can result in the global install location being used when a private location is intended.
- `global.json` defaults to roll-forward on patch version for the specified SDK version (for example `6.0.100` -> `6.0.101`), by default. This results in fragile environments. Instead, the version specified should be a version floor by default.

## Convenient and consistent infra builds

As a (.NET) team, we get significant feedback about CI services, primarily Azure DevOps and GitHub Actions. Customers rely on these services very heavily for their builds, for projects big and small. A key aspect of CI is automatic and managed delivery of developer platforms (like .NET). Developers expect same-day delivery of .NET in CI. If there is a public blog post about a new .NET version, that new version should be available in CI with very low or (ideally) no effort on the part of developers.

As we were preparing to ship .NET 6, we took a look at the [GitHub Actions Virtual Environments VM images](https://github.com/actions/virtual-environments). We saw that the [Actions Ubuntu 20.04 images](https://github.com/actions/virtual-environments/blob/main/images/linux/Ubuntu2004-Readme.md), for example, included [several versions of the .NET SDK](https://github.com/actions/virtual-environments/blob/main/images/linux/Ubuntu2004-Readme.md#net-core-sdk), some of which are out-of-support. That caused us to re-assess our approach to these CI images.

At the time of writing (February 2022), the following .NET SDK versions are available in those images:

- `2.1.302`, `2.1.403`, `2.1.526`, `2.1.617`, `2.1.701`, `2.1.818`
- `3.1.120`, `3.1.202`, `3.1.302`, `3.1.416`
- `5.0.104`, `5.0.210`, `5.0.303`, `5.0.404`
- `6.0.101`

The .NET SDK has a concept of ["feature bands"](https://docs.microsoft.com/dotnet/core/porting/versioning-sdk-msbuild-vs) (which are a compatibility boundary for patches). Those are all the hundreds-based versions, like `2.1.3xx` and `2.1.4xx`. By default, if you specify `2.1.300` (or `2.1.317`), your builds requires a `2.1.3xx` SDK (at least as high as the version number specified), but will not work with `2.1.400` or higher versions. This behavior is what motivates (one could argue, requires) the GitHub team to maintain all these .NET SDK versions so that customer builds work, even out-of-support ones. It is easy to come to the conclusion that this is a problematic design, leading to unfortunate requirements on the part of service providers like GitHub.

At the same time, GitHub offers the [`setup-dotnet`](https://github.com/actions/setup-dotnet) action for configuring the .NET SDK. You can grab any patch you want (including out-of-support ones), and it offers roll-forward capabilities for servicing.

We decided to NOT include .NET 6 in the GitHub Ubuntu images to see if we could encourage users to migrate to `setup-dotnet`. That fixes everything, so perfect. Job done. Our users told us: ["Really?"](https://github.com/actions/virtual-environments/issues/4424#issuecomment-1012698407) They were right. We are now offering the .NET 6 SDK in Ubuntu images, as we should have from the start.

Clearly, the dozen+ SDKs we're shipping isn't the right solution. However, it is also clear that relying solely on `setup-dotnet` is also not the solution. Another option is to tell everyone to use `global.json` "properly", which would mean ensuring that the existing `rollForward` property is always properly set. That's a nice idea in theory, but its really hard to require changes distributed across thousands of code-bases and a much larger set of developers. We could consider changing the default roll-forward setting.

This situation is similar with Azure DevOps. We expect that other cloud and CI providers have similar challenges due to the default behavior of the .NET SDK. We're sorry about that.

SDK version selection is entirely focused on repo-resident `global.json` files and that there are not adequate other controls. For example, runtime selection can be controlled by environment variables (ENVs). We could expand SDK selection to have the same broad set of controls as runtime selection. That would enable a CI build (possibly via a GitHub Actions setting) to override `global.json` in a repo, as either a temporary or permanent approach.

## Migrate old apps to newer runtimes

Note: This customer scenario is speculative and not based on customer feedback, but on expected future need. We'll likely choose to not productize at this time.

As a development platform becomes mature and established, it leads to organizations with large deployments of apps. Certainly, we see that with .NET Framework today, and we're headed that way with .NET (Core). A runtime-based platform like .NET inherently has the challenge with compiled applications getting left behind, requiring organizations to do something to adapt those applications. Old .NET applications have hard-bound references to old runtime versions and those older applications don't want to use newer runtime versions without some encouragement. We expose good options for migrating applications (as source or binaries) to newer versions, however, they are not always convenient to use across a large body of apps. For example, applying a single version or a single roll-forward policy to thousands of apps isn't going to be an attractive choice for large organizations.

Ideally, organizations could adopt the following approach for runtime management in their environments:

- Adopt and deploy new .NET versions into their environments quickly, with no impact on existing applications (already the case).
- Stop deploying old .NET versions into their environment as soon as they go out-of-support (this is a current challenge).
- Address old applications (that depend on out-of-support runtime versions) by:
  - Re-compile old applications to target an in-support version, or
    - Deploy some form of configuration to their environment which configures the .NET runtime minimum floor version (likely to the oldest in-support version). Any application requesting an old out-of-support .NET version would automatically adopt that version as it's minimum. For example, one can imagine (at the time of writing) that `3.1.0` would be a good floor version, which would cause all .NET Core 2.1 apps to automatically run on .NET Core 3.1 within that environment, while leaving .NET Core 3.1+ apps unaffected by the configuration.

Re-compiling applications to run on a new .NET version is already possible, while the configuration option is not.

Note: It is possible and likely that some applications will fail to run on a new .NET version. That's OK. Imagine 80% of apps just work and 20% do not. That means that you now have a much smaller group of apps to address, and can have a bit more time to do so. Admittedly, you'd need some partitioning in your environment to deploy such a policy.

## Principles

Version selection should have the following principles:

- The `dotnet` that is launched is the one that is used, with no magic policies by default.
- Convenience/opinionated/magic experiences are opt-in.
- Common tasks should be made easy/easier, possibly via opt-in convenience experiences.
- SDK and runtime selection policies and controls should be aligned, as much as possible (to aid intuition and overall simplicity).

## Model

Let's briefly recap the fundamentals of the version selection model. It is based on the following configuration concepts:

- **Install root** -- Where should the host look for .NET versions?
- Which version should be selected?
  - **Minimum version** -- What's the minimum version that is required?
  - **Roll forward policy** -- What's the policy for determining the highest version acceptable?
  - **Preview versions** -- Are preview versions acceptable to use?
- **Configuration** -- Which configuration knobs are available for answering these questions?

.NET hosts select versions for two components:

- Runtimes (AKA "frameworks", under the `shared` directory)
- SDKs (under the `sdk` directory)

Note: Apphost (`myapp`) and custom hosts only select runtimes. The `dotnet` host is responsible for selecting both SDKs and runtimes.

The following sections describe the updates in detail. They are intended be satisfy the customer scenarios described earlier (at least, in part). They also propose improvements to known gaps and bugs that are distinct from the customer scenarios. Existing behavior is also provided as a baseline to help readers understand how the proposed changes fit in.

## Location configuration

.NET hosts expose the following (existing and proposed) location configuration for discovering the .NET install root location.

For SDKs and runtimes:

- `PATH` -- The use of `dotnet` is resolved by the `PATH`.
- Absolute path -- The use of an absolute path to `dotnet` (like `C:\PrivateSDK\dotnet`).
- [NEW SDK scenario] Convention location -- `dotnet` looks for SDKs and runtimes in the `.dotnet` directory in the current or (grand-*)parent directory.

For runtimes only:

- Global installation registration -- [register different install location for each architecture](https://github.com/dotnet/designs/blob/main/accepted/2021/install-location-per-architecture.md).
- `DOTNET_ROOT` -- specifies installation root (architecture-specific ENVs can also be used).

Note: `DOTNET_ROOT` is currently only honored by apphost. We should decide if it should be honored by `dotnet`, too. This topic requires more thought.

Note: You may see [references to `DOTNET_INSTALL_DIR`](https://github.com/dotnet/sdk/blob/724762e77544716ddef17a34bb837151826dbd66/src/Tests/Microsoft.NET.TestFramework/ToolsetInfo.cs#L241). It isn't honored by the host. It is an ENV that the team uses in some of its scripts and tests. It is out of scope of this proposal.

### Multi-level lookup

> Customer value: This feature contributes to the "intuitive version selection" customer scenario.

[Multi-level lookup](https://github.com/dotnet/runtime/blob/main/docs/design/features/multilevel-sharedfx-lookup.md) enables looking at both private and global .NET locations to find runtime and SDK components. It has utility in theory, but suffers from two big problems: it causes significant confusion for many people in practice, and it is only supported on Windows. It has never been requested to be enabled on macOS or Linux but has been requested many times to be disabled on Windows. [Multi-level lookup will be disabled](https://github.com/dotnet/core/issues/7131) starting with the .NET 7. We will not include the ability to re-enable the feature.

```json
{
  "sdk": {
    "version": "7.0.100"
  }
}
```

The primary value of the multi-level lookup is described by the following example scenario:

- Build and run a .NET 6 app with a private .NET 7 SDK.
- The .NET 7 SDK is used to build the app.
- Multi-level lookup is used to find a .NET 6 runtime that is installed globally but not in the private location.
- The app runs (via the globally installed .NET 6).

It is simpler to install the .NET 6 runtime to the private location, resulting in much higher certainty that the desired versions are used. Multi-level lookup is best thought of as a [YOLO feature](https://en.wikipedia.org/wiki/YOLO_(aphorism)) and should never have been enabled by default (if it was needed at all).

### Local `.dotnet`

> Customer value: This feature contributes to the "Dev inner loop consistency with lab builds" customer scenario.

Note: We haven't decided if/when we will build this feature.

.NET Team build scripts implement a convention where a private .NET SDK is installed into the `.dotnet` directory at the repo root. The version is controlled via the [version in the `global.json` at repo root](https://github.com/dotnet/iot/blob/79b9c35b6b575917ab841cd1b499a840c6f15bd1/global.json#L11). Currently, this pattern is just a convention, which `dotnet` doesn't honor. You have to add the `.dotnet` directory to the `PATH` if you want to be able to type `dotnet` and use the private .NET installation.

We will teach `dotnet` to recognize this pattern and proxy the launch of the global `dotnet` to a local/private `.dotnet` location. One can think of this as the opposite behavior of multi-level lookup. This means that you can rely on your `PATH` copy of `dotnet` for convenience while using an isolated/private `dotnet`. It's a sort of version-manager-like feature.

Feature characteristics:

- User must opt-in to using the local `.dotnet` convention location feature.
- Only supported for the `.dotnet` directory (not configurable to something else).
- The directory must contain an SDK installation. Otherwise, error, and direct user to the [dotnet-install script](https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-install-script) help page.
- Note: the `https://aka.ms/dotnet-download` download link isn't helpful in this case (we need a private, non-global, install for this scenario).
- Can be in current directory or (grand-*)parent (same probing logic as `global.json`).
- Upward directory probing terminates if either a `.dotnet` directory or a `global.json` file are found.
- If `.dotnet` and `global.json` exist in the same directory, `.dotnet` wins; `global.json` version information is ignored, however other information may still be used.
- No relationship to `nuget.config` and its probing logic. Open question of whether `nuget.config` probing should also terminate at `global.json`.

User opt-in characteristics:

- Set `--use-local` via the CLI.
- It is not an error to opt-in and for a `.dotnet` directory not to be found.
- There is no searching for `.dotnet` in absence of opt-in.

Requiring using `global.json` opt-in:

- Some repos may want to force using a private runtime and desire to produce an error (with a helpful error message) in absence of a correct configuration.
- `global.json` exposes a new boolean property (child of `sdk`): `requireUseLocal` .
- The `.dotnet` directory must exist at the same location as `global.json`.
- Opt-in is still required when `requireUserLocal` is set to `true`. Setting the value to `false` has no meaning.
- User can override this setting by passing `--use-local false` as an argument to `dotnet`.
- Users can also overide this setting by using the `--use-this` flag (described later).

Implications for Visual Studio (and other IDEs), in order to enable this feature (like enable double click on a solution):

- Must opt-in on behalf of users (via CLI) to enable using the feature, via a prompt.
- By definition, the feature will result in using a different .NET SDK. Visual Studio would need to support running this way.

Notes:

- Apphost will not honor this value, nor does it consult `global.json` in any scenario. Same with the `dotnet myapp.dll` pattern.
- CI environments are not encouraged to use this feature, but abs-path to `.dotnet/dotnet` or add `.dotnet` to the `PATH`.

Considerations for the future:

- We could add a `dotnet install` command to enable private acqusition.
- We could install .NET (local) Tools into the repo-resident `.dotnet` directory instead of into the user profile location. That approach might avoid the need for a local tools manifest.
- It would be straightforward to build a version manager (like [DNVM](https://www.codeproject.com/Articles/1005145/DNVM-DNX-and-DNU-Understanding-the-ASP-NET-Runtime)) using symlinks for the `.dotnet` directory. This would be particularly useful for preview and nightly builds.

## SDK version configuration

SDK version selection is (currently) exclusively controlled via `global.json`. This is a file that can be placed anywhere on your machine (although typically within a repo). The `dotnet` host will look for it in the current directory or one of its (grand-)parents, including up to the root of the drive.

```json
{
  "sdk": {
    "version": "6.0.201",
    "rollForward": "latestMajor",
    "allowPrerelease": false
  }
}
```

This `global.json` example requires the 6.0.201 SDK or higher, and won't accept using preview versions (like .NET 7 Preview 1). There are other `global.json` configuration options, however, the three listed are the relevant ones.

### Default SDK roll-forward behavior

> Customer value: This feature contributes to the "intuitive version selection" and "Convenient and consistent infra builds"customer scenarios.

By default, SDK version selection does not roll-forward past a feature band. For example, the following `global.json` file requires the use of .NET 6.0.100 and will accept any version through 6.0.199. It will not allow for the use of 6.0.200 (if 6.0.1xx builds are absent).

```json
{
  "sdk": {
    "version": "6.0.100",
  }
}
```

The feature bands have a reason to exist. With each feature band (`.200`, `.300`, ...), there is typically a significant update in a tools component like Roslyn, MSBuild, or NuGet. The feature bands are not intended for intentional breaking changes, but there may still be compatibility breaks. Note: If the breaks are significant, there will likely be an investigation and attention applied to resolving them (in full or in part).

This default behavior is overly strict. Starting with .NET 7, the specified version will be treated as the floor version, with no ceiling.

For example, the following two `global.json` files would be equivalent.

First:

```json
{
  "sdk": {
    "version": "7.0.100"
  }
}
```

Second:

```json
{
  "sdk": {
    "version": "7.0.100",
    "rollForward": "latestMajor",    
  }
}
```

The pre-.NET 7 "pinning" behavior becomes opt-in and can be achieved with the following pattern:

```json
{
  "sdk": {
    "version": "7.0.100",
    "rollForward": "latestPatch",
  }
}
```

This change will only affect `global.json` files with a `version` of `7.0.100` or greater. It will not affect `global.json` files with `6.0.100` (for example) even if .NET 7 SDK is installed on the machine.

The complete set of [roll-forward options are defined in the `global.json` schema](https://docs.microsoft.com/dotnet/core/tools/global-json#rollforward).

### SDK roll-forward gestures

> Customer value: This feature satisfies the "intuitive version selection" and "Convenient and consistent infra builds"customer scenarios.

We will expand the SDK roll-forward gestures to match the similar capabilities for the runtime. These new gestures would enable controlling SDK roll-forward beyond just `global.json`.

It's useful to consider the options that the host offers for runtime roll-forward, via the following ENVs:

- `DOTNET_ROLL_FORWARD` -- a roll-forward policy like `LatestMajor`.
- `DOTNET_ROLL_FORWARD_TO_PRERELEASE` -- a boolean value on whether roll-forward candidates should include pre-release versions.

The SDK has similar concepts (per an [earlier design](https://github.com/dotnet/designs/blob/main/accepted/2020/global-json-updates.md)). The most obvious approach is to expose matching ENVs as `DOTNET_SDK` to differentiate:

- `DOTNET_SDK_ROLL_FORWARD`
- `DOTNET_SDK_ROLL_FORWARD_TO_PRERELEASE`

That takes care of the roll-forward ENVs. Let's look at what the CLI offers for the runtime:

- `--roll-forward` -- specifies runtime roll-forward policy

We can follow the same pattern again, by offering a mirror SDK variant:

-- `--sdk-roll-forward` (default value is `major`)

Note: The CLI doesn't offer a CLI affordance that matches `DOTNET_ROLL_FORWARD_TO_PRERELEASE`. We should wait for more feedback before adding pre-release-oriented switches to the CLI, for the runtime or SDK.

### SDK version selection

The host offers `--fx-version` to select a specific runtime. We will add support to the same thing for selecting an SDK with `--sdk-version`.

### Ignore SDK version policies -- `--use-this`

The SDK has version policies -- including `global.json` -- that you might just want to ignore. The following gesture is intended as the ultimate override. It will ignore `global.json` and any other SDK policies (ENVs or CLI params).

```bash
dotnet run --use-this
```

You will get whichever `dotnet` you are using (via `PATH` or abs-path). `global.json` will be ignored for version selection. The local `.dotnet` location will also be ignored.

## Runtime version configuration

Runtime version selection offers configuration via file, ENV, and CLI, detailed in [Runtime Binding Behavior](https://github.com/dotnet/designs/blob/main/accepted/2019/runtime-binding.md). This section describes the improvements that should be made to improve runtime version configuration.

The following `*.runtimeconfig.json` file demonstrates the information needed to describe the version requirements for an ASP.NET Core application.

```json
{
  "runtimeOptions": {
    "tfm": "net6.0",
    "frameworks": [
      {
        "name": "Microsoft.NETCore.App",
        "version": "6.0.0"
      },
      {
        "name": "Microsoft.AspNetCore.App",
        "version": "6.0.0"
      }
    ],
    "configProperties": {
      "System.GC.Server": true,
      "System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization": false
    }
  }
}
```

The key aspect to notice is that there are two runtimes listed, specified as "frameworks". In this case, the versions match exactly, but there is no requirement that they do so.

Let's say you want to run this .NET 6 app on .NET 7 without source code changes. We can validate the efficacy of the existing configuration gestures to achieve that end.

- `TargetFramework` -- Changing the `TargetFramework` MSBuild value to `net7.0` will result in the `frameworks` defaulting to a `7.0.0`.  That works, although requires a change to app source and for the app to be rebuilt.
- `RuntimeFrameworkVersion` -- This MSBuild setting is an override on default `TargetFramework` behavior. It updates all frameworks to a single value, like `7.0.1`. This value can be specified in the project file or (more likely) as an argument to `dotnet build`. This option is one step better since it doesn't require updating source, but still requires a rebuild.
- `version` in `runtimeconfig.json` -- This value can be hand-edited. In general, editing `runtimeconfig.json` is not a first-class experience (that's why [`runtimeconfig.template.json`](https://docs.microsoft.com/dotnet/core/runtime-config/#example-runtimeconfigtemplatejson-file) exists). For apps that will not be rebuilt, hand-editing `runtimeconfig.json` is a good approach.
- `DOTNET_ROLL_FORWARD` and `DOTNET_ROLL_FORWARD_TO_PRERELEASE` -- These host ENV settings enable specifying a roll-forward strategy. They work well, although can sometimes feel a bit awkward if you just want to specify a specific version.
- `--fx-version` -- This host CLI setting is the host analog of `RuntimeFrameworkVersion`. It enables updating `framework` versions at the last possible moment, at the point of launching an application. Unfortunately, this CLI argument only affects the first framework it finds (the ASP.NET Core reference would remain as `6.0.0`).

In summary, the ENVs work well, and the CLI argument is broken.

### Fixing `--fx-version`

> Customer value: This feature satisfies the "Intuitive version selection" customer scenario.

We will update `--fx-version` to match `RuntimeFrameworkVersion` behavior. That means the `--fx-version` value will be used to update all frameworks. Ideally, the SDK and host would have knowledge on the "known frameworks" and only update those. That may not be required at this time, but would if we ever enable third party frameworks.

This change will be made for version numbers >= `7.0.0`.

In absence of fixing this behavior, we should deprecate this CLU argument since it doesn't offer a correct behavior currently.

### Enabling easier migration with `DOTNET_FX_VERSION_FLOOR`

> Customer impact: This feature satisfies the "Migrate old apps to newer runtimes" customer scenario.

Note: As stated earlier, we likely won't build this feature now as we haven't heard any requests for it.

To upgrade from .NET 6 to .NET 7, users needs to update the `TargetFramework` property (or set the `RuntimeFrameworkVersion` property to `7.0.0`) in app projects and then re-build. That can be a lot of work for a large app suite. We can contrast this experience with .NET Framework.

With .NET Framework, many (even most) developers DO NOT upgrade their target framework when a .NET Framework is/was released. Instead, they keep their apps targeting an older .NET Framework version (like .NET Framework 4.5.2) and then deploy the newer .NET Framework version on their development and production machines. This can also happen by upgrading to a newer version of Windows that includes a newer .NET Framework. The older apps automatically run on the newer .NET Framework version. .NET Framework has an in-place update model, so this automatic roll-forward policy is an inherent choice. In this model, `TargetFramework` primarily affects the APIs that the application can use and the minimum version it requires.

With .NET/.NET Core, the `TargetFramework` property has a similar role, but also caps the version that the app can use (by default). For example, a .NET 6 app will happily roll-forward to newer .NET 6 patches (like `6.0.12`), but never automatically roll forward to .NET 7. That's a good model from a compatibility perspective. Most developers are not comfortable with automatic roll-forward because they cannot predict the future.

It would be nice, however, if we had a better model for reacting to the past. The one version selection feature we're missing is a global minimum version that rolls apps forward to a minimum version if they reference older versions.

It should have the following conceptual behavior: `max(app-version, global-minimum-version)`

One could imagine an organization with a wide range of apps. They might support only .NET LTS versions within their environment. They would likely (at the time of writing) set the following (proposed) ENV in their environment: `DOTNET_FX_VERSION_FLOOR=3.1.0`. When .NET Core 3.1 goes out of support, they could switch to `DOTNET_FX_VERSION_FLOOR=6.0.0`.

This feature can be thought of as an analog to `global.json` SDK version selection, but for controlling the runtime version. Thinking of it that ways helps contextualize that this feature is another case of creating parity between SDK and runtime selection features.

An organization could alternatively use `DOTNET_ROLL_FORWARD=Major`. On the upside, this capability already exists and would provide much of the same behavior. On the downside, roll-forward affects all versions, and could mask some deployment problems that should be resolved. For example, the organization might intend to install both .NET Core 3.1 and .NET 6, but accidentally only install .NET 6. The roll-forward configuration could mask that issue. Even if it worked without issue, it isn't something you want without it being intentional.

Note: The `Major` roll-forward policy is only applied when the requested major version is NOT available. It doesn't force applications to use a later version when the preferred version is available.

Note: Assuming we enable this scenario, the .NET application framework teams (like ASP.NET Core) would need to focus more on binary upgrade compatibility.

### Support ranges

Some folks have asked us for a way to state compatibility for a range of versions, for an app. It's a good scenario, but not a feature we've designed or invested in.

## Simplifying roll-forward gestures

The following changes don't directly contribute to the customer scenarios described earlier, but are intended to improve overall developer experience.

Roll-forward gestures are harder to use than needed. There are a few simplifications we could make to make the experience more straightforward. 

### Roll-forward CLI defaults

The `--roll-forward` runtime switch currently requires a value. We have an opportunity to make this functionality simpler to use by offering a generally useful policy default when you specify the switch with no value. The obvious option is `Major`. This change would likely encourage the use of the roll-forward capability. The SDK doesn't need such an experience since it already implements the `LatestMajor` policy by default.

That would enable the following gesture:

```bash
app --roll-forward
```

It would be identical to the following examples:

```bash
app --roll-forward Major
app --roll-forward Major -- --myarg 1234
```

Note: If your app also accepts its own arguments, you can use `--` (it is an industry convention) to separate host from app arguments.

This experience would also work with `dotnet`:

```bash
dotnet run --roll-forward
```

`--sdk-roll-forward` should work the same way and default to `Major` roll-forward when a value is not specified. This is demonstrated below.

```bash
dotnet run --sdk-roll-forward
```

### Simplifying upgrading SDK and Runtime versions together -- `use-latest`

In the case that you are trying to upgrade an old project (with an old TFM and old `global.json`), then you need to use multiple gestures together, as you can see demonstrated:

```bash
dotnet run --roll-forward Major --sdk-roll-forward Major
```

That's OK, but also not fun and multiple concepts to reason about.

Alternatively, we could offer that experience with one gesture: 

```bash
dotnet run --use-latest
```

It would be the equivalent of the following:

```bash
export DOTNET_ROLL_FORWARD_TO_PREVIEW=1
export DOTNET_SDK_ROLL_FORWARD_TO_PRERELEASE=1
dotnet run --roll-forward LatestMajor --sdk-roll-forward LatestMajor
```

### `dotnet run` experience

`dotnet run` has a somewhat odd experience where it will build your code for you but not run it in absence of a missing runtime. `dotnet run` is intended as a high-productivity experience. It should auto-roll-forward your app for you in absense of the targeted runtime. If it does so, it should tell you.

Today's experience:

```bash
root@59d103ada42c:/app# cat app.csproj | grep Target
    <TargetFramework>net6.0</TargetFramework>
root@59d103ada42c:/app# dotnet --version
7.0.100-preview.2.22153.17
root@59d103ada42c:/app# cat app.csproj | grep Target
    <TargetFramework>net6.0</TargetFramework>
root@59d103ada42c:/app# dotnet run
It was not possible to find any compatible framework version
The framework 'Microsoft.NETCore.App', version '6.0.0' (x64) was not found.
  - The following frameworks were found:
      7.0.0-preview.2.22152.2 at [/usr/share/dotnet/shared/Microsoft.NETCore.App]

You can resolve the problem by installing the specified framework and/or SDK.

The specified framework can be found at:
  - https://aka.ms/dotnet-core-applaunch?framework=Microsoft.NETCore.App&framework_version=6.0.0&arch=x64&rid=debian.11-x64
root@59d103ada42c:/app# dotnet run --roll-forward Major
Hello, World!
```

Proposed experience:

```bash
root@59d103ada42c:/app# dotnet run
Warning: .NET 6.0.0 was requested, but 7.0.1 was used.
Hello, World!
```

Note: We may want to print the warning to STDERR to avoid poluting the output of the app.

## Implemenation Plan

There are several (some unrelated) changes specified in this document. The following is proposed as the order (and grouping) to approach them.

Priority 1:

- Disable MLL.
- Add SDK ENVs.
- Change default for `global.json` SDK version to a floor version.

Priority 2:

- Make SDK and Runtime gestures consistent, complete, and correct (fix `--fx-version`, add `--sdk-version`, ...).

Priority 3:

- Add support for `.dotnet` "convention location".
- Add support for `DOTNET_FX_VERSION_FLOOR`.

## Summary

Version selection is one of the most fundamental features and experiences of using .NET. It could work significantly better and more intuitively. This proposal is intended to bridge much of the gap between the good experiences offered today and the first-class ones we should have.
