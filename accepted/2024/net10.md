# Parsing `net10`

**Owner** [Immo Landwerth](https://github.com/terrjobst)

NuGet's framework name parser is fairly old and stems from a world where only
.NET Framework exists. In fact, the early versions of NuGet didn't even support
targeting different versions of .NET Framework.

In this world, version numbers were represented without periods, for example,
`net20` would be .NET Framework 2.0 while `net35` referred to .NET Framework
3.5.

And here lies the problem: next year we're shipping .NET 10. It's reasonable for people to put `net10` in the project file. This will produce some interesting error messages:

> **error MSB3645**: .NET Framework v3.5 Service Pack 1 was not found. In order to target ".NETFramework,Version=v1.0", .NET Framework v3.5 Service Pack 1 or later must be installed.
>
> **error MSB3644**: The reference assemblies for .NETFramework,Version=v1.0 were not found. To resolve this, install the Developer Pack (SDK/Targeting Pack) for this framework version or retarget your application. You can download .NET Framework Developer Packs at https://aka.ms/msbuild/developerpacks

It's not exactly terrible, but many people will find this confusing as it's not
obvious that the fix is to change the framework from `net10` to `net10.0`.

This document proposes how to address this.

## Stakeholders and Reviewers

* NuGet team
* MSBuild/SDK team
* VS project system team

## Design

Considering that .NET Framework 1.0 is out of support for a while and Visual
Studio doesn't even support building for anything older than .NET Framework 2.0,
it seems very doable to make `net10` mean .NET 10. The question is what happens
with all the other frameworks, such as `net20`, `net452` etc. Quite a few of
those are still supported and actively used.

Here is the proposal:

* Make `net10` mean .NET 10 but keep all higher versions as-is (`net20` still
  means .NET Framework 2.0).
* The .NET SDK produces a warning if the `<TargetFramework>` property doesn't
  contain a period.

This results in two things:

1. Existing behaviors won't change
2. People making the mistake today don't get unintelligible errors which makes
   the warning more visible

The next version where this is a problem is .NET 20, which will ship in 2035 (assuming we keep the schedule). I think 10 years of warning people to include a period ought to be enough to avoid this problem. In fact, we should consider making omitting a version number a build break in, say, .NET 12.
