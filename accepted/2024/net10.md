# Parsing `net10`

**Owner** [Immo Landwerth](https://github.com/terrajobst)

NuGet's framework name parser is fairly old and stems from a world where only
.NET Framework exists. In fact, the early versions of NuGet didn't even support
targeting different versions of .NET Framework.

In this world, version numbers were represented without periods, for example,
`net20` would be .NET Framework 2.0 while `net35` referred to .NET Framework
3.5.

And here lies the problem: next year we're shipping .NET 10. It's reasonable for
people to put `net10` in the project file. This will produce some interesting
error messages:

> **error MSB3645**: .NET Framework v3.5 Service Pack 1 was not found. In order
> to target ".NETFramework,Version=v1.0", .NET Framework v3.5 Service Pack 1 or
> later must be installed.
>
> **error MSB3644**: The reference assemblies for .NETFramework,Version=v1.0
> were not found. To resolve this, install the Developer Pack (SDK/Targeting
> Pack) for this framework version or retarget your application. You can
> download .NET Framework Developer Packs at
> https://aka.ms/msbuild/developerpacks

It's not exactly terrible, but many people will find this confusing as it's not
obvious that the fix is to change the framework from `net10` to `net10.0`.

This document proposes how to address this.

## Stakeholders and Reviewers

* NuGet team
* MSBuild/SDK team
* VS project system team

## Design

We want to steer people towards a proper version syntax, that is

```xml
<TargetFramework>netX.Y.Z</TargetFramework>
```

Examples include `net9.0`, `net10.0`, `net4.5.1`. This syntax makes it
unambiguous.

Proposal:

* Fail the build if the TFM is `net10` or `net11` with an error message
  > This framework syntax is unsupported because it's ambiguous. If you mean
  > .NET Framework 1.0, use `net1.0`. If you mean .NET 10, use `net10.0`.
* If the TFM is neither `net10` nor `net11`, produce a warning if it doesn't
  contain a period:
  > This framework syntax is obsolete. You should use periods to separate
  > version components.

The reason to fail the build is because in practice someone trying to target
.NET 10 will most likely fail anyway because the either don't have the .NET
Framework 3.5 targeting pack -- or worse -- they do, but the code doesn't
compile because it's meant for modern .NET Core, not a 25 year old .NET
Framework 1.0. By failing it early it turns an unintelligible error message into
something actionable.

The reason to issue the warning is to avoid any ambiguities moving forward. 

> [!NOTE]
>
> Please note that this change only applies to the .NET Core SDK, i.e. SDK-style
> projects. Old school .NET Framework CSPROJ files will be unaffected by this
> change because the representation of the target framework is already a proper
> version string.

### Usage

Below is a comparison how much a given TFM syntax is being used on GitHub. As
you can see, for modern TFMs, the syntax with an explicit period is most widely
used.

| GitHub Usage of a TFM syntax                          | `netX` | `netXY` | `netX.Y` |
| ----------------------------------------------------- | -----: | ------- | -------: |
| [`net5`] [`net50`] [`net5.0`]                         |    916 | 181     |     142k |
| [`net6`] [`net60`] [`net6.0`]                         |   2.3k | 273     |     436k |
| [`net7`] [`net70`] [`net7.0`]                         |    520 | 104     |     138k |
| [`net8`] [`net80`] [`net8.0`]                         |    704 | 46      |     381k |
| [`net9`] [`net90`] [`net9.0`]                         |      9 | 1       |     4.3k |
| [`netstandard2`] [`netstandard20`] [`netstandard2.0`] |     83 | 148     |     132k |

[`net5`]: https://github.com/search?q=net5%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net6`]: https://github.com/search?q=net6%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net7`]: https://github.com/search?q=net7%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net8`]: https://github.com/search?q=net8%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net9`]: https://github.com/search?q=net9%3C%2FTargetFramework%3E+lang%3Axml&type=code

[`net50`]: https://github.com/search?q=net50%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net60`]: https://github.com/search?q=net60%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net70`]: https://github.com/search?q=net70%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net80`]: https://github.com/search?q=net80%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net90`]: https://github.com/search?q=net90%3C%2FTargetFramework%3E+lang%3Axml&type=code

[`net5.0`]: https://github.com/search?q=net5.0%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net6.0`]: https://github.com/search?q=net6.0%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net7.0`]: https://github.com/search?q=net7.0%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net8.0`]: https://github.com/search?q=net8.0%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net9.0`]: https://github.com/search?q=net9.0%3C%2FTargetFramework%3E+lang%3Axml&type=code

[`netstandard2`]: https://github.com/search?q=netstandard2%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`netstandard20`]: https://github.com/search?q=netstandard20%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`netstandard2.0`]: https://github.com/search?q=netstandard2.0%3C%2FTargetFramework%3E+lang%3Axml&type=code

For .NET Framework, the most dominant TFM syntax is the one without periods,
which is to be expected given that was the encouraged syntax:

| GitHub Usage of a TFM syntax  | `netX` | `netXY` | `netX.Y` |
| ----------------------------- | -----: | ------: | -------: |
| [`net1`] [`net10`] [`net1.0`] |      0 |      75 |        0 |
| [`net11`] [`net1.1`]          |        |       1 |        0 |
| [`net2`] [`net20`] [`net2.0`] |      1 |     15k |        6 |
| [`net3`] [`net30`] [`net3.0`] |      0 |       0 |        3 |
| [`net35`] [`net3.5`]          |        |     42k |       16 |
| [`net4`] [`net40`] [`net4.0`] |      0 |     24k |       26 |
| [`net45`] [`net4.5`]          |        |     13k |       48 |
| [`net451`] [`net4.5.1`]       |        |     988 |        3 |
| [`net452`] [`net4.5.2`]       |        |     35k |      107 |
| [`net46`] [`net4.6`]          |        |     39k |       17 |
| [`net461`] [`net4.6.1`]       |        |     84k |       43 |
| [`net462`] [`net4.6.2`]       |        |     82k |       34 |
| [`net47`] [`net4.7`]          |        |     13k |       25 |
| [`net471`] [`net4.7.1`]       |        |     19k |       23 |
| [`net472`] [`net4.7.2`]       |        |    159k |      146 |
| [`net48`] [`net4.8`]          |        |    134k |      320 |
| [`net481`] [`net4.8.1`]       |        |     652 |       37 |

[`net1`]: https://github.com/search?q=net1%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net2`]: https://github.com/search?q=net2%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net3`]: https://github.com/search?q=net3%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net4`]: https://github.com/search?q=net3%3C%2FTargetFramework%3E+lang%3Axml&type=code

[`net10`]: https://github.com/search?q=net10%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net11`]: https://github.com/search?q=net11%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net20`]: https://github.com/search?q=net20%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net30`]: https://github.com/search?q=net3%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net35`]: https://github.com/search?q=net35%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net40`]: https://github.com/search?q=net40%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net45`]: https://github.com/search?q=net45%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net451`]: https://github.com/search?q=net451%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net452`]: https://github.com/search?q=net452%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net46`]: https://github.com/search?q=net46%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net4.6`]: https://github.com/search?q=net4.6%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net461`]: https://github.com/search?q=net461%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net462`]: https://github.com/search?q=net462%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net47`]: https://github.com/search?q=net47%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net471`]: https://github.com/search?q=net471%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net472`]: https://github.com/search?q=net472%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net48`]: https://github.com/search?q=net48%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net481`]: https://github.com/search?q=net481%3C%2FTargetFramework%3E+lang%3Axml&type=code

[`net1.0`]: https://github.com/search?q=net1.0%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net1.1`]: https://github.com/search?q=net1.1%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net2.0`]: https://github.com/search?q=net2.0%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net3.0`]: https://github.com/search?q=net3.0%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net3.5`]: https://github.com/search?q=net3.5%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net4.0`]: https://github.com/search?q=net4.0%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net4.5`]: https://github.com/search?q=net4.5%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net4.5.1`]: https://github.com/search?q=net4.5.1%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net4.5.2`]: https://github.com/search?q=net4.5.2%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net4.6.1`]: https://github.com/search?q=net4.6.1%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net4.6.2`]: https://github.com/search?q=net4.6.2%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net4.7`]: https://github.com/search?q=net4.7%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net4.7.1`]: https://github.com/search?q=net4.7.1%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net4.7.2`]: https://github.com/search?q=net4.7.2%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net4.8`]: https://github.com/search?q=net4.8%3C%2FTargetFramework%3E+lang%3Axml&type=code
[`net4.8.1`]: https://github.com/search?q=net4.8.1%3C%2FTargetFramework%3E+lang%3Axml&type=code


## Q & A

### What about `net10.0`

That is the preferred syntax and will continue to work. The point of this work
is not to make `net10` work because it's nicer -- it's to promote "framework
names should be specified with a period", but we don't want to make this an
error (because many, many people use `net472` today) but a warning. However,
when using `net10` will most likely fail because the SDK doesn't support
targeting it, which will drown out the warning telling you to use period.
Failing it only for `net10` would be possible but feel odd because we'd prefer
periods for all framework names, not just for `net10.0`.

### Why don't we make `net10` just work?

Considering that .NET Framework 1.0 is out of support for a while and Visual
Studio doesn't even support building for anything older than .NET Framework 2.0,
it seems very doable to make `net10` mean .NET 10.

There are two downsides to this:

1. It makes a syntax work that we don't want to promote
2. The other (widely used) monikers `net20`, `net452`, `net48`, need to continue
   to work and mean what they mean today, which is confusing.
3. It requires changing parsing rules in the NuGet library, which might have
   unforeseen ripple effects. 
