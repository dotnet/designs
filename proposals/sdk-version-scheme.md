# .NET Core SDK Versioning

.NET Core runtimes and SDKs are used together. They release independently, so it has been a challenge picking sensible version numbers. Going forward, the first and second positions of the version number will match for the Runtime and SDK. The third position will not match since the Runtime and SDK release independently.

The Runtime will follow [semantic versioning](https://semver.org/). The first, second and third positions of the Runtime version number will correspond to major, minor and patch versions, respectively.

The SDK will not follow [semantic versioning](https://semver.org/). The first and second positions of the SDK version number will match the major and minor version of the Runtime it contains. You can use the major.minor version of the SDK to determine the maximum runtime it supports. Each version of the SDK supports previous versions of the runtime. 

For the SDK, both feature-level and servicing changes will be represented in the third position of the version number.

## Scheme

* The first and second position of the version of the .NET Core SDK will always match the .NET Core Runtime.
* The third position of the version of the .NET Core SDK will generally not match the Runtime.
* The third position of the .NET Core SDK version will contain gaps to allow servicing of releases.

The third position will increment to the next multiple of 100 for each feature release, and by 1 for each servicing update. As an example of SDK servicing, we may need to update an older version of the SDK to fix an earlier version of the C# compiler.

By matching the major and minor versions between the SDK and Runtime, we will better show how these releases align. In general, programmers can use the highest available SDK, which will be backwards compatible with previous runtimes.

## Examples

A hypothetical set of releases to illustrate the type of pattern:

| Runtime | SDK     | Runtime Change | SDK Change |
|---------|---------|----------------|------------|
| 2.1.0   | 2.1.100 | Feature        | Feature    |
| 2.1.0   | 2.1.200 | None           | Feature    |
| 2.1.0   | 2.1.201 | None           | Servicing  |
| 2.1.1   | 2.1.202 | Servicing      | Servicing  |
| 2.2.0   | 2.2.100 | Feature        | Feature    |
| 2.2.0   | 2.2.101 | None           | Servicing  |

> Examples:
>   * SDK feature release: includes C# 7.3
>   * SDK servicing release: includes .NET Core Runtime security update

When the Runtime shifts to a new minor release, the SDK will start at 100, rather than 0. This approach makes it easy to identify Runtime and SDK versions.

If required, we will roll our release number series into the 1000 range. The next release after x.y.9nn will be x.y.10nn.

## Use of the SDK Version Number

In some places visible to the user we will use the two part version number where the Runtime and SDK match. The full version numbers for the Runtime and SDK will be used when needed for clarity. The full name is 

 ```
 .NET Core SDK (v [full SDK version])

 .NET Core 2.1 SDK (v 2.1.100)
 ```

We hope this will reduce confusion and allow focus to remain on the important portions of the version number.

## SDK Selection

By default, the highest version of the SDK is used. You can override this behavior by specifying an SDK version in a global.json file.

### When global.json is present

SDK selection rules:

If global.json is present (and contain a version):
* If the version exists, use that
* If the version does not exist, use latest servicing version for a given feature version
* If no version has been found, error out

Note: These rules have not changed from previous SDK versions.

Behavior examples with previous SDK versioning scheme:

| Version in global.json| SDKs available | Result         |
|-----------------------|----------------|----------------|
| 2.0.1                 | 2.0.3, 2.1.0   | 2.0.3 will run |
| 2.0.1                 | 2.1.0          | error          |

Behavior with new SDK versioning scheme:

| Version in global.json | SDKs available   | Result           |
|-----------------------|------------------|------------------|
| 2.1.200               | 2.1.203, 2.1.300 | 2.1.203 will run |
| 2.1.200               | 2.1.300          | error            |

Behavior when SDK versions using both schemes are present:

| Version in global.json | SDKs available | Result        |
|-----------------------|----------------|----------------|
| 2.1.1                 | 2.1.3, 2.1.300 | 2.1.3 will run |
| 2.1.1                 | 2.1.300        | error          |

> Note: We will ship the updated SDK selection behavior with both .NET Core Runtime 2.1 and .NET Core SDK 2.1.300. This update in behavior is only important if your global.json includes a version that is 2.1.0 or higher and that SDK version is not present. From the release of .NET Core SDK 2.1.100 until the release of .NET Core SDK 2.1.300, we recommend you ensure that any versions of the SDK referenced by global.json is present on the machine. This approach avoids selecting a higher feature version of the SDK than you probably expect given the version specified in global.json