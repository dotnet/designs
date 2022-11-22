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

Projects targeting .NET 8 and higher will by default use a new output path format.  The output path will consist of the following 3 nested folders

- `bin` - All output (including intermediate output, will go under this folder)
- Output Type - Such as `build`, `publish`, `obj`, or `packages`
- Pivots - This will at minimum be the `Configuration`, such as `Debug` or `Release`. Other pivots such as `TargetFramework` or `RuntimeIdentifier` may also be included, and the pivots will be joined by the underscore (`_`) character
  - `TargetFramework` will be included in the folder name if the project is multi-targeted (`TargetFrameworks` is non-empty), or if the `TargetFramework` property was set via the command line (ie is a global property)
  - `RuntimeIdentifier` will be included in the folder name if it was explicitly set (either in a project file or on the command line).  If it is set automatically by the SDK (for example because `SelfContained` was set), then the `RuntimeIdentifier` will not be included in the folder name

Some examples:

- `bin\build\Debug` - The build output path for a simple project when you run `dotnet build`
- `bin\obj\Debug` - The intermediate output path for a simple project when you run `dotnet build`
- `bin\build\Debug_net8.0` - The build output path for the `net8.0` build of a multi-targeted project
- `bin\publish\Release_linux-x64` - The publish path for a simple app when publishing for `linux-x64`
- `bin\package\Release` - The folder where the release .nupkg will be created for a project

## Considerations

### Breaking changes

These changes could break things such as scripts that copy the output of the build or custom MSBuild logic that hard-codes these paths.  Tieing these changes to the project TargetFramework ensures that these breaks will be encountered when the project is modified to target a new TargetFramework, not when updating to a new version of the .NET SDK.

### Opting in or out of the new behavior

We need a way to explicitly opt in to or out of the new behavior.  This will let projects targeting prior versions of .NET opt in to the new folder structure, which is especially desirable for multi-targeted projects so that the inner builds have consistent output paths with each other.  It will also let projects that target .NET 8 use the old output path structure, for example if the new paths break scripts or CI setup.

We need to come up with a name for the property that controls this.  `UseNewOutputPathFormat` is probably not a good name.

### Can't we use `bin\Debug`?

Before .NET Core, .NET Framework projects generally put their output directly in `bin\Debug` or `bin\Release`.  It would be desirable if we could go back to putting the output in `bin\Debug`.  However, to do so we would need to do one of the following:

- Add more top-level project output folders (for example `pub/` for publish output next to `bin/` and `obj`)
- Have an inconsistent folder structure (for example `bin\publish` next to `bin\Debug`)

This is a classic "pick two of three things" situation, and we believe the least important of the three was trying to preserve `bin\<Configuration>` as an output path.
