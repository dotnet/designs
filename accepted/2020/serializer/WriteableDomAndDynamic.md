# Overview
February 10th, 2021.

This document covers the API and design for a writable DOM along with support for the [C# `dynamic` keyword](https://docs.microsoft.com/en-us/dotnet/framework/reflection-and-codedom/dynamic-language-runtime-overview).

It is expected that a significant percent of existing System.Text.JSON consumers will use these new APIs, and also attract new consumers that need this functionality.

The types of consumers for a writeable DOM are essentially the same as those who use `JsonDocument` today, plus those that need the writable characteristics:
- Unable to use the serializer for varying reasons:
  - Heavyweight; requires compilation of POCO types.
  - Limitations in the serializer such as polymorphism.
  - Schema is not fixed and must be inspected.
- Desire for a lightweight, simple API especially for one-off cases.
- To efficiently read very large graphs, objects or arrays.
- To efficiently modify a subset of a very large graph.

## High-level API
Represented by an abstract base class `JsonNode` along with derived classes for objects, arrays and values:
```cs
namespace System.Text.Json.Serialization
{
    public abstract class JsonNode {...};
    public class JsonObject : JsonNode, IDictionary<string, JsonNode?> {...}
    public class JsonArray : JsonNode, IList<JsonNode?> {...};
    public abstract class JsonValue : JsonNode {...};
    public class JsonValue<T> : JsonValue {...};

    // For C# 'dynamic' we implement System.Dynamic.IDynamicMetaObjectProvider.
    // These are separate classes so the ILLinker can remove the reference to System.Linq.Expressions.
    public sealed class JsonDynamicObject : JsonObject, System.Dynamic.IDynamicMetaObjectProvider {...};
    public sealed class JsonDynamicArray : JsonArray, System.Dynamic.IDynamicMetaObjectProvider {...};
    public sealed class JsonDynamicValue : JsonValue<object>, System.Dynamic.IDynamicMetaObjectProvider {...};
}
```

## Background
The existing `JsonDocument` and `JsonElement` types represent the DOM support today which is read-only. It maintains a single immutable UTF-8 buffer and returns `JsonElement` value types from that buffer on demand. This design minimizes the initial `JsonDocument.Parse()` time and associated heap allocs but also makes it slow to re-obtain the same value (which is common with LINQ) and does not lend itself to being extended to support writability directly. Although the DOM is read-only, the `JsonNode` design supports interactions between `JsonElement` and `JsonNode` by creating new `JsonElement` instances from `JsonNode` instances.

In general, adding support for C# `dynamic` would mean adding a writeable DOM. So considering both dynamic and non-dynamic "writeable DOM" scenarios in a single design allows for a common API, shared code and intuitive interop between the two.

This design is based on learning and scenarios from:
- The writable DOM prototype.
  - During 5.0, there was a [writable DOM effort](https://github.com/dotnet/runtime/issues/30436). Unlike `JsonDocument`, the design centered around writable aspects, usability and LINQ support, and not performance and allocations. It was shelved due to time constraints and outstanding work.
     - What's the same:
       - The high-level class names `JsonNode`, `JsonArray` and `JsonObject`.
       - Interaction with `JsonElement`.
     - What's different:
       - `JsonValue` instead of `JsonString`, `JsonNumber` and `JsonBoolean`.
       - The writable `JsonValue` internal value is based on CLR types, not a string representing JSON. For example, a `JsonValue` initialized with an `int` keeps that `int` value throughout its lifetime without boxing or converting to a string. However, a string- or byte-based `JsonValue` can still treated as a JSON value and types such as an `int` can be obtained from it.
- The dynamic support code example.
  - During 5.0, [dynamic support](https://github.com/dotnet/runtime/issues/29690) was not able to be implemented due to schedule constraints. Instead, a [code example](https://github.com/dotnet/runtime/pull/42097) was provided that enables `dynamic` and a writeable DOM that can be used by any 3.0+ runtime in order to unblock the community and gather feedback for a 6.0 feature.
- Azure prototyping.
  - The Azure team needs a writable DOM, and supporting `dynamic` is important for some scenarios but not all. A [prototype](https://docs.microsoft.com/en-us/dotnet/api/azure.core.dynamicjson?view=azure-dotnet-preview) has been created. Work is also being coordinated with the C# team to support Intellisense with `dynamic`, although that is not expected to be implemented in time to be used in 6.0.
- Newtonsoft
  - Json.NET has a similar `JToken` class hierarchy and support for `dynamic`. The `JToken` hierarchy includes a single `JValue` type to represent all types, like is being proposed here (instead of separate types for string, number and boolean). Json.NET also has implicit and explicit operators similar as to what is being proposed here.
  - Converting Json.NET code to System.Text.Json should be fairly obvious although not all Json.NET features are implemented. See the "Features not proposed in first round" section for more information.

# API overview and design
A deserialized `JsonNode` value internally contains the JSON token type (object, array, number, string, true, false) and the raw UTF8 value. Internally this is stored as a `JsonElement`.

When the consumer obtains the value through a method such as `jvalue.To<Double>()` the CLR `Double` value is deserialied from the raw UTF8. Thus the mapping between UTF8 JSON and the CLR object model is deferred. Deferring is consistent with `JsonElement` semantics where, for example, a JSON number is not automatically mapped to any CLR type until the consumer calls a method such as `JsonElement.GetDecimal()` or `JsonElement.GetDouble()`.

When deserializing a `JsonNode`, the internal value is backed by a `JsonElement`:
```cs
JsonObject jObject = JsonObject.Parse("{""MyProperty"":42}");
JsonValue jValue = jObject["MyProperty"];
Debug.Assert(jValue is JsonValue<JsonElement>);
int i = jValue.To<int>();
```

A `JsonValue` that is returned from deserialization is based on a `JsonElement`. To change the value to a CLR type:
```cs
jObject["MyProperty"] = new JsonValue<int>(43);
```

A `JsonValue` supports custom converters that were added via `JsonSerializerOptions.Converters.Add()`. A common use case would be to add the `JsonStringEnumConverter` which serializes `Enum` values as a string instead of an integer, but also user-defined custom converters:
```cs
var money = new Money(1.23); // Money is a custom data type
jObject["MyProperty"] = new JsonValue<MoneyCustomType>(money);
```

During serialization, the custom converter for `Money` will be called to produce the JSON.

Although `JsonValue<T>` is intended to support simple value types, any CLR value including POCOs and various collection types can be specified, assuming they are supported by the serializer. If a POCO or collection is specified in a `JsonValue<T>`, when a `JsonNode` is deserialized it will be either a `JsonObject` or `JsonArray`.

During serialization, some `JsonSerializerOptions` are supported including quoted numbers and null handling. See the "Interop" "Serialization" section.

## Why only a `JsonValue` and not `JsonNumber`, `JsonString` and `JsonBoolean`?
The JSON primitives (number, string, true, false) do **not** have their own `JsonNode`-derived type like `JsonNumber`, `JsonString` and `JsonBoolean`. Instead, a common `JsonValue` class represents them. This allows a JSON number, for example, to serialize and deserialize as either a CLR `String` or a `Double` depending on usage and options. Without this support, for example, consider this **hypothetical API** that has a `JsonNumber` class (which again is not the proposal):
```cs
// Hypothetical programming model (not proposed).

// Obtain node with a JSON number token type.
JsonNumber number = JsonNode.Parse<JsonNumber>("1.2");
double dlb = number.To<double>();

// Change to "NaN".
number.Value = double.Nan;
string json = number.Write();
Debug.Assert(json == "\"NaN\""); // Due to quoted number support, this could be made to work.

number = JsonNode.Parse<JsonNumber>(json); // Throws
```

The last line throws since a `JsonString` instance, not a `JsonNumber` would be created by default. Even if the above could be made to work by the knowledge of `<T>` being `JsonNumber`, and not a `JsonString`, it wouldn't work when deserializing a property on a `JsonObject` node. For example, a `JsonObject` would create a `JsonString` for a "NaN" JSON string since it has no way of knowing these should map to a `JsonNumber` (or a CLR number such as `double`).

The proposed programming is shown below. There a JSON "NaN" string is deserialized into a `JsonValue` and when `To<double>` is called, a `double` is returned, assuming the appropriate `JsonSerializerOptions` are configured for quoted numbers. When the node is serialized, it produces a JSON string (again, if configured). The consumer of the DOM is not necessarily aware that `double` values assigned to the node may be serialized to JSON as either a string or a JSON number.
```cs
// Proposed programming model.

// Write numbers as strings.
JsonSerializerOptions options = new JsonSerializerOptions();
options.NumberHandling = JsonNumberHandling.WriteAsString;

// Obtain node with a JSON number token type.
JsonNode number = JsonNode.Parse("1.2", options);
Debug.Assert(number is JsonValue);
double dlb = number.To<double>();

// Change to "NaN".
number = double.Nan; // There is an implicit conversion from double to JsonNode<double>.
string json = number.Write();
Debug.Assert(json == "\"NaN\"");

// Round-tripping works fine even with a quoted number.
number = JsonNode.Parse(json);
```

## Primary API
All types live in STJ.dll.
```cs
namespace System.Text.Json.Serialization
{
    public abstract class JsonNode
    {
        internal JsonNode(); // prevent external derived classes.

        public JsonNode Clone(); // Deep clone.

        public JsonSerializerOptions Options { get; }

        // JsonArray terse syntax support (no JsonArray cast necessary).
        public virtual JsonNode? this[int index] { get; set; }

        // JsonObject terse syntax (no JsonObject cast necessary).
        public virtual JsonNode? this[string propertyName] { get; set; }

        // Return the parent and root nodes; useful for LINQ.
        // These are set internally once added to a JsonObject or a JsonElement.
        public JsonNode? Parent { get; }
        public JsonNode? Root { get; }

        // The JSON Path; same format we use for JsonException information.
        public string Path { get; }

        // Return the internal value or convert to the provided type as necessary.
        public abstract TypeToReturn To<TypeToReturn>();
        public abstract bool TryTo<TypeToReturn>(out TypeToReturn? value);

        // Convert a non-dynamic node to a dynamic.
        // Not used internally; the ILLinker will remove reference to if not used which is important
        // here since 'dynamic' implies reference to System.Linq.Expressions.
        public dynamic ToDynamic();

        // Serialize\deserialize wrappers. These are helpers and thus are an optional API feature.
        // Parse() terminology consistent with Utf8JsonReader\JsonDocument.
        // WriteTo() terminology consistent with Utf8JsonWriter.
        // Also consider naming variants:
        // - Serialize() \ Deserialize()
        // - FromJson() \ ToJson()
        public string ToJsonString();
        public byte[] ToUtf8Bytes();
        public void WriteTo(System.Text.Json.Utf8JsonWriter writer);
        public Task WriteToAsync(Stream utf8Json, CancellationToken cancellationToken = default);

        // The token type from deserialization; for new instances JsonValueKind.Unspecified.
        public JsonValueKind ValueKind { get; }

        public static JsonNode? Parse(string? json, JsonSerializerOptions options = null);
        public static JsonNode? ParseUtf8Bytes(ReadOnlySpan<byte> utf8Json, JsonSerializerOptions options = null);
        public static JsonNode? ReadFrom(ref Utf8JsonReader reader, JsonSerializerOptions options = null);

        // JsonElement interop
        public static JsonNode GetNode(JsonElement jsonElement);
        public static bool TryGetNode(JsonElement jsonElement, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out JsonNode? jsonNode);

        // Do we want to have non-generic overloads? They will not be fast since the internal representation is a generic <T> value:
        // public virtual object To(Type type, int index);
        // public virtual object To(Type type, string propertyName);
        // public abstract object To(Type type));
        // public abstract bool TryTo(Type type, out object? value);

        // Do we want ToString() to return the JSON? Same as ToJsonString above.
        // public override string ToString();

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

    public class JsonArray : JsonNode, IList<JsonNode?>
    {
        public JsonArray(JsonSerializerOptions? options = null);
        public JsonArray(JsonElement jsonElement, JsonSerializerOptions? options = null);

        // Param-based constructors for easy constructor initializers.
        public JsonArray(JsonSerializerOptions? options, params JsonNode[] items);
        public JsonArray(params JsonNode[] items);

        public override JsonNode Clone();

        // Can be used to return:
        // - Any compatible collection type
        // - JsonElement
        // - string or byte[] (as JSON)
        public override TypeToReturn To<TypeToReturn>();
        public override bool TryTo<TypeToReturn>(out TypeToReturn? value);

        public override void WriteTo(Utf8JsonWriter writer);

        // IList<JsonNode?> implemention (some hidden with explicit implementation):
        public int Count { get ;}
        bool ICollection<System.Text.Json.Serialization.JsonNode?>.IsReadOnly { get ;}
        public void Add(object? item);
        public void Add(JsonNode? item);
        public void Clear();
        public bool Contains(JsonNode? item);
        public IEnumerator<System.Text.Json.Serialization.JsonNode?> GetEnumerator();
        public override TypeToReturn To<TypeToReturn>();
        public int IndexOf(JsonNode? item);
        public void Insert(int index, JsonNode? item);
        public bool Remove(JsonNode? item);
        public void RemoveAt(int index);
        void ICollection<JsonNode?>.CopyTo(System.Text.Json.Serialization.JsonNode?[]? array, int arrayIndex);
        IEnumerator IEnumerable.GetEnumerator();

        public static JsonArray? Parse(string? json, JsonSerializerOptions options = null);
        public static JsonArray? ParseUtf8Bytes(ReadOnlySpan<byte> utf8Json, JsonSerializerOptions options = null);
        public static JsonArray? ReadFrom(ref Utf8JsonReader reader, JsonSerializerOptions options = null);
    }

    public class JsonObject : JsonNode, IDictionary<string, JsonNode?>
    {
        public JsonObject(JsonSerializerOptions options = null);
        public JsonObject(JsonElement jsonElement, JsonSerializerOptions? options = null);

        public override JsonNode Clone();

        // Can be used to return:
        // - Any compatible dictionary type
        // - POCO including anonymous type or record
        // - JsonElement
        // - string or byte[] (as JSON)
        public override T To<T>();
        public override bool TryTo<T>(out T? value);

        public bool TryGetPropertyValue(string propertyName, outJsonNode? jsonNode);

        public override void WriteTo(Utf8JsonWriter writer);

        // IDictionary<string, JsonNode?> implemention (some hidden with explicit implementation):
        public int Count { get; }
        bool ICollection<KeyValuePair<string,JsonNode?>>.IsReadOnly { get; }
        ICollection<string> IDictionary<string,JsonNode?>.Keys { get; }
        ICollection<JsonNode?> IDictionary<string,JsonNode?>.Values { get; }
        public void Add(string propertyName,JsonNode? value);
        public void Clear();
        public bool ContainsKey(string propertyName);
        public IEnumerator<KeyValuePair<string,JsonNode?>> GetEnumerator();
        public override TypeToReturn To<TypeToReturn>();
        public bool Remove(string propertyName);
        void ICollection<KeyValuePair<string,JsonNode?>>.Add(KeyValuePair<string,JsonNode> item);
        bool ICollection<KeyValuePair<string,JsonNode?>>.Contains(KeyValuePair<string,JsonNode> item);
        void ICollection<KeyValuePair<string,JsonNode?>>.CopyTo(KeyValuePair<string,JsonNode>[] array, int arrayIndex);
        bool ICollection<KeyValuePair<string,JsonNode?>>.Remove(KeyValuePair<string,JsonNode> item);
        IEnumerator IEnumerable.GetEnumerator();
        bool IDictionary<string,JsonNode?>.TryGetValue(string propertyName, outJsonNode? jsonNode);

        public static JsonObject? Parse(string? json, JsonSerializerOptions options = null);
        public static JsonObject? ParseUtf8Bytes(ReadOnlySpan<byte> utf8Json, JsonSerializerOptions options = null);
        public static JsonObject? ReadFrom(ref Utf8JsonReader reader, JsonSerializerOptions options = null);
    }

    public abstract class JsonValue : JsonNode
    {
        public JsonValue(JsonSerializerOptions options = null);
    }

    public class JsonValue<T> : JsonValue
    {
        public JsonValue(T value, JsonSerializerOptions options = null);

        public override JsonNode Clone();

        public override TypeToReturn To<TypeToReturn>();
        public override bool TryTo<TypeToReturn>(out TypeToReturn value);

        // The internal raw value.
        public override T Value {get; set;}

        public override void WriteTo(Utf8JsonWriter writer);

        public static JsonValue<T> Parse(string? json, JsonSerializerOptions options = null);
        public static JsonValue<T> ParseUtf8Bytes(ReadOnlySpan<byte> utf8Json, JsonSerializerOptions options = null);
        public static JsonValue<T> ReadFrom(ref Utf8JsonReader reader, JsonSerializerOptions options = null);
    }

    // These types are used with dynamic support.
    // JsonSerializerOptions.EnableDynamicTypes must be called before using.
    // These types are public for "edit mode" scenarios when using dynamic.
    public sealed class JsonDynamicObject : JsonObject, System.Dynamic.IDynamicMetaObjectProvider
    {
        public JsonDynamicObject(JsonSerializerOptions? options = null);

        System.Dynamic.DynamicMetaObject System.Dynamic.IDynamicMetaObjectProvider.GetMetaObject(System.Linq.Expressions.Expression parameter);

        public static JsonDynamicObject Parse(string? json, JsonSerializerOptions options = null);
        public static JsonDynamicObject ParseUtf8Bytes(ReadOnlySpan<byte> utf8Json, JsonSerializerOptions options = null);
        public static JsonDynamicObject ReadFrom(ref Utf8JsonReader reader, JsonSerializerOptions options = null);
    }

    public sealed class JsonDynamicArray : JsonArray, System.Dynamic.IDynamicMetaObjectProvider
    {
        public JsonDynamicArray(JsonSerializerOptions? options = null);
        public JsonDynamicArray(JsonSerializerOptions? options, params JsonNode[] items);
        public JsonDynamicArray(params JsonNode[] items);

        System.Dynamic.DynamicMetaObject System.Dynamic.IDynamicMetaObjectProvider.GetMetaObject(System.Linq.Expressions.Expression parameter);

        public static JsonDynamicArray Parse(string? json, JsonSerializerOptions options = null);
        public static JsonDynamicArray ParseUtf8Bytes(ReadOnlySpan<byte> utf8Json, JsonSerializerOptions options = null);
        public static JsonDynamicArray ReadFrom(ref Utf8JsonReader reader, JsonSerializerOptions options = null);
    }

    public sealed class JsonDynamicValue : JsonValue<object>, System.Dynamic.IDynamicMetaObjectProvider
    {
        public JsonDynamicValue(object? value, JsonSerializerOptions? options = null);

        System.Dynamic.DynamicMetaObject System.Dynamic.IDynamicMetaObjectProvider.GetMetaObject(System.Linq.Expressions.Expression parameter);

        public static JsonDynamicValue Parse(string? json, JsonSerializerOptions options = null);
        public static JsonDynamicValue ParseUtf8Bytes(ReadOnlySpan<byte> utf8Json, JsonSerializerOptions options = null);
        public static JsonDynamicValue ReadFrom(ref Utf8JsonReader reader, JsonSerializerOptions options = null);
    }
}
```
## Serializer interop
For serializing a `JsonNode` instance to JSON, instance helpers on `JsonNode` are provided that pass in the value and options:
```cs
JsonObject jObject = ...

// short version:
string json = jObject.ToJsonString();

// equivalent longer versions:
string json = JsonSerializer.Serialize(jObject, jObject.GetType(), jObject.Options);
string json = JsonSerializer.Serialize<JsonObject>(jObject, jObject.Options);
string json = JsonSerializer.Serialize<JsonNode>(jObject, jObject.Options);
```

Serializer-specific functionality including custom converters and quoted numbers just work.

The `JsonSerializerOptions` is passed to `JsonNode` and used to specify any run-time added custom converters, handle other options such as quoted numbers, and to respect the `PropertyNameCaseInsensitive` to determine whether the string-based lookup of property names on `JsonObject` are case-sensitive (for both direct use of `JsonObject` and also indirect use through `dynamic`). If we have `static JsonValue Parse()` methods, those would also need an override to specify the `JsonSerializerOptions`.

Although the serializer can be used to directly (de)serialize `JsonNode` types, once a `JsonNode` instance is created, it is expected the `JsonNode.To<T>()` method is used to change types instead of using the serializer:
```cs
    JsonNode node = ... // any value

    // This works, but not intuitive:
    byte[] utf8 = node.ToUtf8Bytes();
    string str = JsonSerializer.Deserialize<string>(utf8, options);

    // For ease-of-use and performance, use To().
    // It may return the raw internal value or the last converted value instead of deserializing.
    string str = node.To<string>();
```

## Deserialize interop
For deserializing JSON to a `JsonNode`, static helpers on `JsonNode` are provided:
```cs
JsonSerializerOptions options = ...
string json = ...
JsonNode jNode;

// short version:
jNode = JsonNode.Parse(json, options);

// equivalent longer version:
jNode = JsonSerializer.Deserialize<JsonNode>(json, options);
```

## Cast operators
No explicit or implicit cast operators are *required* since `To<T>` allows for a single, consistent programming model. This works on "known" types in the CLR such as `String` and also custom data types known by the serializer.

However, based on usability of a terse syntax, the use of both explicit cast operators (from a `JsonNode` to known primitives) and implicit cast operators (from known primitives to a `JsonNode<T>`) are supported.

Note that cast operators do not work in all languages such as F#.

With cast operators:
```cs
JsonArray jArray = ...
jArray[0] = "hello"; // Possible though an implicit operator.
jArray[1] = new JsonElement<MyCustomDataType>(myCustomDataType); // No cast operator; this wouldn't compile.

string v0 = (string)jArray[0]; // Possible though an explicit operator.
MyCustomDataType v1 = jArray[1].To<MyCustomDataType>();
```

Explicit:
```cs
JsonArray jArray = ...
jArray[0] = new JsonValue<string>("hello"); 
jArray[1] = new JsonElement<MyCustomDataType>(myCustomDataType); // No cast operator; this wouldn't compile.

string v0 = jArray[0].To<string>();
MyCustomDataType v1 = jArray[1].To<MyCustomDataType>();```

Dynamic:
```cs
JsonDynamicArray jArray = ...
jArray[0] = "hello";
jArray[1] = myCustomDataType; // Possible through dynamic.

string v0 = jArray[0];
MyCustomDataType v1 = jArray[1]; // Possible through dynamic.
```

### Specifying the `JsonSerializerOptions` instance
The `JsonSerializerOptions` instance needs to be specified in order for any run-time added custom converters to be found, to support `JsonSerializerOptions.PropertyNameCaseInsensitive` for property lookup with `JsonObject` and to support other options such as `JsonSerializerOptions.NumberHandling = JsonNumberHandling.WriteAsString`.

It is cumbersome and error-prone to specify the options instance for every new element or property. Consider this hypothetical programming model:
```cs
// Hypothetical programming model (not proposed)
var jObject = new JsonObject(options)
{
    ["MyString"] = new JsonValue("Hello!", options),
    ["MyBoolean"] = new JsonValue(false, options),
    ["MyArray"] = new JsonArray(options)
    {
        new JsonValue(2, options),
        new JsonValue(3, options),
        new JsonValue(42, options)
    },
}
```
so it is possible to omit the options instance for non-root members. When a property or element is added, the options instance is set to the parent's instance:
```cs
// Proposed
var jObject = new JsonObject(options) // options still needed at root level
{
    ["MyString"] = new JsonValue("Hello!"),
    ["MyBoolean"] = new JsonValue(false),
    ["MyArray"] = new JsonArray()
    {
        new JsonValue(2),
        new JsonValue(3),
        new JsonValue(42)
    },
}
```

It is required that all option instances are the same across a given `JsonNode` tree. An `InvalidOperationException` will be thrown when a mismatch is detected.

### Constructor initialization
todo including arrays

### Using `System.Object` for values
`JsonNode` has indexers that allow `JsonObject` properties and `JsonArray` elements to be specified. This allows a terse and more performant mode:
```cs
    var jObject = new JsonObject(options)
    {
        ["Child"]["Array"][0]["Message"] = new JsonValue("Hello!");
        ["Child"]["Array"][1]["Message"] = new JsonValue("Hello!!");
    }
```

If `System.Object` was used instead of `JsonNode`, or the indexers were not exposed on `JsonNode`, the above terse syntax wouldn't work and a more verbose syntax would be necessary:
```cs
    object jObject = new JsonObject(options)
    {
        ((JsonObject)((JsonArray)((JsonObject)jObject["Child"])["Array"])[0])["Message"] = new JsonValue("Hello!");
        ((JsonObject)((JsonArray)((JsonObject)jObject["Child"])["Array"])[1])["Message"] = new JsonValue("Hello!!");
    }
```

During serialization, the appropriate converter is used to serialize either the primitive value or the underlying value held by a `JsonNode`.

It is also possible to take a given CLR type and assign it to `Value`:
``` cs
    JsonArray jArray = ...
    jArray.Value = new int[] {0, 1}; // raw array used instead of JsonValue
    string json = jArray.Write();
```

## Changing the deserialization of System.Object
The serializer has always deserialized JSON that maps to a `System.Object` as a `JsonElement`. This will remain the default behavior going forward.

However one potential option, **currently not proposed**, is to add the option:

```cs
namespace System.Text.Json
{
    public partial class JsonSerializerOptions
    {
        public bool UseJsonNodeForUnknownTypes { get; set;}
    }
}
```

Setting this to true will create `JsonNode`-derived types instead of `JsonElement`.

This setting would have no effect if `JsonSerializerOptions.EnableDynamicTypes()` is called since it overrides the handling of unknown types. 

The reason it is not proposedy yet:
- It hasn't been requested yet.
- If a given POCO type is owned, it may be possible to change the signature to return `JsonNode` instead of `System.Object`.
- It overlaps with possible future polymorphic support and needs to be vetted against that, including other new options. When polymorphic deserialization is added as a separate feature, the meaning may change slightly to handle the case of a type being deserialized with JSON "known type metadata" specifying an unknown or unhandled type on the CLR side. i.e. for polymorphic deserialization, a `System.Object` property during deserialization may be set to a concrete POCO type and not `JsonNode` or `JsonElement` even when `UseJsonNodeForUnknownTypes == true`.

## Extension data
The serializer has always supported placing "overflow" properties in JSON into a dictionary on a property that has the `[JsonExtensionData]` attribute. The property itself can either be declared in several ways:
```cs
    // Existing support:
    [JsonExtensionData] public Dictionary<string, object> ExtensionData {get; set;}
    [JsonExtensionData] public IDictionary<string, object> ExtensionData {get; set;}
    [JsonExtensionData] public Dictionary<string, JsonElement> ExtensionData {get; set;}
    [JsonExtensionData] public IDictionary<string, JsonElement> ExtensionData {get; set;}
```

Going forward, `JsonObject` can also be specified as the property type, which is useful if the missing properties should be writable or support LINQ:
```cs
    // New support:
    [JsonExtensionData] public JsonObject ExtensionData {get; set;};
```

Note that `JsonObject` implements `IDictionary<string, JsonNode>`.

To prevent unnecessary permutations, this is **not** supported:
```cs
    // Not to be supported:
    [JsonExtensionData] public IDictionary<string, JsonNode> ExtensionData {get; set;}
    [JsonExtensionData] public Dictionary<string, JsonNode> ExtensionData {get; set;}
```

## Missing vs. null
The indexer for `JsonObject` and `JsonDynamicObject` return `null` for missing properties. This aligns with:
- Expected support for `dynamic`.
- Newtonsoft.
- The serializer today when deserializing JSON mapping to `System.Object`; i.e. a `JsonElement` with `JsonValueKind.Null` is not created.

However, for some scenarios, it is important to distinguish between a `null` value deserialized from JSON vs. a missing property. Since `JsonObject` implements `IDictionary<string, JsonNode>` it can be inspected:
```cs
JsonObject jObject = ...
bool found = jObject.TryTo("NonExistingProperty", out object value);
```

An alternative, not currently recommended, is to add a `JsonNull` node type along with a `JsonSerializerOptions.DeserializeNullAsJsonNull` option to configure.

## Interop with JsonElement
The constructors of `JsonNode` allow any type to be specified including a `JsonElement`.

`JsonNode.To<JsonElement>()` can be used to obtain the current `JsonElement`, or create one if the internal value is not a `JsonElement`.

## New DOM
```cs
var options = new JsonSerializerOptions();
var jObject = new JsonObject(options)
{
    ["MyString"] = new JsonValue("Hello!"),
    ["MyNull"] = null,
    ["MyBoolean"] = new JsonValue(false),
    ["MyInt"] = new JsonValue(43),
    ["MyDateTime"] = new JsonValue(new DateTime(2020, 7, 8)),
    ["MyGuid"] = new JsonValue(new Guid("ed957609-cdfe-412f-88c1-02daca1b4f51")),
    ["MyArray"] = new JsonArray(options)
    {
        new JsonValue(2),
        new JsonValue(3),
        new JsonValue(42)
    },
    ["MyObject"] = new JsonObject()
    {
        ["MyString"] = new JsonValue("Hello!!")
    },
    ["Child"] = new JsonObject()
    {
        ["ChildProp"] = new JsonValue(1)
    }
};

string json = jObject.Write();
}
```
## New DOM + dynamic
```cs
var options = new JsonSerializerOptions();
options.EnableDynamicTypes();

dynamic jObject = new JsonDynamicObject(options);
jObject.MyString = "Hello!"; // "Hello" internally converted to 'new JsonValue("Hello!")'
jObject.MyNull = null;
jObject.MyBoolean = false;
jObject.MyInt = 43;
jObject.MyDateTime = new DateTime(2020, 7, 8);
jObject.MyGuid = new Guid("ed957609-cdfe-412f-88c1-02daca1b4f51");
jObject.MyArray = new int[] { 2, 3, 42 };
jObject.MyObject = new JsonDynamicObject();
jObject.MyObject.MyString = "Hello!!"
jObject.MyObject.Child = new JsonDynamicObject();
jObject.MyObject.Child.ChildProp = 1;

string json = jObject.Write();
```

## Number
To:
```cs
JsonValue jValue = JsonNode.Parse<JsonValue>("42");

int i = jValue.To<int>();
double d = jValue.To<double>();

// Any type with a converter can be used.
MyEnum enum = jValue.To<MyEnum>();

// Casts are NOT supported (doesn't compile):
double d2 = (double)jValue;
double d3 = jValue;
```

Setting values:
```cs
JsonValue jValue = new JsonValue(42);
int i1 = (int)jValue.Value; // cast is not always safe since returning internal value
int i2 = jValue.To<int>(); // safe since a conversion occurs if necessary

jValue.Value = 3.14;

// A quoted number can also be specified.
jValue.Value = "42";
int i3 = jValue.To<int>();
Debug.Assert(i3 = 42);

// Or an enum
jValue = (MyEnum)42;
MyEnum myEnum = jValue.To<MyEnum>();
```

## String
```cs
JsonValue jValue = JsonNode.Parse<JsonValue>("Hello");

string s1 = (string)jValue.Value; // not always safe
string s2 = jValue.To<string>(); // safe
Enum e = jValue.To<MyEnum>();
```

## Boolean
```cs
JsonValue jValue = JsonNode.Parse<JsonValue>("true");

bool b1 = (bool)jValue.Value; // not always safe
bool b2 = jValue.To<bool>(); // safe
MyBoolCompatibleType o = jValue.To<MyBoolCompatibleType>();
```

## LINQ
`JsonObject` and `JsonArray` implement `IEnumerable` so there is no extra work for LINQ there. In addition, the `JsonNode.Parent` property can be used to help query parent:child relationships.

```cs
private class BlogPost
{
    public string Title { get; set; }
    public string AuthorName { get; set; }
    public string AuthorTwitter { get; set; }
    public string Body { get; set; }
    public DateTime PostedDate { get; set; }
}

[Fact]
public static void DynamicObject_LINQ_Convert()
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
        Title = p["Title"].To<string>(),
        AuthorName = p["Author"]["Name"].To<string>(),
        AuthorTwitter = p["Author"]["Mail"].To<string>(),
        PostedDate = p["Date"].To<DateTime>(),
        Body = p["BodyHtml"].To<string>()
    }).ToList();

    const string expected = "[{\"Title\":\"TITLE.\",\"AuthorName\":\"NAME.\",\"AuthorTwitter\":\"MAIL.\",\"Body\":\"Content.\",\"PostedDate\":\"2021-01-20T19:30:00\"}]";

    string json_out = JsonSerializer.Serialize(blogPosts);
    Assert.Equal(expected, json_out);
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

[Fact]
public static void DynamicObject_LINQ_Query()
{
    JsonArray allOrders = (JsonArray)JsonNode.Parse(Linq_Query_Json);
    IEnumerable<JsonNode> orders = allOrders.Where(o => o["Customer"]["City"].GetValue<string>() == "Fargo");

    Assert.Equal(2, orders.Count());
    Assert.Equal(100, orders.ElementAt(0)["OrderId"].To<int>());
    Assert.Equal(300, orders.ElementAt(1)["OrderId"].To<int>());
    Assert.Equal("Customer1", orders.ElementAt(0)["Customer"]["Name"].To<string>());
    Assert.Equal("Customer3", orders.ElementAt(1)["Customer"]["Name"].To<string>());
}

[Fact]
public static void DynamicObject_LINQ_Query_dynamic()
{
    var options = new JsonSerializerOptions();
    options.EnableDynamicTypes();

    IEnumerable<dynamic> allOrders = (IEnumerable<dynamic>)JsonNode.Parse(Linq_Query_Json, options);
    IEnumerable<dynamic> orders = allOrders.Where(o => ((string)o.Customer.City) == "Fargo");

    Assert.Equal(2, orders.Count());
    Assert.Equal(100, (int)orders.ElementAt(0).OrderId);
    Assert.Equal(300, (int)orders.ElementAt(1).OrderId);
    Assert.Equal("Customer1", (string)orders.ElementAt(0).Customer.Name);
    Assert.Equal("Customer3", (string)orders.ElementAt(1).Customer.Name);
}
```

## Interop with custom converter + dynamic
```cs
    internal class PersonConverter : JsonConverter<Person>
    {
        public override Person Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            dynamic jObject = JsonNode.Parse<object>(ref reader, options);

            // Pass a value into the constructor, which was hard to do before in a custom converter
            // since all of the values need to be manually read and cached ahead of time.
            Person person = new Person(jObject.Id)
            {
                Name = jObject.name, // JSON property names exactly match JSON or be case insensitive depending on options.
                AddressLine1 = jObject.addr1, // support 'JsonPropertyName["addr1"]'
                AddressLine2 = jObject.addr2, // support 'JsonPropertyName["addr2"]'
                City = jObject.city,
                State = jObject.state,
                Zip = jObject.zip
            }

            return person;
        }

        public override void Write(Utf8JsonWriter writer, Person value, JsonSerializerOptions options)
        {
            // Default serialization is fine in this example.
            writer.Write(writer, value, options);
        }
    }
```

# Dynamic
The dynamic feature in C# is enabled by both language features and implementation in the `System.Linq.Expressions` assembly. The core interface detected by the language to enable this support is [IDynamicMetaObjectProvider](https://docs.microsoft.com/en-us/dotnet/api/system.dynamic.idynamicmetaobjectprovider?view=net-5.0).

There are implementations of `IDynamicMetaObjectProvider` including [DynamicObject](https://docs.microsoft.com/en-us/dotnet/api/system.dynamic.dynamicobject?view=net-5.0) and [ExpandObject](https://docs.microsoft.com/en-us/dotnet/api/system.dynamic.expandoobject?view=netcore-3.1). However, these implementations are not ideal:
- There are many public members used for wiring up dynamic support, but are of little to no value for consumers so they would be confusing when mixed in with other methods intended to be used for writable DOM support.
- It is not possible to have a single class hierachy that is not tied to `dynamic` and thus `System.Linq.Expressions`:
  - If combined, there would need to be an assembly reference to the very large `System.Linq.Expressions.dll` even when `dynamic` features are not needed -- the ILLinker will not be able to remove the reference to `System.Linq.Expressions` since a base class or implemented interface can't be linked out.
  - If separate standalone classes, the `dynamic` class would likely forward to a separate writable DOM class, which doubles the object instances and increases the concept count around interop between the types returned from `dynamic` vs. the writable DOM classes.
- `ExpandoObject` does not support case insensitivity and throws an exception when accessing missing properties (a `null` is desired instead). This could be fixed, however.
- `DynamicObject` prevents potential optimizations including property-lookup.

Thus, the design presented here for `dynamic` assumes:
- Implementation of `IDynamicMetaObjectProvider`.
- Use of the writable DOM without requiring a reference to `System.Linq.Expressions.dll` (when `dynamic` support is not used).
- Efficient implementation of tying both `IDynamicMetaObjectProvider` and the writable DOM classes into a single instance and type hierarchy.
  - Note that Newtonsoft has a single object model with [JToken](https://www.newtonsoft.com/json/help/html/T_Newtonsoft_Json_Linq_JToken.htm) which implements `IDynamicMetaObjectProvider`.

## Prototype
A prototype for 6.0 that is available at https://github.com/steveharter/runtime/tree/WriteableDomAndDynamic.

## Depending on `System.Linq.Expressions.dll`
Having `System.Text.Json.dll` directly reference `System.Linq.Expressions.dll` is only feasible if the ILLinker can remove the reference to the large `System.Linq.Expressions.dll` when the dynamic functionality is not used.
- Implementations of Blazor and stand-alone apps that do not use `dynamic` features do not want to carry the large `System.Linq.Expressions.dll`.
  - Referencing `System.Linq.Expressions.dll` will require ~5.5MB of disk size. `System.Linq.Expressions.dll` is around 5MB in size but since it also references to `System.Linq` (~400K) and `System.ObjectModel` (~80K) (which that are not referenced by STJ) it becomes ~5.5MB (crossgen'd size).
  - STJ is considering removing its reference to using reflection emit, so referencing `System.Linq.Expressions.dll` would require keeping the the various System.Reflection.* references. Since reflection emit is currently implemented in the runtime (`System.Private.CoreLib`), disk savings by avoiding the API surface layer in `SREmit.ILGeneration` and `SREmit.Lightweight` is limited to ~30K.
- Existing usages of STJ such as by the SDK and Visual Studio that do not use `dynamic` also do not want to carry `System.Linq.Expressions.dll`.

The preferred approach is to ensure ILLinker can remove the dependency to `System.Linq.Expressions.dll` when `dynamic` is not needed. This is achieved by having an opt-in method `EnableDynamicTypes()` on `JsonSerializerOptions` that roots internal usage of `System.Linq.Expressions.dll`.

## Proposed API to enable dynamic mode
All classes to be located in STJ.dll.

```cs
namespace System.Text.Json
{
    public partial class JsonSerializerOptions
    {
        // The root method that if not called, ILLinker will trim out the reference to `System.Linq.Expressions.dll`.
        // STJ does not expose any types from SLE publically.
        // Any System.Object-qualified type specified in a called to JsonNode.Parse() will create either a JsonDynamicArray, JsonDynamicObject or JsonDynamicValue.
        public void EnableDynamicTypes();
    }
}
```

Sample code to enable dynamic types:
```cs
    var options = new JsonSerializerOptions();
    options.EnableDynamicTypes();

    dynamic obj = JsonNode.Parse<JsonNode>("{\"MyProp\":42}", options);
    int i = obj.MyProp;
    Debug.Assert(i == 42);

    string json = obj.Write();
```

### Varying the `T` in `Deserialize<T>`
This
```cs
    dynamic obj = JsonNode.Parse<object>("{\"MyProp\":42}", options);
```
is equivalent to
```cs
    dynamic obj = JsonNode.Parse<dynamic>("{\"MyProp\":42}", options);
```
and these (assuming `options.EnableDynamicTypes()` was called)
```cs
    dynamic obj = JsonNode.Parse<JsonDynamicNode>("{\"MyProp\":42}", options);
    dynamic obj = JsonNode.Parse<JsonDynamicObject>("{\"MyProp\":42}", options);
```

### Supporting non-dynamic to dynamic
If a previously created non-dynamic `JsonNode` object needs dynamic support:
```cs
    JsonNode node = ... // a non-dynamic JsonNode
    dynamic obj = node.ToDynamic();
```

The implementation of `ToDynamic()` is efficient since it will perform a shallow clone by copying the internal value (for `JsonValue`), array (for `JsonArray`) or dictionary (for `JsonObject`):
```cs
    JsonNode node = ... // a non-dynamic JsonNode
    byte[] utf8 = node.SerializeToUtf8Bytes();
    dynamic obj = JsonNode.Parse<JsonNode>(utf8, options);
```

# Interop
## Serialization
These serializer features are supported via `JsonSerializerOptions`:
- `Converters.Add()` (custom converters added at run-time)
- `NumberHandling`
- `DefaultIgnoreCondition` (default\null handling )
- `PropertyNamingPolicy`.  Note that `DictionaryKeyPolicy` is not used since an object and a dictionary both have the same JSON representation.
- `PropertyNameCaseInsensitive`
- `AllowTrailingCommas`
- `ReadCommentHandling`. Note that round-tripping comments are not possible. See the "Features not proposed in first round" section.
- `MaxDepth`

A deserialized `JsonNode` that is not modified will be re-serialized using the existing `JsonElement` semantics where essentially the raw UTF8 is written back. This is important because the above serializer features will not be in effect for these values. A node needs to be created to replace the original `JsonElement` in order for the above serializer features to interop. This is a performance optimization that avoids having to expand the entire tree for cases when the `JsonSerializerOptions` supports the above options.

Normally POCOs will round-trip the same JSON anyway, so in most cases not invoking custom converters, checking for null handling, etc should be fine.

### `JsonNode.To<T>`
This method has 3 stages:

**First stage: return internal value directly from JsonValue<T>**
If the `To<T>` generic type matches the `JsonValue<T>` type the raw value is returned:
```cs
var jValue = new JsonValue<int>(42);
int i = jValue.To<int>(); // returns the internal <T> value
```

Note that `JsonValue<T>` has a `Value` property that can be used instead:
```cs
var jValue = new JsonValue<int>(42);
int i = jValue.Value; // returns the internal <T> value
```

**Second stage: JsonElement support**
For `JsonValue<JsonElement>` special logic exists to obtain the known primitives:
```cs
JsonObject jObject = JsonObject.Parse(...);
JsonNode jNode = jObject["MyStringProperty"];
Debug.Assert(jNode is JsonValue<JsonElement>);
string s = jNode.To<string>(); // calls JsonElement.GetString()
```

This is necessary because the serializer doesn't support deserializing values from a `JsonElement` instance.

**Third stage: serializer fallback**
Use the serializer to obtain the value. This stage is expensive compared to the other two.

```cs
JsonNode jNode = JsonNode.Parse("""42"""); // a quoted number.
Debug.Assert(jNode is JsonValue<JsonElement>);
int i = jNode.To<int>(); // The value from ToJsonString() is passed to the deserializer to obtain an int (if quoted numbers are enabled).
Debug.Assert(i == 42);
```
which can also work for POCOs and collection types:
```cs
JsonNode jNode = JsonNode.Parse("[0,1,2]");
Debug.Assert(jNode is JsonValue<JsonElement>);
int[] iArray = jNode.To<int[]>(); // The value from ToJsonString() is passed to the deserializer to obtain an int[]
Debug.Assert(i == 42);
```

### Internal custom converter
Note that a custom converter can be specified to override the built-in custom converters for `JsonNode` etc, although this should be a rare case. Currently there is no per-node option to specify a custom converter, but that could be added later.

## `JsonElement`
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

## Utf8JsonReader, Utf8JsonWriter

# Performance
## Internal value is a `JsonElement`
`JsonElement` is used as the internal deserialized value. Since it supports lazy creation of values, it is performant for scenarios that don't fully access each property or element. For example, the contents of a `JsonValue<string>` which internally is a UTF8 `byte[]`, not a `string`, is not "cracked" open until the value is requested.

Note that `JsonElement` is [now ~2x faster](https://github.com/dotnet/runtime/pull/42538) in 6.0 for cases where a stand-alone `JsonElement` is needed as is the case here.

## Lazy creation of `JsonNode` tree
The design supports lazy and shallow creation which is performant when only a subset of the tree is accessed.

A `JsonNode` tree is populated when `JsonObject` and `JsonArray` instances are navigated. For example, a `JsonObject` contains only its internal `JsonElement` value after deserialization. When a property is accessed for the first time, a `JsonNode` instance is created for every property and added to the `JsonObject`'s internal dictionary. Each of those child nodes maintains a single reference to its `JsonElement` which may be another `JsonObject` or `JsonArray`, meaning lazy creation is also "shallow".

## Enumerating `JsonElement` directly
Accessing properties and elements in a read-only manner does not require `JsonNode` instances to be created. This is important to improve performance primarily for very large collections. This can be achieved by obtaining the `JsonElement` via `JsonNode.AsJsonElement()` and enumerating through existing APIs on `JsonElement`. No `JsonNode` instances need to be created in this case.

## Boxing and mutating of `Value<T>`
The design of values uses generics to hold internal `<T>` value. This avoids boxing for value types.

A given `Value<T>`, such as `Value<int>` can be modified by the `Value` property which can be used to avoid creation of a new `JsonValue` instance.

## Property lookup
The property-lookup algorithm can be made more efficient than the standard dictionary by using ordering heuristics (like the current serializer) or a bucketless B-Tree with no need to call `Equals()`. The Azure `dynamic` prototyping effort has been thinking of the B-Tree approach.

# Features not proposed

These feature are reasonable, but not proposed (at least for first round):
- Reading\writing JSON comments. Not implemented since `JsonDocument` doesn't support it although the reader and writer support them. 
  - Cost to implement is not known (no known prototype or PR).
  - For the public API, a `JsonValueKind.Comment` enum value would be added and a setter added to `JsonNode.ValueKind` (the getter is already there).
  - A comment would be represented by a `JsonValue<string>` with the `ValueKind` property value == `JsonValueKind.Comment`
- JsonPath support. Newtonsoft has this as a way to parse a subset of JSON into a `JToken`.
- Annotations. Newtonsoft has this to provide the LineNumber and Position and any user-specified values.
- LineNumber and Position. These are not preserved in `JsonNode` although any exceptions will have that information. Note that a `Path` property, however, is supported on `JsonNode`.

These features will likely never be implemented:
- Support for `System.ComponentModel.TypeConverter` in the `To<T>()` method or another new method. Currently the serializer does not support this, so it doesn't make sense to add it here. Note that Newtonsoft does support this.

# Dependencies
Other issues to consider along with this:
- [Api proposal: Change JsonSerializerOptions default settings](https://github.com/dotnet/runtime/issues/31094)
  - Useful to prevent the `JsonSerializerOption` parameter from having to be specified.
- [We should be able serialize and deserialize from DOM](https://github.com/dotnet/runtime/issues/31274)
  - For consistency with `JsonNode`.
- [More extensible object and collection converters](https://github.com/dotnet/runtime/issues/36785)
  - The DOM could be used in a future feature to make object and collection custom converters easier to use.

# Todos
- [X] Review general direction of API. Primarily using the serializer methods vs. new `node.Parse()` \ `node.Write()` methods and having a sealed `JsonValue` class instead of separate number, string and boolean classes.
- [X] Update API based on feedback and additional prototyping.
- [X] Provide more samples (LINQ).
- [X] Create API issue and review.
- [ ] Prototype and usabilty study?
- [ ] Ensure any future "new dynamic" C# support (being considered for intellisense support based on schema) is forward-compatible with the work here which uses the "old dynamic".
