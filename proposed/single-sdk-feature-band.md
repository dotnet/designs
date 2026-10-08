# Monthly SDK Minor Releases

Two-band transition in .NET 11 and monthly minor releases beginning in .NET 12

**Owner** [marcpopMSFT](https://github.com/marcpopMSFT)

## Decision Summary

We will make this change in two stages:

- **.NET 11:** Ship exactly two SDK feature bands. `11.0.1xx` is the long-lived servicing band and accepts only security fixes and fixes required to unblock servicing. `11.0.2xx` is the feature-delivery band.
- **.NET 12:** Replace SDK feature-band versions with monthly minor SDK releases, such as `12.1.0`, `12.2.0`, and `12.3.0`.

We will announce both stages together. During the early .NET 12 previews, we will address the infrastructure and ecosystem gaps required to support minor-version SDK releases.

## Motivation

.NET tooling (MSBuild, NuGet, Roslyn, etc) and Visual Studio already ship on a monthly cadence starting with 18.0, but stable SDK features are delivered through quarterly feature bands. This mismatch creates a release-management problem every month: teams must coordinate monthly tooling and Visual Studio releases with an SDK release model that advances only once per quarter.

Internally, each feature band requires repeated engineering and coordination work for:

- Branding and branching.
- Implicit version updates.
- Runtime and workload updates.
- `darc` subscription and channel maintenance.
- Visual Studio insertion coordination, particularly between main and stable branches.

Many of these we should solve regardless of this proposal but the current state still creates a lot of additional friction and mental load at a minimum.

Externally, the mismatch makes it harder to explain which tooling features and fixes are available in Visual Studio, in the standalone SDK, and in servicing releases. Customers can receive monthly Visual Studio and tooling updates while the SDK's visible feature boundary remains quarterly.

Feature bands add another layer of complexity. Versions such as `12.0.100`, `12.0.200`, and `12.0.300` do not clearly communicate release order, support status, or their relationship to the monthly tooling releases. The concept is .NET SDK-specific and requires customers, support teams, and internal tooling to understand special version-selection and servicing rules.

## Proposal

We propose a phased rollout that reduces the number of release trains in .NET 11 while the ecosystem prepares for monthly minor SDK releases in .NET 12.

### Phase 1: .NET 11 two-band transition

.NET 11 ships exactly two SDK feature bands:

| Version | Purpose | Allowed changes |
| --- | --- | --- |
| `11.0.1xx` | Long-lived servicing baseline | Security fixes and fixes required to unblock servicing |
| `11.0.2xx` | Feature delivery | New SDK and tooling features, plus applicable servicing fixes |

There will be no `11.0.3xx` or `11.0.4xx` feature bands. Existing feature-band-aware infrastructure remains in place for .NET 11, avoiding a versioning migration before the required host, workload, installer, acquisition, update, and release infrastructure is ready.

### Phase 2: .NET 12 monthly minor releases

Beginning with .NET 12, SDK feature bands are replaced with monthly minor SDK releases. The .NET 12 GA SDK is `12.0.0`, followed by `12.1.0`, `12.2.0`, and so on. Each monthly minor is an explicit feature boundary. The latest minor receives new features, while the original `12.0.x` line provides a stability-oriented servicing boundary.

Release boundaries provide stronger isolation than a shared feature-flag system. In particular, analyzer, compiler, and API-shape changes can affect customers even when a tool attempts to gate user-visible behavior.

## Scenarios and User Experience

### Enterprise source-build partner requires maximum stability

An enterprise source-build partner remains on the original `12.0.x` line. It receives security and reliability fixes without taking new SDK or tooling features.

Outcome:

- Servicing updates exclude new features and intentional default changes, although necessary security or reliability fixes can still have observable effects.
- New features do not depend on every SDK-loaded component correctly implementing a shared flag.
- Partner validation cost and risk are reduced.
- The release version clearly identifies the stability boundary.

### Visual Studio stable users receive monthly tooling updates

A developer on a supported Visual Studio stable channel receives the latest stable monthly SDK minor according to Visual Studio insertion and servicing policies. The SDK includes the latest applicable runtime and tooling security fixes.

Outcome:

- Features can reach stable customers monthly instead of quarterly.
- Preview content is validated before monthly promotion.
- SDK release boundaries align more closely with the Visual Studio release cadence.

### Visual Studio Canary users validate the next monthly release

A developer on Visual Studio Canary receives preview builds of the next SDK minor from the public release branch.

Outcome:

- Tooling receives validation before the next monthly promotion.
- Preview behavior does not appear in the current stable monthly release.
- Visual Studio Insiders can consume the Canary flow without requiring a separate SDK build.

### Command-line and VS Code users select an SDK release

A developer using the .NET CLI or VS Code can use `global.json` and existing SDK acquisition mechanisms to remain on the original stability line, select a monthly minor, or roll forward to newer monthly releases.

Outcome:

- The SDK version communicates the feature set without an additional global configuration.
- SDK selection continues to support projects with different stability requirements; whether monthly minors install side by side remains an installer-design decision.
- Moving to a newer feature set is an explicit SDK version change.

## Requirements

### Goals

- Ship exactly two SDK feature bands in .NET 11, with distinct servicing and feature-delivery roles.
- Preserve existing feature-band-aware infrastructure during the .NET 11 transition.
- Replace quarterly SDK feature bands with monthly minor SDK releases.
- Make the SDK version an explicit and reliable feature boundary.
- Deliver stable tooling features monthly.
- Preserve the original `N.0.x` line for customers that require security and reliability fixes without new features.
- Maintain a preview path for the next monthly minor through Visual Studio Canary.
- Define predictable branching, branding, promotion, and servicing processes.
- Define how tooling fixes reach both the VMR and Visual Studio without introducing unmanageable mirror conflicts or check-in overhead.
- Update workloads, installers, SDK selection, and release automation for the new compatibility boundary.

### Non-goals

- Changing runtime versioning to match the SDK's monthly minor.
- Requiring the SDK and runtime contained in it to have the same version.
- Redesigning component-specific experimental feature flags.
- Supporting every superseded monthly minor indefinitely.
- Changing the feature rollout experience of components loaded directly by Visual Studio rather than through the SDK.

## Versioning Model

The SDK version components have the following meaning:

- Major: the annual .NET release.
- Minor: a monthly SDK feature release.
- Patch: an SDK or tooling hotfix, or a release used to carry an updated workload set between monthly feature releases.
- Prerelease label: builds of the next monthly minor before promotion to stable.

For example:

- `12.0.0-preview.1.<buildnumber>`: First preview of .NET 12 SDK
- `12.0.0`: the annual GA and the beginning of the stability-oriented release line.
- `12.0.1`: a security or reliability update to the original stability line.
- `12.1.0`: the first monthly SDK feature release.
- `12.2.0-preview.<buildnumber>`: a preview of the next monthly SDK feature release.
- `12.2.1`: a hotfix or workload update after `12.2.0`.

Incrementing the minor resets the patch component to zero. Within the feature-delivery train, `12.(M+1).0` succeeds `12.M.P` and normally contains SDK and tooling content at least as recent as the prior minor, except for an intentional rollback. The `12.0.x` line is a parallel stability-oriented servicing family rather than the predecessor of every monthly minor.

The SDK and runtime versions are intentionally independent. An SDK hotfix such as `12.2.1` might contain runtime `12.0.3`. A monthly SDK preview contains the most recently available runtime patch rather than requiring a runtime release on the same day. Release notes and diagnostics must make the relationship clear.

When a `12.0.P` servicing release and a `12.M.0` feature release ship in the same month, they contain the same runtime servicing level. A preview of the next minor may temporarily contain the runtime servicing level from the preceding month.

Using the patch component for both SDK hotfixes and workload updates means patch numbers identify release order rather than a particular kind of change. Release tooling and documentation must not assume that every patch increment has the same cause.

When regular feature delivery ends, the final minor becomes the servicing line for the feature train. Subsequent updates advance its patch component rather than creating additional minors.

## Release and Branch Model

Development occurs primarily in a long-lived public `release/12.x` branch. Public code flow from tooling repositories targets this branch. Preview builds of the next monthly minor are produced from it and flow to Visual Studio Canary.

Once a month, approved preview content is promoted to a long-lived internal stable branch, such as `internal/release/12.x`. The public preview and internal stable branches are branded independently, with the version update and promotion process automated where possible.

The model has three logical release paths:

- The original `12.0.x` stability line.
- The latest stable monthly minor.
- The next monthly minor in preview.

Only the original stability line and latest monthly minor are serviced. Superseded monthly minors remain installable but do not receive further fixes.

## Tooling Code Flow

Tooling teams continue flowing public development into the public `release/12.x` branch for the next monthly SDK preview. Stable fixes must also reach the internal VMR branch used to produce the stable SDK and, when applicable, Visual Studio.

The design must select one of the following models:

### Code flow with mirror conflict resolution

Stable tooling changes flow automatically into the internal stable branch. The mirror and code-flow infrastructure must distinguish public preview content from stable servicing content and provide a supported way to resolve conflicts.

Benefits:

- A tooling fix has one authoritative check-in.
- Automated propagation reduces the chance that a required fix is omitted from either the SDK or Visual Studio.
- Tooling repositories retain their normal ownership and validation processes.

Costs and risks:

- The current public-to-internal mirror topology may blur preview and stable intent.
- Conflicts between the public preview branch and internal stable branch can block propagation.
- The mirror system becomes part of the release-critical path and needs clear ownership and service-level expectations.

### Dual check-in by tooling teams

Tooling teams check stable fixes into both their normal repository or branch and the VMR or internal release branch when the fix is needed by both the SDK and Visual Studio.

Benefits:

- Each destination receives an explicit, independently validated change.
- The model does not depend on solving preview-to-stable mirror conflicts first.
- Tooling teams can tailor a fix to each branch when the branches have diverged.

Costs and risks:

- Duplicate check-ins increase tooling-team workload and can diverge.
- Tooling teams typically insert into VS from a build from their repo and into the SDK in the VMR
- A fix can be missed in one destination.
- Security fixes require coordinated validation and disclosure-safe propagation across multiple repositories.

### Decision criteria

The code-flow model should be selected with the tooling teams and VMR owners based on:

- Whether the mirror can reliably distinguish preview development from stable servicing.
- Whether conflicts can be detected, assigned, and resolved within release timelines.
- Whether one check-in can satisfy the validation requirements for both SDK and Visual Studio.
- The expected frequency of fixes that must reach both destinations.
- The operational cost and omission risk of dual check-ins.
- Ownership of failures in the propagation path.

Until this decision is made, the proposal does not assume that monthly promotion alone solves tooling branch management. The selected model must include a security-fix path and a recovery process for failed or incomplete propagation.

## Servicing and Security Fixes

The servicing policy must distinguish among:

- Runtime security and reliability updates included in an SDK release.
- SDK or tooling hotfixes that do not require a runtime release.
- Workload updates released between monthly SDK releases.
- Fixes required by Visual Studio or source-build consumers on a different SDK release path.

Security and reliability fixes need an explicit propagation path to the original stability line, the current stable monthly release, the next preview, supported Visual Studio SDK builds, and any dedicated source-build branch. A runtime security release does not necessarily require a new SDK minor, but every supported SDK line still needs a way to acquire the runtime update.

The policy must also cover delayed, skipped, and out-of-band runtime releases. Any security fix needed in both stable and preview must be applied through the selected tooling code-flow model rather than waiting for the next monthly promotion.

## Visual Studio Integration

- The next monthly SDK preview flows to Visual Studio Canary.
- The current stable monthly SDK is available to supported Visual Studio channels according to their insertion and servicing policies.
- Visual Studio Insiders consume the preview flow through Canary rather than requiring a separate SDK build.
- We would try to avoid any insiders insertions unless a blocking bug necessitated an update
- Components loaded directly by Visual Studio continue to follow their existing Visual Studio rollout policies.

## Source-build Contract

Enterprise source-build partners that require maximum stability can remain on `12.0.x` and receive security and reliability fixes without taking monthly feature releases. Other source-build consumers can move to newer monthly minors through their normal distribution update process.

A customer remaining on `12.0.x` cannot opt into selected features from later monthly minors without moving to a newer SDK. This tradeoff is intentional: release boundaries provide stronger isolation than relying on every component to gate behavior correctly. It is particularly important for analyzer and compiler fixes that can introduce warnings, change syntax trees or APIs, or otherwise affect downstream tooling.

The next minor preview should be buildable with a prior stable release so 12.4.0 can be built with 12.3.0. The previews of the SDK will have to build with N-2 SDKs as the N-1 will still be unreleased and being worked on.

## Workloads, Installers, and SDK Selection

Existing workload and SDK infrastructure encodes feature-band identity in workload manifests, workload sets, package IDs, version validation, and selection logic. These systems must recognize monthly SDK minors while continuing to model workload compatibility boundaries independently.

Workload set package versions generally match the SDK version. For example, SDK `12.2.0` can consume `Microsoft.NET.Workloads.12.2.0`, while SDK hotfixes and mid-month workload updates consume successive patch numbers such as `12.2.1` and `12.2.2`. The release process must define ordering when an SDK hotfix and workload update are both required in the same month.

Workload manifest package compatibility bands do not need to change for every monthly minor. The initial .NET 12 manifest package could be:

`Microsoft.NET.Workload.Mono.ToolChain.Current.Manifest-12.0.0`

SDKs `12.1.0` through `12.6.x` could continue resolving manifests from that compatibility band. If a workload change requires a new compatibility boundary, a later minor can rebaseline it, for example:

`Microsoft.NET.Workload.Mono.ToolChain.Current.Manifest-12.7.0`

SDK `12.7.0` and later releases would then resolve the new manifest band, while earlier SDK minors remain on the original band. Workload commands and Visual Studio select the appropriate band without requiring customers to manage this distinction directly.

Existing `global.json` version selection and roll-forward behavior must allow customers to remain on `12.0.x`, select a particular monthly SDK, or move to newer monthly releases. The exact behavior of each roll-forward policy across minor releases must be specified, including how feature-band-oriented policy names map to monthly minors.

Installer and acquisition behavior also needs an explicit contract:

- Whether monthly minors install side by side or replace the previous monthly SDK.
- How MSI related-product and upgrade relationships are represented.
- How uninstalling a monthly SDK affects shared runtimes and workload content.
- How the .NET uninstall tool identifies monthly SDK releases and determines which installations are eligible for removal.
- How Microsoft Update targets standalone SDKs on the original stability line versus later monthly minors.
- How SDKs installed by Visual Studio are detected and serviced separately from standalone SDKs.

The release library and other compliance, inventory, and support tooling may also encode feature-band assumptions when parsing, classifying, or comparing SDK versions. These tools must be audited and updated so that monthly minors are recognized as SDK feature boundaries and the original `N.0.x` stability line is handled correctly.

## Transition

Adopting monthly minor versions at the beginning of a major release is simpler than changing versioning after customers and tooling have consumed feature-band versions. .NET 11 therefore uses a two-band transition model:

- `11.0.1xx` remains the rollback and servicing boundary for customers and source-build partners that require maximum stability. It receives only security fixes and fixes required to unblock servicing.
- `11.0.2xx` receives new SDK and tooling features.
- No additional .NET 11 feature bands are produced.

During the early .NET 12 previews, we will identify, assign, and close the gaps required to use monthly minor versions. This work includes host and SDK resolution, `global.json`, workload tooling, installers, the .NET uninstall tool, the release library, Microsoft Update, WinGet, DNIM, getdotnet, release pipelines, VMR infrastructure, compliance and inventory systems, support tooling, third-party version parsing, and documentation.

Tools that encounter both the feature-band scheme used through .NET 11 and the monthly-minor scheme beginning in .NET 12 need defined compatibility behavior. Changes that affect the ecosystem should land early enough in the .NET 12 preview cycle to allow validation and correction before stabilization.

## External Communication

We will publish a blog post that explains both stages of the plan:

- Why .NET 11 retains feature-band numbering while reducing the release model to two bands.
- The servicing-only role of `11.0.1xx` and the feature-delivery role of `11.0.2xx`.
- Why monthly minor versions are the intended .NET 12 direction.
- Which infrastructure and ecosystem gaps will be addressed during the .NET 12 previews.
- What customers, partners, and tool authors should expect for SDK selection, workloads, installation, servicing, and version parsing.

Migration guidance for affected tooling must be available before the first .NET 12 preview that uses the new versioning model.

## Benefits and Drawbacks

Benefits:

- Monthly versions communicate release order directly and align SDK tooling delivery with a monthly Visual Studio cadence.
- Features reach stable customers monthly rather than waiting for a quarterly feature band.
- Release boundaries provide stronger isolation for stability-focused customers.
- The SDK does not depend on every loaded component implementing and honoring a shared feature-channel setting.
- Tooling teams use normal version and branch boundaries instead of maintaining an expanding set of feature gates.

Drawbacks:

- Source-build customers on the original stability line cannot opt into selected newer features.
- Workload, installer, SDK selection, release, and servicing infrastructure require significant changes.
- Preview-to-stable promotion and fix propagation can create branch drift and merge debt.
- Visual Studio and source-build may still require separate servicing paths.
- SDK and runtime versions can differ in ways that require clear diagnostics and documentation.
- Monthly minors increase the number of SDK versions encountered by customers and support teams.

## Implementation Work and Remaining Questions

The following decisions are settled:

- .NET 11 has exactly two SDK feature bands.
- `11.0.1xx` receives only security fixes and fixes required to unblock servicing.
- `11.0.2xx` is the .NET 11 feature-delivery band.
- Monthly minor SDK versions are the intended .NET 12 direction.
- Both stages will be communicated publicly.

The .NET 12 preview work must:

- Choose code flow with mirror conflict resolution or dual check-ins for tooling fixes needed by both the SDK and Visual Studio.
- Define ownership and service expectations for the selected tooling propagation path.
- Validate monthly promotion and branding automation.
- Define security-fix propagation for the stability line, current stable minor, next preview, Visual Studio, and source build.
- Define workload compatibility and patch-number ordering.
- Specify `global.json` roll-forward behavior across monthly minors.
- Define installer, Microsoft Update, uninstall, acquisition, rollback, and side-by-side installation behavior.
- Audit first-party and third-party systems that parse or compare SDK versions.
- Confirm operational responsibilities with tooling teams and VMR owners.

## Alternatives Considered

### Continue quarterly feature bands

The SDK could retain versions such as `12.0.100`, `12.0.200`, `12.0.300`, and `12.0.400`. This avoids much of the transition cost, but it preserves the current feature-band concept, delays stable feature delivery, and does not communicate monthly release ordering directly.

### Single feature band with global feature channels

We considered retaining one SDK feature band and introducing a global feature channel such as `baseline`, `stable`, or `preview`. Every participating SDK-loaded component would read the channel and gate new features or behavior accordingly.

We did not select this approach because:

- It requires substantial cross-repository infrastructure, governance, validation, diagnostics, and ongoing feature-gate management.
- It moves complexity into testing without ensuring that every feature or breaking change is correctly gated.
- Analyzer and compiler fixes can introduce warnings or behavioral changes, especially for customers using warnings as errors, and often cannot be practically flag-gated.
- Experimental API and syntax-tree changes can break analyzers even when user-visible behavior is behind a flag.
- A continuously evolving feature band conflicts with the stability expectations of enterprise and source-build customers.
- Release boundaries are easier for customers and support teams to identify than the effective state of many component-level gates.

Component-specific flags can still be used for experiments where appropriate, but they are not the SDK-wide compatibility or release mechanism.