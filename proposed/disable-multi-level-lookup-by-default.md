# Disable multi-level lookup by default

## Multi-level lookup

Running framework dependent application or SDK command performs a search for
install location of .NET (.NET Core or .NET 5+). If the user has private install
locations of .NET such search may result in multiple possible locations.
See [install-locations](https://github.com/dotnet/designs/blob/main/accepted/2020/install-locations.md)
for more details. On Windows all of these locations are considered when looking
for the right version of Runtime/SDK. This behavior is called "multi-level lookup".

An example is if the machine has a private install in
`C:\repo\.dotnet` (version 3.0) and a global install in
`C:\Program Files\dotnet` (version 3.1).
Running `C:\repro\.dotnet\dotnet.exe My30App.dll` will by default use
the runtime version 3.1 from `C:\Program Files\dotnet`,
even though it was started from the private install.

This behavior can be disabled by setting `DOTNET_MULTILEVEL_LOOKUP=0`.

Also of note is that this behavior is Windows specific, on non-Windows platforms
only the first location is considered (the behavior is identical to `DOTNET_MULTILEVEL_LOOKUP=0`).

## Feedback

There have been a steady stream of feedback from all sides about this behavior:

* It's often confusing: "I ran .NET from my private install, but it ended up
using the global one anyway". Frequently users end up setting
`DOTNET_MULTILEVEL_LOOKUP=0` globally on their machines to avoid this.
* Inconsistent behavior between Windows and non-Windows.
* Cause of breaks, typically in automated systems, where installing
a new version of .NET globally changes behavior of otherwise isolated builds/tests.
* Performance issues. These have shown up especially in CI environments
(e.g. GitHub Actions) because a particular version of the SDK is downloaded,
but a different version that happens to exist in the global location is used.
Because these VMs are I/O constrained, they end up crossing the IOPS threshold
and throttling, causing several second delays.

## Disable this by default for .NET 6+ applications

This is a proposal to change the default to disable multi-level lookup.

* Applications will change behavior based on the target .NET version.
This version will be determined by the `tfm` property in `.runtimeconfig.json`.
    * Applications targeting .NET 6 and above will act as if `DOTNET_MULTILEVEL_LOOKUP`
    is set to `0` by default.
    * Applications targeting .NET 5 and below (.NET Core 3.\*, .NET Core 2.\*, ...)
    will keep their current behavior which is as if `DOTNET_MULTILEVEL_LOOKUP`
    is set to `1`.
    * All applications will continue to obey an explicit setting of
    the `DOTNET_MULTILEVEL_LOOKUP` variable.
    * This behavior will be the same regardless of how the app was started
    (`dotnet.exe` muxer or `app.exe`).
* SDK invocation
    * When using .NET 6+ `dotnet.exe` to invoke SDK commands (like `dotnet build`)
    multi-level lookup is disabled by default. This means that running
    `C:\private.6.install\dotnet.exe build` will only look for SDKs in
    `C:\private.6.install` regardless of `global.json` settings.
    If the version requested by `global.json` is not found it will fail.
* Muxer commands
    * Running `dotnet --info`, `dotnet --list-runtimes` and `dotnet --list-sdks`
    with .NET 6+ `dotnet.exe` will also disable multi-level lookup by default.
    This may be considered a visible discrepancy. It will be possible to run
    .NET 5 app via a private install `dotnet.exe`, but running `dotnet --list-runtimes`
    will not list .NET 5 runtime as available.
    * It might make sense to include a special message in the output of `dotnet --info`
    which would effectively warn about this discrepancy. The output of `dotnet --info`
    is meant to be humanly readable text and it already includes for example links
    to install more SDKs so adding another message should have minimal risk
    of breaking anything. `dotnet --list-runtimes` and `dotnet --list-sdks`
    on the other hand are not formatted for humans and should not include
    any additional message.
* Native hosting APIs
    * All hosting API scenarios have `.runtimeconfig.json` available and so will
    act as applications - they will react to target framework version
    as described above.
    * `hostfxr_get_dotnet_environment_info` and `hostfxr_get_available_sdks`
    would behave the same as `dotnet --list-runtimes` and `dotnet --list-sdks` -
    so multi-level lookup is off by default on `hostfxr` .NET 6+.
    * `hostfxr_resolve_sdk`, `hostfxr_resolve_sdk2` - similar to the enumeration
    APIs these will use multi-level lookup off by default when running in
    `hostfxr` .NET 6+.
    * `nethost`'s `get_hostfxr_path` API to find `hostfxr` does not react
    to multi-level lookup in any way (effectively disabled always) and as such
    is not affected by this change. This means that using private install location
    will always run the `hostfxr` from the private install location, with no way
    to allow multi-level lookup for `hostfxr` (existing behavior).

## UX changes

We want to inform the user about this change in some way in cases where it's
permissible to do so. This means additional messages should not be printed when
running applications since the app may depend on exact output format. As such
the additional information should only be provided when running CLI commands (SDK).

Host should print out a similar message when enumerating runtimes and SDKs in
`dotnet --info`. As mentioned above `dotnet --list-runtimes` and `dotnet --list-sdks`
should not be affected.

The message should only be shown when there's observable difference in behavior.
For CLI commands that means when running the command with MLL on-by-default
would end up using different version of SDK then when running it with
MLL off-by-default. `dotnet --info` should print the message in all cases where
there's a global install location (on top of the private one which is used
to run the command).

