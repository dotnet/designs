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
`BinaryData`. Curious what this is, she decides to new one up. Since there is no
constructor, she explores the static members. Naomi realizes that there are
several `FromXxx` methods. "Hmm, I need to serialize my object somehow", she
concludes that `FromObjectAsJson` is the right API for her.

```C#
Task QueueImageResize(string imageUrl)
{
    var queueClient = new QueueClient(userSecret.QueueConnectionString, "resize-queue");

    var message = new ResizeImageMessage
    {
        Url = imageUrl,
        Resolutions = new[] {"200x200", "400x400", "1200x1200" }
    };

    var messageData = BinaryData.FromObjectAsJson(message);
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
* Make it easy for people to convert bytes to common the same representations
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
(Microsoft.Extensions, ASP.NET Core, Azure SDK etc). While that might be
unfortunate, we believe it's acceptable because most of the usability gains
would be in higher levels anyway.

Thus, we shouldn't think of this as an exchange type in the BCL. We already have
exchange types; the point of this type is unify them by providing conversions;
the point isn't to provide yet another type. Thus, we don't see the need to, for
example, provide new virtual methods on Stream.

We have two options to design the type:

1. We could decide to model the construction via JSON serialization as a factory
   method. This ensures we can easily support additional serialization formats
   as necessary and remain linker friendly.

2. But we need to balance implementation flexibility concerns with usability.
   The primary goal of this type is to provide usability via conveniences. If we
   don't make the type super usable, the feature will fail. If we feel that the
   factories aren't discoverable enough, we should combine it with constructors,
   even if that reduces our flexibility.

### Shipping vehicle

**Assembly**: System.Binary.Data.dll
**NuGet Package**: System.Binary.Data (stable)

### API

```C#
namespace System
{
    public sealed class BinaryData
    {
        public static BinaryData FromBytes(ReadOnlyMemory<byte> data);
        public static BinaryData FromBytes(ReadOnlySpan<byte> data);
        public static BinaryData FromBytes(byte[] data);
        public static BinaryData FromObjectAsJson<T>(T jsonSerializable, CancellationToken cancellationToken = default);
        public static Task<BinaryData> FromObjectAsJsonAsync<T>(T jsonSerializable, CancellationToken cancellationToken = default);
        public static BinaryData FromStream(Stream stream);
        public static Task<BinaryData> FromStreamAsync(Stream stream, CancellationToken cancellationToken = default);
        public static BinaryData FromString(string data);
        public static implicit operator ReadOnlyMemory<byte>(BinaryData data);
        public ReadOnlyMemory<byte> ToBytes();
        public T ToObjectFromJson<T>(CancellationToken cancellationToken = default);
        public ValueTask<T> ToObjectFromJsonAsync<T>(CancellationToken cancellationToken = default);
        public Stream ToStream();
    }
}
```

### API (Hybrid)

```C#
namespace System
{
    public readonly struct BinaryData
    {
        public BinaryData(ReadOnlySpan<byte> data);
        public BinaryData(byte[] data);
        public BinaryData(object jsonSerializable, Type? type = null);
        public BinaryData(ReadOnlyMemory<byte> data);
        public BinaryData(string data);
        public static BinaryData FromBytes(ReadOnlyMemory<byte> data);
        public static BinaryData FromBytes(ReadOnlySpan<byte> data);
        public static BinaryData FromBytes(byte[] data);
        public static BinaryData FromObject<T>(T jsonSerializable, CancellationToken cancellationToken = default);
        public static Task<BinaryData> FromObjectAsync<T>(T jsonSerializable, CancellationToken cancellationToken = default);
        public static BinaryData FromStream(Stream stream);
        public static Task<BinaryData> FromStreamAsync(Stream stream, CancellationToken cancellationToken = default);
        public static BinaryData FromString(string data);
        public static implicit operator ReadOnlyMemory<byte>(BinaryData data);
        public ReadOnlyMemory<byte> ToBytes();
        public T ToObject<T>(CancellationToken cancellationToken = default);
        public ValueTask<T> ToObjectAsync<T>(CancellationToken cancellationToken = default);
        public Stream ToStream();
        [EditorBrowsable(EditorBrowsableState.Never)]
        public override bool Equals(object? obj);
        [EditorBrowsable(EditorBrowsableState.Never)]
        public override int GetHashCode();
        public override string ToString();
    }
}
```
