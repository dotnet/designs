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

> What will the compiler do when the attribute is not applied at the assembly level?

I don't think the compiler/analyzer should do anything in this case. The SDK
will generate the assembly level attribute based on the project setting, but
this can be turned, off which is what we will do to build the BCL. For obvious
reasons we don't want `System.Runtime` as whole to have an assembly level
attribute -- we only want this on specific types/members.

> If assembly A has a method M marked with [RequiresPreviewFeatures], but no
> assembly level attribute, and it references assembly B that has the assembly
> level attribute, will the compiler warn?

Yes. The assembly level attribute is meant to protect the user that just uses
the project level setting. Advanced users (like us) can turn off the generation
of the assembly level attribute and selectively apply this attribute to
particular parts of the library. In this case it will only warn users that uses
those APIs.

The advanced user is responsible for making sure that they very well understand
which parts are using preview features and ensure they get the attribution
right. Hence, it doesn't make sense for such as user to depend on an assembly
that didn't do that due diligence because all bets are off at that point.

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

### Compilation context

When `EnablePreviewFeatures` is true, the `LangVersion` property is set to
`Preview` (unless the customer has explicitly set `LangVersion` in their project
file already). This avoids customers having to turn on preview features for the
language separately.

In addition, the property `EnablePreviewFeatures` should be passed to the
compilation context (akin to how `TargetFramework` is passed in today). An
analyzer will use that to block use of APIs that are marked as preview as basing
this off the language preview mode is nonsensical.

The way the compiler knows which features a targeted runtime supports is by
looking at the `RuntimeFeature` type. Each runtime feature corresponds to a
specific field on that type. If the type doesn't have the field, the runtime is
considered as not-supporting the feature. To indicate that a feature is there
but requires turning on preview mode, we'll simply mark the field with the
`[RequiresPreviewFeatures]` attribute, just like any other preview API.

This solves two problems:

* The customer is using a future version of C# where a given language feature is
  no longer considered "preview" and thus doesn't require `LangVersion` to be
  set to preview but in the targeted runtime the feature is still preview. When
  a language feature is used that requires the given runtime feature, the
  compiler should check if the field is marked as preview and fail unless the
  customer has turned preview mode on.

* The customer has manually set `LangVersion` to `Preview` but
  `EnablePreviewFeatures` is not configured (thus defaulting to `False`).
  Similar case as above, the customer can use any language feature that doesn't
  require runtime changes but as soon as a feature is used that requires a
  preview runtime feature, the compiler will report an error, demanding that
  preview mode needs to be turned on.

***Note:*** F# doesn't have the capability to use analyzers today. We can
decided to make this a compiler feature for F#.

### API Analyzer

We'll need an attribute to record whether or not the project was
built with preview features turned on:

```C#
namespace System.Runtime.Versioning
{
    [AttributeUsage(AttributeTargets.Assembly |
                    AttributeTargets.Module |
                    AttributeTargets.Class |
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

We'll ship a built-in analyzer that will report diagnostics when an API is being
used that is marked `[RequiresPreviewFeatures]` but the consumer (member, type,
module, assembly) isn't marked. The default severity is `error`.

This analyzer will also validate that if any referenced assembly has applied
`[RequiresPreviewFeatures]`, that the consuming assembly must also have this
attribute applied.

User code will typically only have this attribute applied at the assembly-level.
However, the framework will typically only use this attribute on types and
members without putting it on the assembly itself.

***Note:*** Since F# doesn't support analyzers, we would have to add this
capability to the compiler itself.

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

**OPEN ISSUE** Should we block calls to preview APIs from reflection?

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

No. The entire point of preview feature is to gather feedback, and make changes
based on it. This means we can't support them:

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

Since preview features ship with an GA build, there will still be a quality bar.
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

### Why are we recording whether an assembly used preview features?

For two reasons:

1. It can be used for auditing
2. It is used to enforce transitivity. An assembly that doesn't use preview
   features shouldn't depend on assemblies that do. It doesn't matter whether
   this binary was a project-to-project reference, a NuGet package, or a raw
   reference to some checked in file -- we only have to look at the assembly
   level attribute.

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

In this design the system has no way to prevent this and it will in fact just
work.

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
