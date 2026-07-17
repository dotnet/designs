# Making `dotnetup` win: system PATH and muxer integration for the "happy path"

**Status:** Proposed
**Owners:** (dotnetup / SDK team — fill in)
**Area:** Acquisition / host / SDK resolution
**Related:** [`accepted/2026/dotnetup/cli-acquisition-tool.md`](https://github.com/dotnet/designs/blob/main/accepted/2026/dotnetup/cli-acquisition-tool.md) · [dotnet/designs#350 (closed)](https://github.com/dotnet/designs/pull/350) · [install-location-per-architecture](https://github.com/dotnet/designs/blob/main/accepted/2021/install-location-per-architecture.md) · [local-sdk-global-json](https://github.com/dotnet/designs/blob/main/accepted/2025/local-sdk-global-json.md)

## Summary

We want `dotnetup` to be **opinionated** and to **win**: once a user adopts `dotnetup`, the
CLI, build tooling, background agents (e.g. GitHub Copilot), and C# Dev Kit should all resolve
the **same** SDK — the latest, from the `dotnetup`-managed hive — so a machine has one
consistent .NET that every build uses. This is the "happy path": tooling, CLI, and agents all
agree on one SDK, so a developer gets predictable behavior no matter how a build is launched,
instead of different tools silently picking different SDKs.

The hard part is *making* `dotnetup` win. `dotnetup` installs .NET into a user-local hive so
users can manage SDKs and runtimes without elevation — but on Windows the .NET that actually
*wins* is usually the machine-wide install under `C:\Program Files\dotnet`, because its
`dotnet.exe` is on the **system** `PATH` and the muxer resolves SDKs relative to its own
location. So after a user runs `dotnetup`, their CLI, C# Dev Kit, GitHub Copilot, and other
tools may still pick a *different* SDK than the one `dotnetup` manages. This document is about
closing that gap: the system `PATH` and muxer/host integration that lets the `dotnetup` hive
win.

It proposes a two-phase plan to make the `dotnetup`-managed hive reliably win:

- **Near term (.NET 10 timeframe, no runtime changes):** when a machine-wide install is
  present, insert the `dotnetup` dotnet directory into the **system** `PATH` *immediately
  before* the `Program Files\dotnet` entry. This is a
  one-time elevated change that survives later .NET updates because .NET's MSIs *append*
  to `PATH`.
- **Long term (.NET 11):** introduce a single new redirect mechanism in the machine-wide
  muxer/host that relocates **both SDK and runtime resolution** — including `dotnet foo.dll` —
  to a per-user `dotnetup` hive, driven by a **user-scoped** environment variable. Because the
  variable is user-scoped, no elevation is required, each user opts in independently, a later
  machine-wide SDK install cannot silently take ownership back, and there is no system `PATH`
  manipulation.

## Motivation

### What "winning" needs to mean

The end-user goal (the "happy path"): after adopting `dotnetup`, the SDK it manages is the
one *everything* uses — `dotnetup` should be opinionated and "update paths everywhere to win."

Why it matters:

- **`dotnetup` only delivers its purpose if the installs it manages actually win.**
  `dotnetup` lets users acquire, update, and manage .NET at the user level, without elevation.
  But if a user installs or updates an SDK with `dotnetup` and their shell, IDE, and agents
  keep resolving a *different* machine-wide .NET, nothing they run uses it. Making the managed
  hive win is what turns `dotnetup` from a parallel, ignored install into the .NET the machine
  actually uses.
- **Predictability.** Different tools silently resolving different SDKs is confusing and hard
  to diagnose; one SDK across CLI, tooling, and agents means a build behaves the same however
  it is launched.
- **Consistency of caches and results.** Up-to-date checks and build caches (e.g. LSCaches)
  stay consistent when every build on the machine uses the same SDK.
- **Uniform delivery of SDK improvements.** Value we are moving into the SDK reaches users
  uniformly. One concrete driver is the decomposition of C# Dev Kit and pushing its
  capabilities into the SDK (the up-to-date check / FUTDC, the single project system,
  LSCaches), which light up consistently only when tooling, CLI, and agents all resolve the
  same SDK — but the underlying consistency benefit is general.

Success looks like:

- New command-line sessions resolve the `dotnetup` dotnet.
- New UI apps resolve it too — VS Code / C# Dev Kit, the GitHub Copilot app, and ideally
  any app that shells out to `dotnet`.
- It works when **no** other .NET is installed.
- It works when a machine-wide ("system") .NET *is* installed.
- It works even when that machine-wide .NET is **older** (.NET 6/7/8).

## Goals

- Make the `dotnetup`-managed hive the SDK that wins for CLI and tools, reliably, after a
  simple `dotnetup` action.
- Survive later machine-wide .NET updates (VS / Windows Update) without the user having to
  re-run a fix.
- Avoid a design that requires ongoing elevation.
- Keep loose-zip and hand-extracted .NET usage predictable.
- Make the `dotnetup`-managed hive win for **both** SDK/tooling and runtime resolution
  (including `dotnet app.dll`), not just the SDK.

## Non-goals

- Changing how `dotnetup` acquires or lays down SDKs/runtimes
- Forcing `global.json` adoption on users as the mechanism for winning (it helps some IDE
  cases but is not the general answer; see [Relationship to existing mechanisms](#relationship-to-existing-mechanisms)).
- Managing workloads or ASP.NET Core hosting modules (e.g. ANCM) that a machine-wide install
  provides; those remain open items.
- Redesigning the [`dotnetup env`](https://github.com/dsplaisted/sdk/blob/feature/dotnetup-path-mode/documentation/general/dotnetup/designs/dotnetup-env.md)
  access-mode model (`none` / `shell` / `everywhere`); this
  proposal only changes `everywhere` mode's behavior and makes it the default on Windows.

## Proposal

`dotnetup`'s environment integration is governed by an access mode — `none`, `shell`, or
`everywhere` (the `dotnetup env` command):

- **`none`** wires nothing onto `PATH`; the user invokes .NET via `dotnetup dotnet`.
- **`shell`** writes only the shell profile. On Windows that means PowerShell command-line
  sessions see the `dotnetup` dotnet (they load the profile), but `cmd.exe` and GUI-launched
  apps — which read no profile — still resolve the machine-wide `Program Files\dotnet`.
- **`everywhere`** additionally writes the Windows user-scope `PATH`/`DOTNET_ROOT`, and is the
  only mode that touches the **system** `PATH`.

This proposal is delivered as a change to **`everywhere`** mode and proposes making `everywhere`
the **default on Windows**; `none` and `shell` are unchanged. Both phases below describe what
`everywhere` mode does.

### Phase 1 (near term, .NET 10 timeframe): prepend before the Program Files entry

In `everywhere` mode, when a machine-wide `Program Files\dotnet` install is on the system
`PATH`, `dotnetup` today *removes* that entry. This proposal changes that to a durable prepend:

1. Elevate once (the same elevation `dotnetup` already uses to edit the machine-wide `PATH`).
2. **Insert the `dotnetup` dotnet directory into the system `PATH` immediately before the
   `Program Files\dotnet` entry** (rather than removing that entry).
   - If there is no machine-wide `dotnet` on the system `PATH` at all, append the `dotnetup`
     dotnet directory to the **end** of the system `PATH`.  If a machine-wide install is added
     later, dotnetup will still win.
3. Do **not** place `dotnetup` at the very front of `PATH`.

Why this works:

- .NET's MSIs **append** their `PATH` entry. A later update therefore lands *after* the
  `dotnetup` entry, so `dotnetup` keeps precedence without the user re-running anything.
- Inserting directly before the existing `dotnet` entry (rather than at the front of `PATH`)
  avoids shadowing unrelated OS tools and keeps the blast radius of any future loader/DLL
  issue limited to "`dotnet` and things after it," not Windows/system utilities. (Historical
  precedent: tools that jam themselves at the front of `PATH` have shadowed same-named OS
  utilities — e.g. Java breaking Windows Kerberos tools.)

Why this isn't ideal:

The system `PATH` is machine-wide but points at one user's hive. Other users generally can't
access it, so they fall through to the machine-wide install — but an elevated process (or an account such as SYSTEM)
may resolve `dotnet` from that hive, so the wrong user's install can win. This "multi-user oddness"
(accepted for the near term) is why Phase 1 is a bridge; Phase 2 (per-user, no system `PATH`
change) removes it.

### Phase 2 (.NET 11): redirect the system host to the user's hive

**Requirements.** The mechanism should:

- Make the `dotnetup` hive win for the **whole toolchain** — SDK resolution *and* runtime/host
  resolution, including `dotnet foo.dll` — not SDK selection only.
- Be **per-user**: each user opts in independently, and one user's choice never changes another
  user's resolution.
- Need **no elevation** and make **no system `PATH` change**.
- Be **durable**: a later machine-wide .NET install can't silently reclaim ownership.
- Keep **loose/hand-extracted** .NET predictable: running a `dotnet.exe` you unzipped yourself
  is unaffected.

**Proposed approach (subject to change).**

This seems to lead to the following solution:

- The `Program Files\dotnet` muxer (`dotnet.exe`) can be the one resolved via the `PATH`
- The muxer supports a **user-scoped** environment variable that redirects its resolution to a
  different dotnet hive.

Concretely, the muxer would support the following environment variables:

- `DOTNET_ROOT_REDIRECT_TARGET` - The path of the hive to redirect to
- `DOTNET_ROOT_REDIRECT_SOURCES` - A list of sources to redirect from.  This allows the redirect to be restricted to the Program Files muxer, so that other muxers (such as from an unzipped runtime/SDK) are not affected

The redirect should cover **both SDK and runtime** resolution — including `dotnet foo.dll` — so the whole toolchain comes from the chosen hive.

This requires a redirect-aware machine-wide host (.NET **≥ 11**). When none is present,
  `dotnetup` falls back to Phase 1 or installs a minimal redirect-aware muxer (see
  [Installing a redirect-aware muxer when the system muxer is too old](#installing-a-redirect-aware-muxer-when-the-system-muxer-is-too-old)).
  Within `everywhere` mode this supersedes Phase 1's system-`PATH` edit.

### Installing a redirect-aware muxer when the system muxer is too old

Phase 2 needs the machine-wide muxer/host to be redirect-aware (.NET ≥ 11). If a machine has
no machine-wide .NET, or only an older one, `dotnetup` can install a **minimal system-level
muxer** — just the redirect-aware host, not a full SDK or runtime — at the machine-wide install
location. It honors the redirect variable and defers to the user's `dotnetup` hive, so Phase 2
works without the user first installing a full machine-wide .NET ≥ 11.

This is the most robust form of a "shim": the shim **is** the real muxer, installed minimally,
rather than a separate binary that imitates it.

**Why not a separate shim that differs from the muxer.** An earlier idea was a distinct
shim/proxy binary at the system location that resolves the current user and routes to their
hive. Making the shim a *different* thing from the muxer has several problems:

- **It isn't durable; the real muxer is.** The host/muxer is a versioned MSI component that
  upgrades in place. A foreign shim dropped at the system location is not that component, so a
  later .NET host install or repair overwrites it with the real native muxer. Installing the
  real redirect-aware muxer instead participates in that version-monotonic, in-place upgrade —
  a later install never downgrades it, and if it is also ≥ 11 the redirect keeps working.
- **A different location reintroduces the `PATH`-order problem.** Putting the shim somewhere
  other than the machine-wide location merely moves the "which `dotnet` is first on `PATH`"
  fight around.
- **The muxer has responsibilities a wrapper can't fully cover.** The host is the entry point
  for much more than `dotnet build`: `dotnet app.dll` runtime launches, apphost resolution,
  `hostfxr`/`hostpolicy` loading, and version roll-forward. A shim that only wraps CLI commands
  would behave differently from the real host for app launches and hosting — exactly the
  SDK-vs-runtime inconsistency Phase 2 exists to remove. Using the real muxer keeps one code
  path and one behavior.

Installing a system-level muxer requires a one-time elevation. Durability is not a concern:
the .NET host/muxer is a separately-versioned MSI component that upgrades in place, so a later
.NET install — even of an older SDK/runtime — will not downgrade the redirect-aware muxer we
installed. Once a new-enough muxer is present, it stays.

## Background: why this is hard

- **Every .NET MSI puts `C:\Program Files\dotnet` on the system `PATH`.** Even if `dotnetup`
  elevates once to remove it, the next .NET update — which can arrive via Windows Update or
  a Visual Studio update — adds it back. Removal is not durable.
- **There's no existing way to redirect resolution to a different hive across all scenarios.**
  The knobs that exist are each partial:
  - `DOTNET_ROOT`/`DOTNET_ROOT_<ARCH>` influence the *apphost* (where a framework-dependent app
    finds its runtime), but per
    [install-location-per-architecture](https://github.com/dotnet/designs/blob/main/accepted/2021/install-location-per-architecture.md)
    the `dotnet` muxer resolves relative to the directory it is launched from, not `DOTNET_ROOT`.
  - `global.json` `sdk.paths` (with the `$host$` token; see
    [local-sdk-global-json](https://github.com/dotnet/designs/blob/main/accepted/2025/local-sdk-global-json.md))
    can point SDK resolution at other locations, but it is **per-project** and covers **SDK
    selection only** — not runtime or `dotnet app.dll` launches.

  No single knob points every scenario — `dotnet build`, `dotnet app.dll`, apphost launches — at
  a chosen hive, so which `dotnet.exe` is found first on `PATH` is what actually decides, and
  that is exactly what the machine-wide install keeps re-claiming.
- **Suppressing the machine-wide `PATH` entry is hard.**
  [dotnet/runtime#118092](https://github.com/dotnet/runtime/pull/118092) tried to add an
  environment variable that would signal the .NET installer *not* to add
  `C:\Program Files\dotnet` to the system `PATH`, and was reverted in
  [#120472](https://github.com/dotnet/runtime/pull/120472) because historical details of how
  the MSI components were authored could cause the value to be incorrectly removed from the PATH in some install/uninstall scenarios.

These are the constraints this proposal is designed to respect: don't rely on removing the
machine-wide `PATH` entry, don't break loose-zip users, and relocate the SDK **and** the
runtime together rather than leaving the runtime behind.

## Relationship to existing mechanisms

- **`global.json` `sdk.paths` / `$host$`**
  ([local-sdk-global-json](https://github.com/dotnet/designs/blob/main/accepted/2025/local-sdk-global-json.md)):
  lets a workspace point the resolver at a user-local directory first, and is a good fit for
  some IDE scenarios. It is not proposed as the general answer here: it requires a
  `global.json` in play (we do not want to push broad `global.json` adoption as the way to
  "win"), and it does not address `DOTNET_ROOT`/runtime resolution or the machine-wide `PATH`
  ordering that governs bare `dotnet` in a fresh shell.
- **`DOTNET_ROOT` / `DOTNET_ROOT_<ARCH>`**: affect the apphost, not the muxer's SDK
  resolution, so they cannot by themselves make the `dotnetup` SDK win for `dotnet build`.
  `dotnetup` already sets `DOTNET_ROOT` where appropriate; the Phase 2 redirect is what closes
  the SDK- **and** runtime-resolution gap (it relocates both, unlike `DOTNET_ROOT` alone).
  Cross-architecture (`DOTNET_ROOT_<ARCH>`) handling is an open item.
- **VS Code / C# Dev Kit**: VS Code loads the user's shell profile, so it tends to pick up
  the `PATH`/`DOTNET_ROOT` that `dotnetup` writes there — but other GUI apps do not read the
  shell profile. Phases 1–2 target the broader "any app that shells out to `dotnet`" case
  rather than relying on profile loading.

## Alternatives considered

- **Remove `Program Files\dotnet` from the system `PATH`.** Requires elevation and is undone
  by the next .NET update (VS / Windows Update). A VS-install custom action doing a related
  x86 removal was tried and rolled back. Rejected as non-durable. Phase 1's "prepend before"
  achieves precedence without fighting future updates.
- **Prepend `dotnetup` at the very front of `PATH`.** Rejected on Windows-compatibility
  grounds: front-of-`PATH` placement can shadow OS utilities (historical breakage precedent).
  Phase 1 inserts directly before the existing `dotnet` entry instead.
- **Unconditional override that always redirects** (e.g. a plain `DOTNET_SDK_ROOT`, considered
  in #350): a variable that overrides resolution regardless of which `dotnet` was launched.
  Rejected because a user who downloads and runs a loose zip would be surprised to get a
  *different* SDK/runtime than the one next to that `dotnet.exe`. Phase 2 uses a
  **source-matched** redirect to avoid this.
- **Two separate SDK and runtime variables with SDK-only redirect** (the #350 shape:
  `DOTNET_SDK_ROOT` + `DOTNET_ROOT`): superseded. It left `dotnet foo.dll` on the machine-wide
  runtime ("the runtime is not fully relocated"), and reviewers found two values harder to
  explain than one. Phase 2 uses a single redirect covering SDK **and** runtime.
- **A separate shim/proxy binary distinct from the muxer.** Overwritten at the normal system
  location by the next machine-wide installer, and cannot fully cover host responsibilities
  (`dotnet app.dll`, apphost, `hostfxr`/`hostpolicy`). Prefer installing the real
  redirect-aware muxer instead — see
  [Installing a redirect-aware muxer when the system muxer is too old](#installing-a-redirect-aware-muxer-when-the-system-muxer-is-too-old).

## Open questions

- **Redirect variable name and semantics.** Reconcile with #350's `DOTNET_ROOT_REDIRECT_*`
  and the `DOTNET_SDK_ROOT` discussion. Since Phase 2 uses one mechanism for SDK and runtime,
  is it a single value or a source/target pair?
- **Precedence vs. `global.json` and `DOTNET_ROOT`.** Where does the redirect sit relative to a
  project's `global.json` (`sdk.paths`/version), an explicit `DOTNET_ROOT`, and the
  per-architecture `DOTNET_ROOT_<ARCH>` (e.g. an x64 SDK on arm64)?
- **Source-restriction design (for review).** We deliberately retain the source-path gate so
  redirection applies only to the centrally-installed muxer and loose zips are left alone. The
  runtime team has objected to launch-path-dependent behavior (jkotas on #350). Settle the
  exact matching rules — which source location(s) qualify, and how symlinked/copied hosts are
  treated — and confirm the trade-off is acceptable.
- **Runtime-load-behavior buy-in.** Relocating `dotnet foo.dll` requires the runtime/host
  team to honor the redirect for app launches, which #350 avoided changing. Securing that
  agreement is a prerequisite for Phase 2.
- **Non-Windows.** The goals are the same on macOS/Linux, but this proposal focuses on the
  Windows solution (likely the hardest). Phase 1 is Windows-specific and likely doesn't apply;
  Phase 2's redirect may or may not be the right approach there. The cross-platform design
  remains to be worked out.

## Appendix: prior art

- [`dotnetup/designs`](https://github.com/dotnet/sdk/tree/release/dnup/documentation/general/dotnetup/designs)
  (dotnet/sdk, `release/dnup` branch): the in-repo `dotnetup` design docs (environment setup,
  `dotnet` selection, env access modes, multi-architecture) that this proposal builds on.
- [`accepted/2026/dotnetup/cli-acquisition-tool.md`](https://github.com/dotnet/designs/blob/main/accepted/2026/dotnetup/cli-acquisition-tool.md)
  (PR #339, merged): the foundational `dotnetup` design; first-run sets `DOTNET_ROOT` and puts
  the user hive on `PATH`, and on Windows requests elevation for a registry key intended to
  keep global installers from re-claiming the system `PATH`.
- [dotnet/designs#350](https://github.com/dotnet/designs/pull/350) (closed unmerged): proposed
  the muxer redirect (`DOTNET_ROOT_REDIRECT_SOURCES` / `DOTNET_ROOT_REDIRECT_TARGET`) and
  `dotnetup use`. Closed unmerged over two concerns (see
  [Alternatives](#alternatives-considered) and [Open questions](#open-questions)): it redirected
  **SDK selection only**, leaving `dotnet app.dll` running on the runtime next to the
  machine-wide muxer ("the runtime is not fully relocated"); and a user who extracts a zip and
  runs it while a global redirect variable is set would get surprising behavior. This proposal
  treats both as solvable — a single SDK+runtime redirect, gated by a source path — rather than
  reasons to abandon the approach.
- [install-location-per-architecture](https://github.com/dotnet/designs/blob/main/accepted/2021/install-location-per-architecture.md):
  the resolution/priority rules; establishes that the muxer uses its own location while
  `DOTNET_ROOT`/`DOTNET_ROOT_<ARCH>` affect the apphost.
- [local-sdk-global-json](https://github.com/dotnet/designs/blob/main/accepted/2025/local-sdk-global-json.md):
  `sdk.paths`/`$host$`, the non-`PATH` way IDEs can find user-local installs.
