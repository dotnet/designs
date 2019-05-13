# Proposal for global.json updates for preview and limited roll-forward

---
**NOTE:** 

SDK version numbers do not use semantic versioning. [Docs has an article on SDK versioning](https://docs.microsoft.com/en-us/dotnet/core/versions/).

---

`global.json` allows users to pin the version of the SDK they are using. This was added to .NET Core in case we introduced an issue to allow roll-back per project until a fix could be provided. While our guidance remains to use it only for special circumstances, users now use it for numerous reasons.

This document describes changes to `global.json` to provide more flexibility.

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

## Proposal

### Roll-forward

.NET Core SDK "roll-forward" is problematic because version numbers are not linear. For example: 2.1.600 contains MSBuild 16 while 2.2.100 contains MSBuild 15.9. This is not a new problem, since it already occurs when there is no `global.json`. However, we do not want to roll-forward in scenarios that might surprise the user. Because of this, roll-forward will be allowed only in limited scenarios.

### Details

The `global.json` schema to offers support for all of the identified scenarios that can be fixed without altering how we find `global.json`:

* `sdk`
  * `version`
    * The lowest SDK the resolver would select - any SDKs with lower versions are ignored.
    * Wildcards are not supported.
    * Default: empty
  * `ignorePreview`
    * `true` or `false`
    * Default: `false` to match previous behavior.
    * Alternate names: `releaseOnly` or `useReleaseOnly`
    * This is new behavior.
  * `rollForward`
    * Lower version numbers are always ignored. 
    * `patch`
      * If the requested major, minor, feature band, and patch is found, use it.
      * Otherwise, use the highest patch within the specified major, minor and feature band.
      * This policy means the latest patches, including security patches will not be used unless the specified version is remove from the machine.
      * This matches previous behavior when a `global.json` file contains a version.
    * `latestPatch`
      * Even if the requested major, minor, feature band and patch is found, do not use it unless it matches rules below.
      * Select the highest available patch version that matches the specified major, minor and feature band versions.
      * This is new behavior.
    * `disable`
      * Do not roll forward. Only bind to specified version. 
      * This policy means the latest patches, including security patches will not be used.
      * This is new behavior.
    * `latest`
      * Even if the requested major, minor, feature band and patch is found, do not use it unless it matches rules below.
      * Select the highest available major version, and select it's highest minor version, and that minor's highest feature band, and that feature band's highest patch.
      * Using latest with a version is new behavior. Otherwise this matches previous behavior when there is no `global.json` or no version is specified.
    * Defaults: 
      * Defaults are defined to support backward compatibility issues.
      * If a version is specified in the `global.json`, the default is `patch` to match previous behavior.
      * If there is no `global.json` or a version is not specified, the default is`latest`, again to match previous behavior.

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

| Available SDKs                              | patch   | latestPatch | latest  | disable |
|---------------------------------------------|---------|-------------|---------|---------|
| 2.1.500                                     | fail    | fail        | fail    | fail    |
| 2.1.501, 2.1.503                            | 2.1.501 | 2.1.503     | 2.1.503 | 2.1.501 |
| 2.1.503, 2.1.505, 2.1.601, 2.2.101, 3.0.100 | 2.1.503 | 2.1.505     | 3.0.100 | fail    |
| 2.2.101, 2.2.203, 3.0.100                   | fail    | fail        | 3.0.100 | fail    |
| 3.0.100, 3.1.102                            | fail    | fail        | 3.1.102 | fail    |

Or, if it is easier to understand possible logic:

1. If `ignorePreview` is `false`, exclude all preview versions.
1. If `patch` or `disable`, and there is a match, select it.
1. If we get here for `disable`, then fail.
1. Exclude any lower versions.
1. If `patch` or `latestPatch`, exclude any that don't match major, minor and feature band version.
1. If there are none left, then fail.
1. If there is one left, select it.
1. Select the highest remaining.

## Customer problems to address

| Problem                                                       | Current                                                                                                                                        | Proposed                                                                                                                                        |
|---------------------------------------------------------------|------------------------------------------------------------------------------------------------------------------------------------------------|-------------------------------------------------------------------------------------------------------------------------------------------------|
| Install a preview but use it with only a subset of projects.  | `version: <specific version>` or `version: -pre` or similar (*)| `ignorePreview: true`, or `ignorePreview: true` higher in the file system and `ignorePreview: false`. |
| Fail if specific version is not available.                    | Not possible                                                                                                                                   | `rollForward: disable` in `global.json`                                                                                                         |
| Always run the latest patch of a particular SDK feature band. | Not possible                                                                                                                                   | `rollForward: latestPatch` and lowest acceptable `version` (**)                                                                                  |
| Use at least a specific version, otherwise latest             | Not possible                                                                                                                                   | `rollForward: latest` and lowest acceptable `version` (**)                                                                                       |
| Hosted CI                                                     | Friction matching SDKs for users                                   
                                                                  | _This proposal needs review by DevOps                                                                                                           |
(*) One pain point of this is that these `global.json` file must be removed or later be updated to use the new stable SDK, which is almost never the desired behavior.
(**) Because the SDK version numbers are not linear, this will not always result in the highest SDK on the machine being used. 

## Examples of usage

This section lists a series of scenarios and following the corresponding `global.json`. Following this is a table that shows the SDK that would be selected with specific combinations of SDKs present on the machine.

### 1. Explicitly state previous behavior when a `global.json` specifies a version

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

### 2. Explicitly state previous behavior when there is no `global.json`

previous behavior is that an exact match is used if found, and if not found, the highest patch above the current in the feature band is used.

If there is no `global.json` that would logically be the same as the following.

```json
{
  "sdk": {
    "ignorePreview": false,
    "rollForward": "latest"
  }
}
```

### 3. Skip previews, otherwise as though there was no `global.json`

If there is no `global.json` but the user does not want to use previews, that would logically be the same as the following:

```json
{
  "sdk": {
    "ignorePreview": true,
    "rollForward": "latest"
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

The user may include their preference for previews high in their directory structure, and then individual project can elect different behavior with a new `global.json`. This will continue to follow previous rules. In particular: if there is a `global.json` file for a different reason, like a custom SDK, the higher `global.json` will not be used. 

### 4 Select highest available, higher than specified

If the user wants to specify the lowest SDK they want to use, they could use the following:

```json
{
  "sdk": {
    "version": "2.2.100",
    "ignorePreview": false,
    "rollForward": "latest"
  }
}
```

The common `global.json` for this case is likely to be:

```json
{
  "sdk": {
    "version": "2.2.100",
    "rollForward": "latest"
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
    "rollForward": "latest"
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
    "rollForward": "highest"
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

### Selections made

This table lists the SDKs selected in each of the above scenarios. 

| Available SDKs                | #1      | #2          | #3      | #4          | #5      | #6      | #7      |
|-------------------------------|---------|-------------|---------|-------------|---------|---------|---------|
| 2.1.700                       | fail    | 2.1.700     | 2.1.700 | fail        | fail    | fail    | fail    |
| 2.2.100                       | 2.2.100 | 2.2.100     | 2.2.100 | 2.2.100     | 2.2.100 | fail    | 2.2.100 |
| 2.2.103                       | 2.2.103 | 2.2.103     | 2.2.103 | 2.2.103     | 2.2.103 | fail    | fail    |
| 2.1.700, 2.2.100, 2.2.103     | 2.2.100 | 2.2.103     | 2.2.103 | 2.2.103     | 2.2.103 | fail    | 2.2.100 |
| 2.1.700, 2.2.103, 3.1.100-Pre | 2.2.103 | 3.1.100-Pre | 2.2.103 | 3.1.100-Pre | 2.2.103 | fail    | fail    |
| 2.1.700, 2.2.103, 3.1.100     | 2.2.103 | 3.1.100     | 3.1.100 | 3.1.100     | 3.1.100 | 3.1.100 | fail    |

## Other proposals considered

Prior to arriving at this proposal, two others were considered. 

The first, was an independent versioning scheme. It's largest downside is that it would be _another_ way users would have to think about specifying versioning behavior. 

The second was an attempt to parallel NuGet. Definining [NuGet version ranges](https://docs.microsoft.com/en-us/nuget/reference/package-versioning) is relatively complex, and it didn't feel like a good fit. 

The recommended proposal (above), aligns closely to existing behavior and parallels runtime roll-forward behavior in other cases.
