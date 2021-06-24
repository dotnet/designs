# Flexible HTTP APIs

**DRAFT**

**Owner** [Cory Nelson](https://github.com/scalablecory) | [Geoff Kizer](https://github.com/geoffkizer)

This design doc describes a set of flexible, high-performance APIs for HTTP.

In shorthand, this may be referred to as "LLHTTP".

## Target customers

This API is intended for advanced users who need control or performance beyond what `HttpClient` offers. `HttpClient` would be kept as the feature-rich "easy" API.

Some specific scenarios this must satisfy:

- Opening connections without making a request.
- Making requests without using a connection pool.
- Ability to build a custom connection pool. Some features this requires:
    - Is a HTTP/2 stream available?
        - E.g. if there are multiple connections, and one is fully booked, try another.
    - Is GOAWAY being processed?
        - E.g. to remove connection from being eligible for new requests.
    - If an exception occurs, was the request possibly processed?
        - E.g. to allow retry on another connection.
- Retrieving statistics about the connection used for a request.
    - e.g. TCP EStats.
- Retrieving fine-grained timings for a request.
    - Connection established.
    - TLS handshake finished.
    - Response received.
    - Headers received.
    - Content received.
    - Trailing headers received.
    - Request finished.
- Tracking request and connection status.
    - TBD, see e.g. [gRPC status codes](https://github.com/grpc/grpc/blob/master/doc/statuscodes.md).
- Efficient and lossless forwarding (e.g. proxy).
- Better control over TLS handshakes.
    - TBD.
- The entirety of `SocketsHttpHandler` must be able to be built as a layer on top of this API.

## APIs

The APIs are split into three groups:

- Low-level APIs
- Compositional APIs
- High-level APIs

### Definitions

- `Span<byte>` indicates a contiguous blob of unvalidated `byte` and may be used as a placeholder for all its variants e.g. `ReadOnlyMemory<byte>`.

- `string` indicates a contiguous blob of `char` and may be used as a placeholder for all its variants e.g. `Span<char>`.

- For discussion sake (actual API TBD), we will consider that this API consists of an abstraction and concrete implementations:
    - `HttpConnection`, the abstraction.
    - `Http1Connection`, `Http2Connection`, `Http3Connection`, the low-level implementations.
    - `HttpRequest`, the abstraction over a request/response.

## Low-level API

Low-level APIs implement HTTP without any additional features or opinions. They return HTTP data direct to the user without transformation, and are built to be forward-compatible with new HTTP features (e.g. HTTP/2 and HTTP/3 extension frames) in the future.

Performance is of high priority:

- Operate on `Span<byte>` rather than `string` and `Uri`.
    - Parameters will not be validated (to enable efficient forwarding), but helpers will be exposed to allow user to perform validation.
- Zero allocations (steady-state).

Ease of use is an explicit non-goal:

- We assume users will know more about HTTP than `HttpClient` currently requires of them.
- It is okay if it takes more code to use than `HttpClient`.

### Connections

These APIs are constructed on top of `Stream`. This has some implications worth noting:

- Connections are not established by these APIs: the user must establish the connection.
- Connecting via a proxy, or over TLS is not done by this API: the user must pass in a `Stream` that already has these established.

### Headers

Headers are read and written "streaming", without going through a collection.

"Prepared" headers will enable dynamic table compression and pre-validation/serialization equivalent to existing internal "known header" concept of `SocketsHttpHandler`.

Huffman compression will be opt-in on a per-header basis.

### Content

Content will be read and written directly to the `HttpRequest`, without using `Stream` or a `HttpContent`-equivalent, to avoid the associated allocations.

Expect 100 Continue will not be implemented here, but full informational responses will be returned to allow the caller to implement their own Expect Continue processing.

## Compositional APIs

Compositional APIs are built on top of low-level APIs to provide additional features, as well as some opinionated implementations.

Every higher-level feature that `SocketsHttpHandler` currently supports should have an implementation here. This will mean one or more `HttpConnection` that implements some features:

- Automatic decompression.
- Automatic redirects.
- Connection pooling.
- Version negotiation.
    - ALPN, Upgrade header, ALT-SVC.
- Authentication.

As well as some additional APIs for working with the `HttpConnection` API:

- Header parsing/printing.
- Input validation.
- Proxy resolver.
    - "HTTP_PROXY" etc. environment variables.
    - WinInet settings.
- Proxy support.
    - HTTP proxy as `HttpConnection`.
    - CONNECT proxy as `Stream`.
    - SOCKS proxy as `Stream`.
- Wrapper to work with `HttpRequest` as a `Stream`.
- Expect 100 Continue handling.
- WebSockets.
- JSON helper extensions on top of `HttpConnection`.

## High-level APIs

High-level APIs are one or more `HttpMessageHandler` implementations for use with `HttpClient` allowing:

- A full `SocketsHttpHandler`-parity implementation.
    - Once we are confident in implementation and features, we would refactor the `SocketsHttpHandler` guts to use use this implementation.
- Wrapping a caller-supplied `HttpConnection` in a `HttpMessageHandler`.

## Timeline

| Feature                 | Phase 1 | Phase 2 | Future |
| ----------------------- | :-----: | :-----: | :----: |
| Client API              | x       |         |        |
| HTTP/1                  | x       |         |        |
| HTTP/2                  | x       |         |        |
| HTTP/3                  |         |         | x      |
| Prepared headers        |         |         | x      |
| Header compression      |         |         | x      |
| Automatic decompression |         | x       |        |
| Automatic redirect      |         | x       |        |
| Connection pooling      |         | x       |        |
| Version upgrades        |         | x       |        |
| Authentication          |         | x       |        |
| Header parsing/printing |         |         | x      |
| Proxy resolver          |         | x       |        |
| HTTP proxy              |         | x       |        |
| CONNECT proxy           |         | x       |        |
| SOCKS proxy             |         | x       |        |
| Stream wrappers         |         | x       |        |
| Expect 100 Continue     |         | x       |        |
| WebSockets              |         |         | x      |
| JSON extensions         |         |         | x      |
| HttpMessageHandler      | x       | x       | x      |
