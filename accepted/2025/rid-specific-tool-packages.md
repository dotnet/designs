# RID-Specific .NET Tool Packages

**Owner** [Daniel Plaisted](https://github.com/dsplaisted)

## Summary

This proposal adds the ability for a .NET Tool to have separate packages for each supported OS and architecture.

In .NET, the combination of OS and architecture is represented by a Runtime Identifier (RID).  Today, .NET Tools support native assets, but the native assets for all of the supported Runtime Identifiers need to be included in the same package.  For tools with large native dependencies, or if the entire tool is native (for example via NativeAOT), this multiplies the package download size by the number of supported RIDs.

We would like to add support for RID-specific .NET Tool packages.  This would enable a tool to have a separate package for each supported RID, and when the tool is installed only the package for the correct RID would need to be downloaded.

### Goals

- Tools can create RID-specific packages
- Installing a tool with RID-specific packages is transparent but only downloads the package for the current RID

### Non-goals

- Allowing a tool to have assets from a mixture of packages, for example have architecture neutral assets in the primary package and native dependencies in RID-specific packages

## Tool manifests

.NET Tools have a tool manifest file named `DotnetToolSettings.xml`.  It is stored alongside the executable tool assets in the `tools\<TargetFramework>\<RuntimeIdentifier>` folder.  Currently for most or all tools, the `RuntimeIdentifier` is `any`.  Here is an example of a current manifest:

```xml
<DotNetCliTool Version="1">
  <Commands>
    <Command Name="dotnet-say" EntryPoint="dotnet-say.dll" Runner="dotnet" />
  </Commands>
</DotNetCliTool>
```

## Design

A tool with RID-specific packages will consist of a single primary package and RID-specific packages for each supported RID.  The primary package will include a tool manifest that lists the RIDs supported by the tool, and the package name and package version of the RID-specific tool package for each one.  The primary package will not include any tool implementation assets or shims.  The RID-specific packages will have the same layout and format as tool packages currently have.  The only difference will be that the NuGet package type will be set to a new `DotnetToolRidPackage` type, in order to prevent tool search results from being cluttered with RID-specific tool packages (the primary package is the one that should show up in the results).

The RID-specific tool packages should be named using the convention `<Toolname>.<RID>`.  For example, if the `dotnetsay` tool has RID-specific packages, they would be named `dotnetsay.win-x64`, `dotnetsay.linux-x64`, etc.

The tool manifest for the primary package would look like this:

```xml
<DotNetCliTool Version="1">
  <Commands>
    <Command Name="dotnet-say" />
  </Commands>
  <RuntimeIdentifierPackages>
    <RuntimeIdentifierPackage RuntimeIdentifier="win-x64" Id="dotnet-say.win-x64" Version="1.0.0" />
    <RuntimeIdentifierPackage RuntimeIdentifier="linux-x64" Id="dotnet-say.linux-x64" Version="1.0.0" />
  </RuntimeIdentifierPackages>
</DotNetCliTool>
```

Note that the Command has no EntryPoint or Runner, as that is defined in the RID-specific package.  The command name is still included because that may be useful for tooling.

## Entry points

Recall that the tool manifest file has a section that looks like this:

```xml
<Command Name="dotnet-say" EntryPoint="dotnet-say.dll" Runner="dotnet" />
```

Currently, the only supported value for `Runner` is `dotnet`, which means that the `EntryPoint` is a DLL that can be launched with the `dotnet` executable.  When a global tool is installed, the .NET SDK will create a version of the .NET apphost (called a shim) in the global tools folder.  This shim has a filename matching the tool command name so that the tool can be launched from the path.  The shim also has the relative path to the tool entry point embedded in it so that it can launch the correct tool.  Tools can also include their own shims so that they can be properly signed.

To support NativeAOT .NET apps (or non .NET apps), we should support an additional `executable` runner for executables that can be launched natively by the operating system.  On Unix-like operating systems, we can create a symlink in the global tools folder to the tool entry point executable.  On Windows, we will create a batch file that launches the entry point executable.  If this has drawbacks we might create a new type of shim/launcher.

## Tool manifest format version

The root element of the tool manifest has a Version attribute.  Currently this is set to 1.  When creating a tool manifest with a `RuntimeIdentifierPackages` element or a command Runner type of `executable`, we should set the Version to 2, so that prior .NET SDKs that don't support those features will error out gracefully when trying to install the tool.  If the tool manifest doesn't use these new features, the version should still be set to 1 so that the tool can be consumed by previous SDKs.

## Production

A tool project will be able to specify the runtime identifiers to create RID-specific packages for using the `ToolPackageRuntimeIdentifier` item.  For example:

```xml
<ItemGroup>
	<ToolPackageRuntimeIdentifier Include="win-x64"/>
	<ToolPackageRuntimeIdentifier Include="linux-x64"/>
</ItemGroup>
```

If `ToolPackageRuntimeIdentifier` is non-empty, then packing the project without a RuntimeIdentifier will create the primary package, and packing the project with a RuntimeIdentifier will create the corresponding RID-specific package.  If feasible, we could do an automatic inner pack of the ToolPackageRuntimeIdentifiers before creating the primary tool package.

The package name for the RID-specific packages will be `<ToolPackageName>.<RuntimeIdentifier>`.  If not specified, the version number of the RID-specific package will be the same as that of the primary package.  `Version` metadata on the `ToolPackageRuntimeIdentifier` item can be used to set the RID-specific package version.

Note that NativeAOT is not generally supported for target platforms other than the current platform.  So creating a NativeAOT .NET Tool will involve building the RID-specific packages on separate machines and building a primary package that refers to them.

Packing a project with `PackAsTool` set to true will pack the publish output.  However, for NativeAOT, the `_IsPublishing` property needs to be set to true.  This is normally set to true when invoking `dotnet publish`.  There is a similar `_IsPacking` property, so in the SDK targets we will set `_IsPublishing` to true if `_isPacking` and `PackAsTool` are set to true.  This means that `dotnet pack` will work correctly, however `dotnet build /t:Pack` will have similar problems that `dotnet build /t:Publish` currently has.

## Generalized support for RID-specific dependencies

A common piece of feedback has been that we should have a general way to express RID-specific dependencies that would also work for normal NuGet packages.  Some related issues:

- https://github.com/NuGet/Home/issues/10571
- https://github.com/NuGet/Home/issues/1660

It would be good to support RID-specific NuGet package dependencies.  However, I don't believe we should try to have a unified mechanism that applies both to normal NuGet packages referenced via PackageReference as well as .NET Tool packages.

Package dependencies can form an arbitrarily complex graph.  That graph is walked during NuGet restore, which needs to select a single version of each dependency in the case of "diamond dependencies."  Assets from all of the resolved packages are merged and typically included in the app that is being built.

These behaviors don't apply to .NET Tools.  We don't want to support an arbitrary graph or resolve which version to use based on different dependencies.  We don't want to merge the assets of multiple packages-- we want to be able to resolve a single package and run the tool directly from that package (which is what we do for local tools and will do for one-shot tools).