# Inbox Source Generators

**PM** [Immo Landwerth](https://github.com/terrajobst) |
**Dev** [Eric StJohn](https://github.com/ericstj)

Roslyn source generators allow for component developers to provide functionality that runs in the context of compilation, analyzes a user's code, and can generate additional code to add to the compilation.  Compelling scenarios for source generators involve moving dynamic runtime code to be specialized and statically emitted by the compiler: specializing serializer code for a target type, pre-compiling regular expressions at design time, pre-computing composition graphs for DI systems, and many more.  This technique was possible before but was rather clunky and had to be done outside the compiler, so its use was minimal and not part of the default experience.  Roslyn source generators solve this and as a result we wish to develop many source generators which are part of the default experience for .NET applications.  Source generators present a new type of challenge for a design time component: they need to be part of the toolchain but are tied to specific components within a target framework, and often re-implement or share the functionality of those components.  We need to define how we ship these source generators and make them available to developers.  We need to define the lifecycle for these source generators and how it is tied to the framework component it extends.

## Scenarios and User Experience

A developer can use source generators for framework API without making any changes to their project.  Creating a new project should be sufficient.

A developer can use source generators for components in NuGet packages by referencing the NuGet package for that component.  

When a component ships both in a NuGet package and a framework, the developer has a consistent experience with the source generator as they do with the library itself.  If the package referenced is newer than the framework the source generator from the package will be used as is already the case for the library in that package.  Similarly, if the framework is newer than the NuGet package the framework's source generator will be used as is already the case for the library in the framework.

A developer can receive updates to a source generator through normal channels by which they already obtain updates today.

A developer can update their version of Visual Studio or .NET SDK and have confidence that new versions of the toolchain will not impact their applications which target older frameworks, outside of known and published servicing updates.

A developer has confidence that their library using inbox source generators will continue to work in the same way a library that does not use inbox source generators works on future versions of .NET.

## Requirements

### Goals

- Enable consumption of source generators for components in shared frameworks
- Address conflict resolution between source generators in shared frameworks and NuGet packages
- Define servicing process for shared framework source generators
- Establish guidelines building and versioning shared framework source generators
- Describe compatibility requirements for source generators.

### Non-Goals

- Define a process for components not in shared frameworks to contribute source generators.
- Define any changes to the source generator architecture.
- Define any implementation details of source generators or source generator tests.
- Define a process around internal or non-shipping source generators
- Define guidelines applicable to all source generators.

## Stakeholders and Reviewers

- ASP.NET - also building shared framework source generators.
- .NET Libraries team - building multiple shared framework source generators.
- Mono team - interested in providing source generators.
- .NET Runtime team - interested in providing source generators.
- Roslyn team - responsible for source generator infrastructure.
- .NET SDK team - responsible for resolving shared frameworks and doing conflict resolution with content.
- NuGet team - FYI only, no changes requested

## Design

### Framework provided source generators

Framework provided source generators should have a mechanism to be exposed and versioned along side the framework and library which they extend.  These source generators will be passed to the compiler when that framework is targeted.  These source generators will not be used when the framework is not targeted.  For example, ASP.NET specific source generators will not be used when ASP.NET is not referenced.  The version of the source generators specific to the TargetFramework version will be used.  For example, when targeting .NET 6.0 the source generators specific to net6.0 will be used and not those for .NET 7.0, even when using the SDK that ships with .NET 7.0.    

Source generators and analyzers are represented and communicated in the same manner to the compiler, as analyzer dlls using the `-analyzer` switch.  Though this document is motivated by describing the process for source generators it will work in the same manner for analyzers.  In some places we may refer to the assemblies containing source generators as analyzers due to the way they are understood by the compiler, package manager, and SDK.

To facilitate this, we will package source generators inside [reference packs](https://github.com/dotnet/designs/blob/main/accepted/2019/targeting-packs-and-runtime-packs.md).  We will use the standard [NuGet conventions for describing analyzers](https://docs.microsoft.com/en-us/nuget/guides/analyzers-conventions) with respect to the location in the package, however the SDK will not probe this path.  All source generators and their metadata will be listed in FrameworkList.xml, similar to references.  Analyzers will be added as `File` elements with `Type="Analyzer"` (constrasting to `Type="Managed"` which is currently used for references) and optionally `Language="cs|vb"`.  Omitting the `Language` attribute means the analyzer applies to all languages.  The .NET SDK will be responsible for locating the appropriate source generators from the refpack, based on the project's `$(Language)` and creating `@(Analyzer)` items as part of the `ResolveTargetingPackAssets` task.  These items will be conflict-resolved with other `@(Analyzer)` sources as described in the following section.  When viewed from the IDE it should be clear that the source of the `Analyzer` is the framework reference, similarly to how it is clear that the source of NuGet package analyzers come from packaages.

Existing public functionality which controls the behavior of shared-framework references should also apply to source generators.  `$(DisableImplicitFrameworkReferences)` should disable the creation of `@(Analyzer)` items.  Any other property which might disable the creation of `@(Reference)` items from the ref-pack should also disable the creation of `@(Analyzer)` items.  Additionally, a new property should be introduced to disable the creation of `@(Analyzer)` items: `$(DisableFrameworkReferenceAnalyzers)`.  No mechanism will be provided to select individual `@(Analyzer)` items, just as there is no mechanism to select individual `@(Reference)` items.

Alternative designs are listed in the [Q & A](#Q%20&%20A) section below.

### Source generator conflict resolution

Source generators and analyzers are passed to the compiler as `@(Analyzer)` items.  These items may come from NuGet packages, be built into the SDK targets, be directly defined by the user via `ProjectReference` or raw `Analyzer` items, or be provided by the new framework mechanism described in this document.  All should be considered for conflict resolution based on the same constraint.

The .NET SDK already does conflict resolution for other asset types: references and copy-local runtime assets.  It should do the same for source generators, or more generally Analyzers.  Analyzers need not have "platform manifest" inputs for conflict resolution since all analyzers will be available during build, unlike runtime files which may not be available.  Comparison for the sake of conflict resolution can consider the same assembly and file version rules as reference files.  When finding two or more source generators with the same name conflict resolution will select the generator with higher assembly version, then higher file version, then that which is in the framework.  This parallels the same rules followed by reference assemblies.  The SDK may choose to optimize this comparison as it does for reference assemblies, by including a data file that includes analyzer metadata.

Source generator conflict resolution should run before compile, but after all NuGet package assets have been evaluated.  Specific sequencing, public/private target naming, extensibility will not be specified here but should follow best practice as determined by the SDK team. 

### Source generator versioning

Source generators should be strongly named and update assembly version with every public stable release (GA and servicing).  This ensures that the publicly shipped source generators can be loaded side-by-side in the same process and follows our versioning guidelines for components which ship and must run on .NET Framework.  Source generators need not update assembly version during pre-release but should update file version to facilitate well behaved distribution by installers.  In general source generators need to follow all the same guidelines as other code that ships out of dotnet/runtime and runs outside the shared framework.

### Source generator compatibility concerns

Source generators should only generate code which uses public API in framework components they extend.  This public API already needs to meet the strict binary compatibility guarantees as framework API.  As such libraries containing source-generated code will be binary-compatible with future releases, just as if the user wrote this code directly.

Source generators should not generate code which uses public API with "reserved" or "undocumented" functionality in attempts to avoid public API guarantees.  Libraries paired with source generators need to provide the same guarantees as libraries alone do today.

Source generators should avoid emitting public API in a user's projects.  Such API can create a compatibility contract in that user's library which needs to be maintained.  If a source generator produces public API in a user's project by design, that public API must be strictly maintained following the same rules as framework components across versions of the source generator.  Care should be taken to the public API which is generated and should be reviewed by the Framework Design Council.  A similar constraint applies to internal API: users will have source that expects to interact with the generated internal API and the user will expect that code to continue to compile between versions.

Generated code should not be generated in System namespaces, it should prefer the user's namespace.  Generated code should be triple-slashed commented and be free from compiler warnings.  Generated code should avoid name collisions with user code and provide affordances for resolving name conflicts.  As much as possible generated code should meet the quality standards of .NET library code, using nullable annotations, meeting framework design guidelines, naming guidelines, etc.  It should feel like a natural extension of the library.

### Deterministic failure

The absence of a required source generator from code that requires it should produce a clear diagnosable error message from the compiler, like the error from a missing reference.  A codebase that was written to make use of a source-generator should never compile and silently omit the source generated code.  For example: if a source generator uses an attribute to activate the generator and then generates extension methods with overloads that are automatically preferred, the source generator should define the attribute in the source generator, not the framework library.  This will ensure that the project fails to compile if the source generator is absent.

### Source generator servicing

Source generators contribute significant code to user assemblies and that code may have issues.  We have very little control over changing customer assemblies once they have consumed a source generator.  We cannot and should not attempt to change the behavior of customer's assemblies (eg: hot patch) to fix issues.  Should an issue be found we should fix it in the source generator and ship a new version, just as we would if we found an issue with compiler.  Customers desiring this fix must proactively acquire the new code-generator, typically through an updated SDK which carries the ref pack, rebuild their code, and update their production applications.

Due to the limited servicing agility for code-generated code, we should be very careful about the complexity of the code which is generated.  Where possible we should limit generated code to "glue code", only that which must be specific to the user's assembly or types.  We should try to put more complex code inside the framework or library itself, even if it means exposing public API that is specifically for source generated code to call.

Inbox source generators will be serviced in their framework-specific location by shipping a new version of the ref-pack.  Should a bug exist in a source generator, we will evaluate the bug against the bar for each framework in which it exists and patch them independently.  In this way the servicing process for these source generators will match the .NET runtime.

**Open issue:** should we do anything to make it easier to identify assemblies which might need an update?  We can consider some case studies.  

If a specific compiler version has a code-gen bug it is not clear from the final assembly if that bug is present, unless you directly inspect the IL/bytecode for the issue.  The compiler version is embedded in the PDB of the assembly which could be used to identify assemblies with the issue.  There is no notification at design time or runtime of any issue with the compiler, the user needs to subscribe to updates to understand when an update is needed.

If a runtime assembly has a bug, we have multiple avenues of detection.  We could examine the assembly metadata files contained in the application layout, including for self-contained applications.  The runtime will also write "breadcrumb" files on a machine when code is executed that is annotated for servicing that help identify that a specific assembly or package version has been executed on that machine.  Today we use these breadcrumbs to help target updates to a machine.  Those updates can install in a well-known location and be preferred over the application's copy.

For means of detection we have:
1. Manual
2. PDB indicator: source generator assembly version, file version, hash
3. Assembly attribute in consuming user `[assembly: AssemblyMetadata("CodeGenerator", "System.Foo.Generator, 5.0.0+cf258a14b70ad9069470a108f13765e0e5988f51")`
3. Attribute on generated types/members: `[GeneratedCode("System.Foo.Generator", "5.0.0+cf258a14b70ad9069470a108f13765e0e5988f51")`
4. Runtime breadcrumbs

For means of update we have:
1. No notification, user needs to subscribe to servicing updates.
2. Design time notification.
3. Killbit in the runtime to prevent execution of user assemblies that contain vulnerable source generated code.
4. ~~Automatically update user assembly though a hotpatch applied by runtime or JIT~~

We will need to better understand what customers' expectations are around servicing to decide if we should do any work on these axes.  My initial inclination is to only have a PDB indicator and require users to subscribe to updates.

### Functionality concerns for source generators

Though the following are not specific to inbox source generators, these concerns are worth reviewing to describe how they apply to framework specific source generators.

Source generator assemblies execute in the compiler process and must target .NET Standard in order run on both .NET Framework and .NET Core. Source generators should support generating source for all target frameworks supported by their companion library.  In the case of inbox source generators discussed in this document this need only be the target framework the generator ships in.  In the case of a NuGet package provided source generator, this should be all frameworks supported by package.

Source generators should limit their dependencies.  There is no scheme for encoding framework-specific nor runtime-specific implementations of dependencies or source generators based on the runtime environment so source generators should avoid any dependencies on assemblies which need to differ by framework or runtime.  Source generators may depend on packages which are known dependencies of the compiler and should depend on versions less than or equal to that provided by the compiler and should not include that dependency in their package.

Source generators should not rely on referencing nor executing framework types they wish to extend.  Despite inbox source generators being coupled to a specific framework version, source generators cannot assume they will be running on that framework.  They may be running on a newer, older, or completely different framework.  For example, API added to help a source generator do its job may not be present when running on .NET Framework or the .NET Core version the compiler is running on.  There is no type-equivalence guaranteed between types loaded in the compiler process and those of the user code that is under analysis.  For example, a source generator cannot instantiate attribute instances in the compiler process from user references to those attributes in source.  Should a source generator need to identify type usage it will need to examine user code references by name or resolve types in the user's references to ensure valid equivalence checks.  Source sharing should be considered as an alternative for cases where source generators need to execute runtime library helper code when generating source.  For example, serializer code which needs to examine the shape of user types can be shared between the runtime library and source generator.  Common abstractions can be made over metadata to allow the same code to handle different sources of metadata (source vs reflection).

Source generators should localize any diagnostic messages displayed to the user.  This can be done using localized resource satellite assemblies.  These assemblies should be distributed with the source generator assembly. 

## Q & A

### Why not just deliver source generators in the SDK same as roslyn analyzers?

Doing so would require source generators to carefully version and test across all framework versions.  This would increase testing cost for source generators and increase risk when shipping updates to source generators since they would apply to past, stable target frameworks.  Conceptually source generators are lifting a substantial amount of runtime functionality out of a library and executing that at design time. This code is directly included in the user's application and contributes to runtime behavior.

### Can't the analyzer handle delivering different behavior per library version?

We could implement the framework/library specific behavior in the analyzer itself, like the compiler does with `$(LangVersion)`.  We could use an input like the project's `$(TargetFramework)`, metadata in the reference asssembly like a source generator version experessed in an attribute `AssemblyMetadataAttribute("UseMySourceGeneratorVersion", "1.2")`, or even do API probing to determine which features are supported by the library.  Whichever input chosen would need to directly correlate with the beavior of the library selection (conflict resolution), ruling out `TargetFramework`, and be stable across servicing updates to provide determinsm to existing projects.  These solutions introduce complexity into the source generator and risk for the end user.  They would require us to build and test analyzers across all older targets: we would need to test the latest source generator across all previous versions of the library.  They also introduce risk since they could impact a customer's application when using the latest SDK but targeting an older, LTS framework, since those would use the newest analyzer undergoing churn that is not scrutinized through our normal servicing process.

### Inplace servicing updates are good enough for the compiler, why not source generators?

The compiler has a very stable 1:1 relationship between a user's source and the code it generates. As such the user can control the output of the compiler through their own usage.  The compiler's contribution of complexity to the user's assembly is very small and highly specified.
 
Contrast this to source generators which can take a very small amount of user source to produce a large amount of generated code.  This large amount of generated code contains significant complexity which is loosely specified.  Its behavior is specific to the component it extends.  It's likely that we'd need to bugfix this code over time and change it as we develop features in the components it extends.  It is also likely that we will learn new techniques and improvments to source generators over time, increasing the changes for bugfixes and features.

Users expect stability for projects which do not change, for example a project targeting an LTS release. It is much more difficult to ensure this stability when we need to significantly components which contribute complexity to that project.

### Isn't this a lot of servicing if we find a bug?

Although this seems like more changes than if behavior was in the compiler-distributed analyzers it is not.  If we cared enough about a bug to service an analyzer that runs on a stable .NET version we would also want to service the SDK for that stable .NET Version: including the same number of changes and branches.  In fact, this strategy *reduces* servicing in the case where a bug only exists in a single version of source generator.

What this strategy does do is reduce risk.  We eliminate the channel where the latest SDK would contain changes that impacted users' projects targeting older .NET versions.  That is a good thing as it reduces the need to test those configurations and ensures we can guarantee the quality and stability of our stable .NET releases.

### Is it bad to have multiple versions of a source generator possibly loaded by a repository?

A repository may have projects targeting multiple frameworks.  These frameworks may contain different versions of the same source generator.  When building the repository this may require the same compiler instance to load multiple copies of a source generator.  The same compiler would not load multiple copies of source generators which ship in a non-framework specific location.  This should work and be supported based on source generator versioning.

### Should all inbox source generators be tied to a shared framework?

No, some source generators might be independent of a target framework. One example of such: a source generator may not contain functionality, but may instead build some convenience API on top of existing public API that is unlikely to ever change.  These could be considered stable enough to place in a non-framework-specific location like the SDK's analyzer folder.  Source generators which choose to ship this way should be highly specified and will need to support backwards compatibility and test on all frameworks.

### Should only source generators be shipped this way?  What about analyzers and code-fixes?

Nothing is preventing analyzers and code fixes from shipping in the same manner.  If it is desirable for a such a component to ship with the framework rather than the compiler then that component may ship in this manner.  We should only do this if we have a good reason; stable analyzers that are language extensions or tied to stable public API should ship non-framework-specific with the SDK as they do today.  Analyzers that need to validate source generator usage, or analyzers that are closely tied to runtime functionality may consider shipping with the framework.

### Why in the ref pack, why not some other "analyzer" pack?

The SDK already has a means for acquisition and selection of ref packs.  The inputs to the selection of ref packs are the same as that of source generators (Version, FrameworkReference, and TargetFramework), and the outputs are consumed by the same phase (compile) with the same level of cross-targeting granularity (TargetFramework and references).

It may be desirable to create a separate package just for analyzers to reduce risk in servicing.  We support servicing ref packs today, but we must be careful not to expose new API nor change any assembly identities.  It makes more sense to invest efforts in ensuring safe servicing of ref packs, with potential increased complexity in our own builds.  There is benefit in having serviceable ref packs regardless and this keeps additional complexity (resolution of additional pack) out of user builds.

### Why in the ref pack, why not runtime pack?

Runtime packs are currently independent of compile.  Sourcing inputs for compile from a runtime-specific package would break this and require significant changes to the user experience, such as permitting cross-compiling by runtime or "targeting" a runtime.

### Why in the ref pack, why not a dedicated NuGet package?

It is a goal for source-generators to be part of the default experience.  Additionally, the default experience should not require a restore step.  As such analyzers cannot be delivered by NuGet packages alone.

NuGet packages are a good option in addition to the ref pack for components which already ship as NuGet packages, such as `System.Text.Json`.  In this way the source generator can deliver the integrated experience of being *part of* the library.

Standalone source-generator NuGet packages may be used for components which are in the shared framework when the source-generator is not ready to stabilize at the same time as the shared framework, and the component inside the shared framework does not need to be changed.  If the component in the shared framework needs to be changed, it should be done so by either a library+generator NuGet package (`System.Text.Json`) or an experimental build of the entire framework, typically from a codebase like [dotnet/runtimelab](https://github.com/dotnet/runtimelab).

###  I really really need to execute runtime code on the TargetFramework!

You probably need a designer or independent build component that has a host process that can leverage the runtime targeting information of the application.  At the moment this doesn't have a great pattern to follow with source generators.  The compiler does not provide a host, nor does it know anything about target runtime (or in some cases executable frameworks).

### How does this impact source build?

For Linux distribution .NET defines a process from which the .NET product can be built from source: [dotnet/source-build](https://github.com/dotnet/source-build).  This process requires building the entire .NET product without any pre-built binaries.  Some components which make up .NET target older frameworks which aren't built from the latest codebase.  This is done because these components may need to run at design-time on a different framework (.NETFramework), or may also ship out-of-band as a NuGet package for use by libraries that work on many frameworks.  To support building these components on past frameworks, source-build uses an alternate representation of reference assets for those frameworks: [dotnet/source-build-reference-packages](https://github.com/dotnet/source-build-reference-packages).  In order to satisfy build-from-source, packages should only contain reference-API and metadata, no executable code.  As a result, the source-build-reference-packages that represent past framework versions will omit source generators.  This could be a problem for any component which needs to build during source-build and targets this old framework, for example a project targeting .NET 6.0 in the .NET 7.0 source build.  That component would fail to build during source build due to the missing source-generator (see [Deterministic failure](#Deterministic%20failure)).  That component can mitigate the failure by either including a .NET 7.0 configuration for source build.  Alternatively, if the dependent source-generator is also out-of-band (for example: System.Text.Json) the failing component may reference the latest dependent out-of-band package to get the latest source-generator from the package instead of the ref-pack.  To force components to make this mitigation early, we will make sure that Arcade disables source-generators from ref-packs which are not the latest when building for source build.
