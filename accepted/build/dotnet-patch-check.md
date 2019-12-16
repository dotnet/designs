# Getting latest .NET Core Runtime patch information

## Goal

When patches are released for the .NET Core runtime, developers should not need to install an entirely new .NET Core SDK.  They should be able to install only the updated runtime as needed, and the existing SDK should have a way of discovering that new patches should be used for self-contained applications.

Eventually we hope to be able to stop producing new releases of the .NET Core SDK whenever there is a patch to the runtime.  This proposal is a step in that direction, however a lot more will be needed to enable that.

## Proposal overview

- Add a `dotnet` command which will check for the latest patches to the .NET Core runtime.  Naming is TBD, straw man for this proposal is `dotnet patch-check`.  The command will:
    - Update the latest patch versions to use for self-contained applications
    - Notify the user if the installed shared frameworks are not up-to-date, with a link to download the latest patch
- When building a self-contained app, generate a warning if `dotnet patch-check` has not been run within a specified period of time (7 days?), suggesting that the user run `dotnet patch-check` to make sure the self-contained app uses the latest patch
    - This should be configurable to either specify the time period or disable the warning altogether

## Latest patch information source

The `dotnet patch-check` command will need a source of truth to find information about latest patch versions.  This information is currently encoded in releases.json files on GitHub ([example](https://github.com/dotnet/core/blob/master/release-notes/3.0/releases.json)).  We could expose that information via a web service (or web site) that the `dotnet patch-check` command would send a request to.  Alternatively, that information could be included in a NuGet package, and the command could use NuGet to get the latest version of the package (likely via a latest-version `PackageDownload`).

We should also consider how `dotnet patch-check` works in scenarios where internet access is restricted or not available.  If we use a NuGet package, then that package can be included in an internal feed to support the command.  If we make a web request, then we may want to provide a way to specify a path to a releases.json file to use instead.

## Storing and consuming latest patch versions

The CLI stores some data in the `.dotnet` folder, by default under the user folder.  This is where the latest patch information should be stored, as well as the time that updates were last checked.

Currently, the latest patch versions are encoded in the Microsoft.NETCoreSdk.BundledVersions.props file, which is generated during the core-sdk build.  The SDK will need to be updated to read this information from the downloaded latest patch version information, falling back to default values packaged in the SDK if there is nothing in the `.dotnet` folder (or if the version from the `.dotnet` folder is older than the fallback information built in to the .NET Core SDK).