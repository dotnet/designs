# Linking the .NET Libraries

When publishing a self-contained application, a .NET runtime is bundled together
with the application. This bundling adds a significant amount of content to the
packaged application. Depending on the complexity of the application, only a
subset of the runtime is required to run the application. These unused parts of
the runtime are unnecessary and can be trimmed from the packaged application.

This is especially important for applications that need to be downloaded over
the internet, for example Xamarin and Blazor applications. End users on a slow
or limited network will be less likely to install and use applications that are
too large. Additionally, there may be store or device limitations on how big an
acceptable application can be, e.g. how large an of application can be installed
over a cellular network; or installed on watchOS.

Trimming a framework-dependent application can also be advantageous since the
application may depend on libraries and only use a small subset of the
functionality available in those libraries.

The [ILLink](https://github.com/mono/linker) is a tool that is able to take
an application and trim the classes and methods that are not used by the
application. In many scenarios, this results in a significantly smaller packaged
application.

This document describes how to modify libraries to work well with the ILLink.

*Note:For .NET Core frameworks our goal is to use the .NET Libraries for all
.NET applications. In previous releases, Xamarin and Blazor applications
have been using the Mono libraries, which have been written with size in mind.
However, the .NET Libraries have traditionally been concerned with throughput
and not size. As such, it may take multiple releases to meet all size goals
with the .NET Libraries.*

In the context of trimming applications, there are two main concerns:

1. Any code and data that are used by the application must be preserved in the application.
1. Any code or data that are not used by the application should be removed from
the application.

Note the difference in "must" and "should" between those two concerns.
An application that works is preferred over a smaller application that doesn't.
Therefore it is critical that any necessary code must be preserved in the application.

## Scenarios and User Experience

The ILLink can be executed on a .NET application and the developer will be
confident that the trimmed application works correctly, while reducing its
final size.

The developer can also enable trim analysis to gain confidence that trimming
will not introduce unexpected changes to the application's behavior. The feedback
presented by the analysis must be actionable in the context of the application.

### Goals

1. Ensure all required code is preserved by the linker.
1. Application size is as small as possible.
1. Trim analysis feedback is actionable for application developers.

## Design

There are a number of things the author of a library needs to undertake in order
to make it work well with trimmed applications. In general they fall into
the two buckets outlined above:

1. Trimming compatibility
1. Smaller Size

### Trimming compatibility

Ensuring required code is preserved by the linker is not easy. .NET has a very
rich Reflection capability that enables dynamic invocation of code. There are
many cases where a static analysis tool (like the ILLink) is not able to know
which code is required and which isn't. For example, a .NET application could
read a Type name from a file and create a new instance of that Type through
Reflection. When the linker analyzes the application statically, it doesn't see
any hard references to that Type and removes it. But when the application is
executed, an error occurs because the Type is no longer available in the application.

#### How trimming works

The trimming operates on the entire application including the application assemblies
any dependent libraries and frameworks. It starts with known entry points to
the application (for example the `Main` method) and marks everything the entry point
needs to run. This logic is applied recursively to mark all the code the application
needs.

The trimming tool has built-in knowledge of some of the most common problematic
patterns and relies on annotations like `RequiresUnreferencedCodeAttribute` or
`DynamicallyAccessedMembersAttribute` to determine the rest. If it encounters
a pattern which it can't recognize or handle for other reasons it generates
a trim analysis warning.

Such warning tells the user that there's code in the application which may
require additional dependency which the tool could not figure out and thus
it's possible the app will not work correctly after trimming.

See the [Linker Reflection Flow Design](https://github.com/mono/linker/blob/master/docs/design/reflection-flow.md)
for more information on how the process works.

#### Annotating code

Some library code can use reflection or other problematic patterns and thus
running the trimming tool on it can break its functionality and it generates
trim analysis warnings.

Warnings originating in the library code itself are not actionable by the end
user as they frequently point to internal implementation details and it's
typically hard to determine which part of the application relies on
the affected functionality.

For this reason, affected libraries need to be annotated to help trimming tools
to better understand the code and to generate warnings which are actionable
in the context of an application. The goal is for the library code itself to
not generate any warnings (as those are not actionable) and instead either
resolve them (by providing annotations which help the trimming tool correctly
recognize all dependencies) or to annotate public APIs such that the problem
can be resolved in the call site by the tool automatically or the warning
is generated pointing at the user code which uses the affected API.

Depending on the specific problematic pattern different solutions can be employed.
The list below is not complete list, it's just the most common patterns we've seen
so far. The list below describes the progression of improvements which should
be made for any affected feature.

The order of these is essentially the recommended order in which to try to
apply the solutions as they go roughly in increasing order of complexity.

#### Refactor hardcoded reflection

The trim tool can already recognize lot of reflection patterns if they are hard
coded (type and member names specified as literals in the code). But it doesn't work
on all such patterns. If the affected code is using what can be described as
hard coded reflection it is often possible to rewrite it in such a way that the
trim tool can recognize all of its patterns and avoid any issues that way. If that
is not possible or practical a reasonable attempt should be made to improve the trimming
tool and teach it about such pattern.

Example of a refactoring: [Using statically known types instead of types from reflection.](https://github.com/dotnet/runtime/commit/29c4895d13ed8768ee5e8307ccef01dff38c1289#diff-58f0fbbb8b2d1c2d89fdae163e981f01697cb4c2de1fe67e6e171504bc615586L1176-L1178)

#### Annotate with DynamicallyAccessedMembers

If the code passes around values of `System.Type` type, these can be annotated
with [`DynamicallyAccessedMembersAttribute`](https://docs.microsoft.com/dotnet/api/system.diagnostics.codeanalysis.dynamicallyaccessedmembersattribute.-ctor?view=net-5.0)
to declare requirements on the type stored in the value. The trim tool uses
this information to both enforce the fulfillment of the requirements
as well as validate that these are sufficient for the code using the values.

This attribute is similar in nature to the nullable annotations that were added
in C# 8 in that they are both viral. `DynamicallyAccessedMembers` needs to be
applied from the actual usage of Reflection all the way through the call tree
to the location that specifies the Type, which may be a public API.

[Cross method annotation](https://github.com/mono/linker/blob/master/docs/design/reflection-flow.md#cross-method-annotations)
has more details on the design of this trimming feature.

Example of using these annotations:
[Annotate type parameter to include constructor if the method body asks for it
via `GetConstructor`.](https://github.com/dotnet/runtime/pull/36532/files#diff-58f0fbbb8b2d1c2d89fdae163e981f01697cb4c2de1fe67e6e171504bc615586L1116-L1131)

#### Suppress warnings

If the warning while technically correct does not affect app's functionality,
consider suppressing it. For this add [`UnconditionalSuppressMessageAttribute`](https://docs.microsoft.com/dotnet/api/system.diagnostics.codeanalysis.unconditionalsuppressmessageattribute?view=net-5.0)
(its usage is nearly identical to [`SuppressMessageAttribute`](https://docs.microsoft.com/dotnet/api/system.diagnostics.codeanalysis.suppressmessageattribute?view=net-5.0),
but it will be kept in the code for all configurations).

Example of suppressing warning on code which doesn't change behavior when trimmed:
[Trimming fields on attributes doesn’t affect equality](https://github.com/dotnet/runtime/commit/0f97c7af662c68862995c0b2ab59e87242eff459#diff-a911305f19a82ebd3a64f9293f9e5347ff15c74d5440f6bc56af65f1fa56e9b8L17-L18)

#### Declare explicit dynamic dependency

In cases where the trimming tool is not able to infer the dependency or
the developer wants to be explicit, the [`DynamicDependencyAttribute`](https://docs.microsoft.com/dotnet/api/system.diagnostics.codeanalysis.dynamicdependencyattribute?view=net-5.0)
can be used to state an explicit dependency from a method to a type/method.
The trimming tool doesn’t reason about this dependency. If the annotated method
is included in the application, its declared dynamic dependency will also be included.

Example of using dynamic dependency: [Declaring dependency from `Queryable.Where`
to `IEnumerable.Where`](https://github.com/dotnet/runtime/commit/20710bbcae006e32f8a133c372c8d78722890982#diff-0d3f3d1b5d2d66a392f61078fc14b72085f52da3d95f46196ae4a6d547cfa74cL31-L32)

#### Mark an API as incompatible with trimming

If none of the above works, the first step should be to annotate the functionality
with [`RequiresUnreferencedCodeAttribute`](https://docs.microsoft.com/dotnet/api/system.diagnostics.codeanalysis.requiresunreferencedcodeattribute?view=net-5.0).
Unless the feature is fundamentally incompatible with trimming, over time
a different solution which makes the code of the feature compatible with
trimming should be implemented. Until that is complete though, it's better
to annotate the API with `RequiresUnreferencedCodeAttribute`
to improve the experience. In this case the main goal is to avoid generating
warnings from within the library and provide a more actionable experience
to the user.

For example, loading new code into the process with APIs like `Assembly.LoadFrom`
will in general break the application when trimmed (since the tooling can't
determine the dependencies required by the loaded code at build time).
A good first step is to annotate the functionality as incompatible with trimming.
This is done by annotating the affected method with `RequiresUnreferencedCodeAttribute`.
It will in turn produce warnings in the call site of the method. This typically
means the annotation needs to be propagated all the way up to the public API
which invokes the functionality.

The message provided in the attribute should:

* Describe what is the feature which is incompatible with trimming
(ideally not the specific API, but the underlying functionality).
For example, "loading new code".
* Provide some guidance on how to resolve this, either directly in the message
if it can be short or by providing a link to docs.

The attribute instructs the tooling to automatically suppress any trim analysis
warnings from the annotated method's body. This makes it suitable for
propagating the trim incompatibility up the call tree.

For example: [Annotating startup hook functionality as incompatible with trimming](https://github.com/dotnet/runtime/pull/44050/files#diff-9c65180e471f0d5b540d61b5aee02e5f6992374bef257ef165ac44099f75dfd4L98-L99)

#### Separate incompatible functionality

The next step in improving the experience is to enable at least some subset
of the functionality while trimming. Frequently only some parts of a feature
are incompatible with trimming while other parts can still be used without issues.
To resolve this the feature and often also its public API needs to be changed
such that static analysis can determine if the incompatible functionality
is in use or not.

If this is not possible the incompatible functionality can also be disabled
using [feature switches](#Feature-Switches). See a separate section below about
more details on this approach.

Example using the feature switch approach: [Introducing the `EventSourceSupported`
feature switch](https://github.com/dotnet/runtime/commit/a547d4178cd2d71d9b6a7a99600e20a3211d4436)

#### Making the feature truly compatible with trimming

Finally, invest into built-time tooling (or other approaches) to enable usage of
the full feature even for trimmed apps. For example,
[source generators](https://devblogs.microsoft.com/dotnet/introducing-c-source-generators/)
can be used to help with this effort.

It’s expected that some features are never fully trimming compatible.
Such features should be at the very least annotated with `RequiresUnreferencedCode`
to provide a good user experience, explaining what feature is incompatible
and offering potential alternatives.

Example of applying source generators: [Plan to use source generator for `System.Text.Json`](https://github.com/dotnet/designs/blob/main/accepted/2020/serializer/SerializerGoals5.0.md#comparison-of-options)

#### .NET Libraries challenges

There are features in the .NET Libraries that will be very expensive, and in some
cases virtually impossible, to make 100% trim compatible. A few examples are:

* `System.ComponentModel.Composition`, `System.Composition`, and other dependency
injection style libraries
    * Contains features such as loading "parts" from a directory
* `Microsoft.CSharp` and `System.Linq.Expressions`
    * Use Reflection extensively to implement `dynamic` and expression trees
* `Assembly.Load/LoadFrom/LoadFile`
    - Dynamically loading assemblies into a trimmed application is problematic
because if the trim tool isn't aware of those assemblies, it won't know to preserve
the methods required by those assemblies. If the core application doesn't need
`string.Split`, trimming will remove the `Split` method. When an assembly that
the trim tool didn't know about is loaded dynamically, it may need `string.Split`,
and fail to run.
* `System.Reflection.Emit`
    * A method or property that is only called by dynamically generated code may
be trimmed if the linker isn't aware of it.

#### Serialization

An especially hard situation for the trimming is code that uses Reflection to
serialize and deserialize object graphs to and from some external representation.
Examples in the .NET Libraries include (but are not limited to):

- `XmlSerializer`
- `JsonSerializer`
- `EventSource.Write<T>()`

One reason this is hard is that every serialization and deserialization mechanism
has its own rules about what it looks for:

* What are acceptable constructors to create the object?
* What properties (or fields) are inspected?
* Does it handle derived types?
* Does call (even optionally) any methods on the objects during
serialization or deserialization?

As of this writing, there is not a bullet proof solution for the trimming to work
in all serialization cases. See [Recursive properties](https://github.com/mono/linker/issues/1087)
for a proposal on what it would take to make `JsonSerializer` at least partially
trim compatible.

One solution that would solve most of the issues is to create
[Source Generators](https://devblogs.microsoft.com/dotnet/introducing-c-source-generators/)
for each supported serializer. This would remove the use of Reflection,
and would allow the trim tool to more easily see which code must be preserved.
However, Source Generators are a new feature and are potentially expensive to create.
As such, it may take some time before they are used broadly.

### Smaller Size

To make applications smaller, we have to let the trim tool know what code is not
necessary and can be removed. A lot of times it is obvious that if a method
isn't called, it can be trimmed and the trim tool will recognize this automatically.
But there are places where unnecessary code looks like it is being called.

#### Removing features

One mechanism we are adding that will allow us to conditionally remove code from
applications is [feature switches](#Feature-Switches). Using this mechanism, an SDK
(like Xamarin) or the developer can decide if a feature that would normally be
available in the libraries can be removed, both at runtime and during trimming.

An example of an existing feature switch we have today is
[InvariantGlobalization](https://docs.microsoft.com/dotnet/core/run-time-config/globalization).
We can add more switches to the libraries to allow large, optional pieces of code
to be removed during trimming.

Examples of other feature switches:

* `ResourceManager`
    * `ResourceManager` has logic that can load types from the resource file.
This could be conditionally disabled by a feature switch. It is already disabled
when loading a `.resource` file directly from disk, which is not embedded in a
resource assembly. [Issue tracking this work](https://github.com/dotnet/runtime/issues/45272).
    * Another possibility is a switch to make exception/resource messages
smaller in the libraries. See [issue](https://github.com/dotnet/runtime/issues/34057)
for more information.
* `EventSource`
    * On some devices the `EventSource` logic is unnecessary because it has no
place to write the events. We can introduce a feature switch to remove
`EventSource` code/calls. [Issue tracking this work](https://github.com/dotnet/runtime/issues/37414)

#### Removing embedded descriptor files

There are libraries which use private Reflection to call
between assemblies. One reason for this is layering. If Assembly A references
Assembly B, and Assembly B needs to call a method in Assembly A, it can't
reference it directly. We've solved that with a Reflection call.

In order to ensure the trim tool doesn't remove the private method, these
libraries may have added "Embedded Descriptor Files" (or `ILLinkTrim.xml`
in the `dotnet/runtime` repo) which get embedded into the built assembly.
These tell the trim tool not to trim the specified method/type. The problem is
that the trim tool never trims the specified method/type, even if the call site
of the Reflection is not needed.

See [issue](https://github.com/dotnet/runtime/issues/31712) and
[another issue](https://github.com/dotnet/runtime/issues/35199)
for examples of this problem.

To solve this the code should use one of the approaches described above. Typically
either make the hard coded code recognizable by the trim tool directly or use
`DynamicDependencyAttribute` to explicitly express a dependency. Libraries should
ideally not have any descriptor files in them, as those unconditionally add code
into the application potentially making it unnecessarily large.
[Example of a discussion on solutions of this problem](https://github.com/dotnet/runtime/issues/31712)

There are 2 existing cases which are somewhat special here worth mentioning:

1. Types only used for Debugging
1. Types used for COM support

In both of these cases, the code is sometimes needed, and sometimes isn't.
If you are never going to debug your application (for example, the application
is deployed to a device that can't be debugged), it isn't necessary to keep
these types in the application. A similar situation exists with COM.
Since COM is not IL, the trim tool isn't able to see if some code in
the application is going to use COM or not.

To solve these, we can define a [feature switch](#Feature-Switches) for each of
these scenarios. An application (or SDK) can default these to on or off, as appropriate.
For example, a Xamarin iOS application can safely turn COM support off.

#### Analyzing Canonical Applications

Application size is similar to throughput performance in that you can't guess
at what could effect an application size. You have to measure it. In order to
measure it, it's a good idea to define a set of canonical applications that we
want to measure. Once we have these applications, we can trim them and then analyze
the output to see what pieces are remaining, why, and investigate how to remove
the unnecessary pieces.

In order to analyze the trimmed application, we will need to create a set of
tools that make it possible. We need to dedicate some design efforts here in
order to help library and application developers, to do this analysis. The kinds
of questions a typical developer needs to ask are:

1. What are the biggest types/methods left after trimming?
2. What is the call graph(s) that is keeping a specific type/method from being trimmed?

##### Potential Size Analysis Tooling

The ILLink is capable of saving the dependency graph when it trims
an application. There is a tool, [Linker Analyzer](https://github.com/mono/linker/tree/master/src/analyzer),
that can load this graph and allows a developer to analyze it.

Today the Linker Analyzer is a console application. One enhancement we can make
is to put the core logic into a NuGet package, and allow a developer to analyze
their application using the [.NET Interactive](https://github.com/dotnet/interactive/blob/master/README.md)
notebook experience. Advantages of this experience are:

1. The dependency graph can be large (many MBs). Loading and unloading it to
ask one question at a time in a console app takes time. In a notebook, it is
loaded into memory once and cached. Many questions can be asked faster.
1. Developers can write their own logic and extensions to the dependency graph.
1. We can have template notebooks that show examples of how to analyze an
application's size.
1. When a new feature is added to the Analyzer, we just need to create a public API.
We don't need to create command line arguments.
1. We have the option of visuals and more interactive output - for example hyperlinks.
In a console application, you are stuck with text.

[PerfView](https://github.com/microsoft/perfview/) has size displaying capabilities
as well. If ILLink outputted the size and graph information in the format PerfView
is capable of displaying, developers could use PerfView to do the size analysis.

### Analysis and feedback

The goal is to make the experience predictable, as such the plan is to employ
multiple tools to provide feedback to developers about the application's compatibility
with trimming (and other modes of deploying the app). These tools will run in
different stages of the SDK pipeline. Roslyn analyzers will be added to provide
immediate feedback about the source code during code editing and in inner build loops.
There’s already the ILLink tool which performs trimming but also analyses the app
as a whole and provides feedback about the entire app. All these tools need
to be enabled only when it makes sense. If the user has no plans to ever trim
the application, it would be a bad experience to show warnings about trimming.
Similarly, for options like single-file, AOT and so on. To this end there needs
to be a mechanism through which user declares an intent to trim the application
(or use single-file and so on). This mechanism must be in place across the entire
SDK pipeline and not just during the publish phase.

The existing properties which enable publishing features like `PublishTrimmed`
or `PublishSingleFile` will be used to also declare intent to use the respective
feature during publishing. The values for these properties should be set in the
project of the application to declare such intent.

At publishing they will be the defaults but can still be overwritten with
command-line or publish-only settings. These values will also have no direct
effect on for example building the application, that is setting `PublishSingleFile=true`
will not produce a single-file from `dotnet build`.

More details provided in [Analyzers for publish only functionality](https://github.com/dotnet/sdk/issues/14562)

### Feature Switches

Feature switches were introduced to make it possible to disable functionality
which is typically included by default with a given .NET Core application,
but it's either incompatible with trimming or it makes the resulting application
unnecessarily large.

.NET has quite a few features which don't have a clear public API which would
disable the functionality fully, instead it's part of every application and
it is "enabled on demand". For example, features related to improving debugging
experience are often activated only when a debugger is attached.

If the user wants the resulting application as small as possible and have
confidence that it won't break the functionality, the user should be able
to choose to disable such features for the app.

That said the end goal should be that analyzing the code of the app is enough
to determine a need to include/use/enable given functionality. So, feature switches
should be only used if there's no way to describe the intent via code or it's
too problematic to do so.

There are also cases where feature switches will be used to disable functionality
which is inherently incompatible with trimming while size is not a big concern.

Recommendations around feature switches:

* Do use feature switches for functionality which is on-by-default and doesn't
already have a way to disable it.
* Do not use feature switches for functionality which is directly invoked from
application's code. For example, if the app's code asks for XML serialization,
adding a feature switch to disable XML serialization is not the best way
to solve the problem.
* Do make sure that the feature behavior is changed by the feature switch
for all configurations of the app. For example, the functionality is disabled
even when the app is not trimmed.
* Do make sure that the behavior of a disabled feature is well defined
and predictable. Tests should be added which validate that trying to use
a disabled feature results in predictable behavior (typically an error).

There will be compromises where feature switch is added to solve a problem with
preexisting public API or when the functionality is intertwined and
is hard to refactor. But it should never be the end goal, because:

* Feature switches are harder to discover - they're not part of the code.
`RequiresUnreferencedCode` message can be used to guide users toward them.
* Feature switches can get into a collision with the code itself -
if a given feature is disabled, but the app still calls it. The ideal behavior
in this case is very scenario dependent, ideally it should produce a warning
(probably via `RequiresUnreferencedCode`) to provide build-time feedback.
* Feature switches are not added just for trimming - they are added to allow
users to disable certain functionality. The reason to disable a functionality
may or may not include trimming. Trimming is just one of the use cases
for feature switches.

Detailed design document and how to implement a [Feature switch](https://github.com/dotnet/designs/blob/main/accepted/2020/feature-switch.md).

#### Feature switches and defaults

Typically feature switch default values should be to enable the respective
functionality as that has been the norm of how .NET behaves
(things work by default).  

On the other hand, there are already scenarios where some functionality
is not available. For example, Blazor WASM doesn't currently support
event tracing. In such cases the default value for a feature switch
should match the capabilities of the scenario.

It is also desirable to change feature switch defaults for certain
configurations. For example, if an application is mean to be trimmed,
startup hooks should be disabled as those are incompatible with trimming.

See [Default values for feature switches in trimmed console apps](https://github.com/dotnet/sdk/issues/14475)
for a discussion of such defaults for console applications.

Important note is that these changes will be made based on the intent
to trim the app (`PublishTrimmed=true` in project file as described above)
and thus will take effect in every stage of SDK.
So `dotnet run` on such app will still have these features disabled,
not just `dotnet publish`. As already mentioned, the goal is to have a
consistent experience across various SDK actions (build, run, publish).

## Testing

Testing the trimmed applications falls into two buckets:

1. Does the application still work?
1. Is the application smaller?

In general, using a library's unit tests to ensure a trimmed application still
works isn't a great testing strategy. Unit tests, by definition, try exercising
all the code in the library. If all the code in the library is being exercised,
the trim tool won't be able to trim any of it. Thus if nothing is trimmed,
you aren't confirming that the library still works when not all of the code is present.
Similarly, if we try to run a library's unit tests on a trimmed build of that library
as included in an application, it's likely that a large percentage of the library's
tests will fail, as they were targeting APIs that were trimmed away having not
been required for the application.

Instead of using unit tests, we should create dedicated applications to test that
the libraries are trim compatible. One set of applications will be higher-level
applications that we want to measure the size over time. Another set will target
specific APIs that needed to be annotated to ensure the annotations and the
trim tool are working correctly.

The targeted tests will use the API in an application in isolation. For example,
if an API takes a Type that is annotated to preserve the constructors on that Type,
the application will be written so the constructor is not called statically. And
when the application is executed, it will ensure the API still works correctly,
and the constructor wasn't trimmed. These tests should be able to be executed in
a regression CI environment to ensure the API works over time and isn't broken by
either the trim tool or the library. They will be created and maintained like any
other unit test for the API. However, since the tests will not be able to use a
unit test framework, like xUnit (which would require making xUnit trim compatible),
the application can simply `return 100;` when it passes and return any other error
code when it fails. This is a similar pattern used by many CoreCLR's unit tests.

As we are measuring size over time, we would not like to get "noise" in the sizes
to cause false positives. For example, if an application size gets
larger by ~10 KB, we don't necessarily want the test to fail. And conversely,
if an application size suddenly get 50% larger (or smaller), we probably do want
to alert someone - as an anomaly was detected and it is possibly a bug somewhere.

These requirements are very similar to benchmark performance tests. One possibility
is that we can piggy-back off of the benchmark perf testing infrastructure/repo
for these size tests. This would allow us to depend on the linker, libraries,
runtime, Xamarin SDKs, etc in the tests and not create new dependencies in the
`dotnet/runtime` repo.
