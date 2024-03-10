# Sign CLI Signature Provider Plugins

**Owners** [Damon Tivel](https://github.com/dtivel) | [Claire Novotny](https://github.com/clairernovotny)

Recent CAB Forum updates to baseline requirements<sup>[1](#r1)</sup> strengthened private key storage requirements for publicly trusted code signing certificates.  While older, less secure storage options (e.g.:  [PKCS #12 & PFX](https://wikipedia.org/wiki/PKCS_12)) became obsolete, more secure options (e.g.:  [HSM](https://wikipedia.org/wiki/Hardware_security_module)) became standard.  Many existing code signing tools do not support the new standard.

[Sign CLI](https://github.com/dotnet/sign) already supports the new standard with [Azure Key Vault](https://learn.microsoft.com/azure/key-vault/general/overview#securely-store-secrets-and-keys) digest signing.  However, Sign CLI does not support other cloud providers, code signing services, or HSM tokens.  For that, Sign CLI needs a formal abstraction for signature providers and a signature provider plugin model that enables users to install the appropriate plugin for their situation.

Note that a signature provider plugin is agnostic of signature format (e.g.:  Authenticode, VSIX, NuGet, etc.).  A plugin accepts a digest and provides a raw signature which Sign CLI embeds in the appropriate signature format.

There is nothing in this proposed plugin model that precludes creation of a plugin that enables signing with a PFX file, and such a plugin might be welcome to a subset of users.  However, given the relative lack of support in existing signing tools for more secure private key storage options, the primary driver for this proposal is the need to support CAB Forum's more secure key storage requirements.

## Scenarios and User Experience

It is assumed that Sign CLI has already been installed (e.g.:  [`dotnet tool install --global sign --version 0.9.1-beta.23530.1`](https://www.nuget.org/packages/sign/0.9.1-beta.23530.1)).

_All plugin names and examples below are fictitious and for illustration purposes only.  Also for illustration purposes, assume that Sign CLI's existing Azure Key Vault support has already been moved out of Sign CLI itself into a separate plugin._

### Sign artifacts using Azure Key Vault

First, the Azure Key Vault plugin must be installed.  The following command would download and install the latest version of the plugin.

```text
sign plugin install Microsoft.Azure.KeyVault.Sign
```

Executing `sign code -?` will show the new available command:

```text
...
Commands:
  azure-key-vault <file(s)>  Use Azure Key Vault.
```

Similarly, executing `sign code azure-key-vault -?` will show help for the new command and its options.

```text
Description:
  Use Azure Key Vault.

Usage:
  sign code azure-key-vault <file(s)> [options]

Arguments:
  <file(s)>  File(s) to sign.

Options:
  -kvc, --azure-key-vault-certificate                    Name of the certificate in Azure Key Vault.
  <azure-key-vault-certificate> (REQUIRED)
  -kvi, --azure-key-vault-client-id                      Client ID to authenticate to Azure Key Vault.
  <azure-key-vault-client-id>
  -kvs, --azure-key-vault-client-secret                  Client secret to authenticate to Azure Key Vault.
  <azure-key-vault-client-secret>
  -kvm, --azure-key-vault-managed-identity               Managed identity to authenticate to Azure Key Vault.
  -kvt, --azure-key-vault-tenant-id                      Tenant ID to authenticate to Azure Key Vault.
  <azure-key-vault-tenant-id>
  -kvu, --azure-key-vault-url <azure-key-vault-url>      URL to an Azure Key Vault.
  -an, --application-name <application-name>             Application name (ClickOnce).
  -d, --description <description> (REQUIRED)             Description of the signing certificate.
  -u, --description-url <description-url> (REQUIRED)     Description URL of the signing certificate.
  -b, --base-directory <base-directory>                  Base directory for files. Overrides the current working
                                                         directory. [default: F:\git\sign]
  -o, --output <output>                                  Output file or directory. If omitted, input files will be
                                                         overwritten.
  -pn, --publisher-name <publisher-name>                 Publisher name (ClickOnce).
  -fl, --file-list <file-list>                           Path to file containing paths of files to sign within an
                                                         archive.
  -fd, --file-digest <file-digest>                       Digest algorithm to hash files with. Allowed values are
                                                         'sha256', 'sha384', and 'sha512'. [default: SHA256]
  -t, --timestamp-url <timestamp-url>                    RFC 3161 timestamp server URL. [default:
                                                         http://timestamp.acs.microsoft.com/]
  -td, --timestamp-digest <timestamp-digest>             Digest algorithm for the RFC 3161 timestamp server. Allowed
                                                         values are sha256, sha384, and sha512. [default: SHA256]
  -m, --max-concurrency <max-concurrency>                Maximum concurrency. [default: 4]
  -v, --verbosity                                        Sets the verbosity level. Allowed values are 'none',
  <Critical|Debug|Error|Information|None|Trace|Warning>  'critical', 'error', 'warning', 'information', 'debug', and
                                                         'trace'. [default: Warning]
  -?, -h, --help                                         Show help and usage information
```

The new command can then be used to sign artifacts.

```text
sign code azure-key-vault -kvu https://my.vault.azure.test/ -kvc MyCertificate -kvm -d Description -u http://description.test -b C:\ClassLibrary1\ClassLibrary1\bin\Debug\net8.0 ClassLibrary1.dll
```

### Sign artifacts using Windows certificate store

First, the plugin must be installed.  The following command would download and install the latest version of the plugin.

```text
sign plugin install Microsoft.Windows.CertificateStore.Sign
```

The new command can then be used to sign artifacts.

```text
sign code certificate-store --store-location CurrentUser --store-name My --sha1fingerprint da39a3ee5e6b4b0d3255bfef95601890afd80709 -d Description -u http://description.test -b C:\ClassLibrary1\ClassLibrary1\bin\Debug\net8.0 ClassLibrary1.dll
```

## Requirements

### Goals

* Create a plugin model that enables pluggable signature providers.  A signature provider plugin will offer an alternate implementation of [`System.Security.Cryptography.AsymmetricAlgorithm`](https://learn.microsoft.com/dotnet/api/system.security.cryptography.asymmetricalgorithm) and the [`System.Security.Cryptography.X509Certificates.X509Certificate2`](https://learn.microsoft.com/dotnet/api/system.security.cryptography.x509certificates.x509certificate2), if available, corresponding to the private key.  Note that while there may be valid scenarios for signing with a raw asymmetric key pair, some signing operations require a certificate and will fail if a certificate is not available.
* Make Sign CLI plugin-neutral.  While Sign CLI may install some core plugins (TBD), most plugins should be installed separately from Sign CLI itself, and Sign CLI's only interactions with any plugin should be through this plugin model.
* Enable Sign CLI and plugins to version and release independently.

### Non-Goals

* Enable signature formats that require a certificate to sign without a certificate.
* Enable signature formats to support asymmetric algorithms they do not already support.
* Create a distribution channel for plugins.  Sign CLI is a .NET tool and is [available on https://nuget.org](https://www.nuget.org/packages/sign/).  Plugin packages can be published to any NuGet feed, including <https://nuget.org>.
* Create a dynamic discovery mechanism for plugins.  Initially, we'll probably have a web page that lists common plugins and where to get them.
* Manage (list, update, uninstall) installed plugins.
* Add support for other signature algorithms (e.g.:  ECDSA).
* Enable localizability of plugin command and option descriptions.

## Design

### High-level approach

1. Create and publish a new _interfaces-only_ NuGet package that defines plugin-specific interfaces to be implemented by plugins.
1. Implement the [dependency inversion](https://wikipedia.org/wiki/Dependency_inversion_principle) pattern by having Sign CLI and plugins reference the interfaces package.
1. Move Azure Key Vault-specific implementations currently in Sign CLI into an Azure Key Vault-specific plugin.
1. Augment Sign CLI commands at runtime with contributions from installed plugins (like [these options](https://github.com/dotnet/sign/blob/ef0e6b3ef8281dff1d62cea34445bd88fc3e6714/src/Sign.Cli/AzureKeyVaultCommand.cs#L25-L31) for Azure Key Vault).
1. Enable Sign CLI to install new plugins and discover locally installed plugins.

This design roughly follows [.NET's existing plugin model](https://learn.microsoft.com/dotnet/core/tutorials/creating-app-with-plugin-support).

### Interfaces package

We will create a new .NET assembly that contains only public interfaces to be implemented by plugins.  Sign CLI will implement a new command for a plugin that loads and interacts with the plugin implementation entirely by interfaces defined in the interfaces assembly.  This approach will enable Sign CLI and plugins to rev their implementations without either having any extraneous compile-time or runtime dependencies.

Proposed interfaces:

```C#
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Sign.Plugins.SignatureProvider.Interfaces
{
    /// <summary>
    /// This is the core plugin interface.  Plugins conforming with this specification must implement this interface.
    /// </summary>
    public interface IPlugin
    {
        Task InitializeAsync(
            IReadOnlyDictionary<string, string?> arguments,
            ServiceProvider serviceProvider,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// This is the core interface for signature provider plugins.  Plugins conforming with this specification must
    /// implement this interface.
    /// </summary>
    /// <remarks>
    /// The plugin host will try casting a <see cref="IPlugin" /> instance to this interface to determine if the
    /// plugin is a signature provider.
    /// </remarks>
    public interface ISignatureProvider
    {
        Task<AsymmetricAlgorithm> GetAsymmetricAlgorithmAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// This is an optional interface for a signature provider plugin.
    /// If the asymmetric key pair returned by <see cref="ISignatureProvider" /> has an associated X.509 certificate,
    /// the plugin should also implement this interface.
    /// <summary>
    /// <remarks>
    /// The plugin host will try casting a <see cref="ISignatureProvider" /> instance to this interface to determine
    /// if the plugin is a certificate provider.  A plugin should not implement this interface unless it can provide
    /// a certificate.  Do not implement this interface and throw <see cref="NotImplementedException" /> to indicate
    /// non-support.
    ///
    /// Some signature formatters work with raw asymmetric key pairs without a certificate.  To support only these
    /// signature formatters, a plugin need not implement this interface.
    ///
    /// Some signature formatters (like Authenticode) do require a certificate and will fail without a certificate.
    /// To support these signature formatters, the plugin should implement this interface.  Signature formatters may
    /// fail if they require a certificate but a signature provider plugin does not implement this interface.
    /// </remarks>
    public interface ICertificateProvider
    {
        Task<X509Certificate2> GetCertificateAsync(CancellationToken cancellationToken);
    }
}
```

Sign CLI will pass all plugin arguments as a read-only dictionary of name-value pairs.  The plugin is responsible for argument parsing and validation for all options defined by the plugin.

The new interfaces assembly will be packaged and published to <https://nuget.org>, similar to [NuGetRecommender's contracts-only package](https://www.nuget.org/packages/Microsoft.DataAI.NuGetRecommender.Contracts).  The Sign CLI team will manage the source code repository for this package and publish the package to <https://nuget.org>.

The interfaces package itself can have package dependencies; however, because Sign CLI and all plugins would inherit new interfaces package dependencies, we should exercise restraint and caution before adding new dependencies.  An example of a package dependency worth having is [`Microsoft.Extensions.Logging.Abstractions`](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Abstractions).  Sign CLI uses [`ILogger`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.logging.ilogger) ubiquitously, it makes sense for plugins to write to a shared logger.  From the provided `System.IServiceProvider` argument, plugins can request an `ILogger` instance for logging.

#### Package versioning

The interfaces package version will follow strict [SemVer 2.0.0](https://semver.org/spec/v2.0.0.html) versioning rules.  It is expected that no interfaces package version will introduce a breaking change over any previous version.  Because the interfaces package is intended to provide stable abstractions to both Sign CLI and plugins, every package version will be fully backwards compatible.  Therefore, it is expected that we will only release packages in the version range [1.0.0, 2.0.0).  If a breaking change is warranted, versions >= 2.0.0 are a possibility.

It is intended that we will only publish release versions of the interfaces package.  If, for any reason, we decide to publish prerelease versions later, it should still be assumed that official Sign CLI releases (even prerelease versions) will only reference release versions of the interfaces package.  For any given official Sign CLI release, Sign CLI should reference the latest release version of the interfaces package at that point in time.

#### Interface versioning

Interfaces defined in the interfaces package should be considered permanent and immutable.  New interfaces can be added, but existing interfaces should not be modified or removed as long as Sign CLI will load plugins that implement those existing interfaces.

Versioning strategies for plugin interfaces are out of scope for this specification, but it is expected that all interfaces for every supported version of the plugin will be available in the interfaces package to enable Sign CLI users to install not only the latest version but previous, supported versions as well.

> Note:  If a plugin author wanted to remove support for older versions of a plugin, we could achieve that by specifying a minimum version of the plugin package in Sign CLI.  Older versions would be ignored, and plugin interfaces for those older versions could safely be removed from the interfaces package, provided that they are no longer needed.  Then, it would be possible for a plugin author to remove support for older versions and modify existing interfaces (vs. add new interfaces) in one step.  This remains true to the spirit of earlier guidance, that the interfaces package should preserve interfaces for all _supported_ plugin versions.  Enabling plugin authors to drop support for older versions of a plugin is out of scope for this specification.

### Plugins

A Sign CLI signature provider plugin:

* extends Sign CLI functionality
* contains a [`plugin.json`](#plugin-json-file) file in its root directory
* contains implementations for plugin interfaces defined in the interfaces package
* internalizes all necessary dependencies, both direct and indirect, not provided by the .NET runtime or the plugin host (Sign CLI)

#### Creating a plugin

1. Create a .NET class library project that targets the same runtime as Sign CLI.
1. Add a package reference to the latest version of the interfaces package.  In the plugin's project file, update the package reference to have `PrivateAssets="all"` and `ExcludeAssets="runtime"` to exclude the interfaces package dependency and its runtime assets from the plugin's package.

   ```XML
   <ItemGroup>
       <PackageReference Include="Sign.Plugins.SignatureProvider.Interfaces" Version="1.0.0" PrivateAssets="all" ExcludeAssets="runtime" />
   </ItemGroup>
   ```

1. Add all other necessary package references.  In the plugin's project file, update all package references to have `PrivateAssets="all"` to exclude package dependencies from the plugin's package.  Example:

   ```XML
   <ItemGroup>
       <PackageReference Include="Azure.Identity" Version="1.8.2" PrivateAssets="all" />
       <PackageReference Include="Azure.Security.KeyVault.Certificates" Version="4.4.0" PrivateAssets="all" />
       <PackageReference Include="Azure.Security.KeyVault.Keys" Version="4.2.0" PrivateAssets="all" />
       <PackageReference Include="RSAKeyVaultProvider" Version="2.1.1" PrivateAssets="all" />
   </ItemGroup>
   ```

1. Add public implementations for relevant interfaces defined in the interfaces package.
1. Add a [`plugin.json`](#plugin-json-file) file to the project.
1. Update the plugin's project file to create a NuGet package.

   ```XML
   <PropertyGroup>
       <!-- Enable NuGet package creation. -->
       <IsPackable>true</IsPackable>

       <!--
       Be sure to add other mandatory and optional properties for creating a NuGet package.
       See https://learn.microsoft.com/nuget/reference/msbuild-targets#pack-target.
       -->

       <!-- Enable rolling forward to a later runtime. -->
       <!-- See https://learn.microsoft.com/dotnet/core/project-sdk/msbuild-props#rollforward -->
       <RollForward>LatestMajor</RollForward>

       <!-- Copy runtime dependencies to the build output directory. -->
       <!-- https://learn.microsoft.com/dotnet/core/project-sdk/msbuild-props#enabledynamicloading -->
       <EnableDynamicLoading>true</EnableDynamicLoading>

       <!-- All private dependencies must be internalized.  Enable Sign CLI to load private dependencies using the plugin's deps.json file. -->
       <GenerateDependencyFile>true</GenerateDependencyFile>
       <TargetsForTfmSpecificBuildOutput>$(TargetsForTfmSpecificBuildOutput);CopyProjectReferencesToPackage</TargetsForTfmSpecificBuildOutput>
   </PropertyGroup>

   <ItemGroup>
       <!-- This adds plugin.json to the root directory of the plugin's package. -->
       <Content Include="plugin.json">
           <Pack>true</Pack>
           <!-- An empty value means the root directory. -->
           <PackagePath></PackagePath>
       </Content>
   </ItemGroup>

   <!-- This target copies runtime dependencies to the build output directory. -->
   <Target Name="CopyProjectReferencesToPackage" DependsOnTargets="ResolveReferences">
       <ItemGroup>
           <BuildOutputInPackage
               Include="@(ReferenceCopyLocalPaths)"
               TargetPath="%(ReferenceCopyLocalPaths.DestinationSubPath)" />
       </ItemGroup>
   </Target>

   <!-- This target adds the generated deps.json file to the build output directory. -->
   <Target Name="AddBuildDependencyFileToBuiltProjectOutputGroupOutput"
           BeforeTargets="BuiltProjectOutputGroup"
           Condition=" '$(GenerateDependencyFile)' == 'true'">

       <ItemGroup>
           <BuiltProjectOutputGroupOutput
               Include="$(ProjectDepsFilePath)"
               TargetPath="$(ProjectDepsFileName)"
               FinalOutputPath="$(ProjectDepsFilePath)" />
       </ItemGroup>
   </Target>
   ```

#### <a name="plugin-json-file"></a>The `plugin.json` file

Sign CLI needs to load and execute plugins.  The general problem is that Sign CLI needs to know which assemblies in a plugin to load, which types to instantiate, how to initialize those objects, and so forth.  To simplify matters, plugins will embed this information in a `plugin.json` JSON file in their package's root directory.  The file should include the following properties:

* `name`: The plugin's command name (e.g.:  `azure-key-vault` in `sign code azure-key-vault`).
* `description`: The plugin's command descripton, to be displayed in command help.
* `entryPoints`: Information for plugin instantiation.  The value is an object of key-value pairs:
  * `<tfm>`: The target framework moniker (TFM) (e.g.:  `net6.0`, `net7.0`, `net8.0`) that the entry point targets.
    * `filePath`: The full file path within the package, relative to the package's root directory, for the assembly which contains a public implementation of an interface defined in the interfaces package.
      * The path must be in its simplest form, without `..` or `.` directories.
      * The directory separator must be `/`.
      * The path must not have a leading slash `/`.
      * The path must be case-sensitive.
    * `implementationTypeName`: The fully qualified type name of the public type implementing the public interface defined in the interfaces package.
    * `interfaceTypeName`: The fully qualified type name of the public type in the interfaces package that `implementationTypeName` implements.
* `parameters`: The plugin's command options.
  * `name`: The option name.  Not displayed.
  * `description`: The option's description.  Displayed in command help.
  * `aliases`: The option's names.  Should include both long and short forms, (e.g.:  `--azure-key-vault-managed-identity` and `-kvm`, respectively).  A user will type these option names.
  * `dataType`: The option's data type.  The default is `String`, and all `String` options require an explicit value.  When this value is `Boolean`, it provides a hint to Sign CLI to create a switch option that supports both explicit value (e.g.:  `--global true`) and implicit value syntaxes (e.g.:  `--global`).
  * `defaultValue`: The option's default value.  Only used if `isRequired` is `false`; otherwise, this is ignored.
  * `isRequired`: Whether the option is required or not.

Parameter | Required | JSON Type | Default | Possible Values
-- | -- | -- | -- | --
`name` | yes | string | N/A | N/A
`description` | yes | string | N/A | N/A
`aliases` | yes | array of strings | N/A | N/A
`dataType` | no | string | `Text` | `Text`, `Boolean`
`defaultValue` | no | string | N/A | N/A
`isRequired` | no | `true`/`false` | `false` | `true`, `false`

Example:

```JSON
{
  "name": "azure-key-vault",
  "description": "Use Azure Key Vault.",
  "entryPoints": {
    "net6.0": {
        "filePath": "lib/net6.0/Microsoft.Azure.KeyVault.Sign.dll",
        "implementationTypeName": "Microsoft.Azure.KeyVault.Sign.Plugin",
        "interfaceTypeName": "Sign.Plugins.SignatureProvider.Interfaces.IPlugin"
    },
    "net8.0": {
        "filePath": "lib/net8.0/Microsoft.Azure.KeyVault.Sign.dll",
        "implementationTypeName": "Microsoft.Azure.KeyVault.Sign.Plugin",
        "interfaceTypeName": "Sign.Plugins.SignatureProvider.Interfaces.IPlugin"
    }
  },
  "parameters": [
    {
      "name": "certificate-name",
      "description": "Name of the certificate in Azure Key Vault.",
      "aliases": [ "-kvc", "--azure-key-vault-certificate" ],
      "isRequired": true
    },
    {
      "name": "client-id",
      "description": "Client ID to authenticate to Azure Key Vault.",
      "aliases": [ "-kvi", "--azure-key-vault-client-id" ]
    },
    {
      "name": "client-secret",
      "description": "Client secret to authenticate to Azure Key Vault.",
      "aliases": [ "-kvs", "--azure-key-vault-client-secret" ]
    },
    {
      "name": "managed-identity",
      "description": "Managed identity to authenticate to Azure Key Vault.",
      "aliases": [ "-kvm", "--azure-key-vault-managed-identity" ],
      "dataType": "Boolean",
      "defaultValue": "false"
    },
    {
      "name": "tenant-id",
      "description": "Tenant ID to authenticate to Azure Key Vault.",
      "aliases": [ "-kvt", "--azure-key-vault-tenant-id" ]
    },
    {
      "name": "url",
      "description": "URL to an Azure Key Vault.",
      "aliases": [ "-kvu", "--azure-key-vault-url" ]
    }
  ]
}
```

This design roughly borrows from [.NET's templating](https://github.com/dotnet/templating/blob/f8aec1818bd9ae82a8849bfe2138e4a76fed1da1/docs/Reference-for-template.json.md#parameter-symbol).

#### Plugin dependencies

Although a plugin will install as a NuGet package, the package should not have any package dependencies.  A plugin package should include all necessary dependencies except what will be provided by the .NET runtime and Sign CLI.  A plugin must not require Sign CLI or the interfaces package to depend on a package or assembly outside of what the .NET runtime already has.  Sign CLI will not resolve runtime dependencies through declared package dependencies.  The motivation here is to simplify Sign CLI's responsibility of loading and executing plugins.  It is the plugin author's responsibility to satisfy all runtime dependencies.

For dependencies in common to both Sign CLI and plugins, Sign CLI should dictate the dependency version, which usually should be the latest release version.  If a plugin depends on a later version than what Sign CLI depends on, the plugin may fail to load.

#### Plugin installation location

By default, plugin packages will install to the directory indicated by [`Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)`](https://learn.microsoft.com/dotnet/api/system.environment.specialfolder#fields).

Example (where `%LOCALAPPDATA%` is `C:\Users\dtivel\AppData\Local`):  `C:\Users\dtivel\AppData\Local\Sign\Plugins`

This default location could be overridden with an environment variable or CLI option.

The directory structure for the `Plugins` directory will contain one subdirectory for each lower-cased plugin package ID.  Each plugin package ID directory will contain a subdirectory for each lower-cased plugin package version.  Each version subdirectory will contain the extracted contents of the corresponding plugin package.  Example:

```text
Plugins
├─microsoft.azure.keyvault.sign
│ ├─0.9.1-beta.23274.1
│ │ └─<package contents>
│ └─0.9.1-beta.23530.1
│   └─<package contents>
└─<another plugin package ID>
  └─<package version>
    └─<package contents>
```

To identify installed plugins, Sign CLI will simply look in this directory for packages and use the latest version (release or prerelease) using SemVer 2.0.0 versioning rules.

Sign CLI will not maintain any installation state.  How a plugin was installed --- using NuGet client tools, Sign CLI, manual package extraction, or some other method --- is immaterial.  The presence of an extracted package in the directory means it has been installed.

#### Plugin instantiation

A plugin's command in Sign CLI will:

1. Read a plugin's `plugin.json` file.
1. Find the plugin's entry point that best matches the host (Sign CLI) not the runtime and deviate only if it's a sound fallback.  Examples:
    1. If a plugin contains net6.0, net7.0, and net8.0 assemblies and Sign CLI's net7.0 assemblies are loaded by the .NET 8 runtime, Sign CLI should load the plugin's net7.0 assemblies.
    1. If a plugin only had net6.0 and net8.0 assemblies, Sign CLI should load the net6.0 assemblies or report that plugin isn't available for the current runtime.
1. Load the assembly at the entry point's `filePath` location.
1. Create an instance of the type `implementationTypeName`.
1. Cast the instance to an interface defined in the interfaces package with type name `interfaceTypeName`.

As part of this process, Sign CLI will use [`System.Runtime.Loader.AssemblyLoadContext`](https://learn.microsoft.com/dotnet/api/system.runtime.loader.assemblyloadcontext) and [`System.Runtime.Loader.AssemblyDependencyResolver`](https://learn.microsoft.com/dotnet/api/system.runtime.loader.assemblydependencyresolver) to load a plugin assembly and its dependencies strictly from the directory cone of the plugin's entry point assembly.  For example, if the plugin's entry point is `lib/net6.0/Microsoft.Azure.KeyVault.Sign.dll`, then Sign CLI will attempt to resolve assemblies under `lib/net6.0`.

Sign CLI should log:

* high-level plugin loading information at the [`Information`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.logging.loglevel?view=dotnet-plat-ext-7.0#fields) log level.
* assembly loading details at `Debug` / `Trace` log levels
* errors at the `Error` log level

### Sign CLI commands

New commands will be added to Sign CLI to manage plugins.

#### `sign plugin <install>`

The `sign plugin` command will expose subcommands for managing plugins.

##### `sign plugin install <PluginPackageId> [--version <Version>]`

This command will install the latest release version of the plugin package identified by `PluginPackageId` using existing package sources and NuGet's default NuGet.config lookup order.  If the latest version is already installed, the command will no-op.

Example:

```text
sign plugin install Microsoft.Azure.KeyVault.Sign
```

Using `--version <Version>` will install the specified version.  If the specified version is already installed, the command will no-op.

Example:

```text
sign plugin install Microsoft.Azure.KeyVault.Sign --version 1.0.0
```

### Considerations

1. Sign CLI should probably move to using a [lock file](https://devblogs.microsoft.com/nuget/enable-repeatable-package-restores-using-a-lock-file/) to increase transparency in dependency versions and to ensure deterministic builds.

1. Because a plugin package isolates all private dependencies from Sign CLI, a plugin package author is responsible for servicing the plugin package with updates for the plugin and any of its dependencies.

1. Currently, Sign CLI depends on the [`NuGetKeyVaultSignTool`](https://www.nuget.org/packages/NuGetKeyVaultSignTool.Core/3.2.3) package for signing NuGet packages with Azure Key Vault.  Under this proposed specification, Sign CLI must be cloud-provider agnostic.  This dependency should simply take any `AsymmetricAlgorithm` implementation and remove [`RSAKeyVaultProvider`](https://www.nuget.org/packages/RSAKeyVaultProvider/2.1.1) and [`Azure.Security.KeyVault.Certificates`](https://www.nuget.org/packages/Azure.Security.KeyVault.Certificates/4.2.0) package dependencies.

1. Some asymmetric algorithms require algorithm-specific options.  For example, `System.Security.Cryptography.RSA` may require RSA signature padding; every [`SignData(...)` overload](https://learn.microsoft.com/dotnet/api/system.security.cryptography.rsa.signdata?#overloads) requires a [`System.Security.Cryptography.RSASignaturePadding`](https://learn.microsoft.com/dotnet/api/system.security.cryptography.rsasignaturepadding) argument to indicate either `Pkcs1` (PKCS #1 v1.5) or `Pss` signature padding mode.

   Because the caller --- a signature formatter --- passes the `RSASignaturePadding` argument, it an RSA signature provider plugin should be able to support both options to be generally useful.  While a signature provider plugin could declare an option that sets the signature padding mode at the plugin level, this would effectively override whatever the caller passes and would be generally discouraged.  Plus, arguments for options that a plugin defines are processed only by the plugin, not signature formatters.

   One option might be for Sign CLI itself to declare a new option for RSA signature padding and pass the value to individual signature formatters when RSA signing.  However, some signature formatters may support only 1 RSA signature padding mode, and therefore may not respect a user's choice.

## Q & A

1. Q: How will Sign CLI move from one major version of the .NET runtime to the next major version?

   A: Sign CLI already specifies [`<RollForward>LatestMajor</RollForward>`](https://github.com/dotnet/sign/blob/f4efed9e8fb3296f29497b90feb6548e506f2078/src/Sign.Cli/Sign.Cli.csproj#L11), which roughly has the effect of enabling Sign CLI to run on later major versions of the .NET SDK.

   It's worth noting that the .NET team recognizes the impact of the default roll forward policy (`Minor`) on .NET tools.  You install a newer major version of the .NET SDK and suddenly all .NET tools are unavailable.  They are thinking about how to solve it.
     * [Enable projects to use the SDK `TargetFramework``](https://github.com/dotnet/sdk/issues/29949)
     * [[Proposal] .NET Tools should default to running on the latest available Runtime](https://github.com/dotnet/sdk/issues/30336)

1. Q: How will plugins move from one major version of the .NET runtime to the next major version?

   A: The gold standard is to retarget, recompile, and retest on the newer .NET runtime, but that isn't always feasible.  This applies to both plugins and Sign CLI, the plugin host.  [This](https://learn.microsoft.com/dotnet/core/versions/#net-runtime-compatibility) is a good read.

   Sign CLI will attempt to load a plugin entry point that best matches Sign CLI's target framework.  Plugins can add support for newer target frameworks at any time, and Sign CLI will load them when Sign CLI adds matching support.

1. Q: Although NuGet CLI's do not respect the `requireLicenseAcceptance` property, should Sign CLI require license acceptance before installing/updating a plugin package?  See the following issues for more context:

   * NuGet:  [Deprecate \<requireLicenseAcceptance\> from nuspec and VS](https://github.com/NuGet/Home/issues/7439)
   * NuGet:  [Nuget.exe install does not honor requireLicenseAcceptance](https://github.com/NuGet/Home/issues/8299)
   * PowerShellGetv2:  [Changes to support require license acceptance flag](https://github.com/PowerShell/PowerShellGetv2/pull/150)

   A: TBD

1. Q: Should we create a JSON schema (like [.NET templating](https://github.com/dotnet/templating/blob/c90f24f02bf582d80d00ccc807066347d32edca3/src/Microsoft.TemplateEngine.Orchestrator.RunnableProjects/Schemas/JSON/template.json)) for `plugin.json`?

   A: It seems like a good idea.  Are there considerations either way?

1. Q: How do we enable plugin localization?  See [.NET template localization](https://github.com/dotnet/templating/blob/f5fef556632723ecf1387ef1498aa55f54299fba/docs/authoring-tools/Localization.md) for prior art.

   A: Localizability is out of scope now, to be addressed in a later specification.

1. Q: Why is `plugin.json` necessary?

   A: `plugin.json` declares which assembly and type for the plugin host to load and what command-line options to offer to users.  This greatly simplifies plugin loading and execution.

   In the future, we could consider tooling to generate `plugin.json` as part of a plugin build.

## References

<a name="r1"></a>1. ["Baseline Requirements for the Issuance and Management of Publicly‐Trusted Code Signing Certificates"](https://cabforum.org/wp-content/uploads/Baseline-Requirements-for-the-Issuance-and-Management-of-Code-Signing.v3.3.pdf), section 6.2.7.4 (version 3.3.0, June 29, 2023)
