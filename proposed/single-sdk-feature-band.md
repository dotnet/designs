# Single SDK Feature Band with Global Feature Channels

**Owner** [marcpopMSFT](https://github.com/marcpopMSFT)

Today, SDK feature bands, SDK tools, and Visual Studio are coupled in ways that is complex to manage and confusing for users.  The challenge with changing this is users can receive new features or behavior changes without intentionally opting in. This is especially problematic for managed environments and source build partners that prioritize stability. We propose a single SDK feature band strategy, combined with a global feature channel setting that all SDK-loaded components honor. This setting has three states: baseline, stable, and preview. New behavior or new features should never show up in baseline. New features should initially be enabled only in preview. Stable is for features that have gone through some preview validation and make include some behavior changes or new warnings.

## Scenarios and User Experience

### Scenario 1: Enterprise Source build partner requires maximum stability

Some source build partners consume servicing builds and needs strict behavioral predictability. They configure the global channel to baseline. New features and behavior changes remain off by default across SDK, MSBuild, Roslyn, NuGet, MSTest, and other participating components. Today, these partners stay on the N.0.1xx feature band in order to prioritize stability.

Outcome:

- Servicing updates can include security and reliability fixes.
- Existing behavior remains stable unless explicitly opted in.
- Partner validation costs and risk are reduced.
- Customers using source build SDKs can use the same SDK on multiple platforms with the same behavior with a simple environment variable
- Source build partners can choose additional features through the stable channel and source build customers can even choose to try out previews which has been a long term ask.

### Scenario 2: Visual Studio stable users get curated, stable behavior

A developer on Visual Studio stable channel receives SDK updates from the internal branch insertion path. They get behavior considered production-ready for the stable channel, while preview-only changes remain off. They get the latest runtime with all security fixes and any tooling security fixes as well.

Outcome:

- Stable channel receives deliberate feature lighting.
- Behavior aligns with VS stable expectations.
- Mid-cycle surprises are reduced.

### Scenario 3: Visual Studio Canary/Insider users validate upcoming behavior

A developer in VS canary receives the public 11.0.1xx SDK build with preview branding. Their global channel is preview, so new behavior can be enabled early for validation.

Outcome:

- Early adopter feedback happens before stable rollout.
- Preview-only changes do not leak into stable/baseline channels by default.
- Reduces the need to worry about the Insider behavior as it'll be the same as Canary

### Scenario 4: VS Code user gets flexible feature control

A developer using VS Code and the .NET SDK can choose their preferred feature channel to match their development needs. They default to stable for a balance of new features and stability. If they want maximum stability, they can set the environment variable to baseline. If they want to try cutting-edge features early, they can opt into preview.

Outcome:

- Stable channel is a sensible default for most developers.
- Power users have direct control via environment variable configuration.
- Independent of IDE choice; behavior is consistent across any editor using the SDK.
- Easy to experiment with new features without waiting for broader SDK releases.
- With each major release, VSCode will update the SDK installed to a new band resulting in a new baseline.

## Alternatives

### Monthly SDK minor releases

Instead of using feature channels within a single SDK feature band, the SDK could adopt monthly minor releases. For example, the .NET 12 GA SDK would be `12.0.0`, followed by `12.1.0`, `12.2.0`, and so on in subsequent months. Each monthly SDK would replace the previous monthly release as the actively supported feature release and contain the latest available runtime, including applicable security and reliability fixes.

Monthly feature bands were also considered, but versions such as `12.0.100` followed by `12.0.200` or `12.0.1000` continue the feature-band concept and do not communicate the monthly release ordering as directly.

#### Release and branch model

Development would primarily occur in a long-lived preview branch. It could be as simple as a public release/12.x branch with all public codeflow to this branch. Once a month, the preview content intended for release would be promoted to a long-lived stable branch which couild just be internal/release/12.x which has internal codeflow of the runtime version. Each month we'd update the branding in each branch separately.

The original `12.0.x` release line would remain available as the stability-oriented release for customers that want security and reliability fixes without new features (specifically, enterprise source build partners). This produces at least three logical release paths:

- The original `12.0.x` stability line.
- The latest stable monthly minor release.
- The next monthly minor release in preview.

Monthly promotion introduces a risk of drift between preview and stable. We could have codeflow from stable tooling to the stable branch but that would cause mirror conflicts or alternatively, we could require all stable tooling changes be made in the internal/release branch. Any security fixes would have to be ported on release day to the preview branch.

#### Servicing and security fixes

Only the latest monthly minor and the original `12.0.x` release line would be serviced. Superseded monthly minors would remain installable but would not receive further fixes. The `12.0.x` line would receive security and reliability fixes but no new features, providing a predictable option for enterprise source-build and other stability-focused customers.

The servicing policy needs to distinguish among:

- Runtime security and reliability updates included in an SDK release.
- SDK or tooling hotfixes that do not require a runtime release.
- Workload updates released between monthly SDK releases.
- Fixes required by Visual Studio or source-build consumers that remain on a different SDK release path.

Security and reliability fixes need an explicit propagation path to the original stability line, the current stable monthly release, the next preview, Visual Studio's supported SDK builds, and any dedicated source-build branch. Stable tooling fixes would need to flow through the VMR or an equivalent internal release process rather than flowing only into the preview branch.

The policy must also define what happens when a runtime security release is delayed, skipped, or released out of band. A runtime release should not necessarily require a new SDK minor, but supported SDK lines still need a way to acquire that runtime update.

#### SDK and runtime version relationship

SDK and runtime versions would intentionally be allowed to differ. For example, an SDK hotfix `12.2.1` might contain runtime `12.0.7`, and a new monthly SDK minor preview would contain the most recently available runtime patch rather than a runtime released on the same day. Release notes would need to make this relationship clear.

The SDK version components would have the following meaning:

- Major: the annual .NET release.
- Minor: a monthly SDK feature release.
- Patch: an SDK/tooling hotfix or a release used to carry an updated workload set between monthly feature releases.
- Prerelease label: builds of the next monthly minor before promotion to stable.

Using the patch component for both SDK hotfixes and workload updates preserves a three-part version but means patch numbers identify release order rather than a particular type of change. Release tooling and documentation must not assume that every patch increment has the same cause.

#### Visual Studio integration

The preview SDK would continue to flow to Visual Studio Canary for validation. The stable monthly SDK would be available to supported Visual Studio channels according to their insertion and servicing policies. Visual Studio Insiders could continue to consume the preview flow through Canary rather than requiring a separate SDK build.


#### Source-build contract

Enterprise source-build partners that require maximum stability could remain on `12.0.x` and receive security and reliability fixes without taking monthly feature releases. Other source-build consumers could move to newer monthly minors through their normal distribution update process.

Unlike the feature-channel proposal, a customer remaining on `12.0.x` could not opt into selected newer SDK features without moving to a newer monthly SDK. This loses a benefit of the feature-channel model for customers that must acquire the SDK from a distribution feed but want newer features. On the other hand, release boundaries provide stronger isolation than relying on every component to gate all behavior correctly. This is particularly important for changes such as analyzer fixes that may introduce new warnings even when the change is otherwise considered a correctness fix.

#### Workloads, installers, and SDK selection

Existing workload and SDK infrastructure encodes feature-band identity in workload manifests, workload sets, package IDs, version validation, and selection logic. This model would need to define the monthly minor as the new compatibility boundary and update those systems accordingly.

Workload set versions would generally match the SDK version. For example, the workload set for `12.2.0` would use that version, while SDK hotfixes and mid-month workload updates would consume successive patch numbers such as `12.2.1` and `12.2.2`. The release process must define ordering when both an SDK hotfix and a workload update are required in the same month and ensure that an SDK can resolve a compatible workload set after either kind of update.

Existing `global.json` version selection and roll-forward behavior would allow customers to remain on `12.0.x`, select a particular monthly SDK, or move to newer monthly releases. The exact behavior of each roll-forward policy across minor releases needs to be specified, including whether existing feature-band-oriented policies retain their current names and how they map to monthly minors.

Installer and acquisition behavior also needs an explicit contract:

- Whether monthly minors install side by side or replace the previous monthly SDK.
- How MSI related-product and upgrade relationships are represented.
- How uninstalling a monthly SDK affects shared runtimes and workload content.
- How Microsoft Update targets standalone SDKs on the original stability line versus later monthly minors.
- How SDKs installed by Visual Studio are detected and serviced separately from standalone SDKs.

#### Transition

Adopting this model at the beginning of a major release is simpler than changing versioning after customers and tooling have already consumed feature-band versions. One transition option is to retain the existing feature-band model through .NET 11, including a final feature band if needed, and begin monthly minor releases with .NET 12.

The transition plan would need to identify and update automation that parses SDK feature bands, including workload tooling, `global.json` handling, installers, Microsoft Update, release pipelines, support tooling, and documentation. Compatibility behavior is required for tools that encounter both the older feature-band scheme and the monthly-minor scheme.

#### Benefits and drawbacks

The primary benefits of this approach are:

- Monthly versions communicate release order directly and align SDK tooling delivery with a monthly Visual Studio cadence.
- Features can reach stable customers monthly rather than waiting for a quarterly feature band.
- Release boundaries provide stronger isolation for stability-focused customers.
- The SDK does not depend on every loaded component implementing, naming, testing, documenting, and honoring a shared feature-channel setting correctly.
- Tooling teams can use normal version and branch boundaries instead of maintaining a growing set of indefinitely supported feature gates.

The primary drawbacks are:

- Source-build customers remaining on the original stability line cannot opt into selected newer features.
- Workload, installer, SDK selection, release, and servicing infrastructure require significant changes.
- Preview-to-stable promotion and fix propagation can create branch drift and merge debt.
- Visual Studio and source-build may still require separate servicing paths, limiting the expected branch reduction.
- SDK and runtime versions may differ in ways that require clear diagnostics and documentation.
- Monthly minors increase the number of SDK versions customers and support teams encounter.

This alternative should be selected if the operational cost of monthly promotion, servicing, installer, workload, and version-selection changes is lower than the governance and compatibility risk of requiring every SDK-loaded component to implement feature channels correctly. Before selecting it, the design needs validated plans for security-fix propagation, Visual Studio servicing, source-build support, workload versioning, installer upgrade behavior, and the transition from feature bands.

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
- Stable from major version N (eg .NET 11) will automatically be included in baseline of N+1 (eg .NET 12)

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

global.json integration may be added later, but is not required for the initial design.

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
  - Insert into VS stable and last servicing version. VS typically releases a new GA and a final release of the last minor version on the same day and we would update both.
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

We will not ship two SDK/tooling versions in parallel for the same feature band.

Rationale:

- Command-line usage of `dotnet` inside VS canary/insiders has not produced enough critical feedback to justify the operational complexity of dual-version shipping.

Updated branch and insertion policy for MSBuild and similar tooling teams:

- Preview tooling ships from `main`.
- Those preview bits flow into VS canary and the .NET vNext SDK.
- Released SDKs ship tooling only from stable tooling branches.
- No separate preview-branded/stable-branded split for the same released feature band.

This keeps preview validation where it is most useful (canary and vNext) while reducing branch/codeflow complexity and release risk for shipped SDKs.

Risk:

This potentially limits the amount of feedback tooling teams would get on new features before promoting them to stable as they'd only be available in vNext or VS/msbuild.exe.

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

- How do we enforce this in every contributing repo?
  - PR review guidance as a minimum
  - What other enforcement can we create?
- Do we need the ability to dump all new features into a machine and human readable format for review and comparison against intent/docs/PRs?
  - It'd be great to be able to tag issues/PRs on which release they are in as well.

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
3. Is there a change to the versioning schema we could do for the SDK? We could change to match runtime versioning but we sometimes have SDK hotfixes so it might be confusing to release an 11.0.108 that has 11.0.7 runtime in it.