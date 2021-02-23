# Inbox Source Generators

**PM** [Immo Landwerth](https://github.com/terrajobst) |
**Dev** [Eric StJohn](https://github.com/ericstj)

Roslyn source generators allow for component developers to provide functionality that runs in the context of compilation, analyzes a users code, and can generate additional code to add to the compilation.  Compelling scenarios for source generators involve moving dynamic runtime code to be specialized and static emitted by the compiler: specializing serializer code for a target type, pre-compiling regular expressions at design time, pre-computing composition graphs for DI systems, and many more.  This techinque was possible before but was rather clunky and had to be done outside the compiler, so its use was minimal and not part of the default experience.  Roslyn source generators solve this and as a result we wish to develop many source generators which are part of the default experience for .NET applications.  Source generators present a new type of challenge for a design time component: they need to be part of the toolchain but are tied to a specific components within a target framework, and often re-implement or share the functionality of those components.  We need to define how we ship these source generators and make them available to developers.  We need to define the lifecycle for these source generators and how it is tied to the framework component it extends.

## Scenarios and User Experience

A developer can use source generators for framework API without making any changes to their project.  Creating a new project should be sufficient.

A developer can use source generators for components in NuGet packages by referencing the NuGet package for that component.  

When a component ships both in a NuGet package and a framework, the developer has a consistent experience with the source generator as they do with the library itself.  If the package referenced is newer then the framework the source generator from the package will be used as is already the case for the library in that package.  Similarly if the framework is newer than the NuGet package the framework's source generator will be used as is already the case for the library in the framework.

A developer can recieve updates to a source generator through normal channels which they already obtain updates today.

A developer can update their version of Visual Studio or Dotnet SDK and have confidence that new versions of the toolchain won't impact their applications which target older frameworks, outside of known and published servicing updates.

A developer has confidence that their library using source generators will continue to work in the same way a library without source generators works on future versions of .NET following the same compatibility rules.

## Requirements

### Goals

- Enable consumption of source generators for components in shared frameworks
- Address conflict resolution between source generators in shared frameworks and nuget packages
- Define servicing process for shared framework source generators
- Establish guidelines building and versioning shared framework source generators
- Describe compatibility requirements for source generators.

### Non-Goals

- Define a process for components not in shared frameworks to contribute source generators.
- Define any changes to the source generator architecture.
- Define any implementation details of source generators or source generator tests.
- Define a process around internal or non-shipping source generators

## Stakeholders and Reviewers

- ASP.NET - also building shared framework source generators.
- .NET Libraries team - building multiple shared framework source generators.
- Mono team - interested in providing source generators.
- Roslyn team - responsible for source generator infrastructure.
- .NET SDK team - responsible for resolving shared frameworks and doing conflict resolution with content.
- NuGet team - FYI only, no changes requested

## Design

### Framework provided source generators

Framework provided source generators should have a mechanism to be exposed and versioned along side the framework and library which they extend.  These source generators will be passed to the compiler when that framework is targeted.  These source generators will not ne used when the framework is not targeted.  For example, ASP.NET specific source generators will not be used when ASP.NET is not referenced.  The version of the analyzers specific to the TargetFramework version will be used.  For example, when targeting .NET 6.0 the analyzers specific to net6.0 will be used and not those for .NET 7.0, even when using the SDK that ships with .NET 7.0.

To facilitate this, we will package analyzers inside [reference packs](https://github.com/dotnet/designs/blob/main/accepted/2019/targeting-packs-and-runtime-packs.md).  We will use the standard [nuget conventions for describing analyzers](https://github.com/dotnet/designs/blob/main/accepted/2019/targeting-packs-and-runtime-packs.md).  The .NET SDK will be responsible for locating the appropriate analyzers for the TargetFramework and creating `@(Analyzer)` items as part of the `ResolveTargetingPackAssets` task.  These items will be conflict-resolved with other Analyzer sources based as described in the following section.

### Source generator conflict resolution

Source generators and analyzers are passed to the compiler as @(Analyzer) items.  These items may come from NuGet packages, be built into the SDK targets, be directly defined by the user via ProjectReference or raw Analyzer items, or be provided by the new framework mechanism described in this document.  All should be considered for conflict resolution based on the same constraint.

The .NET SDK already does conflict resolution for other asset types: references and copy-local runtime assets.  It should do the same for Analyzers.  Analyzers need not have "platform manifest" inputs for conflict resolution since all analyzers will be available during build, unlike runtime files which may not be available.  Comparison for the sake of conflict resolution can consider the same assembly and file version rules as reference files.  When finding two or more source generators with the same name

Source generator conflict resolution should run before compile, but after all NuGet package assets have been evaluated.  Specific sequencing, public/private target naming, extensibility will not be specified here but should follow best practice as determined by the SDK team. 

### Source generator versioning

Source generators should be strongly named and update assembly version with every public stable release (GA and servicing).  This ensures that the publicly shipped source generators can be loaded side-by-side in the same process and garuntees 

### Source generator compatibility concerns

Source generators should only use public API in framework components they extend.  This public API already needs to meet the strict binary compatibility guarantees as framework API.  As such libraries containing source-generated code will be binary-compatible with future releases, just as if the user wrote this code directly.

Source generators should not use public API with "reserved" or "undocumented" functionality in attempts to avoid public API guarantees.  Libraries paired with source generators need to provide the same guarantees as libraries alone do today.

Source generators should avoid emitting public API in user's projects.  Such API can create a compatibility contract in that user's library which needs to be maintained.  If a source generator produces public API in a user's project by design, that public API must be strictly maintained following the same rules as framework components across versions of the source generator.  Care should be taken to the public API which is generated and should be reviewed by the Framework Design Council.  A similar constraint applies to internal API: users will have source that expects to interact with the generated internal API and the user will expect that code to continue to compile between versions.

Generated code should not be generated in System namespaces, it should prefer the user's namespace.  Generated code should be triple-slashed commented and be free from compiler warnings.  Generated code should avoid name colisions with user code and provide affordances for resolving name conflicts. 

### Functionality concerns for source generators

Though the following are not specific to inbox source generators, these concerns are worth reviewing to describe how they apply to framework specific source generators.

Source generators target .NETStandard and must run on both .NETFramework and .NETCore.  

Source generators should limit their dependencies.  There is no scheme for encoding framework-specific nor runtime-specific implementations of dependencies or source generators based on the runtime environment so source generators should avoid any dependencies on assemblies which need to differ by framework or runtime.  Source generators may depend on packages which are known dependencies of the compiler and should depend on versions less than or equal to that provided by the compiler and should not include that dependency in their package.

Source generators should not directly reference nor execute runtime code.  Despite inbox source generators being coupled to a specific framework version, source generators cannot assume they will be running on that framework.  They may be running on a newer, older, or completely different framework.  There is no type-equivalence guaranteed between references of the source generator and those of the user code that is under analysis.  As such source generators will need to examine user code references by name, or when needing to make equivalence checks can resolve types in the user code's type reference set to ensure valid equivalence checks.  Source sharing should be considered as an alternative for cases where source generators need to execute runtime library code.

## Q & A

### Why not just deliver source generators in the SDK same as roslyn analyzers?

Doing so would require source generators to carefully version and test across all framework versions.  This would increase testing cost for source generators and increase risk when shipping updates to source generators since they would apply to past, stable TargetFrameworks.  Conceptually source generators are lifting a substantial amount of runtime functionality out of a library and executing that at design time. This code is directly included in the user's application and contributes to runtime behavior.

### Inplace servicing updates are good enough for the compiler, why not source generators?

The compiler has a very stable 1:1 relationship between a user's source and the code it generates. As such the user has the ability to control the output of the compiler through their own usage.  The compiler's contribution of complexity to the user's assembly is very small and highly specified.
 
Contrast this to source generators which can take a very small amount of user source to produce a large amount of generated code.  This large amount of generated code contains significant complexity which is loosely specified.  It's behavior is specific to the component it extends.  It's likely that we'd need to bugfix this code over time, and change it as we develop features in the components it extends.  It's also likely that we will learn new techniques and improvments to source generators over time, increasing the changes for bugfixes and features

User's expect stability for projects which do not change, for example a project targeting an LTS release. It is much more difficult to ensure this stability when we need to significantly components which contribute complexity to that project.

### Is it bad to have multiple versions of a source generator possibly loaded by a repository?

A repository may have projects targeting multiple frameworks.  These frameworks may contain different versions of the same source generator.  When building the repository this may require the same compiler server instance to load multiple copies of a source generator.  The same compiler would not likely load multiple copies of analyzers which do not ship side by side.  

### Should all inbox source generators be tied to a shared framework?

No, some source generators might be independent of libraries in a shared framework: these could go in a non-side-by-side location (eg: analyzers folder).  Other source generators may not contain functionality, but may instead just build some convenience API on top of existing public API.  These could be considered stable enough to place in a non-side-by-side location.

### Should only source generators be shipped this way?  What about analyzers and code-fixes?

Nothing is preventing analyzers and code fixes from shipping in the same manner.  If it's desireable for a such a component to ship with the framework rather than the compiler then that component may ship in this manner.

### Why in the ref pack, what not some other "analyzer" pack?

The SDK already has a means for acquisition and selection of ref packs.  The inputs to the selection of ref packs are the same as that of source generators (Version, FrameworkReference, and TargetFramework).  Ref packs user normal nuget convention which support analyzers and source generators.

It may be desirable to create a separate package just for analyzers in order to reduce risk in servicing.  We support servicing ref packs today, but we have to be careful not to expose new API nor change any assembly identities.  

### Why in the ref pack, what not runtime pack?

Runtime packs are currently independent of compile.  Sourcing inputs for compile from a runtime-specific package would break this and require signicant changes to the user experience, such as permitting cross-compiling by runtime or "targeting" a runtime.

###  I really really need to execute runtime code in the TargetFramework!

You probably need a designer or indepent build component that has a host process that can leverage the runtime targeting information of the application.  At the moment this doesn't have a great pattern to follow with source generators.  The compiler does not provide a host, nor does it know anything about target runtime (or in some cases executable frameworks).
