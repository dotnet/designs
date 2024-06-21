# Add ability to embed install location options in apphost

There have been numerous requests around being able to customize how/where the
.NET root path will be determined for a an application. This document describes
a proposed mechanism for basic configuration of how `apphost` will search for
the .NET install location.

Goals:

- Enable developers to build an app and configure it such that it can:
    - Only consider global .NET installs
    - Always look at a relative path for a .NET install
- Create a building block for a fuller, SDK-supported experience that should fit
in regardless of where we land for the full experience

Non-goals:

- SDK support for layout and deployment of the app with a runtime files in a
corresponding relative path
- SDK and project system experience for building a set of applications using the
same custom runtime install location

Related:

- [dotnet/runtime#2572](https://github.com/dotnet/runtime/issues/2572)
- [dotnet/runtime#3453](https://github.com/dotnet/runtime/issues/3453)
- [dotnet/runtime#53834](https://github.com/dotnet/runtime/issues/53834)
- [dotnet/runtime#64430](https://github.com/dotnet/runtime/issues/64430)
- [dotnet/runtime#70975](https://github.com/dotnet/runtime/issues/70975)
- [dotnet/runtime#86801](https://github.com/dotnet/runtime/issues/86801)

## State in .NET 8

The current state is described in detail in
[install-locations](https://github.com/dotnet/designs/blob/main/accepted/2020/install-locations.md)
and [install-locations-per-architecture](https://github.com/dotnet/designs/blob/main/accepted/2021/install-locations-per-architecture.md)

At a high level, the process and priority for `apphost` determining the install
location is:

  1. App-local
      - Look for the runtime in the app's folder (self-contained apps)
  2. Environment variables
      - Read the `DOTNET_ROOT_<arch>` and `DOTNET_ROOT` environment variables
  3. Global install (registered)
      - Check for a registered install - registry key on Windows or a install
      location file on non-Windows
  4. Global install (default)
      - Fall back to a well-known default install location based on the platform

## Embedded install location options for `apphost`

Every `apphost` already has a placeholder that gets rewritten to contain the app
binary path during build (that is, `dotnet build`). This proposal adds another
placeholder which would represent the optional configuration of search locations
and embedded relative path to an install location. Same as existing placeholder,
it would conditionally be rewritten during build based on the app's settings.
This is similar to the proposal in [dotnet/runtime#64430](https://github.com/dotnet/runtime/issues/64430),
but with additional configuration of which search locations to use.

### Configuration of search locations

Some applications do not want to use all the default search locations when they
are deployed - for example, [dotnet/runtime#86801](https://github.com/dotnet/runtime/issues/86801).

We can allow selection of which search locations the `apphost` will use. When
search locations are configured, only the specified locations will be searched.

```c
enum search_location
{
    sl_default = 0,
    sl_app_local = 1 << 0,
    sl_embedded = 1 << 1,
    sl_environment_variables = 1 << 2,
    sl_global = 1 << 3,
};
```

The behaviour corresponding to the `default` value is defined by the `apphost`.
It is currently `app_local | environment_variables | global`.

The search location could be specified via a property in the project:

```xml
<AppHostDotNetSearch>Global</AppHostDotNetSearch>
```

where the valid values are `AppLocal`, `Embedded`, `EnvironmentVariables`, or
`Global`. Multiple values can be specified, delimited by semi-colons.

When a value is specified, only those locations will be used. For example, if
`Global` is specified, `apphost` will only look at global install locations, not
app-local, at any embedded path, or at environment variables.

### Embedded relative path to install location

When a relative path is embedded and the `apphost` is [configured to look at it](#configuration-of-search-locations),
that path will be used as the .NET install root when running the application.

The install location could be specified via a property in the project:

```xml
<AppHostDotNetRoot>./path/to/runtime</AppHostDotNetRoot>,
```

Setting this implies `AppHostDotNetSearch=Embedded`. If `AppHostDotNetSearch` is
explicitly set to a value that does not include `Embedded`, `AppHostDotNetRoot`
is meaningless - the SDK will not write the relative path into the `apphost`
and the `apphost` will not check for an embedded relative path.

## Updated behaviour

At a high level, the updated process and priority for `apphost` determining the
install location would be:

  1. App-local, if search location not configured
      - Look for the runtime in the app's folder (self-contained apps)
  2. Embedded, if specified as a search location
      - Use the path embedded into `apphost`, relative to the app location
  3. Environment variables, if search location not configured or if set as a
  search location
      - Read the `DOTNET_ROOT_<arch>` and `DOTNET_ROOT` environment variables
  4. Global install (registered), if search location not configured or if set as
  a search location
      - Check for a registered install - registry key on Windows or a install
      location file on non-Windows
  5. Global install(default), if search location not configured or if set as a
  search location
      - Fall back to a well-known default install location based on the platform

Be default - that is, without any embedded install location options - the
effective behaviour remains as in the [current state](#state-in-net-8).

## Considerations

### Writing embedded options on build

This proposal writes the install location options in the `apphost` on `build`.
Without SDK support for constructing the layout, this means that if a developer
specifies an embedded relative install location path, it is up to them to create
a layout with the runtime in the expected location relative to the app output as
part of their build. Otherwise, running the app immediately after build would
not work. Another possible option is to only rewrite on `publish`, but that may
introduce another confusing difference between `build` and `publish`. Currently,
the `apphost` is the same between `build` and `publish` (with the exception of
single-file, which is only a `publish` scenario).

### Other hosts

This proposal is only for `apphost`. It is not relevant for `singlefilehost`, as
that has the runtime itself statically linked. Other hosts (`comhost`,`ijwhost`)
are also not included. In theory, other hosts could be given the same treatment,
but we do not have feedback for scenarios using them. They also do not have the
app-local search or the existing requirment of being partially re-written via a
known placeholder. Changing them would add significant complexity without a
compelling scenario. If we see confusion here, we could add an SDK warning if
`AppHostDotNetRoot` or `AppHostDotNetSearch` is set for those projects.
