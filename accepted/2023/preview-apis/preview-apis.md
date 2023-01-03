# Library Preview Features

**Owner** [Immo Landwerth](https://github.com/terrjobst)

In .NET 6, we've added a way to [ship preview features][preview-features] in
otherwise stable releases. It works by tagging APIs (and language features) as
preview features and requiring the user's project to explicitly opt-into using
them via the `<EnablePreviewFeatures>` property.

The feature was primarily designed to accommodate platform-level features, that
is for features that span runtime, library, and language, such as generic math.
However, we also believed that the feature would work for libraries that want to
offer preview functionality in otherwise stable NuGet packages.

Since then, we've seen requests from partners who have tried using it and hit
limitations that come from guardrails we put into place due to the complex
nature of platform-level preview features.

The goal of this feature is to find a design that works for both, platform-level
preview features and library-level preview features.

Let's first define these two terms:

* **platform-level preview feature**. These are features that rely on preview
 behaviors across runtime, core library, tooling, and languages. The nature of
 these features that they are in support of each other. For example, generic
 math requires new runtime behaviors for interfaces, which the language has to
 support, and of course new interfaces and methods in core library that
 leverages them. If you want to use generic math, you need a combination of
 runtime, framework, and language that were designed to work in conjunction.

* **library-level preview feature**. These are library APIs that don't require
  any platform-level preview features to be consumed. The only difference to
  regular APIs is that these are in preview so their shape isn't stable and thus
  offers no backwards compatibility when upgrading to a later version of the
  library.

Due to their nature, platform-level preview features can only be consumed in
specific combination of framework and SDK: namely, the framework and SDK version
must match. For example, you can only use preview features in `net6.0` when
using the .NET 6 SDK and only use preview features in `net7.0` when using the
.NET 7 SDK. Specifically, you can't use the .NET 7 SDK to use preview features
in `net6.0`. That's because preview features aren't backwards compatible and
platform-level preview features span both framework and tooling. In
multi-targeting scenarios this means you can only use preview features for the
latest framework version.

For platform-level preview features this restriction makes sense because it
models reality. However, for library-level preview features not only does this
restriction not make sense, it's highly undesirable. Library authors simply want
a way to express "these parts of my API surface might change". This statement
isn't tied to any runtime-, framework-, or language features.

## Scenarios and User Experience

## Requirements

### Goals

* Platform-level preview features need the same guardrails as before, specifically
    - They require opt-in (without, the build fails with an error)
    - They require to be viral (i.e. opt-in is also required for indirect
      dependencies relying on preview features)
    - They require SDK and framework to match
* Library-level preview need to have same guard rails, except for the
  requirement of SDK and framework to match

### Non-Goals

* Extending this feature to allow for multiple independent preview features

## Stakeholders and Reviewers

* Runtime teams
* C#, VB, F# compiler and IDE teams
* Library teams
    - Core libraries
    - ASP.NET
    - EF
    - Azure SDK
    - R9
* Project system team
* SDK team
* MSBuild team
* NuGet team

## Design

### Project file

We change the behavior of `EnablePreviewFeatures` to allow four values instead
of the current `bool`:

| Value                | Meaning                                                                                  |
| -------------------- | ---------------------------------------------------------------------------------------- |
| `False`              | Off. Neither platform- nor library-level preview features can be consumed.               |
| `True`               | Same as `LibraryAndPlatform`                                                             |
| `Library`            | You can consume library-level preview features, but not platform-level preview features. |
| `LibraryAndPlatform` | You can consume both library- and platform-level preview features.                       |

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <EnablePreviewFeatures>LibraryAndPlatform</EnablePreviewFeatures>
  </PropertyGroup>

</Project>
```

### SDK

Currently, the SDK ignores `<EnablePreviewFeatures>` unless the target framework
is the SDK's current version. This will change the behavior as follows:

If `<EnablePreviewFeatures>` is set to `LibraryAndPlatform` and the target
framework isn't the current version, it will be interpreted is if it was set to
`Library`.

### Attribute

We'll extend the `[RequiresPreviewFeatures]` to take in the scope. The existing
constructors will behave as if the scope is `PreviewScope.LibraryAndPlatform`:

```C#
namespace System.Runtime.Versioning;

public enum PreviewScope
{
    LibraryAndPlatform,
    Library
}

[AttributeUsage(AttributeTargets.Assembly |
                AttributeTargets.Module |
                AttributeTargets.Class |
                AttributeTargets.Struct |
                AttributeTargets.Enum |
                AttributeTargets.Constructor |
                AttributeTargets.Method |
                AttributeTargets.Property |
                AttributeTargets.Field |
                AttributeTargets.Event |
                AttributeTargets.Interface |
                AttributeTargets.Delegate, Inherited = false)]
public sealed class RequiresPreviewFeaturesAttribute : Attribute
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public RequiresPreviewFeaturesAttribute() {}
        : this(PreviewScope.LibraryAndPlatform) {}

    [EditorBrowsable(EditorBrowsableState.Never)]
    public RequiresPreviewFeaturesAttribute(string? message)
        : this(PreviewScope.LibraryAndPlatform, message) {}

    // NEW
    public RequiresPreviewFeaturesAttribute(PreviewScope scope) {}

    // NEW
    public RequiresPreviewFeaturesAttribute(PreviewScope scope, string? message) {}

    // NEW
    public PreviewScope scope { get; }

    public string? Message { get; }
    public string? Url { get; set; }
}
```

### Analyzer

The analyzer will be changed as follows:

* APIs marked with `PreviewScope.LibraryAndPlatform` can only be used if the
 consumer is also marked with `PreviewScope.LibraryAndPlatform`.

* APIs marked with `PreviewScope.Library` can only be used if the consumer is
  marked with either `PreviewScope.LibraryAndPlatform` or
  `PreviewScope.Library`.

We should add a new diagnostic ID when library-level preview APIs are being used
without opt-in (i.e. the second case above).

## Tracking Items

* The overall work of enabling this feature for libraries
    - [dotnet/runtime#77869](https://github.com/dotnet/runtime/issues/77869)
* Shipping `RequiresPreviewFeaturesAttribute` for downlevel
    - [dotnet/runtime#79479](https://github.com/dotnet/runtime/issues/79479)
* Highlighting preview APIs in docs
    - [dotnet/dotnet-api-docs#6861](https://github.com/dotnet/dotnet-api-docs/issues/6861)
* Highlighting preview APIs in IDE
    - [dotnet/roslyn#65915](https://github.com/dotnet/roslyn/issues/65915)

[preview-features]: ../../2021/preview-features/preview-features.md
