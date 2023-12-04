# SDK Analysis Level Property and Usage


**Owner** (PM) [Chet Husk](https://github.com/baronfel) | (Engineering) [Daniel Plaisted](https://github.com/dsplaisted)

Today, users of the .NET SDK have a large degree of control over the way that the .NET SDK and the
tools bundled in it emit diagnostics (warnings and errors). This control is provided in part by a
series of MSBuild properties that control the severity levels of specific warnings, if certain messages
should be treated as diagnostics, even a coarse-grained way to set the entire analysis baseline for a project.

The default values for these properties are often driven by the Target Framework(s) chosen for a project, and
as a result users have developed an expectation of behaviors around changing the Target Framework of a project.
It's generally understood that when a TFM is changed, new kinds of analysis may be enabled, and as a result what
was a successful build may now have errors.

This cadence is predictable and repeatable for users - new TFMs are generally only introduced once a year - but
for tooling developers this cadence means that important changes can generally only be tied to a new TFM release.
New diagnostics can be introduced mid cycle, but they can only be enabled by default in a new TFM. Failure to adhere
to this pattern results in pain for our users, as they are often not in control of the versions of the SDK used
to build their code, often because their environment is managed by an external force like an IT department or
a build environment.

Some changes are not able to be logically tied to the TFM, however, and we have no toolset-level parallels
to the existing MSBuild Properties of `AnalysisLevel`, `WarningLevel`, `NoWarn`, etc. This means we have no way to introduce
changes that are activated just by installing a particular version of the SDK, and we have no clear way
to communicate intent to tools that naturally operate outside of the scope of a project - like NuGet at
the repo/solution level.

To fill this gap, and to provide users a way to simply and easily control the behaviors of the SDK and tools,
I propose that we:

* Add a new property called `SdkAnalysisLevel` to the base .NET SDK Targets, right at the beginning of target evaluation
* Set this property default value to be the `MAJOR.Minor.FeatureBand` of the current SDK (e.g. 7.0.100, 7.0.400, 8.0.100)
* Increment this value in line with the SDK’s actual version as it advances across releases
* Use this property to determine the default values of existing properties like `AnalysisLevel` and `WarningLevel` in the absence of any user-provided defaults
* Pass this value wholesale to tools like the compilers – where there is a complicated decision matrix for determining the effective verbosity of any given diagnostic

## Scenarios and User Experience

### Scenario 1: Jolene doesn't control her CI/CD environment

In this scenario Jolene is a developer on a team that uses a CI/CD environment that is
managed by an external team. The infrastructure team has decided that the version of the .NET SDK that
will be preinstalled on the environment will be 8.0.200, but this version introduced a new
warning that is treated as an error by default. Jolene's team doesn't have time to fix the
diagnostic until next month, so for now she instructs the build to behave as if it were
using the 8.0.100 SDK by setting the `SdkAnalysisLevel` property to `8.0.100` in a
`Directory.Build.props` file in her repo root:

```xml
<Project>
    <PropertyGroup>
        <SdkAnalysisLevel>8.0.100</SdkAnalysisLevel>
    </PropertyGroup>
</Project>
```

### Scenario 2: Marcus is on a GPO-managed device

Marcus is working on a small single-project .NET MAUI app, and his company manages his hardware via GPO.
On release day, the company pushed out Visual Studio updates to his team, and as a result his prior
feature band of `8.0.200` is no longer available - it's been replaced by `8.0.300`. He's not sure
about these warnings new warnings so he unblocks himself by setting `<SdkAnalysisLevel>8.0.200</SdkAnalysisLevel>`
in his project file to unblock builds until he has time to investigate.

---

In both of these scenarios, the user is *not in control* of their environment. They can control their code,
but they do not have the time to address the full set of problems introduced by an update. They use the new
property to request older behavior, for a _limited time_, until they can address the issues. In addition,
this single property was able to control the behavior of the SDK, the compilers, and any other tools that
have been onboarded to the new scheme, without having to look up, comment out, or add many  `NoWarn` properties
to their project files.

<!--
Provide examples of how a user would use your feature. Pick typical scenarios
first and more advanced scenarios later.

Ensure to include the "happy path" which covers what you expect will satisfy the
vast majority of your customer's needs. Then, go into more details and allow
covering more advanced scenarios. Well designed features will have a progressive
curve, meaning the effort is proportional to how advanced the scenario is. By
listing easy things first and more advanced scenarios later, you allow your
readers to follow this curve. That makes it easier to judge whether your feature
has the right balance.

Make sure your scenarios are written in such a way that they cover sensible end-
to-end scenarios for the customer. Often, your feature will only cover one
aspect of an end-to-end scenario, but your description should lead up to your
feature and (if it's not the end result) mention what the next steps are. This
allows readers to understand the larger picture and how your feature fits in.

If you design APIs or command line tools, ensure to include some sample code on
how your feature will be invoked. If you design UI, ensure to include some
mock-ups. Do not strive for completeness here -- the goal of this section isn't
to provide a specification but to give readers an impression of your feature and
the look & feel of it. Less is more.
-->

## Requirements

### Goals

<!--
Provide a bullet point list of aspects that your feature has to satisfy. This
includes functional and non-functional requirements. The goal is to define what
your feature has to deliver to be considered correct.

You should avoid splitting this into various product stages (like MVP, crawl,
walk, run) because that usually indicates that your proposal tries to cover too
much detail. Keep it high-level, but try to paint a picture of what done looks
like. The design section can establish an execution order.
-->

### Non-Goals

<!--
Provide a bullet point list of aspects that your feature does not need to do.
The goal of this section is to cover problems that people might think you're
trying to solve but deliberately would like to scope out. You'll likely add
bullets to this section based on early feedback and reviews where requirements
are brought that you need to scope out.
-->

## Stakeholders and Reviewers

<!--
We noticed that even in the cases where we have specs, we sometimes surprise key
stakeholders because we didn't pro-actively involve them in the initial reviews
and early design process.

Please take a moment and add a bullet point list of teams and individuals you
think should be involved in the design process and ensure they are involved
(which might mean being tagged on GitHub issues, invited to meetings, or sent
early drafts).
-->

## Design

<!--
This section will likely have various subheadings. The structure is completely
up to you and your engineering team. It doesn't need to be complete; the goal is
to provide enough information so that the engineering team can build the
feature.

If you're building an API, you should include the API surface, for example
assembly names, type names, method signatures etc. If you're building command
line tools, you likely want to list all commands and options. If you're building
UI, you likely want to show the screens and intended flow.

In many cases embedding the information here might not be viable because the
document format isn't text (for instance, because it's an Excel document or in a
PowerPoint deck). Add links here. Ideally, those documents live next to this
document.
-->

## Q & A

<!--
Features evolve and decisions are being made along the road. Add the question
as a subheading and provide the explanation for the decision below. This way,
you can easily link to specific questions.

When you find yourself having to explain something in a GitHub discussion or in
email, consider to update your proposal and link to your answer instead. This
way, you avoid having to explain the same thing over and over again.
-->
