# DotNetCliTool v3 Proposal

[DotNetCliTool v2](../2025/rid-specific-tool-packages.md) enables publishing [RID-specific tools](https://learn.microsoft.com/dotnet/core/tools/rid-specific-tools), delivered as part of the .NET 10 release. It is a game changer for publishing optimized tools, including supporting Native AOT (NAOT) tools publishing. More recently, we've talked about [expanding the scope of the `dotnet` CLI with NuGet tools](https://github.com/dotnet/sdk/issues/54333). This direction puts more emphasis on tool acquisition, tip-servicing for dotnet CLI tools, and "bring your own runtime" (BYOR). This proposal aims to make BYOR a defining principle of DotNetCliTool v3 and to simplify the E2E workflow based on that assumption.

The key take-away is that runtime resolution occurs at publish time for BYOR apps, not as part of package acquisition. That means that we don't need TFM as a key bit of currency in the design. The primary problem is that tools pointer package acts as a runtime gate when that's not needed; it is actively harmful. It's also challenging to coordinate runtime gates for BYOR apps (not needed) and `any` fallback packages (needed).

The proposal is intended as a sort of ["OCI moment"](https://opencontainers.org/) for tools: remove assumptions and complexity, increase flexibility to enable new scenarios, and adopt a format that multiple clients can read and write. The existing CLI tools infra (v1 and v2) heavily depend on the NuGet content model and its understanding of runtime compatibility. Remove that and it opens up tools packages to deploy NAOT and even Rust apps (as a hypothetical) as a first class experience.

## Problem statement

DotNetCliTool v2 couples three things that should be independent:

- **Manifest entry** — can the SDK read the tool manifest?
- **RID payload compatibility** — does a matching native payload exist for this environment?
- **Fallback compatibility** — if no RID payload matches, is the managed fallback compatible with this SDK's runtime?

## A DotNetCliTool case study: `dotnet-inspect`

The process of building [`dotnet-inspect`](https://github.com/richlander/dotnet-inspect) was a good validation and stress test of DotNetCliTool v2. It led to two feature requests: first-class experience for native tools and tools meta-packages.

`dotnet-inspect` is a `net10.0` project, which enables the use of RID-specific tools. It uses NAOT for high-value (portable and concrete) RIDs and the `any` fallback for everything else. That works well. An observation was made along the way that the runtime version choice for NAOT publish doesn't matter since NAOT apps are BYOR. That idea could enable publishing the NAOT builds as `net11.0` (in contrast to the base `net10.0` choice) and to enable new features like Runtime Async. Those ideas were put to action. Unfortunately, the deployment was messed up and initially broke the tool for .NET 10 users. A user reported ["Installation fails with latest version: 'DotnetToolSettings.xml was not found in the package'"](https://github.com/richlander/dotnet-inspect/issues/240). The problem was quickly resolved, however, it led to the observation that even with DotNetCli v2 that runtime-dependence is the overarching design assumption and that it's a sort of gravity well. That model doesn't serve our aspirations and should be softened.

More generally, the tool is published as a NuGet package with no intention (somewhat unintuitively) to ever include it in-box in the SDK. It is designed as an agent-adjacent extension of `dotnet` CLI functionality. AI agents are updated one or more times a day while the `dotnet` CLI has an update latency of six weeks, at best. That's not workable for AI-adjacent tools, which may need to be updated at any time due to undesirable interaction with agents or models, particularly after a big model update. That explains the motivation to use NuGet packages, which can be updated at any time. NuGet is our high-velocity publishing platform.

It is easy to imagine that we will end up with multiple tools on the high-velocity shipping plan. It will be important to be able to acquire and update them as a group. Doing that would also enable us to remove existing tools from the SDK while enabling reasonable compatibility by offering a single gesture to re-acquire and update those tools as a group. These tools are currently a source of unnecessary servicing cost, which helps to explain the motivation. Reducing the size of the SDK to only what matters for building apps in CI is an important future direction.

## Compatibility behavior

The `dotnet-inspect` compatibility experience deserves a deeper explanation.

The bug report above provides us with good clues. The user tells us that `0.4.0` and `0.5.2` are good packages and something in between is bad, presumably starting with `0.5.0`.

We can look at the structure of the top-level pointer package for the key versions in that range.

- [`0.4.0`](https://nuget.info/packages/dotnet-inspect/0.4.0): `tools/net10.0/any/DotnetToolSettings.xml`
- [`0.5.0`](https://nuget.info/packages/dotnet-inspect/0.5.0): `tools/net11.0/any/DotnetToolSettings.xml`
- [`0.5.2`](https://nuget.info/packages/dotnet-inspect/0.5.2): `tools/net10.0/any/DotnetToolSettings.xml`

The middle version explains why the .NET 10 user saw a gating error. They got blocked at the front door since there was no `DotnetToolSettings.xml` available for a compatible runtime.

A [side problem](https://github.com/dotnet/sdk/issues/54796) is that the reported error message is cryptic and doesn't mention which package version caused the problem:

```bash
$ dotnet tool update --global dotnet-inspect
Tool 'dotnet-inspect' failed to update due to the following:
The settings file in the tool's NuGet package is invalid: Settings file 'DotnetToolSettings.xml' was not found in the package.
Tool 'dotnet-inspect' failed to install. Contact the tool author for assistance.
```

The current `0.10.7` version provides an example of a working configuration that satisfies the dual goal of compatibility and best performance.

- [`0.10.7` pointer](https://nuget.info/packages/dotnet-inspect/0.10.7): `tools/net10.0/any/DotnetToolSettings.xml`
- [`0.10.7` `linux-x64`](https://nuget.info/packages/dotnet-inspect.linux-x64/0.10.7): `tools/any/linux-x64/dotnet-inspect` (executable)
- [`0.10.7` `any` (portable fallback)](https://nuget.info/packages/dotnet-inspect.any/0.10.7): `tools/net10.0/any/dotnet-inspect.dll` (portable app-library)

This experience is close to what we want. There is a gate for the fallback package, but not for the RID-specific ones. The pointer package gate is where the problem is. However, the modeling situation is a bit worse than it appears.

Imagine that we want to make the fallback as `net12.0` after .NET 13 ships, while keeping the pointer package effectively ungated.

Ignoring version numbers, this is the configuration we'd want in that case:

- [`0.10.7` pointer](https://nuget.info/packages/dotnet-inspect/0.10.7): `tools/net10.0/any/DotnetToolSettings.xml`
- [`0.10.7` `linux-x64`](https://nuget.info/packages/dotnet-inspect.linux-x64/0.10.7): `tools/any/linux-x64/dotnet-inspect` (executable)
- [`0.10.7` `any` (portable fallback)](https://nuget.info/packages/dotnet-inspect.any/0.10.7): `tools/net12.0/any/dotnet-inspect.dll` (portable app-library)

Note: `net10.0` as a minimum requirement is effectively the same as ungated, since DotnetCliTool v2 starts with .NET 10.

We'd want `net10.0` consumers to still be able to pass the initial gate, to get access to RID-specific BYOR packages. For users that don't match a concrete RID, then they are out of luck and must use .NET 12 SDK. It doesn't make sense to make `net12.0` the entry gate solely to match the fallback. This configuration is not convenient to configure. In fact, the model that `dotnet-inspect` is using is also not convenient.

This problem space is loosely analogous to [Minimum Supported Rust Version (MSRV)](https://doc.rust-lang.org/cargo/reference/rust-version.html). The idea is that you can install any Rust crate where MSRV >= your installed Rust version. In this model, a CLI v3 NAOT tool can be installed by any SDK that supports CLI v3. That seems like an important principle to adopt. CLIv3 becomes MS-NAOT-V.

## More experimentation

After some experimentation, it became clear that the SDK has a knob to enable the behavior we want.

The magic incantation for `any` is `SelfContained=true`. That means that we can publish the pointer package with `-p:SelfContained=true`, resulting in the manifest being located at `tools/any/any/DotnetToolSettings.xml`. This approach then leaves the fallback `any` package to be the sole package with a TFM gate. That's good and useful, if a bit awkward. In fact, [dotnet-inspect has been updated](https://github.com/richlander/dotnet-inspect/pull/574) to use this pattern, shipped first in the [0.11.0 version](https://nuget.info/packages/dotnet-inspect/0.11.0).

We could further productize this idea with a nicer more purpose-oriented gesture and call the feature done. However, the v2 design is never going to be a purpose-built native tool delivery design. There is a second feature to consider as well, which is tool bundles. As a result, the intent isn't to spend time polishing the v2 design.

## Existing design points

There are multiple valuable design points that remain highly leveraged in the new design:

- **Tools cannot have dependencies:** This is a very critical aspect of the v1 tools design. Apps are deployed complete, with the exception of the runtime in the v1 design. It means that there is no connection between tools and libraries packages that needs to be addressed at acquisition time.
- **Portable RIDs describe platforms:** RIDs are a .NET thing, but they map cleanly across developer platforms (particularly because we adopted a [conservative compatibility scheme](https://github.com/dotnet/runtime/issues/120826)). This is in contrast to TFMs, which have no general meaning outside the .NET ecosystem.
- **Packages are split by pointer and implementations:** This split is a major performance optimization and much the same thing as [OCI manifest lists](https://aws.amazon.com/blogs/containers/introducing-multi-architecture-container-images-for-amazon-ecr/). It's also an auditable structural aspect.
- **NuGet protocol enables flexible registries:** NuGet has a great story for security, availability, and deployment flexibility (obviously dependent on which registry is being used).
- **NuGet packages provide a single and securable currency for all assets:** You can have a directory of packages, push them to a registry, and it will result in the correct publishing behavior.

## Design

The primary design philosophy is to rebuild dotnet tools as if we'd had NAOT from the start and to treat it as the recommended option, making the runtime-dependent option as a first-class fallback.

Note: There is no intent to mix v1, v2, and v3 modes.

### The manifest defines configuration

The fundamental problem is *where compatibility is stated*. With v2, the file system is used to define the compatibility statement. With v3, a manifest file is used, but critically without the deep directory hierarchy. This means that the pointer package no longer acts as a compatibility gate, at least not to read the tools manifest.

A new DotNetCli spec version enables us to make substantially different choices:

- Use JSON as the format.
- Rename the file as `manifest.json`.
- Store the manifest at the top of the tools directory, at `tools/manifest.json`.

Note: There is a desire to make it easier to write standalone tools to install .NET tools packages. It has always been unfortunate that tools installation required the SDK. With BYOR tools, that situation becomes untenable. A recent effort with [dotnet-install](https://github.com/richlander/dotnet-install) went down just this path. Tools like that are part of the reason to move to JSON. Many tools already have a System.Text.Json dependency and JSON serialization works with NAOT while XML serialization doesn't.

### Pointer package

The pointer package will be much the same as the v2 spec:

- Same naming scheme (for example `dotnet-inspect`)
- Same (general) payload: manifest, README.md, LICENSE, and any other top-level files
- The key difference is that the manifest exists at `tools/manifest.json` and there is no other imposed directory structure
- No version-gating information beyond what's specified in `manifest.json`.

### RID-specific packages

RID-packages are much the same as the v2 spec:

- Same naming scheme (for example `dotnet-inspect.linux-x64`)
- Same payload, except located at `tools/[rid]`
- The key difference is that the manifest exists at `tools/manifest.json` and is not co-mingled with app files.
- No version-gating information beyond what's specified in `manifest.json`.

### Bundles

Bundles are a new concept. They are virtual packages that specify a set of other packages that should be installed and uninstalled together. They are listed in the `dotnet tool list` experience. They are described via the same pointer packages. Pointer packages must define a toolset or reference RID-specific tools, not both.

### `manifest.json` schema

Note: This isn't a schema, per se, but examples.

The pointer manifest is an *index*: a list of child package references, in the spirit of an OCI image index (a.k.a. Docker manifest list) that points at platform-specific image manifests. A RID tool's `index` selects one payload by `rid`; a toolset's `bundle` lists packages to install together. The matching RID-specific (payload) package is the equivalent of a single image manifest. The producer (`dotnet publish`) emits fully-specified entries — the consumer reads a property and has the package it needs, with no string concatenation.

**Pointer package (BYOR only):**

```json
{
  "version": 3,
  "name": "dotnet-inspect",
  "index": [
    { "rid": "linux-x64",   "id": "dotnet-inspect.linux-x64" },
    { "rid": "linux-arm64", "id": "dotnet-inspect.linux-arm64" }
  ]
}
```

BYOR apps (NAOT, self-contained, single file) do not gate on runtime version, only on DotNetCliTool v3 and RID compatibility.

**Pointer package (BYOR, with fallback):**

```json
{
  "version": 3,
  "name": "dotnet-inspect",
  "index": [
    { "rid": "linux-x64",   "id": "dotnet-inspect.linux-x64" },
    { "rid": "linux-arm64", "id": "dotnet-inspect.linux-arm64" },
    { "rid": "any", "id": "dotnet-inspect.any", "runtimeVersion": "10.0"}
  ]
}
```

The `any` RID is the addition in this example. It adds a `runtimeVersion` gate (two- or three-part; including preview syntax; no TFMs).

**RID-specific package (BYOR):**

```json
{
  "version": 3,
  "name": "dotnet-inspect",
  "descriptor": { "rid": "linux-x64",   "id": "dotnet-inspect.linux-x64" },
  "commands": [
    {"entryPoint": "dotnet-inspect"}
  ]
}
```

The `"runner": "executable"` specification is elided because `entryPoint` already implies it.

**RID-specific package (`any` fallback):**

```json
{
  "version": 3,
  "name": "dotnet-inspect",
  "descriptor": { "rid": "any", "id": "dotnet-inspect.any", "runtimeVersion": "10.0"},
  "commands": [
    {"entryPoint": "dotnet-inspect", "runner": "dotnet" }
  ]
}
```

**Pointer package -- tools bundle:**

```json
{
  "version": 3,
  "name": "dotnet-diagnostics",
  "bundle": [
    { "id": "dotnet-dump",     "version": "[9.0.661903]" },
    { "id": "dotnet-gcdump",   "version": "[9.0.661903]" },
    { "id": "dotnet-trace",    "version": "[9.0.661903]" },
    { "id": "dotnet-counters", "version": "[9.0.661903]" }
  ]
}
```

Note: The tool versions don't need to match. We'll also need to decide if floating versions are allowed or if this scheme is exclusively last-known-good (LKG). The current thinking is the latter.

### Installation

RID tools workflow:

1. **Acquire the pointer package.** any v3 installer can read it.
2. **Read the pointer manifest.** It lists RID-specific packages. Each entry must specify a RID
   and can optionally add a runtime version (two or three part version), or leave it blank
   (no runtime requirement — the native case).
3. **Select and acquire the RID-specific package.** The payload package is the
   final arbiter of compatibility. If it declares a version requirement, it is
   evaluated here in the manifest. `any` remains the (optional) fallback if the other RIDs
   don't match.
4. **Validate runtime compatibility.** The runtime version match should be just like `.runtimeconfig.json`.
5. **If no RID + runtime version matches**, installation fails.

Tool bundle workflow:

1. **Acquire the pointer package.** any v3 installer can read it.
2. **Read the pointer manifest.** It lists bundle packages.
3. **Install the bundle packages.** They must also be v3 packages. Bundle packages cannot depend on other bundles
4. **Stop installation at first failure.** If some packages are installed, leave them for the user to manually uninstall. Do not register the bundle package as installed if 0 referenced packages are installed.

### Storage

The existing `~/.dotnet/tools` directory is a NuGet content model location, very much oriented on the v1 and v2 specs. It doesn't make sense to continue to use that. Instead, we should adopt a Rust and Go inspired tools location, specifically `~/.dotnet/bin`. This is the same directory that the `dotnet-install` prototype used for all the same reasons.

In the v3, model:

- BYOR tools should be installed to `~/.dotnet/bin` in a flat structure (for example `~/.dotnet/bin/dotnet-inspect`)
- `any` fallback tools should be installed to `~/.dotnet/tools` per the existing v1/v2 scheme.

BYOR tools get a nice clean location and fallback tools can continue to use their existing location. People adopting NAOT + v3 tools support from other ecosystems will find a very familiar and pleasing system.

### Error messaging

Today's failure is mechanical and unactionable:

> Settings file 'DotnetToolSettings.xml' was not found in the package.

It describes an internal detail (a missing XML file), not the user's situation
("this tool requires a newer SDK" or "this tool does not support your platform").

v3 needs actionable messages for its new failure modes:

- no RID payload exists for the current platform;
- the only available fallback is incompatible; upgrade your runtime or SDK to x version.
- a bundle package fails to resolve.

### Design decisions

1. v3 manifests are JSON at `tools/manifest.json`, discriminated by a `"version": 3` field.
2. v3 manifests live outside the v2 `tools/{tfm}/{rid}/` content-model path; the v3 SDK owns manifest discovery independently of NuGet's `ToolsAssemblies` pattern.
3. `any` is the RID-neutral bucket or fallback. It is not reused to mean "no runtime required."
4. Tool bundles are real installed tool packages, not general NuGet dependency graphs.
5. No ref counting for child tools; a tool bundle owns its children as a group.
6. Tool bundle installation fails atomically on duplicate command names.
7. Child tool packages own their payload selection; the toolset composes tools, not assets.

## H2H comparisons

We can compare the existing tools design to Rust and Go. This helps to contextualize the design pattern that the v3 design is attempting to match.

### .NET Tools

Here's a quick demo of tools v1 and v2:

```bash
$ dotnet tool install -g dotnetsay #v1 tool
$ dotnet tool install -g dotnet-inspect # v2 tool
$ ls -l $(which dotnet-inspect)
lrwxr-xr-x@ 1 rich  staff  95 Jun 16 21:54 /Users/rich/.dotnet/tools/dotnet-inspect -> .store/dotnet-inspect/0.10.7/dotnet-inspect.osx-arm64/0.10.7/tools/any/osx-arm64/dotnet-inspect
```

A v2 RID-specific tool like `dotnet-inspect` is a symlink, so `ls -l` reveals its implementation path directly. That's a very deep hierarchy and makes .NET native tools look like a second-class citizen and after-thought.

A v1 managed tool like `dotnetsay` is an apphost shim (a real launcher binary, not a symlink), so the path is embedded inside it instead. `strings` recovers it:

```bash
$ ls -l $(which dotnetsay)
-rwxr-xr-x@ 1 rich  staff  124714 Jun 16 21:54 /Users/rich/.dotnet/tools/dotnetsay
$ strings $(which dotnetsay) | grep .store
.store/dotnetsay/3.0.3/dotnetsay/3.0.3/tools/net8.0/any/dotnetsay.dll
```

Same story, different mechanism.

### Go and Rust

Rust and Go naturally have a more native-oriented approach to installing native tools. `cargo install` and `go install` each drop a single self-contained executable directly into a flat, PATH-included `bin` directory. The binary at that path *is* the tool — no symlink indirection, no `.store` shadow tree, no apphost shim:

```bash
$ cargo install ripgrep           # command is `rg`, crate is `ripgrep` (from crates.io)
$ ls -l $(which rg)
-rwxr-xr-x@ 1 rich  staff  5843184 Jun 16 22:20 /Users/rich/.cargo/bin/rg

$ go install github.com/jesseduffield/lazygit@latest  # GitHub is the registry for Go
$ ls -l $(which lazygit)
-rwxr-xr-x@ 1 rich  staff  26540370 Jun 16 22:24 /Users/rich/go/bin/lazygit
```

The same is true for your *own* projects, not just packages pulled from a registry. Building a local "hello world" and installing it lands the binary in exactly the same `bin` directory:

```bash
$ cd hello-rust && cargo install --path .
$ ls -l $(which hello-rust)
-rwxr-xr-x@ 1 rich  staff  431200 Jun 16 22:29 /Users/rich/.cargo/bin/hello-rust
$ hello-rust
Hello from Rust!

$ cd hello-go && go install .
$ ls -l $(which hello-go)
-rwxr-xr-x@ 1 rich  staff  2492466 Jun 16 22:30 /Users/rich/go/bin/hello-go
$ hello-go
Hello from Go!
```

This is the model v3 BYOR / Native AOT tools adopt with `~/.dotnet/bin`: the installed file is the real native executable, found and run with no extra launcher layer. It is also the model the `dotnet-install` prototype already implements — [a locally built tool and a published tool install to the one shared location](https://github.com/dotnet/sdk/issues/50747).

Listing the directories shows what each ecosystem keeps there. Both put *installed tools* — registry tools and your own builds alike — directly in one flat `bin` (located via `$CARGO_HOME` and `$GOBIN`/`$GOPATH`, with the defaults shown). Rust goes further: rustup also keeps the toolchain itself (`cargo`, `rustc`, `rustfmt`, `rust-analyzer`) in `~/.cargo/bin`. Go keeps its toolchain separate under `GOROOT`, so `$GOPATH/bin` holds only `go install` output:

```bash
$ ls "${CARGO_HOME:-$HOME/.cargo}/bin"
cargo		clippy-driver	rust-analyzer	rustc
cargo-clippy	hello-rust	rust-gdb	rustdoc
cargo-fmt	rg		rust-gdbgui	rustfmt
cargo-miri	rls		rust-lldb	rustup

$ ls "${GOBIN:-$(go env GOPATH)/bin}"
hello-go
lazygit
```

The .NET equivalent today is `~/.dotnet/tools`, which holds the v1/v2 tools —
each entry a symlink or apphost shim into the `.store` content tree rather than
the executable itself:

```bash
$ ls ~/.dotnet/tools
dotnet-cve-enricher  dotnet-inspect  dotnetsay  ildasm     release-notes
dotnet-ildasm        dotnet-release  ilspycmd   release-notes-gen
```

Those names are just the entry points on `PATH`; the real content lives in a
parallel `.store` tree. `find` exposes the difference. Every `go install` tool
*is* the file in `bin` — flat, one entry each:

```bash
$ find ~/go/bin
/Users/rich/go/bin
/Users/rich/go/bin/lazygit
/Users/rich/go/bin/hello-go
```

On .NET, every entry is a thin shim whose real binary is buried deep in `.store`,
and the pattern repeats for *every* installed tool — package id and version each
appear twice, with a redundant `{rid}` nesting:

```bash
$ ls -l ~/.dotnet/tools/dotnet-*
dotnet-cve-enricher -> .store/dotnet.release.cveenricher/0.1.0/dotnet.release.cveenricher.osx-arm64/0.1.0/tools/any/osx-arm64/Dotnet.Release.CveEnricher
dotnet-inspect      -> .store/dotnet-inspect/0.10.7/dotnet-inspect.osx-arm64/0.10.7/tools/any/osx-arm64/dotnet-inspect
dotnet-release      -> .store/dotnet.release.tools/2.3.0/dotnet.release.tools.osx-arm64/2.3.0/tools/any/osx-arm64/Dotnet.Release.Tools
```

Nine installed tools produce **414 entries** under `.store`. Go's `bin` holds one
file per tool; .NET's `.store` holds a deep per-package content tree behind each
name on `PATH`.

```bash
$ find ~/.dotnet/tools/.store | wc -l
414
```
