# A unified configuration file and experience for the .NET SDK

**Owner** [Chet Husk](https://github.com/baronfel) | [Daniel Plaisted](https://github.com/dsplaisted)

The .NET SDK is a product that consists of a number of different tools, all owned by different teams, all operating at slightly different levels of interaction. Some tools operate on the entire repository, some tools on a single project, and yet other tools on a single file.

Because of the disparate nature of these tools, the configuration for each tool is different. This makes it difficult for users to understand how to configure the tools, and difficult for the tools to share configuration information. This document proposes a single, centralized document for repository-wide configuration that tools belonging to the SDK (as well as SDK-adjacent 1st- and 3rd-party tools) can use to share configuration information in a way that has

* a consistent user experience
* a well supported IDE experience
* is shareable across team members and trackable in source control

## What exists today

Today there are several places where teams have put configuration for repo- or project-level configuration for tooling:

* global.json files
* NuGet.config
* .config/dotnet-tools.json
* .runsettings (dotnet test)

In addition, several tools are only configurable via environment variables, which are not easily shareable or trackable in source control (due to the lack) of support for .env or other such conventions in the `dotnet` CLI.

All of these files and systems arose independently, have different formats, and the amount of tooling support for them varies significantly. We have 'shipped the org structure' in terms of our configuration.

## What is the need?

There are [several dotnet/sdk items](https://github.com/dotnet/sdk/issues?q=label%3Aconfig-system-candidate) where the concept of a unified configuration system would be beneficial. These include:

* expressing user behavioral preferences around build/pack/publish Configurations
* if binlogs should be made when performing builds
* the use or non-use of advanced terminal behaviors for build
* default verbosities for build/pack/publish
* custom aliases for CLI commands
* unified opt-out of telemetry
* storing tooling decisions like which mode `dotnet test` should execute in

All kinds of repository-wide configuration need a unified place to be expressed that we lack today.

## Proposal

I propose that we create a new, repo-level configuration file that will be consumed by `dotnet` and `dotnet`-related tooling to provide a unified configuration experience. This file will be called `poohbear.config`[^1] and will be located at the root of the repository.

### Composition and heirarchy

This file will be hierarchical in a very limited fashion. The final realized configuration will be the result of merging the `poohbear.config` file in the repository root with an optional `poohbear.config` file in the user's `$HOME` directory (specifically, the SpecialFolders.UserProfile location in the .NET BCL). This will allow users to set defaults for their own use, and then override those defaults on a per-repository basis for sharing with coworkers.

### Nested keys

We should encourage tools to stake a claim on a top-level 'namespace' for keys in this configuration file. Keys should either be namespaced with the tool's name or be namespaced with a common prefix that is unique to the tool. This will prevent key collisions between tools and make it easier for users to understand what each key does. This allows for the format to be open to extensibility, as new tools can add their own keys without fear of collision.

### File format

>[!NOTE]
> This is going to be a bike-shedding point. Please try to focus first on the use cases and tool-level use, and then we can discuss the file format.

The `poohbear.config` file should be parseable by tooling and pleasant to read and edit by users.

The choice of format is in some ways limited by engineering considerations. The easiest thing would be to choose a format that is already supported by the BCL, which would lead us to JSON, XML, or INI. JSON and XML formats have downsides, though: JSON doesn't support comments and has an anemic data model, and XML is historically verbose and difficult to parse in a consistent way.

The recent wave of programming languages and programming-related tools have popularized the use of more human-readable formats like YAML, TOML, and INI. These formats are more human-readable and have more expressive power than JSON or XML. None of them have parsers in the BCL, though, which would mean that we would have to write our own parser or use a third-party library. Writing a parser is a potentially error-prone task, and using a third-party library would require onboarding the parser to source build as well as doing a thorough security audit. YAML specifically has such a lax parser that it might be a security risk to use it.

My personal suggestion is that we should use [TOML][toml] and advocate for a parser for it in the BCL. TOML is in wide use in the [Python][python-why-toml] and Rust ecosystems. It strikes a good balance between human-readability and expressiveness. The [toml spec][toml-spec] includes a [comparison][toml-comparison] between TOML and other formats.

### Tooling support

#### CLI command
CLI commands (dotnet config) a la `git config` will be provided to allow users to set and get configuration values. This will allow users to set configuration values without having to edit the file directly. We should allow reading of configuration values via a CLI command to provide a least-common-denominator access mechanism.

`dotnet config get <key> [--local|--global]`

For example, to get the 'realized' value of the `dotnet.telemetry` key for a given repo, you could run the following command in that repo:
```shell
> dotnet config get dotnet.telemetry
true
```
and the following command would get the user-global value only
```shell
> dotnet config get dotnet.telemetry --global
false
```

The [git config][git-config] command is a good source of inspiration.


#### Programmatic access

We (BCL) should provide a library to parse the file and integrate its data into `Microsoft.Extensions.Configuration` so that tools can easily consume the configuration data.

#### IDE onboarding

We must ensure that both the CLI and VS support the configuration file and the configuration system.

#### Usage within the dotnet CLI and supporting tools
Inside the `dotnet` CLI (and tools that ship within the CLI), we will initialize a `ConfigurationBuilder` with the `poohbear.config` file and merge it with the user's `poohbear.config` file. We will also seed that builder with environment variables. Tools that need the configuration within the `dotnet` CLI will read the appropriate values and apply them to their execution.

### Secondary goals

Because the configuration file will be read into a Microsoft.Extensions.Configuration context, this opens up the possibility for environment variables to be implicitly defined for all configuration keys. This would allow users to set configuration values via environment variables, which is a common pattern and an improvement from the mismatched support for environment variables that we have today.

### Future work

With adoption, we could soft-deprecate some of the sources of standalone configuration mentioned above. Each configuration file could migrate to one or more namespaced keys in the `poohbear.config` file. For example, .NET SDK tools could live in a `dotnet.tools.<toolid>` namespace that listed the tools and any tool-specific configuration.

```toml
[dotnet.tools.dotnet-ef]
version = "9.0.1"

[dotnet.tools.fsautocomplete]
version = "0.75.0"
roll-forward = true
```

### Non-goals

We do not aim to replace solutions with this config file.

We do not aim to provide 'build configurations' - descriptions of projects to build and properties to apply to that build.

### Suggested configuration keys/namespaces for v1

> TBD, perhaps as part of discussion

* dotnet
  * settings related to cross-cutting dotnet cli behaviors: telemetry, CLI aliases, etc
* test
  * settings related to the behavior of `dotnet test`

### Prior art

The most direct analogue to this is perhaps the [Cargo .config file][cargo-config]. This file doesn't contain any data about the project(s) being built, but instead contains configuration data for the `cargo` tool itself. Some use cases supported by this file include:

* command aliases
* flags that control the behavior of 'cargo build'
* authentication/credential provider configuration
* behavior flags for specific cargo subcommands (e.g. what version control to use when creating a new project)
* http client behaviors
* dependency resolver behaviors
* console output behaviors

All of these have parallels in the dotnet CLI and tools that ship with the SDK.

[cargo-config]: https://doc.rust-lang.org/cargo/reference/config.html#configuration-format
[git-config]: https://git-scm.com/docs/git-config
[python-why-toml]: https://peps.python.org/pep-0518/#other-file-formats
[toml-comparison]: https://github.com/toml-lang/toml?tab=readme-ov-file#comparison-with-other-formats
[toml-spec]: https://toml.io/en/v1.0.0
[toml]: https://toml.io

[^1]: Name is a placeholder, and will be replaced with a more appropriate name before implementation. A final name might change the extension to represent the file type chosen.
