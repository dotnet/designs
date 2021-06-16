# Syntax for passing properties to MSBuild in `dotnet run`

Kathleen Dollard | Status: Draft

The .NET CLI passes all command line arguments that are not explicitly handled through to MSBuild for `dotnet build` and `dotnet publish`. This does not work for `dotnet run` because there are three sets of arguments: 

* CLI arguments for `dotnet run` itself.
* Arguments for the resulting application, identified by `--`.
* MSBuild arguments, which are not currently supported. 

To support the Xamarin/Maui workloads, we need to pass a property to MSBuild that specifies the device to target. This is being solved by adding a `--property` option to `dotnet run`. This will route to MSBuild as `-p:<delimited list>.

**Note:** A long term approach to managing “device” selection is planned for a later release. In .NET 6 CLI usage of these workloads is expected to just be CI, so this is not considered critical, and the design work is expected to include adjacent problems of specifying containers, and x64 emulation). 

A problem arises in the abbreviation. `-p`. It is currently used to specify the `--project` for `dotnet run`. This proposal is a course to change `-p` from meaning `--project` to `--property` in order to be consistent with the similar commands - `dotnet build` and ` dotnet publish`. This proposal balances backward compatibility and consistency with other commands, which we can do because the usages can be distinguished syntactically. 

During a deprecation phase, which will be at least the length of .NET 6, older usage of `-p` as `--project` will be recognized as when there is no trailing colon (`-p:`) and the argument is not legal for MSBuild. Users will receive a warning that the abbreviation is deprecated. 

We will identify usage as `--property` when the argument is legal MSBuild syntax. Legal MSBuild syntax will be defined as: 

* A comma or semi-colon delimited strings of values in the format <name>=<value> or <name>:<value>

## Goals

We believe that in the long term `-p` should be used consistently across `dotnet build`, `dotnet publish` and `dotnet run`  because of similarity between these commands. 

### Design

The basic design is described in the opening: 
* `-p` with syntax that is valid for MSBuild properties will be treated as `--property`.
* `-p:` will be passed to MSBuild so the an MSBuild error is displayed when the argument is illegal
* Any other usage will be treated as `--project` and will receive a warning that this use is deprecated.
* Usage in the form `-p:` will be allowed but not encouraged or documented. 
* In .NET 7, we will add `-p` as an abbreviation equivalent to `p:`
* At some future point, we will remove `-p` for `--project`
  
Implementation note
For System.CommandLine, it is expected that `-p` will be treated as `--property` and removed as an abbreviation for `--project`. Special handling will be needed in the `dotnet run` handler.
Warning text
When -p is used (with no colon) in .NET 6, the following warning will be displayed: 
“The abbreviation of -p for --project is deprecated. Please use --project.”
Q & A

