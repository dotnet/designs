# x64 emulation model

We're working on a [plan to support .NET for x64 emulation on Arm64](https://github.com/dotnet/sdk/issues/17463), on macOS and Windows. There are multiple decision points that we are needing to navigate to make a decision. This document is intended to explore those.

The following issues are the fundamental decision points. The choices we make for each will have significant downstream and user observable impact.

- Install location
- Model for targeting architecture
- Which .NET versions are supported (for x64 emulation)?
- Quality of the experience (how polished is it?)

## Install location

Goal: .NET is installed to a disk location(s) that makes sense across time and space.

We have to install the .NET x64 product *somewhere* on Arm64 machines. We can install it in a `dotnet-x64` directory (or similar variant), as a peer to `dotnet` or as child of the `dotnet` directory within a new `x64` directory. We decided on the latter. The former sticks out like a sore thumb. The layout within the `x64` directory will be the exact same as a native architecture x64 installation.

The expected install location of x64 .NET on Arm64:

- macOS: `/usr/local/share/dotnet/x64`
- Windows: `C:\Program Files\dotnet\x64`

There are a few implications of this decision.

- We'll only add the parent `dotnet` directory to the `PATH`, which means that only the native architecture installation will be usable when you type `dotnet`.
- It will be possible for a machine to have .NET x64 installed by itself, with no Arm64 .NET. That means that the `dotnet` directory would be empty, with the exception of the `x64` directory. It also means that the `PATH` would not be set at all (for .NET).
- If x64 builds of Visual Studio for Windows or Visual Studio for Mac are installed on Arm64, then they will need to install the Arm64 .NET SDK and/or adapt those products to .NET x64 being installed in the `x64` directory.

We could install .NET x64 to another location, but the implications would be the same.

## Model for targeting architecture

Goal: Enable developers to produce architecture-specific assets correctly and with confidence.

The .NET application model is oriented around rid-specific apps. In particular, the apphost is rid-specific, and the apphost ia a core part of the experience. In some scenarios, you don't have to pay much attention to the rid of the apphost. For example, if you exclusively develop on *and* target Windows x64, then rid-targeting isn't really important, even though it is present. If your development and target environment differ, then you need to directly participate in rid-targeting.

In the typical scenario, you can develop on your machine, for example Windows x64, and then not need to consider rid-targeting until you are ready to test on or deploy to Linux x64, for example. x64 emulation places new requirements on developers to participate in RID-targeting during inner loop development. That's new.

The core issue is that some .NET versions are available for one rid and not the other. That means that you need to explicitly target the matching rid in some way. There are three choices for that, two of which are already supported today and one not.

### Rely on the implicit rid of the SDK

In theory, the easiest approach is to simply use the matching SDK for the rid you want to target. That's the model used in the "developing and targeting on Windows x64" example discussed earlier.

There are problems with this model:

- Using the x64 SDK will be a substandard experience, primarily due to it not being in the `PATH`. On macOS, you can either type `/usr/local/share/dotnet/x64/dotnet`, prepend that directory to the `PATH`, create a shell alias, or create a symbolic link (in `/usr/local/bin`). Some of those same options exist on Windows.
- Users will find it confusing and unpleasant to need to pivot between the x64 and Arm64 SDKs as a means of targeting a .NET version (short-term problem) or RID (long-term problem).
- This form of targeting will be untenable for many open source projects. Our GitHub repos don't have this problem because we always download the correct SDK via our build and test scripts. Assumption: Many other projects don't.

IDEs would need to do this same pivoting. That means that they would need to install and update SDKs for two different RIDs and provide an experience for the user to switch between those SDKs, which might be the next option.

### Rely on explicit RID targeting in the user project file

The .NET SDK can build for multiple TFMs and RIDs. Starting with .NET 6, it can also cross-compile for different architectures, for compiling IL to native code (crossgen2).

The premise of this option is that developers use the native architecture SDK and rely on its capability to produce compatible assets for the emulated architecture.

Currently, the best experience for explicit RID targeting is declaring the target (and singular) RID in the project file. There are other options but they don't work well.

The following project file uses explicit RID targeting, and maintains the framework-dependent default for .NET apps.

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net5.0</TargetFramework>
    <RuntimeIdentifier>osx-x64</RuntimeIdentifier>
    <SelfContained>false</SelfContained>
    <UseAppHost>true</UseAppHost>
  </PropertyGroup>

