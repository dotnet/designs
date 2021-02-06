# Flexible HTTP APIs

This design doc describes a set of flexible, high-performance APIs for HTTP.

## Target customers

This API is intended for advanced users who need control or performance beyond what `HttpClient` offers, and to eventually replace the `SocketsHttpHandler` implementation.

## APIs

The APIs are split into three groups:

- Low-level APIs
- Compositional APIs
- High-level APIs

## Low-level API

Low-level APIs implement HTTP without any additional features or opinions. They return HTTP data direct to the user without transformation, and are built to support new HTTP features (e.g. HTTP/2 and HTTP/3 extension frames) in the future.

This API consists of an abstraction and concrete implementations:

- `HttpConnection`, the abstraction.
- `Http1Connection`, `Http2Connection`, `Http3Connection`.

Performance is of high priority:

- Operate on `Span<byte>` rather than `string` and `Uri`.
    - Validation will only check for protocol correctness. Things like ASCII or URI validation will not be performed.
- Zero allocations (steady-state).

Ease of use is an explicit non-goal:

- We assume users will know more about HTTP than `HttpClient` currently requires of them.
- It is okay if it takes more code to use than `HttpClient`.

These APIs are constructed on top of `Stream`. This has some implications worth noting:

- Connections are not established by these APIs: the user must establish the connection.
- Connecting via a proxy, or over TLS is not done by this API: the user must pass in a `Stream` that already has these established.

"Prepared" headers will enable dynamic table compression and pre-validation/serialization equivalent to existing internal "known header" concept of `SocketsHttpHandler`.

Huffman compression will be opt-in on a per-header basis.

The API must be efficient for forwarding scenarios, as in reverse proxies.

## Compositional APIs

Compositional APIs are built on top of low-level APIs to provide additional features, as well as some opinionated implemenations.

This will mean a `HttpConnection` implementation implementing some features:

- Automatic decompression.
- Automatic redirects.
- Connection pooling.
- Version negotiation.
    - ALPN, Upgrade header, ALT-SVC.
- Authentication.

As well as some additional APIs for working with the `HttpConnection` API:

- Header parsing.
- Proxy implemenations
    - HTTP proxy as `HttpConnection`.
    - CONNECT proxy as `Stream`.
    - SOCKS proxy as `Stream`.
- Wrapper to work with `HttpConnection` as a `Stream`.
- JSON helper extensions on top of `HttpConnection`.

## High-level APIs

This is a `HttpMessageHandler` implementation for use with `HttpClient`. It will either wrap a user-supplied `HttpConnection`, or will provide `SocketsHttpHandler`-parity functionality.

Once we are confident in implementation and features, this would replace `SocketsHttpHandler`.

Expect 100 Continue will be implemented here.

## Timeline

| Feature                 | Phase 1 | Phase 2 | Future |
| ----------------------- | :-----: | :-----: | :----: |
| Client API              | x       |         |        |
| HTTP/1                  | x       |         |        |
| HTTP/2                  | x       |         |        |
| HTTP/3                  |         |         | x      |
| Prepared Headers        |         |         | x      |
| Header compression      |         |         | x      |
| Automatic decompression |         | x       |        |
| Automatic redirect      |         | x       |        |
| Connection pooling      |         | x       |        |
| Version upgrades        |         | x       |        |
| Authentication          |         | x       |        |
| Header parsing          |         |         | x      |
| HTTP proxy              |         | x       |        |
| CONNECT proxy           |         | x       |        |
| SOCKS proxy             |         | x       |        |
| Stream wrappers         |         | x       |        |
| JSON extensions         |         |         | x      |
| HttpMessageHandler      | x       | x       | x      |
|                         |         |         |        |
