# .NET SDK build types and Architecture targeting

**Owner** [Rich Lander](https://github.com/richlander)

Developer platforms tend to fall into two categories, in terms of the apps they produce and support: architecture-neutral, or architecture-specific. .NET is potentially an outlier in that it attempts to support both of these architecture modes equally well. From a runtime and libraries standpoint, we've satisfied this goal. From a CLI standpoint, we have not. This proposal aims to improve the CLI experience of producing both architecture-neutral and architecture-specific apps.

As a comparison, node.js apps are architecture-neutral (one build of the app can run on multiple architectures) by default, while golang apps are (always) architecture-specific (one build of the app is required for each architecture). There are plenty of other examples of development platforms for both categories.

This topic has always been important, but not front-and-center for most developers. The introduction of [x64 emulation on Arm64 operating systems](https://github.com/dotnet/designs/pull/217) makes architecture targeting a mainline scenario, and is the motivating reason to make the proposed changes.

The proposal aims to resolve the outstanding problems with architecture targeting, not solely the aspects required to satisfy the x64 emulation scenario. However, the proposal is prioritized in terms of the x64 emulation scenario, as described near the end of the document.

## Context

Today, architecture targeting shows up in the following SDK experiences and concepts:

* Deployment models: framework-dependent vs self-contained.
* RID targeting: RID-neutral vs RID-specific.
* Various CLI verbs and their capabilities.

Across these topic areas, we find that the .NET SDK lacks a coherent model for architecture targeting. The following statements provide a brief description of the SDK UX today. Feel free to skip this section. It is primarily here to demonstrate the state of the current model.

### Good

* The SDK produces framework-dependent apps by default across all verbs. These apps rely on a separately installed runtime on the machine of a compatible version.

### Bad

* The SDK produces portable apps by default across all verbs. These apps  can run on multiple operating systems and architectures.
* At the same time, the SDK generates a native "apphost" executable for the app that matches the RID of the SDK used. This isn't symmetric with the app being portable, as the apphost will only work in one of the environments that the app will. There is no experience to reason about that (which single environment does the apphost support? which environments does the underlying app support?)
* The presence of the apphost for a portable app might be useful for development, but it is confusing in a deployed environment if the development and production RIDs do not match. If they do match, then the app isn't as optimized as it could be.
* Portable apps will be bigger if the app depends on packages with architecture-specific dependencies, since the app will (likely) contain the native assets for multiple architectures (that's one of three key aspects of what makes it portable). The only reason to build portable apps is to accommodate multi-architecture packages.
* Portable apps and RID-specific builds [do not place native assets in the same location](https://github.com/dotnet/sdk/issues/11754). That should be configurable and is a [confusing behavior](https://github.com/dotnet/sdk/issues/3903).
* It is possible to make an app portable for one operating system, such as for Windows x86, x64, and Arm64, but that isn't enabled.
* Portable apps -- for the apphost -- don't support the universal binaries feature of Windows or macOS, which reduces their utility, particularly for client apps.
* Some users want to target a specific architecture and/or operating system. The SDK produces a self-contained app by default when you do this. Many users (likely most) do not want that.
* For example, we see much higher pulls of our container images for framework-dependent apps as opposed to self-contained ones, which tells us that the framework-dependent modality is very popular.
* There are important scenarios for targeting a specific architecture with framework-dependent apps. It's a logical leap for the SDK to switch to the self-contained deployment model as a consequence of targeting a specific RID since RID targeting for framework-dependent apps is a legitimate and important scenario.
* There are performance benefits for framework-dependent apps being RID-specific.

### Ugly

* `dotnet build` and `dotnet publish` do not have the same capabilities (in terms of CLI arguments) for architecture targeting. Both verbs enable specifying a RID, while only `dotnet publish` enables changing the deployment model, between self-contained (the default when `-r` is used) and framework-dependent. This isn't technically true, because you can rely on MSBuild properties for this purpose. It is, however, effectively true.

## Desired build types

Today, we talk about self-contained and framework-dependent as the two [deployment types](https://docs.microsoft.com/dotnet/core/deploying/). This model is misleading for two different reasons: the set isn't complete, and it's oriented exclusively on deployment.

The following are the full set of "build types" (I'm changing the term) that can be used to greater or lesser degrees across development, testing, and production scenarios.

* Portable framework-dependent app
* RID-specific framework-dependent app
* RID-specific self-contained app

Note: The first build type is the default today.

This list makes these build types look like a progression, however it is not. It's just the cross-product of two characteristics (RID targeting and deployment model), with one variant removed ("portable self-contained app") because it isn't technically possible.

Portable apps are the least well-defined app type, even though it's the current default. On one hand, portable apps are for advanced scenarios that require a single set of assets to be run in multiple production environments. That's not common.

On the other hand, portable apps enable developers to reliably publish apps built on Windows to run on Linux, for example, without any special knowledge. In this case, the "multiple environments" are dev and production. For example, apps that depends on a local database, like [SQLite](https://www.nuget.org/packages/SQLite/) or [Microsoft.Data.SqlClient](https://www.nuget.org/packages/Microsoft.Data.SqlClient/) will run into this scenario, since local databases almost always contain RID-specific assets.

With portable apps, the developer doesn't need to worry about the fact that these database packages include multiple RID implementations. With RID-specific apps, they would.

However, there is more to the story. For example, the `SQLite` package contains (at the time of writing) seven RID-specific implementations (see the following list). They range from `800k` to `4MB`. That means a portable app that uses SQLite will contain all seven copies of the database engine even though it only ever relies on one, and need to accept the significant size increase that comes with that.

* `linux-x64`
* `osx-x64`
* `win10-arm`
* `win10-x64`
* `win10-x86`
* `win7-x64`
* `win7-x86`

See the appendix for an analysis of portable app size metrics.

Portable apps are easier to deploy to production for PaaS scenarios, like Azure Websites. As soon as developers adopt containers, it makes more sense to adopt our [documented RID-specific container patterns](https://github.com/dotnet/dotnet-docker/tree/main/samples/aspnetapp), which results in optimized apps.

The reason to keep portable apps as the default is that it is easier for non-container web developers that target a different operating system than the one they use locally for development. Portable apps are significantly worse for all other scenarios, particularly client.

In short, portable apps should be an opt-in scenario. The user should make a decision on the extra app size and alternate folder structure that they will experience.

Some people may argue that portable apps benefit new developers since they are simpler. That's potentially true, if they are deploying to a PaaS service that runs another operating system or uses a different CPU type. On the other hand, portable apps have the worst metrics (they can be significantly larger) such that their utility for the new developer experience is questionable. They are not our "best foot forward".

## Proposal for behavior changes

There are multiple proposed changes.

### Enable RID-specific as default build type

The default build type should be changed to the RID-specific framework-dependent model. This approach has the following benefits:

- Apps are smaller (no unusable RID assets).
- Apps likely build faster on slow drives (copy less assets to `bin` folder).
- Apps startup faster (no need to do RID parsing/matching on startup).
- All app assets are symmetric (the apphost matches other app assets and the runtime environment).
- Simpler folder layout (there is none).

The most significant beneficiary of this change would be containerized ASP.NET Core apps with RID-specific dependencies. We'd also get to delete a bunch of [architecture-specific Dockerfiles](https://github.com/dotnet/dotnet-docker/tree/main/samples/aspnetapp). A number of those Dockerfiles only exist due to the current architectural targeting CLI model.

This change would resolve the request for [dotnet-publish with --use-current-rid or --infer-rid option](https://github.com/dotnet/sdk/issues/10449) without needing any additional syntax. This request aligns with the challenge we have with Dockerfiles.

Portable framework-dependent apps are the default scenario today. As already stated, it's not a good default choice because it produces larger apps with niche benefits.

### Enable gesture to build portable apps

If rid-specific apps become the default, then we need a new gesture for portable apps. There are two (non-mutually-exclusive) models for that:

- Use the existing `any` RID to mean portable.
- Expose a new CLI arg and MSBuild property for portable apps, like  `--portable` and `<Portable>true</Portable>`.

Recommendation: Start with using just the RID. Wait for feedback to enable more polished experiences to validate that portable is an important scenario.

There is also an existing `win` RID. It could be used in a similar way as `any` but limited to just Windows family RIDs. ASP.NET Core developers deploying and/or developing apps on some combination of Windows x86, x64, and Arm64 may find that scenario useful. We should not proactively develop that scenario, but wait for feedback.

In addition, the SDK would no longer generate apphost EXEs for portable apps by default. There already is an opt-in model for EXEs (`<UseAppHost>true</UseAppHost>`). Portable app users can rely on that.

The rationale for making apphost EXEs opt-in is that they are a non-portable asset, creating a strange asymmetry in an otherwise portable app. Perhaps, we should make it easy for developers to generate an apphost only for debug builds to aid the development experience. That said, if developers want a better development experience, they could just as easily build RID-specific apps (the proposed default) for development and then build a portable app with no EXE for production where an EXE is presumably not needed.

Note: Portable apps are not a meaningful option for client apps since almost all client apps require an executable launcher, and there is only ever one generated. Portable apps are only meaningful for commandline apps.

Note: In theory, we could consider an option to create multiple apphosts for different RIDs, either of different names or in different directories. Again, this capability would need to be opt-in.

### Rationalize RID folders for RID-specific and portable apps

There have been some issues filed (see appendix) that suggest that managed libraries are not resilient to RID-specific libraries changing location as a result of changing build type (portable to RID-specific). This would also be true for single file deployment builds.

We could enable (as an option) creating `runtimes` folders for RID-specific apps to make the folder layout the same across deployment types. Doing that would put single file at a significant disadvantage. We'd rather encourage the ecosystem to support both folder layouts. If we had to choose one of these deployments types to disadvantage, it would be portable (with the `runtimes` folders), not RID-specific, which is the likely the opposite of what developers are tuning for today.

We need to invest more in docs and tools to the end of clear and actionable guidance.

### Use SDK RID as default RID

RID-specific apps should default to the SDK RID as an implicit RID, and no longer require a RID to be specified. The SDK already does this for the apphost for portable apps. This proposal is a straightforward extension of that behavior.

This means that the following commands would produce assets in terms of the implicit RID, such as with the following commands.

- `dotnet publish --self-contained`
- `dotnet publish /p:PublishSingleFile=true`

Note: As already suggested, this change would make Dockerfiles MUCH easier to write. Needing to specify RIDs is probably the worst part of using .NET with Docker.

Specifying a RID should have no effect on the build type/deployment model. It should simply refine the existing model. This change largely follows from using the SDK RID as the implicit default for all types of RID-specific apps. When a RID is specified, it is substituted for the implicit RID.

This means that the following commands would produce a RID-specific framework-dependent app:

- `dotnet build -r linux-x64`
- `dotnet publish -r linux-x64 /p:PublishReadyToRun=true`

Developer would add `--self-contained` or `--self-contained true` to create a self-contained app, like with the following command.

- `dotnet build -r linux-x64 --self-contained`

### Enable all build types for all (appropriate) verbs

There are three build types that were defined earlier. All three are possible to create with `dotnet publish` but the same is not true for `dotnet build`. `dotnet build` does not enable creating a RID-specific framework-dependent app (w/o relying on MSBuild properties). Going forward, we need to ensure that both `build` and `publish` have the same capabilities for all three build types.

This behavior largely follows from the previous sections, but good to call out.

Up until now, `dotnet build` and `dotnet publish` have had slightly different behaviors and capabilities with respect to the topics discussed in this document. Going forward, they need to have the same behavior. There is some discussion of deprecating `dotnet publish`. That's out of scope of this doc. That said, a first step to deprecating `dotnet publish` would be rationalizing the behavior differences. Making `dotnet build` and `dotnet publish` expose the same model and provide the same behavior for RID, architecture and deployment targeting would be a great approach to that.

Some people have asked that `dotnet publish` produces `-c release` apps by default. Great idea but out of scope for this document.

### Enable RID targeting for all (appropriate) verbs

Today, the CLI assumes that the implicit RID is the correct one to use for apphost creation, but is [wrong in some scenarios](https://github.com/dotnet/sdk/issues/17241) and does not provide an override.

The primary scenario is enabling the native-architecture SDK to target an emulated architecture. For example, `dotnet run`, `dotnet test`, and `dotnet tool install` should all accept a RID to influence apphost creation, such as with the following commands:

- `dotnet run -r osx-x64 --`.
- `dotnet test -r win-x64`
- `dotnet tool install -r win-x64`

Note: `dotnet watch` is effectively a wrapper over `dotnet run` and `dotnet test` so would follow the same model as they do.

Where appropriate, the SDK should encode a different implicit RID, such as `osx-x64` when targeting .NET Core 3.1 on Apple Silicon macOS machines with the Arm64 .NET 6 SDK. The SDK should understand that .NET Core 3.1 only supports x64 and not Arm64 for macOS. The x64 SDK should provide the same behavior in reverse.

Tool installation with `dotnet tool install` is a particularly problematic scenario. At the time .NET 6 is released, one can expect that close to 100% of .NET tools (packaged via NuGet) will target a version prior to .NET 6 and therefore be incapable on running on Arm64 (at least on macOS). However, when installed, the SDK will generate an apphost that matches the SDK RID. As a result, the tool will consistently fail to launch (since the required runtime will be missing). The SDK needs to apply the same implicit RID rules for .NET tools that it will apply for `dotnet build`. In addition, `dotnet tool install` should enable explicit RID targeting.

Note: One can think of .NET tools as being distributed as portable apps with no apphost, with the most favorable apphost being provided just-in-time at installation. It's a pretty good model and aligns with the spirit of the overall design.

### Enable shorthand RID targeting syntax

RIDs are a pain to use on a regular basis. Particularly for emulation, it would be very useful to have a shorthand model for just specifying the architecture. The operating system remains constant.

Imagine building a .NET 6 app on a Mac Arm64 machine, day in, day out. Every hour or two or three, you like to run a quick test run on x64. It would be really nice to type the following (or some analogue to it):

```bash
dotnet test -a x64
```

Instead of:

```bash
dotnet test -r osx-x64
```

It would also be useful (and symmetrical) to have a shorthand for operating system. The primarily advantage with this addition is that users could specify architecture and operating system separately, and never need to learn the .NET RID concept. This is very similar to the [golang model](https://golang.org/cmd/go/#hdr-Compile_and_run_Go_program).

None, either or both shorthand values could be specified and both, one or neither would use implicit values, respectively, as demonstrated in the following examples.

- `dotnet build`
- `dotnet build -a x64`
- `dotnet build --os Linux`
- `dotnet build -a arm64 --os osx`

Assuming a Windows Arm64 SDK, the following RIDs would be used in these examples:

- `win-arm64`
- `win-x64`
- `linux-arm64`
- `osx-arm64`

Note: We should also determine if we can switch from `osx` to `macos` as the RID-suffix for macOS. That's out of scope of this proposal.

The eventual goal is to adopt framework-dependent as the default for RID-specific apps. We should adopt that model for this shorthand RID syntax. That would (A) give us the model we want from the start (for new syntax), (B) avoid a breaking change in the next release, and (C) provide a more straightforward breaking-change-avoidance syntax for framework-dependent apps than `--no-self-contained`.

That means that the following commands would be equivalent:

- `dotnet build -a x64 --os win`
- `dotnet build -r win-x64 --no-self-contained`

This asymmetry largely locks us into making the breaking change to `-r`. If we don't, the architecture targeting system will become even more confusing than it already is.

Specifying both a RID and the short-hand syntax would be an error, for example with the following commands:

- `dotnet build -r win-x64 -a arm64`
- `dotnet build -r linux-arm64 --os win -a x64`

The shorthand syntax is intended as a CLI and not MSBuild concept. For scenarios that today require specifying RIDs in project file, the intent is that users will continue to specify RIDs. If we find that there is a need for a shorthand RID syntax in project files, then we can consider extending this syntax to MSBuild.

## Implementation timeline

This proposal includes multiple breaking changes. They are impactful enough that we need to ensure a few key characteristics:

- Breaks are introduced in an early preview of a new .NET version.
- Breaks can be avoided by using existing gestures in a previous .NET version.
- The breaks are configurable in some way.

That leaves us with the following plan:

- Add high-value additive changes for x64 emulation in .NET 6.
- Add gestures that enable avoiding the .NET 7 breaking changes.
- Make rest of changes, including all breaking changes, in .NET 7 Preview 1 or 2.
- Publish plan before .NET 6 releases.

The second point -- adding gestures that avoid future breaks -- is a key part of the plan. We also need to ensure that we update documentation and blogs to ensure that the users are aware of the changes that they should make when using .NET 6 in preparation for .NET 7 and later versions.

### .NET 6

The following changes should be included in .NET 6, motivated by the x64 emulation scenario. They are all additive and non-breaking.

- Enable shorthand RID syntax. Add to the following verbs (priority order):
   - `build`
   - `publish`
   - `tool install`
   - `test`
   - `run`
   - `watch run`
   - `watch test`
- Do NOT enable the `-r` RID syntax on these same verbs. Doing so would cause a breaking change in .NET 7, which seems like a unforgivable mistake.
- Enable implicit RID where there is one known choice:
   - The macOS Arm64 SDK should treat pre .NET 6 versions as X64 by default.
   - The Windows Arm64 SDK should treat pre .NET 5 versions as X64 by default.
- Parity syntax between `dotnet build` and `dotnet publish` for pivoting between self-contained and framework-dependent modalities, for example:
   - `dotnet build -r win-x64 --self-contained`
   - `dotnet build -r win-x64 --self-contained true`
   - `dotnet build -r win-x64 --self-contained false`
   - `dotnet build -r win-x64 --no-self-contained`
- Add warning when using `-r` without a `--self-contained` or `--no-self-contained`. The warning is for .NET 6+ apps only.

The addition of the shorthand RID syntax and the parity syntax between `build` and `publish` will provide a satisfactory set of gestures to enable migration with .NET 6 to a non-breaking syntax with respect to .NET 7. In particular, users need to migrate any uses or `-r` to always be accompanied by one of the `--self-contained` or `--no-self-contained` switches.

### .NET 7

The following changes should be included in an early .NET 7 preview, motivated by x64 emulation, containers and the need for a coherent architecture and RID targeting model. They are a mix of additive and breaking.

1. Expose `-r` on all appropriate verbs (including the ones that expose the RID shorthand syntax).
1. [Breaking change] Change `-r` to be framework-dependent by default, requiring the use of `--self-contained` to opt into producing a self-contained app.
1. [Breaking change] Switch to RID-specific instead of portable as the default build type.
1. [Breaking change] Use the SDK RID as the implicit RID for any operation that requires a RID.

Note: The last change isn't really breaking. It's a follow-on change of the actual break to switch from portable to RID-specific as the default build type. That said, it should be communicated as a breaking change.

The breaking changes will be configurable:

- By default, they will only affect app projects that target .NET 7 or higher.
- There will be an MSBuild property and environment variable that enables this behavior for either all projects or no projects.

The following settings will be exposed:

- `DefaultDeploymentTypeRIDSpecific`
   - `frameworkdependent` (for all TFMs)
   - `selfcontained` (for all TFMs)
   - Note: Disables change #2.
- `DefaultBuildType`
   - `ridspecific` (for all TFMs)
   - `portable` (for all TFMs)
   - Note: disables change #3, which in turn disables change #4.
- `EnableDotnet7RIDTargetingForAllVersions`
   - `false` (for all TFMs)
   - `true` (for all TFMs)
   - Note: Enables changes #2, #3 and #4 for all versions.

Note: These config names are not great. They are placeholders for better names.

### .NET 8

The following changes should be included in an early .NET 8 preview.

- Remove support for the three config options added in .NET 7, just just above.
- The breaking changes will be enabled for all versions, including .NET 6.

## Summary

This document started with a description of the good, bad, and ugly of the current experience. The appendix includes several issues that demonstrate user challenges for RID targeting and related topics. This section is intended to provide a summary of the improvements that this design delivers, in contrast to the descriptions of the current design.

This design would deliver the following benefits, in terms of the CLI.

- Users do not need to learn about RIDs, but only operating system and architecture concepts. RIDs would become an advanced scenario.
- Specifying a RID with `-r` would no longer implicitly change the deployment type (to self-contained). Instead, the deployment type must be explicitly changed. As a result, the `--no-self-contained` switch would no longer be needed and could be deprecated.
- `build` and `publish` verbs would have greater parity, resolving perhaps the most critical lack of asymmetry.
- Several verbs would enable architecture and RID targeting, enabling much more expressivity.

This design would deliver the following benefits, in terms of build artifacts.

- The default build type would be much simpler with no confusing characteristics to explain (e.g. what `runtimes` folders are for, where the apphost executable can be used and where not).
- Apps would be optimized for size and startup by default.
- It would be easier to describe what the portable build type is for, with it no longer being the default and no longer having an executable by default.

And, of course, the design delivers first-class and straightforward support for the x64 emulation scenario based on general CLI primitives and concepts. It also streamlines the building .NET apps with the Dockerfile syntax, for building containers.

Looking forward, there are other design changes we should separately consider:

- Enable multi-pass build for RIDs, which would establish greater parity between TFMs and RIDs and `TargetFrameworks` and `RuntimeIdentifiers`.
- As a consequence, enable RID-based build across ProjectReference, which would establish greater parity between PackageReference and ProjectReference.
- Enable building RID-based NuGet packages with the same ease as multi-TFM packages. This experience depends on the previous two design changes.
- Provide warnings when package assets are not available for the current RID, avoiding runtime errors as the primary indication of an issue.

## Appendix -- dotnet/sdk issues

The following dotnet/sdk issues demonstrate various challenges related to this topic, some of which go beyond the scope of this proposal but are nonetheless relevant and related.

### RID-targeting

The following issues are resolved by this proposal.

- [How to dotnet publish RID to the current OS (closest matching RID)](https://github.com/dotnet/sdk/issues/11282)
- [dotnet-publish with --use-current-rid or --infer-rid option](https://github.com/dotnet/sdk/issues/10449)

The following issues need more information:

- [Dotnet Publish with RID doesn't bring the native dll in original folder structure](https://github.com/dotnet/sdk/issues/11754)
- [Publishing with win-x64 RID doesn't include runtime-specific Nuget libraries](https://github.com/dotnet/sdk/issues/10665)
- [Cannot find dependencies when executing2 an assembly published by dotnet publish](https://github.com/dotnet/sdk/issues/3903)
- [dotnet publish from non-Windows does not copy required native files](https://github.com/dotnet/sdk/issues/3875)
- [dotnet restore no longer supports multiple runtimes](https://github.com/dotnet/sdk/issues/3843)

The following issue are related but unresolved by this proposal.

- [Architecture-specific folders](https://github.com/dotnet/sdk/issues/16381)
- [RuntimeIdentifier does not propagate to dependency projects when multi-targeting TFMs](https://github.com/dotnet/sdk/issues/10625)
- [How to consume native dependencies via ProjectReference?](https://github.com/dotnet/sdk/issues/10575)
- [Android's implementation of multiple $(RuntimeIdentifiers)](https://github.com/dotnet/sdk/issues/14359)
- [Consider guards for projects missing RID-specific assets](https://github.com/dotnet/sdk/issues/11206)
- [NETSDK1083 error when providing multiple RIDs in .NET Core 3](https://github.com/dotnet/sdk/issues/11426)

### Build type

The following issues are resolved by this proposal.

- [Publishing as framework dependent creates self-contained instead when not explicitly disabled](https://github.com/dotnet/sdk/issues/4230)
- [dotnet publish won't publish self contained exe without a RuntimeIdentifier. I am passing -r in the command.](https://github.com/dotnet/sdk/issues/10566)
- [Cannot publish self-contained worker project referencing another](https://github.com/dotnet/sdk/issues/10902)

### General

The following issues need more information:

- [Regression with package restore: RID Graph not respected when selecting native library from nuget package](https://github.com/dotnet/sdk/issues/4195)
- [PlatformTarget is not set when Platform is "arm64" (MSBuild)](https://github.com/dotnet/sdk/issues/15434)

## Appendix -- Portable App analysis

We can compare the size of apps when building as portable versus framework-dependent.

Let's start with [SQLite](https://www.nuget.org/packages/SQLite/), as discussed earlier in this document. Here's how that shows up for a minimal console app.

```bash
root@2e4e994c001a:/app# dotnet new console
root@2e4e994c001a:/app# dotnet build      
Time Elapsed 00:00:00.54
root@2e4e994c001a:/app# du -ch bin/Debug/net6.0/
12K	bin/Debug/net6.0/ref
116K	bin/Debug/net6.0/
116K	total
root@2e4e994c001a:/app# dotnet add package SQLite
root@2e4e994c001a:/app# rm -r bin obj
root@2e4e994c001a:/app# dotnet build
Time Elapsed 00:00:00.62
root@2e4e994c001a:/app# du -ch bin/Debug/net6.0/
12K	bin/Debug/net6.0/ref
1.5M	bin/Debug/net6.0/runtimes/osx-x64/native
1.5M	bin/Debug/net6.0/runtimes/osx-x64
4.1M	bin/Debug/net6.0/runtimes/linux-x64/native
4.1M	bin/Debug/net6.0/runtimes/linux-x64
1.7M	bin/Debug/net6.0/runtimes/win7-x64/native
1.7M	bin/Debug/net6.0/runtimes/win7-x64
812K	bin/Debug/net6.0/runtimes/win7-x86/native
816K	bin/Debug/net6.0/runtimes/win7-x86
8.0M	bin/Debug/net6.0/runtimes
8.1M	bin/Debug/net6.0/
8.1M	total
```

Let's build the same app RID-specific for Linux x64.

```bash
root@2e4e994c001a:/app# rm -r bin
root@2e4e994c001a:/app# dotnet build -r linux-x64 /p:SelfContained=false
Time Elapsed 00:00:00.58
root@2e4e994c001a:/app# du -ch bin/Debug/net6.0/
8.0K	bin/Debug/net6.0/linux-x64/ref
4.2M	bin/Debug/net6.0/linux-x64
4.2M	bin/Debug/net6.0/
4.2M	total
```

The app is half the size and has more targeted content and a much simpler folder structure.

Now, let's try targeting Windows x64.

```bash
root@2e4e994c001a:/app# dotnet build -r win7-x64 /p:SelfContained=false
Time Elapsed 00:00:00.69
root@2e4e994c001a:/app# du -ch bin/Debug/net6.0/
8.0K	bin/Debug/net6.0/win7-x64/ref
1.8M	bin/Debug/net6.0/win7-x64
1.8M	bin/Debug/net6.0/
1.8M	total
```

Wow! The app dropped from 8MB to less than 2MB.

Let's try the same thing with an [EF Core sample](https://github.com/dotnet/EntityFramework.Docs/tree/main/samples/core/GetStarted).

```bash
root@2e4e994c001a:~# git clone https://github.com/dotnet/EntityFramework.Docs
root@2e4e994c001a:~# cd EntityFramework.Docs/samples/core/GetStarted
root@2e4e994c001a:~/EntityFramework.Docs/samples/core/GetStarted# cat EFGetStarted.csproj | grep "<PackageRef"
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="5.0.2" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Tools" Version="5.0.2">
root@2e4e994c001a:~/EntityFramework.Docs/samples/core/GetStarted# rm -rf bin objroot@2e4e994c001a:~/EntityFramework.Docs/samples/core/GetStarted# dotnet build
Time Elapsed 00:00:00.91
root@2e4e994c001a:~/EntityFramework.Docs/samples/core/GetStarted# du -ch bin/Debug/net5.0/
12K	bin/Debug/net5.0/ref
1.6M	bin/Debug/net5.0/runtimes/osx-x64/native
1.6M	bin/Debug/net5.0/runtimes/osx-x64
1.1M	bin/Debug/net5.0/runtimes/linux-musl-x64/native
1.1M	bin/Debug/net5.0/runtimes/linux-musl-x64
1.1M	bin/Debug/net5.0/runtimes/linux-arm64/native
1.1M	bin/Debug/net5.0/runtimes/linux-arm64
1.2M	bin/Debug/net5.0/runtimes/linux-x64/native
1.2M	bin/Debug/net5.0/runtimes/linux-x64
1.1M	bin/Debug/net5.0/runtimes/win-arm/native
1.1M	bin/Debug/net5.0/runtimes/win-arm
1.4M	bin/Debug/net5.0/runtimes/linux-mips64/native
1.4M	bin/Debug/net5.0/runtimes/linux-mips64
756K	bin/Debug/net5.0/runtimes/linux-arm/native
760K	bin/Debug/net5.0/runtimes/linux-arm
1.6M	bin/Debug/net5.0/runtimes/win-x64/native
1.6M	bin/Debug/net5.0/runtimes/win-x64
1.1M	bin/Debug/net5.0/runtimes/linux-armel/native
1.1M	bin/Debug/net5.0/runtimes/linux-armel
1.1M	bin/Debug/net5.0/runtimes/alpine-x64/native
1.1M	bin/Debug/net5.0/runtimes/alpine-x64
1.2M	bin/Debug/net5.0/runtimes/linux-x86/native
1.2M	bin/Debug/net5.0/runtimes/linux-x86
1.3M	bin/Debug/net5.0/runtimes/win-arm64/native
1.4M	bin/Debug/net5.0/runtimes/win-arm64
1.2M	bin/Debug/net5.0/runtimes/win-x86/native
1.2M	bin/Debug/net5.0/runtimes/win-x86
16M	bin/Debug/net5.0/runtimes
20M	bin/Debug/net5.0/
20M	total
root@2e4e994c001a:~/EntityFramework.Docs/samples/core/GetStarted# rm -rf bin obj
root@2e4e994c001a:~/EntityFramework.Docs/samples/core/GetStarted# dotnet build -r linux-x64 /p:SelfContained=false
Time Elapsed 00:00:01.28
root@2e4e994c001a:~/EntityFramework.Docs/samples/core/GetStarted# du -ch bin/Debug/net5.0/
12K	bin/Debug/net5.0/linux-x64/ref
5.9M	bin/Debug/net5.0/linux-x64
5.9M	bin/Debug/net5.0/
5.9M	total
```

In this case, the difference between portable and RID-specific builds is even more marked and further advocates for adopting RID-specific as the default build type.
