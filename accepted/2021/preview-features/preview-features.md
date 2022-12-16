# Preview Features

**Owner** [Immo Landwerth](https://github.com/terrajobst)

Starting with .NET 5, we've moved to a fixed release schedule whereby we ship one
version of .NET once a year, at RTM quality, with every other year being a long
term support (LTS) release.

This is in contrast to the .NET Framework days where we had multiple years
for a given release, typically two to three years.

Shipping more frequently has many advantages, such as being able to react
quicker to trends and having an organization and customer base that is used to
shipping & absorbing technology more quickly.

With .NET Core, we've solved the problem of the extremely high in-place
compatibility bar that .NET Framework has. This reduces risks for unintentional
behavioral changes and also allows certain parts of .NET to make deliberate
breaking changes if necessary.

As a result, this makes it viable to innovate across runtime, libraries, and
languages, allowing us to build more compelling features that would have been
almost impossible to do on .NET Framework. Examples include static linking,
default interface members, and ref structs (such as `Span<T>`).

However, it's sometimes challenging to deliver cross-cutting feature work in a
single release. A good example of such a feature is the upcoming improved
numeric support in generic code, with the help of static interface members. Such
a feature requires work in the runtimes, the languages, and the libraries. The
work at the library level isn't very high, but it's all about finding the right
set of support infrastructure so that developers are able to be productive at
writing generic code that can rely on numerical expressions and operators. In a
sense, the library work can't really fully start until the runtime and compiler
support is sufficiently bootstrapped, but it's the most important part in the
validation of the feature set. Practically, this means a good chunk of the
feature will only be available halfway through a release cycle, which greatly
reduces the time window for gathering feedback from other parts of the product
as well as from customers using preview builds of the upcoming .NET version.

Sadly, preview feedback isn't always sufficient to judge how well a feature
works because

1. Many fewer people use preview builds of .NET and
2. Many people don't use .NET previews for their production projects, thus
   greatly reducing the breadth & depth at which previews are being exercised.

This document is exploring a model where GA quality builds of .NET can contain
features that are considered still in preview.

The goal is to ensure that customers can discover those features & have an easy
way to play with them while simultaneously being fully aware that these features
aren't supported yet and thus might change (or be removed entirely) between
versions of .NET, just like they could in preview builds.

## Scenarios and User Experience

### Enabling a preview feature

Ainsley is working for the insurance company Fabrikam. As part of their work,
they are trying to figure out how they can integrate machine learning with
ML.NET more tightly with their services, but they struggle with data ingestion
because a lot of the heavy computations are happening inside of an old C++ code
base. When that code was written, .NET wasn't fast enough to do heavy numerical
computations.

As part of their research into this problem space, they notice a recent blog
post about how the upcoming generic numeric support and hardware intrinsics can
help to speed things up considerably. They decide to give it a spin, so they
copy & paste some sample code from the blog post.

The code immediately produces an error message:

> error: The type 'INumber\<TSelf>' is in preview. In order to use it, you need
> to enable preview features.

After taking a quick look at the blog post, Ainsley realizes that they only
need to set `EnablePreviewFeatures` to `true` in the project file.

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <EnablePreviewFeatures>True</EnablePreviewFeatures>
  </PropertyGroup>

</Project>
```

### Consuming a library that uses a preview feature

Sahibi works with Ainsley and tries to consume their library that exposes some
of the core algorithms so she can integrate those with ML.NET. After adding the
reference to Ainsley's library `Fabrikam.Algorithms.Core` she instantiates the
engine type which produces the following error:

> The library 'Fabrikam.Algorithms.Core' uses preview features of the .NET
> platform. In order to use it, you need to enable preview features.

Sahibi IMs Ainsley to ask what this is about where she learns that this feature
isn't a supported part of .NET, therefore she should make sure to not integrate
the library into the production instance of Fabrikam.

### Building a library that offers preview features

Tanner is working on `System.Runtime` that needs to offer preview features
without forcing all consumers to enable preview mode.

To do this, Tanner turns on preview features for `System.Runtime` but disables
the automatic generation of the assembly level attribute
`[assembly: RequiresPreviewFeatures]`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <EnablePreviewFeatures>True</EnablePreviewFeatures>
    <GenerateRequiresPreviewFeaturesAttribute>False</GenerateRequiresPreviewFeaturesAttribute>
  </PropertyGroup>

</Project>
```

This puts the project in expert mode and Tanner is now on the hook to ensure
that all public APIs that rely on preview features are marked with
`[RequiresPreviewFeatures]`. For example, the new interface that he's adding to
represent numbers need static interface members, so he marks the interface
accordingly:

```C#
namespace System
{
    [RequiresPreviewFeatures]
    public interface INumber<TSelf>
    {
        // ...
    }
}
```

## Requirements

### Goals

* Customers can discover preview features
* Customers are made aware if a particular feature is in a preview state so that
  they always know which part of the product is fully supported and which parts
  aren't.
* Customer have an easy way to audit if their applications or libraries are
  using preview features.
* Customers can't accidentally depend on preview features, either directly or
  indirectly through some library.
* We can deliver a reasonable experience even when a preview feature can't be
  supported in all the workloads, for example, by failing with a sensible error
  message.
* We have a simple experience for enabling preview features that span multiple
  parts of the system.
* We don't have to do unnatural acts to expose a preview feature, by, for
  example, having to build a plug-in based system that allows swapping in and
  out files.

### Non-Goals

* Allowing Microsoft to turn features on or off remotely, e.g. A/B testing
* Designing a mechanism for shipping preview features as separate packages or
  installers
* Modeling runtime-only preview features (see [What about runtime-only preview
  features?](#what-about-runtime-only-preview-features))

## Stakeholders and Reviewers

* Runtime teams
* C#, VB, F# compiler and IDE teams
* Library teams
* Project system team
* SDK team
* MSBuild team
* NuGet team
* PowerShell team
    - They may want to/need to do work to prevent PowerShell scripts taking
      accidental dependency on preview features.

## Design

### Property in project files

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <EnablePreviewFeatures>True</EnablePreviewFeatures>
  </PropertyGroup>

</Project>
```

### SDK support of preview features

The point of preview features is to gather customer feedback and adjust course
accordingly. This includes doing breaking changes, just like in any other
preview.

Many preview features will span tooling as well as libraries. Tooling could
include MSBuild targets but also language and IDE features.

Unlike runtime and framework, projects don't directly indicate which .NET SDK
they are using. Instead it's implied by the CLI or by the IDE version and
configuration (Visual Studio, Visual Studio Code, and Rider all have different
ways how and to what extent this can be controlled by the developer).

Just like in the case of runtime and framework, we don't plan to support preview
features in the SDK across versions. For instance, a preview API in `net6.0`
shouldn't be expected to have the same shape -- or even exists -- in `net7.0`.
The same is true for the .NET SDK: .NET 6 SDK might have preview features in
MSBuild and C# that don't exist or have a very different syntax in .NET 7.

Since preview features between runtime, framework, and tooling are usually in
support of each other, you can't expect to use, say, the .NET 7 SDK to build for
.NET 6 when using preview features. This is likely counter intuitive because
SDKs are usually backwards compatible, but it's really no different from the
framework, which is also backwards compatible but not for preview features.

This means:

> The .NET SDK will only support preview features for the current .NET
> framework.
>
> The current framework is defined as the framework whose major and minor
> version match the SDK. For instance, the .NET 6 SDK will only support preview
> features in `net6.0`. Conversely, the .NET 7 SDK will only support preview
> features in `net7.0`.

This has implications for multi-targeting, which is covered next.

### Meaning of property in multi-targeted projects

Imagine a project that targets .NET 6, .NET 5, and .NET Standard 2.0:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net6.0;net5.0;netstandard2.0</TargetFrameworks>
    <EnablePreviewFeatures>True</EnablePreviewFeatures>
  </PropertyGroup>

</Project>
```

This configuration shouldn't mean "turn preview features on in all TFMs" but
rather "turn preview features on for the current TFM". The current TFM is
defined as the major version of the SDK. For example, in the .NET 6 SDK the
current TFM would be `net6.0` while in the .NET 7 SDK the current TFM is defined
as `net7.0` (see previous section on why that is the case).

So you'll only be able to use preview features when the TFM and the SDK match.
This ensures that users get an error message if, for example, they try to use a
preview feature in .NET 6 but they are using the .NET 7 SDK. The assumption is
that because the feature was in preview for .NET 6, we likely took some customer
feedback and made a breaking change when we shipped .NET 7. For people that want
to target .NET 6 and its preview features, they will have to use the .NET 6 SDK.

In practice this means that a multi-targeted project will only be able to use
preview features in a single TFM, specifically the current one.

### Assembly info generation

When we generate the `AssemblyInfo.cs`, we'll generate an assembly level
attribute (the attribute and the meaning is discussed [later in this
document](#api-analyzer)).

For example, this project information:

```xml
<Project>

  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <EnablePreviewFeatures>True</EnablePreviewFeatures>
  </PropertyGroup>

</Project>
```

Would result in this assembly-level attribute:

```C#
[assembly: RequiresPreviewFeatures]
```

This means you can look at a binary and tell whether it was built using preview
features. This can be used for auditing and for detecting transitive
dependencies ([discussed later](#api-analyzer)).

### `runtimeconfig.json`

While we don't have concrete plans to use it, we should make sure that the
application project's preview mode is written to the `runtimeconfig.json` so
that the runtime can use this information if ever needed:

```json
{
  "runtimeOptions": {
    "tfm": "netcoreapp3.1",
    "previewFeatures": true
    // ...
  }
}
```

### Compiler tracking

*We propose that the compiler does no tracking of preview features at this
point.*

When `EnablePreviewFeatures` is true, the `LangVersion` property is set to
`Preview` (unless the customer has explicitly set `LangVersion` in their project
file already). This avoids customers having to turn on preview features for the
language separately.

The way the compiler knows which features a targeted runtime supports is by
looking at the `RuntimeFeature` type. Each runtime feature corresponds to a
specific field on that type. If the type doesn't have the field, the runtime is
considered as not-supporting the feature.

We discussed extending the compiler to treat `RuntimeFeature` fields marked with
`[RequiresPreviewFeatures]` differently, but this has the complication that this
effectively means that the compiler needs to track the use of runtime preview
features. We considered having a split where an analyzer would handle the use of
preview APIs with the compiler tracking the use of language features that
require preview runtime features.

The challenge is that this means we need to formalize how the compiler should
enforce it. At this point, it's not clear to us that we actually want a sound
analysis in this space because we likely need to allow the developer "to cheat",
that is, the author of an API needs to be able to say "I want to ship an RTM
version of my API that doesn't require consumers to opt-into preview" while
still being able to use preview feature in other parts of the code.

The trick is to ensure that parts depending on preview features are annotated
while ensuring that the parts that don't aren't, especially when those already
exist. However, with the upcoming statics in interfaces feature we run into
challenges where we want to be able to do the following things:

1. Define new interfaces that have statics on them. Those interfaces will be marked
   with `[RequiresPreviewFeatures]`.
2. Implement those interfaces on existing types, such as `Int32` and `Single`. Those
   types cannot be marked with `[RequiresPreviewFeatures]`.
3. We will implement many of the interface methods explicitly but not all of
   them will or even can. For cases where the static member already exists (for
   example, some of `Decimal`'s operators) those can't be required to be marked
   with `[RequiresPreviewFeatures]`.

At this point, it seems to us that its better to keep the logic in an analyzer,
potentially special-casing certain relationships of language features, runtime
support, and core APIs in order to deliver a good customer experience. Once we
understand the desired rules more, we can potentially formalize them and make
them a proper compiler feature.

### Analyzer

We'll need an attribute to record whether or not the a given assembly or API
requires preview features:

```C#
namespace System.Runtime.Versioning
{
    [AttributeUsage(AttributeTargets.Assembly |
                    AttributeTargets.Module |
                    AttributeTargets.Class |
                    AttributeTargets.Interface |
                    AttributeTargets.Delegate |
                    AttributeTargets.Struct |
                    AttributeTargets.Enum |
                    AttributeTargets.Constructor |
                    AttributeTargets.Method |
                    AttributeTargets.Property |
                    AttributeTargets.Field |
                    AttributeTargets.Event, AllowMultiple = true, Inherited = false)]
    public sealed class RequiresPreviewFeaturesAttribute : Attribute
    {
        public RequiresPreviewFeaturesAttribute();
    }
}
```

We'll ship a built-in analyzer that will report diagnostics when code depends on
preview features without having opted-into preview features. The default
severity is `error`.

Opting into preview features is done by marking the consuming member, type,
module, or assembly with `[RequiresPreviewFeatures]`.

***Note:** The analyzer doesn't track the `<EnablePreviewFeatures>` property
itself, it tracks the attribute `[RequiresPreviewFeatures]`. By default, setting
`<EnablePreviewFeatures>` to `True` will mark the entire assembly with
`[RequiresPreviewFeatures]`, but developers can turn this off which means that
they now need to manually annotate parts of their code with
`[RequiresPreviewFeatures]`.*

An API is considered marked as `[RequiresPreviewFeatures]` if it is directly
marked with `[RequiresPreviewFeatures]` or if the containing type, module or
assembly is marked with `[RequiresPreviewFeatures]`.

The following operations are considered use of preview features:

* Defining a static member in an interface, if, and only if, the
  `RuntimeFeature.VirtualStaticsInInterfaces` field is marked with
  `[RequiresPreviewFeatures]`
* Referencing an assembly that is marked as `[RequiresPreviewFeatures]`
* Deriving from a type marked as `[RequiresPreviewFeatures]`
* Implementing an interface marked as `[RequiresPreviewFeatures]`
* Overriding a member marked as `[RequiresPreviewFeatures]`
* Calling a method marked as `[RequiresPreviewFeatures]`
* Reading or writing a field or property marked as `[RequiresPreviewFeatures]`
* Subscribing or unsubscribing from an event marked as
  `[RequiresPreviewFeatures]`

This design ensures that using a future version of C# where statics in
interfaces are no longer a language preview will still enforce marking the
calling code as preview when building for a runtime where the corresponding
runtime feature is still considered in preview.

We plan to use this analyzer when building the BCL itself. Given the rules
above, we'll get a diagnostic when implementing the new interfaces on existing
types, such as `Int32`. That is intentional. The idea is that we suppress those
to indicate that we're OK with that:

```C#
namespace System
{
    public struct Int32 : // ...
#pragma warning disable CAXXXX // Use of preview features
        INumber<Int32>
#pragma warning restore CAXXXX // Use of preview features
    {
        // ...
    }
}
```

However, since this analyzer will also be used by 3rd parties, we believe it's
generally good if the analyzer is conservative with telling the developer when
they use preview features from non-preview code and leave it up to the developer
to suppress it when it's considered acceptable.

***Note:** Since F# doesn't support analyzers, we need to consider how preview
feature enforcement affects F# users.*

### Reflection usage

We need to decide whether or not to block calls via reflection to APIs that are
marked as `[RequirePreviewFeatures]`.

For instance:

```C#
void f(dynamic x) => x.PreviewMethod();
```

There are several problems with that:

1. **It's not pay-for-play friendly**. That is, every single time a method is
   called via reflection, we need to check for the applied attributes, so
   everyone has to pay for this enforcement, whether or not they actually use
   preview APIs or not.

2. **It's hard to get right**. We've tried that before, during early days of UWP
   to prevent users from calling out of the Window Store approved API set and it
   turns out to be quite leaky. For example, code walking the methods and
   properties might be able to see them but then wouldn't be able to call them.

I'd argue that whenever reflection is used, our ability to provide guardrails is
going down (e.g. no support for analyzers, no warnings for using obsolete APIs
etc). Calling preview APIs is really not much different from that.

## Q & A

### What about runtime-only preview features?

We already have runtime-only preview features and the way we have shipped them
is as environment variables, for example, [tiered JIT]:

```text
COMPlus_TieredCompilation=1
```

These can typically also be configured via `runtimeconfig.json`:

```json
{
  "runtimeOptions": {
    "configProperties": {
      "System.Runtime.TieredCompilation": true
    }
  }
}
```

We have decided that we scope this feature to only track the use of preview
features that result in changes to the files distributed by the customer, such
as using preview compiler features or preview APIs.

Preview features that only impact the process, which is what runtime-only preview
features are, don't need to be traced through the library ecosystem and thus
aren't part of this work.

Also, we don't think that it makes sense for libraries to declare a dependency
on runtime-only preview features because those tend to be performance tweaks.

The existing mechanism via environment variables and `runtimeconfig.json` is
considered good enough. Also, for those features it is useful to be able to turn
individual features on an off in order to troubleshoot. Restricting those
features to the global preview mode is considered too restrictive an
unnecessary, considering that they are application-level concerns.

We discussed whether the environment variables or switches should include the
name "preview" or "unsupported" but this would be akin to naming preview APIs
differently which we generally don't want because it increases the friction for
customers that want to move from the preview to the supported version. Also,
historically we have not been able to rename them. We believe it sufficient to
document which of these switches are supported and which ones are in preview.

[tiered-jit]: https://devblogs.microsoft.com/dotnet/tiered-compilation-preview-in-net-core-2-1/

### Are preview features supported?

No. The entire point of preview features is to gather feedback, and make changes
based on that. This means we can't support them:

* We will generally not service preview features even if they are included in LTS
  releases. The compiler has done this successfully already. If we can't hold
  this line, then this effort has failed.
* This also means that people using preview feature in an LTS, they may no
  longer be able to use the latest tooling if the later version of the tooling
  made a breaking change for a preview feature. We consider this acceptable.
* We don't believe preview features will be around for that long, because the
  idea is that this only includes features that are on track for productization
  in the very next release of the platform. This is how it has worked for
  compiler previews as well.

### What is the quality bar for preview features?

Since preview features ship with a GA build, there will still be a quality bar.
For instance, we don't want preview features to adversely affect the performance
or stability of Visual Studio or unrelated areas in the .NET runtime itself. The
reason being that developers frequently forgot that they turned preview features
on and if Visual Studio is becoming unresponsive or crashes, they will likely
blame Visual Studio, without realizing that this is due to a preview feature.
This can result in negative customer sentiment which we need to avoid.

Also, for Watson reports we likely want to record if preview features were
turned on. This will allow us to measure how responsible preview features are
with respect to hangs and crashes.

And lastly this is for preview features that we think are ready for consumption
and gathering feedback. This is *not* for unfinished feature work. Preview
features have to be in a state that we'd consider acceptable for a preview
release. For highly speculative work, we'd likely keep that outside of GA
builds, e.g. in feature branches or experimental repos, such as `runtimelabs`.

### Can we make breaking changes in preview features?

Yes! That's the whole point of marking features as preview in the first place.
Please note that breaking change also includes yanking the preview feature in
its entirety. Preview features should be treated like previews of .NET: we don't
make promises and reserve the right to make changes.

Halfway through a release cycle we might also decide that certain features won't
make the cut for RTM quality and we might decide to mark features we shipped in
a previous preview as `[RequiresPreviewFeatures]`. The reverse is also possible
(and arguably not a breaking change) but probably less likely.

### Why a single on/off switch as opposed to individual feature switches?

I originally designed it as such:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <ExperimentalFeatures>FancyGC;StaticInterfaceMembers</ExperimentalFeatures>
  </PropertyGroup>

</Project>
```

The upside is that one can be explicit about which preview feature one wants to
turn on (and thus depend on).

The downsides were:

1. It's a more complicated concept for the user to understand and very different
   from how previews of the .NET platform itself work. This might increase the
   barrier of entry, which we don't want.

2. The test matrix for the product gets much more complicated as the combinations
   of on/states explode quickly.

3. Not all preview features have a convenient entry point (language syntax,
   API). Having a global preview mode increases airtime for features that
   otherwise wouldn't have been turned on.

4. We're unlikely to ship a grab bag of preview features and instead are more
   likely to ship a cohesive feature set that in its totality is still in
   preview, such as static interface members, which includes runtime work,
   language work, and compiler work. Said differently, having a single preview
   mode forces us to jointly coordinate preview features across all layers.

### What happens when a preview feature is released?

Several things will happen:

1. We remove the `[RequiresPreviewFeatures]` attribution from any APIs
2. We remove any guards from the runtime/compilers that check for the preview
   flag (of course, compilers are likely still checking language versions, but
   that's separate)

If someone shipped a binary to nuget.org that depends on a preview feature,
consuming it from a .NET version where the feature was released will still
generate an error unless the consuming project has preview features turned on.

### Can 3rd parties introduce their own preview APIs?

In this design the system has no way to prevent this and it will just work.

### Why did we use the term *preview*?

Originally I used the term *experimental* but we decided to change it to
*preview* for these reasons:

* The quality bar and connotation of preview features is virtually identical to
  preview builds of the platform. The only different is that an GA build
  additionally contains features that are still in the preview state.
* The compiler has a language version `preview` that has the same connotation
  that we use here.
* We generally used the term *experimental* where probability of shipping is
  <50%, e.g. in `runtimelabs`
* Overall, the goal is draw more people in and motivate them to try the
  features. The term *preview* seems more helpful than *experimental*.

### Why didn't we use the `<LangVersion>Preview</LangVersion>`?

There is an existing property that allows customers to opt into preview
features of the language by setting this property in their project file:

```xml
<Project>

  <PropertyGroup>
    <LangVersion>Preview</LangVersion>
  </PropertyGroup>

</Project>
```

There are several issues with using this as the primary mechanism for enabling
preview features for the .NET platform:

* The `LangVersion` property doesn't have quite the right name and it would now
  cause transitivity requirements.
* Using this property might not work well for VB and F#
* The biggest counter argument is that the compiler can be in stable version but
  the TFM you're targeting is not.
* Due to the transitivity tracking, this can be a breaking change for anyone who
  has set `LangVersion` to `Preview`. That's because today, the resulting binary
  isn't marked as requiring callers to be in preview mode as well where we'd now
  demand this to be the case. We could address this by only applying the new
  behavior to code that is targeting .NET 6 (or higher), but this will likely
  still bite folks that upgrade to .NET 6.
