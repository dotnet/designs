# Building architecture-specific applications

The first set of .NET Core releases, up to and including .NET Core 2.2, focused on the needs of web applications built with ASP.NET Core. Web applications don't directly depend on native libraries or OS-specific APIs as a general rule. As a result, we didn't focus on the usability of producing architecture-specific applications or that depend on native dependencies as much as we need to looking forward.

Client applications differ in this respect, often relying on native libraries and APIs and typically need to integrate with OS-provided experiences (like notifications). With the addition of client scenarios in .NET Core 3.0, we need to reassess the usability and capability of building applications and libraries that that are intended for only a single architecture.

Our goal is that `dotnet build` should produce useful and intuitive artifacts for building both client and server applications. Where a non-default configuration is desired, it should be easy and intutive to select that option.

The document proposes the following:

* .NET Core applications should be architecture-neutral by default.
* Client applications should be architecture-specific by default, controlled via templates.
* Only architecture-specific (including self-contained) applications should have EXEs.
* The default for `--self-contained` is bad for usability. We should fix that for 3.0+ projects.

Note: The term *architecture* is used throughout this document. It is intended to mean the unique combination of an operating system and CPU, like Windows X64, or Linux ARM64. It is equivalent to runtime identifier (RID).

Note: There are separate challenges on building libraries that have native dependencies. That issue is not covered in this document, but is a problem that will be discussed in a following document.

## Current Behavior

The default `dotnet build` behavior in .NET Core 3.0 Preview 4 is the following:

* Framework-dependent application
* Architecture-specific executable (matches architecture of the SDK )
* Native dependencies for all architectures

This combination of characteristics, particularly the last two points, isn't entirely coherent. It gives you an executable for one architecture, but native dependencies for all architectures. That means that the application will run on all architecture supported by the native dependencies (if any) but that an executable is provided for only one architecture and the `dotnet` launcher needs to be used on all other architectures.

It also means that the application may be carrying extra files it doesn't need if the application is intended to be run on only one architecture. We expect this to be the case for WPF and Windows Forms applications, for example.

As a result, the current behavior is considered to be unfortunate. It doesn't seem like the right behavior for developers intentionally targeting multiple architectures or for targeting single architectures.

An example of the current default experience follows, with an application that has a single NuGet dependency (from Microsoft):

```console
C:\git\testapps\cpumath>type cpumath.csproj
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>netcoreapp3.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.ML.CpuMath" Version="0.11.0" />
  </ItemGroup>

</Project>

C:\git\testapps\cpumath>dotnet build

C:\git\testapps\cpumath>dir /s /b bin\*.dll bin\*.exe bin\*.so bin\*.dylib
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\cpumath.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\Microsoft.ML.CpuMath.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\cpumath.exe
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\runtimes\linux-x64\native\libCpuMathNative.so
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\runtimes\osx-x64\native\libCpuMathNative.dylib
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\runtimes\win-x64\native\CpuMathNative.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\runtimes\win-x86\native\CpuMathNative.dll

C:\git\testapps\cpumath>bin\Debug\netcoreapp3.0\cpumath.exe
Hello World!
```

Notice the inclusion of native dependencies for Linux, macOS and two Windows architectures. At the same time, the application includes an executable that will only run on Windows x64. If the application is launched with `dotnet cpumath.dll`, it will then run on all of the architectures supported by the native dependencies.

## Proposed Behavior

`dotnet build` should have three modes:

* Architecture-agnostic, framework-dependent, application that includes native dependencies for all supported architectures and no EXE
* Achitecture-specific, framework-dependent, application that includes an EXE and native dependencies for a single targeted architecture
* Architecture-specific, self-contained, application that includes EXE and native dependencies for a single targeted architecture

The proposal is:

* Retain the first behavior as the default.
* Enable the second behavior as opt-in. Enable this behavior in Windows client application templates.
* Maintain the third behavior as opt-in (change defaults for `-r` and `--self-contained` for 3.0+).

The first behavior is most similar to the .NET Core 2.2 behavior. It is a good behavior because it builds an application with no assumptions and produces artifacts with the broadest scope and utility. Many developers will want the second behavior. It needs to be very easy to opt-in to that behavior, either via the CLI or with a single property in a template.

## Detailed Proposal

Many .NET Framework developers unfamiliar with .NET Core will start using the newer environment with .NET Core 3.0 and will expect a similar experience as they have been used to, particularly for those that only ever intend to target Windows with Windows Forms and WPF applications. Very few of them will expect Linux or macOS binaries in their build output. We need to deliver an experience they expect.

### Goals

The following broad goals should be satisfied for building applications with native dependencies:

* .NET CLI defaults (for architecture and native dependencies) are intuitive and align with key user scenarios.
* .NET CLI opt-in arguments (for architecture and native dependencies) have good usability for key user scenarios, with a bias to short and memorable arguments and good defaults for those arguments.
* It is straightforward to transform CLI opt-in arguments (for architecture and native dependencies) that enable your project use-case into msbuild properties in your project file, resulting in a custom default for that project.
* It is straightforward to produce deterministic builds, independent of OS or architecture of the build machine.
* It is easy and intuitive to share applications with others or deploy them to other machines (like a Rasperry Pi, or a Windows ARM64 laptop) as part of a fluid workflow.

The following are more specific goals to improve CLI usability:

