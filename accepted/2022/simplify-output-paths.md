# Simplify .NET Output Paths

We'd like to simplify the output paths for .NET 8.

Currently, the default output path for a .NET project includes folders for the `TargetFramework` and the `RuntimeIdentifier` (if it is set).  Additionally, the publish output goes in a `publish` subfolder of the output folder.  The resulting paths are as follows:

- `bin\<Configuration>\<TargetFramework>` - Build output with no `RuntimeIdentifier`
- `bin\<Configuration>\<TargetFramework>\<RuntimeIdentifier>` - Build output with `RuntimeIdentifier`
- `bin\<Configuration>\<TargetFramework>\publish` - Publish output with no `RuntimeIdentifier`
- `bin\<Configuration>\<TargetFramework>\<RuntimeIdentifier>\publish` - Publish output with `RuntimeIdentifier`

This is rather complicated and inconsistent, which can make for a poor first impression of the .NET platform.

We'd like to try to improve the output path structure.  Desired qualities include:

- Simple and consistent
- Avoid cases where one output path is nested inside of another one
- Avoid excessively deep directory hierarchies
- Get rid of `obj` folder in project root

## Proposed behavior

The new output path format will consist of the following 3 nested folders:

- `artifacts` - All output will go under this folder
- Output Type - Such as `bin`, `publish`, `intermediates`, or `package`
- Pivots - This will at minimum be the `Configuration` (lower-cased), such as `debug` or `release`. Other pivots such as `TargetFramework` or `RuntimeIdentifier` may also be included, and the pivots will be joined by the underscore (`_`) character
  - `TargetFramework` will be included in the folder name if the project is multi-targeted (`TargetFrameworks` is non-empty), or if the `TargetFramework` property was set via the command line (ie is a global property)
  - `RuntimeIdentifier` will be included in the folder name if it was explicitly set (either in a project file or on the command line).  If it is set automatically by the SDK (for example because `SelfContained` was set), then the `RuntimeIdentifier` will not be included in the folder name
  - For the `package` output type, the pivots will only be the `Configuration`, and other values won't be included in the path

Some examples:

- `artifacts\bin\debug` - The build output path for a simple project when you run `dotnet build`
- `artifacts\intermediates\debug` - The intermediate output path for a simple project when you run `dotnet build`
- `artifacts\bin\debug_net8.0` - The build output path for the `net8.0` build of a multi-targeted project
- `artifacts\publish\release_linux-x64` - The publish path for a simple app when publishing for `linux-x64`
- `artifacts\package\release` - The folder where the release .nupkg will be created for a project

### Controlling the output path format

We would like the new output format to be used by default.  However, a variety of things may depend on the output path, such as scripts that copy the output of the build, or custom MSBuild logic that hard-codes the output path.  So it would be a breaking change to change the output path format for all projects.

One strategy we often use for situations like this is to make new behavior the default only when targeting the version of .NET that the behavior was introduced in or higher.  This prevents the new .NET SDK from breaking existing, unmodified projects, and ensures that the change in behavior will only be encountered when projects are modified to target a new `TargetFramework`.

The logic for determining which output path format will be used will (mostly) be as follows:

- If a project targets .NET 8 or higher, it will by default use the new output path format.  If it targets .NET 7 or lower, it will by default use the old output path format
- A project can explicitly choose which output path format to use by setting the `UseArtifactsOutput` property to `true` or `false` in a `Directory.Build.props` file.

### That pesky intermediate output path

Unfortunately, the base intermediate output path can't depend on the project's `TargetFramework`, or anything else in the project file.  The only place to set properties which affect the base intermediate output path is in a `Directory.Build.props` file.  This is because MSBuild imports "project extensions" of the form `<ProjectName>.*.props` and `<ProjectName>.*.targets` from the base intermediate output path, and the project extensions .props files are imported early on in evaluation, before the body of the project file is evaluated.

So, the way we will handle this is:

- If `UseArtifactsOutput` is set to `true` in a `Directory.Build.props` file, then the new output format will be used, including for the intermediate output path
- Otherwise, if `UseArtifactsOutput` is set to true later, either in the project file or in .NET SDK logic due to the `TargetFramework` being at least .NET 8, then the old output format will be used for the intermedate folder (so it will start with `obj\<Configuration>` by default), but the new `artifacts` format will be used for all other output.

### Mixed output path formats in multi-targeted builds

If a project is multi-targeted to `net7.0` and `net8.0` and doesn't specify the output path format, then different formats will be used for the output for the different target frameworks.  The .NET 7 build output will be in `bin\Debug\net7.0`, while the .NET 8 output will be in `artifacts\bin\debug_net8.0`.

To prevent the output from one target framework to be globbed as part of the inputs to another target framework, both the `bin` and `artifacts` folder will need to be excluded from the default item globs (via the `DefaultItemExcludes` property).  This means that even projects that target .NET 7 or lower could be impacted by breaking changes if they currently have source files or other assets in an `artifacts` folder.

Question: Could we have the outer build of a multi-targeted build query the inner builds to determine whether any of them target .NET 8 or higher, so we wouldn't have to have inconsistent output path formats?  Would this work with Visual Studio builds?

### Solution or repo level output paths

Many projects want to put all the output for the repo or the solution in a single folder.  We will support this with a new `RootArtifactsPath` property which can be set in a `Directory.Build.props file`.  If set, then we will use the following format for the output path:

`<RootArtifactsPath>\<OutputType>\<ProjectName>\<Pivots>`

If `RootArtifactsPath` is set, then it will not be necessary to separately set `UseArtifactsOutput`.

We will consider updating commands such as `dotnet build` and `dotnet publish` so that when used on a `.sln` file, the `--output` parameter sets the `RootArtifactsPath`.

## Considerations

### Can't we use `bin\Debug`?

Before .NET Core, .NET Framework projects generally put their output directly in `bin\Debug` or `bin\Release`.  It would be desirable if we could go back to putting the output in `bin\Debug`.  However, to do so we would need to do one of the following:

- Add more top-level project output folders (for example `pub/` for publish output next to `bin/` and `obj`)
- Have an inconsistent folder structure (for example `bin\publish` next to `bin\Debug`)

This is a classic "pick two of three things" situation, and we believe the least important of the three was trying to preserve `bin\<Configuration>` as an output path.

### NuGet pack logic

Currently, as part of the NuGet pack command, the `_WalkEachTargetPerFramework` target runs the `_GetBuildOutputFilesWithTfm` target for each `TargetFramework`.  This means in this inner build the `TargetFramework` will be a global property, even if there is only one.

This interferes with the logic where the output path depends on whether the `TargetFramework` is specified as a global property.  To fix this, the NuGet pack logic would need to change so that if there is only one target framework, it does not pass the `TargetFramework` global property in this case.

https://github.com/NuGet/Home/issues/12323