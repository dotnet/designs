# Single-file Publish

Design for publishing apps as a single-file in .Net Core 3.0

## Introduction

The goal of this effort is enable .Net-Core apps to be published and distributed as a single executable.

There are several strategies to implement this feature -- ranging from bundling the published files into zip file (ex: [Warp](https://github.com/dgiagio/warp)), to native compiling and linking all the binaries together (ex: [CoreRT](https://github.com/dotnet/corert)). These options, along with their cost/benefit analysis is explored in this [staging document](staging.md).

#### Goals

In .Net Core 3.0, we plan to implement a solution that 

* Is widely compatible: Apps containing MSIL assemblies, ready-to-run assemblies, native binaries, configuration files, etc. can be packaged into one executable.
* Can run framework dependent pure managed apps directly from bundle:
  * Executes IL assemblies, and processes configuration files directly from the bundled executable.
  * Extracts ready-to-run and native binaries to disk before loading them.
* Usable with debuggers and tools:  The single-file should be debuggable using the generated symbol file. It should also be usable with profilers and tools similar to a non-bundled app.

This feature-set is described as Stage 2 in the [staging document](staging.md), and can be improvised in further releases.

#### Non Goals

* Optimizing for development: The single-file publishing is typically not a part of the development cycle. It is typically used in release builds as a packaging step. Therefore, the single-file feature will be designed with focus on consumption rather than production.
* Merging IL: Tools like [ILMerge](https://github.com/dotnet/ILMerge) combines the IL from many assemblies into one, but lose assembly identity in the process. This is not a goal for single-file feature.

#### Existing tools

Single-file packaging for .Net Core apps is currently supported by third-party tools such as [Warp](https://github.com/dgiagio/warp) and [Costura](https://github.com/Fody/Costura). We now consider the pros and cons of implementing this feature within .Net Core.

##### Advantages

* A standardized experience available for all apps
* Integration with dotnet CLI
* A more transparent experience: for example, external tools may utilize public APIs such `AssemblyLoadContext.Resolving` or `AssemblyLoadContext.ResolvingUnmanagedDll` events to load files from a bundle. However, this may conflict with the app's own use of these APIs, which requires a cooperative resolution. Such a situation is avoided by providing inbuilt support for single-file apps.

##### Limitations

* Inbox implementation of the feature adds complexity for customers with respect to the list of deployment options to choose from.
* Independent tools can evolve faster on their own schedule.
* Independent tools can provide richer set of features catering to match a specific set of customers.

We believe that the advantages outweigh the disadvantages in with respect to implementing this feature inbox.

## Design

There are two main aspects to publishing apps as a self-extracting single file:

- The Bundler: A tool that embeds the managed app, its dependencies, and the runtime into a single host executable.
- The host: The "single-file" which facilitates the extraction and/or loading of embedded components.

### The Bundler

#### Bundling Tool

The bundler tool must be:

* Able to embed any file required to run a .Net Core app -- managed assemblies, native binaries, configuration files, data files, etc.
* Able to generate cross-platform bundles (ex: publish a single-file for Linux target from Windows)
* Deterministic (generate the exact same single-file on multiple runs)

With the above requirements in mind, we plan to implement a tool similar to [MkBundle](https://github.com/mono/mono/blob/master/mcs/tools/mkbundle) that simply appends the bundled dependencies as a binary blob at the end of a host binary.

To any host (native binary) specified, the bundler tool will add:

* A footer that contains:
  * A flag to identify that this is actually a bundle.
  * A version identifier.
  * Offset of the bundle manifest
* A manifest that identifies (via offset and size)
  * The configuration files `app.deps.json` `app.runtimeconfig.json`
  * The embedded app to load
  * The set of MSIL images
  * The set of ready-to-run images
  * All other files
* The actual files to be bundled.

##### Implementation

The bundler should ideally be located close to the core-host, since their implementation is closely related. Therefore, the bundler will be implemented in the `core-setup` repo. The bundler will be implemented in managed code.

##### Command Line Interface

The bundler will be a standalone tool with the following command line:

```bash
bundle -a <App>      The name of the managed app
       -h <Host>     The path to the native host
       -c <contents> The path to a directory containing the files to bundle
       [-o <output>] The path to output bundle
       [-v]          Generate verbose output
```

Most users are only expected to interact with the bundler via the build system.

##### Build System Interface

Publishing to a single file can be triggered by adding the following property to an application's project file:

```xml
<PropertyGroup>
    <PublishSingleFile>true</PublishSingleFile>
</PropertyGroup>    
```

* The `PublishSingleFile` property applies to both framework dependent and self-contained publish operations.
* The `PublishSingleFile` property applies to platform-specific builds with respect to a given runtime-identifier. The output of the build is a native binary for the specified platform.
* Setting the `PublishSingleFile`property causes the managed app, managed dependencies, platform-specific native dependencies, configurations, etc. (basically the contents of the publish directory when `dotnet publish` is run without setting the property) to be embedded within the native `apphost`. The publish directory will only contain the single bundled executable (and the PDB file).

Optionally, we can add a switch to the CLI, as a shortcut to setting the `PublishSingleFile` property.

```bash
# Alias for dotnet publish /p:PublishSingleFile=true
dotnet publish --single-file
```

By default, `PublishSingleFile` bundles all files in the publish directory into the single executable. This behavior can be altered by using the following content meta-data:

```xml
<IncludeInSingleFile>{Always, PreserveNewest, Never}</IncludeInSingleFile>
```

For example, to place some files in the publish directory but not bundle them in the single-file:

```xml
<ItemGroup>
    <Content Update="*.xml">
      <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
      <IncludeInSingleFile>PreserveNewest</IncludeInSingleFile>
    </Content>
  </ItemGroup>
```

#### Interaction with other tools

Once the single-file-publish tooling is added to the publish pipeline, other static binary transformation tools may need to adapt its presence. For example:

* The MSBuild logic in `dotnet SDK` should be crafted such that [IlLinker](https://github.com/dotnet/core/blob/master/samples/linker-instructions.md), [crossgen](https://github.com/dotnet/coreclr/blob/master/Documentation/building/crossgen.md), and the single-file bundler run in that order in the build/publish sequence. 
* External tools like [Fody](https://github.com/Fody/Fody) that use  `AfterBuild`/`AfterPublish` targets may need to adapt to expect the significantly different output generated by publishing to a single file. The goal in this case is to provide sufficient documentation and guidance.

### The Host

On Startup, the AppHost checks if it has embedded files. If so, it 

* Processes configuration files such as `app.deps.json` , `app.runtimeconfig.json` from the embedded files, instead of looking for them on the disk next to the app.
* Sets up the data-structures to locate pure managed assemblies within the bundle so that they can be loaded on-demand.
* Extracts the other files (native libraries, ready-to-run compiled assemblies, and other data files to an *install-location*). Extraction of files is discussed in detail in this  [document](extract.md). 
* Communicates the information about bundled and extracted files to the runtime, as explained in the section on dependency resolution.

#### Dependency Resolution

An app may choose to only embed some files (ex: due to licensing restrictions) and expect to pickup other dependencies from application-launch directory, nuget packages, etc. In order to resolve assemblies and native libraries, the embedded resources are probed first, followed by other probing paths. 

The communication between the host and the runtime will continue to be through properties such as `TRUSTED_PLATFORM_ASSEMBLIES`, `NATIVE_DLL_SEARCH_DIRECTORIES`, etc.  These properties will be populated with entries from the bundled files (using a special marker) and the extracted files.

For example, if the TPA without single-exe publish is:
`~/app/il.dll`; `~/app/r2r.dll`; `/usr/local/nuget/dep.dll`

Where

* `il.dll` is a pure managed assembly that is loaded directly from the bundle
* `r2r.dll` is a ready-to-run assembly that is bundled and extract out to disk
* `dep.dll` is an assembly that is not bundled with the app,

the TPA with single-exe publish will be (assuming `?` is the bundle marker):
`?/il.dll`; `/tmp/.net/e53xf3/r2r.dll`; `/usr/local/nuget/dep.dll`

### New API

In this section, we propose adding a few APIs to facilitate common operations on bundled-apps.
This is only a draft of the proposed APIs. The actual shape of the APIs will be decided via API review process.

#### Single-file? 

Check whether an app is running from a single-file bundle:

```cs
namespace System.Runtime.Loader
{
    public partial class Bundle
    {
        public static bool IsBundle(Assembly assembly);
    }
}
```

#### APIs to access Bundled files

The binaries that are published in the project are expected to be handled transparently by the host. However, explicit access to the embedded files is useful in situations such as:

- Reading additional files packaged into the app (ex: data files).
- Open an assembly for reflection/inspection
- Load plugins built as single-file class-libs using existing Loader APIs.

Therefore, we propose adding an API similar to [GetManifestResourceStream](https://docs.microsoft.com/en-us/dotnet/api/system.reflection.assembly.getmanifestresourcestream?view=netframework-4.7.2#System_Reflection_Assembly_GetManifestResourceStream_System_String_) to obtain a stream corresponding to an embedded file. 

```C#
// Open a file embedded in the bundle built for the specified assembly 
namespace System.Runtime.Loader
{
    public partial class Bundle
    {
        public static System.IO.Stream GetFileStream(Assembly assembly, string name);
    }
}
```

We can also provide an abstraction that abstracts away the physical location of a file (bundle or disk). For example, add a variant of  `GetFileStream` API that looks for a file in the bundle, and if not found, falls back to disk-lookup. However such abstractions are also easy to build outside of the .Net Framework.

### Existing API

We need to determine the semantics of current APIs such as `Assembly.Location` that return the information about an assembly's location on disk. For example, `Assembly.Location`  for a bundled assembly could return:

* A fixed literal (ex: `null`) indicating that no actual location is available.
* The simple name of the assembly (with no path).
* A special UNC notation such as `//?/asm.dll` to denote files that come from the bundle. If multiple single-file bundles are used in an app (ex: plugins), this notation could be extended as `//?bundle-assembly/name.dll` -- if it is important to make a distinction with respect to which bundle an assembly came from.
* The path of the assembly as if it were not to be packaged into the single-file.
* In the case of files spilled to disk (ex: ready-to-run assemblies) -- the actual location of the extracted file.
* A configurable selection of the above, etc.

Most of the app development can be agnostic to whether the app is published as single-file or not. However, the parts of the app that deal with physical locations of files need to be aware of the single-file packaging. 

In the first implementation, we propose keeping the default behavior of `Assembly.Location`. That is, 

* If the assembly is loaded directly from the bundle, return empty-string
* If the assembly is extracted to disk, return the location of the extracted file.

The semantics of assembly location should be tuned further based on user feedback, and such that it aligns with other bundling technologies in .Net Core ecosystem.

## Testing

* Unit Tests to achieve good coverage on apps using managed, ready-to-run, native code 
* Tests for the newly added API 
* Tests to verify that framework-dependent and self-contained apps can be published as single file.
* Tests for ensure that every app model template supported by .NET Core (console, wpf, winforms, web, etc.) can be published as a single file.
* A subset of CoreCLR Tests
* Tests to ensure that MSIL files with embedded PDBs are handled correctly
* End-to-End testing on real world apps such as Roslyn, MusicStore

#### Measurements

Measure publish size and run-time (first run, subsequent runs) for HelloWorld (framework-dependent and self-contained), Roslyn and MusicStore.

#### Telemetry

Collect telemetry for single-file published apps with respect to parameters such as:

* Framework-dependent vs self-contained apps.
* Whether the apps are Pure managed apps, ready-to run compiled apps, or have native dependencies.
* Embedding of additional/data files, and use of file access API.

## Further Work

#### Bundler Optimizations

Since all the files of an app published as a single-file live together, we can perform the following optimizations

- R2R compile the app and all of its dependent assemblies in a single version-bubble

  Single-file apps compiled cross-platform may have this optimization disabled until the ready-to-run compiler  (`crossgen`) supports cross-compilation. 

- Investigate whether collectively signing the files in an assembly saves space for certificates.

#### Single-file Plugins

The above design should be extended to seamlessly support single-file publish for plugins.

- The bundler tool will mostly work as-is, regardless of whether we publish an application or class-lib. The binary blob with dependencies can be appended to both native and managed binaries.
- For host/runtime support, the options are:
  - Implement plugins using existing infrastructure. For example: Take control of assembly/native binary loads via existing `AssemblyLoadContext` callbacks and events. Extract the files embedded within the single-file plugin using the `GetFileStream()` API and load them on demand.
  - Have new API to load a single-file plugin, for example: `AssemblyLoadContext.LoadWithEmbeddedDependencies()`.

#### VS Integration

Developers should be able to use the feature easily from Visual Studio. The feature will start with text based support -- by explicitly setting the `PublishSingleFile` property in the project file. In future, we may provide a single-file publish-profile, or other UI triggers.

## User Experience

To summarize, here's the overall experience for creating a HelloWorld single-file app 

*  Create a new HelloWorld app: `HelloWorld$ dotnet new console`

#### Framework Dependent HelloWorld

* Normal publish: `dotnet publish` 

  * Publish directory contains the host `HelloWorld.exe` ,  the app `HelloWorld.dll`, configuration files `HelloWorld.deps.json`, `HelloWorld.runtimeconfig.json`, and the PDB-file `HelloWorld.pdb`.

* Single-file publish: `dotnet publish /p:PublishSingleFile=true`

  * Publish directory contains: `HelloWorld.exe` `HelloWorld.pdb`

  * `HelloWorld.dll`, `HelloWorld.deps.json`, and `HelloWorld.runtimeconfig.json` are embedded within `HelloWorld.exe`.

* Run: `HelloWorld.exe`

  * The app runs completely from the single-file, without the need for intermediate extraction to file.

#### Self-Contained HelloWorld

- Normal publish: `dotnet publish -r win10-x64 --self-contained`

  * Publish directory contains 221 files including the host, the app, configuration files, the PDB-file and the runtime.

- Single-file publish: `dotnet publish -r win10-x64 --self-contained /p:PublishSingleFile=true`

  - Publish directory contains: `HelloWorld.exe` `HelloWorld.pdb`
  - The remaining 219 files are embedded within the host `HelloWorld.exe`.

- Run: `HelloWorld.exe`

  * The bundled app and configuration files are processed directly from the bundle.
  * Remaining 216 files will be extracted to disk at startup. 
  * If reuse of extracted files is enabled, subsequent runs of the app may skip the extraction step.


Most applications are expected to work without any changes. However, apps with a strong expectation about absolute location of dependent files may need to be made aware of bundling and extraction aspects of single-file publishing. No difference is expected with respect to debugging and analysis of apps.

## Related Work

* [Mono/MkBundle](https://github.com/mono/mono/blob/master/mcs/tools/mkbundle) 
* [Fody.Costura](https://github.com/Fody/Costura)
* [Warp](https://github.com/dgiagio/warp)
* [BoxedApp](https://docs.boxedapp.com/index.html)

