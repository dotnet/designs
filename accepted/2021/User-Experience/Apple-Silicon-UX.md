# Apple Silicon User Experience

Apple has [announced plans to transition its MacOS hardware line]( https://www.apple.com/newsroom/2020/06/apple-announces-mac-transition-to-apple-silicon/) to a new Arm64-based chip that they refer to as “Apple Silicon”.

The transition has a few impacts on the .NET user experience.

## Background

### Apple Multiple Architecture Support

#### Rosetta 2 x64 Emulation

As part of the their transition plan, Apple created the Rosetta 2 `x86_64` emulator to run `x64` code on an Apple Silicon processor. It is intended to support most `x64` code on Apple Silicon without modification.

Usage is generally transparent to the user. When creating a new process, the current process architectural affinity is used to select the best architecture to use for the new process.

The user also has explicit architecture control through the use of the `arch` utility. The user can select a specific architecture when executing a command from the command line i.e. `arch -arch x86_64 <command>`.

While Rosetta 2 is supported for running `x64` code on Apple Silicon, the reverse emulator is not currently shipping.

#### Universal Binaries

For macOS, universal are the default approach. Most binaries are multi-arch. For single arch binaries the kernel can detect and automatically switch the process to run in the correct architecture, either native or Rosetta 2 emulation of x64.

A universal binary can be created simply by combining two or more architecture specific binaries into a universal binary using the `lipo` tool. The process is reversible too. A universal binary can be `thinned` to extract a single arch binaries.

Given both architectures, universal binaries are relatively easy to create. The general syntax would be `lipo <binaryArchA> <binaryArchB> -create -output <binaryUniversal>`

#### Apple Store requires universal binaries

I have heard that publishing native Apple Silicon (or Apps supporting multiple architectures) apps to the Apple store will require them to be published as Universal binaries. I have not been able to find public documentation of this, so the details are not clear.

This document is assumes this requirement is reasonably accurate.

### Security Enhancements

#### Write No Execute

Another major change with the Apple Silicon is the transition to a new prohibition of pages simultaneously marker writable and executable. A new API was introduced for JIT usage to allow a single thread to switch a page from executable to writable or vice-versa.

This requirement currently only applies when executing as Apple Silicon native code.

This is not expected to impact our customers as it needs to be handled automatically by our run-times.

#### Tightened Signing Requirements

When running on Apple Silicon unsigned binaries will get killed automatically by the kernel. A simple non-obvious message will be printed on the console (`zsh killed: helloWorld`).

To allow a native shared library to be loaded, it will also need to be signed.

When running the equivalent unsigned binary from a Rosetta 2 emulated process the process is not killed. When running a x64 process on Apple Silicon the process was not killed either.

XCode 12 adhoc signs native binaries. These anonymous signature are sufficient to prevent these from being killed (on the machine that compiled them at least.)

I haven't found the documentation exactly how and what changed. These behavior descriptions have been through experimentation and debugging.

### Microsoft One.NET Transition

We are in the middle of a migration from two distinct .NET ecosystems (Mono & .NET Core), to a shared ecosystem, One.NET.

This document was originally written form a pure CoreCLR perspective and was missing a Mono perspective. As Mono feedback is received it will be incorporate to make this a better design.

Any feedback is appreciated.

### New Architecture and .NET Back-porting

Support for a new processor architecture is a significant undertaking.  It is too large an undertaking to consider on our stable release branches.  New architecture development is occurring in .NET 6.

#### CoreCLR Runtime

We do not plan to back-port Apple Silicon support to the current CoreCLR runtime releases .NET 2.1, .NET 3.1, and .NET 5.0.

We have tested the .NET 5 release with Rosetta 2.  Apple has resolved the majority of issues we reported to them.  This means that CoreCLR .NET 2.1 LTS, .NET 3.1 LTS and .NET 5 will mostly just work on Apple Silicon.

## .NET 6 Preview1 CoreCLR User Experience Issues

### Mixed Architecture Side By Side Installs Fail

While the customer can install the macOS Apple Silicon .NET preview build by itself, install this beside the macOS x64 builds is problematic.

We have several possible representative scenarios. Two typical ones are described:

- .NET 3.1 x64 with .NET 6 Apple Silicon. As side effect of not back-porting older releases to the new architecture is that customer will need to deal with multi-architecture side by side installs at least until .NET 3.1 goes out of support. However, this mixed forced architecture problem is transient in nature.

- .NET 6 x64 with .NET 6 Apple Silicon. This scenario is not absolutely critical, but may be required by some customers.
  - It enables moving legacy Apps with x64 dependent apps to .NET 6.
  - It enables testing macOS x64 apps under the Rosetta 2 emulator.

For .NET 6 Preview1 these Side By Side mixed architecture scenarios simply don't work.

#### Installer Path is Hardcoded and Uses Same path

One key issue is that all runtime installs use the same path. The users is not given the option of choosing an alternative path (except a different hard drive.)

#### Shared dotnet muxer

The `dotnet` muxer is a shared component. Effectively every side-by-side install was designed to share the same muxer. The muxer's job was to run the SDK command or user application. It is intended to be the entry point to all versions of the SDK and the runtime.

This `dotnet` command line architecture makes it difficult to support side-by-side mixed architecture CoreCLR installs. The dotnet tool is responsible for starting the newest version of the `hostfxr` library to resolve the runtime version to use. Until the runtime version is selected it is not clear which architecture the runtime will require.

For instance, the `dotnet --info` command must resolve the currently selected SDK, by finding the `global.json` or the most recent SDK. Then it must find the best runtime as requested by that SDK. Once that is determined the runtime required architecture could be known.

The current installers are installing a single architecture version of `dotnet`. The process will fail when either

- `dotnet` and the newest `hostfxr` are different machine architectures.
- `dotnet`/`hostfxr` and the selected CoreCLR runtime's `hostpolicy` are different architectures.

#### Current Workaround

The best known workaround is for the customer to install the runtimes/SDK for a single architecture only to the default path.

If the customer needs the second architecture. They must rename the patch to the first architecture, before installing the second architecture.

For instance:

`sudo mv $(dirname $(which dotnet)) $(dirname $(which dotnet))-x64`

Which version to be used must be controlled by the path environment variable.

#### Design Constraints

- This mixed forced architecture problem is transient in nature.

- We want to avoid making major architectural changes to support this temporary design problem.

- We want to keep the process parent-child relationships as consistent as possible.

- We want minimal changes to the earlier releases.

#### Design Experiment

https://github.com/dotnet/runtime/pull/48541 represented an early design experiment to allow side-by-side macOS-x64 .NET 5 with macOS-arm64 .NET 6 to coexist.

The experiment:

- Assumed .NET runtime versions less than 6.0 will always require `x86_64` architecture. All other runtime versions will require `arm64` architecture on Apple Silicon.

- Made the .NET 6 `dotnet` & `libhostfxr.dylib` (`hostfxr`) universal binaries. This means both native and emulated processes can perform runtime resolution to identify the correct runtime to use.

- The `hostfxr` was modified to enforce the architecture version  policy above. It will return an architecture mismatch error when the runtime (by version policy above) require a different architecture than the currently running process.

- `dotnet` was modified to enable switching the architecture of the current process when it saw an architecture mismatch error returned by `hostfxr`. The current process will switch architecture as needed and run with the same arguments as the new process. The switching will use `posix_spawn` with changes to `posix_spawnattr_t` to set `POSIX_SPAWN_SETEXEC` and preferred architecture via `posix_spawnattr_setbinpref_np` (see Apple Silicon man page) and setting `sysctl` `kern.curproc_arch_affinity` property appropriately.

Overall the experiment demonstrated a highly workable user experience.

- The experiment represents a viable design alternative. It did not support side-by-side install of multiple architectures of the same runtime version.

- The implementation was incomplete. It did not automate the build process or deal with installer issues

### Dotnet Publish Experience Inconsistent

#### Signing

- dotnet publish for Xamarin workloads handles signing automatically.
- dotnet publish for CoreClr console apps do not handle signing automatically. The developer would need to explicitly sign after publishing for Apple Silicon.  The signing command is relatively simple `codesign -s <signature> --entitlements <app-entitlements-path> <app-path>`.
- There is no message to the customer that the published app needs to be signed

#### Dotnet Publish as Universal Binaries

- This scenario is also supported for Xamarin iOS workloads. This is a slightly different scenario as Xamarin iOS apps are native binaries (not JIT binaries)
- CoreCLR runtime apps have no mechanism to publish universal binaries.

### Dotnet-SOS and the SOS plugin Issues

The `dotnet-sos` tool installs the `SOS` diagnostic plugin into `lldb`. Since `lldb` is a universal binary, it expects its plugins to also be universal binaries. With the single architecture design, when `SOS` is installed, `lldb` will complain about not being able to load the plugin when the architecture mismatches.

#### Universal Binary Experiment

Making the Apple Silcon SOS be a universal binary to allowed SOS to work on `x86_64` and `arm64` processes.

## User Experience Designs

### Mixed Architecture Side By Side Installs

This is controversial. This is _uncommitted/unplanned_. Ongoing discussions will likely affect this proposal.

For CoreCLR runtime, the proposal would be to:

- Make `dotnet` & `hostfxr` to be universal binaries
- Support automatic architecture selection
  - Modify `hostfxr` to assume .NET 5 and earlier require x64 architecture.
  - Modify `dotnet` to restart in the correct architecture on an architecture mismatch.
- Make the CoreCLR runtime effectively a Universal Binaries
  - Create a single distribution for both macOS architectures.
  - Make the runtime native binaries to be universal binaries
  - Modify `hostpolicy` to probe for trusted platform assemblies based on the current RID. This is designed to architecture specific ready-to-run managed assemblies. This RID specific path would be probed before probing the common any platform assembly path.
  - Modify the runtime probing to be consistent with the new `hostpolicy` probing.
- Modify the build system to build the Universal Binaries
- Modify installers
  - Modify .NET 5 installers to allow installation without breaking universal `dotnet`.
  - Modify .NET 6 installer to overwrite the x64 `dotnet`

#### Open Questions

- Would an alternative design be better? For instance:
  - Choose a simpler design
  - Use same basic universal `dotnet` and `hostfxr` ideas
  - Keep separate runtimes. Each can optionally be installed without overwriting the other
  - Move macOS .NET 6 CoreCLR runtime into a RID specific directory
  - Teach `hostfxr` about the new `RID` in the probing for `hostpolicy`
    - If the current architecture is not present treat it as a missing runtime.
  - Probably more consistent with the original scope of our .NET 6
  Apple Silicon plan.

- What unanticipated consequences will a universal runtime have?

- How will a universal runtime affect macOS publishing?

#### Dotnet Muxer Side-By-Side Design Alternatives

##### Separate install directories

For other OS architectures CoreCLR has dealt with this by using separate install directories.  The default architecture would be on the path and the IDEs would know how to select the architecture as needed.

##### Universal binaries

There are a some design choices here:

- How much runtime is universal?
  - Dotnet muxer & hosfxr (library responsible for runtime selection)
  - All runtime native binaries (starting at .NET 6)
  - All runtime native and _some_ managed binaries (starting at .NET 6)
  - All runtime native and _all_ managed binaries (starting at .NET 6)
- How does `hostfxr` we identify which runtimes support which architecture?
  - Let the kernel fatally fail
  - Fail and retry with another arch (slow, not scalable)
  - Policy heuristic
    - .NET 5 runtime = x64
    - .NET 6 runtime must be native
    - .NET 6 runtime has RID in some well defined place
    - .NET 6 runtime must be universal
  - New config variable in runtime's json config file
  - Runtime install path. Add RID to runtime install path.
  - Universal `hostpolicy` with RID aware probing paths

### Apple Silicon requires Signed Binaries

Given that the SDK needs to handle this automatically for some platforms, it seems best to handle this consistently and automatically on all platforms.

Proposal is currently _Unplanned/Uncommitted._

### Publishing Universal Binary Apps

No planned support for publishing CoreCLR universal binaries in .NET 6.

This represent the current CoreCLR runtime team plan.

Pursuing a universal CoreCLR runtime would affect this design and might allow us to at least specify a manual method to produce a universal app.

### Dotnet-SOS & SOS

SOS will be installed on Apple Silicon as a Universal Binaries.

This represents the current CoreCLR runtime diagnostics plan.