* `build` and `publish` should have the same capability, except where there is a good scenario reason why a task should only be made available via `publish`.
* Defaults should be the same between `build` and `publish`.
* All common scenarios should work without specifying a RID if you are happy targeting the current machine.
* We should strongly avoid any breaking changes.

### Proposed Application Types

For the purposes of this document, there are three types of applications:

* **Architecture-neutral, framework-dependent** -- Applications of this kind target and can run on multiple architectures. They require an installed runtime to run (that matches the architecture of the target machine). They carry multiple sets of dependent native libraries for a variety of target architectures. All application and other architecture-agnostic  binaries are contained in a single directory and architecture-specific binaries are carried in RID-specific directories within a `runtimes` directory.
* **Architecture-specific, framework-dependent** -- Applications of this kind target and can only run on a single architecture, like Windows x64 or Linux ARM32. They require an installed runtime to run. They carry one set of dependent native libraries for a single target architecture. They include an executable launcher for the given architecture. All application binaries are contained in a single directory.
* **Architecture-specific, self-contained** -- Applications of this kind target and can only run on a single architecture, like Windows x64 or Linux ARM32. They carry a runtime and one set of dependent native libraries for a single target architecture. They include an executable launcher for the given architecture. All application, runtime and native binaries are contained in a single directory.

### Proposed CLI argument and MSBuild property changes

The following changes are proposed for .NET Core 3.0. `build` will be the specific verb discussed in the following text. The behavior is intended to be the same for `publish` unless specifically called out.

#### Framework-dependent architecture-neutral applications

Maintain framework-dependent architecture-netural applications as the default for `build`.
  
* Produces an application with native dependencies for all supported architectures.
* Remove the capability to produce EXEs for this type of build.
* If EXEs are desired for this build type, consider for a later release. It's a hard scenario to get right due to filename conflicts.

ASP.NET and console templates should accept this default.

#### Framework-dependent architecture-specific applications

Expose a new opt-in build type, called architecture-specific framework-dependent.

* Produces an application with native dependencies and an EXE for a single specified architecture.
* This build type can be selected via
  * MSBuild: `<ArchitectureSpecific>true</ArchitectureSpecific>`
  * CLI: `--architecture-specific` or `-a`
* A RID must be specified (or it is an error). Any of the following options is legal to specify a RID:
  * MSBuild: `<RuntimeIdentifier>RID</RuntimeIdentifier>`
  * CLI: `--runtime RID`, or `-r RID`
  * MSBuild: `<RuntimeIdentifier>$(NETCoreSdkRuntimeIdentifier)</RuntimeIdentifier>` (means: use RID of SDK; this already works)
* With this build type, `--self-contained` is `false` by default, even when a RID is specified. `--self-contained` or `--self-contained true` must be specified to produce a self-contained build.

WPF and Windows Forms templates should opt-in to this build type, with the following pattern:

```xml
<ArchitectureSpecific>true</ArchitectureSpecific>
<RuntimeIdentifier>win-x86</RuntimeIdentifier>
```

The `win-x86` RID is proposed as the default to match the 32-bit default for .NET Framework projects. There is a perf benefit and it will be more compatible with existing .NET Framework projects that may have 32-bit only dependencies. It does mean that we need to install the Windows 32-bit .NET Core runtime by default (the 32-bit SDK is not needed). Visual Studio should make it straightforward to switch the default RID to `win-x64`.

Note: It is possible that the RID should be specified a different way that better aligns with the Visual Studio configuration manager. This document doesn't take an opinion on that. it is a detail to work out.

Alternative idea for specifying architecture:

* MSBuild: `<ArchitectureSpecific>RID</ArchitectureSpecific>`

This alternative idea requires one instead of two lines, but also replaces the existing `RuntimeIdentifier` property, which can be viewed as good or bad.

#### Self-contained architecture-specific applications

Maintain the existing self-contained behavior as-is for .NET Core 2.2 apps and earlier, but change it for 3.0 apps and later.

The behavior where specifying a runtime (via `-r`) is convenient but confusing and unfortunate given that we consider framework-dependent applications to be the default. The proposal for framework-dependent architecture-specific apps makes self-contained opt-in when a RID is specific. It would be better to do that for all build types, making the system easier to understand.

* For 1.x and 2.x application, `-r RID` assumes `--self-contained true`
* For 3.0+ application, `-r RID` assumes `--self-contained false` -- `--self-contained` or `--self-contained true` must be specified to produce a self-contained application.

## Exploration of current behavior for building .NET Core applications

The following sections explore the current behavior for building .NET Core applications. It is provided for informational purposes only and isn't needed to understand the propopsal.

### Architecture-neutral, framework-dependent applications with NO native dependencies

This type of application is the easiest to consider because it doesn't depend on native libraries. It benefits from the architecture-neutral nature of .NET binaries and doesn't need to consider the complications of native dependencies. As a result, it is out of scope of this document, but is important to compare to as a baseline.

The following example demonstrates the set of assets produced by an application of this type and the associated user experience.

