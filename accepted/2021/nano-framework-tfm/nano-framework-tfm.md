# TFM for .NET nanoFramework

**Owners** [Immo Landwerth](https://github.com/terrajobst) | [Laurent Ellerbach](https://github.com/Ellerbach) | [José Simões](https://github.com/josesimoes)

While .NET has been optimized to be a general purpose application development
framework, we also created a flavor of .NET that can be used in the context of
very resource-constrained hardware, called [.NET Micro Framework][netmf]. To
give you an idea what resource-constrained looks like: 512 KB of flash and 128
KB of RAM. As such, the .NET Micro Framework doesn't have many features of .NET,
such as generics.

While the [.NET Micro Framework][netmf] is archived, there is a community
version of it called the [.NET nanoFramework][nanoframework].

Because of the resource constraints there isn't much of a point of being able to
share binaries between .NET nanoFramework and the rest of the .NET ecosystem.
Also, it simply wouldn't even be feasible for it to implement .NET Standard.
Thus, there are no plans to make .NET nanoFramework compatible with .NET
Standard or the new [.NET 5 based TFMs][net5-tfm].

However, in order for the .NET nanoFramework to have a tooling experience in
Visual Studio, we need to have a TFM that NuGet package authors can use to build
libraries that are specific to the .NET nanoFramework.

[netmf]: https://github.com/NETMF/netmf-interpreter
[nanoframework]: https://github.com/nanoframework/nf-interpreter
[net5-tfm]: ../../2020/net5/net5.md

## Requirements

### Goals

* NuGet needs to able to recognize the TFM for .NET nanoFramework and select
  assets from a NuGet package into a project that targets .NET nanoFramework.
* NuGet needs to allow creating a NuGet package that includes assets for .NET
  nanoFramework.
* Normal versioning rules need to apply (e.g. a package can offer assets for
  multiple versions of the .NET nanoFramework and NuGet will use the largest
  version that is less or equal to the targeted version).
* Once NuGet knows of the TFM for .NET nanoFramework, the .NET nanoFramework
  maintainers should be able to ship new versions without having to submit any
  changes to the NuGet client.

### Non-Goals

* NuGet doesn't need to understand any compatibility relationships between .NET
  nanoFramework and other TFM.

## Stakeholders and Reviewers

* NuGet team
* MSBuild/SDK team

## Design

TFM Component  | Value
---------------|------------------
Friendly name  | netnano
TFI            | .NETnanoFramework