## Open questions

### New dotnet CLI actions for install/update

This is effectively an extension to the discrepancy between app behavior and
`dotnet --info` behavior. What is the desired behavior for the new CLI actions -
this is yet to be determined.

### Way to dismiss informational message about the change

There should be some way to dismiss the information messages printed by
CLI commands if they are affected.

1. Require the user to set `DOTNET_MULTILEVEL_LOOKUP=0` - which will disable
   the message.
1. Only show the message once (would require a way to remember that it was shown,
   similar to first run CLI experience).
1. Introduce some other mechanism (new env. variable?)

I personally don't like either option. 2 is too restrictive and might make
the message effectively useless. 1 feels counterproductive as it basically goes
back to current state where setting `DOTNET_MULTILEVEL_LOOKUP=0` is very common.
3 feels even worse as it introduces yet another env. variable to set.

### Hosting APIs accept install location as parameter

Currently most hosting APIs take a parameter which specifies the "dotnet root",
so that they can behave as if one used `dotnet` from that location.
But the APIs themselves are implemented in `hostfxr` which can be of any version.
This is already a niche problem (if a really old `hostfxr` is used to run things
on a really new private install the behavior is a bit problematic).
With this change such discrepancy would become more apparent.
So the question is to which version should the APIs react to:

1. Use the version of the `hostfxr` itself - this would be similar to the above
   described behavior of reacting to the version of the `dotnet.exe` itself,
   but it might be a bit unexpected.
1. Use the version of the specified "dotnet root" - this would be more in line
   with what the "dotnet root" parameter tries to convey. This might be technically
   a bit challenging in some cases.

I think it should be OK to go with option 1 - rely on the version of `hostfxr` -
but this will need more detailed investigation.

## Alternative solutions

### Off by default everywhere

In the above, there's a discrepancy between application and SDK behavior.
SDK will always have multi-level lookup disabled by default,
even if the projects asks for a lower version (via `global.json`).
On the other hand apps, which ask for a lower version (via `.runtimeconfig.json`)
will get a backward compatible behavior - multi-level lookup on by default.

It might be a good idea to make the behavior consistent and always treat
multi-level lookup as off by default. The impact would be that trying to run
applications targeting .NET 5 or lower will NOT run if executed on
a private install of .NET 6.

* Pros - consistent behavior across all use cases
* Cons - more breaking behavior

SDK is generally backward compatible on its own, that's why we always pick
the latest version available if not specified otherwise. `global.json` is
the override mechanism which lets users specify more precise version requirements.
That said having `global.json` and also using a private install location is
typically a very explicit action, and so it's reasonable to assume users
will keep these consistent.

Doing the same for applications is a tougher proposition because they're not
governed by one `global.json` (typically repos or solutions have only one `global.json`),
instead they're governed by their own `.runtimeconfig.json` which may
(and usually are) targeting multiple versions even from one project/repo.

### Off by default everywhere and auto-roll-forward

Same as above - multi-level lookup is always off by default, for all applications,
but automatically roll forward .NET 5- applications to the available runtime.
By default applications don't allow roll forward across major versions.
So typically application targeting .NET 5 will not run on .NET 6 runtime,
unless explicitly overridden by command-line or environment variable -
[detailed spec](https://github.com/dotnet/designs/blob/main/accepted/2019/runtime-binding.md).
This would mean that running .NET 5 application on a .NET 6 private install fails.

We could implement a change where for this specific case we allow roll-forward
over major version even if the application didn't ask for it.

* Pros - applications continue to run as before
* Cons - risk of breaking the applications via breaking changes in the runtime
  introduced across major versions

There are potential tweaks to this:

* App may not specify roll-forward policy at all (implies `Minor`) -
  in which case it might make more sense to auto-roll-forward
* App specifies a more restrictive policy then default (for example
  `LatestPatch`) -in which case it would make sense to not auto-roll-forward -> fail.