```console
C:\git\testapps\threezeroapp>type threezeroapp.csproj
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>netcoreapp3.0</TargetFramework>
  </PropertyGroup>

</Project>

C:\git\testapps\threezeroapp>dir /b /s bin\*.dll bin\*.exe bin\*.so bin\*.dylib
C:\git\testapps\threezeroapp\bin\Debug\netcoreapp3.0\threezeroapp.dll
C:\git\testapps\threezeroapp\bin\Debug\netcoreapp3.0\threezeroapp.exe

C:\git\testapps\threezeroapp>bin\Debug\netcoreapp3.0\threezeroapp.exe
Hello World!

C:\git\testapps\threezeroapp>dotnet bin\Debug\netcoreapp3.0\threezeroapp.dll
Hello World!
```

The resulting application is very simple, containing only a .NET dll with the application logic and a native launcher (aka "app host").

The application is invoked in two different ways, once with a native launcher (that can only be used on X64 Windows, due to being built with the Windows X64 .NET Core SDK) and the other with the standard dotnet launcher enabling the application to be used anywhere. The dichotomy of these choices is discussed later in the document. Only one of the launchers will be used in subsequent examples.

### Architecture-neutral, framework-dependent applications with native dependencies

Architectural neutral applications with native dependencies must contain native assets for all supported operating systems and architectures. This means that this type of applications needs to include multiple builds of the same native library, in order to provide a compatible asset on each native environment on which the application is supported to run.

The architecture-neutral model depends on three characteristics: an installed .NET Core runtime (for that environment) on the target machine, multiple variants of dependent native binaries carried with the application (the .NET assembly binder know how to pick the correct one), application binaries are composed of architectural-neutral byte code (IL) as opposed to pre-compiled code (using crossgen). This is a great model for cases where a developer wants to deploy the same build of an application to machines that use multiple operating systems and chips. It isn't a good option for developers that use conditional compilation that targets operating system or chip.

The following example demonstrates the set of assets produced by an application of this type and the associated user experience. This application depends on the `Microsoft.ML.CpuMath` NuGet library, which includes native binaries that support a variety of operating systems and chip combinations.

```console
C:\git\testapps\cpumath>type cpumath.csproj
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>netcoreapp3.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.ML.CpuMath" Version="0.11.0" />
  </ItemGroup>

</Project>

C:\git\testapps\cpumath>dotnet build
Microsoft (R) Build Engine version 16.1.21-preview+gc36f772a85 for .NET Core
Copyright (C) Microsoft Corporation. All rights reserved.

  Persisting no-op dg to C:\git\testapps\cpumath\obj\cpumath.csproj.nuget.dgspec.json
  Restore completed in 105.76 ms for C:\git\testapps\cpumath\cpumath.csproj.
C:\Program Files\dotnet\sdk\3.0.100-preview4-010761\Sdks\Microsoft.NET.Sdk\targets\Microsoft.NET.RuntimeIdentifierInference.targets(151,5): message NETSDK1057: You are using a preview version of .NET Core. See: https://aka.ms/dotnet-core-preview [C:\git\testapps\cpumath\cpumath.csproj]
  cpumath -> C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\cpumath.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:02.11

C:\git\testapps\cpumath>dir /s /b bin\*.dll bin\*.exe bin\*.so bin\*.dylib
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\cpumath.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\Microsoft.ML.CpuMath.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\cpumath.exe
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\runtimes\linux-x64\native\libCpuMathNative.so
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\runtimes\osx-x64\native\libCpuMathNative.dylib
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\runtimes\win-x64\native\CpuMathNative.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\runtimes\win-x86\native\CpuMathNative.dll

C:\git\testapps\cpumath>bin\Debug\netcoreapp3.0\cpumath.exe
Hello World!
```

The resulting application contains all architecture-agnostic code within the root directory and then multiple copies of native libraries in RID-specific `runtime` directories that support the various environments. The .NET Core assembly binder discovers and loads the correct native library for the given environment. The launcher is specific to the RID of the SDK that was used to build the application.

The intention of this model is that the build output will be deployed as a uniform quantity on all operating systems, even though only one quarter of the native assets (in this particular example) will be used in any given environment. The benefit is that you only to need to produce a single build of an application. The downside is that the application will be larger than it needs to be because it contains the aggregate of native assets that will be needed across all supported deployments. If that's not desirable, then the next option might be a better option.

### Architecture-specific, framework-dependent applications with native dependencies

Framework-dependent architecture-specific applications only contain native assets for the specific environment that they support. That specialization can help reduce the size and complexity (in terms of disk layout) of framework-dependent applications that depend of native assets.

The following example demonstrates the set of assets produced by an application of this type and the associated user experience, using the same `Microsoft.ML.CpuMath` package used earlier. In order to produce the desired application, the target architecture as a runtime ID (RID) must be specified as well as opting out of the default self-contained behavior (when a RID is specified).

```console
C:\git\testapps\cpumath>dotnet build -r win-x64 --self-contained false
Microsoft (R) Build Engine version 16.1.21-preview+gc36f772a85 for .NET Core
Copyright (C) Microsoft Corporation. All rights reserved.

MSBUILD : error MSB1001: Unknown switch.
Switch: --self-contained

For switch syntax, type "MSBuild -help"
```

That approach doesn't currently work. Instead, we can publish the application with `publish`, which produces the expected results. Due to the way `publish` works, we have two separate copies of the application. The `publish` directory is the one to pay attention to.

