# Publishing and Consuming Symbols and Source for Debugging

**Owner** [Tomáš Matoušek](https://github.com/tmat)

Debugging with symbols and source makes developers much more efficient and effective at developing and debugging (including debugging crash dumps). Our goal is to make debugging with source and symbols a characteristic of the .NET ecosystem.

This document outlines an end-to-end flow from library through to app development that enables symbols and source to always be available. The primary audience of this document are developers who participate in the .NET ecosystem, either as a producer or consumer of NuGet packages.

## Motivation

By default, Visual Studio steps through just your code when you are debugging your application. This is a useful characteristic because you are normally wanting to understand and investigate the logic of the code you've written. The feature that enables this experience is aptly called "Just my Code". In some cases, you need to debug through the logic of 3rd party components or of the platform itself, however, it is currently challenging to do so. The missing link is acquisition of symbols and sources that match the 3rd party components and platform binaries that you are using.

JavaScript has almost the opposite problem of .NET. The JavaScript community, both browser and node.js variants, uses the [source maps](https://www.npmjs.com/package/source-map) project to provide a good experience debugging 3rd party minified code.  However, JavaScript editors do not provide a "Just my Code" experience. For .NET developers, we want it to be easy and natural to transition between the default "Just my Code" experience and debugging with symbols and source for 3rd party components and the platform.

## .NET Symbols

A good debugging experience relies on the presence of [debug symbols](https://en.wikipedia.org/wiki/Debug_symbol) in an appropriate form (every platform has its own flavor). Symbols give you multiple pieces of critical information, for example: the association of instructions with the snippets of source code from which the instructions were compiled, names of local variables, their scopes, etc. 

### PDB Formats

There are multiple forms of symbol files. This document will use the term "symbols" unless there is a need to discuss a more specific symbol file variant. Microsoft stack developers are familiar with symbols as [Program Database files](https://en.wikipedia.org/wiki/Program_database), using the .PDB file extension.

We support two symbol types for _managed code_ in .NET: [Windows PDBs](https://github.com/Microsoft/microsoft-pdb) and [Portable PDBs](portable-pdb.md). Historically, Windows PDBs have been used to store debug information of both native and managed code. The tooling for reading and writing these PDBs is only supported on Windows platform. Portable PDBs were designed to store managed debug information efficiently in an open-source platform-agnostic format with tooling available and supported on multiple platforms. Storing managed debug information in Portable format results in much smaller PDBs, which is an important characteristic when considering symbol distribution.

### Publishing Symbols

By default, symbols are built as separate files to minimize the size of shipping binaries. These files need to be published by the system that builds them (e.g. the CI server) and discovered and retrieved by the system that needs them (e.g. the debugger).

#### Publishing Symbols to a Symbol Server

Today, symbol servers are mostly used for hosting symbol files inside enterprise environments. [Symbol server tooling](https://docs.microsoft.com/windows-hardware/drivers/debugger/symbol-stores-and-symbol-servers) is available for such environments and recently has also been integrated into [VSTS](https://blogs.msdn.microsoft.com/devops/2017/11/15/vsts-is-now-a-symbol-server) and exposed as a service. 

For developers publishing their libraries on NuGet.org the options for publicly available symbol servers are limited and the process of publishing and consuming symbols is more involved than it should be. Thus the number of packages published on NuGet.org that are easy to debug is low. We propose to address these shortcoming by having the NuGet.org gallery host a symbol server available to publisher to that gallery (see GitHub issue [6104](https://github.com/NuGet/Home/issues/6104) for details).

#### Symbol Packages

Today, [symbol packages](https://docs.microsoft.com/en-us/nuget/create-packages/symbol-packages) are used to distribute symbols and sources. Although NuGet client tools provide an option to create these packages NuGet.org gallery doesn't accept them. Developers either have to manually host and acquire the packages or use other services, like http://www.symbolsource.org. 

We propose that NuGet.org gallery accepts symbol packages, indexes symbols it finds in them and makes these symbols publicly available via an existing symbol server protocol ([SSQP](https://github.com/dotnet/symstore/blob/master/docs/specs/Simple_Symbol_Query_Protocol.md)). Visual Studio would be pre-configured with this symbol service by default, making it easy for developers to enable it (single click). Consumers of other tools would use this symbol service as well, not only on Windows but on any platform.

Today, symbol packages duplicate files contained in the corresponding primary package. To reduce the amount of data that the package publisher needs to upload to the server, we propose to introduce a new style of symbol packages that only contains artifacts that the NuGet symbol server indexes.

To streamline the packaging and publishing process we propose that the client tools that generate packages during build (`dotnet pack`, `msbuild /t:pack`, `nuget pack`) also generate new-style symbol packages by default provided that symbols are generated for the project being built. The package publishing tool would also upload symbol package by default if it was produced by the build.

To enable debugging offline, when the symbol server is not available, we propose to develop a tool (msbuild task and dotnet tool) that downloads symbols from the symbol server for all binaries a given project depends on. This tool would enumerate all dependencies of the project similarly to `dotnet restore`. It would download symbols to the local symbol cache where debuggers can find them.   

#### Including Symbols in NuGet Packages

Until the symbol services and tools proposed above are available we recommend to include managed _Portable_ PDBs in the primary package alongside code binaries. We do not recommend this approach for managed Windows PDBs due to their size. 

This packaging command can be configured to include symbols in the package alongside code binaries by including the following snippet in the project:

```xml
<PropertyGroup Condition="'$(DebugType)' == 'portable'">
  <AllowedOutputExtensionsInPackageBuildOutputFolder>$(AllowedOutputExtensionsInPackageBuildOutputFolder);.pdb</AllowedOutputExtensionsInPackageBuildOutputFolder>
</PropertyGroup>
```

**Use case** The Roslyn team intends to publish packages to NuGet.org containing Roslyn binaries and Portable PDBs. We expect that developers will be able to debug Roslyn code on all platforms supported by Roslyn.

#### Symbol Embedding

In some cases, it is beneficial to embed symbols in binaries such that they are always available without publishing to a symbol server. Embedding symbols is the most convenient deployment option for portable symbols, but has the drawback of increasing size of binaries. The format of embedded symbols is always Portable PDBs given that the size of Windows PDBs is prohibitively large.

You can enable building with embedded symbols by setting the following property in your project:

```<DebugType>Embedded</DebugType>```

**Use case:** The Roslyn repository uses Jenkins to run tests as part of PR validation. Jenkins captures crash dumps when tests crash. In order to open crash dumps on a developer machine, developers used to need to manually point the debugger at symbol files built on the Jenkins machine. This was not incovenient and slowed investigations. Instead, the team decided to embed symbols into test and product binaries built on Jenkins machines. After that, debuggers were able to find symbols directly in the crash dump without any intervention  from developers. This made the task easier and sped up investigations.

### Consuming Symbols

Consuming symbols in Visual Studio or other tool should be convenient and easy. It needs to work for all .NET implementations, including .NET Core, .NET Framework, Xamarin, Unity and UWP.

There are three primary scenarios for debugging with symbols:

* Debugging an application in its development phase. In this case, third party library symbols need to be retrieved from outside of the build system while application symbols are generated as part of the build.
* Debugging an application in its deployed state. In this case, application and library symbols need to be retrieved. The scenario is attaching to a running application.
* Debugging a crash dump of an application. In this case, application and library symbols are needed.

#### Symbols and Binaries Distributed Together

This scenario applies to the development phase. It doesn't apply if symbols are already embedded in binaries. NuGet is a common case of binary distribution, which allows for deployment of symbols and binaries together.

The key characteristic is that a symbol file will exist right beside the loaded code file on your disk, enabling a debugger to trivially locate a matching symbol file for a given binary. Per the guidance in this document, binaries and symbols will frequently be colocated in a NuGet package in a structure similar to the following example.

```
/
  /lib
     /netstandard2.0
        foo.dll
        foo.pdb
``` 

.NET Core development is NuGet-centric, which helps this scenario. During development (for example, with `dotnet run`), the .NET Core Runtime loads libraries from the NuGet cache by default, enabling a debugger to find a matching symbol file in the same location.

.NET Framework development uses NuGet but less formally. When a project is built, NuGet libraries are copied to the application bin directory but not the symbols. As a result, the link between code binary and symbols is lost.

The following logic should be used by build systems to better enable debugging with symbols:

* If a code binary is copied to a location, also copy the symbol file to the same location. In most cases, this will be to the application bin directory.

#### Symbols and Binaries Distributed Separately

This scenario applies to debugging deployed applications, crash dumps, and debugging third party libraries that do not come with symbols in NuGet packages. In these cases, you need to acquire symbols from a symbol server. As stated above, we propose to provide a public symbol service for symbols uploaded to NuGet.org. You may also use other symbol servers, per your needs.

### Tools that Require Windows PDBs

There are a number of tools that only support Windows PDBs. These tools were typically built many years ago and aligned with native Windows development as opposed to .NET. The use of these tools is much more niche than Visual Studio (which supports both Windows and Portable PDBs) but are used by some important users, such as the .NET Core team and Microsoft Customer Support.

The following tools and libraries require Windows PDBs today:

* [Microsoft Symbol Store](https://docs.microsoft.com/windows-hardware/drivers/debugger/microsoft-public-symbols)
* [Watson / Windows Error Reporting](https://docs.microsoft.com/windows-hardware/drivers/debugger/windows-error-reporting)
* [WinDbg](https://docs.microsoft.com/en-us/windows-hardware/drivers/debugger/)
* [DIA API](https://docs.microsoft.com/visualstudio/debugger/debug-interface-access/debug-interface-access-sdk)

The work on these tools to support Portable and Embedded PDBs is in progress. Until the necessary changes are implemented and customers of these tools update to their new versions we provide a [conversion tool and API](https://github.com/dotnet/symreader-converter) that allows users to convert between these formats in both directions.

This conversion can be performed either at the time of use of the PDB or at the time the library is being published. In the latter case, the build would produce a Portable PDB and include it in a NuGet package. The build would also convert this Portable PDB to Windows PDB and publish it to a symbol server. The Portable PDB can also be published to the symbol server if the symbol server supports it (e.g. VSTS symbol service).

**Use case** The .NET teams publish Windows and Portable PDBs as part of their build systems. The Windows PDBs will be published to the Microsoft Symbol Store while the Portable PDBs are included in NuGet packages. 

## Acquiring and Consuming Source

Acquiring and consuming source is mostly a function of first having acquired symbols. Once you have symbols, a debugger will find that one or multiple of the following cases are true:

* The symbol file contains embedded source for the source file the debugger is looking for, at which point the debugger will use that source.
* The symbol file is a Windows PDB and contains an embedded source server information. Build tools such as [pdbstr](https://msdn.microsoft.com/en-us/library/windows/desktop/ms680641(v=vs.85).aspx) or [GitLink](https://github.com/GitTools/GitLink) are available that amend an existing Windows PDB with source server information. 
* The symbol file contains an embedded source link file, at which point the debugger will interpret and act upon the declarations in the source link file.
* Otherwise, source will not be available via the mechanisms discussed in this document

> For consideration: If the debugger can't find the source file in the symbol file or via source link it may still try to look it up on the symbol server using [Simple Symbol Query Protocol](https://github.com/dotnet/symstore/blob/master/docs/specs/Simple_Symbol_Query_Protocol.md). This would allow for scenarios where the source is made available later, after the binaries and symbols have been built. To support this scenario we would provide a command line tool that uploads the sources to a specified symbol server.

### Source Link Files

Source link files are json files that describe the relationship between symbol records of source files and the physical availability of source files on a secure web server (meaning using the https protocol).

You can see an [example of a source link file](https://github.com/dotnet/core/blob/master/Documentation/diagnostics/source_link.md#examples) in the following example.

```json
{
    "documents": {
        "C:\\src\\CodeFormatter\\*": "https://raw.githubusercontent.com/dotnet/codeformatter/bcc51178e1a82fb2edaf47285f6e577989a7333f/*"
    }
}
```

## SourceLink Project

[SourceLink](https://github.com/ctaggart/SourceLink) is [.NET Foundation](http://dotnetfoundation.org/) project that generates source link files.

SourceLink 2.6.0 is currently available. Using this version you can enable source link file generation by adding a reference to the [SourceLink.Create.CommandLine](https://www.nuget.org/packages/SourceLink.Create.CommandLine/) NuGet package, as you can see in the following example. This package contains [MSBuild targets](https://github.com/ctaggart/SourceLink/blob/master/SourceLink.Create.CommandLine/SourceLink.Create.CommandLine.targets) that are used in the build to produce the source link file.

```xml
    <PackageReference Include="SourceLink.Create.CommandLine" Version="2.2.0" PrivateAssets="All" />
```

> Currently missing features:
> Version 2.* doesn't support git submodules (see issue [#228](https://github.com/ctaggart/SourceLink/issues/228))
> Version 2.* doesn't support TFVC repositories.

### Integration into the .NET Core SDK

We intend to integrate the SourceLink project in some form into the .NET Core SDK (for SDK-style projects). We will work with the SourceLink maintainer to do this in a mutally agreed manner.

The rest of this document discusses the experience that the .NET team intends to provide, focussing on the .NET Core SDK but not exclusively oriented on it.

### Users of Source Link Files

Developers must opt-in to producing source link files. The URLs included in these files may be to private source repositories that may not be intended to be exposed to anyone who has access to the symbol file, so developers should make a considered choice.

Open source developers are expected to opt-in to source link file generation since they typically do not have disclosure concerns. The .NET Core team, for example, will use this experience.

Corporate developers are also expected to opt-in to source link file generation for the following patterns:

* All application assets (binaries, symbols and source) are used within the corporate firewall, such the only users who have access to these assets are authorized to see them.
* Binary assets are shipped externally but symbols and source only ever used within the corporate firewall, such that the only users who have access to symbol and source assets are authorized to see them.

There is another (arguably anti-pattern) option that corporate developers have, as follows.

* Binary and symbol assets are shared externally. The symbol assets contain source link files (and potentially generated file assets).
* The source link file points to a symbol source that requires authentication, such as [VSTS](https://www.visualstudio.com/team-services/).
* Authorized users (likely few in number) would get access to the source.
* Unauthorized users (likely many more in number) would get access denied messages from an endpoint that they don't understand.

If this option becomes popular / desirable, we could change the experience in Visual Studio so that unathorized users don't see access denied messages but just don't have access to source. This would potentially come at the cost of discoverability for authorized users.

### Source Link File Generation

The source link file maps names of source files as they are written to the symbol file to the corresponding URLs that point to the source file content on a server. 

To generate the source link file a few pieces of information about the current state of the repo are needed:

- Repository revision id -- e.g. git commit sha1 hash
- Repository raw content URL -- The endpoint providing raw source file content
- Repository root -- the root directory under which all sources that participate in built reside 

If a repository contains links to other repositories (e.g. git submodules) the following information is needed for each of the linked repositories:

- Linked repository revision id
- Linked repository relative path (relative to the root of the containing repository)
- Linked repository raw content URL

Some of the information is specific to the source control system (e.g. git, TFVC, etc.) other to the provider that host the repository (e.g. github, VSTS, etc.). 

Therefore the logic of generating source link files needs to be factored into multiple components that may potentially be implemented by different parties:

- Source control information provider (source control system specific)
  Naming convention for nuget package: ```Microsoft.Build.Tasks.{source-control-system}.nuget```
  E.g. _Microsoft.Build.Tasks.Git.nuget_, _Microsoft.Build.Tasks.TFVC.nuget_
  
  These packages are expected to export msbuild tasks and targets that provide repository information such as snapshot id, list of linked repositories, etc.  
  
- Source Link provider (repository host specific)
  Naming convention for nuget package: ```SourceLink.{provider-name}[.{source-control-system}].nuget```
  E.g. _SourceLink.GitHub.nupkg_, _SourceLink.VSTS.TFVC.nupkg_, _SourceLink.VSTS.Git.nupkg_

  These packages are expected to reference the corresponding source control information provider package and export msbuild targets that provide source link generation targets.

The .NET SDK will define _abstract_ APIs that source control providers can implement to provide source link capabilities for their customers.

The .NET SDK will enable source link file generation via `EnableSourceLink` project setting. The _SourceLink_ package is expected to set this property to `true`, therefore the user enables source link generation by simply adding a package reference to the _SourceLink_ package to their repository. The property can be overridden by user's project file settings if needed (e.g. if source link should only be used in certain scenarios such as when the project is built on a CI server).

The build will fail if `EnableSourceLink` is on and no _SourceLink_ package is referenced by the project.

> Consideration: In some cases a repository hosted by one provider might link to a repository hosted by another provider. E.g. a VSTS repository that links to a GitHub repository via a submodule. In that case multiple _SourceLink_ packages would be needed and the API needs to be designed to allow each of these packages to contribute to the final source link.

### Source Embedding

In some cases, it is beneficial to embed source in symbols so that you have a convenient means of deploying source for debugging. It is however a trade-off between convenience and the size of the PDB. Although sources are stored compressed in the PDB including many source files might increase the PDB size significantly.

The following embedding options (for both Windows and Portable PDBs) are available:

* Embed only source files that are not tracked by source control (e.g. generated during build). The rest of the files are mapped by a source link.
* Embed manually selected subset of source files.
* Embed all source files

Use case: A Microsoft team has a centralized production environment but decentralized development environments. They found it difficult to publish symbol and source assets in a centralized way from their many decentralized development environments. As a result, developers investigating failures involving multiple components were not able to acquire symbols and source efficiently. Instead, teams in that environments moved to embedding symbols and all sources in application binaries such that symbols and source were always available. Although this approach increased size of binaries, the added benefit of investigating failures was seen as a bigger win.

Project property `EmbedAllSources` with boolean value indicates that all sources passed to the compiler should be embedded to the PDB. 

#### Automatic Embedding of Untracked Source Files

Source link enables the debugger and other tools to find source content for files that are tracked by the source control. However, not all files participating in build are tracked. For example, files generated during the build are usually not checked into the repository. Although it is possible to manually identify such files and mark them for being embedded into the PDB, such process is tedious and error prone.

> The [SourceLink.Embed](https://github.com/ctaggart/SourceLink) project already supports automatic identification and embedding of files not tracked by source control. 

We propose the source control packages defined above provide APIs that would determine files not tracked by the source control (e.g. for git repositories the files matching an entry in `.gitignore` file) and a setting `EmbedUntrackedSources` that would then instruct the compiler to embed the untracked sources. 

### Stack Traces

Starting in .NET Framework 4.7.2 the `System.Diagnostics.StackTrace` API supports displaying line and file information for stack frames of methods built with Portable and Embedded PDBs (Windows PDBs have always been supported).
The feature is only enabled by default for applications built against .NET Framework 4.7.2. Applications built against previous .NET Framework versions can add the following configuration entry to their  App.config files in order to enable reading source information from Portable and Embedded PDBs when running on .NET Framework 4.7.2:

```xml
<runtime>
  <AppContextSwitchOverrides value="Switch.System.Diagnostics.IgnorePortablePDBsInStackTraces=false" />
</runtime>
```

The same configuration entry can also be included in Machine.config file, which will enable this feature for all applications running on the machine.

#### Integrating Source Link into Stack Traces

Stack traces are a specific case where a link to source is useful. You can see this demonstrated in the following image. Source information can be provided for each stack frame, with a clickable link to source.

![source link in stack trace](https://pbs.twimg.com/media/DHj4aP9XoAE3BPR.jpg:large)

The source location should be the following:

* If source link, the fully qualified URL, with line number.
* For embedded source, a relative path, with line number.

It is important that the fully qualified URL is included to make copy/paste sharing of stack traces outside of Visual Studio useful and lossless.

Clicking on the source link in Visual Studio should open the source in a new source window at the correct line number. The alternative is opening up a web browser.
