
Provide SDK hint paths in global.json
===

## Summary
This proposal adds two new properties to the `sdk` object in [global.json](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json#globaljson-schema).

```json
{
    "sdk": {
        "additionalPaths": [ ".dotnet" ],
        "additionalPathsOnly": false,
    }
}
```

These properties will be considered by the resolver host when attempting to locate a compatible .NET SDK. This particular configuration would cause the local directory `.dotnet` to be considered _in addition_ to the current set of locations.

## Motivation
There is currently a disconnect between the ways the .NET SDK is deployed in practice and what the host resolver can discover when searching for compatible SDKs. By default the host resolver is only going to search for SDKS in machine wide locations: `C:\Program Files\dotnet`, `%PATH%`, etc ...  The .NET SDK though is commonly deployed to local locations: `%LocalAppData%\Microsoft\dotnet`, `$HOME/.dotnet`. Many repos embrace this and restore the correct .NET for their builds into a local `.dotnet` directory.

The behavior of the host resolver is incompatible with local based deployments. It will not find these deployments without additional environment variable configuration and instead search in machine wide locations. That means tools like Visual Studio and VS Code simply do not work with local deployment by default. Developers must take additional steps like manipulating `%PATH%` before launching these editors. That reduces the usefulness of items like the quick launch bar, short cuts, etc ...

This is further complicated when developers mix local and machine wide installations. The host resolver will find the first `dotnet` according to its lookup rules and search only there for a compatible SDK. Once developers manipulate `%PATH%` to prefer local SDKS the resolver will stop considering machine wide SDKS. That can lead to situations where there is machine wide SDK that works for a given global.json but the host resolver will not consider it because the developer setup `%PATH%` to consider a locally installed SDK. That can be very frustrating for end users.

This disconnect between the resolver and deployment has lead to customers introducing a number of creative work arounds:

- [scripts](https://github.com/dotnet/razor/pull/9550) to launch VS Code while considering locally deployed .NET SDKs
- [docs and scripts](https://github.com/dotnet/sdk/blob/518c60dbe98b51193b3a9ad9fc44e055e6e10fa0/documentation/project-docs/developer-guide.md?plain=1#L38) to setup the environment and launch VS so it can find the deployed .NET SDKs.
- [scritps](https://github.com/dotnet/runtime/blob/main/dotnet.cmd) that wrap `dotnet` to find the  _correct_ `dotnet` to use during build.

These scripts are not one offs, they are increasingly common items in repos in `github.com/dotnet` to attempt to fix the disconnect. Even so many of these solutions are incomplete because they themselves only consider local deployment. They dont't fully support the full set of ways the SDK can be deployed.

This is not a problem specific to the .NET team. Indeed this problem is felt sharply there due to arcade infrastructure using xcopy style deployment into `.dotnet`. Our customers feel this problem as well. Consider the following examples:

- This [issue](https://github.com/dotnet/sdk/issues/8254) from 2017 attempting to solve this problsem. It gets several hits a year from customers who are similarly struggling with our toolings inability to handle local deployment.
- This [internal discussion](https://teams.microsoft.com/l/message/19:ed7a508bf00c4b088a7760359f0d0308@thread.skype/1698341652961?tenantId=72f988bf-86f1-41af-91ab-2d7cd011db47&groupId=4ba7372f-2799-4677-89f0-7a1aaea3706c&parentMessageId=1698341652961&teamName=.NET%20Developer%20Experience&channelName=InfraSwat&createdTime=1698341652961) from a C# team member. They wanted to use VS as the product is shipped to customers and got blocked when we shipped an SDK that didn't have a corresponding MSI and hence VS couldn't load Roslyn anymore.
- [VS code](https://github.com/dotnet/vscode-csharp/issues/6471) having to adjust to consider local directories for SDK because our resolver can't find them.

## Detailed Design
The global.json file will support two new properties under the `sdk` object:

- `"additionalLocations"`: this is a list of paths that the host resolver should consider when looking for compatible SDKs. Relative paths will be interpreted relative to the global.json file.
- `"additionalLocationOnly"`: when true the resolver will _only_ consider paths in `additionalLocations`. It will not consider any machine wide locations (unless they are specified in `additionalLocations`). The default for this property is `false`.

The `additionalLocations` works similar to how multi-level lookup works. It adds additional locations that the host resolver should consider when trying to resolve a compatible .NET SDK. For example:

```json
{
    "sdk": {
        "additionalPaths": [ ".dotnet" ],
    }
}
```

In this configuration the host resolver would find a compatible SDK if it exists in `.dotnet` or a machine wide location.

The values in the `additionalLocations` property can be a relative or absolute path. When a relative path is used it will be resolved relative to the location of global.json. These values also support the following substitutions:

- `"$user$"`: this matches the user-local installation point of .NET SDK for the current operating system: `%LocalAppData%\Microsoft\dotnet`` on Windows and `$HOME/.dotnet`` on Linux/macOS.
- `"$machine$"`: this matches the machine installation point of .NET for the current operating system.
- `%VARIABLE%/$VARIABLE`: environment variables will be substituted. Either the Windows or Unix format can be used here and will be normalized for the operating system the host resolver executes on.

The host resolver will consider the `additionalLocations` in the order they are defined and will stop at the first match. If none of the locations have a matching SDK then it it will fall back to considering machine wide installations (unless `additionalLocationsOnly` is `true`).

This design requires us to only change the host resolver. That means other tooling like Visual Studio, VS Code, MSBuild, etc ... would transparently benefit from this change. Repositories could update global.json to have `additionalLocations` support `.dotnet` and Visual Studio would automatically find it without any design changes.

## Considerations
### Installation Points
One item to keep in mind when considering this area is the .NET SDK can be installed in many locations. The most common are:

- Machine wide
- User wide: `%LocalAppData%\Microsoft\dotnet`` on Windows and `$HOME/.dotnet`` on Linux/macOS.
- Repo: `.dotnet`

Our installation tooling tends to avoid redundant installations. For example if restoring a repository that requires 7.0.400, the tooling will not install it locally if 7.0.400 is installed machine wide. It also will not necessarily delete the local `.dotnet` folder or the user wide folder. That means developers end up with .NET SDK installs in all three locations but only the machine wide install has the correct ones.

As a result solutions like "just use .dotnet if it exists" fall short. It will work in a lot of casse but will fail in more complex scenarios. To completely close the disconnect here we need to consider all the possible locations.

### Do we need additionalPathsOnly?
The necessity of this property is questionable. The design includes it for completeness and understanding that the goal of some developers is complete isolation from machine state. As long as we're considering designs that embrace local deployment, it seemed sensible to extend the design to embrace _only_ local deployment.

At the same time the motivation for this is much smaller. It would be reasonable to cut this from the design and consider it at a future time when the motivation is higher.
