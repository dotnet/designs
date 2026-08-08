# Combined Tool and Library Packages

**Status**: Proposed

**Owner** [Chet Husk](https://github.com/baronfel)

## Summary

This proposal introduces a new packaging model for .NET that allows developers to create packages that serve dual purposes: as command-line tools and as libraries. This approach enables a single package to contain both executable tool functionality and traditional library assets, allowing developers to invoke functionality directly via the command line while also being able to reference the same package as a dependency in their projects.

## Motivation

.NET tools provide a convenient way to distribute and consume command-line utilities, but they exist in isolation from the libraries they might be built upon. This means that a developer must create multiple packages, push multiple package ids to feeds, consumers must search for and use seemingly-random package names, etc. Our current model forces a package author to choose which _modality_ of package consumption (tool or reference) gets the 'nice' package name, while the other unused modality gets whatever is left. Allowing for the nice/concise/expected package name to fill both needs helps with name recognition and makes a tool/library appear more cohesive. This mirrors patterns common in Node.js (`npm run <command>` / `npm bin <command>`) and Go (`go install` for CLI, regular import for library usage), bringing similar convenience to the .NET ecosystem.

## Scenarios and User Experience

### Scenario 1: Tool Author - Creating a Combined Package

A developer building a JSON schema validator wants to provide both a CLI tool for CI/CD pipelines and a library for programmatic validation within .NET applications.

```xml
<!-- JsonSchemaValidator.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>json-validate</ToolCommandName>
    <PackageId>JsonSchemaValidator</PackageId>
    <Version>1.0.0</Version>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  </ItemGroup>
</Project>
```

The project structure includes both a CLI entry point and library classes:

```csharp
// Program.cs - CLI entry point
class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: json-validate <schema-file> <json-file>");
            return 1;
        }

        var validator = new JsonValidator();
        var result = await validator.ValidateAsync(args[0], args[1]);
        Console.WriteLine(result.IsValid ? "Valid" : $"Invalid: {result.ErrorMessage}");
        return result.IsValid ? 0 : 1;
    }
}

// JsonValidator.cs - Library class
public class JsonValidator
{
    public async Task<ValidationResult> ValidateAsync(string schemaPath, string jsonPath)
    {
        // Implementation details...
    }
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public string ErrorMessage { get; set; }
}
```

When packed with `dotnet pack`, this creates a single combined package containing:
- Tool assets for CLI execution, packaged as a framework-dependent binary with an expected execution pattern of `dotnet <package_root>/tool/net10.0/any/JsonSchemaValidator.dll`
- Library assets (`lib/net10.0/JsonSchemaValidator.dll`)
- Reference assemblies (`ref/net10.0/JsonSchemaValidator.dll`)

### Scenario 2: Tool Consumer - Using as CLI Tool

Another developer wants to validate JSON files in their build pipeline:

```shell
user@host:~$ dnx -y JsonSchemaValidator schema.json data.json
Tool package JsonSchemaValidator@1.0.0 will be downloaded from source https://api.nuget.org/v3/index.json.
Valid
```

### Scenario 3: Library Consumer - Using as Dependency

The same developer wants to integrate JSON validation into their .NET application:

```shell
user@host:~$ dotnet package add JsonSchemaValidator
```

```csharp
using JsonSchemaValidator;

public class DocumentProcessor
{
    private readonly JsonValidator _validator = new();

    public async Task ProcessDocument(string document)
    {
        var result = await _validator.ValidateAsync("schema.json", document);
        if (!result.IsValid)
            throw new InvalidDataException(result.ErrorMessage);

        // Process valid document...
    }
}
```

### Scenario 4: Advanced Packaging Options

In the original scenario the 'combined' package contains the framework-dependent version of the binary - but for lowest startup time and best performance, a combined package can be built with different deployment models. This allows developers to choose the best option for their scenario.
In this case, AOT publishing is chosen by updating the project file:

```xml
<PublishAOT>true</PublishAOT>
<RuntimeIdentifiers>linux-x64;win-x64;macos-x64;linux-arm64;win-arm64;macos-arm64</RuntimeIdentifiers>
```

Updating the package-creation process:

* once on any build host
```shell
user@host~$: dotnet pack # make the top-level package
```
* and once on each platform-specific build host
```shell
user@host~$: dotnet pack --use-current-runtime # make the platform-specific tool package
```

When packed in this way this creates a 7 total packages:
* one platform-specific tool package for each runtime identifier (e.g., `JsonSchemaValidator.linux-x64.1.0.0.nupkg`)
* one combined package that contains the library assets and the 'tool manifest' for the platform-specific tool packages

To users this change is completely transparent - they interact with the package as a tool or as a library as in scenarios 2 and 3, but the package is now optimized for the platform it is running on.


## Making it work

To make this work, changes will be needed on both the consumption and packaging sides.

### Consumption changes for tools

* No changes required - as long as a package has the DotnetTool package type, `dnx` and all other tool-interaction commands will use the package as a tool.

### Consumption changes for libraries

The primary change that will be required is to loosen the checks made by the [CompatibilityChecker](https://github.com/NuGet/NuGet.Client/blob/4ce65d4a1c482eda1c8656fc032d1f5cf247763a/src/NuGet.Core/NuGet.Commands/RestoreCommand/CompatibilityChecker.cs) to allow using packages with the explicit PackageType of `Dependency` as a library - regardless of their other attributes. Today, this checking takes many characteristics of the package into account, but it has a [hard deny](https://github.com/NuGet/NuGet.Client/blob/4ce65d4a1c482eda1c8656fc032d1f5cf247763a/src/NuGet.Core/NuGet.Commands/RestoreCommand/CompatibilityChecker.cs#L339-L347) for `DotnetTool` packages. If this restriction is removed, the package can be used as a library without any other changes. Packages with no package type will continue to be treated as Dependency packages.

### Packaging changes

In order to create a combined package, we effectively need to call the standard library packing process _and_ the tool-specific packing process. This is easy enough - the tool packing process itself hooks into existing Packaging extensibility points - the nitpicky points are in how the users projects need to be laid out to efficiently create the combined package. A worked example of
what this might look like in practice can be found at [baronfel/multi-rid-tool#1](https://github.com/baronfel/multi-rid-tool/pull/1).

#### Managing the project type

The overall project needs to be considered as both a Library and an Exe by different parts of the process. We opt to instead have the user drop the `OutputType` property entirely (defaulting to `Library`), and have the `pack` and `run` commands orchestrate the correct behavior based on the value of the `PackAsTool` property instead. This is necessary in part because of the second issue:

#### Managing tool PackageReferences and Program.cs

It's very possible for the Tool expression of a project to need different dependencies than the Library expression. For example, a tool might need a command-line parsing library. To handle this, we can use conditional references in the project file to detect the 'mode' we are in:

```xml
<PackageReference Include="CommandLineParser" Version="2.9.1" Condition="'$(OutputType)' == 'Exe'" />
<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.0" />
```

However, ideally none of the _entrypoint_ (or code only used by the entrypoint) of the application would be included in the library portion of the package. To achieve this, we can condition the removal of the Entrypoint-related code from the project:

```xml
<ItemGroup>
    <Compile Remove="Program.cs" Condition="'$(OutputType)' == 'Library'" />
</ItemGroup>
```

If there is enough convention here, we could potentially automate around these kinds of modifications.


## Stakeholders and Reviewers

- .NET SDK team (packaging and tool execution)
- NuGet team (package format and discovery)

## Design

### Package Structure

Combined packages extend the existing NuGet package format to include both tool and library assets. For example, a framework-dependent, platform-agnostic combined package (i.e. scenario 1) might look like this:

```
JsonSchemaValidator.1.0.0.nupkg
├── tools/
│   └── net10.0/
│       └── any/
│           ├── json-validate.dll
│           ├── Newtonsoft.Json.dll
│           └── DotnetToolSettings.xml
├── lib/
│   └── net10.0/
│       └── JsonSchemaValidator.dll
├── ref/
│   └── net10.0/
│       └── JsonSchemaValidator.dll
└── [package metadata files]
```

Where scenario 4 (the AOT packages) might look like this for the library/tool manifest combined package:

```
JsonSchemaValidator.1.0.0.nupkg
├── tools/
│   └── net10.0/
│       └── any/
│           └── DotnetToolSettings.xml
├── lib/
│   └── net10.0/
│       └── JsonSchemaValidator.dll
├── ref/
│   └── net10.0/
│       └── JsonSchemaValidator.dll
└── [package metadata files]
```
and this for the platform-specific tool package(s):
```
JsonSchemaValidator.win-x64.1.0.0.nupkg
├── tools/
│   └── any/
│       └── win-x64/
│           ├── json-validate.exe
└── [package metadata files]
```

### Compatibility Considerations

- Existing .NET tool packages continue to work unchanged
- Existing library packages continue to work unchanged
- Combined packages can be consumed as either tools or libraries by consumers unaware of the dual nature
- NuGet clients that don't understand combined packages treat them as either tools or libraries based on which assets they recognize

## Q & A

### Why not create separate packages for tools and libraries?

While separate packages remain a valid approach, combined packages offer several advantages:
- Simplified version management - one package ID, one version
- Reduced maintenance burden for package authors
- Better discoverability - users finding the library can easily access the CLI tool and vice versa

### What happens if someone tries to use the package as both a tool and library in the same project?

This scenario works fine - the tool execution happens in a separate process via `dnx`, while the library reference works within the consuming project's process. There's no conflict between the two usage modes. NuGet disallows adding references to packages that are only tool packages, and will continue to do so. If a tool package _is_ referenced, it has no effect on the dependency graph due to not having any package assets in the locations that NuGet expects.

### How does this affect package size?

Depending on the deployment model chosen for the tool package, the impact to library package size is variable.
* For packages that lean into the RID-specific deployment model, the library package size remains similar to existing library packages - it only adds a single small XML manifest file to locate the platform-specific tools.
* For packages that prefer the framework-dependent, platform-agnostic deployment model, the library package will be increased by the size of the tool's runtime assets.

### Will this work with existing NuGet feeds and tooling?

Yes, combined packages use the standard NuGet package format with additional asset folders. Existing NuGet infrastructure, feeds, and tooling continue to work without modification.
