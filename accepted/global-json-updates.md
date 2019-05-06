# Proposal for global.json updates for preview and roll-forward

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

## Current behavior

The SDK resolvers (in both the CLI and VS) search from the current location (project in VS) up the directory hierarchy until they encounter a `global.json` file. When the resolver finds one, _it stop looking_ and attempts to resolve the SDK version specified in that file. This happens even if this `global.json` does not specify an SDK version.

When a `global.json` is encountered and it specifies an SDK version, the SDK resolvers attempt to locate that specific SDK patch version:

1. When the resolver finds no `global.json` or finds one that does not specify SDK version, the latest SDK is used, regardless of whether it is preview or stable.
2. When the resolver finds a `global.json`, the specific SDK patch version it specifies is used if it is found.
3. When the resolver finds a `global.json`, and the SDK it specifies can't be found, the resolver looks for a close match:
    * Prior to .NET Core 2.1
      * The highest patch version for the SDK feature band is used _regardless of whether it is higher than what is specified_. For example, if `global.json` specifies 2.2.103 is specified and is not available, and 2.2.101 is the highest SDK on the machine, it will be used.
    * In .NET Core 2.1 and higher
      * The highest patch version for the SDK feature band is used _assuming it is higher than what is specified_. For example, if `global.json` specifies 2.2.103 is specified, and 2.2.101 is the highest SDK on the machine, an error will occur.

Due to the rules above, the following occurs:

* Due to #2, simply installing a patch on the machine will not update the machine to use the safer version.
  * The impact of this is mitigated by work we did in .NET Core 3.0 Preview 3 to replace earlier patch versions.
* Due to #3, users can not guarantee they are pinned to a specific version.

## Customer problems to address

* User wants to install a preview but only use it with a subset of projects.
  * Due to Rule #1, a `global.json` is the only way to opt out of a preview in the common case of the preview being the highest version on the machine.
  * The `global.json` the user addss must include a specific version.
  * If no matching SDK version in the feature band (xyz in x.y.z.nn) is found, the command being called fails.
  * Each `global.json` must later be updated to use the new stable SDK.
  * Due to Rule #2, there is no roll forward from preview to stable if they are both on the machine.
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

## Proposal C

