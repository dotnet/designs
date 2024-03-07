# Provide SDK hint paths in global.json

## Summary

This proposal adds two new properties to the `sdk` object in
[global.json][global-json-schema]

```json
{
    "sdk": {
        "paths": [ ".dotnet", "$host$" ],
        "errorMessage": "The .NET SDK could not be found, please run ./install.sh."
    }
}
```

These properties will be considered by the resolver host during .NET SDK
resolution. The `paths` property lists the locations that the resolver should
consider when attempting to locate a compatible .NET SDK. The `errorMessage`
property controls what the resolver displays when it cannot find a compatible
.NET SDK.

This particular configuration would cause the local directory `.dotnet` to be
considered _in addition_ to the current set of locations. Further if resolution
failed the resolver would display the contents of `errorMessage` instead of
the default error message.

## Motivation

There is currently a disconnect between the ways the .NET SDK is deployed in
practice and what the host resolver can discover when searching for compatible
SDKs. By default the host resolver is only going to search for SDKs next to
the running `dotnet`. This often means machine-wide locations, since users
and tools typically rely on `dotnet` already being on the user's path when
launching, instead of specifying a full path to the executable. The .NET SDK
though is commonly deployed to local locations: `%LocalAppData%\Microsoft\dotnet`,
`$HOME/.dotnet`. Many repos embrace this and restore the correct .NET for their
builds into a local `.dotnet` directory.

The behavior of the host resolver is incompatible with local based deployments.
It will not find these deployments without additional environment variable
configuration and only search next to the running `dotnet`. That means tools
like Visual Studio and VS Code simply do not work with local deployment by
default. Developers must take additional steps like manipulating `%PATH%` before
launching these editors. That reduces the usefulness of items like the quick
launch bar, short cuts, etc.

This is further complicated when developers mix local and machine wide
installations. The host resolver will find the first `dotnet` according to its
lookup rules and search only there for a compatible SDK. Once developers
manipulate `%PATH%` to prefer local SDKS the resolver will stop considering
machine wide SDKS. That can lead to situations where there is machine wide SDK
that works for a given global.json but the host resolver will not consider it
because the developer setup `%PATH%` to consider a locally installed SDK. That
can be very frustrating for end users.

This disconnect between the resolver and deployment has lead to customers
introducing a number of creative work arounds:

- [scripts][example-scripts-razor] to launch VS Code while considering locally
deployed .NET SDKs
- [docs and scripts][example-scripts-build] to setup the environment and launch
VS so it can find the deployed .NET SDKs.
- [scripts][example-scripts-dotnet] that wrap `dotnet` to find the  _correct_
`dotnet` to use during build.

These scripts are not one offs, they are increasingly common items in repos in
`github.com/dotnet` to attempt to fix the disconnect. Even so many of these
solutions are incomplete because they themselves only consider local deployment.
They don't fully support the full set of ways the SDK can be deployed.

This problem also manifests in how customers naturally want to use our
development tools like Visual Studio or VS Code. It's felt sharply on the .NET
team, or any external customer who wants to contribute to .NET, due to how
.NET Arcade infrastructure uses xcopy deployment into `.dotnet`. External teams
like Unity also feel this pain in their development:

- This [issue][cases-sdk-issue] from 2017 attempting
to solve this problem. It gets several hits a year from customers who are
similarly struggling with our toolings inability to handle local deployment.
- This [internal discussion][cases-internal-discussion] from a C# team member.
They wanted to use VS as the product is shipped to customers and got blocked
when we shipped an SDK that didn't have a corresponding MSI and hence VS
couldn't load Roslyn anymore.
- [VS Code][cases-vscode] having to adjust to consider local directories for SDK
because our resolver can't find them.

## Detailed Design

The global.json file will support two new properties under the `sdk` object:

- `"paths"`: this is a list of paths that the host resolver should
consider when looking for compatible SDKs. In the case this property is `null`
or not specified, the host resolver will behave as it does today.
- `"errorMessage"`: when the host resolver cannot find a compatible .NET SDK it
will display the contents of this property instead of the default error message.
In the case this property is `null` or not specified, the default error message
will be displayed.

The values in the `paths` property can be a relative path, absolute path or
`$host$`.  When a relative path is used it will be resolved relative to the
location of the containing global.json. The value `$host$` is a special value
that represents the machine wide installation path of .NET SDK for the
[current host][installation-doc].

The values in `paths` are considered in the order they are defined. The host
resolver will stop when it finds the first path with a compatible .NET SDK.
For example:

```json
{
    "sdk": {
        "paths": [ ".dotnet", "$host$" ],
    }
}
```

In this configuration the host resolver would find a compatible .NET SDK, if it
exists in `.dotnet` or a machine wide location.

This lookup will stop on the first match which means it won't necessarily find
the best match. Consider a scenario with a global.json that has:

