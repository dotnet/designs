# Overview
March 11th, 2021.

This document covers the API and design for a writable DOM along with support for the [C# `dynamic` keyword](https://docs.microsoft.com/en-us/dotnet/framework/reflection-and-codedom/dynamic-language-runtime-overview).

It is expected that a significant percent of existing System.Text.Json consumers will use these new APIs, and also attract new consumers including:
- A need for a lightweight, simple API especially for one-off cases.
- A need for `dynamic` capabilities for varying reasons including sharing of loosely-typed, script-based code.
- To efficiently read or modify a subset of a large tree. For example, it is possible to efficiently navigate to a subsection of a large tree and read an array or deserialize a POCO from that subsection. LINQ can also be used with that.
- Those unable or unwilling to use the serializer for varying reasons:
  - Too heavyweight; requires compilation of POCO types.
  - Limitations in the serializer such as polymorphism or to address denormalization scenarios.
  - JSON schema is not fixed and must be inspected.
- Those wanting to extend serializer capabilities within a custom converter. For example, to work around serializer limitations such as property ordering or flexible POCO constructors. In this case, the `Read()` and\or `Write()` methods can use nodes instead of or in addition to POCOs.

A prototype for 6.0 is available at https://github.com/steveharter/runtime/tree/WriteableDomAndDynamic.

## High-level API
Represented by an abstract base class `JsonNode` along with derived classes for objects, arrays and values:
```cs
namespace System.Text.Json.Node
{
    public abstract class JsonNode {...};
    public sealed class JsonObject : JsonNode, IDictionary<string, JsonNode?> {...}
    public sealed class JsonArray : JsonNode, IList<JsonNode?> {...};
    public abstract class JsonValue : JsonNode {...};
    public sealed class JsonValue<T> : JsonValue {...};
}
```

## Background
The existing `JsonDocument` and `JsonElement` types represent the DOM support today which is read-only. `JsonDocument` maintains a single immutable UTF-8 buffer and returns `JsonElement` value types from that buffer on demand. That design minimizes the initial `JsonDocument.Parse()` time and associated heap allocs but also makes it slow to re-obtain the same value (which is common with LINQ) and does not lend itself to being directly extended to support writability.

Although the internal UTF-8 buffer will continue to be immutable with the design proposed here, a `JsonElement` will indirectly support writability by forwarding serialization to a linked `JsonNode`. When a linked `JsonElement` is serialized, it will forward to the `JsonNode`. This `JsonNode` interop is not intended as the primary "writeable DOM" API, but is useful for scenarios that already use `JsonElement`.

Currently there is no direct support for `dynamic` in System.Text.Json. Adding support for that implies adding a writeable DOM. The design proposed here considers both `dynamic` and non-`dynamic` scenarios for a writeable DOM which allows for a common API, shared code and intuitive interop between the two.

This design is based on learning and scenarios from these reference implementations:
- The writable DOM prototype.
  - During 5.0, there was a [writable DOM effort](https://github.com/dotnet/runtime/issues/30436). The design centered around writable aspects, usability and LINQ support. It was shelved due to time constraints and outstanding work.
     - What's the same as the design proposed here:
       - The high-level class names `JsonNode`, `JsonArray` and `JsonObject`.
       - Interaction with `JsonElement`.
     - What's different:
       - `JsonValue` instead of `JsonString`, `JsonNumber` and `JsonBoolean`.
       - The writable `JsonValue` internal value is based on CLR types, not a string representing JSON. For example, a `JsonValue` initialized with an `int` keeps that `int` value without boxing or converting to a string-based field.
- The dynamic support code example. During 5.0, [dynamic support](https://github.com/dotnet/runtime/issues/29690) was not able to be implemented due to schedule constraints. Instead, a [code example](https://github.com/dotnet/runtime/pull/42097) was provided that enables `dynamic` and a writeable DOM that can be used by any 3.0+ runtime in order to unblock the community and also has been useful in gathering feedback for the design proposed here.
- Azure prototyping. The Azure SDK team needs a writable DOM, and supporting `dynamic` is important for some scenarios but not all. A [prototype](https://github.com/Azure/azure-sdk-for-net/tree/master/sdk/core/Azure.Core.Experimental) has been created. Work is also being coordinated with the C# team to support Intellisense with `dynamic`, although that is not expected to be implemented in time to be used in 6.0.
- Newtonsoft's Json.NET. This has a similar `JToken` class hierarchy and support for `dynamic`. The `JToken` hierarchy includes a single `JValue` type to represent all value types, like is being proposed here (instead of separate types for string, number and boolean). Json.NET also has implicit and explicit operators similar as to what is being proposed here.
  - Converting code using Json.NET to System.Text.Json should be fairly straightforward although not all Json.NET features are implemented. See the "Features not proposed in first round" section for more information.

## Layering
During deserialization, `JsonNode` uses `JsonElement`.

During serialization, `JsonNode` uses `JsonElement` if the values are backed by `JsonElement` otherwise `JsonNode` uses `JsonSerializer`.

Layering on `JsonSerializer` is necessary to support `dynamic` since arbitrary CLR types, POCOs, collections, anonymous types, etc can be assigned to dynamic object properties and array elements and these are expected to serialize. This layering also supports the use serialization features including custom converters that support (de)serialization of custom data types.

## Noteworthy features
- Support for any CLR type in "edit" mode. This makes generating JSON easy for calling services, etc.
  - Support for custom data types registered with `JsonSerializerOptions`.
  - Support for C# Anonymous types which works with `JsonSerializerOptions` including quoted numbers and property naming policies.
  - Support for `dynamic` which can overlap with `JsonNode` methods including `GetValue()` etc.
  - Support for all POCOs and collection types by using `JsonValue<T>`.
- Performance
  - A `JsonNode.Parse()` method is based on `JsonElement` which is very efficient: a single alloc to maintain entire JSON buffer and no materialized child elements.
  - Obtaining primitive values from backing `JsonElement` is very efficient: delayed creation of strings etc until `GetValue()` is called.
  - After `Parse()`, it is possible to navigate to a deep child node with minimal allocations: only the nodes navigated are created, and those are back by `JsonElement`.
  - Can work with `JsonDocument` dispose pattern and pooled buffers to prevent the potentially large JSON alloc.
- Programming model
  - Common programming model to obtain values whether backed by a `JsonElement` after `Parse()` or backed by an actual CLR type in "edit" mode. e.g. `jvalue.GetValue<int>()` works in either case.
  - A POCO property or collection element can be a `JsonNode` or a derived type.
  - The extension data property (to capture extra JSON properties that don't map to a CLR type) can now be `JsonObject` instead of `JsonElement`.
  - Ability to deserialize `System.Object` as `JsonNode` instead of `JsonElement`.
  - Interop from `JsonElement` to `JsonNode` via `JsonElement.AsNode()`.
- Debugging
  - `ToString()` returns JSON for easy inspection (same as `JsonElement.ToString()`).
  - `GetPath()` to determine where a given node is at in a tree. This is also used to provide detail in exceptions.
- LINQ
  - `IEnumerable<JsonNode>`-based `JsonObject` and `JsonNode`.
  - `Parent` and `Root` properties to support querying against relationships.

## API walkthrough
A deserialized `JsonNode` value internally holds a `JsonElement` which knows about the JSON kind (object, array, number, string, true, false) and the raw UTF-8 value including any child nodes (for a JSON object or array).

When the consumer obtains a primitive value through `jvalue.GetValue<double>()`, for example, the CLR `double` value is deserialized from the raw UTF-8. Thus the mapping between UTF-8 JSON and the CLR object model is deferred until manually mapped by the consumer by specifying the expected type.

Deferring is consistent with existing `JsonElement` semantics where, for example, a JSON number is not automatically mapped to any CLR type until the consumer calls a method such as `JsonElement.GetDecimal()` or `JsonElement.GetDouble()`. This design is preferred over eager "guessing" what the CLR type should be based upon the contents of the JSON such as whether the JSON contains a decimal point or the presence of an exponent. Guessing can lead to issues including overflow\underflow or precision loss.

In addition to deferring the creation of numbers in this manner, the other node types are also deferred including strings. In addition, `JsonObject` and `JsonArray` instances are not populated with child nodes until traversed. Using `JsonElement` in this way along with deferred creation of values and child nodes greatly improves CPU performance and reduces allocations especially when a subset of a tree is traversed.

Simple deserialization example:
```cs
JsonNode jObject = JsonNode.Parse("{""MyProperty"":42}");
JsonValue jValue = jObject["MyProperty"];

// Verify the contents
Debug.Assert(jObject is JsonObject);
Debug.Assert(jValue is JsonValue<JsonElement>); // On deserialize, the value is JsonElement
int i = (int)jValue; // Same programming model as the sample below; shortcut for "jValue.GetValue<int>()"
Debug.Assert(i == 42);
```

To mutate the value, a new node instance must be created:
```cs
jObject["MyProperty"] = 43;

// Verify the contents
JsonValue jValue = jObject["MyProperty"];
Debug.Assert(jValue is JsonValue<int>);
int i = (int)jValue; // Same programming model as the sample above.
Debug.Assert(i == 43);
```

Note that in both cases an explicit cast to `int` is used: this is important because it means there is a common programming model for cases when a given `JsonValue<T>` is backed by `JsonElement` (for read mode \ deserialization) or backed by an actual value such as `int` (for edit mode or serialization). In this example, the consumer always knows that the "MyProperty" number property can be returned as an `int` when given a `JsonValue` and doesn't have to be concerned about whether it is backed by an `int` or `JsonElement`.

An explicit operator was used in the above example:
```cs
int i = (int)jValue;
```
which expands to:
```cs
int i = jValue.GetValue<int>();
```

An implicit operator was used in the above example:
```cs
jObject["MyProperty"] = 43;
```
which expands to:
```cs
jObject["MyProperty"] = new JsonValue<int>(43);
```

The `JsonValue<T>.Value` property can also have its internal value changed without having to create a new instance:
```cs
var jValue = JsonValue<int>(43);
jValue.Value = 44;
```
although the type itself (`int` in this example) can't be changed without creating a new `JsonValue<T>` where `<T>` is `int`.

A `JsonValue` supports custom converters that were added via `JsonSerializerOptions.Converters.Add()` or specified in the `JsonValue<T>()` constructor. A common use case would be to add the `JsonStringEnumConverter` which serializes `Enum` values as a string instead of an integer, but also supports user-defined custom converters:
```cs
JsonNode jObject = JNode.Parse("{\"Amount\":1.23}");
JsonValue jValue = jObject["Amount"];
Money money = jValue.GetValue<Money>(options);
```

The call to `GetValue<Money>()` above calls the custom converter for `Money`.

During serialization, some features specified on `JsonSerializerOptions` are supported including quoted numbers and null handling. See the "Interop" section.

Serialization can be performed in several ways:
- Calling `string json = jNode.ToJsonString()`.
- Calling `jNode.WriteTo(utf8JsonWriter)`
- Assigning a node to a POCO or adding to a collection and serializing that.
- Using the existing `JsonSerializer.Serialize<JsonNode>(jNode)` methods.
- Obtaining a `JsonElement` from a node and serializing that through `jElement.WriteTo(utf8JsonWriter)`.

# API changes based on previous API review on 3/2/2021
An assumption is that an explicit model is supported but not acceptable for tree traversal:
```cs
    int i = ((JsonArray((JsonObject)((JsonObject)jNode)["Child"])["Array"])[1].GetValue<int>();
```

Compare that explicit model to using `dynamic`:
```cs
    int i = jNode.Child.Array[1];
```

The `JsonNode` API proposed here aligns with the `dynamic` syntax above plus and makes the most common scenarios look like:
```cs
    int i = jNode["Child"]["Array"][1].GetValue<int>();
    // or
    int i = (int)jNode["Child"]["Array"][1];
```

This terse syntax works by adding the following helper methods to JsonNode:
```cs
    public virtual JsonNode? this[int index] { get; set; } // for JsonArray
    public virtual JsonNode? this[string propertyName] { get; set; } // for JsonObject
    public virtual TValue? GetValue<TValue>(); // for JsonValue
```

However, no other helper methods such `.Add()` is present (for `JsonArray`).

Adding "As*()" methods would allow easier navigation to `JsonArray.Add()` and others without casting. For example, here's a verbose way to obtain the value using the new "As*()" methods and without using the 3 helper methods above.
```cs
    int i = jNode.AsObject()["Child"].AsObject()["Array"].AsArray()[1].GetValue<int>();
```

With the "As*()" methods, Add() can be called inline in one line of code and without casting:
```cs
    JsonObject jObjectToAdd = ...
    jNode["Child"]["Array"].AsArray().Add(jObjectToAdd);
```

The "As*()" methods are also used to remove `Parse()` overloads on `JsonArray`, `JsonObject` and `JsonValue`:
```cs
// This:
JsonObject jObject = JsonNode.Parse(...).AsJsonObject();
// is an in-line alternative to:
JsonObject jObject = (JsonObject)JsonNode.Parse(...);
```

# Proposed API
All types live in `System.Text.Json.dll`.

The proposed namespace is **"System.Text.Json.Node"** although "System.Text.Json" is also a suitable namespace since it contains the existing `JsonDocument`, `JsonElement` and `JsonSerializer` classes.

## Main API: JsonNode and derived classes
```cs
namespace System.Text.Json.Node
{
    public abstract class JsonNode : System.Dynamic.IDynamicMetaObjectProvider
    {
        internal JsonNode(JsonNodeOptions? options); // prevent external derived classes.

        // Options specified during Parse() or in a constructor.
        // Normally only specified in root node.
        // If null, Parent.Options are used (recursively). If Root.Options is null, default options used.
        public JsonNodeOptions? Options { get; }

        // Alternative to casting to allow for in-line dot syntax.
        // Throws InvalidOperationException on mismatch.
        public JsonArray? AsArray();
        public JsonObject? AsObject();
        public JsonValue? AsValue();

        // JsonArray terse syntax support.
        // Throws InvalidOperationException on non-JsonArray instances.
        // Use AsArray() to get access members other than this indexer.
        public virtual JsonNode? this[int index] { get; set; }

        // JsonObject terse syntax.
        // Throws InvalidOperationException on non-JsonObject instances.
        // Use AsObject() to get access members other than this indexer.
        public virtual JsonNode? this[string propertyName] { get; set; }

        // JsonValue terse syntax.
        // Throws InvalidOperationException on non-JsonValue instances.
        // Use AsValue() to get access members other than this method.
        // Returns the internal value, a JsonElement conversion, or a custom conversion to the provided type.
        // Allows for common programming model when JsonValue<T> is based on JsonElement or a CLR value.
        // "TValue" vs "T" to prevent collision with JsonValue<T>.
        public virtual TValue? GetValue<TValue>();

        // Return the parent and root nodes; useful for LINQ.
        public JsonNode? Parent { get; }
        public JsonNode? Root { get; }

        // Deep clone. Expensive.
        public JsonNode Clone();

        // The JSON Path; same "JsonPath" syntax we use for JsonException information.
        // Not a simple calculation since nodes can change order in JsonArray.
        public string GetPath();

        // Not to be used as deserializable JSON.
        // - Pretty printed (JsonWriterOptions.Indented).
        // - A string-based root JsonValue will not be quoted.
        public override string ToString(JsonSerializerOptions options = null);

        // Serialize as JSON that can later be used to deserialize.
        public string ToJsonString(JsonSerializerOptions options = null);

        // Serialize as Utf8
        public byte[] ToUtf8Bytes(JsonSerializerOptions options = null);

        // Wrappers over ReadFrom(JsonDocument.Parse().RootElement.Clone()) for common string- and byte- based deserialization:
        public static JsonNode? Parse(string json,
            JsonNodeOptions? nodeOptions = null,
            JsonDocumentOptions documentOptions = default(JsonDocumentOptions));

        public static JsonNode? ParseUtf8Bytes(ReadOnlyMemory<byte> utf8Json,
            JsonNodeOptions? nodeOptions = null,
            JsonDocumentOptions documentOptions = default(JsonDocumentOptions));

        public static JsonNode? ReadFrom(ref Utf8JsonReader reader,
            JsonNodeOptions? nodeOptions = null);

        public static JsonNode? ReadFromUtf8Stream(Stream utf8Json,
            JsonNodeOptions? nodeOptions = null,
            JsonDocumentOptions documentOptions = default(JsonDocumentOptions));

        public void WriteTo(
            Utf8JsonWriter writer,
            JsonSerializerOptions? options = null);

        public void WriteToUtf8Stream(
            Stream utf8Json,
            JsonSerializerOptions? options = null);

        // Dynamic support; implemented explicitly to help hide.
        System.Dynamic.DynamicMetaObject System.Dynamic.IDynamicMetaObjectProvider.GetMetaObject(System.Linq.Expressions.Expression parameter);

        // Explicit operators (can throw) from known primitives.
        public static explicit operator bool(JsonNode value);
        public static explicit operator byte(JsonNode value);
        public static explicit operator DateTime(JsonNode value);
        public static explicit operator DateTimeOffset(JsonNode value);
        public static explicit operator decimal(JsonNode value);
        public static explicit operator double(JsonNode value);
        public static explicit operator Guid(JsonNode value);
        public static explicit operator short(JsonNode value);
        public static explicit operator int(JsonNode value);
        public static explicit operator long(JsonNode value);
        [CLSCompliantAttribute(false)]
        public static explicit operator sbyte(JsonNode value);
        public static explicit operator float(JsonNode value);
        public static explicit operator char(JsonNode value);
        [CLSCompliantAttribute(false)]
        public static explicit operator ushort(JsonNode value);
        [CLSCompliantAttribute(false)]
        public static explicit operator uint(JsonNode value);
        [CLSCompliantAttribute(false)]
        public static explicit operator ulong(JsonNode value);

        public static explicit operator bool?(JsonNode value);
        public static explicit operator byte?(JsonNode value);
        public static explicit operator DateTime?(JsonNode value);
        public static explicit operator DateTimeOffset?(JsonNode value);
        public static explicit operator decimal?(JsonNode value);
        public static explicit operator double?(JsonNode value);
        public static explicit operator Guid?(JsonNode value);
        public static explicit operator short?(JsonNode value);
        public static explicit operator int?(JsonNode value);
        public static explicit operator long?(JsonNode value);
        [CLSCompliantAttribute(false)]
        public static explicit operator sbyte?(JsonNode value);
        public static explicit operator float?(JsonNode value);
        public static explicit operator string?(JsonNode value);
        public static explicit operator char?(JsonNode value);
        [CLSCompliantAttribute(false)]
        public static explicit operator ushort?(JsonNode value);
        [CLSCompliantAttribute(false)]
        public static explicit operator uint?(JsonNode value);
        [CLSCompliantAttribute(false)]
        public static explicit operator ulong?(JsonNode value);

        // Implicit operators (won't throw) from known primitives.
        public static implicit operator JsonNode(bool value);
        public static implicit operator JsonNode(byte value);
        public static implicit operator JsonNode(DateTime value);
        public static implicit operator JsonNode(DateTimeOffset value);
        public static implicit operator JsonNode(decimal value);
        public static implicit operator JsonNode(double value);
        public static implicit operator JsonNode(Guid value);
        public static implicit operator JsonNode(short value);
        public static implicit operator JsonNode(int value);
        public static implicit operator JsonNode(long value);
        [CLSCompliantAttribute(false)]
        public static implicit operator JsonNode(sbyte value);
        public static implicit operator JsonNode(float value);
        public static implicit operator JsonNode?(char value);
        [CLSCompliantAttribute(false)]
        public static implicit operator JsonNode?(ushort value);
        [CLSCompliantAttribute(false)]
        public static implicit operator JsonNode?(uint value);
        [CLSCompliantAttribute(false)]
        public static implicit operator JsonNode?(ulong value);

        public static implicit operator JsonNode(bool? value);
        public static implicit operator JsonNode(byte? value);
        public static implicit operator JsonNode(DateTime? value);
        public static implicit operator JsonNode(DateTimeOffset? value);
        public static implicit operator JsonNode(decimal? value);
        public static implicit operator JsonNode(double? value);
        public static implicit operator JsonNode(Guid? value);
        public static implicit operator JsonNode(short? value);
        public static implicit operator JsonNode(string? value);
        public static implicit operator JsonNode(int? value);
        public static implicit operator JsonNode(long? value);
        [CLSCompliantAttribute(false)]
        public static implicit operator JsonNode(sbyte? value);
        public static implicit operator JsonNode(float? value);
        public static implicit operator JsonNode?(char? value);
        [CLSCompliantAttribute(false)]
        public static implicit operator JsonNode?(ushort? value);
        [CLSCompliantAttribute(false)]
        public static implicit operator JsonNode?(uint? value);
        [CLSCompliantAttribute(false)]
        public static implicit operator JsonNode?(ulong? value);
    }

    public sealed class JsonArray : JsonNode, IList<JsonNode?>
    {
        // JsonNodeOptions in the constructors below allow for case-insensitive property names and
        // are normally applied only to root nodes since a child node will use the Root node's options.
        public JsonArray(JsonNodeOptions? options = null);
        public JsonArray(int capacity, JsonNodeOptions? options = null);

        // Param-based constructors to support constructor initializers:
        public JsonArray(params JsonNode[] items);
        public JsonArray(JsonNodeOptions options, params JsonNode[] items);

        // When a value can't be implicitly converted to JsonValue<T>, here's a helper that
        // allows "Add(value)" instead of the more verbose "Add(new JsonValue<MyType>(value))".
        public void Add<T>(T value);

        // IList<JsonNode?> (some hidden via explicit implementation):
        public int Count { get ;}
        bool ICollection<JsonNode?>.IsReadOnly { get ;}
        public void Add(JsonNode? item);
        public void Clear();
        public bool Contains(JsonNode? item);
        public IEnumerator<JsonNode?> GetEnumerator();
        public int IndexOf(JsonNode? item);
        public void Insert(int index, JsonNode? item);
        public bool Remove(JsonNode? item);
        public void RemoveAt(int index);
        void ICollection<JsonNode?>.CopyTo(JsonNode?[]? array, int arrayIndex);
        IEnumerator IEnumerable.GetEnumerator();
    }

    public sealed class JsonObject : JsonNode, IDictionary<string, JsonNode?>
    {
        public JsonObject();

        // JsonNodeOptions in the constructors below allow for case-insensitive property names and
        // are normally applied only to root nodes since a child node will use the Root node's options.
        public JsonObject(JsonNodeOptions? options = null);

        public bool TryGetPropertyValue(string propertyName, outJsonNode? jsonNode);

        // IDictionary<string, JsonNode?> (some hidden via explicit implementation):
        public int Count { get; }
        bool ICollection<KeyValuePair<string,JsonNode?>>.IsReadOnly { get; }
        ICollection<string> IDictionary<string,JsonNode?>.Keys { get; }
        ICollection<JsonNode?> IDictionary<string,JsonNode?>.Values { get; }
        public void Add(string propertyName,JsonNode? value);
        public void Clear();
        public bool ContainsKey(string propertyName);
        public IEnumerator<KeyValuePair<string,JsonNode?>> GetEnumerator();
        public bool Remove(string propertyName);
        void ICollection<KeyValuePair<string,JsonNode?>>.Add(KeyValuePair<string,JsonNode> item);
        bool ICollection<KeyValuePair<string,JsonNode?>>.Contains(KeyValuePair<string,JsonNode> item);
        void ICollection<KeyValuePair<string,JsonNode?>>.CopyTo(KeyValuePair<string,JsonNode>[] array, int arrayIndex);
        bool ICollection<KeyValuePair<string,JsonNode?>>.Remove(KeyValuePair<string,JsonNode> item);
        IEnumerator IEnumerable.GetEnumerator();
        bool IDictionary<string,JsonNode?>.TryGetValue(string propertyName, outJsonNode? jsonNode);
    }

    // Separate class to make it easy to check type via "if (node is JsonValue)"
    // and to support polymorphic scenarios (not required to specify the <T> in JsonValue<T>)
    public abstract class JsonValue : JsonNode
    {
        public abstract TValue? GetValue<TValue>(
            JsonConverter<TValue> converter,
            JsonSerializerOptions options = null);

        public abstract bool TryGetValue<TValue>(
            out TValue? value,
            JsonConverter<TValue> converter = null,
            JsonSerializerOptions options = null);

        // Factory and deserialize methods below that doen't require specifying <T> due to generic type inference. This is necessary for anonymous types.
        // T is normally a primitive but can also be JsonElement or any other type.
        public static JsonValue<T> Create(T value);
    }

    public sealed class JsonValue<T> : JsonValue
    {
        public JsonValue(T value);

        public override TValue? GetValue<TValue>(JsonConverter<TValue> converter, JsonSerializerOptions options = null);
        public override bool TryGetValue<TValue>(out TValue? value, JsonConverter<TValue> converter = null, JsonSerializerOptions options = null);

        // The internal raw value.
        public T Value {get; set;}
    }

    public struct JsonNodeOptions
    {
        public bool PropertyNameCaseInsensitive { get; set; }

        maxlevel\recusion

        // Possibly add later:
        // DuplicatePropertyNameHandling { get; set; }
    }
}
```

## JsonElement additions
```cs
namespace System.Text.Json
{
    public partial struct JsonElement
    {
        // Returns a JsonNode that has a back-link to a copy of the current JsonElement.
        // A copy of the current JsonElement is necessary since a JsonElement is immutable.
        // Thus the Element and Node both have referencs to eachother, allowing interop.
        public JsonNode AsNode(JsonNodeOptions? options = null);
    }
}
```

## JsonSerializerOptions additions
Currently (and going back to 3.0) a `JsonElement` instance is created in three cases by the serializer:
1) When a CLR property\field is of type `JsonElement`.
2) When a CLR property\field is of type `System.Object`.
3) When a property exists in JSON but does not map to any CLR property. Currently this is stored in a dictionary-backed property (e.g.`IDictionary<string, JsonElement>`) with the `[JsonExtensionData]` attribute.

However, using the node classes instead of `JsonElement` for the last two cases may be convenient since they are editable plus easier to use. So `JsonNode`-derived classes would be returned for `System.Object` and `JsonObect` for extension data.

```cs
namespace System.Text.Json
{
    // Determines the type to create for properties and members declared as System.Object.
    public enum JsonUnknownTypeHandling
    {
        JsonElement = 0, // Default
        JsonNode = 1, // Create JsonNode*-derived types for System.Object properties and elements.
    }

    public partial class JsonSerializerOptions
    {
        public JsonUnknownTypeHandling UnknownTypeHandling {get; set;}
    }
}
```

Sample code to enable:
```cs
    var options = new JsonSerializerOptions();
    options.UnknownTypeHandling = JsonUnknownTypeHandling.JsonNode;

    object obj = JsonSerializer.Deserialize<object>("{}", options);
    Debug.Assert(obj.GetType() == typeof(JsonObject));
```

Currently the extension property can be declared in several ways:
```cs
    // Existing support:
    [JsonExtensionData] public Dictionary<string, object?> ExtensionData {get; set;}
    [JsonExtensionData] public IDictionary<string, object?> ExtensionData {get; set;}
    [JsonExtensionData] public Dictionary<string, JsonElement> ExtensionData {get; set;}
    [JsonExtensionData] public IDictionary<string, JsonElement> ExtensionData {get; set;}
```

When `options.UnknownTypeHandling == JsonUnknownTypeHandling.JsonNode` the `object?` dictionary value above will create `JsonNode?` instances instead of `JsonElement` instances. This is for consistency since the type is `object` and other usages of `object` elsewhere will do the same. However, `JsonObject` can be specified as the property type, which is a better experience:
```cs
    // New support:
    [JsonExtensionData] public JsonObject ExtensionData {get; set;}
```

These and other unnecessary permutations are not supported:
```cs
    // Not supported:
    [JsonExtensionData] public IDictionary<string, JsonNode?> ExtensionData {get; set;}
    [JsonExtensionData] public Dictionary<string, JsonNode?> ExtensionData {get; set;}
```

## JsonSerializer additions
```cs
public partial class JsonSerializer
{
        public override TValue GetValue<TValue>(JsonSerializerOptions options = null);
        public override TValue GetValue<TValue>(JsonConverter converter, JsonSerializerOptions options = null)
}
```

# Interop with the Serializer and JsonElement
## Serializer
These serializer features are supported via `JsonSerializerOptions`:
- `Converters.Add()` (custom converters added at run-time)
- `NumberHandling`
- `DefaultIgnoreCondition` (default\null handling)

The `JsonSerializerOptions` are only used during serialization, not deserialization\Parse(). Deserialization uses `JsonNodeOptions` and `JsonDocumentOptions` instead which makes it clear what is and what is not supported, and doesn't couple deserialization of `JsonNode` to the serializer.

A deserialized `JsonNode` that is not modified will be re-serialized using the existing `JsonElement` semantics where essentially the raw UTF-8 is written back. This is important to call out because the above serializer features do not work during serialization of a `JsonElement`. This is a performance optimization that avoids having to expand the entire tree for cases when the `JsonSerializerOptions` supports the above options. However, normally POCOs will round-trip the same JSON anyway, so in most cases not supporting these serializer features should be fine.

### `JsonNode.GetValue<TypeToReturn>`
This method has 4 stages:

**Stage 1: return internal value directly from JsonValue<T>**
If the `<TypeToReturn>` in `GetValue<TypeToReturn>` is the same type as `<T>` in `JsonValue<T>` then return the internal value:
```cs
var jValue = new JsonValue<int>(42);
int i = jValue.GetValue<int>(); // returns the internal <T> value
```
The implementation assumes a direct Type match, not `IsAssignableFrom()` semantics.

**Stage 2: JsonElement support for known types**
For `JsonValue<JsonElement>` special logic exists to obtain the known primitives:
```cs
JsonNode jObject = JsonNode.Parse(...);
JsonNode jNode = jObject["MyStringProperty"];
Debug.Assert(jNode is JsonValue<JsonElement>);
string s = jNode.GetValue<string>(); // calls JsonElement.GetString()
```
This is necessary because the serializer doesn't currently support deserializing values from a `JsonElement` instance.

**Stage 3: JsonElement support for custom types**
If the type is not known by `JsonElement`, the raw Utf-8 bytes are obtained, the converter obtained for the type, and `converter.TryParse(ReadOnlySpan<byte> utf8Bytes, out T? value, options)` is used.

**Stage 4: serializer fallback**
Use the serializer to obtain the value. This stage is expensive compared to the other two.

### Internal custom converter for JsonNode
Note that a custom `JsonNode`-based converter can be specified to override the built-in custom converters although this should be a rare case.

### ToJsonString()
A `JsonNode` is normally serialized by the `ToJsonString()` helper method:
```cs
JsonObject jObject = ...

// short version:
string json = jObject.ToJsonString();

// equivalent longer versions; note the need to pass in Options (otherwise the default options will be used)
string json = JsonSerializer.Serialize(jObject, jObject.GetType(), jObject.Options);
string json = JsonSerializer.Serialize<JsonObject>(jObject, jObject.Options);
string json = JsonSerializer.Serialize<JsonNode>(jObject, jObject.Options);
```

## Deserialize interop
For deserializing JSON to a `JsonNode`, a static "Parse()" method on `JsonNode` and all derived node types is provided:
```cs
// short version:
JsonNode jNode = JsonNode.Parse(json, options);

// equivalent longer version:
JsonNode jNode = JsonSerializer.Deserialize<JsonNode>(json, options);
```

To deserialize a node into a CLR object such as a collection, POCO other value type:
```cs
// short version:
MyPoco? obj = jNode.GetValue<MyPoco>();

// equivalent longer version:
MyPoco? obj = JsonSerializer.Deserialize<MyPoco>(jNode.ToJsonString());
```

## Interop with JsonElement
`JsonElement.AsNode()` can be used to navigate to an editable node. This may help usability for those familiar with `JsonDocument` or `JsonElement` and perhaps help first-time usability to discover the node types. The method is also useful to navigate to a lower tree of JSON, call `AsNode()` and then modify a subsection which will not include the parent nodes.

The existing methods on `JsonElement` can be used including:
- `ToInt32()` etc which forwards to `JsonValue<T>`
- `EnumerateObject` that forwards to `JsonObject`.
- `EnumerateArray` that forwards to `JsonArray`.
- `WriteTo` that forwards to the respective node.
- `ToString` which forwards to `JsonNode.ToString()`.

# Programming model notes
## Why `JsonValue` and not `JsonNumber` + `JsonString` + `JsonBoolean`?
The JSON primitives (number, string, true, false) do **not** have their own `JsonNode`-derived type like `JsonNumber`, `JsonString` and `JsonBoolean`. Instead, a common `JsonValue` class represents them. This allows a JSON number, for example, to serialize and deserialize as either a CLR `String` or a `Double` depending on usage and options.

## Cast operators for JsonValue<T>
Although explicit or implicit cast operators are not required for functionality, based on usability feedback requesting a terse syntax, the use of both explicit cast operators (from a `JsonValue<T>` to known primitives) and implicit cast operators (from known primitives to a `JsonValue<T>`) are supported.

Note that implicit cast operators do not work in all languages including F#.

Explicit (no operators):
```cs
var jArray = new JsonArray();
jArray.Add(new JsonValue<string>("hello")); 
jArray.Add(new JsonElement<MyCustomDataType>(myCustomDataType));

string s = jArray[0].GetValue<string>();
MyCustomDataType m = jArray[1].GetValue<MyCustomDataType>();
```
with operators:
```cs
var jArray = new JsonArray();
jArray.Add("hello"); // Uses implicit operator.
jArray.Add(new JsonElement<MyCustomDataType>(myCustomDataType)); // No implicit operator for custom types (unless it adds its own)

string s = (string)jArray[0]; // Uses explicit operator.
MyCustomDataType m = jArray[1].GetValue<MyCustomDataType>(options); // no explicit operator for custom types  (unless it adds its own). Must pass options that contains custom converter.
```
with dynamic:
```cs
dynamic jArray = new JsonArray();
jArray.Add("hello"); // Uses implicit operator.
jArray.Add(myCustomDataType); // Possible through dynamic.

string s = jArray[0]; // Possible through dynamic.
MyCustomDataType m = jArray[1].GetValue(options); // Must call GetValue(options) to specify custom converter
```

## Specifying the `JsonSerializerOptions` instance
The `JsonNodeOptions` instance needs to be specified in order for options including case sensitivity of property names. Since the explicit and implicit cast operators do not support passing the the options, having them available in the root node is necessary.

It is cumbersome and error-prone to specify the options instance for every new element or property. Consider the verbose syntax:
```cs
// Verbose syntax (not recommended, but still supported)
JsonNodeOptions nodeOptions = ...
var jObject = new JsonObject(nodeOptions)
{
    ["MyString"] = new JsonValue<string>("Hello!", nodeOptions),
    ["MyBoolean"] = new JsonValue<bool>(false, nodeOptions),
    ["MyArray"] = new JsonArray(nodeOptions)
    {
        new JsonValue<int>(2, nodeOptions),
        new JsonValue<int>(3, nodeOptions),
        new JsonValue<int>(42, nodeOptions)
    },
}
```
and the terse syntax which omits the options instance for non-root members:
```cs
// Terse syntax
var jObject = new JsonObject(nodeOptions) // options can just be at root
{
    ["MyString"] = new JsonValue<string>("Hello!"),
    ["MyBoolean"] = new JsonValue<bool>(false),
    ["MyArray"] = new JsonArray()
    {
        new JsonValue<int>(2),
        new JsonValue<int>(3),
        new JsonValue<int>(42)
    },
}
```
including implicit operators and `JsonArray` support for `params`:
```cs
var jObject = new JsonObject(nodeOptions) // options can just be at root
{
    ["MyString"] = "Hello!",
    ["MyCustomDataType"] = false,
    ["MyArray"] = new JsonArray(2, 3, 42)
}
```

Nodes only allow the `JsonNodeOptions` value to be specified during creation or during deserialization, and not during serialization since they are not used. Instead, `JsonSerializerOptions` are used during serialization.

Child nodes use the parent `JsonNodeOptions`, recursively, if they don't have any options specified during creation.

## Adding `JsonNode` indexers for objects\arrays and GetValue() for values
`JsonNode` has indexers that allow `JsonObject` properties and `JsonArray` elements to be specified. This allows a terse mode:
```cs
    // Terse programming model:
    string str = jNode["Child"]["Array"][0]["Message"].GetValue<string>();
```

If `System.Object` was used instead of `JsonNode`, or the indexers were not exposed on `JsonNode`, the above terse syntax wouldn't work and a more verbose syntax would be necessary:
```cs
    // Programming model not acceptable:
    string str = ((JsonValue)((JsonObject)((JsonArray)((JsonObject)((JsonObject)jObject)["Child"])["Array"])[0])["Message"]).GetValue<string>();
```

A `JsonNode.GetValue<T>()` helper method also exists for `JsonValue<T>`.

## Missing vs. null
The indexer for `JsonObject` returns `null` for missing properties. This aligns with:
- Expected support for `dynamic`.
- Newtonsoft.
- The serializer today. This includes when deserializing JSON to `System.Object`, `null` is returned today, not, for example, a `JsonElement` with `ValueKind == JsonValueKind.Null`.

However, for some scenarios, it is important to distinguish between a `null` value deserialized from JSON vs. a missing property. Since `JsonObject` implements `IDictionary<string, JsonNode>` it can be inspected:
```cs
bool found = jObject.TryGetValue("NonExistingProperty", out object _);
```

## Allowing any CLR type in JsonValue<T>
Although `JsonValue<T>` is intended to support simple value types, any CLR value including POCOs and various collection types can be specified, assuming they are supported by the serializer. If they are not supported by the serializer, an exception or incorrect serialization can occur.

However, normally one would use `JsonArray` or `JsonObject` instead to create a new POCO or collection. A POCO or collection in this scenario is serialized as expected (as a JSON object or array) but when deserialized the type will be either a `JsonObject` or a `JsonArray`, not a `JsonValue<T>`.

One reason to support this (and not throw) is because it is not possible to identify what comprises a POCO (`JsonObject`) vs. a value from the `<T>` in `JsonValue<T>`. However, an array (`JsonArray`) can be identified somewhat reliabily since `<T>` will implement `IEnumerable`. 

Unless we remove the support for custom data types, allowing any type of object in `JsonValue<T>` is assumed.

## Support for C# object initializers
### Terse syntax with operators (non-dynamic)
```cs
var jObject = new JsonObject()
{
    ["MyString"] = "Hello!",
    ["MyNull"] = null,
    ["MyBoolean"] = false,
    ["MyInt"] = 43,
    ["MyDateTime"] = new DateTime(2020, 7, 8),
    ["MyGuid"] = new Guid("ed957609-cdfe-412f-88c1-02daca1b4f51"),
    ["MyArray"] = new JsonArray(2, 3, 42),
    ["MyObject"] = new JsonObject()
    {
        ["MyString"] = "Hello!!"
    },
    ["Child"] = new JsonObject()
    {
        ["ChildProp"] = 1
    }
};
```
### dynamic
Currently `dynamic` doesn't support object initializers, so a more verbose model is required:
```cs
dynamic jObject = new JsonObject();
jObject.MyString = "Hello!";
jObject.MyNull = null;
jObject.MyBoolean = false;
jObject.MyInt = 43;
jObject.MyDateTime = new DateTime(2020, 7, 8);
jObject.MyGuid = new Guid("ed957609-cdfe-412f-88c1-02daca1b4f51");
jObject.MyArray = new JsonArray(2, 3, 42);
jObject.MyObject = new JsonObject();
// We call .MyObject again, but could manually reference the previous value for perf.
jObject.MyObject.MyString = "Hello!!"
jObject.MyObject.Child = new JsonObject();
jObject.MyObject.Child.ChildProp = 1;
```

## LINQ samples
`JsonObject` and `JsonArray` implement `IEnumerable` which is the basic for LINQ. In addition, the `JsonNode.Parent` and `JsonNode.Root` properties can be used to help query parent:child relationships.

```cs
private class BlogPost
{
    public string Title { get; set; }
    public string AuthorName { get; set; }
    public string AuthorTwitter { get; set; }
    public string Body { get; set; }
    public DateTime PostedDate { get; set; }
}

{
    string json = @"
    [
        {
        ""Title"": ""TITLE."",
        ""Author"":
        {
            ""Name"": ""NAME."",
            ""Mail"": ""MAIL."",
            ""Picture"": ""/PICTURE.png""
        },
        ""Date"": ""2021-01-20T19:30:00"",
        ""BodyHtml"": ""Content.""
        }
    ]";

    JsonArray arr = (JsonArray)JsonNode.Parse(json);

    // Convert nested JSON to a flat POCO.
    IList<BlogPost> blogPosts = arr.Select(p => new BlogPost
    {
        Title = p["Title"].GetValue<string>(),
        AuthorName = p["Author"]["Name"].GetValue<string>(),
        AuthorTwitter = p["Author"]["Mail"].GetValue<string>(),
        PostedDate = p["Date"].GetValue<DateTime>(),
        Body = p["BodyHtml"].GetValue<string>()
    }).ToList();

    const string expected = "[{\"Title\":\"TITLE.\",\"AuthorName\":\"NAME.\",\"AuthorTwitter\":\"MAIL.\",\"Body\":\"Content.\",\"PostedDate\":\"2021-01-20T19:30:00\"}]";

    string json_out = JsonSerializer.Serialize(blogPosts);
    Debug.Assert(expected == json_out);
}

const string Linq_Query_Json = @"
[
    {
        ""OrderId"":100, ""Customer"":
        {
            ""Name"":""Customer1"",
            ""City"":""Fargo""
        }
    },
    {
        ""OrderId"":200, ""Customer"":
        {
            ""Name"":""Customer2"",
            ""City"":""Redmond""
        }
    },
    {
        ""OrderId"":300, ""Customer"":
        {
            ""Name"":""Customer3"",
            ""City"":""Fargo""
        }
    }
]";

{
    // Query for orders
    JsonArray allOrders = JsonNode.Parse(Linq_Query_Json).AsJsonArray();
    IEnumerable<JsonNode> orders = allOrders.Where(o => o["Customer"]["City"].GetValue<string>() == "Fargo");

    Debug.Assert(2 == orders.Count());
    Debug.Assert(100 == orders.ElementAt(0)["OrderId"].GetValue<int>());
    Debug.Assert(300 == orders.ElementAt(1)["OrderId"].GetValue<int>());
    Debug.Assert("Customer1" == orders.ElementAt(0)["Customer"]["Name"].GetValue<string>());
    Debug.Assert("Customer3" == orders.ElementAt(1)["Customer"]["Name"].GetValue<string>());
}

{
    // Query for orders with dynamic
    IEnumerable<dynamic> allOrders = (IEnumerable<dynamic>)JsonNode.Parse(Linq_Query_Json);
    IEnumerable<dynamic> orders = allOrders.Where(o => ((string)o.Customer.City) == "Fargo");

    Debug.Assert(2 == orders.Count());
    Debug.Assert(100 == (int)orders.ElementAt(0).OrderId);
    Debug.Assert(300 == (int)orders.ElementAt(1).OrderId);
    Debug.Assert("Customer1" == (string)orders.ElementAt(0).Customer.Name);
    Debug.Assert("Customer3" == (string)orders.ElementAt(1).Customer.Name);
}
```

# Dynamic
The dynamic feature in C# is implemented in the CLR by both language features and in the `System.Linq.Expressions` assembly. The core interface is [IDynamicMetaObjectProvider](https://docs.microsoft.com/en-us/dotnet/api/system.dynamic.idynamicmetaobjectprovider?view=net-5.0).

There are existing implementations of `IDynamicMetaObjectProvider` including [DynamicObject](https://docs.microsoft.com/en-us/dotnet/api/system.dynamic.dynamicobject?view=net-5.0) and [ExpandObject](https://docs.microsoft.com/en-us/dotnet/api/system.dynamic.expandoobject?view=netcore-3.1). However, these implementations are not ideal for JSON scenarios because:
- There are many public members used for wiring up dynamic support, but are of little to no value for consumers thus they would be confusing when mixed in with other methods intended to be used for writable DOM support.
- `ExpandoObject` does not support case insensitivity and throws an exception when accessing missing properties (a `null` is desired instead).
- `DynamicObject` prevents potential optimizations including property-lookup and avoiding some reflection calls.

Thus, the design presented here for `dynamic` assumes explicit interface implementation of `IDynamicMetaObjectProvider`. This is also what Newtonsoft does; its [JToken](https://www.newtonsoft.com/json/help/html/T_Newtonsoft_Json_Linq_JToken.htm) implements `IDynamicMetaObjectProvider`.

## Referencing the `System.Linq.Expressions` assembly
Having `System.Text.Json.dll` directly reference `System.Linq.Expressions.dll` is feasible because the ILLinker is able to remove the reference to the large `System.Linq.Expressions.dll` when the dynamic functionality is not used. In tests, a simple `JsonNode` stand-alone console app not using `dynamic` was ~10.5MB and using dynamic grew to ~11.5MB. It was also verified the SLE assembly reference in STJ was removed when not using `dynamic`.

## Varying the `T` in `Deserialize<T>`
For a `JsonObject`, this programming model:
```cs
    dynamic obj = JsonNode.Parse<object>("{\"MyProp\":42}", options);
```
is equivalent to
```cs
    dynamic obj = JsonNode.Parse<dynamic>("{\"MyProp\":42}", options);
```
and
```cs
    dynamic obj = JsonNode.Parse<JsonNode>("{\"MyProp\":42}", options);
    dynamic obj = JsonNode.Parse<JsonObject>("{\"MyProp\":42}", options);
```

## Sample interop with custom converter + dynamic
Here's an example using a custom converter along with dynamic to populate a POCO.
```cs
internal class PersonConverter : JsonConverter<Person>
{
    public override Person Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        dynamic jObject = JsonNode.Parse<object>(ref reader, options);

        // Pass values into the constructor which requires the values to be read ahead of time.
        Person person = new Person(jObject.Id)
        {
            Name = jObject.name,
            AddressLine1 = jObject.addr1,
            AddressLine2 = jObject.addr2,
            City = jObject.city,
            State = jObject.state,
            Zip = jObject.zip
        }

        return person;
    }

    public override void Write(Utf8JsonWriter writer, Person value, JsonSerializerOptions options)
    {
        // Standard serialization is fine in this example.
        JsonSerializer.Serialize<Person>(writer, value, options);
    }
}
```

# Performance
## Internal value as `JsonElement`
`JsonElement` is used as the internal deserialized value. Since it supports lazy creation of values, it is performant for scenarios that don't fully access each property or element. For example, the contents of a `JsonValue<string>` internally is a UTF-8 `Span<byte>`, not a `string`, is not "cracked" open until the value is requested.

Note that `JsonElement` is [now ~2x faster](https://github.com/dotnet/runtime/pull/42538) in 6.0 for cases used here.

## Lazy creation and enumeration of `JsonNode` tree
The design supports lazy and shallow creation which is performant when only a subset of the tree is accessed.

A `JsonNode` tree is populated when `JsonObject` and `JsonArray` instances are navigated. For example, a `JsonObject` contains only its internal `JsonElement` value after deserialization. When a property is accessed for the first time, a `JsonNode` instance is created for every property and added to the `JsonObject`'s internal dictionary. Each of those child nodes maintains a single reference to its corresponding `JsonElement` which internally still points to the shared UTF-8 buffer on the parent `JsonDocument`.

## Boxing and mutating of `Value<T>`
The design of values uses generics to hold internal `<T>` value. This avoids boxing for value types. A given `Value<T>`, such as `Value<int>` can be modified by the `Value` property which can be used to avoid creation of a new `JsonValue` instance.

## Property lookup
The property-lookup algorithm can be made more efficient than the standard dictionary by using ordering heuristics (like the current serializer) or a bucketless B-Tree with no need to call `Equals()`. The Azure `dynamic` prototyping effort has been thinking of the B-Tree approach.

## Debugging
A `[DebuggerDisplay]` attribute will be added to `JsonNode` that can performs a pretty-printed version of the JSON via `ToJsonString()`.

## JsonConverter<T> additions (possible; not current proposed)
When `JsonValue<T>.ToJsonString()` is called for primitives such as for an `Int32` that are not backed by a `JsonElement`, the simplest implementation would be:
```cs
public string ToJsonString()
{
    return JsonSerializer.Serialize<T>(this.Value, this.Options);
}
```

However this involves many steps that make this relatively slow:
1) Obtaining the converter for `<T>`.
2) Creating an instance of a `Utf8JsonWriter`.
3) Allocating a temporary buffer (from a pool).
4) Calling the custom converter, which calls the writer, which writes to the buffer.
5) Transcoding the UTF8-based buffer to a string.