This proposal parallels [runtime roll-forward behavior as initially described](https://github.com/dotnet/core-setup/blob/master/Documentation/design-docs/roll-forward-on-no-candidate-fx.md)} and [recent improvements](https://github.com/dotnet/designs/blob/master/accepted/runtime-binding.md). All of the features proposed here may not be included in Phase 1. It will include at least `ignorePreview` and `"rollforward": "disable"`

This is called Proposal C to differentiate it from two earlier proposals that were discarded but are included at the end of this document for reference.

### Details

The runtime options allow for three basic approaches: find nearest, find latest and find exact. 

To avoid a backward compatibility issue, the default recovery strategy when a match is not found needs to match the old `dotnet` behavior. This defines the defaults, and the default behavior differs between runtime. 

* Runtime: From the [recent improvements](https://github.com/dotnet/designs/blob/master/accepted/runtime-binding.md) document "_In all cases except `disable` the highest available patch version is selected._"
* SDK: For backwards compatibility, the additonal option `patch` is offered and is the default.
  * If the specified patch version is found, it is used.
  * In all other cases (except `disable`), the highest patch within the appropriate feature band is used. 

The `global.json` schema to offers support for all of the identified scenarios that can be fixed without altering how we find `global.json`:

* `sdk`
  * `version`
    * The lowest SDK the resolver would select - any SDKs with lower versions are ignored.
    * Identical to today - wildcards are not supported.
    * Default: empty
  * `ignorePreview`
    * `true` or `false`
    * Default: `false` to more closely match today's behavior.
    * Alternate names: `releaseOnly` or `useReleaseOnly`
  * `rollForward`
    * To avoid some of the repetitiveness, I do not restate in these rules that version numbers below that specified are always ignored. 
    * `patch`
      * If the requested major, minor, feature band, and patch if found, use it.
      * Otherwise, use the highest patch within the specified major, minor and feature band.
    * `feature`
      * If the requested major, minor, feature band, and patch is found, use it.
      * Otherwise, if the requested major, minor and feature band is found, use the highest patch in that band.
      * Otherwise, use the lowest higher feature band, and use the highest patch within that feature band.
    * `minor`
      * If the requested major, minor, feature band, and patch is found, use it.
      * Otherwise, if the requested major, minor and feature band is found, use the highest patch in that band.
      * Otherwise, if the requested major and minor is found, use the highest patch in the _lowest_ feature band within that major and minor version.
      * Otherwise, use the lowest higher minor, it's lowest feature band, and the highest patch within that feature band.
    * `major` 
      * If the requested major, minor, feature band, and patch is found, use it.
      * Otherwise, if the requested major, minor and feature band is found, use the highest patch in that band.
      * Otherwise, if the requested major and minor is found, use the highest patch in the _lowest_ feature band within that major and minor version.
      * Otherwise, if the requested major is found, use the  _lowest_ minor within that major version, and it's lowest feature band, and it's highest patch.
      * Otherwise, use the lowest higher major, it's lowest minor, and it's lowest feature band, and the highest patch within that feature band.
    * `latestPatch`
      * Even if the requested major, minor, feature band and patch is found, do not use it unless it matches rules below.
      * Select the highest available patch version that matches the specified major, minor and feature band versions.
    * `latestFeature`
      * Even if the requested major, minor, feature band and patch is found, do not use it unless it matches rules below.
      * Select the highest available feature band that matches the specified major and minor version, and select it's highest patch.
    * `latestMinor`
      * Even if the requested major, minor, feature band and patch is found, do not use it unless it matches rules below.
      * Select the highest available minor that matches the specified major version, and select it's highest feature band, and that feature band's highest patch.
    * `latestMajor` 
      * Even if the requested major, minor, feature band and patch is found, do not use it unless it matches rules below.
      * Select the highest available major version, and select it's highest minor version, and that minor's highest feature band, and that feature band's highest patch.
    * `disable`
      * Do not roll forward. Only bind to specified version. This policy means the latest patches, including security patches will not be used.
    * Defaults: 
      * If a version is specified in the `global.json`, the default is `patch` to match today's behavior.
      * If there is no `global.json` or a version is not specified, the default is`latestMajor`, again to match today's behavior.
 
### Examples to illustrate roll-forward options

The following table considers various SDK version combinations that may be available on the machine, and the outcome of the following `global.json`:

```json
{
  "sdk": {
    "version": "2.1.501",
    "rollForward": [entry in table header]
  }
}
```

| Available SDKs                                       | patch     | feature | minor   | major   | latestPatch | latestFeature | latestMinor | latestMajor | disable |
|------------------------------------------------------|-----------|---------|---------|---------|-------------|---------------|-------------|-------------|---------|
| 2.1.500                                              | fail      | fail    | fail    | fail    | fail        | fail          | fail        | fail        | fail    |
| 2.1.501, 2.1.503                                     | _2.1.501_ | 2.1.501 | 2.1.501 | 2.1.501 | _2.1.503_   | 2.1.503       | 2.1.503     | 2.1.503     | 2.1.501 |
| 2.1.503, 2.1.505, 2.1.601, 2.2.101, 3.0.100          | 2.1.503   | 2.1.503 | 2.1.503 | 2.1.503 | 2.1.505     | 2.1.601       | 2.2.101     | 3.0.100     | fail    |
| 2.1.601, 2.1.604, 2.1.702, 2.2.101, 2.2.203, 3.0.100 | fail      | 2.1.601 | 2.1.601 | 2.1.601 | fail        | 2.1.702       | 2.2.203     | 3.0.100     | fail    |
| 2.2.101, 2.2.203, 3.0.100                            | fail      | fail    | 2.2.101 | 2.2.101 | fail        | fail          | 2.2.203     | 3.0.100     | fail    |
| 3.0.100, 3.1.102                                     | fail      | fail    | fail    | 3.0.100 | fail        | fail          | fail        | 3.1.102     | fail    |

Or, if it is easier to understand with logic (bail out of logic on fail or select):

1. If not `latest`, and there is a match, select it.
1. If we get here for `disable`, then fail.
1. Exclude any lower versions.
1. If `minor` or `latestMinor`, exclude any that don't match major version. 
1. If `feature` or `latestFeature`, exclude any that don't match major and minor version.
1. If `patch` or `latestPatch`, exclude any that don't match major, minor and feature band version.
1. If there is one left, select it.
1. If any of the `latest` options are used, select the highest. 
1. Otherwise, select the lowest major, and within that lowest minor, and within that lowest feature band. Then select the highest patch in that feature band.  

## Customer problems addressed

* User wants to install a preview but only use it with a subset of projects.
  * User opts out in projects where preview should not be used by adding `ignorePreview: true` to those projects, or
  * User adds `ignorePreview: true` in a `global.json` higher in their file system, and adds `ignorePreview: false` in a `global.json` in specific projects.
* User wants to always use a specific SDK patch version, and receive an error if it is not on the machine.
  * User adds `rollForward: disable` to a `global.json`
* User wants to always run the latest patch of a particular SDK feature band.
  * User adds `rollForward: latestPatch` and the lowest version (with patch) they want to a `global.json`
* User includes a feature that requires an SDK that corresponds to a SDK feature band or higher, and wants to run the best available tools.
  * User adds `rollForward: latestMajor` and the lowest version (with patch) they want to a `global.json`
* User wants to know what version of the SDK is being used.
  * Not addressed in this proposal: `global.json` will still be a subtle, hidden and easily forgotten marker
* User rarely wants the risk of running the unsupported scenario of multiple SDK versions in a solution build.
  * Not addressed in this proposal: There is nothing that encourages users to place `global.json` in a logical location.
* Hosted CI, including Azure DevOps and Kudu, need to provide a small number of SDKs.
  * It will now be possible for users to specify `global.json` files in a way that can work with Azure DevOps (roll-forward across at least feature bands)
  * We will work with Azure DevOps to determine the recommended `global.json` for this scenario.

## Examples of usage

This section lists a series of scenarios and following the corresponding `global.json`. Following this is a table that shows the SDK that would be selected with specific combinations of SDKs present on the machine.

### 1. Explicitly state today's behavior when a `global.json` specifies a version

The following `global.json` defines a specific version number, and to use a higher version if it is available.

```json
{
  "sdk": {
    "version": "2.2.100",
    "ignorePreview": false,
    "rollForward": "patch"
  }
}
```

The common `global.json` for this case is likely to be:

```json
{
  "sdk": {
    "version": "2.2.100"
  }
}
```

### 2. Explicitly state today's behavior when there is no `global.json`

Today's behavior is that an exact match is used if found, and if not found, the highest patch above the current in the feature band is used.

If there is no `global.json` that would logically be the same as the following.

```json
{
  "sdk": {
    "ignorePreview": false,
    "rollForward": "latestMajor"
  }
}
```

### 3. Skip previews, otherwise as though there was no `global.json`

If there is no `global.json` but the user does not want to use previews, that would logically be the same as the following:

```json
{
  "sdk": {
    "ignorePreview": true,
    "rollForward": "latestMajor"
  }
}
```

The common `global.json` for this case is likely to be:

```json
{
  "sdk": {
    "ignorePreview": true,
  }
}
```

The user may include their preference for previews high in their directory structure, and then individual project can elect different behavior with a new `global.json`. This will continue to follow today's rules. In particular: if there is a `global.json` file for a different reason, like a custom SDK, the higher `global.json` will not be used. 

### 4 Select highest available, higher than specified

If the user wants to specify the lowest SDK they want to use, they could use the following:

```json
{
  "sdk": {
    "version": "2.2.100",
    "ignorePreview": false,
    "rollForward": "latestMajor"
  }
}
```

The common `global.json` for this case is likely to be:

```json
{
  "sdk": {
    "version": "2.2.100",
    "rollForward": "latestMajor"
  }
}
```

### 5. Select highest available, higher than specified, previews ignored

If the user wanted to use any SDK higher than the one listed but ignore previews, they could use the following:

```json
{
  "sdk": {
    "version": "2.2.100",
    "ignorePreview": true,
    "rollForward": "latestMajor"
  }
}
```

### 6. Select highest available with unusual combination

If the user wants to use any SDK as high or higher than the one listed but ignore previews, they could use the following:

```json
{
  "sdk": {
    "version": "3.0.100-Pre",
    "ignorePreview": true,
    "rollForward": "highestMajor"
  }
}
```

This will always fail. The error will be:

```
No SDK could be selected due to global.json issue: Specifying a preview version with exactMatch and skipPreview set to true will always fail.
```

### 7. Fail if exact match is not found

If the user wants an exact match, they could use the following.

```json
{
  "sdk": {
    "version": "2.2.100",
    "ignorePreview": false,
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

### 8. Exact match or highest within runtime major.minor

This proposal does not support arbitrary ranges or arbitrary upper bounds.

### 9. Highest within runtime major.minor

If the user wants the highest within a runtime major/minor, they could use the following. 


```json
{
  "sdk": {
    "version": "2.2.100",
    "ignorePreview": false,
    "rollForward": "latestMinor"
  }
}
```

### 10. Highest within arbitrary upper and lower bound

If the user wanted to use any SDK higher than the one listed but ignore previews, they could use the following. 

Proposal C

There is no concept of an arbitrary upper bound (as there isn't with the runtime)

### Selections made

This table lists the SDKs selected in each of the above scenarios. 

| Available SDKs                | #1      | #2          | #3      | #4          | #5      | #6      | #7      | #8      | #9      | #10     |
|-------------------------------|---------|-------------|---------|-------------|---------|---------|---------|---------|---------|---------|
| 2.1.700                       | fail    | 2.1.700     | 2.1.700 | fail        | fail    | fail    | fail    | fail    | fail    | 2.1.700 |
| 2.2.100                       | 2.2.100 | 2.2.100     | 2.2.100 | 2.2.100     | 2.2.100 | fail    | 2.2.100 | 2.2.100 | 2.2.100 | 2.2.100 |
| 2.2.103                       | 2.2.103 | 2.2.103     | 2.2.103 | 2.2.103     | 2.2.103 | fail    | fail    | 2.2.103 | 2.2.103 | 2.2.103 |
| 2.1.700, 2.2.100, 2.2.103     | 2.2.100 | 2.2.103     | 2.2.103 | 2.2.103     | 2.2.103 | fail    | 2.2.100 | 2.2.100 | 2.2.103 | 2.2.103 |
| 2.1.700, 2.2.103, 3.1.100-Pre | 2.2.103 | 3.1.100-Pre | 2.2.103 | 3.1.100-Pre | 2.2.103 | fail    | fail    | 2.2.100 | 2.2.103 | 2.2.103 |
| 2.1.700, 2.2.103, 3.1.100     | 2.2.103 | 3.1.100     | 3.1.100 | 3.1.100     | 3.1.100 | 3.1.100 | fail    | 2.2.103 | 2.2.103 | 2.2.103 |

## Other proposals considered

Prior to arriving at this proposal, two others were considered. The first, Proposal A is an independent versioning scheme. It's largest downside is that it would be _another_ way users would have to think about specifying versioning behavior. The second, Proposal B was an attempt to parallel NuGet. Definining [NuGet version ranges](https://docs.microsoft.com/en-us/nuget/reference/package-versioning) is relatively complex, and it didn't feel like a good fit. The recommended proposal (above), Proposal C, is based on runtime roll-forward behavior. 

### Proposal A

This proposal is independently created, not considering other versioning definitions like NuGet. 

The proposal for Phase 1 is limited to allow us to complete it as quickly as possible - specifically for .NET Core 3.0.  Phase 1 would support only `ignorePreview` and possibly `exactMatch`.

This Phase 2 proposal extends the `global.json` schema to offer support for all of the identified scenarios:

* `sdk`
  * `version`
    * The lowest SDK the resolver would select - any SDKs with lower versions are ignored.
    * Identical to today - wildcards are not supported.
    * Default: empty
  * `ignorePreview`
    * `true` or `false`
    * (Proposed) Default: `false` to more closely match today's behavior.
    * Alternative text to consider: `releaseOnly`, `allowPrerelease`
  * `matchRule`
    * `exactMatch`
    * `exactMatchOrHighest` - exact match to version or highest below upper limit. If no upper limit is declared, highest on box. If no version is specified, the highest considering upper limit or on box.
    * `highest` - highest below upper limit. If no upper limit is declared, highest on box.
    * Default: `exactMatchOrHighest` to more closely match today's behavior if a version is specified.
  * `upperLimit`
    * An upper limit that supports wildcards.
      * Asterisk are the only character valid as a wildcard
      * All asterisk must be right of all numbers. 2.2.2** is legal. 2.*.2** is illegal.
    * Default: 
      * If exactMatchOrHighest and the version is specified, the feature band of the version. If the feature band is `2.2.203`, the default is `2.2.1**`. 
      * If no version is specified, `*.*.***`.
      * Alternative to consider: NuGet's approach of exclusive upper limit.

#### Examples

Example 1

```json
{
  "sdk": {
    "version": "2.2.100",
    "skipPreview": false,
    "matchRule": "exactMatchOrHighest",
    "upperLimit": "2.2.1**"
  }
}
```

Example 2

```json
{
  "sdk": {
    "skipPreview": false,
    "matchRule": "highest",
    "upperLimit": "*.*.***"
  }
}
```

Example 3

```json
{
  "sdk": {
    "skipPreview": true,
    "matchRule": "highest",
    "upperLimit": "*.*.***"
  }
}
```

In this case, `whenNotFound` has no meaning.

Example 4

```json
{
  "sdk": {
    "version": "2.2.100",
    "skipPreview": false,
    "matchRule": "highest",
    "upperLimit": "*.*.***"
  }
}
```

Example 5

```json
{
  "sdk": {
    "version": "2.2.100",
    "matchRule": "highest",
    "skipPreview": true,
    "upperLimit": "*.*.***"
  }
}
```

Example 6

```json
{
  "sdk": {
    "version": "3.0.100-Pre",
    "matchRule": "highest",
    "skipPreview": true,
    "upperLimit": "*.*.***"
  }
}
```

Example 7

```json
{
  "sdk": {
    "version": "2.2.100",
    "matchRule": "exactMatch",
    "skipPreview": false,
    "upperLimit": "*.*.***"
  }
}
```

Example 8

```json
{
  "sdk": {
    "version": "2.2.100",
    "matchRule": "exactMatchOrHighest",
    "skipPreview": false,
    "upperLimit": "2.2.***"
  }
}
```

Example 9

```json
{
  "sdk": {
    "version": "2.2.100",
    "matchRule": "highest",
    "skipPreview": true,
    "upperLimit": "2.2.***"
  }
}
```

Example 10

```json
{
  "sdk": {
    "version": "2.1.203",
    "matchRule": "highest",
    "skipPreview": true,
    "upperLimit": "2.2.***"
  }
}
```

#### Proposal A implementation thought exercise

From a user perspective, considering what the user may want to achieve and looking at the results with different SDKs available is helpful. However, this makes it look like a bit of a nightmare to code. Here is one sequence that is not crazy hard:

1. Assign defaults (use high numbers for stars).
1. Look for the `global.json` rules that produce errors and fail if that occurs.
1. If `matchRule == exactMatch` see if there is a match and fail if not.
1. If `matchRule == exactMatchOrHighest` and there is one exact match, use it.
1. Find all available SDKs.
1. Remove any previews if `skipPreview == true`.
1. Remove any that are below the `version`.
1. Remove any that are above the `upperLimit`.
1. If none are left, fail.
1. Since either `matchRule == highest` or `matchRule == exactMatchOrHighest` without a match must now be true, use the highest.

#### Error: Special error on ExactMatch

Proposal A

```json
{
  "sdk": {
    "matchRule": "exactMatch",
    "skipPreview": true,
    "upperLimit": "*.*.***"
  }
}
```

The same error appears for `exactMatch` or `exactMatchOrHighest` match rules.

The error is:

```
No SDK could be selected due to global.json issue: exactMatch and exactMatchOrHighest matchRules can't be used without a version.
```

#### Error: Special error when could never succeed due to `version`, `exactMatch` and `skipPreview` flags

Proposal A

```json
{
  "sdk": {
    "version": "3.0.100-Pre",
    "matchRule": "exactMatch",
    "skipPreview": true,
    "upperLimit": "*.*.***"
  }
}
```

The error is:

```
No SDK could be selected due to global.json issue: Specifying a preview version with exactMatch and skipPreview set to true will always fail.
```

#### Error: Special error when could never succeed because `upperLimit` is below `version`

Proposal A

```json
{
  "sdk": {
    "version": "3.0.100",
    "matchRule": "exactMatch", // any value
    "skipPreview": false,      // any value
    "upperLimit": "2.*.***"
  }
}
```

The error is:

```
No SDK could be selected due to global.json issue: Version is higher than upperLimit.
```

### Proposal B

_NOTE: On reflection, this version as stated would have a backwards compatibility problem. `global.json` with unexpected characters in the version would probably fail. This could be managed with a different name, but this proposal seems overly compex anyway._

This proposal parallels NuGet version selection. Initially only the portions of the [NuGet versioning spec](https://docs.microsoft.com/en-us/nuget/reference/package-versioning#version-ranges-and-wildcards) listed here would be supplied.

To avoid a backward compatibility issue, the default recovery strategy when a match is not found needs to match the old 'dotnet' behavior, for specifications below 3.0. This approach could solve that by retaining current behavior for version definitions below 3.0 that do not use any NuGet formatting. 

The proposal for Phase 1 is limited to allow us to complete it as quickly as possible - specifically for .NET Core 3.0.  Phase 1 would support only `allowPrerelease`.

This Phase 2 proposal extends the `global.json` schema to offer support for all of the identified scenarios:

* `sdk`
  * `version`
    * The lowest SDK the resolver would select - any SDKs with lower versions are ignored.
    * [NuGet versioning](https://docs.microsoft.com/en-us/nuget/reference/package-versioning#version-ranges-and-wildcards)
    * Default: empty
  * `allowPrerelease`
    * `true` or `false`
    * (Proposed) Default: `true` to more closely match today's behavior.
  * `matchRule`
    * `highest` - Use the version listed is a lower bound and the highest is selected, even if the stated version exists. 
    * `legacy` - Use the exact patch version if it is available. If it is not available, use the highest patch version in the current feature band.
    * Default: `legacy` to match today's behavior if a version is specified. `fail` may be a better long term default, but changing at 3.0 means that `version: 2.0.0` and `version: 3.0.0` behave differently.
 
Example 1

```json
{
  "sdk": {
    "version": "2.2.100",
    "allowPrerelease": true,
    "matchRule": "legacy"
  }
}
```

Example 2

```json
{
  "sdk": {
    "version": "*",
    "allowPrerelease": true,
    "matchRule": "legacy"
  }
}
```

Example 3

```json
{
  "sdk": {
    "version": "*",
    "allowPrerelease": false,
    "matchRule": "legacy"
  }
}
```

In this case, `matchRule` has no meaning.

Example 4

```json
{
  "sdk": {
    "version": "2.2.100",
    "allowPrerelease": true,
    "matchRule": "highest"
  }
}
```

Example 5

```json
{
  "sdk": {
    "version": "2.2.100",
    "allowPrerelease": false,
    "matchRule": "highest"
  }
}
```

Example 6

```json
{
  "sdk": {
    "version": "3.0.100-Pre",
    "matchRule": "highest",
    "allowPrerelease": false,
  }
}
```

Example 7

```json
{
  "sdk": {
    "version": "[2.2.100]",
    "allowPrerelease": true,
    "matchRule": "highest"
  }
}
```

Example 8

```json
{
  "sdk": {
    "version": "[2.2.100,2.2.*]",
    "allowPrerelease": true,
    "matchRule": "legacy"
  }
}
```

Example 9

```json
{
  "sdk": {
    "version": "2.2.*",
    "allowPrerelease": true,
    "matchRule": "highest"
  }
}
```

Example 10

```json
{
  "sdk": {
    "version": "[2.1.203,2.2.*]",
    "allowPrerelease": false,
    "matchRule": "highest"
  }
}
```

## Additional suggestions

These suggestions have been received via Twitter, and I want to acknowledge that we've seen them and why we aren't acting on them at present.

### [Have Visual Studio offer to download the requested SDK](https://twitter.com/Nick_Craver/status/1124649778078527488)

This idea is for Visual Studio, so it is not covered in this proposal. 

The roll-forward within feature band is a feature, so this probably makes sense only when the presence of global.json would fail.

