# CLI Syntax Guidelines

Many tools have a CLI (command-line interface) which allows users to type English words to describe commands. This post describes how we build the syntax for commands in the .NET Core CLI. This articulates the guidelines that are similar to behavior of other CLIs, including Git (git), Angular (ng) and Azure (az).

These guidelines were adopted as a goal for the CLI during .NET Core SDK 2.1.300 development. This version implements these goals for new features. Future SDKs will bring CLI syntax in line with these guidelines, while allowing existing syntax for backwards compatibility.

NOTE: This explains a breaking change between 2.1.300-Preview1 and Preview2 - `dotnet tool install` replaces `dotnet install tool`

## Goal

The goals of a CLI syntax are:

* Intuitive: experienced users think in the language of the CLI and the commands flow naturally as they think about their intent.
* Discoverability: help should smoothly expand from an overview of what the tool does to details about each command.
* Reduced typing: tab completion should just work.
* Comfort: users should relax with little fear of making a mistake 

These goals are supported by:

* Explicitness in context, actions and arguments
* Generally consistency
* Command parsers libraries
* Minimal traps

Good CLIs use these techniques in support of the goals, but there can be grey areas and a bit of messiness. Tricky and difficult to name situations sometimes means the problem isn't yet clear or too much is being done. In truly challenging naming scenarios, strict adherence to rules is less important than the goals. 

## Form

The general form of a command is:

```terminal
$ dotnet [context] [action] [options] [context specification] [action type] [arguments]
```

Examples from the .NET Core CLI:

```terminal
$ dotnet [project] add [options] [<PROJECT_NAME>] package <PACKAGE_ID>
$ dotnet tool install [options] <PACKAGE_ID>
```

In these examples, `project` denotes the actual word "project" and is optional because it is the core, or default context.

| Item                  | Example 1      | Example 2    | Comments                                       |
|-----------------------|----------------|--------------|------------------------------------------------|
| context               | project        | tool         | `project` generally omitted since core context |
| action                | add            | install      |                                                |
| context specification | <PROJECT_NAME> |              |                                                |
| action type           | package        |              |                                                |
| argument              | <PACKAGE_ID>   | <PACKAGE_ID> |                                                |


### Context and Core Context

Each command works with a core object or concept. This concept may be implicit or explicit. For example, the Docker command `build` builds an image. The context is `image`. For Docker, the core context is `image` and by default commands work on images. Docker allows the context to optionally be included so these two commands are equivalent:

``` terminal
$ docker build ...
$ docker image build ...
```

The core context for the .NET Core CLI is the project. Like many other CLIs, this concept is a little fluffy and you will probably comfortably think about it as _the main thing I am working on right now_. This will generally be a project, but it might in some cases be a solution or a directory. We think of a solution as a super-project and in some cases a project is represented by its directory. The .NET Core CLI will adopt the Docker style of allowing the core context to be explicit or implicit because it makes help much easier. `dotnet --help` may have 30 entries while `dotnet project --help` may have 15. For example, the following commands continue to work as before:

``` terminal
$ build
$ new
$ publish
$ restore
$ run
```

The following commands will work in a future version of the .NET Core CLI:

``` terminal
$ project build
$ project new
$ project publish
$ project restore
$ project run
```

In general terms, this is described:

``` terminal
$ dotnet [context] action ...
```

Where context is legal, but generally omitted. In specific terms, this is described as:

``` terminal
$ dotnet [project] build ...
```

When the context is not project, it is specified. For example:

```terminal
$ dotnet tool install -g MyFavoriteTool
```

_NOTE: This represents a breaking change between .NET Core SDK 2.1.300-Preview1 and .NET Core SDK 2.1.300-Preview2._

### Action

The action works on, or with the concept of the context. `dotnet tool install` installs a tool. `dotnet build` builds a project because the core context does not need to be typed. 

Actions are generally verbs but should primarily be the thing a user thinks of when wanting to do the action.

Limit the number of action words while still allowing them to describe their scenario precisely. `add` is used to add things to the context, generally to add things to the project. `install` is used to install what's described by the context, generally retrieving it from somewhere outside the project. `add` and `install` do similar, but different things. Similarly, `list` displays things currently available in the context, such as the packages in the current project. In the future, a verb like `search` may display packages available in the package sources.

Adding a new verb will not be common, although a few (not all listed here) will be addd in .NET Core SDK 2.1.300. Current verbs are:

```
add 
build
clean
install
list
migrate
new
pack
publish
remove
restore
run
test
uninstall
update
```

### Options

Options are also called switches.

Options generally follows Posix/Gnu standard:

* Options are words specified with a preceding double dash.
* Some options can be abbreviated with a single letter, and then used with a preceding single dash.
* Abbreviations that are more than a single letter are alternate forms of the switch and preceded by double dash.
* Single letters can be combined in the form `-abc` which is equivalent to `-a -b -c`.
* The order in which options appear does not matter.
* Some options can appear multiple times.
* Either form of options may have a single argument, which appears immediately after the option, separated by one or more spaces.
* -- can be used to indicate the end of the argument list; any subsequent arguments are passed onwards.