</Project>
```

The `UseAppHost` property isn't strictly needed, but is a topic we need to resolve for macOS. It is included solely to spur conversation and ensure we don't forget a significant issue.

The upside of this plan is that this model is supported today and doesn't require any work (beyond resolving the apphost challenges on macOS). The downside is that is regression in experience. It has the following problems:

- Projects files get longer and more complicated, for users that need (or are required) to use x64 emulation.
- Project files are no longer portable across machines. This would be a deal killer for teams (or open source projects) that has users with multiple machine types. For example, dotnet org repos would never be able to implement this technique.

### Rely on implicit RID defaults

There are two quite different user scenarios to satisfy, which don't require the same solution. The first is targeting a .NET version (like .NET Core 3.1) that is only available for one architecture for a given operating system and the second is targeting a specific architecture for testing purposes for a .NET version that is available for multiple architectures (like .NET 6). The former is a short-term problem and the latter is not.

We already have a concept of the SDK having an implicit RID for the SDK. We can pivot that by .NET version.

The following is an example of implicit RIDs we would use on macOS:

- .NET 6: osx-arm64
- .NET 5: osx-x64
- .NET Core 3.1: osx-x64

This approach would enable developers to use the Arm64 SDK without needing any special gestures. For example `dotnet run` of a .NET 5 app using the Arm64 SDK would result in running a .NET 5 app with the x64 runtime (assuming the .NET 5 x64 runtime was installed).

This experience would also enable us to provide better error messages for users if they try to run or test a .NET 5 app with the .NET 6 SDK.

The upside of this option is that it provides automatic behaviors. There are two major downsides:

- It would need to respect `DOTNET_ROLL_FORWARD` or provide some other opt-out. The lack of that could be a deal killer, particularly for global tools.
- This model only works for old versions, not to .NET 6. In reality, this option is almost entirely .NET Core 3.1 specific (since .NET 5 will go EOL so soon after .NET 6 is released).

## Provide a new model for RID targeting

There are multiple problems with RID-targeting today that make it inconvenient and confusing.

- RID-specific apps are self-contained by default, which breaks multiple experiences.
- You can specify a RID for `dotnet build` but you cannot specify that you want to maintain the framework-dependent nature of the app with another (native) CLI argument. You can use an MSBuild property for this case `/p:SelfContained=false`. That's really terrible UX.
- RIDs are these special codes that are hard to remember (particularly `osx`). In the case of x64 emulation, you only want to pivot on architecture, not operating system. We should provide an easy mode to enables specifying an architecture without an OS.
- An explicit RID and an implicit one are not symmetric. They are the same as it relates to apphost generation, however, the implict RID defaults to portable apps and the explicit RID defaults to architecture-specific apps. This behavior will become increasingly obvious.
- `dotnet publish` of a RID-specific app produces two copies of the final app, and it isn't clear which one to use.

We haven't defined a new model. In short, it would need to resolve the problems with RID targeting that we have today.

Assuming we had a new model, it would have these general characteristics:

- Users can pivot between architecture or RIDs on the command-line for all the relevant .NET verbs (like `dotnet test`) while maintaining the framework-dependent nature of their app.
- You can specify just architecture as an easy mode, when that's relevant.
- Pivoting by architecture doesn't rewrite builds in your bin folder.
- Incremental build works.
- Roll forward participates in RID selection. It is easy to coerce apps to roll forward (to enable using the native architecture), particularly for `dotnet tool install`.

Note: these changes may or not be breaking. There are both breaking and non-breaking options to satisfy these characteristics.

## Which .NET Versions are supported with x64 emulation

Goal: Support the .NET versions that developers expect to use on Arm64, particularly if an Apple Silicon Mac or Surface Pro X is their only development device.

We've just spent a lot of effort getting .NET Core 3.1 and .NET Core 3.1 to work on Apple Silicon, with Apple. Of course they are supported! Also, it would be a major regression to remove .NET Core 3.1 and .NET 5 targeting from Visual Studio for Mac users on Apple Silicon.

On the other hand, the change to install location for .NET x64 builds is very disruptive, and we'd prefer not to pay it for all versions.

Zooming out, all in-support versions are supported for x64 emulation. It's easiest to consider that for self-contained apps. A self-contained .NET Core 3.1 app built for `osx-x64` is supported on Apple Silicon machines.

The big question is how to manage global installs, particularly for the developer desktop. That's where the disruptive change, of requiring .NET x64 builds to install in an `x64` directory, is relevant.

We cannot make an informed decision until we understand what we're doing for RID targeting, discussed earlier. The decisions are co-dependent.

We can put some stakes in the ground (some hard, some soft):

- We need x64 runtime installers.
- We need x64 .NET Core 3.1 runtime installers for sure.
- We can likely get away without updating/supporting x64 .NET 5 runtime installers given the proximity of .NET EOL to .NET 6 RTM.
- ASP.NET Core does not have a macOS runtime installer. We may need one, dependent on our plan for the x64 .NET SDK.
- We may not need to update/support x64 SDK installers, dependent on whether decisions on RID targeting mean the SDK must match the architecture or the native SDK can target either Arm64 or x64.

## Proposals

As suggested, multiple of the options are co-dependent. The following section describes three options with varying UX and cost. There is some opportunity for mix and match between these options.

### Option 0: Do nothing

This option articulates the no cost option.

- **RID UX:** Rely on the implicit rid of the SDK
- **SDK guidance:** Use the .NET 6 SDK (x64 or Arm64) that matches the process type you want to start. You can only have one architecture installed at once.
- **Supported x64 installers (for coexistence):**
  - None
- **Unsupported installers (for coexistence)**
  -  All x64 installers

Note: Every time you switch between Arm64 and x64, you need to uninstall .NET. On macOS, you have to `rm -rf` the `dotnet` directory.

Note: This back-and-forth option would be untenable for VS for Mac. With this option, VS for Mac would either have to support x64 only or .NET 6+ only (as native architecture). The same thing applies to VS for Windows should it be supported (as an x64 app) on Windows Arm64.

Note: Alternatively IDEs could support Arm64 with a global install and x64 to an alternative location (admin or user space). That's likely not tenable either. That means that .NET users and the IDE might install to different locations. As Microsoft (or DevDiv), if we need to durable location for .NET, then we need to define it.

### Option 1: UX insensitive; cost sensitive

This option is intended as the lowest cost option. It's the MVP for x64 emulation support.

- **RID UX:** Rely on the implicit rid of the SDK
- **SDK guidance:** Use the .NET 6 SDK (x64 or Arm64) that matches the process type you want to start.
- **Supported x64 installers (for coexistence):**
  - x64 .NET 6 SDK
  - x64 .NET 6 runtimes
  - x64 .NET Core 3.1 runtimes
  - Install ASP.NET Core 3.1 ASP.NET Core via tar.gz on macOS.
- **Unsupported installers (for coexistence)**
  -  Pre .NET 6 x64 SDKs

### Option 2: Balanced between UX and cost

This option is intended as a balance between UX and cost, and also intending to do the minimum work now while retaining the option to do more later.

- **RID UX:** Rely on implicit RID defaults, per .NET version.
- **SDK guidance:** Use the Arm64 .NET 6 SDK, by default. Fallback to x64, as needed.
- **Supported x64 installers (for coexistence):**
  - x64 .NET 6 SDK
  - x64 .NET 6 runtimes
  - x64 .NET Core 3.1 runtimes
  - New x64 .NET 3.1+ ASP.NET Core runtimes for macOS
- **Unsupported x64 installers (for coexistence)**
  -  Pre .NET 6 x64 SDKs

Note: If we provide new x64 macOS installers, we should consider doing same for Arm64.

Note: One oddity of this plan is that it is easier to use x64 emulation with .NET Core 3.1 than .NET 6.

### Option 3: UX sensitive; cost insensitive

This option is intended as the most user friendly option. It's the best experience we could imagine providing. It is expected to be the most expensive and least defined (at least currently).

- **RID UX:** New model for RID targeting
- **SDK guidance:** Always use the .NET 6+ Arm64 SDK
- **Supported x64 installers (for coexistence):**
  - x64 .NET 6 runtimes
  - x64 .NET 5 runtimes
  - x64 .NET Core 3.1 runtimes
  - [New] x64 .NET 3.1+ ASP.NET Core runtimes for macOS
- **Unsupported x64 installers (for coexistence)**
  -  x64 .NET SDKs

Note: If we provide new x64 macOS installers, we should consider doing same for Arm64.

## Recommendation

As expected, the UX of these options gets considerably worse, with the options (in order).

Option 1 would be a very hard to deliver as-is. The experience of using [.NET global tools](https://github.com/dotnet/sdk/issues/17241) is the worst-case experience but is generally descriptive of the UX of relying on the implicit RID targeting of the SDK for x64-only .NET versions.

Option 2 is tenable. It's primary challenge is that it is a nuanced behavior, and works best for the oldest supported runtime, not the newest one. The requirement of making the implicit RID sensitive to .NET version (including for global tools) is uncosted.

Option 3 is the architecturally sound option with the best UX. It provides more uniform capabilities across all .NET versions. It is also uncosted.

The cost delta between options 2 and 3 is unknown. It's quite likely that we'd decide that option 2 is a subset of option 3.

Actual Plan:

- Commit to delivering option 1.
- Cost and design options 2 and 3.
- Determine if we fund options 2 and 3 in .NET 6.

We decided that option 3 will result in CLI breaking changes. While option 2 could theoretically be delivered between .NET 6 and .NET 7 with a .NET SDK update (like `6.0.200`), option 3 can only be delivered with a major release. It is also fair to note that the breaking changes associated with option 3 are not dependent on option 2. We could in theory make the breaking changes for option 3 now, enabling the remaining work to be done either before .NET 6 or in a .NET SDK update.

Note: These plans assume that Visual Studio (Dev17) will not be supported on Windows Arm64 (with x64 emulation) before .NET 7. They also assume that Visual Studio for Mac will adapt to this plan, aligned with .NET 6. If those are not true, then we need to re-assess this plan.