```console
C:\git\testapps\cpumath>dotnet publish -r win-x64 --self-contained false
Microsoft (R) Build Engine version 16.1.21-preview+gc36f772a85 for .NET Core
Copyright (C) Microsoft Corporation. All rights reserved.

  Persisting no-op dg to C:\git\testapps\cpumath\obj\cpumath.csproj.nuget.dgspec.json
  Restore completed in 109.27 ms for C:\git\testapps\cpumath\cpumath.csproj.
C:\Program Files\dotnet\sdk\3.0.100-preview4-010761\Sdks\Microsoft.NET.Sdk\targets\Microsoft.NET.RuntimeIdentifierInference.targets(151,5): message NETSDK1057: You are using a preview version of .NET Core. See: https://aka.ms/dotnet-core-preview [C:\git\testapps\cpumath\cpumath.csproj]
  cpumath -> C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\win-x64\cpumath.dll
  cpumath -> C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\win-x64\publish\

C:\git\testapps\cpumath>dir /s /b bin\*.dll bin\*.exe bin\*.so bin\*.dylib
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\win-x64\cpumath.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\win-x64\CpuMathNative.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\win-x64\Microsoft.ML.CpuMath.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\win-x64\cpumath.exe
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\win-x64\publish\cpumath.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\win-x64\publish\CpuMathNative.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\win-x64\publish\Microsoft.ML.CpuMath.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\win-x64\publish\cpumath.exe
```

The resulting application contains all binaries, architecture-agnostic and architecture-specific, in the same directory. In this case, look at the `C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\win-x64\publish\` directory.

The command-line required to enable this style of build is long and may be hard for people to remember. Those options can be similarly added to a project file for a similar result (what we would have been expected if `dotnet build` had worked as desired).

```console
C:\git\testapps\cpumath>type cpumath.csproj
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>netcoreapp3.0</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>false</SelfContained>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.ML.CpuMath" Version="0.11.0" />
  </ItemGroup>

</Project>

C:\git\testapps\cpumath>dotnet build
Microsoft (R) Build Engine version 16.1.21-preview+gc36f772a85 for .NET Core
Copyright (C) Microsoft Corporation. All rights reserved.

  Persisting no-op dg to C:\git\testapps\cpumath\obj\cpumath.csproj.nuget.dgspec.json
  Restore completed in 106.23 ms for C:\git\testapps\cpumath\cpumath.csproj.
C:\Program Files\dotnet\sdk\3.0.100-preview4-010761\Sdks\Microsoft.NET.Sdk\targets\Microsoft.NET.RuntimeIdentifierInference.targets(151,5): message NETSDK1057: You are using a preview version of .NET Core. See: https://aka.ms/dotnet-core-preview [C:\git\testapps\cpumath\cpumath.csproj]
  cpumath -> C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\win-x64\cpumath.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.11

C:\git\testapps\cpumath>dir /s /b bin\*.dll bin\*.exe bin\*.so bin\*.dylib
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\win-x64\cpumath.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\win-x64\CpuMathNative.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\win-x64\Microsoft.ML.CpuMath.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\win-x64\cpumath.exe
```

The resulting application is the same as the one demonstrated above, with `publish`, but in this case with `build`, which produces simpler output.

A further optimization on this experience is changing the project file to always build for the RID of the SDK you are using, which will always result in an application that targets the current OS and chip combination. The following project file can be used for that need. It will produce a similar result, depending on the RID of the SDK used to build an application.

```console
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>netcoreapp3.0</TargetFramework>
    <RuntimeIdentifier>$(NETCoreSdkRuntimeIdentifier)</RuntimeIdentifier>
    <SelfContained>false</SelfContained>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.ML.CpuMath" Version="0.11.0" />
  </ItemGroup>

</Project>
```

### Architecture-neutral but explicitly limited, framework-dependent applications with native dependencies

The next intuitive step is to extend the single RID listed in the project file to multiple, with the goal of supporting a smaller set of runtimes than what an underlying library supports. This is a hybrid case of the two preceding models. Let's suppose we want to support Windows X64 and Linux X64. This model could be used to build a Windows version (that supports x86, x64 and ARM64) and a separate Linux version. The split can be on whatever pivot you want, not just the one chosen in this example. It should be possible to enable our chosen example with the addition of the `RuntimeIdentifiers` property in the project file.

```console
C:\git\testapps\cpumath>type cpumath.csproj
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>netcoreapp3.0</TargetFramework>
    <RuntimeIdentifiers>win-x64;linux-x64</RuntimeIdentifiers>
    <SelfContained>false</SelfContained>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.ML.CpuMath" Version="0.11.0" />
  </ItemGroup>

</Project>

C:\git\testapps\cpumath>dotnet build
Microsoft (R) Build Engine version 16.1.21-preview+gc36f772a85 for .NET Core
Copyright (C) Microsoft Corporation. All rights reserved.

  Persisting no-op dg to C:\git\testapps\cpumath\obj\cpumath.csproj.nuget.dgspec.json
  Restore completed in 192.52 ms for C:\git\testapps\cpumath\cpumath.csproj.
C:\Program Files\dotnet\sdk\3.0.100-preview4-010761\Sdks\Microsoft.NET.Sdk\targets\Microsoft.NET.RuntimeIdentifierInference.targets(151,5): message NETSDK1057: You are using a preview version of .NET Core. See: https://aka.ms/dotnet-core-preview [C:\git\testapps\cpumath\cpumath.csproj]
  cpumath -> C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\cpumath.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.23

