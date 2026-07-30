# Single SDK Feature Band with Global Feature Channels

**Owner** [marcpopMSFT](https://github.com/marcpopMSFT)

Today, SDK feature bands, SDK tools, and Visual Studio are coupled in ways that is complex to manage and confusing for users.  The challenge with changing this is users can receive new features or behavior changes without intentionally opting in. This is especially problematic for managed environments and source build partners that prioritize stability. We propose a single SDK feature band strategy, combined with a global feature channel setting that all SDK-loaded components honor. This setting has three states: baseline, stable, and preview. New behavior is always disabled in baseline, and initially enabled only in preview.

## Scenarios and User Experience

### Scenario 1: Source build partner requires maximum stability

A source build partner consumes servicing builds and needs strict behavioral predictability. They configure the global channel to baseline. New features and behavior changes remain off by default across SDK, MSBuild, Roslyn, NuGet, MSTest, and other participating components. Today, these partners stay on the N.0.1xx feature band in order to prioritize stability.

Outcome:

- Servicing updates can include security and reliability fixes.
- Existing behavior remains stable unless explicitly opted in.
- Partner validation costs and risk are reduced.
- Customers using source build SDKs can use the same SDK on multiple platforms with the same behavior with a simple environment variable

### Scenario 2: Visual Studio stable users get curated, stable behavior

A developer on Visual Studio stable channel receives SDK updates from the internal branch insertion path. They get behavior considered production-ready for the stable channel, while preview-only changes remain off. They get the latest runtime with all security fixes and any tooling security fixes as well.

Outcome:

- Stable channel receives deliberate feature lighting.
- Behavior aligns with VS stable expectations.
- Mid-cycle surprises are reduced.

### Scenario 3: Canary/Insider users validate upcoming behavior

A developer in VS canary receives the public 11.0.1xx SDK build with preview branding. Their global channel is preview, so new behavior can be enabled early for validation.

Outcome:

- Early adopter feedback happens before stable rollout.
- Preview-only changes do not leak into stable/baseline channels by default.
- Reduces the need to worry about the Insider behavior as it'll be the same as Canary

## Requirements

### Goals

- Define a single global configuration that is consumed across all .NET SDK-loaded components.
- Provide three explicit feature channels: baseline, stable, preview.
- Ensure new features or behavior changes default to off for baseline.
- Ensure new features or behavior changes light up first in preview.
- Enable controlled advancement from preview to stable based on explicit promotion.
- Preserve servicing safety: no accidental behavior lights-up in servicing trains.
- Align Visual Studio channel insertion with intended behavior channel semantics.
- Keep .NET optional workload acquisition aligned to latest stable builds across VS channels.
- Reduce branch management costs down to a single feature band sourced from two branches

### Non-Goals

- Redesigning component-specific feature-flag systems end-to-end.
- Guaranteeing identical behavior for non-participating tools outside the SDK composition.
- Replacing existing TFM-based compatibility controls.
- Defining every individual feature's rollout criteria in this proposal.
- Changing any rollout or versioning for optional workloads.
- Change how the feature rollout and experience of components loaded into Visual Studio are managed

## Stakeholders and Reviewers

- SDK: feature-band ownership and global configuration contract
- MSBuild: engine behavior gating and branch/codeflow model
- Roslyn: compiler/analyzer defaults and compatibility modes
- NuGet: restore/package behavior gating where applicable
- MSTest: test SDK/runtime behavior gating
- Source build partners: default channel semantics and validation guidance
- Visual Studio: channel insertion, branding, and defaults

## Design

### Global feature channel

Introduce one SDK-wide configuration value (`SdkFeatureChannel`) with exactly three allowed values:

- baseline
- stable
- preview

All participating components must query this value through a shared contract and use it to gate behavior changes.

Initial policy for any new feature or behavior change:

- baseline: off
- stable: off (unless explicitly promoted)
- preview: on (unless explicitly held back)

Promotion policy:

- A feature starts as preview-on, stable-off, baseline-off.
- Promotion to stable requires explicit approval and release notes in a planned update.
- Baseline remains off by default, with opt-in available for customers that explicitly choose it.
- Minimum preview soak time before promotion is two months.

### Configuration sources and precedence

The final value is provided through environment propagation so all child tools inherit the same channel. The SDK host sets the effective environment variable `DOTNET_SDK_FEATURE_CHANNEL` early in startup (first step in `Program.cs`) before invoking any tool components.

Default channel behavior is configured at SDK build time:

- Source-build distributions default to baseline.
- Microsoft SDK distributions default to stable.

Customers can override with the environment variable if they choose a different risk posture.

Precedence (highest to lowest):

1. Command-line explicit override (if introduced)
2. Environment variable explicit value
3. Host-computed default written by SDK startup
4. SDK build-time default

Global.json integration may be added later, but is not required for the initial design.

Open questions: 

- How do we ensure this behavior propogates from the SDK to the component tool?
- What should the behavior be inside of .NET components running inside of Visual Studio and how do we coordinate that.

### Cross-component contract

Each participating component defines:

- Behavior identifier (feature or breaking behavior ID)
- First enabled channel
- Promotion history (preview to stable)
- Override mechanism (if any)

To improve transparency and auditing, components should also publish a machine-readable manifest of gated behaviors and channel thresholds.

The SDK provides the channel context; each component owns implementation details while following a common policy.

### Visual Studio channel strategy

Proposed VS integration model:

- Public branch 11.0.1xx:
  - Preview branding
  - Insert into VS canary
  - Global channel default: preview

- Internal branch build:
  - Stable branding
  - Includes MSRC fixes and latest runtimes
  - Insert into VS stable and oobstable
  - Global channel default: stable
  - Preview behavior remains default-disabled; customer opt-in is allowed

- VS insiders:
  - Do not insert SDK directly
  - Consume preview flow from canary

### Optional workloads strategy

For optional .NET workloads:

- Always build workload artifacts from internal SDK version.
- Insert workload artifacts into all VS channels.

No additional compatibility contract is required for workloads in this proposal.

### MSBuild and similar component branch flow

For teams that need behavior alignment with VS stability (example: MSBuild engine):

- Stable branch codeflow from internal branch for stable alignment.
- Public flow for latest/preview innovation.
- Explicit merge and promotion policy to avoid accidental stable-channel behavior changes.

This pattern is recommended for all tooling teams that need stable-channel alignment.

For repositories with public-to-internal mirroring, branch topology must preserve stable and preview intent. A candidate model is:

- Public default branch mirrors into a public preview branch (source-preferred). Example: flow from 11.0.1xx to 11.0.1xx-featurepreviews.
- Promotion from internal preview branch to internal stable branch is controlled (target-protected, with explicit acceptance gates).
- Stable insertions are produced from the internal stable branch, while canary insertions are produced from preview-aligned flow.

This model needs validation with engineering systems owners before adoption.

### Servicing safety rules

In servicing trains:

- No new behavior should silently turn on for stable or baseline.
- Security and reliability fixes may ship, but behavior-changing defaults require explicit promotion in a planned update.
- Rollback options are: flip the feature off via hotfix when critical, or fix forward in the next servicing release.

## Q and A

### Why a three-state model instead of a binary preview toggle?

A binary model cannot represent baseline needs independently from mainstream stable users. Three states separate strict stability from normal stable adoption and preview validation.

## Risks and Unknowns

- Cross-component adoption lag: not all components may adopt `SdkFeatureChannel` and `DOTNET_SDK_FEATURE_CHANNEL` at the same pace, leading to inconsistent behavior.
- Environment propagation correctness: if the variable is not set early enough in SDK startup, child tools may observe incorrect defaults.
- Host/tooling interaction complexity: IDEs, build servers, and custom hosts may inject conflicting environment values.
- Configuration discoverability: users may not understand why behavior differs by channel without strong diagnostics and documentation.
- Manifest quality risk: machine-readable behavior manifests may drift from implementation if not validated in CI.
- Enforcement consistency risk across repos: without shared policy checks, teams may implement channel gating differently or incompletely.
- Promotion calibration risk: a two-month preview window may be too short or too long depending on feature blast radius.
- Servicing pressure: urgent fixes may tempt channel policy exceptions that undermine trust in stable/baseline promises.
- Branch/codeflow divergence: internal/public flow patterns can create merge debt and delayed fixes if governance is weak.
- Mirror topology risk for split public/internal versions: current one-way public to internal mirroring can accidentally blur stable versus preview intent.
- Cherry-pick debt risk: if internal stable and preview diverge, security and reliability fixes may require manual multi-branch propagation.
- Validation matrix risk: every participating repo may need baseline/stable/preview validation, increasing CI cost and latency.
- Diagnostics burden: support teams may need additional tooling to quickly determine the effective channel in customer environments.

### Enforcement approach

Open questions:

- Do we need an enfrocement bot enabled in every participating repo?
- Do we need the ability to dump all new features into a machine and human readable format for review and comparison against intent/docs/PRs?

Unknowns to validate during review:

- Whether `baseline` is intuitive enough for external customers and partners.
- Whether `DOTNET_SDK_FEATURE_CHANNEL` should be the only v1 control surface or paired with a CLI switch immediately.
- Whether all in-scope components can enforce gating with acceptable performance overhead.
- Whether existing per-tool feature flags can be harmonized without migration churn.
- Which minimal cross-repo policy checks are required for v1 to avoid silent non-compliance.
- Whether current public-to-internal mirror infrastructure can support a preview-branch intermediary with controlled internal stable promotion.
- Whether stable/canary insertion branches should be structurally separated in all participating repos.

### Does this replace existing feature flags?

No. Component-level flags can remain. This proposal adds a single, global compatibility channel that defines default behavior posture.

### Open questions to resolve

1. Whether `DOTNET_SDK_FEATURE_CHANNEL` should be the only v1 control surface or whether a CLI switch should also ship in v1.
2. Minimum schema for the machine-readable behavior manifest.
