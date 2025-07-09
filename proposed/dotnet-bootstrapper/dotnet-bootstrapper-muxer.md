# `dotnet install` interface as `DNVM` and `PATH` Management

The idea of a .NET bootstrapper ala `nvm` is proposed with a designed CLI in [`dotnet-bootstrapper-cli-experience.md`](./dotnet-bootstrapper-cli-experience.md). This document expands upon how this application could be implemented and suggests several approaches to be discussed and evaluated. I will refer to the bootstrapper as `b`.

## Interfacing as `dotnet`

In some sense, having the .NET SDK be able to manage itself and update itself would be convenient for users. Other toolchains provide a separate management executable (`nvm` as opposed to `node`, and similar for `rustUp`). This motivates the idea of having a bootstrapper ala `nvm` or `dnvm` that interfaces as `dotnet`, to run `dotnet install`, as specified in [`dotnet-bootstrapper-cli-experience.md`](./dotnet-bootstrapper-cli-experience.md). Having `dotnet install` utilize local zip installs is a departure from many existing conventions.

The question if we were to build this experience of interfacing as `dotnet`, is how? And how would it be acquired? Several methods have been suggested.

### Add a binary that lives alongside the muxer/dotnet.exe

A bootstrapper executable, say, `dotnet-bootstrapper.exe` could live either beside `dotnet.exe` or in `DOTNET_HOME` as a standalone AOT executable. The `dotnet.exe` already shells out to the .NET SDK if the SDK is available for unknown commands. If the executable is in `DOTNET_HOME`, it may have to be signature verified every time, because `dotnet.exe` can run under an administrator context. When calling this bootstrapper executable, administrator privileges should be revoked.

This bootstrapper would be acquired in the same fashion as `nvm`:
Via a website with a simple install script that downloads the executable.

The .NET SDK would also shell out to the bootstrapper executable for commands it does not know, aka `dotnet install`, `dotnet uninstall`, etc. This way the bootstrapper could automatically update itself, say for example when the `releases.json` url changed, this could be a breaking change requiring a self-update to function.

From a security context, it may be better to bundle this alongside the host in the same directory, for this would share the same security context.

In theory, this is 'doable' for .NET 10 in terms of changing the muxer to know about a specified bootstrapper location, which would enable these scenarios outside of just .NET 11.

### Have the .NET SDK 'Know Of' the Bootstrapper

To prevent changing the host or muxer, which is a C++ library that has significant impact to all tooling versions, we could instead rely on the SDK. In this scenario, the SDK would be aware of the location of a bootstrapper executable. The SDK would signature verify the bootstrapper executable and shell out to it for commands it is unaware of. The host shells out to the SDK for commands it is unaware of, so this would enable the entire experience.

An issue with this approach is for users with only a host and no SDK, but also with the bootstrapper, the commands would fail. The imagined experience in this case is that the bootstrapper is downloaded via a website as an executable. That executable would pop up an interactive terminal to setup the initial state by downloading an SDK. The older SDKs would not be aware of this behavior, so they would fail to run these commands.

### Utilize Dotnet-Foo Hack

### Create a C# Muxer

The muxer would become a bootstrapper and the muxer itself. It would cease to become C++ and become ported C# code that is run as native AOT code. This is high-risk and also likely still lower performance than a C++ application.


### Don't Interface as `dotnet`

To unblock this experience, we may select to interface as another application executable, say, `dotget`, with the future potential once this product stabilizes to follow the product of adding a binary that lives 'alongside' the muxer.
