# .NET 8.0 Polyfill

**Owner** [Immo Landwerth](https://github.com/terrajobst)

This document covers which APIs we intend to ship for older versions of .NET,
which includes .NET Standard and .NET Framework.

## Polyfills

| Polyfill     | Assembly                   | Package                    | Existing? | API                    | Contacts                   |
| ------------ | -------------------------- | -------------------------- | --------- | ---------------------- | -------------------------- |
| TimeProvider | Microsoft.Bcl.TimeProvider | Microsoft.Bcl.TimeProvider | No        | [dotnet/runtime#36617] | [@tarekgh] [@geeknoid]     |

* `TimeProvider`. This type is an abstraction for the current time and time
  zone. In order to be useful, it's an exchange type that needs to be plumbed
  through several layers, which includes framework code (such as `Task.Delay`)
  and user code.

[@tarekgh]: https://github.com/tarekgh
[@geeknoid]: https://github.com/geeknoid
[dotnet/runtime#36617]: https://github.com/dotnet/runtime/issues/36617