One likely optimization is to cache the common known converters on `JsonSerializerOptions` and look them up from the type (e.g. `Int32`). Once found, the converter can also be cached on the `JsonValue` instance for future calls to the same instance.

However, for the case where only one value is serialized (and not a whole tree) this is still going to be slower than a design that can quickly return a string from a value without getting the writer and serializer involved. Such a design would add overloads including a new string-based overload that can also used to extended the existing quoted number handling to custom converers (currently quoted numbers support isn't extensible and only works on known types).

These new overloads will be implemented on the existing converters:
```cs
namespace System.Text.Json.Serialization
{
    public partial class JsonConverter<T>
    {
        // Override for fast JsonValue<T>.ToJsonString() and to support quoted number extensibility.
        public virtual bool TryConvert(T? value, out string? value, JsonSerializerOptions? options)
        {
            // Default implementation
            value = null;
            return false;
        }

        // Used with JsonNode.ToUtf8Bytes().
        public virtual bool TryConvert(T? value, out byte[]? value, JsonSerializerOptions? options)
        {
            // Default implementation
            value = null;
            return false;
        }

        // Used with JsonValue<T>.GetValue() when backed by JsonElement for unknown types.
        public virtual bool TryParse(ReadOnlySpan<byte> utf8Bytes, out T? value, JsonSerializerOptions? options)
        {
            // Default implementation
            value = null;
            return false;
        }
    }
}
```
If `false` is returned, the slow serializer path is used.

# Features not proposed in 6.0
These feature are reasonable, but not proposed (at least for the first round):
- Reading\writing JSON comments. Not implemented since `JsonDocument` doesn't support it, although the reader and writer support them. 
  - Cost to implement is not known (no known prototype or PR).
  - For the public API, a `JsonValueKind.Comment` enum value would be added and then a mechanism such as a ValueKind property added to `JsonNode`.
- `IEqualityComparer<T>` implementation such as a `JsonNodeComparer` class. This would imply a deep compare which is expensive, although that would be expected.
- "JsonPath" query support. Newtonsoft has this as a way to parse a subset of JSON into a `JToken`.
- Annotations. Newtonsoft has this to provide LineNumber and Position and user-specified values.
- `JsonNode.LineNumber` and `Position` properties. Although the properties don't exist, any inner reader exception or `JsonException` during deserialization will have that information. Note that a `JsonNodePath` property, however, is supported including determining the path at runtime even after modifications to a property name, element ordering, etc.
- Non-generic overloads on `JsonNode`. Note that if implemented they will not be fast since the internal representation is a generic `<T>` value, which requires boxing and\or a indirect call to the generic method:
  - `public virtual object GetArrayElement(Type type, int index);`
  - `public virtual object GetPropertyValue(Type type, string propertyName);`
  - `public abstract object GetValue(Type type);`
  - `public abstract bool TryGetValue(Type type, out object? value);`

These features will likely never be implemented:
- Support for `System.ComponentModel.TypeConverter` in the `GetValue<TypeToReturn>()` method or another new method. Currently the serializer does not support this either.
- Support for reference handling because:
  - Performance. When reference handling is enabled, all nodes must be materialized eagerly.
  - Likely not expected since a DOM is lower-level than an object tree. Consumers would expect the metadata to be available to them programmatically, not hidden (although it could be an option).
- `Equals()`, `==` and `!=` operators. These would be slow since they imply a deep compare in non-trivial cases. The `IEqualityComparer<T>` mechanism is more suitable. It would be useful for those with a custom dictionary or similar cases; it would likely use the case senstivity options for comparing property names.

# Dependencies
Other issues to consider along with this:
- [Api proposal: Change JsonSerializerOptions default settings](https://github.com/dotnet/runtime/issues/31094)
  - Useful to prevent the `JsonSerializerOption` parameter from having to be specified.
- [We should be able serialize and deserialize from DOM](https://github.com/dotnet/runtime/issues/31274)
  - For consistency with `JsonNode`.
- [More extensible object and collection converters](https://github.com/dotnet/runtime/issues/36785)
  - The DOM could be used in a future feature to make object and collection custom converters easier to use.

# Status checklist
- [X] Review general direction of API. Primarily using the serializer methods vs. new `node.Parse()` \ `node.Write()` methods and having a sealed `JsonValue` class instead of separate number, string and boolean classes.
- [X] Update API based on feedback and additional prototyping.
- [X] Provide more samples (LINQ).
- [X] Create API issue.
- [ ] Approve API.
- [ ] Prototype and usabilty studies (likely in collaboration with the Azure SDK team).
- [ ] Modify API as necessary based on usability.
- [ ] Ensure any future "new dynamic" C# support (being considered for intellisense support based on schema) is forward-compatible with the work here which uses the "old dynamic".
