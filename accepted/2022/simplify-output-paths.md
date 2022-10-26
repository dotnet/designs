# Simplify .NET Output Paths

We'd like to simplify the output paths for .NET 8.

Currently, the default output path for a .NET project includes folders for the `TargetFramework` and the `RuntimeIdentifier` (if it is set).  Additionally, the publish output goes in a `publish` subfolder of the output folder.  The resulting paths are as follows:

- `bin\<Configuration>\<TargetFramework>` - Build output with no `RuntimeIdentifier`
- `bin\<Configuration>\<TargetFramework>\<RuntimeIdentifier>` - Build output with `RuntimeIdentifier`
- `bin\<Configuration>\<TargetFramework>\publish` - Publish output with no `RuntimeIdentifier`
- `bin\<Configuration>\<TargetFramework>\<RuntimeIdentifier>\publish` - Publish output with `RuntimeIdentifier`

This makes it harder to find the output of a project, and sometimes nests one type of output in another output folder.

## Proposed behavior

Projects targeting .NET 8 and higher will by default not include the `TargetFramework` in the output path unless the project is multi-targeted.  Similarly, the `RuntimeIdentifier` will not by default be part of the output path.  Finally, the publish output path will be `bin\publish` by default instead of a `publish` subfolder inside of the output path.

These would be the default output paths for .NET 8 projects:

- `bin\<Configuration>` - Build output path for single targeted project
- `bin\<Configuration>\<TargetFramework>` - Build output for multi-targeted project
- `bin\publish` - Publish output for single targeted project
- `bin\publish\<TargetFramework>` - Publish output for multi-targeted project

Note that the publish output paths would only apply when publishing the Release Configuration, which we plan to make the default configuration for `dotnet publish` in .NET 8.

## Implementation

There are currently properties that control whether the TargetFramework and RuntimeIdentifier should be included in the output path: `AppendTargetFrameworkToOutputPath` and `AppendRuntimeIdentifierToOutputPath`.  Both of these currently default to true.  We will change the default values as follows:

- `AppendTargetFrameworkToOutputPath` will default to true if the `TargetFrameworks` property is set (as multi-targeted projects still need different output paths for each TargetFramework), or the project is targeting .NET 7 or lower.  Otherwise it will default to false.
- `AppendRuntimeIdentifierToOutputPath` will default to true only when targeting .NET 7 or lower.
  - To be discussed: Should we also default it to true if `RuntimeIdentifiers` is set?

For publish, we would change the publish output path only when targeting .NET 8 or higher and when the Configuration is set to Release (which we [plan to make the default](https://github.com/dotnet/sdk/issues/27066) for .NET 8).  In that case we would set the publish output path to `$(BaseOutputPath)\publish` (by default `bin\publish`), and append the `TargetFramework` and `RuntimeIdentifier` to the path based on `AppendTargetFrameworkToOutputPath` and `AppendRuntimeIdentifierToOutputPath`.

## Considerations

### Breaking changes

These changes could break things such as scripts that copy the output of the build or custom MSBuild logic that hard-codes these paths.  Tieing these changes to the project TargetFramework ensures that these breaks will be encountered when the project is modified to target a new TargetFramework, not when updating to a new version of the .NET SDK.

### Why have different output paths at all?

It is worth considering why we have different output paths at all.  The advantage of having different output paths for some pivot (such as configuration, target framework, or RuntimeIdentifier) is that you can keep outputs for the different values of the pivot side-by-side, and that if you are switching back and forth between building them, the builds for a given pivot value can be incremental.

Therefore, the assumption with these changes is that it is not common to switch back and forth between different target frameworks or different runtime identifiers.  Projects where it is common to switch back and forth could still set `AppendTargetFrameworkToOutputPath` or `AppendRuntimeIdentifierToOutputPath` to true.

### Publish versus Configuration

Putting the publish output in the `bin\publish` folder may appear to conflate publish with a Configuration.  This, however, is intentional.  We'd like to make publish behave more like a configuration.

Some factors influencing this:

- It is rarely correct to publish with the Debug configuration, as publish is used to create artifacts that are deployed.
- Currently, there are operations that are only supported during publish, such as `PublishSingleFile`, `PublishTrimmed`, and `PublishAot`.  It would be more flexible to allow these to be specified for Build also.
- There's not a good way to set MSBuild properties differently for the Publish operation.  This makes it hard to implement properties such as PublishSingleFile and PublishRuntimeIdentifier, and means that those properties will work with `dotnet publish` but not `dotnet build /t:Publish`.  Developers may also want to condition properties on whether the Publish operation is being run.  On the other hand, conditioning properties based on Configuration is trivial.

We've been thinking about Publish versus Build for a while.  Here's some of that background:

- https://github.com/dotnet/sdk/issues/26446
- https://github.com/dotnet/sdk/issues/26247
- https://github.com/dotnet/core/issues/7566
- https://github.com/dotnet/docs/issues/30023
- https://gist.github.com/dsplaisted/f032de83be1dda7e14ca77f350100065
- https://github.com/dotnet/sdk/issues/15726

### Incremental publish

Originally, the .NET SDK did not support incremental publish.  This meant that if you published for one RuntimeIdentifier, and then published for another RuntimeIdentifier using the same publish path, the files from the first RuntimeIdentifier that should not have been included in the second publish would still be left in the folder.  Probably because of this, the RuntimeIdentifier is [always added to the publish output path](https://github.com/dotnet/sdk/blob/efef23ab729388ffb081731e5b1adbabc6e6b327/src/Tasks/Microsoft.NET.Build.Tasks/targets/Microsoft.NET.Sdk.BeforeCommon.targets#L122-L126) regardless of the value for `AppendRuntimeIdentifierToOutputPath`.  However, we now [support incremental publish](https://github.com/dotnet/sdk/pull/3957), so this isn't a concern anymore.

### Capitalization

Should the default publish output path be `bin\publish` or `bin\Publish`?  Uppercase matches the Configuration values of `Debug` and `Release`, which the publish folder will be a sibling of.  However, the current publish folder capitalization is lowercase.
