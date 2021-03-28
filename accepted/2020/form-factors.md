# .NET Runtime Form Factors

**Owner** [Rich Lander](https://github.com/richlander) |
**Owner** [Jan Kotas](https://github.com/jkotas)

Starting in about 2014, we started a journey to open source and consolidate the .NET platform into a single code base, experience and brand. Those initial goals are now almost complete, with .NET 5.0. It is now time to re-evaluate where the industry and .NET users are heading next, and to make choices for .NET that are aligned. The .NET runtime form-factors are a frequent topic of feedback and discussions -- with current and potential users -- in terms of how we should improve and change fundamental .NET execution characteristics and expand the scenarios in which .NET can be used.

In the context of the .NET runtime, [form factor](https://en.wikipedia.org/wiki/Form_factor_(design)) describes the size and number of binaries that are required for applications, application dependencies, fundamental performance characteristics and how the application is invoked. It could also describe APIs that may or may not be supported due to fundamental form factor design choices. It does not describe operating system or chip architecture.

We have received feedback from many people about .NET and our design choices. Many users are happy with the current form factor options, but we also hear from users who cannot use .NET (even if they are fans of .NET languages) or who expect to reduce their use .NET due to lack of appropriate options. Going forward, we need to make intentional choices about our form-factor portfolio, and to communicate what we expect to deliver over the course of the next several years as a roadmap to allow users and the community to plan accordingly. This document is intended to address these topics and to start a new conversation with the community.

**Table of Contents**

- [Dominant .NET Runtime Form Factors](#dominant-net-runtime-form-factors)
  - [Global and General Purpose](#global-and-general-purpose)
  - [Self-Contained](#self-contained)
  - [Optimized for Size](#optimized-for-size)
  - [Specialized for Mobile Devices](#specialized-for-mobile-devices)
  - [Specialized for Games](#specialized-for-games)
- [Technical Roadmap](#technical-roadmap)
  - [Continuous Improvements](#continuous-improvements)
    - [Better for Containers](#better-for-containers)
    - [Libraries Optimized for Size](#libraries-optimized-for-size)
  - [Compatibility Analyzers and Mitigations](#compatibility-analyzers-and-mitigations)
  - [IL Linking](#il-linking)
  - [Single File](#single-file)
  - [Ahead-of-time (AOT) Compilation](#ahead-of-time-aot-compilation)
  - [Native AOT Form Factors](#native-aot-form-factors)
  - [Embedded Form Factors](#embedded-form-factors)
  - [WebAssembly](#webassembly)
  - [Community Supported Form Factors](#community-supported-form-factors)
- [Appendix](#appendix)
  - [Lessons Learned from CoreRT](#lessons-learned-from-corert)

## Dominant .NET Runtime Form Factors

The form factors described in this section are the dominant ones that exist today.

### Global and General Purpose

The globally installed runtime has been the most successful runtime form factor since the inception of .NET (meaning, the 2001 introduction of the .NET Framework). It fits the needs of typical server-side web and cloud applications, and a number of other important .NET workloads. We think of this form factor as general purpose, covering scenarios (just at Microsoft) as diverse as the Visual Studio IDE, Azure and Office 365 services, and the C# compiler.

This runtime form factor is relatively large (100s of MBs), supports all .NET features and is composed of many binaries (most small, some large). The runtime is often optimized for a specific platform (like Windows x64) using ahead-of-time (AOT) compilation. Typical applications are relatively small and contain only their own code and third-party dependencies. Applications depend on the presence of a specific version of the runtime (like .NET Framework 4.8 or .NET Core 3.1) for execution.

### Self-Contained

The globally installed runtime is not always desirable or even an option (e.g. due to admin privilege requirements). [Self-contained deployment](https://docs.microsoft.com/en-us/dotnet/core/deploying/#publish-self-contained) avoids the dependence on a global install by including the runtime with the application.

The simplest and most compatible way to compose self-contained applications is to include all runtime binaries next to the application, which is an option that has been available since .NET Core 1.0. This approach produces applications with a large disk footprint that are composed of many files. These characteristics present real challenges and negative perception. To address those problems, we introduced an option as part of .NET Core 2.0 that removes unused files. This approach significantly reduced disk footprint, but did not resolve perception issues. We introduced a single-file self-extractor as part of .NET Core 3.0, which in theory helped with customer perception, but wasn't the single-file solution that most users wanted. We are incrementally improving this solution as part of .NET 5.0 to better align with what people expect (a single file executable that loads dependencies as in-memory resources where possible).

The following feedback outlines customer-observed problems with the *single-file form* factor:

- [SingleFilePublish is quite unusable](https://github.com/dotnet/corert/issues/7200#issuecomment-564778685)
- [Current executing assembly location is wrong for single file published project](https://github.com/dotnet/runtime/issues/13531)
- [PublishSingleFile with Windows Service doesn't work with config-file](https://github.com/dotnet/runtime/issues/32735)

We expect that developers will continue to experience compatibility issues with the latest iteration of single-file form factor that we will deliver with .NET 5.0. This is rooted in the success and prevalence of the global runtime, which many libraries, tools and some of the .NET APIs have been specifically designed for. This situation makes it challenging to establish new form factors where compatibility issues, relative to the global runtime, can be observed and that hinder adoption. This topic is a common thread throughout this document.

### Optimized for Size

Binary size is a critical for many scenarios: client-side Blazor, Xamarin or lean micro-services.

[IL linking](https://github.com/mono/linker) is the key technology to significantly reduce the size of applications. It analyzes the application and removes unreachable code from the final binaries. Certain code patterns cannot always be reliably analyzed by the IL linker, which can cause optimized applications to fail at runtime.

The following feedback outlines customer-observed problems with the *optimized for size* form factor:

- [.NET Core 3.0 WPF app crashing after using illink](https://github.com/mono/linker/issues/595)
- [Linker removes needed runtime dependency](https://github.com/mono/linker/issues/365)
- [Add marking of types used in .config files](https://github.com/mono/linker/issues/712)
- [Could not load type 'System.Linq.ParallelEnumerable'](https://github.com/mono/linker/issues/804)
- [Third-party (DevExpress) DLLs not being trimmed](https://github.com/mono/linker/issues/772)

### Specialized for Mobile Devices

[Xamarin](https://dotnet.microsoft.com/apps/xamarin) and [.NET Native](https://docs.microsoft.com/dotnet/framework/net-native/) products are based on a runtime form factor specialized to fit mobile device constraints. They require a more specialized variant of the *self-contained* and *optimized for size* form factors, defined by more aggressive binary size optimizations, and restricting the set of supported APIs (limited by the mobile device environment). It comes with lower compatibility and it is not unusual for libraries to break in unexpected ways on this form-factor.

The following feedback outlines customer-observed problems with the *specialized for mobile devices* form factor:

- [System.Text.Json Serializer does not appear to work on Xamarin iOS](https://github.com/dotnet/runtime/issues/31326)
- [Websocket transport non functional in Xamarin](https://github.com/dotnet/aspnetcore/issues/13451)
- [ReactiveUI binding crash Xamarin.Android](https://github.com/dotnet/reactive/issues/1129)

### Specialized for Games

"C# powered by Mono" has been scripting engine of choice for a number of game engines. [Unity](https://unity.com/) - the world's most popular game engine - is scripted by C#, powered by an embedded heavily customized Mono runtime and their own IL2CPP runtime. It is used by millions of game developers, and billions of users (Unity games are pervasive on phones, tablets, desktop, and game consoles).

Unity's form factor is even more diverged and made less compatible in order to fit game performance and portability requirements. Unity chose to provide their own curated asset store for compatible components. For example, the Unity asset store has a [special version of Json.NET library](https://assetstore.unity.com/packages/tools/input-management/json-net-for-unity-11347) (#1 library on NuGet) that is compatible with Unity. The Unity asset store provides a more predictable experience to developers at the cost of the Unity ecosystem being disconnected from mainstream .NET. Looking forward, Unity plans to target even more compact and less compatible form-factors with [Project Tiny](https://forum.unity.com/threads/project-tiny-c-preview-0-15-3-available.688969/).

## Technical Roadmap

It is useful to think of our product opportunities in terms of sweet spots. We understand where our current form factor sweet spots are. There are more potential sweet spots today and there will be even more in future. This roadmap sets a direction for .NET 5.0 and beyond. It includes both improvements to existing and adding new form factors.

The following principles will allow us to reach different form factors as needed to satisfy users who are not happy with our existing offering or are not even able to use it.

- **Keep existing users happy:** We will continuously improve existing form factors to keep existing users happy. Any new form factors will be opt-in and aim to limit the cost to target them (*ideally*: "just use this new publish switch").
- **Deliver predictable experience:** We will promote designs that achieve working solutions with high confidence. We will avoid designs that require trial and error to find a working solution.
- **Be inclusive of industry trends:** We will enable community and corporate (other companies) efforts that are aligned with industry trends, independent of whether Microsoft intends to officially support their results in foreseeable future or ever.

### Continuous Improvements

Our primary focus continues to be on improving the existing successful form factors. We have large ongoing investments into improving AOT technologies and other performance features that apply to the dominant form factors. We will be mindful during design and implementation of these improvements about the constraints of less common form factors and avoid regressing them needlessly.

#### Better for Containers

Containers are an important deployment and execution environment for modern cloud applications. The runtime used for containers is the default global form-factor today. We are going to investigate creating a custom form-factor of the runtime optimized for containers.

#### Libraries Optimized for Size

The .NET runtime libraries have been often fine-tuned for raw speed of server workloads so far, without regards of code size impact. For example, the implementation of UTF-8 encoding grew to thousands of lines through vectorization and other performance optimizations, even though the algorithm can be expressed in a few hundred lines of code.

Going forward, we will consider binary size and speed to be of equal importance. The libraries' implementation will be tailored for aggressive trimming. We will create tooling for developers contributing to .NET libraries that will calculate and report the binary size impacts for a given PR. The tooling will not only track basic binary size regression but also additional checks like the introduction of cyclic dependencies. Where warranted, we will introduce separate implementations of key algorithms optimized for size and speed that the specific form factors include as appropriate.

### Compatibility Analyzers and Mitigations

We will introduce analyzers that report errors or warnings for code patterns that are not compatible with the specific form factors. The idea of building compatibility analyzers is not novel as similar compatibility analyzers already exist and we will strongly consider unifying them into a single user experience:

* [Platform Compatibility Analyzer](https://github.com/dotnet/platform-compat) -- identifies uses of APIs that are problematic on specific platforms
* [.NET Portability Analyzer](https://docs.microsoft.com/en-us/dotnet/standard/analyzers/portability-analyzer) -- identifies APIs missing on specific target .NET platforms

The analyzers will enable us to transition the current trial and error experience into a more predictable one. Once an application or a library builds successfully, without warnings, it will work during runtime. If there are no warnings and it does not work on a specific form factor due to unsupported functionality, we will treat it as a bug.

The analyzers will be complemented by guidance and product features that allow mitigation of the identified issues. We will work within the .NET team and across the .NET ecosystem to implement the mitigations to make specific vertical scenarios (e.g. client-side Blazor) work well. The larger ecosystem effort is going to be a journey spanning multiple release cycles. We do not expect the entire .NET ecosystem to implement mitigations for specific form factors, either because the scenario is not relevant for the specific package or because the amount of work and redesign required.

We are intentionally not trying to address the subsets of incompatible APIs by introducing new smaller TFMs that exclude these APIs. Building systems with walls leads to ecosystem fragmentation. We have done that in the past and we were not happy with the result, like for example with .NET Framework 4.0 and the "client profile" concept. Our focus is going to be on exposing a single set of broadly available and useful APIs defined by the [`net5` TFM](https://github.com/dotnet/designs/blob/master/accepted/2020/net5/net5.md) introduced with .NET 5.0 and future versions of the platform that expand it.

### IL Linking

The [IL linker](https://github.com/mono/linker) can generate optimized applications that fail at runtime, due to not being able to reliably analyze various problematic code patterns (largely centered on reflection use). It is not possible or desirable to simply invest more to improve the linker, as a strategy. Instead, build-time source generators will be the recommended mitigation for arbitrary reflection use.

The barrier for entry for building source generators is very high today. There is no standard solution and only a few libraries can afford to build a custom one. Razor pages, gRPC, WPF, XAML bindings and Data-Contract serializers each have their own custom build-time source generator solutions today. First-class Roslyn support for [source generators](https://github.com/dotnet/roslyn/blob/master/docs/features/source-generators.md) will lower the barrier of entry for components that need to generate source code.

The existing annotations that provide hints to the IL linker about what to keep or what is safe to link are going to stay, but their use is going to be less preferred as compared to source generators. They provide a less predictable experience that is hard for users to get right.

We will introduce a linker compatibility analyzer to detect code patterns that cannot be reliably analyzed by the linker. This will still be needed for libraries that do not or cannot adopt the source generation technique.

### Single File

The single-file form factor bundles all code into a single binary that the code is directly executed from. Single-file apps are easier to deploy and distribute. Many users have asked for this form factor. We will focus on producing traditional immutable single-file executables, and provide guidelines for application developers to target to successfully target the single-file form factor.

We will not attempt to re-create application virtualization solutions such as [BoxedApp](https://www.boxedapp.com/docs/index.html) or [`App-V`](https://docs.microsoft.com/en-us/windows/application-management/app-v/appv-for-windows) that can make unaware applications deployable as single-file executables by virtualizing the operating system. Application virtualization solutions are a unique domain of sophisticated technology, outside the realm of .NET runtime.

Single-file compatibility analyzers will detect the use of APIs that are known to cause single file deployments to break, such as `Assembly.LoadFile(path)` or `Assembly.Location`. New APIs, such as [API that returns path to the launching .exe path](https://github.com/dotnet/runtime/issues/30448), will be introduced to simplify mitigation of these issues.

Managed C++ is incompatible with the single file form factor and addressing this incompatibility in the tooling is prohibitively expensive. Projects written in managed C++ (such as part of WPF) would have to be rewritten in C# or other managed language to be single-file compatible.

### Ahead-of-time (AOT) Compilation

AOT compilation has been used by .NET runtimes to improve performance. Most .NET runtime form factors come with at least partial AOT compilation. .NET runtime without any AOT compilation would be too slow to startup for typical applications.

AOT compilation is not compatible with runtime code generation. Runtime code generation is a very powerful building block used to build many popular higher-level features (e.g. fast serializers). The runtime code generation comes with steep startup time costs and leans on the JIT or an interpreter for code execution. These characteristics are not desirable in environments that require fast startup time (e.g. device apps, lean microservices) or firm real-time guarantees (e.g. services with strict SLAs, games that must avoid dropped frames).

Source generators are going to be the preferred mitigation for AOT compatibility, same as for linker compatibility above.

Runtime code generation is often used as a workaround for slow reflection APIs provided by the runtime. We have [proposals for faster reflection APIs](https://github.com/dotnet/runtime/issues/23716) that would allow mitigating some of the dynamic code generation uses as well. This mitigation is less desirable since linker compatibility issues with reflection  would remain.

### Native AOT Form Factors

We have seen a surge of interest in statically linked binaries with minimal dependencies in recent years. This is a reversal of a multi-decade trend to deliver dependencies as shared libraries. The benefits to deployment and the ability to optimize a single app outweigh the benefits of sharing for certain types of workloads. The gain is most significant for workloads with a high number of deployed instances: cloud infrastructure, hyper-scale services, popular apps or games. The number of apps of this type is relatively small, but they are highly visible, impactful, and often cost a lot of money to operate (savings can be meaningful).

Emerging programming environments (e.g. Go, Rust) tend to be designed for this form factor. Established programming environments are catching up on this trend to be relevant for this highly visible segment, for example Java [Graal](https://www.graalvm.org/docs/reference-manual/native-image/), [Dart](https://dart.dev/tools/dart2native), [Kotlin/Native](https://kotlinlang.org/docs/reference/native-overview.html).

Startup time in tens of milliseconds, and several MBs binary size for a "Hello world" style application, and high performance and predictability (no JIT) are required to be relevant on this playing field. Different implementation strategies for at least some components are often necessary to achieve such characteristics. Today, neither CoreCLR nor Mono have characteristics to qualify for the Native AOT league. CoreRT is the only .NET technology available today that qualifies.

The native AOT support added to existing programming environments faces form-factor specific compatibility issues, not unlike the form-factor specific compatibility issues faced by .NET and discussed in this document. For example, see the list of compatibility issues in [Java Graal](https://github.com/oracle/graal/blob/master/substratevm/LIMITATIONS.md).

We have an opportunity to participate in this trend. The .NET community has demonstrated interest in this form factor by shipping apps using CoreRT, despite it never being released:

- [http://SoundFlow.org's macOS and Linux apps also proudly built on #CoreRT](https://twitter.com/SoundFlow_org/status/1200155734585094156)
- [Carrion used CoreRT on Linux/macOS/Windows](https://twitter.com/kroskiewicz/status/1286776766460309506) - [Steam](https://store.steampowered.com/app/953490/CARRION/)
- [Streets of Rage 4 successfully used CoreRT for the Steam release on Windows](https://github.com/dotnet/corert/issues/7200#issuecomment-624990602) - [Steam](https://store.steampowered.com/app/985890)

The ingredients of successful native AOT support in .NET are:

- **Clear guidance**: Be explicit about that this form factor is not a good fit for every application out there.
- **Predictable experience**: Implement analyzers and mitigations for all classes of compatibility issues listed above.
- **Code sharing**: Maximize code sharing between form-factors for our own engineering efficiency and ensure that the improvements are implemented across all form factors where possible. The native AOT binaries should be produced as build flavor of dotnet/runtime repo. The aspirational goal is to have less than 10,000 lines specific for the native AOT.

We will continue evolving the native AOT as an experimental project, with a new structure that is more transparent and better aligned with the unified dotnet/runtime repo.

### Embedded Form Factors

The stock .NET runtime does not always have desired characteristics. Runtime embedding solves this problem by allowing heavy customization for how the runtime binaries are built or which runtime features are included.

The Mono runtime has been friendly to embedding by having a first class build from source with a large number of build configurations (with/without JIT, different types of GCs, customized builds of the libraries). This capability has been essential to enable Xamarin to successfully target a broad range of mobile devices from watches to phones.

We will recreate these capabilities in the unified dotnet/runtime repo and work towards a common set of lightweight embedding APIs between CoreCLR and Mono runtimes.

Embedding of the runtime typically goes hand in hand with the need to expose .NET APIs in C. This capability is available as [preview](https://docs.microsoft.com/xamarin/tools/dotnet-embedding/release-notes/preview/0.4) or [experimental](https://github.com/dotnet/corert/tree/master/samples/NativeLibrary) for .NET, but comes as standard in other environments (e.g. Swift, Rust or Julia).

### WebAssembly

WebAssembly has enough unique characteristics and limitations that it deserves to be treated as its own form factor, and should not be thought of as just yet another Unix variant. The WebAssembly limitations such as lack of threads or low-level exception handling mechanism create a slew of compatibility issues that will need detection and mitigations.

One of main differentiators of WebAssembly (when hosted in a browser) is a lazy loading model. Our historical model was to think about .NET runtime as a single entity. To better leverage the web distribution model, we need to think about application packaging in a new way and at a different layer to produce a runtime that can load faster on the web.

We should engage in the Wasm community to influence the designs. Today, we have a choice between the interpreter (slow) and static compiled code (very large downloads) or a blend of those. We will want to add an option to support JITing code to Wasm on the Wasm client. There are challenges in this space, because Wasm as it exists today is not exactly friendly to JITing (thunking is very expensive, as it has to go through JavaScript for any unresolved methods).

### Community Supported Form Factors

The .NET runtime project has been welcoming to contributions of ports to new platforms and architectures, as long as the work has strong support in the community, meets project engineering standards and avoids unnecessary code duplication and maintenance burden. We will extend an invitation to support new form factors under similar conditions, even for form factors that Microsoft does not intend to officially support in the foreseeable future or ever. It is important to us to avoid runtime forks in the .NET ecosystem. We will enable the community supported ports and form factors to be successful by adding minimal coverage in the CI system to avoid breaking them needlessly.

## Appendix

### Lessons Learned from CoreRT

We strived for a clean redesign of the whole .NET runtime, first as the Redhawk project, then .NET Native, and finally as the open source CoreRT project. It borrowed forked copies of selected components, but re-implemented many components (and often as C# rather than C++). We have a good idea what the north star for a cleanly designed .NET runtime architecture looks like thanks to these projects. It allowed us to see that it is possible to build .NET runtime form-factors that have performance characteristics on par with statically compiled environments such as C++ or Golang.

At one point, the goal of .NET Native and CoreRT projects was to replace the established .NET runtime implementation in its entirety. We even had a project for that called Rover -- "Runtime over RedHawk". This goal was proven to be unrealistic. Re-architecting half of the .NET features built over 20 years (with a large team) to run on the nice clean runtime is prohibitively expensive. Executing this endeavor would require slowing down the investment into the mainstream .NET runtime to a trickle. The vast majority of customers would not see any material improvements for number of years. We consider that direction unacceptable.

Many core components were reimplemented in significantly better way in .NET Native and CoreRT projects. We have brought over many of these improvements to the mainstream .NET runtime, essentially undoing the fork between CoreCLR/CoreRT one file at a time. This effort delivered a lot of customer visible value.  For example, look for CoreRT references in Performance Improvements in [.NET Core 2.1](https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-core-2-1/) and [.NET Core 3.0](https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-core-3-0/) blog posts. There are too many to mention all of them here and it is still an ongoing effort. This also explains, in part, how .NET Core performance so quickly improved relative to the .NET Framework.
