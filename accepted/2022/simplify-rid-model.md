# Simplify RID Model

Date: April 2022

[Runtime IDs (RIDs)](https://docs.microsoft.com/dotnet/core/rid-catalog) are one of the most challenging features of the product. They are necessary, impact many product scenarios, and have very obscure and challenging UX. They also equally affect development and runtime and sometimes prevent .NET from running on operating systems that Microsoft doesn't support. This document proposes a plan to improve some aspects of the way RIDs work, particularly for scenarios that we've found to be insufficient for our needs.

## Context

RIDs are (for the most part) modelled as a graph of [target triplet](https://wiki.osdev.org/Target_Triplet) symbols that describe legal combinations of operating system, chip architecture, and C-runtime, including an extensive fallback scheme. This graph is codified in [`runtime.json`](https://github.com/dotnet/runtime/blob/main/src/libraries/Microsoft.NETCore.Platforms/src/runtime.json), which is describes the RID catalog. It is a massive (database in a) file. That's a lot of data to reason about, hard to edit correctly, and a bear to maintain.

Examples of triplets today are:

- `win-x86`
- `macos-arm64`
- `linux-arm64`
- `linux-x64-musl`

Note: A larger set of triplets are included near the end of the document.

The first three examples may seem like doubles and not triplets. For those examples, the C-runtime is assumed, implicitly completing the triplet. For Windows and macOS, the OS-provided C-runtime is always used. For Linux, the OS-provided C-runtime is also used, but there isn't a single answer across all distros. Most distributions use [glibc](https://www.gnu.org/software/libc/), while others use [musl](https://musl.libc.org/). For Linux doubles, glibc is the implicit C-runtime, which completes the triplet.

Native code compilers will generate different code for each triplet, and triplets are incompatible with one another. That's not a .NET concept or design-point, but a reality of modern computing.

The `runtimes.json` file is used for asset description, production, and selection. Code can be conditionally compiled in terms of RIDs, and NuGet packages may contain RID-specific code (often native dependencies). At restore-time, assets may be restored in terms of a specific RID. [SkiaSharp](https://nuget.info/packages/SkiaSharp/) is a good example of a package that contains RID-specific assets, in the `runtimes` folder. Each folder within `runtimes` is named as the appropriate RID. At runtime, the host must select the best-match assets from those same `runtime` folders if present (although no longer within a package).

RIDs can be thought of as making statements about native code, specifically:

- I offer code of this RID.
- I need code of this RID.

Often times, the RIDs being offered and asked for do not match (in terms of the actual string) but are compatible. The role of `runtimes.json` is determining whether two RIDs are compatible (and best-match). That's done via a graph walk of the nodes in that file.

The current system is unnecessarily complex, expensive to maintain, makes it difficult to manage community needs, and costly/fragile at runtime. More simply, it is useful, but overshoots the needs we actually have.

Note: RIDs are almost entirely oriented around calling native code. However, the domain is broader, more generally oriented on native interop and calling [Foreign Function Interfaces (FFI)](https://en.wikipedia.org/wiki/Foreign_function_interface), including matching the environment. For example, we'd still likely need to use RIDs for interacting with another managed platform like Java, possibly with the use of native code or not.

Note: RIDs are a close cousin to Target Framework Monikers (TFMs), however have less polished UX and don't describe the same concept (although they overlap).

Note: This topic area applies to other development platform, and is typically a domain of more challenging UX. Whether .NET is better or worse off than other development platforms for native code targeting is hard to say.

- https://peps.python.org/pep-0600/
- https://peps.python.org/pep-0656/
- https://doc.rust-lang.org/rustc/platform-support.html

## Biggest problems

The following are the biggest problems we see:

- We get requests to add new RIDs ([Asianux Server 8](https://github.com/dotnet/runtime/issues/2129), [CBL-Mariner](https://github.com/dotnet/runtime/issues/65566), [Anolis Linux](https://github.com/dotnet/runtime/pull/66132)), with no end in sight.
- Existing RID authoring sometimes [doesn't work with new OS releases](https://github.com/dotnet/runtime/issues/65152).
- Reasoning about [portable vs non-portable Linux RIDs](https://github.com/dotnet/runtime/pull/62942).
- We have to read and process `runtime.json` at application startup, which has a (unmeasured) performance impact.
- The fact that the RID graph is so large (and continuing to grow) demonstrates that we chose a poor design point.

## General approach

- Ensure `runtimes.json` is correct.
- Freeze `runtimes.json`.
- Continue to use `runtimes.json` for all NuGet scenarios.
- Disable using `runtimes.json` for all host scenarios, by default (starting with a TBD .NET version).
- Enable a host compatibility mode that uses `runtimes.json` via MSBuild property (which writes to), `runtimeconfig.json`, and a host CLI argument.
- Implement a new algorithmic host scheme for RID selection, enabled by default.

The new scheme will be based on the following:

- Each host is built with a hard-coded set of RIDs that it probes for, both triplets and singles.
- Each host is already built for a specific RID triple, so this change is an evolutionary change.
- There are processor-agnostic managed-code scenarios where a RID single is relevant, like Windows-only code (for example, accessing the registry) that works the same x64, Arm64 and x86. The same is true for macOS and Linux.
- The RID that `dotnet --info` returns will always match the first in this hard-coded set of RIDs.

Let's assume that an app is running on Alpine 3.16, the host will probe for the following RIDs:

- `linux-musl-x64`
- `unix`

Ubuntu would be similar:

- `linux-arm64`
- `unix`

macOS would be similar:

- `osx-arm64`
- `unix`

Windows would similar:

- `win-x86`
- `win`

Note: `win7` and `win10` also exist (as processor-specific and -agnostic). Ideally, we don't have to support those in the host-specific scheme, but we need to do some research on that.

Note: A mix of processor-specific RIDs are used above, purely to demonstrate the range of processor-types supported. Each operating system supports a variety of processor-types, and this is codified in their associated RIDs.

More generally, the host will probe for:

- This environment, RID triplet (OS-CRuntime-arch)
- This environment, RID single (OS-only).

Note: The abstract `unix` RID is used for macOS and Linux, instead of a concrete RID for those OSes.

The host will implement "first one wins" probing logic for selecting a RID-specific asset.

This behavior only applies for portable apps. RID-specific apps will not probe for RID-specific assets.

There are other RIDs in `runtimes.json` today. This new scheme will not support those. The `runtimes.json` host compat feature will need to be used to support those, which is likely very uncommon. Even if they are quasi-common, we will likely still make this change and require some combination of the ecosystem to adapt and app developers to opt-in to the `runtimes.json` compat mode.

The host and `runtimes.json` must remain compatible. The RIDs known by the host must be a subset of `runtimes.json` RIDs. We may need to update `runtimes.json` to ensure that the RIDs known by the host are present in that file.


## Minimum CRT/libc version

This scheme doesn't explicitly define a CRT/libc version. Instead, we will define a minimum CRT/libc version, per major .NET version. That minimum CRT/libc version will form a contract and should be documented, per .NET version. Practically, it defines the oldest OS version that you can run an app on, for a given .NET version.

For .NET 7:

- For Windows, .NET will target the Windows 10 CRT.
- For Linux with glibc, .NET will target CentOS 7.
- For Linux with musl, .NET will target the oldest supported Alpine version.

For .NET 8, we'll continue with the model. However, we will no longer be able to use [CentOS 7 (due to EOL)](https://wiki.centos.org/About/Product), but will need to adopt another distro that provides us with an old enough glibc version such that we can offer broad compatibility.

For .NET 9, we'll likely drop support for Windows 10 and instead target the Windows 11 CRT.

As part of investigating this topic, we noticed the [libc compatibility model that TensorFlow uses](https://github.com/tensorflow/tensorflow/blob/f3963e82f21c9d094503568699877655f9dac508/tensorflow/tools/ci_build/devtoolset/build_devtoolset.sh#L48-L57). That model enables the use of a modern OS with an artificially old libc. We also saw that Python folks are doing something similar with their [`manylinux` approach but with docker](https://github.com/pypa/manylinux).

This in turn made us realize that our [`build-tools-prereqs` Docker images](https://github.com/dotnet/dotnet-buildtools-prereqs-docker/blob/main/src/centos/7/Dockerfile) are very similar to the Python approach. We don't need a new contract like `manylinux2014` since we can rely on the contract changing with each major .NET version. We need augment our build-tools-prereq scheme to also include an old glibc version, once CentOS 7 is EOL.

The biggest difference with our `build-tools-prereqs` images is that they are not intended for others to use, while the `manylinux` images are intended as a general community artifact. There are two primary cases where extending the use of the `build-tools-prereqs` images would be useful: enabling others to produce a .NET build with the same compatibility reach, and enabling NuGet authors to build native dependencies with matching compatibility as the underlying runtime. Addressing that topic is outside the scope of this document. This context is included to help frame how the TensorFlow, Python, and .NET solutions compare, and to inspire future conversation. 

Note: It appears that some other folks have been [reacting to CentOS 7 not being a viable compilation target](https://gist.github.com/wagenet/35adca1a032cec2999d47b6c40aa45b1) for much longer.

## Legal RIDs

RIDs are defined by legal combinations of operating system, chip architecture, and C-runtime. We support both triplets (primarily native code) and singles (only processor-agnostic managed code).

A given RID is used to describe:

- This system supports this RID, per the .NET host.
- This code will run on / requires a system that supports this RID, per the author.

The following table describes RIDs supported by the .NET host. It is exhaustive, per the hosts provided by Microsoft.

| RID        | Description of scope |
|------------|----------------------|
| unix       | All Unix-based OSes (macOS and Linux), versions, and architecture builds. |
| win        | All Windows versions and architecture builds. |
| linux-arm64| All Linux glibc-based distros, for Arm64. |
| linux-x64  | All Linux glibc-based distros, for x64. |
| linux-x86  | All Linux glibc-based distros, x86. |
| linux-musl-arm64| All Linux musl-based distros, for Arm64. |
| linux-musl-x64  | All Linux musl-based distros, for x64. |
| osx-arm64  | All macOS versions, for Arm64.
| osx-x64    | All macOS versions, for x64.
| win-arm64  | All Windows versions, for Arm64. |
| win-x64    | All Windows versions, for x64. |
| win-x86    | All Windows versions, for x86. |

Note: Singles are for processor-agnostic managed code.

Note: All RIDs are subject to .NET support policy. For example, .NET 7 doesn't support Ubuntu 16.04. The `linux-x64` RID doesn't include that specificity.

Note: `osx` is used instead of `macOS` within the RID scheme. This design change may be a good opportunity to introduce `macOS`. It probably makes sense only to do that for Arm64.

The following table describes RIDs supported only by `runtimes.json`. It is not exhaustive.

| RID        | Description of scope |
|------------|----------------------|
| any        | The root RID, which is compatible with all other RIDs. |
| alpine     | All Alpine versions and architecture builds. |
| alpine-x64 | All Alpine versions, for x64. Other processor-types are also supported. |
| alpine3.16-arm64 | Alpine 3.16, for Arm64. Other processor-types are also supported.|
| centos     | CentOS has a similar scheme as Alpine. |
| debian     | Debian has a similar scheme as Alpine. |
| osx        | macOS has a similar scheme as Alpine. |
| rhel       | Red Hat Enterprise Linux has a similar scheme as Alpine. |
| tizen      | Tizen has a similar scheme as Alpine. |
| ubuntu     | Ubuntu has a similar scheme as Alpine. |

Note: Many other Linux distros are represented in `runtimes.json`.

Note: `runtimes.json` will be frozen, which means that RID schemes only supported by this file are abandoned. For example, RIDs that include OS versions will not be updated going forward.

## Source-build

A major design tenet of the RID system is maximizing portability of code. This is particularly true for Linux. That makes sense from the perspective of Microsoft wanting to make one build of .NET available across many Linux distros, separately for both glibc and musl. It makes less sense for distros themselves building .NET from source and publishing binaries to a distro package feed.

We sometimes refer to `linux-x64` (and equally to `linux-arm64` and `linux-x86`) as the "portable Linux RID". As mentioned in the libc section, this RID establishes a wide glibc compatibility range. In addition, the RID also establishes broad compatibility across distros (and their associated versions) for .NET dependencies, like OpenSSL and ICU. The way this particular form of compatibility shows up is observable but is nuanced (and isn't explained here).

We'll use Red Hat as an example of an organization that builds .NET from source, to complete this discussion. They have raised concerns on [source-build not playing nicely with `linux-x64`](https://github.com/dotnet/runtime/pull/62942#issuecomment-1056942235).

For Red Hat, it makes sense to accept and support `linux-x64` assets, but not to produce or offer them. Instead, Red Hat would want to produce `rhel-x64` assets. It's easiest to describe this design-point in terms of concrete scenarios.

**NuGet packages** -- NuGet authors will typically value breadth and efficiency. For example, a package like `SkiaSharp` might target `linux-x64`, but not `rhel-x64`. There is no technical reason for that package to include distro-specific assets. Also, if the author produced a `rhel-x64` asset, they would need to consider producing an `ubuntu-x64` and other similar assets for other distros, and that's not scalable. As a result, it makes sense for Red Hat to support `linux-x64`. There is no technical downside to Red Hat doing so.

**Runtime and host packs** -- The .NET SDK doesn't include all assets that are required for all scenarios, but instead relies on many assets being available on a NuGet feed (nuget.org or otherwise). That's a good design point for a variety of reasons. Runtime packs are a concrete scenario, which are used for building self-contained apps. Red Hat should be able to (A) build RHEL-specific runtime packages, (B) publish them to a feed of their choice, and (C) enable their users to download them by specifying a RHEL-specific RID as part of a typical .NET CLI workflow. RHEL-specific runtime packs would not be compatible in the same way as `linux-x64`. They would support `linux-x64` NuGet assets, but would only (presumably) support being run on Red Hat Enterprise Linux family OSes. For example, the RHEL-specific runtime for RHEL 8 would only support the default OpenSSL version that is provided in the RHEL 8 package feed.

**Host RID support** -- The Red Hat provided host would need to probe for assets that might be included in the app, much like was described earlier. It would use the following scheme, here specific to x64.

- `rhel.8-x64`
- `linux-x64`
- `unix`

A source-built RID (here `rhel.8-x64`) will typically be distro-specific and versioned. Red Hat would naturally want to build .NET specifically and separately for RHEL versions, like RHEL7 and RHEL 8, since the packages available in each of their versioned Red Hat package feeds will be different. There might be other differences to account for as well. An unversioned RID (like `rhel-x64`) would not enable that.

This means that we'll have two forms of RIDs:

- A basic, default, form that is distro-agnostic and unversioned, like `linux-x64`, oriented on broad compatibility.
- A specific, source-build, form, that is distro-specific and versioned, like `rhel.8-x64`, oriented on distro version specialization (and matching distro practices).

Note: The source-build term is a poor term, used in this way. In the fullness of time, we want to move all builds to source-build, at which point this terminology would fail to make any sense. For now, we'll continue to use the term to mean "not Microsoft's build".

The use of `rhel.8-x64` in the host RID list is a bit odd. That list is intended for NuGet asset probing, but (as described earlier), we don't expect there to be distro-specific NuGet packages. Distro-specific RIDs are primarily intended for restoring various packs. We may find that restoring RID-specific pack and NuGet package assets should be separated, in terms of how the RIDs are specified. For now, the doc will be left as-is, and assumes that one list controls both scenarios.

Consider these two commands:

```bash
`dotnet build --self-contained`
`dotnet build --self-contained -r rhel.8-x64`
```

On a RHEL 8 machine (using a RH-provided .NET), those commands are intended to be equivalent, and to produce a RHEL 8 specific .NET app.

However, for that to work, we would need to add `rhel.8-x64` to `runtimes.json`. Otherwise, NuGet won't be able to realize that `rhel.8-x64` should be treated as `linux-x64` for restoring NuGet assets. However, we want to freeze `runtimes.json`. That topic is left as an unresolved issue in this spec, at least for now.

There are a few gaps that need to be resolved before we can deliver on this model, such as:

- The various packs have a pre-defined naming scheme, including the string "Microsoft". That may or may not be OK.
- Packs are assumed to be on NuGet.org and can (currently) only be published by Microsoft.
- Packs published to other feeds may require the use of [Package Source Mapping](https://docs.microsoft.com/nuget/consume-packages/package-source-mapping), which may be challenging and would likely need support in the .NET CLI.
- All of these scenarios are currently NuGet-oriented. There may be cases where it makes sense to deploy packs via a package manager feed, but to still enable the typical associated workflows.

We need to work through these and other topics with Red Hat and other source-build users.

## Related topics

The RID topic is quite broad. This proposal is intended to simplify an important aspect of RID handling in the product. There are others to consider that are outside of the scope of the proposal. They are listed for interest and to inspire further improvements.

- [Switch to building RID-specific apps by default](https://github.com/dotnet/sdk/issues/23540).
- Enable multi-pass builds where multiple RIDs are specified, much like multi-targeting for TFMs.
- Enable build RID-specific packages as a first-class .NET CLI experience.
- Enable building RID-split packages (for example a `SkiSharp` parent package with RID-specific dependencies).
- Good experience for using RIDs with `dotnet restore`.