C:\git\testapps\cpumath>dir /s /b bin\*.dll bin\*.exe bin\*.so bin\*.dylib
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\cpumath.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\Microsoft.ML.CpuMath.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\cpumath.exe
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\runtimes\linux-x64\native\libCpuMathNative.so
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\runtimes\osx-x64\native\libCpuMathNative.dylib
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\runtimes\win-x64\native\CpuMathNative.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\runtimes\win-x86\native\CpuMathNative.dll

C:\git\testapps\cpumath>
```

Strangely, the runtimes are not limited in this case. That seems like a bug. The `RuntimeIdentifier` property had no observable effect.

I tried the CLI equivalent of the same thing, but got a strange error.

```console
C:\git\testapps\cpumath>dotnet publish -r linux-x64 -r win-x64
Microsoft (R) Build Engine version 16.1.46-preview+ge12aa7ba78 for .NET Core
Copyright (C) Microsoft Corporation. All rights reserved.

MSBUILD : error MSB1009: Project file does not exist.
Switch: win-x64
```

### Architecture-specific, self-contained applications

Self-contained applications have one model, architecture-specific, with all dependencies contained in a single directory. As a result, it doesn't matter (for the purpose of this document) if a self-contained application contains native dependencies.

The following example demonstrates the set of assets produced by an application of this type and the associated user experience, using the same `cpumath` example used earlier. A target runtime is specified as part of building the application.

```console
C:\git\testapps\gpioapp>dotnet build -r win-x64
Microsoft (R) Build Engine version 16.1.46-preview+ge12aa7ba78 for .NET Core
Copyright (C) Microsoft Corporation. All rights reserved.

  Restore completed in 238.61 ms for C:\git\testapps\gpioapp\gpioapp.csproj.
C:\Program Files\dotnet\sdk\3.0.100-preview4-011022\Sdks\Microsoft.NET.Sdk\targets\Microsoft.NET.RuntimeIdentifierInference.targets(151,5): message NETSDK1057: You are using a preview version of .NET Core. See: https://aka.ms/dotnet-core-preview [C:\git\testapps\gpioapp\gpioapp.csproj]
  gpioapp -> C:\git\testapps\gpioapp\bin\Debug\netcoreapp3.0\win-x64\gpioapp.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.15

C:\git\testapps\cpumath>dir /s /b bin\*.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\win-x64\api-ms-win-core-console-l1-1-0.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\win-x64\api-ms-win-core-datetime-l1-1-0.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\win-x64\api-ms-win-core-debug-l1-1-0.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\win-x64\api-ms-win-core-errorhandling-l1-1-0.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\win-x64\api-ms-win-core-file-l1-1-0.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\win-x64\api-ms-win-core-file-l1-2-0.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\win-x64\api-ms-win-core-file-l2-1-0.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\win-x64\api-ms-win-core-handle-l1-1-0.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\win-x64\api-ms-win-core-heap-l1-1-0.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\win-x64\api-ms-win-core-interlocked-l1-1-0.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\win-x64\api-ms-win-core-libraryloader-l1-1-0.dll
<snip/> -- many more lines follow -- including the application files

C:\git\testapps\cpumath>dir /s /b bin\cpu*
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\win-x64\cpumath.deps.json
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\win-x64\cpumath.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\win-x64\cpumath.exe
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\win-x64\cpumath.pdb
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\win-x64\cpumath.runtimeconfig.dev.json
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\win-x64\cpumath.runtimeconfig.json
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\win-x64\CpuMathNative.dll

C:\git\testapps\cpumath>dir /s /b bin\*.so bin\*.dylib
File Not Found
```

The resulting application contains the application, architecture-specific dependent binaries and an architecture-specific .NET Core runtime.

### Architecture-specific, self-contained applications

Like we did with framework-dependent applications in the prior example, we might want build an application as self-contained  for multiple architectures. Let's try that.

```console
C:\git\testapps\cpumath>type cpumath.csproj
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>netcoreapp3.0</TargetFramework>
    <RuntimeIdentifiers>linux-x64;win-x64</RuntimeIdentifiers>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.ML.CpuMath" Version="0.11.0" />
  </ItemGroup>

</Project>

C:\git\testapps\cpumath>dotnet build
Microsoft (R) Build Engine version 16.1.21-preview+gc36f772a85 for .NET Core
Copyright (C) Microsoft Corporation. All rights reserved.

  Persisting no-op dg to C:\git\testapps\cpumath\obj\cpumath.csproj.nuget.dgspec.json
  Restore completed in 194.37 ms for C:\git\testapps\cpumath\cpumath.csproj.
C:\Program Files\dotnet\sdk\3.0.100-preview4-010761\Sdks\Microsoft.NET.Sdk\targets\Microsoft.NET.RuntimeIdentifierInference.targets(151,5): message NETSDK1057: You are using a preview version of .NET Core. See: https://aka.ms/dotnet-core-preview [C:\git\testapps\cpumath\cpumath.csproj]
  cpumath -> C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\cpumath.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.32

