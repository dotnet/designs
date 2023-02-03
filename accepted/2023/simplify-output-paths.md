# Simplify .NET Output Paths

We'd like to make some improvements to project output paths for .NET 8.

Goals:

- Support output paths in a repo or solution-specific folder, rather than each project folder having its own output paths
- Provide simple and consistent output paths
- Avoid cases where one output path is nested inside of another one
- Avoid excessively deep directory heirarchies

## Motivation

Some developers would like to put all the output for their solution or repo [under a single folder](https://github.com/dotnet/sdk/issues/867).  This would make it easier to delete all of the output, as MSBuild-based clean only deletes output for a single configuration, and doesn't delete everything from the intermediate output folder, as that's where it keeps track of what was produced by the build and hence what should be deleted in a clean.  It would support building with [read only](https://github.com/dotnet/sdk/issues/887) [source trees](https://github.com/dotnet/designs/pull/281#discussion_r1072949097).  It could also make it easier to set up CI and deployment.

The current output paths for .NET projects are rather complicated and inconsistent.  The default output path for a .NET project includes folders for the `TargetFramework` and the `RuntimeIdentifier` (if it is set).  Additionally, the publish output goes in a `publish` subfolder of the output folder.  The resulting paths are as follows:

- `bin\<Configuration>\<TargetFramework>` - Build output with no `RuntimeIdentifier`
- `bin\<Configuration>\<TargetFramework>\<RuntimeIdentifier>` - Build output with `RuntimeIdentifier`
- `bin\<Configuration>\<TargetFramework>\publish` - Publish output with no `RuntimeIdentifier`
- `bin\<Configuration>\<TargetFramework>\<RuntimeIdentifier>\publish` - Publish output with `RuntimeIdentifier`

## Proposed behavior

We will introduce a new output path format, called the "artifacts" output format after the default folder name it will use.  By convention, the artifacts folder will be located at the solution or repo level rather within each project folder.

The recommended way to opt in to the new output path format will be to set the `UseArtifactsOutput` property to `true` in a `Directory.Build.props` file.

The artifacts folder will by default be named `.artifacts`.  The convention we will use to determine which folder the artifacts folder should go in will be:

- If a `Directory.Build.props` file was used in the build, put the artifacts folder in the same folder where `Directory.Build.props` was found, which will typically be at the root of a repo.  Since MSBuild already probes for `Directory.Build.props`, we should be able to use this folder for the artifacts with minimal perf impact
- Look for a folder with a `.sln` file in it, walking up the directory tree from the current project folder to the root.  When a `.sln` file is found, put the artifacts folder in that same directory.  We may need an additional intrinsic MSBuild function to do this, as the `GetDirectoryNameOfFileAbove` function doesn't support wildcards for the file name
- If neither a `Directory.Build.props` file nor a `.sln` file was found, then put the artifacts folder in the project folder.

To override the default artifacts folder path, the `ArtifactsPath` property can be used.  If `ArtifactsPath` is set, then `UseArtifactsOutput` will default to `true` and it won't be necessary to set both properties.  `ArtifactsPath` should normally be set in a `Directory.Build.props` file.  For example:

```xml
<Project>
    <PropertyGroup>
        <ArtifactsPath>$(MSBuildThisFileDirectory)\output</ArtifactsPath>
    </PropertyGroup>
</Project>
```

Under the artifacts folder will be nested folders for the type of the output, the project name, and the pivots, ie `<ArtifactsPath>\<Type of Output>\<Project Name>\<Pivots>`.

- Type of Output - This will be a value such as `bin`, `publish`, `intermediates`, or `package`.  Projects will be able to override this value, for example to separate shipping and non-shipping artifacts into different folders
  - `bin` will be the folder for the normal build output.  Projects can override this with `ArtifactsOutputName`
  - `publish` will be the folder for the publish output.  Projects can override this with `ArtifactsPublishOutputName`
  - `package` will be the folder for the package output, where .nupkg folders are placed. (Or should it be `packages`?  Feedback welcome.)  Projects can override this with `ArtifactsPackageOutputName`
  - `intermediates` will be the folder for the intermediate output.  (Or should we stick with `obj`?  Feedback welcome.)
- The Project Name.  This will ensure that each project has a separate output folder.  By default this will be `$(MSBuildProjectName)`, but projects can override this with the `ArtifactsProjectName` property.  If `ArtifactsPath` was not explicitly specified, and neither a `Directory.Build.props` nor a `.sln` file was found, then the artifacts folder will already be inside the project folder, and an additional Project Name folder will *not* be included in the output paths.  Additionally, for package output, this folder won't be included in the path, so that all the `.nupkg` files that are built can be in a single folder.
- Pivots - This is used to distinguish between builds of a project for different configurations, target frameworks, runtime identifiers, or other values.  It can be specified with `ArtifactsPivots`.  By default, the pivot will include:
  - The `Configuration` value (lower-cased, using the invariant culture)
  - The `TargetFramework` if the project is multi-targeted
  - The `RuntimeIdentifier`, if it was explicitly set (either in the project file or on the command line).  The `RuntimeIdentifier` won't be appended if it was automatically set by the SDK, for example because `SelfContained` was set

If multiple pivot elements are used, they will be joined by the underscore (`_`) character.  For example, a multi-targeted project with the RuntimeIdentifer specified could have a pivot of `debug_net8.0-windows10.0.19041.0_win-x64`. For the `package` output type, the pivots will only be the `Configuration`, and other values won't be included in the path

Some examples:

- `.artifacts\bin\debug` - The build output path for a simple project when you run `dotnet build`
- `.artifacts\intermediates\debug` - The intermediate output path for a simple project when you run `dotnet build`
- `.artifacts\bin\MyApp\debug_net8.0` - The build output path for the `net8.0` build of a multi-targeted project
- `.artifacts\publish\MyApp\release_linux-x64` - The publish path for a simple app when publishing for `linux-x64`
- `.artifacts\package\release` - The folder where the release .nupkg will be created for a project

## Customizing the output paths

The `Directory.Build.props` and `Directory.Build.targets` files can be used to [customize builds](https://learn.microsoft.com/visualstudio/msbuild/customize-your-build#directorybuildprops-and-directorybuildtargets) with logic that will apply to multiple projects.  However, they do not work very well to customize the output path.  This is because custom output path logic usually depends on the contents of the project file, for example the `TargetFramework` value or a custom property such as `IsShipping`.  `Directory.Build.props` is imported before the body of the project file, so it can't read those properties.  On the other hand, `Directory.Build.targets` is imported after common targets, so it is too late to correctly set the output paths.

There is already a `BeforeTargetFrameworkInferenceTargets` property which can be set to import a targets file after the project file is evaluated but before the `TargetFramework` is parsed.  Custom output path logic may depend on the target framework, and it is better to write this logic in terms of the properties which are parsed out of the `TargetFramework` such as `TargetFrameworkIdentifier` and `TargetFrameworkVersion`.  Because of this, we will add support for an `AfterTargetFrameworkInferenceTargets` property which can be used to inject a target file that will be imported after `TargetFramework` parsing.

As an example of how to use this, a project that wants to put the output for "Shipping" projects in a separate folder could set the `IsShipping` property to true in each shipping project, and put the following in `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <UseArtifactsOutput>true</UseArtifactsOutput>
    <AfterTargetFrameworkInferenceTargets>$(MSBuildThisFileDirectory)\Directory.AfterTargetFrameworkInference.targets</AfterTargetFrameworkInferenceTargets>
  </PropertyGroup>
</Project>
```

The `Directory.AfterTargetFrameworkInference.targets` would include the following:

```xml
<Project>
    <PropertyGroup Condition="'$(IsShipping)' == 'true'">
        <ArtifactsOutputName>Shipping\bin</ArtifactsOutputName>
        <ArtifactsPublishOutputName>Shipping\publish</ArtifactsPublishOutputName>
        <ArtifactsPackageOutputName>Shipping\package</ArtifactsPackageOutputName>
    </PropertyGroup>
</Project>
```

## Changing the default output path format

We are proposing this new output format because they represent an improvement over the existing behavior.  If they do represent an improvement, then we would like to change the default to use the new behavior, so that new projects and new users can get the improved behavior without having to know to opt in.  However, there are several obstacles to changing the default.

First of all, we don't want to affect existing projects that haven't opted in to the new behavior.  To do otherwise would be a massive breaking change, as many CI setups, build scripts, custom build logic, etc. all depend on knowing where the output of a project is and would break if it changes.

Often what we do in situations like this is to only make the new behavior the default for a new target framework.  For example, projects that hadn't explicitly opted in or out would only get the new behavior if they targeted .NET 8 or higher.  This would work for single-targeted projects, but if a project multi-targeted to .NET 7 and .NET 8, then it could be very confusing if the output for .NET 7 was in the traditional `bin` folder, but the output for .NET 8 was in an `.artifacts` folder that wasn't even in the project folder but was at the solution level.

Ideally in that case we might want to use the new output path format by default for all target frameworks if any of them are .NET 8 or greater.  Unfortunately this is not possible, as the output paths need to be determined during MSBuild evaluation, and at that point it's not possible to query the build of another target framework to see if it targets a given version of .NET or higher.

So for multi-targeted projects, we'd be left with these options:

- Default to the new output paths - too breaking
- Use different output path formats for different target frameworks - probably too confusing
- Default to the old output paths

Another issue with defaulting to the new output path format is that it can produce output paths that are longer, which would make it more likely for projects to run up against path length limitations.  .NET Maui projects in particular have hit this limitation in the past with nested Java builds in the intermediate output path, and has taken steps to shorten their paths.

## Considerations

### Using `.artifacts` for the folder name

The default `.artifacts` folder name was chosen for a few reasons:

- Folders starting with `.` are already ignored by the default item globs for .NET SDK projects.  This is important so that if a project switches back and forth between output formats, the output path (and especially the intermediate output path, where C# source code files are generated) won't be included in the globs.
- The `.` prefix sorts the folder separately from the rest of the source folders

There is also at least one drawback:

- Folders starting with `.` are hidden in some contexts (MacOS finder for example).  This will make it harder to find the output or run the app.

### Finding the output path from scripts

Today, the output path is likely hardcoded in many places that interact with MSBuild (build scripts, CI pipelines, etc.).  If those are going to need to be updated, it would be better if they could be updated in a way that makes them resilient to output path changes.  To do this, we should provide a way for a script to call in to MSBuild and get the output path value.  This will be a separate design proposal, right now we have it tracked with the following work item: https://github.com/dotnet/msbuild/issues/3911