Options should be consistent where possible. These are some common options:

| Intent            | Option          | Abbreviation | Comments                                                   |
|-------------------|-----------------|--------------|------------------------------------------------------------|
| Help              | --help          | -h           |                                                            |
| Verbosity         | --verbosity     | -v           | q[uiet], m[inimal], n[ormal], d[etailed], and diag[nostic] |
| Configuration fie | --configuration | -c           |                                                            |
| Output location   |                 | -o           |                                                            |

### Context Specification

Many commands allow the specific context to be specified. This generally means defining the project on which the command will operate. For example, PROJECT_NAME is the context specification in the following:

```terminal
dotnet [project] add [options] [<PROJECT_NAME>] package <PACKAGE_NAME>
```

### Action Type

Actions often have variations. For example, `add` can add packages or references to a project. In this example `package` is the action type:

```terminal
$ dotnet [project] add [options] [<PROJECT_NAME>] package <PACKAGE_ID>
```

```terminal
$ dotnet add package MyGreatPackage
```

The action type should appear before arguments. It is internally treated as a subcommand, but not described as a subcommand since action types are generally nouns.

Action types should be as consistent as possible. Current specifiers include:

```terminal
package
reference (project to project reference)
```

### Arguments

Arguments specify details about the command to be performed. Some commands allow multiple arguments. 

In this example, `PACKAGE_ID` is the argument, which is `MyGreatPackage` in the specific example:


```terminal
$ dotnet [project] add [options] [<PROJECT_NAME>] package <PACKAGE_ID>
```

```terminal
$ dotnet add package MyGreatPackage
```

## Avoiding traps

* Avoid difficult to spell words, especially for speakers not in your native tongue. Avoid words spelled differently in the US and Britain.
* Avoid long words and rarely use clear abbreviations (longer than one character) if a long word is the concept
* Supply friendly help when a single dash is used - suggesting the double dash

NOTE: This is a preliminary document, and this section could use more recommendations.

## Appendix: Commands

Commands that currently (prior to 2.1.300) fit these patterns:

| Intent                            | Syntax 4                                                             | Common usage                      | Comment           |
|-----------------------------------|----------------------------------------------------------------------|-----------------------------------|-------------------|
| add nuget package to project      | `[project] add [options] [<PROJECT_FILE>] package <PACKAGE_NAME>`    | `add package foopkg`              |                   |
| add P2P reference to project      | `[project] add [options] [<PROJECT_FILE>] reference<*.*proj>`        | `add reference ../foo/foo.csproj` |                   |
| add project to solution           | `sln [<SLN_FILE>] add [options] <*.*proj>`                           | `sln add foo/foo.csproj`          |                   |
| build and run application         | `[project] run [options] [[--] <additional arguments>...]`           | `run`                             |                   |
| build project                     | `[project] build [options] [<PROJECT_FILE>]`                         | `build`                           |                   |
| create folder for deployment      | `[project] publish [options]`                                        | `publish`                         |                   |
| create nuget package              | `[project] pack [options]`                                           | `pack`                            |                   |
| help                              | `help`                                                               | `help`                            |                   |
| list projects in solution         | `sln [options] [<SLN_FILE>] list`                                    | `sln list`                        |                   |
| push nuget package to server      | `nuget push [arguments] [options]`                                   | `nuget push`                      |                   |
| remove build artifacts            | `[project] clean [options]`                                          | `clean`                           |                   |
| remove nuget package from project | `[project] remove [options] [<PROJECT_NAME>] package <PACKAGE_NAME>` | `remove package foopkg`           |                   |
| remove nuget package from server  | `nuget delete [package]`                                             | `nuget delete foopkg`             |                   |
| remove P2P reference from project | `[project] remove [options] [<PROJECT_NAME>] reference<*.*proj>`     | `remove reference foo.csproj`     | check syntax      |
| remove project from solution      | `sln [<SLN_FILE>] remove [options] <*.*proj>`                        | `sln remove foo.csproj`           |                   |
| restore nuget packages explicitly | `[project] restore [options]`                                        | `restore`                         |                   |
| run test                          | `[project] test [run]`                                               | `test [run]`                      |                   |
| run tests with logger             | `[project] test [run] -l`                                            |                                   | -l not consistent |
| create new project from template  | `[project] new <template> [options]`                                 | `new fooTemplate`                 |                   |

Commands that will be in .NET Core SDK 2.1.300. These changed between 2.1.300-Preview1 and 2.1.300-Preview2

