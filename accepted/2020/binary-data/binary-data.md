# Type for holding & converting binary data

**PM** [Immo Landwerth](https://github.com/terrajobst) |
[GitHub Issue](https://github.com/dotnet/runtime/issues/41686)

In the BCL we have many representations for binary data such as `byte[]`,
`Span<byte>`, and `Memory<byte>`. This isn't helped by they fact that there are
also multiple ways how one can produce a binary payload for a `string` or
`object`.

The Azure SDK has made the experience in user studies that customers are often
struggling to find the right method, or even any, to produce the data from the
data they already have.

## Scenarios and User Experience

### Sending a binary payload

Naomi is building a cloud-based solution for batch resizing of images. She uses
Azure Storage Queues to manage requests for image resizing. In order to queue a
message, she uses the `SendMessageAsync` API. The method takes a type called
`BinaryData`. Curious what this is, she decides to new one up. Since there is a
constructor that takes an object and it works for her:

```C#
Task QueueImageResize(string imageUrl)
{
    var queueClient = new QueueClient(userSecret.QueueConnectionString, "resize-queue");

    var message = new ResizeImageMessage
    {
        Url = imageUrl,
        Resolutions = new[] {"200x200", "400x400", "1200x1200" }
    };

    var messageData = new BinaryData(message);
    await queueClient.SendMessageAsync(messageData);
}
```

### Retrieving a binary payload

Shirin works on Naomi's team but is responsible for the resizing backend. She
wrote an Azure Function that is triggered every time an item is added to the
queue. The template already created a handler `RunAsync` that passes the queue
item as an instance of `BinaryData`.

In order to deserialize, she simply invokes the `ToObjectFromJson` method:

```C#
Task RunAsync(BinaryData messageData)
{
    var message = messageData.ToObjectFromJson<ResizeImageMessage>();

    foreach (var resolution in message.Resolutions)
    {
        var newUrl = GetUrl(resolution.Url, resolution);
        var image = await DownloadImage(resolution.Url);
        image.Resize(resolution);
        await UploadImageAsync(newUrl, image);
    }
}
```

## Requirements

### Goals

* Stable API
* Make it easy for people to construct bytes from common data types (byte
  arrays, spans, memories, string, JSON serialized objects)
* Make it easy for people to convert bytes to common data types of the same
  representations
* Provide opinionated defaults in order to reduce complexity
    - String encoding is assumed to be UTF8
    - Object serialization is done with `System.Text.Json`, with specific options

### Non-Goals

* Allocation free zero-copying semantics. The primary goal is convenience and
  usability, not perf. Performance critical scenarios should be enabled by
  providing overloads with the existing low-level concepts, such as span.

## Design

We're concerned with hard coding the type to `System.Text.Json`, but we
acknowledge that defaults are very valuable. If we need to support other
serializers we can provide additional arguments or APIs.

This type can't live in corlib because it would depend on `System.Text.Json`
(and whatever future serializers it needs to support). Hence, most areas in the
BCL wouldn't be able to take `BinaryData`, but technologies above the BCL could
(Microsoft.Extensions.*, ASP.NET Core, Azure SDK etc). While that might be
unfortunate, we believe it's acceptable because most of the usability gains
would be in higher levels anyway.

The type isn't meant to unify different representations. Rather, it's a helper
to make it easier for users to convert data. Thus, it's not meant as
high-performance exchange type, like span (it is implicitly convertible to
`ReadOnlyMemory<byte>` and `ReadOnlySpan<byte>`).

The type is for "user data", that is, the contract for the API is "give me a
blob of binary data". It's not for protocol-like APIs where the underlying
encoding is part of the API contract, e.g. a REST API.

We don't see the need to, for example, provide new virtual methods on Stream or
`HttpClient`.

Please note: Neither the constructors nor the factories copy data -- they wrap
the input data, so long it's already binary. Anything that requires
transcoding/serializtion (e.g. string, object) isn't wrapped. While the goal
isn't to be high performant, the goal is still to not be wasteful. Idiomatic use
of the type shouldn't inflict copies.

### Shipping vehicle

**Assembly**: System.BinaryData.dll

**NuGet Package**: System.BinaryData (stable)

### API

```C#
namespace System
{
    public class BinaryData
    {
        public BinaryData(byte[] data);
        public BinaryData(object jsonSerializable, JsonSerializerOptions options = default, Type? type = null);
        public BinaryData(ReadOnlyMemory<byte> data);
        public BinaryData(string data);
        public static BinaryData FromBytes(ReadOnlyMemory<byte> data);
        public static BinaryData FromBytes(byte[] data);
        public static BinaryData FromObjectAsJson<T>(T jsonSerializable, JsonSerializerOptions options = default, CancellationToken cancellationToken = default);
        public static BinaryData FromStream(Stream stream);
        public static Task<BinaryData> FromStreamAsync(Stream stream, CancellationToken cancellationToken = default);
        public static BinaryData FromString(string data);
        public static implicit operator ReadOnlyMemory<byte>(BinaryData data);
        public static implicit operator ReadOnlySpan<byte>(BinaryData data);
        public ReadOnlyMemory<byte> AsBytes();
        public T ToObjectFromJson<T>(JsonSerializerOptions options = default, CancellationToken cancellationToken = default);
        public Stream ToStream();
        [EditorBrowsable(EditorBrowsableState.Never)]
        public override bool Equals(object? obj);
        [EditorBrowsable(EditorBrowsableState.Never)]
        public override int GetHashCode();
        public override string ToString();
    }
}
```
