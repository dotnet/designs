# Proposal for global.json

**Owner** [Kathleen Dollard](https://github.com/KathleenDollard)

---
**NOTE:** 

SDK version numbers do not use semantic versioning. [Docs has an article on SDK versioning](https://docs.microsoft.com/en-us/dotnet/core/versions/).

---

`global.json` allows users to pin the version of the SDK they are using. This was added to .NET Core as a way for users to roll-back to a previous version per project until a fix could be provided if there a problem occurred. While our guidance remains to use it only for special circumstances, users now use it for numerous reasons. 

This document describes changes to `global.json` to provide more flexibility to reflect the wider usage. 

## Definitions

The term *SDK patch* is used to describe the last two positions of the SDK major/minor/feature/patch (n in x.y.znn).

The term *feature band* is used to describe the SDK major/minor/feature/patch (x.y.z in x.y.znn).

Given x.y.znn

* x is the major version
* y is the minor version
* z is the feature band
* nn is the patch

## Previous behavior

The SDK resolvers (in both the CLI and VS) search from the current location (project in VS) up the directory hierarchy until they encounter a `global.json` file. When the resolver finds one, _it stops looking_ and attempts to resolve the SDK version specified in that file. This happens even if this `global.json` does not specify an SDK version.

When a `global.json` is encountered and it specifies an SDK version, the SDK resolvers attempt to locate that specific SDK patch version:

1. When the resolver finds no `global.json` or finds one that does not specify SDK version,the latest SDK is used. Whether prerelease versions are considered depends on how `dotnet` is called:
   - If called from the CLI, prerelease versions are considered.
   - If called from Visual Studio, a flag is passed indicating whether to consider prerelease versions:
     - If Visual Studio is a Preview, or the "Use Previews" options is set by the user, prerelease versions are considered.
     - Otherwise, prerelease versions are not considered when called from Visual Studio.
2. When the resolver finds a `global.json`, the specific SDK patch version it specifies is used if it is found.
3. When the resolver finds a `global.json`, and the SDK it specifies can't be found, the resolver looks for a close match:
    * Prior to .NET Core 2.1
      * The highest patch version for the SDK feature band is used _regardless of whether it is higher than what is specified_. For example, if `global.json` specifies 2.2.103 is specified and is not available, and 2.2.101 is the highest SDK on the machine, it will be used.
    * In .NET Core 2.1 and higher
      * The highest patch version for the SDK feature band is used _assuming it is higher than what is specified_. For example, if `global.json` specifies 2.2.103 is specified, and 2.2.101 is the highest SDK on the machine, an error will occur.

Due to the rules above, the following occurs:

* Due to #2, simply installing a patch on the machine will not update the machine to use the safer version.
  * The impact of this is mitigated by work we did in .NET Core 3.0 prerelease version 3 to replace earlier patch versions.
* Due to #3, users can not guarantee they are pinned to a specific version.

## Customer problems to address

* User wants to install a prerelease version but only use it with a subset of projects.
  * Due to Rule #1, a `global.json` is the only way to opt out of a prerelease version in the common case of the prerelease version being the highest version on the machine.
  * The `global.json` the user adds must include a specific version.
  * If no matching SDK version in the feature band (xyz in x.y.z.nn) is found, the command being called fails.
  * Each `global.json` must later be updated to use the new stable SDK.
  * Due to Rule #2, there is no roll forward from prerelease version to stable if they are both on the machine.
    * The impact of this is mitigated by work we did in .NET Core 3.0 Preview 3 to replace earlier patch versions.
* User wants to always use a specific SDK patch version, and receive an error if it is not on the machine.
  * The user cannot currently do this.
* User wants to always run the latest patch of a particular SDK feature band.
  * Due to Rule #2, the only way to do this is to remove the previous version.
    * The impact of this is mitigated by work we did in .NET Core 3.0 Preview 3 to replace earlier patch versions.
* User includes a feature that requires an SDK that corresponds to a SDK feature band or higher, and wants to run the best available tools.
  * There is no mechanism to require at least a specific version, but otherwise use the latest, or the latest in a range.
  * Upgrading to a newer SDK will provide no benefit to projects tied to an earlier version.
* User wants to know what version of the SDK is being used.
  * `global.json` is a subtle, hidden and easily forgotten marker – programmers may not understand the downsides of leaving a global.json that was suggested in a blog post in place – or they may forget it.
* User rarely wants the risk of running the unsupported scenario of multiple SDK versions in a solution build.
  * There is nothing that encourages users to place `global.json` in the repository root or the same level as the solution. Using different versions of the SDK for different projects in the solution is not a supported scenario.
* Hosted CI, including Azure DevOps and Kudu, need to provide a small number of SDKs.
  * If there is no patch of the major/minor/feature version (xyz in `x.y.znn`) present, the build fails, causing friction between user and host needs.

## Proposal

This proposal parallels [runtime roll-forward behavior as initially described](https://github.com/dotnet/core-setup/blob/master/Documentation/design-docs/roll-forward-on-no-candidate-fx.md)} and [recent improvements](https://github.com/dotnet/designs/blob/master/accepted/runtime-binding.md).

The runtime options allow for three basic approaches: find nearest, find latest and find exact. 

## Details

* Runtime: From the [recent improvements](https://github.com/dotnet/designs/blob/master/accepted/runtime-binding.md) document" _In all cases except `disable` the highest available patch version is selected._"

The `global.json` schema to offers support for all of the identified scenarios that can be fixed without altering how we find `global.json`:

* `sdk`
  * `version`
    * The lowest SDK the resolver would select - any SDKs with lower versions are ignored.
    * Identical to today - wildcards are not supported.
    * Default: empty
  * `allowPrerelease`
    * `true` or `false`
    * Default
      * If called from Visual Studio, use the prerelease status requested
      * Otherwise,`true` to match today's behavior.
  * `rollForward` (The `version` must be specified if `rollForward` is specified, except `latestMajor`.)
    * There are four basic intents for roll-forward:
      * `patch`: legacy behavior.
      * `feature`, `minor`, `major`: highest patch of the nearest version within specified limits.
      * `latestPatch`, `latestFeature`, `latestMinor`, `latestMajor`: highest patch of the highest available version within specified limits.
      * `disable`: use exactly the version specified.
    * `patch`
      * If the requested major/minor/feature band/patch is found, use it.
      * Otherwise, if there are higher patches within the major/minor/feature band, use the highest patch of that set.
      * Otherwise, fail.
      * This recreates the previous SDK selection criteria.
    * `feature`
      * If the requested major/minor/feature band is found, use the highest patch within that set.
      * Otherwise, if there are higher feature band versions within the major/minor, use the highest patch in the lowest higher feature band of that set.
      * Otherwise, fail.
    * `minor`
      * If the requested major/minor/feature band is found, use the highest patch within that set.
      * Otherwise, if there are other feature band versions within the major/minor, use the highest patch in the lowest higher feature band of that set.
      * Otherwise, if there are higher minor versions within the major, use the lowest higher minor version in that set, it's lowest feature band, and the highest patch within that feature band.
      * Otherwise, fail.
    * `major`
      * If the requested major/minor/feature band is found, use the highest patch within that set.
      * Otherwise, if there are other feature band versions within the major/minor, use the highest patch in the lowest higher feature band of that set.
      * Otherwise, if there are other minor versions within the major, use the lowest higher minor version in that set, its lowest feature band, and the highest patch within that feature band.
      * Otherwise, if there higher major versions, use the lowest higher major version in that set, its lowest minor, its lowest feature band, and the highest patch within that feature band. 
      * Otherwise, fail.
    * `latestPatch`
      * Even if the requested major/minor/feature band/patch is found, do not use it unless it matches rules below.
      * Considering only the equal and higher patches in the major/minor/feature band, select the highest available patch.
      * If there are no equal or higher patches in the major/minor/feature band, fail.
    * `latestFeature`
      * Even if the requested major/minor/feature band is found, do not use it unless it matches rules below.
      * Considering only the equal and higher feature bands in the major/minor, select the highest available feature band and its highest patch.
      * If there are no equal or higher feature bands in the major/minor, fail.
    * `latestMinor`
      * Even if the requested major/minor is found, do not use it unless it matches rules below.
      * Considering only the equal and higher minor versions in the major version, select the highest available minor, its highest feature band and its highest patch.
      * If there are no equal or higher minor versions in the major, fail.
    * `latestMajor` (latest)
      * Even if the requested major is found, do not use it unless it matches rules below.
      * Considering only the equal and higher major versions, select the highest available major, its highest minor, its highest feature band and its highest patch.
      * If there are no equal or higher major versions, fail.
    * `disable`
      * Do not roll forward. Only bind to specified version. This policy means the latest patches, including security patches will not be used.
 
## Default

### No `global.json`

If there is no `global.json` or a version is not specified, the default is`latestMajor`, again to match today's behavior.

### `global.json` containing a version number

**Change** The default behavior is changed. We took this change because we believe the new behavior will cause no change or be an improvement. 

The new behavior is `major` as described above. The previous behavior is `patch` as described above.

This changes behavior in two scenarios:

* Previously, if the specified patch version was present, it was used even if a higher patch number was installed. In discussing this with users, ignoring the updated patch was not the anticipated behavior. 
* If there was no patch version of the specified major/minor/feature band, SDK resolution previously failed. SDK resolution will now succeed. 

This will change behavior if the specified SDK is not present, but a higher version outside the current feature band is present. Previously SDK selection failed in this scenario, and now it will succeed.

## Examples to illustrate roll-forward options

The following table considers various SDK version combinations that may be available on the machine, and the outcome of the following `global.json`:

```json
{
  "sdk": {
    "version": "2.1.501",
    "rollForward": "<entry in table header>"
  }
}
```

| Available SDKs                                       | patch   | feature | minor   | major(*) | latestPatch | latestFeature | latestMinor | latestMajor | disable |
|------------------------------------------------------|---------|---------|---------|----------|-------------|---------------|-------------|-------------|---------|
| 2.1.500                                              | fail    | fail    | fail    | fail     | fail        | fail          | fail        | fail        | fail    |
| 2.1.501, 2.1.503                                     | 2.1.501 | 2.1.503 | 2.1.503 | 2.1.503  | 2.1.503     | 2.1.503       | 2.1.503     | 2.1.503     | 2.1.501 |
| 2.1.503, 2.1.505, 2.1.601, 2.2.101, 3.0.100          | 2.1.505 | 2.1.505 | 2.1.505 | 2.1.505  | 2.1.505     | 2.1.601       | 2.2.101     | 3.0.100     | fail    |
| 2.1.601, 2.1.604, 2.1.702, 2.2.101, 2.2.203, 3.0.100 | fail    | 2.1.604 | 2.1.604 | 2.1.604  | fail        | 2.1.702       | 2.2.203     | 3.0.100     | fail    |
| 2.2.101, 2.2.203, 3.0.100                            | fail    | fail    | 2.2.101 | 2.2.101  | fail        | fail          | 2.2.203     | 3.0.100     | fail    |
| 3.0.100, 3.1.102                                     | fail    | fail    | fail    | 3.0.102  | fail        | fail          | fail        | 3.1.102     | fail    |

(*)  Default

## Customer problems addressed

* User wants to install a prerelease version but only use it with a subset of projects.
  * User opts out in projects where prerelease version should not be used by adding `allowPrerelease: false` to those projects, or
  * User adds `allowPrerelease: false` in a `global.json` higher in their file system, and adds `allowPrerelease: true` in a `global.json` in specific projects.
* User wants to always use a specific SDK patch version, and receive an error if it is not on the machine.
  * User adds `version` and `rollForward: disable` to a `global.json`
* User wants to set a floor or lowest version and use the best installed version above that (common for public repos).
  * User adds `rollForward: latestMajor` and the lowest version (with patch) they want to a `global.json`
* User wants to always run the latest patch of a particular SDK feature band.
  * User adds `rollForward: latestPatch` and the lowest version (with patch) they want to a `global.json`
* Hosted CI, including Azure DevOps and Kudu, need to provide a small number of SDKs.
  * It will now be possible for users to specify `global.json` files in a way that can work with Azure DevOps (roll-forward across at least feature bands)
  * We will work with Azure DevOps to determine the recommended `global.json` for this scenario.

## Examples of usage

This section lists a series of scenarios and following the corresponding `global.json`. Following this is a table that shows the SDK that would be selected with specific combinations of SDKs present on the machine.

### 1. Previous behavior when a `global.json` specifies a version

The following `global.json` defines a specific version number, and to use a higher patch only when this version is not available. Note: this is not the default. 

```json
{
  "sdk": {
    "version": "2.2.100",
    "rollForward": "patch"
  }
}
```

### 2. Explicitly state previous behavior when there is no `global.json` by excluding the version number and stating behavior

Today's behavior is that an exact match is used if found, and if not found, the highest patch above the requested in the feature band is used.

If there is no `global.json` that would logically be the same as the following.

```json
{
  "sdk": {
    "allowPrerelease": true,
    "rollForward": "latestMajor"
  }
}
```

### 3. Skip prerelease versions, otherwise as though there was no `global.json`

If there is no `global.json` but the user does not want to use prerelease versions, that would logically be the same as the following:

```json
{
  "sdk": {
    "allowPrerelease": false,
    "rollForward": "latestMajor"
  }
}
```

The common `global.json` for this case is likely to be:

```json
{
  "sdk": {
    "allowPrerelease": false,
  }
}
```

The user may include their preference for prerelease versions high in their directory structure, and then individual project can elect different behavior with a new `global.json`. This will continue to follow today's rules. In particular: if there is a `global.json` file for a different reason, like a custom SDK, the higher `global.json` in the directory tree will not be used. 

### 4 Select highest available, higher than specified

If the user wants to specify the lowest SDK they want to use, they could use the following:

```json
{
  "sdk": {
    "version": "2.2.100",
    "allowPrerelease": true,
    "rollForward": "latestMajor"
  }
}
```

### 5. Select highest available, higher than specified, prerelease versions ignored

If the user wanted to use any SDK higher than the one listed but ignore prerelease versions, they could use the following:

```json
{
  "sdk": {
    "version": "2.2.100",
    "allowPrerelease": false,
    "rollForward": "latestMajor"
  }
}
```

### 6. Select highest available with unusual combination

If the user wants to use any SDK as high or higher than the one listed but ignore prerelease versions, they could use the following:

```json
{
  "sdk": {
    "version": "3.0.100-Pre",
    "allowPrerelease": false,
    "rollForward": "latestMajor"
  }
}
```

This will always fail. The error will be:

```
No SDK could be selected due to global.json issue: Specifying a prerelease version version with exactMatch and allowPrerelease is set to false will always fail.
```

### 7. Fail if exact match is not found

If the user wants an exact match, they could use the following.

```json
{
  "sdk": {
    "version": "2.2.100",
    "allowPrerelease": true,
    "rollForward": "disable"
  }
}
```

The common `global.json` for this case is likely to be:

```json
{
  "sdk": {
    "version": "2.2.100",
    "rollForward": "disable"
  }
}
```

When this fails, the error is:

```
The requested SDK version was not available, and SDK version roll-forward was disabled.
```

### 8. Highest within runtime major.minor

If the user wants the highest within a runtime major/minor, they could use the following. 


```json
{
  "sdk": {
    "version": "2.2.100",
    "allowPrerelease": true,
    "rollForward": "latestFeature"
  }
}
```

### 9. Exact match or highest within runtime major.minor

This proposal does not support arbitrary ranges or arbitrary upper bounds.


### Selections made

This table lists the SDKs selected in each of the above scenarios. 

| Available SDKs                | #1      | #2          | #3      | #4          | #5      | #6      | #7      | #8      |
|-------------------------------|---------|-------------|---------|-------------|---------|---------|---------|---------|
| 2.1.700                       | fail    | 2.1.700     | 2.1.700 | fail        | fail    | fail    | fail    | fail    |
| 2.2.100                       | 2.2.100 | 2.2.100     | 2.2.100 | 2.2.100     | 2.2.100 | fail    | 2.2.100 | 2.2.100 |
| 2.2.103                       | 2.2.103 | 2.2.103     | 2.2.103 | 2.2.103     | 2.2.103 | fail    | fail    | 2.2.103 |
| 2.1.700, 2.2.100, 2.2.103     | 2.2.100 | 2.2.103     | 2.2.103 | 2.2.103     | 2.2.103 | fail    | 2.2.100 | 2.2.100 |
| 2.1.700, 2.2.103, 3.1.100-Pre | 2.2.103 | 3.1.100-Pre | 2.2.103 | 3.1.100-Pre | 2.2.103 | fail    | fail    | 2.2.100 |
| 2.1.700, 2.2.103, 3.1.100     | 2.2.103 | 3.1.100     | 3.1.100 | 3.1.100     | 3.1.100 | fail    | fail    | 2.2.103 |

## Other proposals considered

Prior to arriving at this proposal, two others were considered. Neither of these align well with roll-forward on the .NET Core Runtime.

* An independent versioning scheme. It's largest downside is that it would be _another_ way users would have to think about specifying versioning behavior. 
* An attempt to parallel NuGet. Definining [NuGet version ranges](https://docs.microsoft.com/en-us/nuget/reference/package-versioning) is relatively complex, and it didn't feel like a good fit. 
