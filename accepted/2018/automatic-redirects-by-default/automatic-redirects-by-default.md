# Automatic Binding Redirection By Default

**Owner** [Immo Landwerth](https://github.com/terrajobst) |
**Owner** [Rainer Sigwald](https://github.com/rainersigwald) |
**Feature**: [#dotnet/sdk/1405](https://github.com/dotnet/sdk/issues/1405)

Four years ago, .NET Framework 4.5.1 shipped an MSBuild feature that
[automatically generates binding redirects][abrg]. Unfortunately, it's
effectively off for most of our customers. This proposal is about turning the
feature on by default by making it an opt-out.

The .NET Framework runtime is the only runtime that uses an exact binding match
policy. That means that if the runtime needs to resolve a reference to an
assembly in version `X`, it will not succeed if the application deployed this
assembly in version `X+N`. The application author has a way to influence this by
using a feature called [binding redirects]. Those are entries in the
`app.config` that instruct the CLR to unify specific version ranges.

This frequently occurs in these cases:

* **NuGet graphs**. The application author often references a graph of NuGet
  packages while simultaneously also referencing newer versions of some of the
  indirectly referenced packages. Getting consistent graphs is often not
  possible or even desirable.

* **.NET Standard 2.0 support files**. Binding redirects are also required for
  the .NET Standard 2.0 support files that .NET Framework 4.6.1 applications
  need to deploy in order to load .NET Standard libraries.

Correctly authoring binding redirects is non-trivial as it requires the author
to know the affected version range as well as the public key token. That's why
in .NET Framework 4.5.1 we've added a feature to MSBuild called [Automatic
Binding Redirect Generation][abrg] which will compute the correct binding
redirects during the build an automatically add them to the `app.config` file
that is produced to the application's output directory.

Following our conservative compatibility procedures we decided to make this
feature opt-in rather than opt-out. Unfortunately, the opt-in wasn't based
around the target framework the project uses at build-time, but based around the
target-framework at creation time. That's because the setting that turns it on
is conditionally emitted by the project template based on the selected target
framework. If the project is upgraded later on, that setting is never turned on
again. The net effect is that the vast majority of our customers have this
setting still turned off.

The proposal is to turn this setting on by default but allow application authors
to turn the setting off if necessary. In other words, we change it from an
opt-in feature to an opt-out feature.

[binding redirects]: https://docs.microsoft.com/en-us/dotnet/framework/configure-apps/redirect-assembly-versions
[abrg]: https://docs.microsoft.com/en-us/dotnet/framework/configure-apps/how-to-enable-and-disable-automatic-binding-redirection

## Scenarios and User Experience

### Consuming a .NET Standard 2.0 library from .NET Framework 4.6.1

Michelle maintains a .NET Framework 4.5 application. She tries to install the
new `System.IO.Pipelines` NuGet package that targets .NET Standard 2.0. She gets
an error message from NuGet that this isn't compatible with .NET Framework 4.5.
After some research, she learns that she must at least target .NET Framework
4.6.1 in order to use .NET Standard 2.0 packages. After upgrading to .NET
Framework 4.6.1, the package installs without errors. Michelle then starts to
use pipelines from her app. When she F5s her application it just works fine.

Michelle doesn't know that this is thanks to automatic binding redirect
generation being on by default. Otherwise her application would have crashed
with a `FileLoadException`.

### Upgrading a NuGet dependency

Robert is using DocBuzz, a documentation framework that produces help files out
of Markdown documents. DocBuzz depends on Markdig, a popular .NET markdown
library. Robert encounters an issue with some of his recent documentation
changes which causes Markdig to crash. He notices that while there is no updated
version of DocBuzz, there is an updated version of Markdig that fixes his issue.
Robert updates his (indirect) dependency on Markdig to the latest version that
fixes the issue. He triggers a rebuild of the documentation and is happy to see
that the crash no longer occurs.

Robert doesn't know that this is thanks to automatic binding redirect generation
being on by default. Otherwise his application would have crashed with a
`FileLoadException`.

## Requirements

### Goals

* **Turned off if the project isn't targeting .NET Framework**. The .NET
  Framework is the only runtime that needs binding redirects. While computing
  conflicts for other runtimes doesn't do any harm but it does impact
  performance as the MSBuild has to walk more assemblies.

* **Turned off in class libraries**. Binding redirects are generally not useful
  for class libraries as they are for the application. Again, computing them
  doesn't do harm but it does impact performance as the MSBuild has to walk more
  assemblies.

* **Turned off in web projects**. Web projects cannot use an `app.config` file
  as they use `web.config`, which is usually taken from the source directory. At
  best, dropping the extra file is just noise but at worse it might confuse the
  build or the runtime.

* **Automatically turned on when targeting .NET Framework 4.6.1 or higher and a
  .NET Standard 1.5 (or higher) library is consumed**. This addresses the
  primary issues developers face when consuming .NET Standard binaries from .NET
  Framework today.

* **Automatically turned on when targeting .NET Framework 4.7.2 or higher**.
  This properly converts the feature from opt-in to opt-out but ties it a .NET
  Framework version in the rare instance the developer needs to trouble shoot
  any issues. This also avoids issues where customers use a .NET Standard
  library on .NET Framework 4.6.1 and automatic binding redirects *happen* to
  also fix some issue with their NuGet packages. When upgrading to .NET
  Framework 4.7.2 we no longer use the build support for .NET Standard, which
  would now also mean that binding redirects are no longer generated. This can
  be solved by making sure .NET Framework 4.7.2 also generates binding redirects
  by default.

* **Turning this setting on must not break working applications**. We expect
  this setting to make more applications work, but we must not break
  applications where adding binding redirects breaks them. See Q&A section why
  we believe this is achievable.

* **It must be possible to opt-out**. The customer base on .NET Framework is
  fairly large and has an unfathomable amount of different configurations. We
  need to assume that this setting might not work for some extremely rare
  condition and thus allow those customers to opt-out.

### Non-Goals

* **Introducing new binding policy**. This proposal is only about enabling an
  already existing feature in MSBuild which uses the also existing binding
  redirect feature of the CLR.

## Design

Somewhere in the common props we need to add the following snippet:

```xml
<PropertyGroup>
    <AutoGenerateBindingRedirects Condition="'$(AutoGenerateBindingRedirects)' == ''">True</AutoGenerateBindingRedirects>
</PropertyGroup>
```

Logically, this means we turn on `AutoGenerateBindingRedirects` unless the
developer has configured it explicitly. The condition likely needs more
refinement in order to model the constraints listed in the requirement section.

### Class Libraries

Class libraries don't need binding redirects, unless they behave like apps, for
instance unit testing projects.

We generally don't want to compute and emit binding redirects because for most
class library projects it's just wasting time. We need to figure out how we can
detect unit testing projects during build and turn the setting on. It looks like
this would work as follows:

* **MSTest**. This one already has a bunch of targets. Ideally, those targets
  would indicate that the class library is really an application-like output
  instead of enabling `AutoGenerateBindingRedirects` directly. This allows to
  control the policy for binding redirects separately from the question whether
  the class library really needs a configuration file.

* **xUnit**. xUnit provides additional targets via their NuGet package. Whatever
  properties we set for MSTest we should ask xUnit to set as well.

## Q & A

### Why don't we turn this on regardless of target framework?

We're concerned that this might have subtle binding differences when rebuilding
existing code. The current proposal is making this an opt-out rather than an
opt-in starting with .NET Framework 4.7.2. So the application developer has to
change the project by targeting a higher version of .NET Framework. Since that
is an explicit step, we believe in the rare case the binding does change the
developer has a better way to root cause and troubleshoot the issues, for
instance by looking at the .NET Framework 4.7.2 release notes.

The only exception is .NET Framework 4.6.1 when consuming a .NET Standard 2.0
library in which case we turn this on too. The rationale is that this scenario
is broken today and turning on automatic binding redirects is part of fixing it.

### Why do we believe it's safe to be opt-out?

We know that having binding redirects can cause problems, especially for
assemblies that are in-box. However, that's a different problem from whether
automatic generation of binding redirects is on or off. A given set of
assemblies either needs binding redirects or it doesn't. Over the years we have
not encountered many instances where automatic binding redirect generation has
caused applications to not work that would have worked with automatic generation
being disabled. It's important to note that this feature is already on for all
projects that were created targeting .NET Framework 4.5.1 or higher. While this
means it's not on for the vast majority of our customers, the customer is large
enough to give us confidence that this feature works -- and it has been in
production for over four years.

In principle it is possible to create a scenario where this setting is causing a
problem but based on our experience we believe this case to be fairly rare.
That's why we tied the opt-in behavior to a framework change.

### How does this work for ASP.NET projects?

There are two kinds of ASP.NET projects: web application and web sites:

* **Web Applications**. While those have a project file and thus run MSBuild,
  they generally don't publish the `web.config` file but instead use the one
  from the source. This prevents us from having the ability to add additional
  settings as part of the build. The experience developers get is that they see
  a single warning in the error list. Double clicking it will prompt them and
  ask whether they want to add binding redirects to their configuration file.
  Selecting yes will add them. It's a one time action for all affected
  assemblies.

* **Web Sites**. Web sites don't have a build definition and don't work at all.