C:\git\testapps\cpumath>dir /s /b bin\*.dll bin\*.exe bin\*.so bin\*.dylib
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\cpumath.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\Microsoft.ML.CpuMath.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\cpumath.exe
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\runtimes\linux-x64\native\libCpuMathNative.so
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\runtimes\osx-x64\native\libCpuMathNative.dylib
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\runtimes\win-x64\native\CpuMathNative.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\runtimes\win-x86\native\CpuMathNative.dll
```

This doesn't produce the result we wanted. Let's try again.

```console
C:\git\testapps\cpumath>type cpumath.csproj
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>netcoreapp3.0</TargetFramework>
    <RuntimeIdentifiers>linux-x64;win-x64</RuntimeIdentifiers>
    <SelfContained>true</SelfContained>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.ML.CpuMath" Version="0.11.0" />
  </ItemGroup>

</Project>

C:\git\testapps\cpumath>dotnet build
Microsoft (R) Build Engine version 16.1.21-preview+gc36f772a85 for .NET Core
Copyright (C) Microsoft Corporation. All rights reserved.

  Persisting no-op dg to C:\git\testapps\cpumath\obj\cpumath.csproj.nuget.dgspec.json
  Restore completed in 192.85 ms for C:\git\testapps\cpumath\cpumath.csproj.
C:\Program Files\dotnet\sdk\3.0.100-preview4-010761\Sdks\Microsoft.NET.Sdk\targets\Microsoft.NET.RuntimeIdentifierInference.targets(127,5): error NETSDK1031: It is not supported to build or publish a self-contained application without specifying a RuntimeIdentifier.  Please either specify a RuntimeIdentifier or set SelfContained to false. [C:\git\testapps\cpumath\cpumath.csproj]

Build FAILED.

C:\Program Files\dotnet\sdk\3.0.100-preview4-010761\Sdks\Microsoft.NET.Sdk\targets\Microsoft.NET.RuntimeIdentifierInference.targets(127,5): error NETSDK1031: It is not supported to build or publish a self-contained application without specifying a RuntimeIdentifier.  Please either specify a RuntimeIdentifier or set SelfContained to false. [C:\git\testapps\cpumath\cpumath.csproj]
    0 Warning(s)
    1 Error(s)

Time Elapsed 00:00:00.72
```

Intuitively, I expected `RuntimeIdentifiers` to have similar behavior as `TargetFrameworks`, as is demonstrated in the example below (which builds the application for multiple `TargetFrameworks` with a single `build` command).

```console
C:\git\testapps\cpumath>type cpumath.csproj
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFrameworks>netcoreapp3.0;netcoreapp2.1</TargetFrameworks>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.ML.CpuMath" Version="0.11.0" />
  </ItemGroup>

</Project>

C:\git\testapps\cpumath>dotnet build
Microsoft (R) Build Engine version 16.1.21-preview+gc36f772a85 for .NET Core
Copyright (C) Microsoft Corporation. All rights reserved.

  Persisting no-op dg to C:\git\testapps\cpumath\obj\cpumath.csproj.nuget.dgspec.json
  Restore completed in 114.14 ms for C:\git\testapps\cpumath\cpumath.csproj.
C:\Program Files\dotnet\sdk\3.0.100-preview4-010761\Sdks\Microsoft.NET.Sdk\targets\Microsoft.NET.RuntimeIdentifierInference.targets(151,5): message NETSDK1057: You are using a preview version of .NET Core. See: https://aka.ms/dotnet-core-preview [C:\git\testapps\cpumath\cpumath.csproj]
C:\Program Files\dotnet\sdk\3.0.100-preview4-010761\Sdks\Microsoft.NET.Sdk\targets\Microsoft.NET.RuntimeIdentifierInference.targets(151,5): message NETSDK1057: You are using a preview version of .NET Core. See: https://aka.ms/dotnet-core-preview [C:\git\testapps\cpumath\cpumath.csproj]
  cpumath -> C:\git\testapps\cpumath\bin\Debug\netcoreapp2.1\cpumath.dll
  cpumath -> C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\cpumath.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.17

C:\git\testapps\cpumath>dir /s /b bin\*.dll bin\*.exe bin\*.so bin\*.dylib
C:\git\testapps\cpumath\bin\Debug\netcoreapp2.1\cpumath.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\cpumath.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\Microsoft.ML.CpuMath.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\cpumath.exe
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\runtimes\linux-x64\native\libCpuMathNative.so
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\runtimes\osx-x64\native\libCpuMathNative.dylib
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\runtimes\win-x64\native\CpuMathNative.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\runtimes\win-x86\native\CpuMathNative.dll
```

This output looks very suspicions, having native assets only for one of the two TFMs.

### .NET Framework application with native dependencies

We should take a look at what the behavior is like for using native dependencies with .NET Framework.

```console
C:\git\testapps\cpumath>type cpumath.csproj
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFrameworks>netcoreapp3.0;net472</TargetFrameworks>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.ML.CpuMath" Version="0.11.0" />
  </ItemGroup>

</Project>

C:\git\testapps\cpumath>dotnet build
Microsoft (R) Build Engine version 16.1.46-preview+ge12aa7ba78 for .NET Core
Copyright (C) Microsoft Corporation. All rights reserved.

  Restore completed in 219.86 ms for C:\git\testapps\cpumath\cpumath.csproj.
