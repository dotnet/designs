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

For the remainder of this proposal we'll refer to *library-level preview*
features as *experimental APIs*.

## Scenarios and User Experience

### Shipping an experimental API in a stable package

Martin builds a library that integrates with ASP.NET Core and provides features
to make it easier to build services. The library is distributed as a NuGet
package. The package is already in a version well past 1.0 and is considered
stable.

He ships the package often and doesn't want to maintain multiple flavors of the
package, one marked as prerelease and one marked as stable. However, he wants to
expose new APIs without making the promise that these APIs are stable. He
designs the new APIs as if they would be stable, that is, he puts them in the
namespace they belong and names the types and members without any naming
convention that implies that they aren't stable yet. If customer feedback is
positive, the API will become stable as-is, otherwise he'll make changes to
address the feedback.

In order to convey to the consumers that these APIs aren't stable yet, he puts
the `Experimental` attribute on them:

```C#
namespace MartinsLibrary.FancyLogging
{
    public static class FancyLoggerExtensions
    {
        [Experimental("ML123", UrlFormat="https://martinslibrary.net/diagnostics/{0}")]
        public static IServiceCollection AddFancyLogging(this IServiceCollections services)
        {
            // ...
        }
    }
}
```

### Consuming an experimental API

David builds a new version of Jabber.NET and consumes Martin's library. He very
much wants to play with the new fancy logging Martin added. When David calls
`AddFancyLogging` he gets a compilation error:

> error ML123: FancyLoggerExtensions.AddFancyLogging() is not a stable API. In
  order to consume this experimental API, suppress this error from the call
  site.

David is happy to accommodate future breaking changes around the fancy logging
feature. He understands that the diagnostic ID is specific to the fancy logging
feature of Martin's library, so he decides to add a suppression to the project
file:

```xml
<Project>
    <PropertyGroup>
        <!-- Fancy logging is an experimental APIs -->
        <NoWarn>$(NoWarn);ML123</NoWarn>
    </PropertyGroup>
</Project>
```

### Consuming another experimental API

Later, David discovers Martin's library also has support for fancy serialization.
When he starts consuming it, he gets another compilation error:

> error ML456: FancySerializationExtensions.AddFancySerialization() is not a
  stable API. In order to consume this experimental API, suppress this error
  from the call site.

Giving it some thought, David concludes that the risk of using an unstable
serialization API isn't worth it because it's not necessary for Jabber, so he
stops exploring that API.

## Requirements

### Goals

* Don't change the behavior for platform-level preview features

* Offer library-level preview features with a different set of guardrails:
    - Modelled after `[Obsolete]`, except the severity is always an error
    - The opting-in is expressed similar to obsolete, that is the call site is
      either marked as experimental itself or the error is suppressed via any of
      the existing ways (e.g. `<NoWarn>` or `#pragma`).
    - In order to make centralization possible, each feature is encouraged
      to have a separate diagnostic ID.
    - Using experimental APIs poses no requirements on the SDK version

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
    - Windows Forms
    - WPF
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

### Intended Usage

A library owner should use separate diagnostic IDs for each logical feature that
is considered not yet stable. All API entry points into that feature should be
marked with the same diagnostic ID. Like diagnostic IDs, the library owner is
encouraged to create a unique prefix.

The library consumer has multiple options to deal with experimental APIs:

1. **Mark the consuming API as experimental**. In those cases, the consumer can
  either re-use the same diagnostic ID to express that it merely depends on the
  same experimental feature, or introduce a separate diagnostic ID, which
  expresses there is a derived feature that is also experimental. This means if
  the underlying experimental API becomes stable, the derived feature might not
  yet be stable. In either case, the experimental nature is forwarded to the
  consumer's consumer, essentially making it viral.

2. **Suppress**. This basically states "I'm intending to shield my consumer from
  breaking changes that occur as a result of me using an experimental feature".
  The suppression an can be handled in two distinct ways, which we consider a
  coding style question:
   - Suppress individual call sites using `#pragma` or `[SuppressMessageMessage]
   - Suppress them centrally using `<NoWarn>` or `[assembly: SuppressMessageMessage]`

Whether or not the experimental nature is made viral or not is up to the
consumer of the experimental API. Since there is a built-in code fixer for all
diagnostics that enables suppression (2) we should probably also provide a code
fixer for (1) which applies the attribute with the same diagnostics. This way,
both options are being presented to the consumer.

### Compiler Behavior

*For more details on the compiler behavior, see [C# spec][csharp-spec].*

The compiler will raise a diagnostic when an experimental API is used, using the
supplied diagnostic ID.

> [!NOTE]
> The severity is warning, because errors cannot be suppressed. However, the
> warning is promoted to an error for purpose of reporting.

The semantics are identical to how obsolete is tracked, except there is no
special treatment when both caller and callee are in the same assembly -- any
use will generate a diagnostic and the caller is expected to suppress it, with
the usual means (i.e. `#pragma` or project-wide `NoWarn`).

## Tracking Items

* The overall work of enabling this feature for libraries
    - [dotnet/runtime#77869](https://github.com/dotnet/runtime/issues/77869)
* Shipping `RequiresPreviewFeaturesAttribute` for downlevel
    - [dotnet/runtime#79479](https://github.com/dotnet/runtime/issues/79479)
* Highlighting preview APIs in docs
    - [dotnet/dotnet-api-docs#6861](https://github.com/dotnet/dotnet-api-docs/issues/6861)
* Highlighting preview APIs in IDE
    - [dotnet/roslyn#65915](https://github.com/dotnet/roslyn/issues/65915)
* Consider highlighting preview APIs when APIs represent UI elements that aren't
  used via user written code
    - Areas to consider:
    - Toolbox
    - Property Grid
    - Design Service
* Consider providing an analyzer that warns when
  `RequiresPreviewFeaturesAttribute` is being used in (user written) code.
    - It should only be used by the platform
    - We should point users to this new attribute (`ExperimentalAttribute`)

[preview-features]: ../../2021/preview-features/preview-features.md
[csharp-spec]: https://github.com/dotnet/csharplang/blob/main/proposals/csharp-12.0/experimental-attribute.md

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

In the discussion of library-level preview features we concluded that we want to
model it as a peer to `[Obsolete]`:

1. **Can be viral or non-viral**. We ended up liking the fact that this mean it
   can both be viral and not viral, depending on wether the consumer suppresses
   it or marks themselves.

2. **Not a free-for all switch**. We don't want a single global property that
   enables all library preview features. Rather, we want to allow the caller
   opt-in those features separately.

3. **Not tied to the SDK version**. Since these features have no dependency on
 tooling preview features, there is no need for this constraint.

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
