# .NET 8.0 Polyfill

**Owner** [Immo Landwerth](https://github.com/terrajobst)

This document covers which APIs we intend to ship for older versions of .NET,
which includes .NET Standard and .NET Framework.

## Polyfills

| Polyfill     | Assembly                   | Package                    | Existing? | API                    | Contacts                   |
| ------------ | -------------------------- | -------------------------- | --------- | ---------------------- | -------------------------- |
| TimeProvider | Microsoft.Bcl.TimeProvider | Microsoft.Bcl.TimeProvider | No        | [dotnet/runtime#36617] | [@tarekgh] [@geeknoid]     |
| IPNetwork    | Microsoft.Bcl.IPNetwork    | Microsoft.Bcl.IPNetwork    | No        | [dotnet/runtime#79946] | [@antonfirsov] [@geeknoid] |

* `TimeProvider`. This type is an abstraction for the current time and time
  zone. In order to be useful, it's an exchange type that needs to be plumbed
  through several layers, which includes framework code (such as `Task.Delay`)
  and user code.

* `IPNetwork`. It's a utilitarian type that is used across several parties. Not
  necessarily a critical exchange type but if we don't ship it downlevel,
  parties (such as [@geeknoid]'s team) will end up shipping their own copy and
  wouldn't use the framework provided type, even on .NET 8.0.

[@tarekgh]: https://github.com/tarekgh
[@geeknoid]: https://github.com/geeknoid
[@antonfirsov]: https://github.com/antonfirsov
[dotnet/runtime#36617]: https://github.com/dotnet/runtime/issues/36617
[dotnet/runtime#79946]: https://github.com/dotnet/runtime/issues/79946