C:\Program Files\dotnet\sdk\3.0.100-preview4-011022\Sdks\Microsoft.NET.Sdk\targets\Microsoft.NET.RuntimeIdentifierInference.targets(151,5): message NETSDK1057: You are using a preview version of .NET Core. See: https://aka.ms/dotnet-core-preview [C:\git\testapps\cpumath\cpumath.csproj]
C:\Program Files\dotnet\sdk\3.0.100-preview4-011022\Sdks\Microsoft.NET.Sdk\targets\Microsoft.NET.RuntimeIdentifierInference.targets(151,5): message NETSDK1057: You are using a preview version of .NET Core. See: https://aka.ms/dotnet-core-preview [C:\git\testapps\cpumath\cpumath.csproj]
  cpumath -> C:\git\testapps\cpumath\bin\Debug\net472\cpumath.exe
  cpumath -> C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\cpumath.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.19

C:\git\testapps\cpumath>dir /s /b bin\*.dll bin\*.exe bin\*.so bin\*.dylib
C:\git\testapps\cpumath\bin\Debug\net472\Microsoft.ML.CpuMath.dll
C:\git\testapps\cpumath\bin\Debug\net472\System.Buffers.dll
C:\git\testapps\cpumath\bin\Debug\net472\System.Memory.dll
C:\git\testapps\cpumath\bin\Debug\net472\System.Numerics.Vectors.dll
C:\git\testapps\cpumath\bin\Debug\net472\System.Runtime.CompilerServices.Unsafe.dll
C:\git\testapps\cpumath\bin\Debug\net472\cpumath.exe
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\cpumath.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\Microsoft.ML.CpuMath.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\cpumath.exe
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\runtimes\linux-x64\native\libCpuMathNative.so
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\runtimes\osx-x64\native\libCpuMathNative.dylib
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\runtimes\win-x64\native\CpuMathNative.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp3.0\runtimes\win-x86\native\CpuMathNative.dll
```

This build results in two applications, one targeting .NET Framework 4.7.2 and the other targeting .NET Core 3.0. It is plain to see that the .NET Framework layout is simpler, aligning closest with the architecture-specific framework-dependent model described earlier.

I should note that the problem space of managing native dependencies (at least in theory) has been easier to manage with .NET Framework since .NET Framework applications only target Windows and by default run as 32-bit applications, while at the same time being supported on both 32- and 64-bit Windows (including ARM64).

### .NET Core 2.2 Behavior

.NET Core 2.2 has different behavior than .NET Core 3.0 in multiple ways. It is useful to define this behavior as the baseline experience for considering compatibility burden. Let's take a look.

Let's produce an architecture-neutral application with `build`.

```console
C:\git\testapps\cpumath>dotnet --version
2.2.105

C:\git\testapps\cpumath>dotnet build
Microsoft (R) Build Engine version 15.9.20+g88f5fadfbe for .NET Core
Copyright (C) Microsoft Corporation. All rights reserved.

  Restoring packages for C:\git\testapps\cpumath\cpumath.csproj...
  Generating MSBuild file C:\git\testapps\cpumath\obj\cpumath.csproj.nuget.g.props.
  Generating MSBuild file C:\git\testapps\cpumath\obj\cpumath.csproj.nuget.g.targets.
  Restore completed in 241.38 ms for C:\git\testapps\cpumath\cpumath.csproj.
  cpumath -> C:\git\testapps\cpumath\bin\Debug\netcoreapp2.2\cpumath.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.23

C:\git\testapps\cpumath>dir /s /b bin\*.dll bin\*.exe bin\*.so bin\*.dylib
C:\git\testapps\cpumath\bin\Debug\netcoreapp2.2\cpumath.dll
```

The resulting application is framework-dependent. It is very small because it relies on loading any dependent binaries from the NuGet cache (which will have already been restored). I stated above that we were building an architecture-neutral application, in the sense that no architecture was specified, but the resulting application can be thought of as architecture-specific because it is only intended to run on the developer machine (by virtue of not being complete, even for a framework-dependent application) and will only load native binaries from the NuGet cache for the current operating system and architecture.

Let's produce an architecture-specific application with `build`. In short, it doesn't do anything useful.

```console
C:\git\testapps\cpumath>dotnet --version
2.2.105

C:\git\testapps\cpumath>dotnet build -r win-x64
Microsoft (R) Build Engine version 15.9.20+g88f5fadfbe for .NET Core
Copyright (C) Microsoft Corporation. All rights reserved.

  Restoring packages for C:\git\testapps\cpumath\cpumath.csproj...
  Generating MSBuild file C:\git\testapps\cpumath\obj\cpumath.csproj.nuget.g.props.
  Generating MSBuild file C:\git\testapps\cpumath\obj\cpumath.csproj.nuget.g.targets.
  Restore completed in 246.19 ms for C:\git\testapps\cpumath\cpumath.csproj.
  cpumath -> C:\git\testapps\cpumath\bin\Debug\netcoreapp2.2\win-x64\cpumath.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.59

C:\git\testapps\cpumath>dir /s /b bin\*.dll bin\*.exe bin\*.so bin\*.dylib
C:\git\testapps\cpumath\bin\Debug\netcoreapp2.2\win-x64\cpumath.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp2.2\win-x64\hostfxr.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp2.2\win-x64\hostpolicy.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp2.2\win-x64\cpumath.exe
```

The resulting application isn't correct or useful. This result is a random set of files.

Let's produce an architecture-neutral application with `publish`.

```console
C:\git\testapps\cpumath>dotnet --version
2.2.105

