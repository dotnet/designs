# Experimental APIs

**Owner** [Immo Landwerth](https://github.com/terrjobst)

In .NET 6, we've added a way to [ship preview features][preview-features] in
otherwise stable releases. It works by tagging APIs (and language features) as
preview features and requiring the user's project to explicitly opt-into using
them via the `<EnablePreviewFeatures>` property.

The feature was primarily designed to accommodate platform-level preview
features, that is for features that span runtime, library, and language, such as
generic math. However, we also believed that the feature would work for
libraries that want to offer preview functionality in otherwise stable NuGet
packages.

Since then, we've seen requests from partners who have tried using it and hit
limitations that come from guardrails we put into place due to the complex
nature of platform-level preview features.

The goal of this feature is to find a design that works for both, platform-level
preview features and library-level preview features.

Let's first define these two terms:

* **platform-level preview feature**. These are features that rely on preview
  behaviors across runtime, core library, tooling, and languages. The nature of
  these features is that they are in support of each other. For example, generic
  math requires new runtime behaviors for interfaces, which the language has to
  support, and of course new interfaces and methods in core library that
  leverages them. If you want to use generic math, you need a combination of
  runtime, framework, and language that were designed to work in conjunction.

* **library-level preview feature**. These are library APIs that don't require
  any platform-level preview features to be consumed. The only difference to
  regular APIs is that these are in preview so their shape isn't stable and thus
  offers no backwards compatibility when upgrading to a later version of the
  library.

Due to their nature, platform-level preview features can only be consumed in a
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

Another concern we had for platform-level preview features is that we wanted to
ensure that application developers are aware when their applications have a
dependency on them. That is, we want to avoid a case where a library on NuGet
uses platform-level preview features as an implementation detail that the
application consumer isn't aware of. So we made the opt-in mechanism viral: a
library that turns on platform-level preview features requires its consumer to
turn them on as well.

Since library-level preview features cannot change how the .NET runtime itself
operates, we don't have that same level of concern. In fact, we believe the
viral nature is also undesirable.

For the remainder of this proposal we'll refer to *library-level preview*
features as *experimental APIs*.

## Scenarios and User Experience

## Requirements

### Goals

* Don't change the guardrails for platform-level preview features:
    - They require opt-in (without, the build fails with an error)
    - They require to be viral (i.e. opt-in is also required for indirect
      dependencies relying on preview features)
    - The opt-in can be expressed centrally in the project file
    - They require SDK and framework to match

* Offer library-level preview features with a different set of guardrails:
    - They require opt-in (without, the build fails with an error)
    - The opt-in isn't viral and can be an implementation detail
    - The opt-in is centralized, but it's specific to each preview feature
    - Using them poses no requirements on the SDK version

### Non-Goals

* Relaxing or extending the guardrails for platform-level preview features

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

### Attribute

We'll create a new attribute, separate from the existing `System.Runtime.Versioning.RequiresPreviewFeaturesAttribute`:

```C#
namespace System.Diagnostics.CodeAnalysis;

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
public sealed class ExperimentalAttribute : Attribute
{
    public ExperimentalAttribute(string diagnosticId);
    public string DiagnosticId { get; }
    public string? UrlFormat { get; set; }
}
```

### Compiler Behavior

The compiler will raise a diagnostic when an experimental API is used, using the
supplied diagnostic ID. The severity is always error.

The semantics are identical to how obsolete is tracked, except that it will
always raise a diagnostic regardless of whether the call site is marked as
experimental or not. There is also no special treatment when both caller and
callee are in the same assembly -- any use will generate a diagnostic and the
caller is expected to suppress it, with the usual means (i.e. `#pragma` or
project-wide `NoWarn`).

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

## Q & A

### Why didn't we evolve the existing RequiresPreviewFeaturesAttribute?

We considered that initially, but there are major differences in the desired
user experience for `RequiresPreviewFeaturesAttribute` and library preview
features:

1. `RequiresPreviewFeaturesAttribute` is designed to be viral. That is, by
   default any library that turns it on also requires its consumers to turn it
   on. This makes sense for runtime preview features but not so much for
   libraries who simply offer some APIs that aren't stable yet.

2. `RequiresPreviewFeaturesAttribute` is designed to be managed via a single
   MSBuild property, specifically `EnablePreviewFeatures`. This is desirable for
   library features where wanting to use a preview API in some library doesn't
   necessarily mean that one wants to use any preview library of any library.

3. `RequiresPreviewFeaturesAttribute` also covers tooling preview features and
   generally requires the runtime and SDK to match. That is, you cannot use
   preview features in `net6.0` unless you're using the .NET 6.0 SDK. For
   example, if you try to use the .NET 7.0 SDK, you'll get a build error telling
   you that this isn't supported. This alignment isn't necessary for library
   preview APIs and in fact highly undesirable.

We considered offering a different mode for `RequiresPreviewFeaturesAttribute`
instead of using a different attribute but we believe this makes it harder to
understand as these are really disjoint features. Having different names for
them makes it easier to communicate that difference to our users.

### Why did we use the term `Experimental` over `Preview`?

This change was based on [this discussion][experimental-discussion]:

1. It more strongly separates the existing `RequiresPreviewFeatures` from this
   new feature.

2. Folks found it it better aligns with the naming of the community and other
   parts of our stack that has the same semantics (such as WinRT).

[experimental-discussion]: https://github.com/dotnet/designs/pull/285#discussion_r1082052754

### Why is this not an analyzer?

The compiler team suggested to make this a compiler behavior, rather than an
analyzer. This would allow the attribute to specify custom `DiagnosticId` values
which regular analyzers can't (because a given analyzer has to tell the compiler
upfront which diagnostic IDs it will raise).
