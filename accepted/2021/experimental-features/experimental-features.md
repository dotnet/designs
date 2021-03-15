# Experimental Features

**PM** [Immo Landwerth](https://github.com/terrajobst)

Starting with .NET 5, we've moved to fixed release schedule whereby we ship one
version of .NET once a year, at RTM quality, with every other year being a long
term support (LTS) release.

This is in stark contrast to the .NET Framework days where we had multiple years
for a given release, typically two to three years.

Shipping more frequently has many advantages, such as being able to react
quicker to trends and having an organization and customer base that is used to
shipping & absorbing technology quicker to make the most of today's fast moving
world.

With .NET Core, we've solved the problem of the extremely high in-place
compatibility bar that .NET Framework has. This reduces risks for unintentional
behavioral changes and also allows certain parts of .NET to make deliberate
breaking changes if necessary.

As a result, this makes it viable to innovate across runtime, libraries, and
languages, allowing us to build more compelling features that would have been
almost impossible to do on .NET Framework. Examples include static linking,
default interface members, and ref structs (such as `Span<T>`).

However, it's sometimes challenging to deliver cross-cutting feature work in a
single release, especially when they are breaking new ground in terms of
expressiveness. A good example of such a feature is the upcoming improved
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
features that are considered experimental.

The goal is to ensure that customers can discover those features & have an easy
way to play with them while simultaneously being fully aware that these features
aren't supported yet and thus might change (or be removed entirely) between versions of .NET, just like
they could in preview builds.

## Scenarios and User Experience

### Enabling an experimental runtime feature

Maddy is building a Tye application. She reads in a blog post that the .NET
runtime team is experimenting with a new GC mode, code named FancyGC.

She wants to see how this improves the performance of her backend. She's glad to
see that the new GC mode was shipped as part of .NET 6, so she doesn't need to
figure out how to run a new version of .NET in her CI lab. So she creates a
feature branch and enables the new GC mode simply by changing the service's
project file as follows:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <ExperimentalFeatures>FancyGC</ExperimentalFeatures>
  </PropertyGroup>

</Project>
```

She then submits a draft PR against `main` to trigger their CI/CD pipeline which
also runs some performance tests.

### Enabling an experimental language feature

During lunch, Adriana discusses a design problem with one of the architects who
mentions that the upcoming static interface members in C# might be able to solve
this issue. Adriana does a quick web search and finds a blog post from the C#
team that showcases how this feature might look like. She wonders how she can
plan with that. Optimistic, she copy & pastes the sample code into her project
which immediately generates a compilation error:

> error: static interface members are an experimental feature of C# 9.0 and must
> be turned on explicitly.

The error displays a light-bulb in Visual Studio, suggesting to turn the feature
on, which modifies her project file as follows:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <ExperimentalFeatures>StaticInterfaceMembers</ExperimentalFeatures>
  </PropertyGroup>

</Project>
```

### Enabling a cross-cutting experimental feature

Ainsley is working for the insurance company Fabrikam. As part of their work,
they are trying to figure out how they can integrate machine learning with
ML.NET more tightly with their services, but they struggle with data ingestion
because a lot of the heavy computations are happening inside of an old C++ base.
When that code was written, .NET wasn't fast enough to do heavy numerical
computations.

As part of their research into this problem space, they notice a recent blog
post about how the upcoming generic numeric support and hardware intrinsics can
help to speed things up considerably. They decide to give it a spin, so they
copy & paste some sample code from the blog post.

The code immediately produces an error message:

> error: The type 'IArithmetic\<T>' is part of the experimental feature
> 'StaticInterfaceMembers' which must be turned on explicitly.

After taking a quick look at the blog post, Ainsley realizes that they only need
to set the `ExperimentalFeatures` property in the project files.

### Consuming a library that uses an experimental feature

Sahibi works with Ainsley and tries to consume their library that exposes some
of the core algorithms so she can integrate those with ML.NET. After adding the
reference to Ainsley's library `Fabrikam.Algorithms.Core` she instantiates the
engine type which produces the following error:

> The library 'Fabrikam.Algorithms.Core' requires the experimental feature
> 'StaticInterfaceMembers' which must be turned on explicitly.

Sahibi IMs Ainsley to ask what this is about where she learns that this feature
isn't a supported part of .NET, therefore she should make sure to not integrate
the library into the production instance of Fabrikam.

## Requirements

### Goals

* Customers can discover experimental features
* Customers are made aware if a particular feature is in an experimental state
  so that they always know which part of the product is fully supported and
  which parts aren't.
* Customer have an easy way to audit if their applications or libraries are
  using experimental features.
* Customers can't accidentally depend on experimental features, either directly
  or indirectly through some library.
* We can deliver a reasonable experience even when an experimental feature can't
  be supported in all the workloads, for example, by failing with a sensible
  error message.
* We have a unified experience for turning on experimental features, no matter
  whether they are in a runtime, a compiler, a library, or span multiple parts
  of the system.
* We don't have to do unnatural acts to add an experimental feature, by, for
  example, having to build a plug-in based system that allows swapping in and
  out files.

### Non-Goals

* Allowing Microsoft to turn features on or off remotely, e.g. A/B testing
* Designing a mechanism for shipping experimental features as separate packages
  or installers

## Stakeholders and Reviewers

* Runtime teams
* C#, VB, F# compiler and IDE teams
* Library teams
* Project system team
* SDK team
* MSBuild team
* NuGet team

## Design

### Property in project files

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <ExperimentalFeatures>StaticInterfaceMembers</ExperimentalFeatures>
  </PropertyGroup>

</Project>
```

### Assembly info generation

When we generate the `AssemblyInfo.cs`, we'll include all experimental features
as an assembly level attribute (the attribute and the meaning is discussed
[later in this document](#api-analyzer)).

For example, this project information:

```xml
<Project>

  <PropertyGroup>
    <ExperimentalFeatures>StaticInterfaceMembers;FancyGC</ExperimentalFeatures>
  </PropertyGroup>

</Project>
```

Would result in these assembly-level attributes:

```C#
[assembly: ExperimentalFeature("StaticInterfaceMembers")]
[assembly: ExperimentalFeature("FancyGC")]
```

This means you can look at a binary and tell which experimental features were
used to build it. This can be used for auditing and for detecting transitive
dependencies ([discussed later](#api-analyzer)).

### runtime.json

The `ExperimentalFeatures` should be written into the `runtime.json` file such
that the host/runtime can use it to make decisions.

### Compilation context

The `ExperimentalFeatures` needs to be passed to the compilers. Roslyn (C#/VB)
already has a mechanism for this which is exposed to both the compiler as well
as for analyzers/source generators. Alternatively, we could say the information
about experimental features can be retrieved from the generated assembly-level
attributes.

***Note:*** F# doesn't have this capability today.

### API Analyzer

We'll add an attribute that we can put on APIs we consider experimental. In some
cases it might not be possible to mark an API (for example, because it's a new
type system feature) in which case the feature would either be guarded by new
language syntax or by a custom analyzer.

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
    public sealed class ExperimentalFeatureAttribute : Attribute
    {
        public ExperimentalFeatureAttribute(string featureName);
        public string FeatureName { get; }
    }
}
```

We'll ship a built-in analyzer that will report diagnostics when an API is being
called that is marked with this attribute and the feature name is not contained
in the project's `ExperimentalFeatures` property. The default severity is
`error`.

This analyzer will also validate that all referenced assemblies only have
assembly-level attributes for experimental features that the consuming project
also declared. For example, if a project has this configuration:

```xml
<Project>

  <PropertyGroup>
    <ExperimentalFeatures>StaticInterfaceMembers</ExperimentalFeatures>
  </PropertyGroup>

</Project>
```

consuming an assembly with these attributes

```C#
[assembly: ExperimentalFeature("StaticInterfaceMembers")]
[assembly: ExperimentalFeature("FancyGC")]
```

would result in a diagnostic like this:

> error: Assembly 'Foo.Bar' depends on the experimental feature 'FancyGC' which
> the consuming project has not enabled.

***Note:*** Since F# doesn't support analyzers, we would have to add this
capability to the compiler itself.

## Q & A

### Why not just have a single on/off switch for experimental features?

We could just have a setting in the project file like this:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <CanUseExperimentalFeatures>True</CanUseExperimentalFeatures>
  </PropertyGroup>

</Project>
```

The problem with that is that once any experimental feature is used, it becomes
a free-for-all for all experimental features. For example, imagine we ship two
features:

* Static interface members
* A new GC mode

Someone who wants to use the new GC mode turns this setting on. Now someone else
who also works on the same project could now accidentally use static interface
members, without realizing that that feature is experimental.

This would violate our guiding principle that customers are aware when they
depend on experimental features.

### Why are we recording experimental features in the assembly?

For two reasons:

1. It can be used for auditing
2. It is used to enforce transitivity. A project can only depend on binaries
   that have a subset of experimental features enabled. It doesn't matter
   whether this binary was a project-to-project reference, a NuGet package, or a
   raw reference to some checked in file -- we only have to look at the assembly
   level attributes and we can check which features were enabled when it was
   compiled.

### What happens when an experimental feature is released?

Several things will happen:

1. We remove the `ExperimentalFeature` attribution from any APIs
2. We remove any guards from the runtime/compilers that check for the feature
   flag (of course, compilers are likely still checking language versions, but
   that's separate)

If someone shipped a binary to nuget.org that depends on an experimental
feature, consuming it from a .NET version where the feature was release will
still generate an error unless the consuming project has the feature turned on.
If we believe this penalizes early adopters too much, we could make sure that
the .NET SDK knows about which experimental features such that diagnostics for
now released features can be suppressed.

### Can we make breaking changes in experimental features?

Yes! That's the whole point of marking features as experimental in the first
place. Please note that breaking change also includes yanking the experimental
feature in its entirety. Experimental features should be treated like previews
of .NET: we don't make promises and reserve the right to make changes.

Halfway through a release cycle we might also decide that certain features won't
make the cut for RTM quality and we might decide to mark features we shipped in
a previous preview as experimental. The reverse is also possible (and argubly
not a breaking change) but probably less likely.

### Can 3rd parties introduce their own experimental APIs?

In this design the system has no way to prevent this and it will in fact just
work. The only important restriction is that the names shouldn't overlap.
Assuming we pick sensible names for preview features, it doesn't seem very
likely to conflict with names a 3rd party might choose.
