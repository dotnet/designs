# Runtime Binding Behavior

[Framework-dependent](https://docs.microsoft.com/dotnet/core/deploying/#framework-dependent-deployments-fdd)  applications require a .NET Core host to find a compatible runtime from a central location. The behavior of runtime binding is critical for both application compatibility and runtime deployment convenience. [Self-contained](https://docs.microsoft.com/dotnet/core/deploying/#self-contained-deployments-scd) apps don't have this need, because there is only one runtime they will ever use.

This document proposes a model that works for both apps and managed COM components/hosting.

Related content:

* [Roll Forward On No Candidate Fx](https://github.com/dotnet/core-setup/blob/master/Documentation/design-docs/roll-forward-on-no-candidate-fx.md) doc defines existing behavior. 
* [Roll forward improvements for COM -- dotnet/core-setup #5062](https://github.com/dotnet/core-setup/issues/5062) specifies a proposal specific to COM.

## Parts of a version number

.NET Core uses three-part version numbers, which include the following version components (in order):

* major version
* minor version
* patch version

An example .NET Core version number is [2.2.1](https://github.com/dotnet/core/blob/master/release-notes/2.2/2.2.1/2.2.1.md).

Each of these version components is treated different for runtime binding, as discussed later in this document.

## Specifing a .NET Core Runtime Version

Each application includes a .NET Core version dependency, specified as a three-part version. By default, this version is the `.0` patch for the two-part version specified by the target framework. We chose this model to allow applications to run on any machine that satisfies the target framework, and to instead make the specific version used a deployment choice.

You can see this model and the different files in which the various values are stored, in the following example.

```console
C:\testapps\twotwoapp>dotnet new console
C:\testapps\twotwoapp>type twotwoapp.csproj
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>netcoreapp2.2</TargetFramework>
  </PropertyGroup>

</Project>
C:\testapps\twotwoapp>dotnet build
C:\testapps\twotwoapp>type bin\Debug\netcoreapp2.2\twotwoapp.runtimeconfig.json
{
  "runtimeOptions": {
    "tfm": "netcoreapp2.2",
    "framework": {
      "name": "Microsoft.NETCore.App",
      "version": "2.2.0"
    }
  }
}
```

The `version` property in `.runtimeconfig.json` file records the three-part version. As you can see, the `version` property specifies the `.0` patch for the `2.2` runtime, recorded as `2.2.0`.

You can specify a different version than `.0` patch. We only recommend specifying a different patch version if your application requires a specific patch version to run (due to a bug fix in the runtime), but this should be very uncommon. You can see a specific patch version being specified in the following example, with the `RuntimeFrameworkVersion` msbuild property set in the project file. This value is written to the `.runtimeconfig.json` file, similar to what was seen in the preceding example.

```console
C:\testapps\twotwoapp>type twotwoapp.csproj
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>netcoreapp2.2</TargetFramework>
    <RuntimeFrameworkVersion>2.2.4</RuntimeFrameworkVersion>
  </PropertyGroup>

</Project>
C:\testapps\twotwoapp>dotnet build
C:\testapps\twotwoapp>type bin\Debug\netcoreapp2.2\twotwoapp.runtimeconfig.json
{
  "runtimeOptions": {
    "tfm": "netcoreapp2.2",
    "framework": {
      "name": "Microsoft.NETCore.App",
      "version": "2.2.4"
    }
  }
}
```

The .NET Core host attempts to find the best installed runtime given the three-part .NET Core version number specified in `.runtimeconfig.json`. If it cannot find a compatible runtime, it will print an error message similar to the following one.

```console
C:\testapps\twotwoapp>dotnet bin\Debug\netcoreapp2.2\twotwoapp.dll
It was not possible to find any compatible framework version
The specified framework 'Microsoft.NETCore.App', version '2.2.4' was not found.
  - Check application dependencies and target a framework version installed at:
      C:\Program Files\dotnet\
  - Installing .NET Core prerequisites might help resolve this problem:
      http://go.microsoft.com/fwlink/?LinkID=798306&clcid=0x409
  - The .NET Core framework and SDK can be installed from:
      https://aka.ms/dotnet-download
```

The rest of the document discusses how to determine the *best installed runtime* for a given three-part version number.

## Patch Version Selection

> This runtime selection logic is used to find an appropriate installed patch version for a given three-part runtime version (as specified in `.runtimeconfig.json`).

**Logic:** Given a request for `x.y.z` version, the host will look for all `x.y` versions that are equal or higher to `x.y.z`. It will select the highest patch version within that set. This behavior is oriented on selecting the most compatible and highest serviced (and therefore most secure) installed product version.

**User Impact:** When a user installs the latest patch version for the `x.y` version of .NET Core, all applications that target `x.y` will start using that newer patch version (on next application launch). The user does not need to configure their applications to use that latest serviced version.

Let's look at an example.

Requested version:

* `2.2.0`

Installed versions:

* `1.1.17`
* `2.2.0`
* `2.2.1`
* `2.2.5`
* `3.0.0`

In this scenario, the `2.2.5` runtime will be selected.

## Minor Version Selection

> This runtime selection logic is used if an appropriate installed patch version for a given three-part runtime version (as specified in `.runtimeconfig.json`) cannot be found.

**Logic:** Given a request for `x.y.z` version, the host will look for an `x.y` runtime version, as described for **Patch Version Selection** above. If it cannot find an `x.y` runtime, then the host will look for all `x` runtimes that are higher than `x.y`. It will select the lowest minor version within that set and then select the highest patch version within that minor version.

If the host cannot find an `x` runtime, then the host shows an error message describing that the requested runtime could not be found. This behavior is oriented on selecting only a known compatible product version.

This behavior is oriented on selecting a known compatible installed product version.

**User Impact:** When a user runs an application on a machine that does not have `x.y` installed, but has a later minor version of `x`, then the app will transparently run on that version. The user does not need to configure their applications to use that later minor version.

Let's look at an example.

Requested version:

* `2.1.0`

Installed versions:

* `1.1.17`
* `2.2.0`
* `2.2.1`
* `2.2.5`
* `2.3.1`
* `3.0.0`

In this scenario, the `2.2.5` runtime will be selected.

Let's look at another example.

Requested version:

* `2.1.0`

Installed versions:

* `1.1.17`
* `3.0.0`

In this scenario, no runtime will be selected and instead the host prints a helpful error message (similar to the one displayed in the **Specifing a .NET Core Runtime Version** section).

We have found that this behavior is problematic for scenarios like [global tools](https://twitter.com/KathleenDollard/status/1079811275641696256) and expect similar challenges for client applications (new with 3.0). To mitigate these scenarios, we will expose a set of configuration knobs to enable developers to opt-into a specific roll-forward behaviors for their app or their environment. We will write guidance for those scenarios to guide developers and end-users to opt-in to roll-forward for scenarios that will benefit from it. We will not provide templates that set these knobs automatically for developers.

## Major Version Selection -- Opt-in Behavior using Configuration Knobs

> This runtime selection logic is used if an appropriate minor version for a given three-part runtime version cannot be found and an application or environment has been configured for major-version roll-forward via **Configuration Knobs** (defined in a following section).

Given a request for `x.y.z` version, the host will look for an `x` runtime version, as described for **Minor Version Selection** above. If it cannot find an `x` runtime, then the host will look for all runtimes that are higher than `x`. It will select the lowest major version within that set and then select the lowest minor and highest patch version within that set. This behavior is oriented on a best effort attempt at selecting a compatible installed product version. That's why the lowest minor version is selected, not the highest one.

**User Impact:** When a user runs an application on a machine that does not have `x` installed, but has a later runtime installed, then the app will transparently run on that version. The user does not need to configure their applications to use that later minor version. There is some risk in this scenario that the app will crash due to incompatibilities in the runtime relative to the runtime that the app targets and that it will be difficult to diagnose the cause (incompatibility)and the solution (installing a older runtime). If that app runs successfully, then the user will be happy. The tension between compatibility and deployment convenience is why this scenario is opt-in.

Let's look at an example.

Requested version:

* `2.1.0`

Installed versions:

* `1.1.17`
* `3.0.0`
* `3.0.1`
* `3.1.0`
* `4.0.0`

In this scenario, the `3.0.1` runtime will be selected.

## Handling Previews

As of 3.0 Preview 2, previews have special binding behavior. In this proposal, previews are proposed to not have special behavior. Apps that depend on preview runtimes can bind to non-preview runtimes and vice-versa. Previews are not considered to be special going forward, with this proposal. This behavior is oriented on enabling applications built with previews to get a chance to run (likely successfully) on a stable version and to make testing stable applications on previews easy.

Assumption: Stable Visual Studio versions only install stable .NET Core versions.

## COM Components

COM-based and other hosted environments have the requirement of loading managed components built for multiple runtime versions, and this set is typically not known a priori.

The application situation, described earlier in the doc, is largely oriented on missing runtime versions and how to handle that. The COM situation is oriented more on "best fit" for a set of components it may load. As a result, COM and other hosts need to artificially roll-forward to the latest acceptable runtime version to enable compatibility for arbitrary components.

We will offer a configuration knobs for hosts that enable artificial roll-forward. These knobs play a similar role to [LockClrVersion](https://docs.microsoft.com/dotnet/framework/unmanaged-api/hosting/lockclrversion-function).

## Configuration Knobs

The host exposes the following configuration knobs that you can use to control binding behavior:

### RollForward

We will introduce a new knob, called `RollForward`, with the following values:

* `None` -- Do not roll forward. Only bind to specified version.
* `Patch` -- Roll forward to the highest patch verison. This disables minor version roll forward.
* `Minor` -- Roll forward to the lowest higher minor verison, if requested minor version is missing. If the requested minor version is present, then the `Patch` policy is used.
* `Major` -- Roll forward to lowest higher major version, and lowest minor version, if requested major version is missing. If the requested major version is present, then the `Minor` and `Patch` policies are used, as appropriate.
* `AlwaysLatestMinor` -- Roll forward to latest minor version, even if requested minor version is present. Intended for COM hosting scenario.
* `AlwaysLatestMajor` -- Roll forward to latest major and highest minor version, even if requested major is present. Intended for COM hosting scenario.

`Minor` is the default setting. See **Configuration Precedence** for more information.

Can be specified via the following ways:

* Project file property: `RollForward`
* Runtime configuration file property: `rollForward`
* Environment variable: `DOTNET_ROLL_FORWARD`
* Command line argument: `--roll-forward`

### DOTNET_ROLL_FORWARD_ON_NO_CANDIDATE_FX

This setting is described in [Roll Forward On No Candidate Fx](https://github.com/dotnet/core-setup/blob/master/Documentation/design-docs/roll-forward-on-no-candidate-fx.md). It does not have the right behavior or UX. We will deprecate this setting for 3.0.

The following policy will be used in 3.0:

* If `ROLL_FORWARD_ON_NO_CANDIDATE_FX` is set, it will be honored.
* If `ROLL_FORWARD` is set, it will be honored.
* If both settings are set, it is an error.
* If neither settings are set, then the existing default behavior is used, as described in the **Patch Version Selection** and **Patch Version Selection** sections above.

Note: The ENV syntax is used above, but the same rules apply for any way that the two settings are set.

### fx-version

You can specify the exact desired runtime version using the command line argument '--fx-version'.

The following example shows the existing behavior of this knob:

```console
C:\testapps\twooneapp>type Program.cs
using System;

namespace twooneapp
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
            Console.WriteLine(typeof(Object).Assembly.Location);
        }
    }
}
C:\testapps\twooneapp>
C:\testapps\twooneapp>dotnet bin\Debug\netcoreapp2.1\twooneapp.dll
Hello World!
C:\Program Files\dotnet\shared\Microsoft.NETCore.App\2.1.7\System.Private.CoreLib.dll

C:\testapps\twooneapp>dotnet --fx-version 2.1.0 bin\Debug\netcoreapp2.1\twooneapp.dll
Hello World!
C:\Program Files\dotnet\shared\Microsoft.NETCore.App\2.1.0\System.Private.CoreLib.dll
```

The `--fx-version` argument can be composed with the new `RollForward` settings, either via CLI or ENV. By default, `--fx-version` does not roll-forward.

The following example enables a .NET Core 2.1 application to run on .NET Core 2.2 without having to select the correct 2.2 patch version. In this case `--fx-version` specifies the floor version. In this scenario, 2.2.0 is not on the machine, but a later patch version is.

```console
C:\testapps\twooneapp>dotnet --fx-version 2.2.0 bin\Debug\netcoreapp2.1\twooneapp.dll
It was not possible to find any compatible framework version
The specified framework 'Microsoft.NETCore.App', version '2.2.0' was not found.
C:\testapps\twooneapp>dotnet --fx-version 2.2.0 --roll-forward Patch bin\Debug\netcoreapp2.1\twooneapp.dll
Hello World!
C:\Program Files\dotnet\shared\Microsoft.NETCore.App\2.2.3\System.Private.CoreLib.dll
```

### Configuration Precedence

The host will consult the various ways of setting `RollForward`, in order (later scopes take precendence over earlier ones):

1. `.runtimeconfig.json` properties (AKA "json")
2. CLI arguments
3. Environment variables (AKA "ENV")

Note: The project file property is not listed because the project file is a build-time not runtime artifact. Runtime-oriented values in project files need to be written to `.runtimeconfig.json` as part of the build in order to influence runtime behavior.

The `version` and `roll-forward` settings compose in the following way:

* A `version` setting at higher precedent scopes overwrites both the `version` and `roll-forward` values at lower scopes. For example, `version` specified at the CLI scope (with `--fx-version`) overwrites both a `version` and `roll-forward` setting that might exist at the json scope.
* A `version` setting can flow to higher scopes if it is not replaced by another `version` value. This enables a `roll-forward` setting at a higher scope to compose with a `version` setting at a lower scope.
* The absense of a `version` value is an error when the `roll-forward` value of `Patch`, `Minor`, `Major` or `AlwaysLatestMinor` is set since the initial version state is not available. It is not an error when the `roll-forward` value of `AlwaysLatestMajor` is set since an initial version state is not meaningful.

More generally:

* The `version` value establishes a floor for roll-forward behavior, except `AlwaysLatestMajor`.
* The default `roll-forward` setting is `Minor` except when `--fx-version` is specified when it is `None`.
