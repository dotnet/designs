# Shared dependencies across applications

The .NET deployment model optionally shares the runtime across apps, but offers no sharing model for other libraries. When multiple applications share many of the same dependencies, each carries its own copies, inflating the overall deployment and requiring every app to be updated to pick up a dependency change. If that common set of libraries is owned and versioned together, it is a natural set to deploy once and share.

**Goals**
- Apps can reference the shared set at build time without deploying it into each app's output
- The shared set lives in a shared location on disk and is resolved at run time by apps that referenced it at build time
- A single deployment of the shared set is used by all the applications
- The shared set can be updated independently of the apps

**Non-goals**
- Extension of the shared framework concept (global install, name-based framework references, framework versioning) to third parties
- Machine-wide assembly store cache
- Deployment of the applications and their shared set(s)

## Proposed approach

### Runtime resolution

The host reads a `sharedDependencies` array in `runtimeconfig.json`. Each entry points at a shared set's `.deps.json`.

`App.runtimeconfig.json`:

```json
{
  "runtimeOptions": {
    "tfm": "net11.0",
    "framework": {
      "name": "Microsoft.NETCore.App",
      "version": "11.0.0"
    },
    "sharedDependencies": [
      "/opt/AppSuite.Shared/AppSuite.Shared.deps.json"
    ]
  }
}
```

- Each `sharedDependencies` entry is either an absolute path or relative path (based on the app's directory) to the shared set's `.deps.json`. Environment variables are not supported.
- The host merges each shared set's `deps.json` into the app's dependencies at startup. The shared set is treated as part of the app, so it resolves with the same priority as assets directly in the app's `deps.json` (that is, before frameworks). This is similar to an [additional `deps.json` via `--additional-deps`/`DOTNET_ADDITIONAL_DEPS`](https://github.com/dotnet/runtime/blob/main/docs/design/features/additional-deps.md).
- The assets in a shared set's `deps.json` are resolved relative to that `deps.json`'s folder (`/opt/AppSuite.Shared/` in the above example). The presence of `sharedDependencies` does not trigger file existence checks at startup. This differs from `--additional-deps`/`DOTNET_ADDITIONAL_DEPS`, which resolves an additional deps file's assets relative to the app directory and enables those file existence checks.

### Developer experience

An app references a shared set so it compiles against the libraries without deploying them, and the build records the deployed `.deps.json` in the app's `sharedDependencies`. The shared set can be a project or package that aggregates the libraries to be shared.

`App.csproj` (app):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net11.0</TargetFramework>
  </PropertyGroup>

  <!-- SharedDependencyLocation metadata (no copy-local + emits the entry) -->
  <ItemGroup>
    <ProjectReference Include="../AppSuite.Shared/AppSuite.Shared.csproj"
                      SharedDependencyLocation="/opt/AppSuite.Shared/" />
  </ItemGroup>

  <!-- SharedDependency item (emits the entry)
       ProjectReference with Private=false (no copy-local)
       or PackageReference with HostProvided=true (compile-only asset) -->
  <ItemGroup>
    <ProjectReference Include="../AppSuite.Utilities/AppSuite.Utilities.csproj" Private="false" />
    <SharedDependency Include="/opt/AppSuite.Utilities/AppSuite.Utilities.deps.json" />

    <PackageReference Include="AppSuite.Extras" Version="1.0.0" HostProvided="true" />
    <SharedDependency Include="/opt/AppSuite.Extras/AppSuite.Extras.deps.json" />
  </ItemGroup>
</Project>
```

`AppSuite.Shared.csproj` (shared set):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Serilog" Version="4.0.0" />
    <ProjectReference Include="../AppSuite.Core/AppSuite.Core.csproj" />
    <!-- Other libraries to be shared -->
  </ItemGroup>
</Project>
```

- `SharedDependencyLocation` metadata on the reference turns copy-local off and emits the `sharedDependencies` entry (`<SharedDependencyLocation>/<depsFileName>`).
- `SharedDependency` item points at a deployed `.deps.json` and emits the entry.
- Publishing the shared set produces a layout with the dependency assemblies and a `deps.json`. That layout is deployed to the shared location and the `deps.json` is pointed at by `sharedDependencies`.

This relies on existing or future functionality for marking a reference as external — compiled against but not deployed with the app. `Private="false"` (copy-local off) on a project reference exists today. `HostProvided="true"` on a package reference would come from a future NuGet feature for compile-only assets and audit ownership transfer to whoever deploys the set. That NuGet design is still in progress and not finalized. Support for both need not land together — project references alone are already valuable.

## Related

- [dotnet/runtime#53834](https://github.com/dotnet/runtime/issues/53834) — Support deploying multiple exes as a single self-contained set
- [dotnet/runtime#71282](https://github.com/dotnet/runtime/issues/71282) — Locally shared deployment
- [`--additional-deps`/`DOTNET_ADDITIONAL_DEPS`](https://github.com/dotnet/runtime/blob/main/docs/design/features/additional-deps.md) — existing support for additional deps files via command line or environment variable
- Not directly related, but in the space of configuring how dependencies can be laid out and found
    - [Configure .NET install search behavior](https://learn.microsoft.com/dotnet/core/deploying#configure-net-install-search-behavior) — existing configuration for how the apphost finds the .NET install
    - [dotnet/sdk#48406](https://github.com/dotnet/sdk/issues/48406) — `localPath` in `deps.json` is used for asset resolution and can be customized via `DestinationSubDirectory`: [dotnet/runtime#117682](https://github.com/dotnet/runtime/issues/117682), [dotnet/runtime#118297](https://github.com/dotnet/runtime/pull/118297), [dotnet/sdk#50120](https://github.com/dotnet/sdk/pull/50120)