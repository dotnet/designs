# .NET Standard Targeting Recommendations

**Owner** [Immo Landwerth](https://github.com/terrajobst) | [Viktor Hofer](https://github.com/ViktorHofer)

We have already said that there is [no new version of .NET
Standard][net-standard-future] and that the replacement moving forward is to
just target the platform-neutral `netX.Y` [base framework][tfm] (with `x.y >=
5.0`) of .NET Core.

For customers that need to author code that still needs to be consumed by .NET
Framework, the recommendation is to continue using `netstandard2.0`. There is
very little reason to target .NET Standard 2.1 because you lose .NET Framework
while only gaining very little few additional APIs. And if you don't care about
.NET Framework support, then just target .NET Core directly; at least then you
get access to a lot more functionality, in exchange for the reduced reach.

There is basically no reason to target .NET Standard 1.x any more as all .NET
implementations that are still supported, can reference .NET Standard 2.0
libraries.

However, we have found that many customers still target .NET Standard 1.x. We
found out the reasons boil down to one of two things:

1. Targeting the lowest possible versions is considered better
2. It was already like that and there is no reason to change

The first argument has merit in a world where .NET Standard continues to produce
new versions. Since that's no longer the case you might as well update to a
later version that allows you to use more functionality. We found the biggest
jump in productivity is with .NET Standard 2.0 because it also supports
[referencing .NET Framework libraries][netfx-compate-mode], which is useful in
cases where you modernize existing applications and have some mixture until you
can upgrade all the projects.

The second reason is always valid; however, in cases when you're actively
maintaining a code base it makes sense to upgrade to avoid missing out on
features that could have made your life easier and you didn't realize exist
because code completion simply didn't advertise them.

## Scenarios and User Experience

### Building a project targeting .NET Standard

Jackie is assigned a work item to add a feature that requires writing code in
the shared business logic project. That project happens to target .NET Standard
1.4. She doesn't know why because the project was built before her time on the
team.  The first time she compiles in Visual Studio she sees a warning:

> warning NETSDK1234: Contoso.BusinessLogic.csproj: Targeting .NET Standard
> prior to 2.0 is no longer recommended. See \<URL\> for more details.

Following the link, she finds a page that inform her that all supported .NET
implementations allow using .NET Standard 2.0 or higher. She asks one of her
colleagues and she remembers that they used .NET Standard 1.4 because at some
point her team also maintained a Windows Phone app (which, to everybody's
disappointment no longer exists).

Jack decides to upgrade the project to .NET Standard 2.0. She later realizes
that this was a good idea because it enables her to use many of the
`Microsoft.Extensions` libraries that make her life a lot easier, specifically
configuration and DI.

## Requirements

### Goals

* Issue a warning when *building* a project targeting `netstandard1.x` with the
  .NET 9 SDK

### Non-Goals

* Issue a warning when building a project targeting `netstandard2.x`
* Issue a warning when consuming a library that was built for .NET Standard 1.x.
* Issue a warning building with a .NET SDK prior to 9.0

## Stakeholders and Reviewers

* Libraries team
* SDK team
* Build team
* NuGet team

## Design

The .NET SDK will check the `TargetFramework` property. If it uses
`netstandardX.Y` with `X.Y < 2.0` it will produce the following warning:

> Targeting .NET Standard prior to 2.0 is no longer recommended. See \<URL\> for
> more details.

* The warning should have a diagnostic ID (prefixed `NETSDK` followed by a four
  digit number).
* The warning should be suppressable via the `NoWarn` property.
* The URL should be point to the documented ID [here][sdk-errors] (not sure why
  it says `sdk-errors` -- I don't believe we have a section for warnings).

## Q & A

### Didn't you promise that .NET Standard is supported forever?

We promised that future .NET implementations will continue to support
*consuming* libraries built for .NET Standard, all the way back to 1.0. That is
different from promising that we'll support *producing* .NET Standard binaries
forever; that would only make sense if .NET Standard is still the best way to
build reusable libraries. And [since .NET 5 and the platform-specific
flavors][tfm] of .NET we think there is now a better way to do so.

The original promise around consumption still stands: existing libraries built
for .NET Standard 1.0 can still be consumed and we have no plans on changing
that. So any existing investment in building a library that can be consumed in
as many places as possible still carries forward.

### Can I continue to build libraries targeting .NET Standard 1.x?

Yes. There *may* come a time where the .NET SDK stops supporting building .NET
Standard libraries, but we currently have no plans on doing so as the targeting
packs for .NET Standard 1.x aren't bundled with the SDK and are only downloaded
on demand already; thus, the benefit to removing the support for building .NET
Standard 1.x seems marginal.

We believe our ecosystem is better off targeting at least .NET Standard 2.0
which is why we want to issue a warning, but the motivation isn't in us being
able to remove the support for building.

[net-standard-future]: https://devblogs.microsoft.com/dotnet/the-future-of-net-standard/
[tfm]: https://github.com/dotnet/designs/blob/main/accepted/2020/net5/net5.md
[netfx-compate-mode]: https://learn.microsoft.com/en-us/dotnet/core/porting/#net-framework-compatibility-mode
[sdk-errors]: https://learn.microsoft.com/en-us/dotnet/core/tools/sdk-errors/
