# Single-file Publish

This document describes the design single-file apps in .NET 5.0.
The design of single-file apps in .NET Core 3.0 can be found [here](design_3_0.md)

## Introduction

The goal of this effort is enable .Net-Core apps to be published and distributed as a single executable.

There are several strategies to implement this feature -- ranging from bundling the published files into zip file to native compiling and linking all the binaries together. These options, along with their cost/benefit analysis is discussed in the [staging document](staging.md) and [related work](related.md).

#### Goals

In .NET 5.0, we plan to implement a solution that:

* Is widely compatible: Apps containing IL assemblies, ready-to-run assemblies, composite assemblies, native binaries, configuration files, etc. can be packaged into one executable.
* Can run managed components of the app directly from bundle, without need for extraction to disk. 
* Usable with debuggers and tools.

#### Non Goals

* Optimizing for development: The single-file publishing is typically not a part of the development cycle, but is rather a packaging step as part of a release. Therefore, the single-file feature will be designed with focus on consumption rather than production.
* Merging IL: Tools like [ILMerge](https://github.com/dotnet/ILMerge) combines the IL from many assemblies into one, but lose assembly identity in the process. This is not a goal for single-file feature.

## User Experience

Here's the overall experience for publishing a HelloWorld single-file app. The new build properties used in this example are explained in the [Build System Interface](#build-system-interface) section.

* Create a new HelloWorld app: `HelloWorld$ dotnet new console`

* Framework Dependent Publish

  * Normal publish: `dotnet publish` 
    * Published files: `HelloWorld.exe`, `HelloWorld.dll`, `HelloWorld.deps.json`, `HelloWorld.runtimeconfig.json`, `HelloWorld.pdb`

  * Single-file publish: `dotnet publish -r win-x64 --self-contained=false /p:PublishSingleFile=true`
    * Published files: `HelloWorld.exe`, `HelloWorld.pdb`

* Self-Contained Publish

  * Normal publish: `dotnet publish -r win-x64`
    * Published files: `HelloWorld.exe`, `HelloWorld.pdb`, and 224 more files 
  * Single-file publish Linux: `dotnet publish -r linux-x64 /p:PublishSingleFile=true`
    * Published files: `HelloWorld.exe`, `HelloWorld.pdb`
  * Single-file publish Windows: `dotnet publish -r win-x64 /p:PublishSingleFile=true`
    * Published files: `HelloWorld.exe`, `HelloWorld.pdb`, `coreclr.dll`, `clrjit.dll`, `clrcompression.dll`,  `mscordaccore.dll`
  * Single-file publish Windows with Extraction: `dotnet publish -r win-x64 /p:PublishSingleFile=true /p:IncludeNativeLibrariesInSingleFile=true` 
    * Published files: `HelloWorld.exe`, `HelloWorld.pdb`

## Build System Interface

Publishing to a single file can be triggered by adding the following property to an application's project file:

```xml
<PropertyGroup>
    <PublishSingleFile>true</PublishSingleFile>
</PropertyGroup>    
```

When the `PublishSingleFile` property is set to true, 

* `RuntimeIdentifier` must be defined. Single-file builds generate a native binary for the specific platform and architecture.
* `UseAppHost` cannot be  set to `false`.  
* If `TargetFramework` is
  * `netcoreapp5.0`, single-file publish works as described in this document.
  * `netcoreapp3.0`  or `netcoreapp3.1` single-file publish works as described [here](design_3_0.md).
  * An earlier framework, causes a compilation error.

Setting the `PublishSingleFile` property causes the managed app, runtime configuration files (`app.deps.json`, `app.runtimeconfig.json`), and managed binary dependencies to be embedded within the native `apphost`.  All managed binaries (IL and ready-to-run files) that would be written to the publish directory and any sub-directories are bundled with the apphost.

All other files, including platform-specific native binaries and symbol files, are left alongside the app by default. However, the set of files left unbundled alongside the app is expected to be small, such as: data-files (ex: `appsettings.json`) and custom native binary dependencies of the application. Further details regarding the files left next to the app is discussed in the [Host build](#Host-Builds) section. 

### Optional Settings

The following settings can be used to package additional files into the single-file app. However, when using these options, the files that cannot be processed directly from the bundle will be extracted out to disk during startup. 

| Property                             | Behavior when set to `true`                                  |
| ------------------------------------ | ------------------------------------------------------------ |
| `IncludeNativeLibrariesInSingleFile` | Bundle published native binaries into the single-file app.   |
| `IncludeSymbolsInSingleFile`         | Bundle the `.pdb` file(s) into the single file app. This option is provided for compatibility with .NET 3 single-file mode. The recommended alternative is to generate assemblies with embedded PDBs (`<DebugType>embedded</DebugType>`). |
| `IncludeAllContentInSingleFile`      | Bundle all published files (except symbol files) into single-file app. This option provides backward compatibility with the  .NET Core 3.x version of single-file apps. |

Certain files can be explicitly excluded from being embedded in the single-file by setting following `ExcludeFromSingleFile` meta-data element. For example, to place some files in the publish directory but not bundle them in the single-file:

```xml
<PropertyGroup>
  <PublishSingleFile>true</PublishSingleFile>
  <IncludeContentInSingleFile>true</IncludeContentInSingleFile>
</PropertyGroup> 
<ItemGroup>
  <Content Update="*-exclude.dll">
    <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
      <ExcludeFromSingleFile>true</ExcludeFromSingleFile>
  </Content>
</ItemGroup>
```

### Alternatives

The behavior of `PublishSingleFile` property described above, is significantly different from .NET Core 3.x  SDK. As an alternative, we could leave `PublishSingleFile` semantics unchanged (bundle all content to an actual single file), and have a different property `PublishFewFiles` to only bundle content that can be directly processed from the single-file.

### Handling Content 

The app may want to access certain embedded content for reading, rather than loading via host/runtime. For example: bundled payload/data files. In this case, the recommended strategy is to embed the content files within appropriate managed assemblies as resources, and access them through [resource handling APIs](https://docs.microsoft.com/en-us/dotnet/api/system.reflection.assembly.getmanifestresourceinfo?view=netcore-3.1). 

### Interaction with External Tools

Once the single-file-publish tooling is added to the publish pipeline, other static binary transformation tools may need to adapt its presence. For example, tools like [Fody](https://github.com/Fody/Fody) that use  `AfterBuild`/`AfterPublish` targets may need to adapt to expect the significantly different output generated by publishing to a single file. The goal in this case is to provide sufficient documentation and guidance.

## The Bundler

The bundler is a tool that embeds the managed app and its dependencies into the native `AppHost` executable. The functional details of the bundler are explained in [this document](bundler.md). 

## The Host

### Startup

On Startup, the [host components](https://github.com/dotnet/core-setup/blob/master/Documentation/design-docs/host-components.md) perform the following functions:

* **AppHost**: The AppHost identifies itself as a single-file bundle (by checking the [bundle marker](bundler.md#bundle-marker)) before invoking [HostFxr](https://github.com/dotnet/core-setup/blob/master/Documentation/design-docs/host-components.md#host-fxr).

* **HostFxr**: If invoked from a single-file app, HostFxr process the `runtimeconfig.json` and `deps.json` files directly from the bundle. The location of these `json` files  are identified directly from the bundle-header for simple access.
* **HostPolicy**: Much of the bundle-processing is performed within HostPolicy.
  
  * If the app needs extraction, extracts out the appropriate files as explained in [this document](extractor.md).
  * Reads the `deps.json` file directly from the bundle and resolves dependencies.
  * Processes the Bundle manifest to maintain internal data-structures to locate bundled assemblies when probed by the runtime.
  * Implements a [`bundle_probe` ](#Dependency-Resolution) function, and passes it (function pointer encoded as a string) to the runtime through a property named `BUNDLE_PROBE`.
  * Starts the runtime to continue execution.
  
### Host Builds

The .NET 5 AppHost will be built in a few different configurations, and consumed by the SDK in appropriate scenarios as noted below.

| Code Name  | Build                                                        | Scenario                                                     |
| ---------- | ------------------------------------------------------------ | ------------------------------------------------------------ |
| AppHost    | Current AppHost build                                        | All non-single-file apps<br />Framework-dependent single-file apps |
| StaticHost | AppHost with HostFxr and HostPolicy statically linked.       | Self-contained single-file apps on Windows                   |
| SuperHost  | StaticHost with CoreCLR runtime components statically linked | Self-contained single-file apps on Unix systems              |

Ideally, we should use the SuperHost for self-contained single-file apps on Windows too. However, due to certain limitations in debugging experience and the ability to collect Watson dumps, etc., the CoreCLR libraries are left on disk beside the app.

The files typically published for self-contained apps on Windows are:

* The `App.Exe`-- StaticHost along with assemblies and configuration files bundled as a single-file app.
* `coreclr.dll`(runtime), `clrjit.dll` (JIT compiler), and `clrcompression.dll` (native counterpart of BCL for certain compression algorithms).
    * It may be possible to link these DLLs together as CoreCLR.dll, but this work is not prioritized, in deference to building the full super-host once debugging framework supports it.
* mscordaccore.dll (to enable Watson dumps)

Certain additional binaries may be optionally included with the app to enable debugging scenarios. 
The work to not require these binaries as separate files on disk alongside the app is ongoing.

* For F5 debugging on VS and VS-Core: `mscordbi.dll`, `libmscordbi.so`
* For Linux mini-dumps: `createdump`, `libmscordaccore.so` 
* For ETW / LTTng: `clretwrc.dll`, `libcoreclrtraceptprovider.so` 
* For exception stack trace source information, error string resources on Windows: `Microsoft.DiaSymReader.Native.amd64.dll`, `mscorrc.debug.dll`, `mscorrc.dll`

When targeting `win7` platform, several additional DLLs are necessary (`api-*.dll`) to handle API compatibility. These files must be alongside the AppHost for the app to even start execution.
Therefore, we propose that targeting `win7-*` should not be supported when publishing apps as a single-file.

## Dependency Resolution

* When probing for assemblies, the [host probing logic](https://github.com/dotnet/runtime/blob/master/docs/design/features/host-probing.md#probing-paths) will treat bundled assemblies similar to assemblies in the app directory. The probe ordering will be in the order:  

  * Servicing location
  * The *single-file bundle*
  * App directory 
  * Framework directory(s) from higher to lower
  * Shared store 
  * Additional specified probing paths.

* The host implements a bundle-probing to locate files embedded in the single-file bundle:

  ```c++
  /// <summary>
  /// <param name="path"> Relative-path to the file being probed. </param>
  /// <param name="size"> Out-param: size of the file, if found. </param>
  /// <param name="path"> Out-param: offset within the bundle, if found</param>
  /// <returns> true if the requested file is found in the bundle, 
  ///           false otherwise. </returns>
  /// </summary>
  bool bundle_probe(const char *path, int64_t *size, int64_t *offset);
  ```

  This probe returns the size and offset of the requested file, if found, using the bundle manifest. However, if a bundled assembly is overridden by one found in a servicing location, the probe returns false.
  
* The assemblies bundled within the single-file are *not* enumerated in `TRUSTED_PLATFORM_ASSEMBLIES`. The absolute-path of assemblies on disk are listed in `TRUSTED_PLATFORM_ASSEMBLIES` as usual. 

* Similarly, the paths to directories containing satellite assemblies within the bundle are not listed in `PLATFORM_RESOURCE_ROOTS`. 

* The extraction directory (if any) will be added to `NATIVE_DLL_SEARCH_DIRECTORIES` as the first destination to probe for native binaries.

* The default assembly resolution logic in the runtime:

  * First attempts to locate the assembly within the bundle using the `bundle_probe` host-callback.  
  * If the assembly is not found in the bundle, it attempts to locate the assembly via `TRUSTED_PLATFORM_ASSEMBLIES` (or within `PLATFORM_RESOURCE_ROOTS` for satellite assemblies). 
  
* The algorithm for resolving native binaries is unchanged.

## The Runtime

### PEImage loader

* IL assemblies are loaded directly from the bundle.
    * The portion of the single-file bundle containing the required assembly is memory mapped, and the contents are appropriately interpreted by the runtime.

* ReadyToRun Assemblies
    * On Linux, ReadyToRun assemblies are loaded directly from bundle. The various sections in the PE file are mapped at appropriate addresses, and offsets are fixed up.
    * On Mac, ReadyToRun assemblies are loaded similar to Linux. However, Mojave hardened runtime doesn't allow executable mappings of a file. Therefore, the contents of an assembly are read from the bundle into pre-allocated executable memory.
    * On Windows, due to [certain limitations](#Windows-Limitations) in memory mapping routines described below, ReadyToRun assemblies are loaded by memory mapping the file and copying sections to appropriate offsets.

* ReadyToRun [Composite](https://github.com/dotnet/runtime/blob/master/docs/design/features/readytorun-composite-format-design.md) Assemblies are expected to be loaded similar to ReadyToRun assemblies. 

#### Windows Limitations
The Windows mapping routines have the following limitations:
* [CreateFileMapping](https://docs.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-createfilemappinga) has no option to create a mapping for a part of the file (no offset argument).
    Therefore, we cannot use the (SEC_IMAGE) attribute to perform automatic section-wise loading (circumventing alignment requirements) of bundled assemblies directly. Instead we need to map each section independently.
* [MapViewOfFile]( https://docs.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-mapviewoffileex) can only map parts of a file aligned at [memory allocation granularity]( https://docs.microsoft.com/en-us/windows/win32/api/sysinfoapi/ns-sysinfoapi-system_info), which is 64KB.
    This means that each section within the assemblies should be aligned at 64KB – which is not guaranteed by the crossgen compiler. Therefore, without substantial changes to the runtime and the ready-to-run file format, we cannot load ready-to-run files using direct file mappings.

We therefore map ReadyToRun assemblies as-is, and subsequently perform an in-memory copy of the sections to appropriate offsets. In the long term, the solution to the mapping problem would involve considerations such as:
* Compile all assemblies in a version bubble into one PE assembly, with a few aligned sections.
* Embed the big composite assembly into the host with proper alignment, so that the single-exe bundle can be loaded without copies at run time.

### API Semantics

#### `Assembly.Location`

There are a few options to consider for the `Assembly.Location` property of a bundled assembly:

* A fixed literal (ex: `null`) indicating that no actual location is available.
* Throw an `AssemblyLoadedFromBundle` exception
* The empty string, similar to assemblies loaded from [byte-array](https://docs.microsoft.com/en-us/dotnet/api/system.reflection.assembly.location?view=netcore-3.1#property-value),
* The simple name of the assembly (with no path).
* The path of the assembly as if it were not to be packaged into the single-file.
* A special UNC notation such as `<bundle-path>/:/asm.dll` to denote files that come from the bundle. 
* A configurable selection of the above, etc.

Proposed solution is for `Assembly.Location` to return the empty-string for bundled assemblies, which is the default behavior for assemblies loaded from memory.

Most of the app development can be agnostic to whether the app is published as single-file or not. However, the parts of the app that deal with physical locations of files need to be aware of the single-file packaging. 

#### `AppContext.BaseDirectory`

`AppContext.BaseDirectory` will be the directory where the AppHost (the single-file bundle itself) resides. In contrast to [.NET Core 3.x single-file apps](design_3_0.md), .NET 5 single-file apps do not always self-extract on startup. Therefore, the details about extraction directory are not exposed through the `AppContext.BaseDirectory` API.  

However, when single file apps are published with `IncludeAllContentInSingleFile` property set (which provides backward compatibility with .NET Core 3.x bundling behavior), `AppContext.BaseDirectory` returns the extraction directory, following  [.NET Core 3.x semantics](design_3_0.md#API-Impact).

## Testing

* Unit Tests:
  * Apps using managed, ready-to-run, native code 
  * Framework-dependent and self-contained apps 
  * Apps with content explicitly annotated for inclusion/exclusion in the single-file bundle
  * IL files with embedded PDBs 
  * PDBs included/excluded from bundle
* Tests for ensure that every app model template supported by .NET 5 can be published as a single file.
* Tests to ensure cross-platform publishing of single-file bundles.
* Manual end-to-end testing on real world apps

#### Measurements

Measure publish size and startup time for a few real-world apps.

#### Telemetry

Collect telemetry for single-file published apps with respect to parameters such as:

* Framework-dependent vs self-contained apps.
* Whether the apps are Pure managed apps, ready-to run compiled apps, or have native dependencies.
* Embedding of additional/data files.

## Further Work

* **Compression**: Currently the bundler does not compress the contents embedded at the end of the host binary.  Compressing the bundled files and meta-data can significantly reduce the size of the single-file output (by about 30%-50% as determined by prototyping). 
* **Single-file Plugins** Extended the above design to seamlessly support single-file publish for plugins.