```json
{
  "sdk": {
    "paths": [ ".dotnet", "$host$" ],
    "version": "7.0.200",
    "rollForward": "latestFeature"
  }
}
```

In a scenario where the `.dotnet` directory had 7.0.200 SDK but there was a
machine wide install of 7.0.300 SDK, the host resolver would pick 7.0.200 out
of `.dotnet`. That location is considered first, it has a matching .NET SDK and
hence discovery stops there.

This design requires us to only change the host resolver. That means other
tooling like Visual Studio, VS Code, MSBuild, etc ... would transparently
benefit from this change. Repositories could update global.json to have
`paths` support `.dotnet` and Visual Studio would automatically find it without
any design changes.

## Considerations

### Installation Points

One item to keep in mind when considering this area is the .NET SDK can be
installed in many locations. The most common are:

- Machine wide
- User wide: `%LocalAppData%\Microsoft\dotnet` on Windows and `$HOME/.dotnet`
on Linux/macOS.
- Repo: `.dotnet`

Our installation tooling tends to avoid redundant installations. For example, if
restoring a repository that requires 7.0.400, the tooling will not install it
locally if 7.0.400 is installed machine wide. It also will not necessarily
delete the local `.dotnet` folder or the user wide folder. That means developers
end up with .NET SDK installs in all three locations but only the machine wide
install has the correct ones.

As a result solutions like "just use .dotnet, if it exists" fall short. It will
work in a lot of cases but will fail in more complex scenarios. To completely
close the disconnect here we need to consider all the possible locations.

### Best match or first match?

This proposal is designed at giving global.json more control over how SDKs are
found. If the global.json asked for a specific path to be considered and it has
a matching SDK but a different SDK was chosen, that seems counter intuitive.
Even in the case where the chosen SDK was _better_. This is a motivating
scenario for CI where certainty around SDK is often more desirable than
_better_. This is why the host discovery stops at first match vs. looking at
all location and choosing the best match.

Best match is a valid approach though. Can certainly see the argument for some
customers wanting that. Feel like it cuts against the proposal a bit because it
devalues `paths` a bit. If the resolver is switched to best match then, the need
for configuration around best versus first match is much stronger. There would
certainly be a customer segment that wanted to isolate from machine state in
that case.

### dotnet exec

This proposal only impacts how .NET SDK commands do runtime discovery. The
command `dotnet exec` is not an .NET SDK command but instead a way to invoke
the app directly using the runtime installed with `dotnet`.

It is reasonable for complex builds to build and use small tools. For example
building a tool for linting the build, running complex validation, etc ... To
work with local SDK discovery these builds need to leverage `dotnet run` to
execute such tools instead of `dotnet exec`.

```cmd
# Avoid
> dotnet exec artifacts/bin/MyTool/Release/net8.0/MyTool.dll
# Prefer
> dotnet run --no-build --framework net7.0 src/Tools/MyTool/MyTool.csproj
```

### Environment variables

Previous versions of this proposal included support for using environment
variables inside `paths`. This was removed due to lack of motivating
scenarios and potential for creating user confusion as different machines can
reasonably have different environment variables.

This could be reconsidered if motivating scenarios are found.

### Other Designs

[This is a proposal][designs-other] similar in nature to this one. There are a
few differences:

1. This proposal is more configurable and supports all standard local
installation points, not just the `.dotnet` variant.
2. This proposal doesn't change what SDK is chosen: the rules for global.json
on what SDKs are allowed still apply. It simply changes the locations where the
SDK is looked for.
3. No consideration for changing the command line. This is completely driven
through global.json changes.

Otherwise the proposals are very similar in nature.

[global-json-schema]: https://learn.microsoft.com/en-us/dotnet/core/tools/global-json#globaljson-schema
[example-scripts-razor]: https://github.com/dotnet/razor/pull/9550
[example-scripts-build]: https://github.com/dotnet/sdk/blob/518c60dbe98b51193b3a9ad9fc44e055e6e10fa0/documentation/project-docs/developer-guide.md?plain=1#L38
[example-scripts-dotnet]: https://github.com/dotnet/runtime/blob/main/dotnet.cmd
[cases-sdk-issue]: https://github.com/dotnet/sdk/issues/8254
[cases-internal-discussion]: https://teams.microsoft.com/l/message/19:ed7a508bf00c4b088a7760359f0d0308@thread.skype/1698341652961?tenantId=72f988bf-86f1-41af-91ab-2d7cd011db47&groupId=4ba7372f-2799-4677-89f0-7a1aaea3706c&parentMessageId=1698341652961&teamName=.NET%20Developer%20Experience&channelName=InfraSwat&createdTime=1698341652961
[cases-vscode]: https://github.com/dotnet/vscode-csharp/issues/6471
[designs-other]: https://github.com/dotnet/designs/blob/main/accepted/2022/version-selection.md#local-dotnet
[installation-doc]: https://github.com/dotnet/designs/blob/main/accepted/2021/install-location-per-architecture.md