C:\git\testapps\cpumath>dotnet publish
Microsoft (R) Build Engine version 15.9.20+g88f5fadfbe for .NET Core
Copyright (C) Microsoft Corporation. All rights reserved.

  Restoring packages for C:\git\testapps\cpumath\cpumath.csproj...
  Generating MSBuild file C:\git\testapps\cpumath\obj\cpumath.csproj.nuget.g.props.
  Generating MSBuild file C:\git\testapps\cpumath\obj\cpumath.csproj.nuget.g.targets.
  Restore completed in 233.52 ms for C:\git\testapps\cpumath\cpumath.csproj.
  cpumath -> C:\git\testapps\cpumath\bin\Debug\netcoreapp2.2\cpumath.dll
  cpumath -> C:\git\testapps\cpumath\bin\Debug\netcoreapp2.2\publish\

C:\git\testapps\cpumath>dir /s /b bin\*.dll bin\*.exe bin\*.so bin\*.dylib
C:\git\testapps\cpumath\bin\Debug\netcoreapp2.2\cpumath.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp2.2\publish\cpumath.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp2.2\publish\Microsoft.ML.CpuMath.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp2.2\publish\runtimes\linux-x64\native\libCpuMathNative.so
C:\git\testapps\cpumath\bin\Debug\netcoreapp2.2\publish\runtimes\osx-x64\native\libCpuMathNative.dylib
C:\git\testapps\cpumath\bin\Debug\netcoreapp2.2\publish\runtimes\win-x64\native\CpuMathNative.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp2.2\publish\runtimes\win-x86\native\CpuMathNative.dll
```

The resulting application is architecture-neutral framework-dependent, which aligns closely with the same behavior as `dotnet build` and `dotnet publish` with .NET Core 3.0.

Let's produce an architecture-specific application with `publish`.

```console
C:\git\testapps\cpumath>dotnet --version
2.2.105

C:\git\testapps\cpumath>dotnet publish -r win-x64
Microsoft (R) Build Engine version 15.9.20+g88f5fadfbe for .NET Core
Copyright (C) Microsoft Corporation. All rights reserved.

  Restoring packages for C:\git\testapps\cpumath\cpumath.csproj...
  Generating MSBuild file C:\git\testapps\cpumath\obj\cpumath.csproj.nuget.g.props.
  Generating MSBuild file C:\git\testapps\cpumath\obj\cpumath.csproj.nuget.g.targets.
  Restore completed in 272.61 ms for C:\git\testapps\cpumath\cpumath.csproj.
  cpumath -> C:\git\testapps\cpumath\bin\Debug\netcoreapp2.2\win-x64\cpumath.dll
  cpumath -> C:\git\testapps\cpumath\bin\Debug\netcoreapp2.2\win-x64\publish\

C:\git\testapps\cpumath>dir /s /b bin\*.dll bin\*.exe bin\*.so bin\*.dylib
C:\git\testapps\cpumath\bin\Debug\netcoreapp2.2\win-x64\cpumath.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp2.2\win-x64\hostfxr.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp2.2\win-x64\hostpolicy.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp2.2\win-x64\cpumath.exe
C:\git\testapps\cpumath\bin\Debug\netcoreapp2.2\win-x64\publish\api-ms-win-core-console-l1-1-0.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp2.2\win-x64\publish\api-ms-win-core-datetime-l1-1-0.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp2.2\win-x64\publish\api-ms-win-core-debug-l1-1-0.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp2.2\win-x64\publish\api-ms-win-core-errorhandling-l1-1-0.dll
<snip/> -- many more lines follow -- including the application files
```

The resulting application is architecture-specific and self-contained. This is the same as it would be with .NET Core 3.0.

Let's produce an architecture-specific framework-dependent application with `publish`.

```console
C:\git\testapps\cpumath>dotnet --version
2.2.105

C:\git\testapps\cpumath>dotnet publish -r win-x64 --self-contained false
Microsoft (R) Build Engine version 15.9.20+g88f5fadfbe for .NET Core
Copyright (C) Microsoft Corporation. All rights reserved.

  Restoring packages for C:\git\testapps\cpumath\cpumath.csproj...
  Generating MSBuild file C:\git\testapps\cpumath\obj\cpumath.csproj.nuget.g.props.
  Generating MSBuild file C:\git\testapps\cpumath\obj\cpumath.csproj.nuget.g.targets.
  Restore completed in 256.12 ms for C:\git\testapps\cpumath\cpumath.csproj.
  cpumath -> C:\git\testapps\cpumath\bin\Debug\netcoreapp2.2\win-x64\cpumath.dll
  cpumath -> C:\git\testapps\cpumath\bin\Debug\netcoreapp2.2\win-x64\publish\

C:\git\testapps\cpumath>dir /s /b bin\*.dll bin\*.exe bin\*.so bin\*.dylib
C:\git\testapps\cpumath\bin\Debug\netcoreapp2.2\win-x64\cpumath.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp2.2\win-x64\cpumath.exe
C:\git\testapps\cpumath\bin\Debug\netcoreapp2.2\win-x64\publish\cpumath.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp2.2\win-x64\publish\CpuMathNative.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp2.2\win-x64\publish\Microsoft.ML.CpuMath.dll
C:\git\testapps\cpumath\bin\Debug\netcoreapp2.2\win-x64\publish\cpumath.exe
```

The resulting application is architecture-specific framework-dependent, targeting Windows x64 in this example.
