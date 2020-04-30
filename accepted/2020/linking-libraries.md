# Linking the .NET Libraries

When publishing a self-contained application, the .NET Core runtime is bundled together with the application. This bundling adds a significant amount of content to the packaged application. Depending on the complexity of the application, only a subset of the runtime is required to run the application. These unused parts of the runtime are unnecessary and can be trimmed from the packaged application.

This is especially important for applications that need to be downloaded over the internet, for example Xamarin and Blazor applications. End users on a slow or limited network will be less likely to install and use applications that are too large.

Trimming a framework-dependent application can also be advantageous since the application may depend on libraries and only use a small subset of the functionality available in those libraries.

The [ILLinker](https://github.com/mono/linker) is a tool that is able to take an application and trim the classes and methods that are not used by the application. In many scenarios, this results in a significantly smaller packaged application.

We will modify the [.NET Libraries](https://github.com/dotnet/runtime/tree/master/src/libraries) to work well with the ILLinker. Our goal is to use the .NET Libraries for all .NET applications. In previous releases, Xamarin and Blazor applications have been using the Mono libraries, which have been written with size in mind. However, the .NET Libraries have traditionally been concerned with throughput and not size. As such, it may take multiple releases to meet all size goals with the .NET Libraries.

In the context of trimming applications, there are two main concerns:

1. Any code and data that are used by the application must be preserved in the application.
2. Any code or data that are not used by the application can be removed from the application.

Note the difference in "must" and "can" between those two concerns. An application that works is preferred over a smaller application that doesn't. Therefore it is critical that any necessary code must be preserved in the application.

## Scenarios and User Experience

The ILLinker can be executed on a .NET application and the developer will be confident that the trimmed application works correctly, while reducing its final size.

The main application types that will be the initial focus are Xamarin and Blazor applications. While ensuring small application sizes in other form factors is important, we will focus our initial efforts on the application types in which size is absolutely critical.

### Goals

1. Ensure all required code is preserved by the linker.
2. Allow Xamarin and Blazor applications to use the .NET Libraries and be as small as possible.

### Non-Goals

1. Optimizing application size in other form factors.

## Design

There are a number of work items the .NET Libraries Team needs to undertake in order to be linker-friendly. In general they fall into the two buckets outlined above:

1. Linker-safety
2. Smaller Size

### Linker-safety

Ensuring required code is preserved by the linker is not easy. .NET has a very rich Reflection capability that enables dynamic invocation of code. There are many cases where a static analysis tool (like the ILLinker) is not able to know which code is required and which isn't. For example, a .NET application could read a Type name from a file and create a new instance of that Type through Reflection. When the linker analyzes the application statically, it doesn't see any hard references to that Type and removes it. But when the application is executed, an error occurs because the Type is no longer available in the application.

There are features in the .NET Libraries that will be very expensive, and in some cases virtually impossible, to make 100% linker-safe. A few examples are:
- `System.ComponentModel.Composition`, `System.Composition`, and other dependency injection style libraries
    - Contains features such as loading "parts" from a directory
- `Microsoft.CSharp` and `System.Linq.Expressions`
    - Use Reflection extensively to implement `dynamic` and expression trees
- `System.Resources.ResourceManager`
    - Resource files can have ResourceReader Type names embedded in them, which `ResourceManager` creates dynamically.
- `Assembly.Load/LoadFrom/LoadFile`
    - Dynamically loading assemblies into a trimmed application is problematic because if the linker isn't aware of those assemblies, it won't know to preserve the methods required by those assemblies. If the core application doesn't need `string.Split`, the linker will trim the `Split` method away. When an assembly that the linker didn't know about is loaded dynamically, it may need `string.Split`, and fail to load.
- `System.Reflection.Emit`
    - A method or property that is only called by dynamically generated code may be trimmed if the linker isn't aware of it.

For these expensive cases, we will need to evaluate if the amount of effort required to make the feature linker-safe will add enough value to the scenarios we are targeting. For example, it is unlikely many developers are using `System.Composition` in Xamarin applications.

To help the developer know which parts of an application or library the linker is not able to analyze correctly the linker is adding features to warn developers when they are calling linker-unfriendly methods. See the [Linker Reflection Flow Design](https://github.com/mono/linker/blob/master/docs/design/reflection-flow.md) for more information.

#### Adding dynamic access annotations

With the addition of the Linker Reflection Flow features above, we will need to add attributes to the .NET Library methods that rely on Reflection. At a high-level, we can run the linker on the libraries and look at the warnings it produces. We then need to analyze the correct fix for the linker warning. Possibilities are:

1. Add a `DynamicallyAccessedMembers` attribute to the code, to tell the linker which members need to be accessed dynamically.
    - This attribute is similar in nature to the nullable annotations that were added in C# 8 in that they are both viral. `DynamicallyAccessedMembers` needs to be applied from the actual usage of Reflection all the way through the call tree to the location that specifies the Type, which may be a public API.

2. In some cases the usage is actually safe. For example, if an "Equals" method is written by retrieving all the fields on a type and looping over them comparing each field. If a field is unused, and therefore trimmed by the linker, there is no need to compare that field for equality. In these cases we can suppress the linker warning using the `[UnconditionalSuppressMessage]` attribute.

3. In some cases the API itself is not able to be linker-friendly. In these cases we don't want to suppress the warning completely, but instead pass the warning to the caller of the API. In these cases we can use the `[RequiresUnreferencedCode]` attribute. This attribute tells the linker to not warn about this method, but instead warn about any callers of this method.

4. In cases where the linker is not able to infer the dependency, or the developer wants to be explicit, the `[DynamicDependency]` attribute states an explicit dependency from one method to another. When the linker sees the attributed method needs to be kept, it will also preserve anything that method has a dynamic dependency to.

A high-level goal is that the .NET Libraries are clean of linker warnings. This is one level of confidence we can have that .NET applications will be linker safe.

#### Serialization

An especially hard situation for the linker to understand is code that uses Reflection to serialize and deserialize object graphs to and from some external representation. Examples in the .NET Libraries include (but are not limited to):

- `XmlSerializer`
- `JsonSerializer`
- `EventSource.Write<T>()`

One reason this is hard is that every serialization and deserialization mechanism has its own rules about what it looks for:
- What are acceptable constructors to create the object?
- What properties (or fields) are inspected?
- Does handle derived types?

As of this writing, there is not a bullet proof solution for the linker to work in all serialization cases. See https://github.com/mono/linker/issues/1087 for a proposal on what it would take to make `JsonSerializer` linker-friendly.

One solution that would solve most of the issues is to create [Source Generators](https://devblogs.microsoft.com/dotnet/introducing-c-source-generators/) for each supported serializer. This would remove the use of Reflection, and would allow the linker to more easily see which code must be preserved. However, Source Generators are a new feature and are potentially expensive to create. As such, it may take some time before they are used broadly.

### Smaller Size

To make applications smaller, we have to let the linker know what code is not necessary and can be removed. A lot of times it is obvious that if a method isn't called, it can be trimmed. But there are places where unnecessary code looks like it is being called.

#### Removing Framework Features

One mechanism we are adding that will allow us to conditionally remove code from applications is [feature switches](./feature-switch.md). Using this mechanism, an SDK (like Xamarin) or the developer can decide if a feature that would normally be available in the libraries can be removed, both at runtime and during linking. An example of an existing feature switch we have today is [InvariantGlobalization](https://docs.microsoft.com/en-us/dotnet/core/run-time-config/globalization). We can add more switches to the libraries to allow large, optional pieces of code to be removed by the linker.

Examples of potential new feature switches:
- ResourceManager
    - ResourceManager has logic that can load types from the resource file. This could be conditionally disabled by a feature switch. It is already disabled when loading a `.resource` file directly from disk, which is not embedded in a resource assembly.
    - Another possibility would be a switch to make exception/resource messages smaller in the libraries. See https://github.com/dotnet/runtime/issues/34057 for more information.
- EventSource
    - On some devices the EventSource logic is unnecessary because it has no place to write the events. We could introduce a feature switch to remove EventSource code/calls.

#### Removing Embedded Descriptor Files

There are places in the .NET Libraries where we use private Reflection to call between assemblies. One reason for this is layering. If Assembly A references Assembly B, and Assembly B needs to call a method in Assembly A, it can't reference it directly. We've solved that with a Reflection call.

In order to ensure the linker doesn't remove the private method, we've added "Embedded Descriptor Files" (or `ILLinkTrim.xml` in the `dotnet/runtime` repo) which get embedded into the built assembly. These tell the linker not to trim the specified method/type. The problem is that the linker never trims the specified method/type, even if the callsite of the Reflection is not needed.

See https://github.com/dotnet/runtime/issues/31712 and https://github.com/dotnet/runtime/issues/35199 for more information on this issue.

To solve this, we will follow the approaches outlined in https://github.com/dotnet/runtime/issues/31712 to not rely on the Embedded Descriptor Files in order to make the Reflection usages linker safe. Instead, we will add the appropriate linker annotations and not embed these descriptor files into our libraries. This will allow applications to safely call, or not call, the APIs and the linker will either preserve or trim the code correctly.

There are 2 existing cases here worth mentioning:
1. Types only used for Debugging
2. Types used for COM support

In both of these cases, the code is sometimes needed, and sometimes isn't. If you are never going to debug your application (for example, the application is deployed to a device that can't be debugged), it isn't necessary to keep these types in the application. A similar situation exists with COM. Since COM is not IL, the linker isn't able to see if some code in the application is going to use COM or not.

To solve these, we can define a [`feature switch`](#Removing-Framework-Features) for each of these scenarios. An application (or SDK) can default these to on or off, as appropriate. For example, a Xamarin iOS application can safely turn COM support off.

Since these 2 scenarios require the use of Embedded Descriptor xml files, we will need to teach the linker to respect feature switches inside an Embedded Descriptor xml file.

#### Analyzing Canonical Applications

Application size is similar to throughput performance in that you can't guess at what could effect an application size. You have to measure it. In order to measure it, we will need to define a set of canonical applications that we want to measure. Once we have these applications, we can link them and then analyze the output to see what pieces are remaining, why, and investigate how to remove the unnecessary pieces.

In order to analyze the trimmed application, we will need to create a set of tools that make it possible. We need to dedicate some design efforts here in order to help both the .NET Libraries developers, as well as 3rd party library and application developers, to do this analysis. The kinds of questions a typical developer needs to ask are:

1. What are the biggest types/methods left after trimming?
2. What is the call graph(s) that is keeping type/method `foo` from being trimmed?

##### Potential Size Analysis Tooling

The ILLinker is capable of saving the dependency graph when it trims an application. There is a tool, [Linker Analyzer](https://github.com/mono/linker/tree/d3eb4ea8179516b0554a3d3089c71062496bd6f0/src/analyzer), that can load this graph and allows a developer to analyze it.

Today the Linker Analyzer is a console application. One enhancement we can make is to put the core logic into a NuGet package, and allow a developer to analyze their application using the [.NET Interactive](https://github.com/dotnet/interactive/blob/master/README.md) notebook experience. Advantages of this experience are:

1. The dependency graph can be large (many MBs). Loading and unloading it to ask one question at a time in a console app takes time. In a notebook, it is loaded into memory once and cached. Many questions can be asked faster.
2. Developers can write their own logic and extensions to the dependency graph.
3. We can have template notebooks that show examples of how to analyze an application's size.
4. When a new feature is added to the Analyzer, we just need to create a public API. We don't need to create command line arguments.
5. We have the option of visuals and more interactive output - for example hyperlinks. In a console application, you are stuck with text.

[PerfView](https://github.com/microsoft/perfview/) has size displaying capabilities as well. If ILLinker outputted the size and graph information in the format PerfView is capable of displaying, developers could use PerfView to do the size analysis.

## Testing

Testing the linked applications falls into two buckets:
1. Does the application still work?
2. Is the application smaller?

In general, using a library's unit tests to ensure a linked application still works isn't a great testing strategy. Unit tests, by definition, try exercising all the code in the library. If all the code in the library is being exercised, the linker won't be able to trim any of it. Thus if nothing is trimmed, you aren't confirming that the library still works when not all of the code is present. Similarly, if we try to run a library's unit tests on a linked build of that library as included in an application, it's likely that a large percentage of the library's tests will fail, as they were targeting APIs that were linked away having not been required for the application.

Instead of using unit tests, we should create dedicated applications to test that the libraries are linker safe. One set of applications will be higher-level applications that we want to measure the size over time. Another set will target specific APIs that needed to be annotated to ensure the annotations and the linker are working correctly.

The targeted tests will use the API in an application in isolation. For example, if an API takes a Type that is annotated to preserve the constructors on that Type, the application will be written so the constructor is not called statically. And when the application is executed, it will ensure the API still works correctly, and the constructor wasn't trimmed. These tests should be able to be executed in a regression CI environment to ensure the API works over time and isn't broken by either the linker or the library. They will be created and maintained like any other unit test for the API. However, since the tests will not be able to use a unit test framework, like xunit (which would require making xunit linker-friendly), the application can simply `return 100;` when it passes and return any other error code when it fails. This is a similar pattern used by many coreclr's unit tests.

As we are measuring size over time, we would not like to get "noise" in the sizes to cause false positives. For example, if an application size gets larger by ~10 KB, we don't necessarily want the test to fail. And conversely, if an application size suddenly get 50% larger (or smaller), we probably do want to alert someone - as an anomaly was detected and it is possibly a bug somewhere.

These requirements are very similar to benchmark performance tests. One possibility is that we can piggy-back off of the benchmark perf testing infrastructure/repo for these size tests. This would allow us to depend on the linker, libraries, runtime, Xamarin SDKs, etc in the tests and not create new dependencies in the `dotnet/runtime` repo.



## Q & A
