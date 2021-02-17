# Overview
February 17th, 2021.

This document covers the API and design for a writable DOM along with support for the [C# `dynamic` keyword](https://docs.microsoft.com/en-us/dotnet/framework/reflection-and-codedom/dynamic-language-runtime-overview).

It is expected that a significant percent of existing System.Text.JSON consumers will use these new APIs, and also attract new consumers including:
- A need for a lightweight, simple API especially for one-off cases.
- A need for `dynamic` capabilities for varying reasons including sharing of loosely-typed, script-based code.
- To efficiently read or modify a subset of a large graph. For example, it is possible to efficiently navigate to a subsection of a large graph and read an array or deserialize a POCO from that subsection. LINQ can also be used with that.
- Those unable to use the serializer for varying reasons:
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
The existing `JsonDocument` and `JsonElement` types represent the DOM support today which is read-only. It maintains a single immutable UTF-8 buffer and returns `JsonElement` value types from that buffer on demand. That design minimizes the initial `JsonDocument.Parse()` time and associated heap allocs but also makes it slow to re-obtain the same value (which is common with LINQ) and does not lend itself to being directly extended to support writability. Although the internal UTF-8 buffer will continue to be immutable with the design proposed here, a `JsonElement` will indirectly support writability by forwarding serialization to a linked `JsonNode`.

Currently there is no direct support for `dynamic` in System.Text.Json. Adding support for that implies adding a writeable DOM. So considering both `dynamic` and non-`dynamic` scenarios for a writeable DOM in a single design allows for a common API, shared code and intuitive interop between the two.

This design is based on learning and scenarios from these reference implementations:
- The writable DOM prototype.
  - During 5.0, there was a [writable DOM effort](https://github.com/dotnet/runtime/issues/30436). The design centered around writable aspects, usability and LINQ support. It was shelved due to time constraints and outstanding work.
     - What's the same as the design proposed here:
       - The high-level class names `JsonNode`, `JsonArray` and `JsonObject`.
       - Interaction with `JsonElement`.
     - What's different:
       - `JsonValue` instead of `JsonString`, `JsonNumber` and `JsonBoolean`.
       - The writable `JsonValue` internal value is based on CLR types, not a string representing JSON. For example, a `JsonValue` initialized with an `int` keeps that `int` value without boxing or converting to a string-based field.
- The dynamic support code example.
  - During 5.0, [dynamic support](https://github.com/dotnet/runtime/issues/29690) was not able to be implemented due to schedule constraints. Instead, a [code example](https://github.com/dotnet/runtime/pull/42097) was provided that enables `dynamic` and a writeable DOM that can be used by any 3.0+ runtime in order to unblock the community and also has been useful in gathering feedback for the design proposed here.
- Azure prototyping. The Azure team needs a writable DOM, and supporting `dynamic` is important for some scenarios but not all. A [prototype](https://docs.microsoft.com/en-us/dotnet/api/azure.core.dynamicjson?view=azure-dotnet-preview) has been created. Work is also being coordinated with the C# team to support Intellisense with `dynamic`, although that is not expected to be implemented in time to be used in 6.0.
- Newtonsoft's Json.NET. This has a similar `JToken` class hierarchy and support for `dynamic`. The `JToken` hierarchy includes a single `JValue` type to represent all value types, like is being proposed here (instead of separate types for string, number and boolean). Json.NET also has implicit and explicit operators similar as to what is being proposed here.
  - Converting Json.NET code to System.Text.Json should be fairly straightforward although not all Json.NET features are implemented. See the "Features not proposed in first round" section for more information.

## API walkthrough
A deserialized `JsonNode` value internally holds a `JsonElement` which knows about the JSON kind (object, array, number, string, true, false) and the raw UTF-8 value including any child nodes (for a JSON object or array).

When the consumer obtains a primitive value through `jvalue.GetValue<double>()`, for example, the CLR `double` value is deserialized from the raw UTF-8. Thus the mapping between UTF-8 JSON and the CLR object model is deferred until manually mapped by the consumer by specifying the expected type.

Deferring is consistent with existing `JsonElement` semantics where, for example, a JSON number is not automatically mapped to any CLR type until the consumer calls a method such as `JsonElement.GetDecimal()` or `JsonElement.GetDouble()`. This design is preferred over "guessing" what the CLR type should be based upon the contents of the JSON such as whether the JSON contains a decimal point or the presence of an exponent. Guessing can lead to issues including overflow\underflow or precision loss.

In addition to deferring the creation of numbers in this manner, the other node types are also deferred including strings. In addition, `JsonObject` and `JsonArray` instances are not populated with child nodes until traversed. Using `JsonElement` in this way along with defferred creation of values and child nodes greatly improves CPU performance and reduces allocations especially when a subset of a graph is used.

Simple deserialization example:
```cs
JsonObject jObject = JsonObject.Parse("{""MyProperty"":42}");
JsonValue jValue = jObject["MyProperty"];

// Verify the contents
Debug.Assert(jValue is JsonValue<JsonElement>); // On deserialize, the value is JsonElement
int i = jValue.GetValue<int>(); // Same programming model as the sample below.
Debug.Assert(i == 42);
```

To mutate the value, a new node instance must be created:
```cs
jObject["MyProperty"] = 43;

// Verify the contents
JsonValue jValue = jObject["MyProperty"];
Debug.Assert(jValue is JsonValue<int>);
int i = jValue.GetValue<int>(); // Same programming model as the sample above.
Debug.Assert(i == 42);
```

Note that in both cases `GetValue<int>()` is called: this is important because it means there is a common programming model for cases when a given `JsonValue<T>` is backed by `JsonElement` (for read mode \ deserialization) or backed by an actual value (for edit mode or serialization) such as `int`. In this example, the consumer always knows that the "MyProperty" number property can be returned as an `int` when given a `JsonValue` and doesn't have to be concerned about whether it is backed by an `int` or `JsonElement`.

Note that an implicit operator was used in the above example:
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
although the type itself (`int` in this example) can't be changed without creating a new `JsonValue<T>`.

A `JsonValue` supports custom converters that were added via `JsonSerializerOptions.Converters.Add()` or specified in the `JsonValue<T>()` constructor. A common use case would be to add the `JsonStringEnumConverter` which serializes `Enum` values as a string instead of an integer, but also supports user-defined custom converters:
```cs
JsonObject jObject = JObject.Parse("{\"Amount\":1.23}", options);
JsonValue jValue = jObject["Amount"];
Money money = jValue.GetValue<Money>();
```

The call to `GetValue<Money>()` above calls the custom converter for `Money`.

During serialization, some features specified on `JsonSerializerOptions` are supported including quoted numbers and null handling. See the "Interop" section.

Serialization can be performed in several ways:
- Calling `string json = jNode.ToJsonString()`.
- Calling `jNode.WriteTo(utf8JsonWriter)`
- Assigning a node to a POCO or adding to a collection and serializing that.
- Using the existing `JsonSerializer.Serialize<JsonNode>(jNode)` methods.
- Obtaining a `JsonElement` from a node and serializing that through `jElement.WriteTo(utf8JsonWriter)`.

# Proposed API
All types live in `System.Text.Json.dll. The proposed namespace is **"System.Text.Json.Node"** since the node types overlap with both "Document" and "Serializer" semantics, so having its own namespace makes sense.

## Main API: JsonNode and derived classes
```cs
namespace System.Text.Json.Node
{
    public abstract class JsonNode : System.Dynamic.IDynamicMetaObjectProvider
    {
        internal JsonNode(); // prevent external derived classes.

        public JsonNode Clone(); // Deep clone.

        public JsonSerializerOptions Options { get; }

        // JsonArray terse syntax support (no JsonArray cast necessary).
        public virtual JsonNode? this[int index] { get; set; }

        // JsonObject terse syntax (no JsonObject cast necessary).
        public virtual JsonNode? this[string propertyName] { get; set; }

        // JsonValue terse syntax (no JsonValue<T> cast necessary).
        // Return the internal value, a JsonElement conversion, or a custom conversion to the provided type.
        // Allows for common programming model when JsonValue<T> is based on JsonElement or a CLR value.
        // "TypeToGet" vs "T" to prevent collision on JsonValue<T>.
        public virtual JsonValue? GetValue<TypeToGet>();
        public virtual bool TryGetValue<TypeToGet>(out TypeToGet? value);
        // Overloads with the converter for <TypeToGet>:
        public virtual JsonValue? GetValue<TypeToGet>(JsonConverter converter);
        public virtual bool TryGetValue<TypeToGet>(JsonConverter converter, out TypeToGet? value);

        // Return the parent and root nodes; useful for LINQ.
        public JsonNode? Parent { get; }
        public JsonNode? Root { get; }

        // The JSON Path; same "JsonPath" syntax we use for JsonException information.
        public string Path { get; }

        // Serialize\deserialize wrappers. These are helpers and thus can be considered optional.
        public abstract TypeToDeserialize? Deserialize<TypeToDeserialize>();
        public abstract bool TryDeserialize<TypeToDeserialize>(out TypeToDeserialize? value);
        public string ToJsonString(); // serialize as a string
        // WriteTo() terminology consistent with Utf8JsonWriter.
        public abstract void WriteTo(System.Text.Json.Utf8JsonWriter writer);
        // Parse() terminology consistent with Utf8JsonReader\JsonDocument.
        public static JsonNode? Parse(string? json, JsonSerializerOptions options = null);
        public static JsonNode? ParseUtf8Bytes(ReadOnlySpan<byte> utf8Json, JsonSerializerOptions options = null);
        public static JsonNode? ReadFrom(ref Utf8JsonReader reader, JsonSerializerOptions options = null);

        // The ValueKind from deserialization. Not used internally but may be useful for consumers.
        public JsonValueKind ValueKind { get; }

        // JsonElement interop
        public static JsonNode GetNode(JsonElement jsonElement);
        public static bool TryGetNode(JsonElement jsonElement, [NotNullWhen(true)] out JsonNode? jsonNode);

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
        public static explicit operator string(JsonNode value);
        public static explicit operator char(JsonNode value);
        [CLSCompliantAttribute(false)]
        public static explicit operator ushort(JsonNode value);
        [CLSCompliantAttribute(false)]
        public static explicit operator uint(JsonNode value);
        [CLSCompliantAttribute(false)]
        public static explicit operator ulong(JsonNode value);

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
        public static implicit operator JsonNode?(string? value);
        public static implicit operator JsonNode?(char value);
        [CLSCompliantAttribute(false)]
        public static implicit operator JsonNode?(ushort value);
        [CLSCompliantAttribute(false)]
        public static implicit operator JsonNode?(uint value);
        [CLSCompliantAttribute(false)]
        public static implicit operator JsonNode?(ulong value);
    }

    public sealed class JsonArray : JsonNode, IList<JsonNode?>
    {
        public JsonArray(JsonSerializerOptions? options = null);
        public JsonArray(JsonElement jsonElement, JsonSerializerOptions? options = null);

        // Param-based constructors for easy constructor initializers.
        public JsonArray(JsonSerializerOptions? options, params JsonNode[] items);
        public JsonArray(params JsonNode[] items);

        public override JsonNode Clone();

        public override void WriteTo(System.Text.Json.Utf8JsonWriter writer);

        public static JsonArray? Parse(string? json, JsonSerializerOptions options = null);
        public static JsonArray? ParseUtf8Bytes(ReadOnlySpan<byte> utf8Json, JsonSerializerOptions options = null);
        public static JsonArray? ReadFrom(ref Utf8JsonReader reader, JsonSerializerOptions options = null);

        // IList<JsonNode?> (some hidden via explicit implementation):
        public int Count { get ;}
        bool ICollection<JsonNode?>.IsReadOnly { get ;}
        public void Add(object? item);
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
        public JsonObject(JsonSerializerOptions options = null);
        public JsonObject(JsonElement jsonElement, JsonSerializerOptions? options = null);

        public override JsonNode Clone();

        public bool TryGetPropertyValue(string propertyName, outJsonNode? jsonNode);

        public override void WriteTo(System.Text.Json.Utf8JsonWriter writer);

        public static JsonObject? Parse(string? json, JsonSerializerOptions options = null);
        public static JsonObject? ParseUtf8Bytes(ReadOnlySpan<byte> utf8Json, JsonSerializerOptions options = null);
        public static JsonObject? ReadFrom(ref Utf8JsonReader reader, JsonSerializerOptions options = null);

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
    public abstract class JsonValue : JsonNode
    {
        public JsonValue(JsonSerializerOptions options = null);
    }

    public sealed class JsonValue<T> : JsonValue
    {
        public JsonValue(T value, JsonSerializerOptions options = null);

        // Allow a custom converter and JsonValueKind to be specified.
        public JsonValue(T value, JsonConverter? converter = null, JsonValueKind valueKind, JsonSerializerOptions options = null);

        public override JsonNode Clone();

        public override TypeToReturn GetValue<TypeToReturn>();
        public override TypeToReturn GetValue<TypeToReturn>(JsonConverter converter);
        public override bool TryGetValue<TypeToReturn>(out TypeToReturn value);
        public override bool TryGetValue<TypeToReturn>(JsonConverter converter, out TypeToReturn value);

        // The internal raw value.
        public override T Value {get; set;}

        public override void WriteTo(System.Text.Json.Utf8JsonWriter writer);

        public static JsonValue<T> Parse(string? json, JsonSerializerOptions options = null);
        public static JsonValue<T> ParseUtf8Bytes(ReadOnlySpan<byte> utf8Json, JsonSerializerOptions options = null);
        public static JsonValue<T> ReadFrom(ref Utf8JsonReader reader, JsonSerializerOptions options = null);
    }
}
```

## JsonSerializerOptions additions
Currently `JsonElement` is created in two cases by the serializer:
- When a CLR property\field is of type `System.Object`.
- When a property exists in JSON but does not map to any CLR property. Currently this is stored in a dictionary-backed property (e.g.`IDictionary<string, JsonElement>`) with the `[JsonExtensionData]` attribute.

However, using the node classes instead may be super convenient since they are editable plus easier to use. So we return `JsonNode`-derived classes for `System.Object` and `JsonObect` for extension data.

```cs
namespace System.Text.Json
{
    // Determines the type to create for extension data and properties declared as System.Object.
    public enum JsonUnknownTypeHandling
    {
        JsonElement = 0, // Default
        JsonNode = 1, // Create JsonNode*-derived types for System.Object and JsonObject for extension data.
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
    Debug.Assert(obj.GetType() == typeof(JsonObject);
```

Currently the extension property can be declared in several ways:
```cs
    // Existing support:
    [JsonExtensionData] public Dictionary<string, object?> ExtensionData {get; set;}
    [JsonExtensionData] public IDictionary<string, object?> ExtensionData {get; set;}
    [JsonExtensionData] public Dictionary<string, JsonElement> ExtensionData {get; set;}
    [JsonExtensionData] public IDictionary<string, JsonElement> ExtensionData {get; set;}
```

Going forward, `JsonObject` can also be specified as the property type, which is useful if the missing properties should be writable or support LINQ:
```cs
    // New support:
    [JsonExtensionData] public JsonObject? ExtensionData {get; set;}
```

These and other unnecessary permutations are not supported:
```cs
    // Not supported:
    [JsonExtensionData] public IDictionary<string, JsonNode?> ExtensionData {get; set;}
    [JsonExtensionData] public Dictionary<string, JsonNode?> ExtensionData {get; set;}
```

## JsonConverter<T> additions
When `JsonValue<T>.ToJsonString()` is called for primitives such as for an `Int32`, the simplest implementation would be:
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

Although some steps above can be optimized, it is still going to up to ~15x slower than a model that can quickly return a string from a value without getting the writer and serializer involved. The new overload is also used to extended the existing quoted number handling to custom converers (currently quoted numbers aren't extensible).

```cs
namespace System.Text.Json.Serialization
{
    public partial class JsonConverter<T>
    {
        // Override for fast JsonValue<T>.ToJsonString() and to support quoted numbers extensibility in custom converters.
        public virtual bool TryConvert(JsonSerializerOptions options, out string? value)
        {
            // Default implementation
            value = null;
            return false;
        }
    }
}
```
If `false` is returned, the slow serializer path is used. There is no mechanism proposed to return UTF-8 to optimize those cases. If a `JsonNode.ToUtf8Bytes()` method is added, then this will be considered.

# Interop with the Serializer and JsonElement
## Serializer
These serializer features are supported via `JsonSerializerOptions`:
- `Converters.Add()` (custom converters added at run-time)
- `NumberHandling`
- `DefaultIgnoreCondition` (default\null handling )
- `PropertyNamingPolicy`.  Note that `DictionaryKeyPolicy` is not used since an object and a dictionary both have the same JSON representation.
- `PropertyNameCaseInsensitive`
- `AllowTrailingCommas`
- `ReadCommentHandling`. Note that round-tripping comments are not possible. See the "Features not proposed in first round" section.
- `MaxDepth`

A deserialized `JsonNode` that is not modified will be re-serialized using the existing `JsonElement` semantics where essentially the raw UTF-8 is written back. This is important to call out because the above serializer features do not work during serialization of a `JsonElement`. This is a performance optimization that avoids having to expand the entire tree for cases when the `JsonSerializerOptions` supports the above options. However, normally POCOs will round-trip the same JSON anyway, so in most cases not supporting these serializer features should be fine.

### `JsonNode.GetValue<TypeToReturn>`
This method has 3 stages:

**Stage 1: return internal value directly from JsonValue<T>**
If the `<TypeToReturn>` in `GetValue<TypeToReturn>` is the same type as `<T>` in `JsonValue<T>` then return the internal value:
```cs
var jValue = new JsonValue<int>(42);
int i = jValue.GetValue<int>(); // returns the internal <T> value
```
The implementation assumes a direct Type match, not `IsAssignableFrom()` semantics.

**Stage 2: JsonElement support**
For `JsonValue<JsonElement>` special logic exists to obtain the known primitives:
```cs
JsonObject jObject = JsonObject.Parse(...);
JsonNode jNode = jObject["MyStringProperty"];
Debug.Assert(jNode is JsonValue<JsonElement>);
string s = jNode.GetValue<string>(); // calls JsonElement.GetString()
```

This is necessary because the serializer doesn't currently support deserializing values from a `JsonElement` instance.

**Stage 3: serializer fallback**
Use the serializer to obtain the value. This stage is expensive compared to the other two but is necessary to support custom converters. See also the proposal above for `TryConvert` to make the simple string-returning deserialization fast when obtaining the JSON for a single `JsonValue`.

```cs
JsonNode jNode = JsonNode.Parse("\"42\"", options); // a quoted number.
Debug.Assert(jNode is JsonValue<JsonElement>);

// This works through the serializer (if quoted numbers enabled):
int i = jNode.GetValue<int>();
Debug.Assert(i == 42);
```

### Internal custom converter
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
MyPoco? obj = jNode.Deserialize<MyPoco>();

// equivalent longer version:
MyPoco? obj = JsonSerializer.Deserialize<MyPoco>(jNode.ToJsonString(), jNode.Options);
```

## Interop with JsonElement
The constructors of `JsonNode` allows any instance and Type to be specified including a `JsonElement`. 

It is possible to obtain a `JsonElement` from `JsonNode.GetNode()`. This returns a `JsonElement` that references the `JsonNode`; changing the node does not actually change the underlying buffer in the corresponding `JsonDocument`.

Thus the existing methods on `JsonElement` can be used including:
- `ToInt32()` etc which forwards to `JsonValue<T>`
- `EnumerateObject` that forwards to `JsonObject`.
- `EnumerateArray` that forwards to `JsonArray`.
- `WriteTo` that forwards to the respective node.

```cs
var jsonObject = new JsonObject
{
    { "text", "property value" }
};

// Get a JsonElement that knows how to forward to nodes.
JsonElement jsonElement = jsonObject.AsJsonElement();

// We can read the value.
Debug.Assert(jsonElement.GetProperty("text").GetStringValue() == "property value");

// The element will see changes made to nodes.
jsonObject["text"] = "new value";
Debug.Assert(jsonElement.GetProperty("text").GetStringValue() == "new value");

// Serialization of modified values is also supported.
jsonElement.WriteTo(writer);
```

To obtain a node from an element, use the node constructors:
```cs
JsonElement jsonElement = ...
var jValue = new JsonValue<JsonElement>(jsonElement);
var jObject = new JsonObject(jsonElement);
var jArray = new JsonArray(jsonElement);
```

# Programming model notes
## Why `JsonValue` and not `JsonNumber` + `JsonString` + `JsonBoolean`?
The JSON primitives (number, string, true, false) do **not** have their own `JsonNode`-derived type like `JsonNumber`, `JsonString` and `JsonBoolean`. Instead, a common `JsonValue` class represents them. This allows a JSON number, for example, to serialize and deserialize as either a CLR `String` or a `Double` depending on usage and options.

## Cast operators for JsonValue<T>
Although explicit or implicit cast operators are not required for functionality, based on usability feedback requesting a terse syntax, the use of both explicit cast operators (from a `JsonValue<T>` to known primitives) and implicit cast operators (from known primitives to a `JsonValue<T>`) are supported.

Note that cast operators do not work in all languages such as F#.

Explicit (no operators):
```cs
JsonArray jArray = ...
jArray[0] = new JsonValue<string>("hello"); 
jArray[1] = new JsonElement<MyCustomDataType>(myCustomDataType);

string s = jArray[0].GetValue<string>();
MyCustomDataType m = jArray[1].GetValue<MyCustomDataType>();```
```
with operators:
```cs
JsonArray jArray = ...
jArray[0] = "hello"; // Uses implicit operator.
jArray[1] = new JsonElement<MyCustomDataType>(myCustomDataType); // No implicit operator for custom types (unless it adds its own)

string s = (string)jArray[0]; // Uses explicit operator.
MyCustomDataType m = jArray[1].GetValue<MyCustomDataType>(); // no explicit operator for custom types  (unless it adds its own).
```
with dynamic:
```cs
JsonArray jArray = ...
jArray[0] = "hello"; // Uses implicit operator.
jArray[1] = myCustomDataType; // Possible through dynamic.

string s = jArray[0]; // Possible through dynamic.
MyCustomDataType m = jArray[1]; // Possible through dynamic.
```

## Specifying the `JsonSerializerOptions` instance
The `JsonSerializerOptions` instance needs to be specified in order for any run-time added custom converters to be found, to support `PropertyNameCaseInsensitive` for property lookup, and other options such as `NumberHandling`.

It is cumbersome and error-prone to specify the options instance for every new element or property. Consider the verbose syntax:
```cs
// Verbose syntax (not recommended, but still possible in the API)
var jObject = new JsonObject(options)
{
    ["MyString"] = new JsonValue<string>("Hello!", options),
    ["MyBoolean"] = new JsonValue<bool>(false, options),
    ["MyArray"] = new JsonArray(options)
    {
        new JsonValue(2, options),
        new JsonValue(3, options),
        new JsonValue(42, options)
    },
}
```
and the terse syntax which omits the options instance for non-root members. When a property or element is added, the options instance is set to the parent's instance:
```cs
// Terse syntax
var jObject = new JsonObject(options) // options still needed at root level
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

Defaulting the options also allows support for implicit operators and `JsonArray` support for `params`:
```cs
var jObject = new JsonObject(options) // options still needed at root level
{
    ["MyString"] = "Hello!",
    ["MyBoolean"] = false,
    ["MyArray"] = new JsonArray(2, 3, 42)
}
```

It is required that all option instances are the same across a given `JsonNode` tree. An `InvalidOperationException` will be thrown when a mismatch is detected.

## Adding `JsonNode` indexers for objects\arrays and GetValue() for values
`JsonNode` has indexers that allow `JsonObject` properties and `JsonArray` elements to be specified. This allows a terse mode:
```cs
    var jObject = new JsonObject(options)
    {
        // Terse programming model:
        ["Child"]["Array"][0]["Message"] = "Hello!";
        ["Child"]["Array"][1]["Message"] = "Hello!!";
    }
```

If `System.Object` was used instead of `JsonNode`, or the indexers were not exposed on `JsonNode`, the above terse syntax wouldn't work and a more verbose syntax would be necessary:
```cs
    object jObject = new JsonObject(options)
    {
        // Programming model not acceptable:
        ((JsonObject)((JsonArray)((JsonObject)jObject["Child"])["Array"])[0])["Message"] = "Hello!";
        ((JsonObject)((JsonArray)((JsonObject)jObject["Child"])["Array"])[1])["Message"] = "Hello!!";
    }
```

A `JsonNode.GetValue<T>()` helper method also exists for `JsonValue<T>`.

## Missing vs. null
The indexer for `JsonObject` returns `null` for missing properties. This aligns with:
- Expected support for `dynamic`.
- Newtonsoft.
- The serializer today. Also when deserializing JSON to `System.Object`, `null` is returned today, not a `JsonElement` with `JsonValueKind.Null`.

However, for some scenarios, it is important to distinguish between a `null` value deserialized from JSON vs. a missing property. Since `JsonObject` implements `IDictionary<string, JsonNode>` it can be inspected:
```cs
bool found = jObject.TryGetValue("NonExistingProperty", out object _);
```

## Allowing any CLR type in JsonValue<T>
Although `JsonValue<T>` is intended to support simple value types, any CLR value including POCOs and various collection types can be specified, assuming they are supported by the serializer.

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
    JsonArray allOrders = JsonArray.Parse(Linq_Query_Json);
    IEnumerable<JsonNode> orders = allOrders.Where(o => o["Customer"]["City"].GetValue<string>() == "Fargo");

    Debug.Assert(2 == orders.Count());
    Debug.Assert(100 == orders.ElementAt(0)["OrderId"].GetValue<int>());
    Debug.Assert(300 == orders.ElementAt(1)["OrderId"].GetValue<int>());
    Debug.Assert("Customer1" == orders.ElementAt(0)["Customer"]["Name"].GetValue<string>());
    Debug.Assert("Customer3" == orders.ElementAt(1)["Customer"]["Name"].GetValue<string>());
}

{
    // Query for orders with dynamic
    IEnumerable<dynamic> allOrders = (IEnumerable<dynamic>)JsonArray.Parse(Linq_Query_Json);
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

Accessing properties and elements in a read-only manner does not require `JsonNode` instances to be created. This is important to improve performance primarily for very large collections. This can be achieved by obtaining the `JsonElement` via `JsonNode.AsJsonElement()` and enumerating through existing APIs on `JsonElement`. No `JsonNode` instances need to be created in this case.

## Boxing and mutating of `Value<T>`
The design of values uses generics to hold internal `<T>` value. This avoids boxing for value types. A given `Value<T>`, such as `Value<int>` can be modified by the `Value` property which can be used to avoid creation of a new `JsonValue` instance.

## Property lookup
The property-lookup algorithm can be made more efficient than the standard dictionary by using ordering heuristics (like the current serializer) or a bucketless B-Tree with no need to call `Equals()`. The Azure `dynamic` prototyping effort has been thinking of the B-Tree approach.

# Features not proposed in 6.0
These feature are reasonable, but not proposed (at least for the first round):
- Adding `public byte[] ToUtf8Bytes()` to JsonNode to serialize as UTF-8 (as a mirror method for `ToJsonString()`). This can be added later if necessary; the workaround is fairly easy.
- Reading\writing JSON comments. Not implemented since `JsonDocument` doesn't support it, although the reader and writer support them. 
  - Cost to implement is not known (no known prototype or PR).
  - For the public API, a `JsonValueKind.Comment` enum value would be added and a setter added to `JsonNode.ValueKind` (the getter is already there).
  - A comment would be represented by a `JsonValue<string>` with the `ValueKind` property value == `JsonValueKind.Comment`
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
- `JsonNode.ReadFromAsync()` and `WriteToAsync()`. Adding every current and future variant doesn't seem like the best action. Note the corresponding serializer methods (including the async ones) can be used instead of the helpers on `JsonNode`.
- Support for `System.ComponentModel.TypeConverter` in the `GetValue<TypeToReturn>()` method or another new method. Currently the serializer does not support this either.
- Support for reference handling because:
  - Performance. When reference handling is enabled, all nodes must be materialized eagerly.
  - Likely not expected since a DOM is lower-level than an object graph. Consumers would expect the metadata to be available to them programmatically, not hidden (although it could be an option).
- `Equals()`, `==` and `!=` operators. These would be slow since they imply a deep compare in non-trivial cases. The `IEqualityComparer<T>` mechanism is more suitable. It would be useful for those with a custom dictionary or similar cases; it would likely use the case senstivity options for comparing property names.
- `ToString()`. Similar to `Equals` etc above, this would be very slow since it would imply serialization. `ToJsonString()` is the proposal here instead. Note that for debugging, a `[DebuggerDisplay]` attribute can be added to `JsonNode` that can perform a limited `ToJsonString()` such as the first 100 characters or so.

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
- [ ] Prototype and usabilty study?
- [ ] Ensure any future "new dynamic" C# support (being considered for intellisense support based on schema) is forward-compatible with the work here which uses the "old dynamic".