| Intent                | Syntax 4                                   | Common usage                 | Comments |
|-----------------------|--------------------------------------------|------------------------------|----------|
| install global tool   | `tool install -g [options] <PACKAGE_NAME>` | `tool install -g fooToolPkg` |          |
| list global tools     | `tool [list] -g`                           | `tool`                       |          |
| uninstall global tool | `tool uninstall -g <TOOL_NAME>`            | `tool uninstall -g fooTool`  |          |
| update global tool    | `tool update -g <TOOL_NAME>`               | `tool update -g fooTool`     |          |

A handful of existing Commands do not fit this pattern. If we fix any of these, the old syntax would be supported for some time:

| Intent                                | Syntax 4                                      | Common usage               | Current                 | Comment                               |
|---------------------------------------|-----------------------------------------------|----------------------------|-------------------------|---------------------------------------|
| clear nuget caches                    | `nuget clear [cache]`                         | `nuget clear`              | `nuget locals --clear`  | Perhaps nuget-cache                   |
| display nuget cache locations         | `nuget info`                                  | `nuget info`               | `nuget locals --list`   |                                       |
| install template                      | `template install [options] <PACKAGE_NAME>`   | `template install`         | `new --install`         |                                       |
| list P2P references for project       | `[project] reference [list]`                  | `reference`                | `[reference] list`      |                                       |
| list templates                        | `template [list]`                             | `template`                 | `new --list`            |                                       |
| list templates w/o illogical          | `template [list] --<magicword>`               | `template --<magicword>`   | `new`                   | Not sure what to call "not illogical" |
| list templates w/type filter          | `template [list] --type`                      | `template --type`          | `new --type`            |                                       |
| list tests                            | `[project] test list`                         | `test list`                | `test --list-tests`     |                                       |
| list tests that match filter          | `[project] test list --filter`                | `test list --filter <EXP>` | `test --filter <EXP>`   |                                       |
| put assemblies into runtime pkg store | `runtime-store add --manifest file_name, etc` | `runtime-store add`        | `store <MANIFEST_FILE>` | I'm quite unclear on this one         |

Syntax for commands we might create:

| Intent                               | Syntax 4                                           | Common usage                     | Comments                                   |
|--------------------------------------|----------------------------------------------------|----------------------------------|--------------------------------------------|
| check runtime for updates            | `runtime check [<VERSION>]`                        | `runtime check`                  |                                            |
| check sdk for updates                | `sdk check [<VERSION>]`                            | `sdk check`                      |                                            |
| check template for updates           | `template check <TEMPLATE_NAME>`                   | `template check`                 |                                            |
| check tool for updates               | `tool check [-repo]`                               | `tool check`                     |                                            |
| install repo tool                    | `tool install [--repo] <PACKAGE_NAME>`             | `tool install`                   |                                            |
| install runtime                      | `runtime install [options] [<VERSION>/latest/lts]` | `runtime install`                | Default latest                             |
| install sdk                          | `sdk install [options] [<VERSION>/latest/lts]`     | `sdk install`                    | Default latest                             |
| list assemblies in runtime pkg store | `runtime-store [list]`                             | `runtime-store`                  |                                            |
| list nuget packages for project      | `[project] package [list]`                         | `package`                        |                                            |
| list repo tools                      | `tool [list] [-repo]`                              | `tool`                           |                                            |
| list runtimes                        | `runtime [list]`                                   | `runtime`                        |                                            |
| list sdks                            | `sdk [list]`                                       | `sdk`                            |                                            |
| search available global/repo tools   | `tool search <FILTER>`                             | `tool search A*`                 |                                            |
| search available packages            | `package search <FILTER>`                          | `nuget search A*`                |                                            |
| search available runtimes            | `runtime search [<VERSION_FILTER>]`                | `runtime search`                 | Default list all available                 |
| search available sdks                | `sdk search [<VERSION_FILTER>]`                    | `sdk search`                     | Default list all available                 |
| search available templates           | `template search <FILTER>`                         | `template search A*`             |                                            |
| uninstall runtime                    | `runtime uninstall <VERSION>`                      | `runtime uninstall 2.0.3`        |                                            |
| uninstall sdk                        | `sdk uninstall <VERSION>`                          | `sdk uninstall 2.0.0`            |                                            |
| uninstall template                   | `template uninstall <TEMPLATE_NAME>`               | `template uninstall footemplate` |                                            |
| uninstall repo tool                  | `tool uninstall [-repo] <TOOL_NAME>`               | `tool update`                    |                                            |
| update runtime                       | `runtime update [<VERSION>]`                       | `runtime update`                 | This could get patches when version passed |
| update sdk                           | `sdk update [<VERSION>]`                           | `sdk update`                     | This could get patches when version passed |
| update template                      | `template update <TEMPLATE_NAME>`                  | `template update`                |                                            |
| update repo tool                     | `tool update [-repo]`                              | `tool update`                    |                                            |



| Special purpose - gateway drugs |     |     |
|---------------------------------|-----|-----|
| vstest                          |     |     |
| migrate                         |     |     |
| msbuild                         |     |     |


