# Automatic Binding Redirection By Default

**PM** [Immo Landwerth](https://github.com/terrajobst) |
**Dev** *TBD* |
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

These cases frequently occur in these cases:

* **NuGet graphs**. The application author often references a graph of NuGet
  packages while simultaneously also referencing newer versions of some of the
  indirectly referenced packages. Getting consistent graphs is often not
  possible or even desirable.

* **.NET Standard 2.0 support files**. Binding redirects are also required for
  the .NET Standard 2.0 support files that .NET Framework 4.6.1 applications
  need to deploy in order to load .NET Standard libraries.

Correctly authoring binding redirects if non-trivial as it requires the author
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
to turn the setting off if necessary. In other words, we change it from an opt-
in feature to an opt-out feature.

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
generation to be on by default. Otherwise her application would have crashed
with a `FileLoadException`.

### Upgrading a NuGet dependency

Robert is using FizzBuzz, a documentation framework that produces help files out
of Markdown documents. FizzBuzz depends on Markdig, a popular .NET markdown
library. Robert encounters an issue with some of his recent documentation
changes which causes Markdig to crash. He notices that while there is no updated
version of FizzBuzz, there is an updated version of Markdig that fixes his
issue. Robert updates his (indirect) dependency on Markdig to the latest version
that fixes the issue. He triggers a rebuild of the documentation and is happy to
see that the crash no longer occurs.

Michelle doesn't know that this is thanks to automatic binding redirect
generation to be on by default. Otherwise his application would have crashed
with a `FileLoadException`.

## Requirements

### Goals

* **Turning this setting on must not break working applications**. We expect
  this setting to make more applications work, but we must not break
  applications where adding binding redirects breaks them.

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
developer has configured it explicitly.

## Q & A

### Why is the setting not conditioned on the target framework?

Over the last years we have not encountered a single instance where binding
redirect generation has caused applications to not work. It's important to note
that this feature is already on for all projects that were created targeting
.NET Framework 4.5.1 or higher. While this means it's not on for the vast
majority of our customers, the customer is large enough to give us confidence
that this feature works -- and it has been in production for over four years.

In principle it is possible to create a scenario where this setting is causing a
problem but based on our experience we believe this case to be extremely
unlikely. Also, these cases would likely require hand crafted binding redirects,
which Automatic Binding Redirect Generation will honor (i.e. it doesn't override
any hand-authored redirects). And in the unlikely case this still poses issues
the application author can turn off the feature entirely.

It's also worth pointing out that the net-effect of this feature is to influence
the .NET Framework binder to match what all other runtimes already do: unify
assembly references to the version deployed by the application, assuming the
version is equal or higher.