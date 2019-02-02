# Runtime Binding Behavior

[Framework-dependent](https://docs.microsoft.com/en-us/dotnet/core/deploying/#framework-dependent-deployments-fdd)  applications require a .NET Core host to find a compatible runtime from a central location. The behavior of runtime binding is critical for both application compatibility and runtime deployment convenience.

This document proposes a model that works for both apps and managed COM components/hosting.

Related content:

* [Roll Forward On No Candidate Fx](https://github.com/dotnet/core-setup/blob/master/Documentation/design-docs/roll-forward-on-no-candidate-fx.md) doc defines existing behavior. 
* [Roll forward improvements for COM -- dotnet/core-setup #5062](https://github.com/dotnet/core-setup/issues/5062) specifies a proposal specific to COM. 
* [--fx-version argument to dotnet exec should apply roll-forward policy -- dotnet/core-setup #4146](https://github.com/dotnet/core-setup/issues/4146) specifies a proposal to enable roll-forward for `--fx-version`

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

COM-based and other hosted environments have the requirement of loading managed components built for multiple runtime versions, and this set is typically not known a priori . 

The application situation, described earlier in the doc, is largely oriented on missing runtime versions and how to handle that. The COM situation is oriented more on "best fit" for a set of components it may load. As a result, COM and other hosts need to artificially roll-forward to the latest acceptable runtime version to enable compatibility for arbitrary components.

We will offer a configuration knobs for hosts that enable artificial roll-forward. These knobs play a similar role to [LockClrVersion](https://docs.microsoft.com/dotnet/framework/unmanaged-api/hosting/lockclrversion-function).

## Configuration Knobs

The host exposes the following configuration knobs that you can use to control binding behavior:

### DOTNET_ROLL_FORWARD_ON_NO_CANDIDATE_FX`

This setting is described in [Roll Forward On No Candidate Fx](https://github.com/dotnet/core-setup/blob/master/Documentation/design-docs/roll-forward-on-no-candidate-fx.md). It does not have the right behavior or UX. We will deprecate this setting for 3.0+.

### RollForward

We will introduce a new knob, called `RollForward`, with the following values:

* `patch` -- Roll forward to the highest patch verison. This disables minor version roll forward.
* `minor` -- Roll forward to the lowest higher minor verison, if requested minor version is missing.
* `major` -- Roll forward to lowest higher major version, and lowest minor version, if requested major version is missing
* `UseLatestMinor` -- Roll forward to latest minor version, even if requested minor version is on machine. Intended for COM hosting scenario.
* `UseLatestMajor` -- Roll forward to latest major and highest minor version, even if requested major is on machine. Intended for COM hosting scenario.

There is no default setting. See **Configuration Precedence** for more information.

Can be specified via the following ways:

* Project file property: `RollForward`
* Runtime configuration file property: `rollForward`
* Environment variable: `DOTNET_ROLL_FORWARD`
* Command line argument: `--roll-forward`

### fx-version

You can specify the exact desired runtime version using the command line argument '--fx-version'. In this case, only the specified version will be accepted, even if patch roll forward is enabled. The expected behavior would be the same in the example above.

The following example shows how to use this knob:

```console
C:\testapps\twotwoapp>dotnet --fx-version 3.0.0 bin\Debug\netcoreapp2.2\twotwoapp.dll
```

The example above will attempt to load the 3.0.0 runtime version. If it isn't found, the host will print an error message.

Another example:

```console
C:\testapps\threezeroapp>bin\Debug\netcoreapp3.0\threezero.exe --fx-version 3.0.0 --roll-forward patch
```

The example above will attempt to load the highest `3.0` patch version, which may be `3.0.0` or `3.0.27`, for example, whichever is the highest path version on the machine.

### Configuration Precedence

The host will consult these settings in order (later scopes take precendence over earlier ones):

* `.runtimeconfig.json` properties (AKA "json")
* CLI arguments
* Environment variables (AKA "ENV")

The `version` and `roll-forward` settings compose in the following way:

* If set at the same scope (json, CLI or ENV), then `version` establishes a floor for any roll-forward behavior.
* The `roll-forward` value does not affect higher precedent scopes. For example, a `version` setting specified at the CLI scope overwrites both a `version` and `roll-forward` setting that might exist at the json scope. Same thing with ENV configuation w/rt CLI arugments and json.
* A `version` setting can flow to higher scopes if it is not replaced by another `version` value. This enables a `roll-forward` setting at a higher scope to compose with a `version` setting at a lower scope.
* The default `roll-forward` for json scope is `minor`. There is no default roll-forward value for CLI and ENV scopes.
