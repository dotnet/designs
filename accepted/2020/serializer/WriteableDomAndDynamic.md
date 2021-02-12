# Overview
February 12th, 2021.

This document covers the API and design for a writable DOM along with support for the [C# `dynamic` keyword](https://docs.microsoft.com/en-us/dotnet/framework/reflection-and-codedom/dynamic-language-runtime-overview).

It is expected that a significant percent of existing System.Text.JSON consumers will use these new APIs, and also attract new consumers including:
- Those unable to use the serializer for varying reasons:
  - Heavyweight; requires compilation of POCO types.
  - Limitations in the serializer such as polymorphism.
  - Schema is not fixed and must be inspected.
- A desire for a lightweight, simple API especially for one-off cases.
- A desire to use `dynamic` capabilities for varying reasons including sharing of loosely-typed, script-based code.
- To efficiently read or modify a subset of a large graph. For example, it is possible to easily and efficiently navigate to a subsection of a large graph and read an array or deserialize a POCO from that subsection. LINQ can also be used.

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
The existing `JsonDocument` and `JsonElement` types represent the DOM support today which is read-only. It maintains a single immutable UTF-8 buffer and returns `JsonElement` value types from that buffer on demand. That design minimizes the initial `JsonDocument.Parse()` time and associated heap allocs but also makes it slow to re-obtain the same value (which is common with LINQ) and does not lend itself to being directly extended to support writability. Although the internal UTF-8 buffer will continue to be immutable with the design proposed here, `JsonElement` will indirectly support writability by interop with `JsonNode`.

Currently there is no direct support for `dynamic` in System.Text.Json. Adding support for that implies adding a writeable DOM. So considering both `dynamic` and non-`dynamic` scenarios for a writeable DOM in a single design allows for a common API, shared code and intuitive interop between the two.

This design is based on learning and scenarios from these reference implementations:
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
A deserialized `JsonNode` value internally contains a `JsonElement` which contains the JSON kind (object, array, number, string, true, false) and the raw UTF-8 value.

When the consumer obtains the value through a method such as `jvalue.GetValue<double>()` the CLR `Double` value is deserialied from the raw UTF-8. Thus the mapping between UTF-8 JSON and the CLR object model is deferred. Deferring is consistent with existing `JsonElement` semantics where, for example, a JSON number is not automatically mapped to any CLR type until the consumer calls a method such as `JsonElement.GetDecimal()` or `JsonElement.GetDouble()`.

```cs
JsonObject jObject = JsonObject.Parse("{""MyProperty"":42}");
JsonValue jValue = jObject["MyProperty"];
Debug.Assert(jValue is JsonValue<JsonElement>);
int i = jValue.GetValue<int>();
```

A `JsonValue<JsonElement>` is returned from deserialization. To change the value to a CLR type a new node instance must be created:
```cs
jObject["MyProperty"] = new JsonValue<int>(43);
int i = jValue.GetValue<int>();
```

Note that in both cases `GetValue<int>` is called: this is important because it means there is a common programming model for cases when a given `JsonValue<T>` is backed by `JsonElement` or the actual `int` value, for example. This means for a given property, the consumer does not need to be concerned about this -- the consumer always knows the "MyProperty" number property will be backed by a `int`.

A `JsonValue<T>` can also have its internal value changed without having to create a new instance:
```cs
var jValue = JsonValue<int>(43);
jValue.Value = 44;
```

A `JsonValue` supports custom converters that were added via `JsonSerializerOptions.Converters.Add()`. A common use case would be to add the `JsonStringEnumConverter` which serializes `Enum` values as a string instead of an integer, but also user-defined custom converters:
```cs
JsonObject jObject = JObject.Parse(...);
JsonValue jValue = jObject["Amount"];
Money money = jValue.GetValue<Money>();
```

`GetValue<Money>` calls the custom converter for `Money`.

Although `JsonValue<T>` is intended to support simple value types, any CLR value including POCOs and various collection types can be specified, assuming they are supported by the serializer. However, normally one would use `JsonArray` or `JsonObject` for a POCO or collection. If a POCO or collection is specified in a `JsonValue<T>`, it is serialized as expected but when deserialized the type will be either a `JsonObject` or `JsonArray`.

During serialization, some `JsonSerializerOptions` are supported including quoted numbers and null handling. See the "Interop" section.

## Why `JsonValue` and not `JsonNumber` + `JsonString` + `JsonBoolean`?
The JSON primitives (number, string, true, false) do **not** have their own `JsonNode`-derived type like `JsonNumber`, `JsonString` and `JsonBoolean`. Instead, a common `JsonValue` class represents them. This allows a JSON number, for example, to serialize and deserialize as either a CLR `String` or a `Double` depending on usage and options. Without this support, for example, consider this **hypothetical API** that has a `JsonNumber` class (which again is not the proposal):
```cs
// Hypothetical programming model (not proposed).

// Obtain node with a JSON number token type.
JsonNumber number = JsonNode.Parse<JsonNumber>("1.2");
double dlb = number.GetValue<double>();

// Change to "NaN".
number.Value = double.Nan;
string json = number.ToJsonString();
Debug.Assert(json == "\"NaN\""); // Due to quoted number support, this could be made to work.

number = JsonNode.Parse<JsonNumber>(json); // Throws
```

This throws since a `JsonString` instance, not a `JsonNumber` would be created by default. Even if the above could be made to work by the knowledge of `<T>` being `JsonNumber`, and not a `JsonString`, it wouldn't work when deserializing a property on a `JsonObject` node. For example, a `JsonObject` would create a `JsonString` for a "NaN" JSON string since it has no way of knowing these should map to a `JsonNumber` -- unlike the serializer, nodes have no metadata that says it should map to a property of type `System.Double`.

The proposed programming is shown below. There a JSON "NaN" string is deserialized into a `JsonValue` and when `GetValue<double>` is called, a `double` is returned, assuming the appropriate `JsonSerializerOptions` are configured for quoted numbers. When the node is serialized, it produces a JSON string (again, if configured). The consumer of the DOM is not necessarily aware that `double` values assigned to the node may be serialized to JSON as either a string or a JSON number.
```cs
// Proposed programming model.

// Write numbers as strings.
JsonSerializerOptions options = new JsonSerializerOptions();
options.NumberHandling = JsonNumberHandling.WriteAsString;

// Obtain node with a JSON number token type.
JsonNode number = JsonNode.Parse("1.2", options);
Debug.Assert(number is JsonValue);
double dlb = number.GetValue<double>();

// Change to "NaN".
number = double.Nan; // There is an implicit conversion from double to JsonValue<double>.
string json = number.ToJsonString();
Debug.Assert(json == "\"NaN\"");

// Round-tripping works fine even with a quoted number.
number = JsonNode.Parse(json);
```

## Proposed API
All types live in STJ.dll. The namespace is "System.Text.Json.Node" since the node types overlap with both "Document" and "Serializer" semantics, so having its own namespace makes sense. 

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
        // "TypeToGet" used to not collide with "T" on JsonValue<T>.
        // The optional 'converter' parameter is only used in edge cases for specifying a one-off
        // converter for a particular property that is different than the default converter on JsonSerializerOptions.
        public virtual JsonValue? GetValue<TypeToGet>(JsonConverter? converter = null);
        public virtual bool TryGetValue<TypeToGet>(out TypeToGet? value);
        public virtual bool TryGetValue<TypeToGet>(JsonConverter converter, out TypeToGet? value);

        // Return the parent and root nodes; useful for LINQ.
        // These are set internally once added to a JsonObject or a JsonElement.
        public JsonNode? Parent { get; }
        public JsonNode? Root { get; }

        // The JSON Path; same "JsonPath" syntax we use for JsonException information.
        public string Path { get; }

        // Serialize\deserialize wrappers. These are helpers and thus are an optional API feature.
        // Parse() terminology consistent with Utf8JsonReader\JsonDocument.
        // WriteTo() terminology consistent with Utf8JsonWriter.
        public abstract TypeToDeserialize? Deserialize<TypeToDeserialize>(JsonConverter? converter = null);
        public abstract bool TryDeserialize<TypeToDeserialize>(out TypeToDeserialize? value);
        public abstract bool TryDeserialize<TypeToDeserialize>(JsonConverter converter, out TypeToDeserialize? value);
        public string ToJsonString(); // serialize as a string
        public byte[] ToUtf8Bytes(); // serialize as UTF-8
        public abstract void WriteTo(System.Text.Json.Utf8JsonWriter writer);
        public static JsonNode? Parse(string? json, JsonSerializerOptions options = null);
        public static JsonNode? ParseUtf8Bytes(ReadOnlySpan<byte> utf8Json, JsonSerializerOptions options = null);
        public static JsonNode? ReadFrom(ref Utf8JsonReader reader, JsonSerializerOptions options = null);

        // The token type from deserialization. Not used internally but may be useful for consumers.
        public JsonValueKind ValueKind { get; }

        // JsonElement interop
        public static JsonNode GetNode(JsonElement jsonElement);
        public static bool TryGetNode(JsonElement jsonElement, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out JsonNode? jsonNode);

        // Dynamic support
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

        // IList<JsonNode?> implemention (some hidden with explicit implementation):
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

        public override void WriteTo(System.Text.Json.Utf8JsonWriter writer);
        public static JsonArray? Parse(string? json, JsonSerializerOptions options = null);
        public static JsonArray? ParseUtf8Bytes(ReadOnlySpan<byte> utf8Json, JsonSerializerOptions options = null);
        public static JsonArray? ReadFrom(ref Utf8JsonReader reader, JsonSerializerOptions options = null);
    }

    public sealed class JsonObject : JsonNode, IDictionary<string, JsonNode?>
    {
        public JsonObject(JsonSerializerOptions options = null);
        public JsonObject(JsonElement jsonElement, JsonSerializerOptions? options = null);

        public override JsonNode Clone();

        public bool TryGetPropertyValue(string propertyName, outJsonNode? jsonNode);

        // IDictionary<string, JsonNode?> implemention (some hidden with explicit implementation):
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

        public override void WriteTo(System.Text.Json.Utf8JsonWriter writer);
        public static JsonObject? Parse(string? json, JsonSerializerOptions options = null);
        public static JsonObject? ParseUtf8Bytes(ReadOnlySpan<byte> utf8Json, JsonSerializerOptions options = null);
        public static JsonObject? ReadFrom(ref Utf8JsonReader reader, JsonSerializerOptions options = null);
    }

    public abstract class JsonValue : JsonNode
    {
        public JsonValue(JsonSerializerOptions options = null);
    }

    public sealed class JsonValue<T> : JsonValue
    {
        public JsonValue(T value, JsonSerializerOptions options = null);
        public JsonValue(T value, JsonValueKind valueKind, JsonSerializerOptions options = null);

        public override JsonNode Clone();

        public override TypeToReturn GetValue<TypeToReturn>();
        public override bool TryGetValue<TypeToReturn>(out TypeToReturn value);

        // The internal raw value.
        public override T Value {get; set;}

        public override void WriteTo(System.Text.Json.Utf8JsonWriter writer);
        public static JsonValue<T> Parse(string? json, JsonSerializerOptions options = null);
        public static JsonValue<T> ParseUtf8Bytes(ReadOnlySpan<byte> utf8Json, JsonSerializerOptions options = null);
        public static JsonValue<T> ReadFrom(ref Utf8JsonReader reader, JsonSerializerOptions options = null);
    }
}
```
Currently `JsonElement` is created in two cases by the serializer:
- When a CLR property\field is of type `System.Object`.
- When a property exists in JSON but does not map to any CLR property. Currently this is stored in a dictionary-backed property with the `[JsonExtensionData]` attribute.

```cs
namespace System.Text.Json
{
    // Determines what type to create for missing properties and properties declared as System.Object.
    public enum JsonUnknownTypeHandling
    {
        JsonElement = 0, // Default
        JsonNode = 1, // Create JsonNode*-derived classes
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

The extension property itself can either be declared in several ways:
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

## Serializer interop
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

The `JsonSerializerOptions` is passed associated with a root `JsonNode` and used to specify supported options including run-time added custom converters, and other options such as quoted numbers and `PropertyNameCaseInsensitive` to determine whether the lookup of property names on `JsonObject` are case-sensitive (for both direct use of `JsonObject` and also indirect use through `dynamic`).

## Deserialize interop
For deserializing JSON to a `JsonNode`, static "Parse" on `JsonNode` and all derived node types are provided:
```cs
JsonSerializerOptions options = ...
string json = ...
JsonNode jNode;

// short version:
jNode = JsonNode.Parse(json, options);

// equivalent longer version:
jNode = JsonSerializer.Deserialize<JsonNode>(json, options);
```

To deserialize a node into a CLR object such as a collection, POCO other value type:
```cs
JsonNode jNode = ...

// short version:
MyPoco? obj = jNode.Deserialize<MyPoco>();

// equivalent longer version:
MyPoco? obj = JsonSerializer.Deserialize<MyPoco>(jNode.ToJsonString());
```

## Cast operators for JsonValue<T>
Although explicit or implicit cast operators are not required since the `JsonValue<T>` provides constructors and `GetValue<TypeToReturn>()`, based on usability feedback requesting a terse syntax, the use of both explicit cast operators (from a `JsonValue<T>` to known primitives) and implicit cast operators (from known primitives to a `JsonValue<T>`) are supported.

Note that cast operators do not work in all languages such as F#.

Explicit:
```cs
JsonArray jArray = ...
jArray[0] = new JsonValue<string>("hello"); 
jArray[1] = new JsonElement<MyCustomDataType>(myCustomDataType);

string s = jArray[0].GetValue<string>();
MyCustomDataType m = jArray[1].GetValue<MyCustomDataType>();```

With cast operators:
```cs
JsonArray jArray = ...
jArray[0] = "hello"; // Uses implicit operator.
jArray[1] = new JsonElement<MyCustomDataType>(myCustomDataType); // No implicit operator for custom types (unless it adds its own)

string s = (string)jArray[0]; // Uses explicit operator.
MyCustomDataType m = jArray[1].GetValue<MyCustomDataType>(); // no explicit operator for custom types  (unless it adds its own).
```

Dynamic:
```cs
JsonArray jArray = ...
jArray[0] = "hello"; // Uses implicit operator.
jArray[1] = myCustomDataType; // Possible through dynamic.

string s = jArray[0]; // Possible through dynamic.
MyCustomDataType m = jArray[1]; // Possible through dynamic.
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
    string json = jArray.ToJsonString();
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

## Missing vs. null
The indexer for `JsonObject` returns `null` for missing properties. This aligns with:
- Expected support for `dynamic`.
- Newtonsoft.
- By design, the serializer today when deserializing JSON to `System.Object` will return `null` not a `JsonElement` with `JsonValueKind.Null`.

However, for some scenarios, it is important to distinguish between a `null` value deserialized from JSON vs. a missing property. Since `JsonObject` implements `IDictionary<string, JsonNode>` it can be inspected:
```cs
bool found = jObject.TryGetValue("NonExistingProperty", out object _);
```

An alternative, not proposed, is to add a `JsonNull` node type along with a `JsonSerializerOptions.DeserializeNullAsJsonNull` option to configure.

## Interop with JsonElement
The constructors of `JsonNode` allow any type to be specified including a `JsonElement`.

To obtain a `JsonElement` from a node, the `GetNode()` and `TryGetNode()` methods can be used.

## New DOM, terse syntax with C# "object initializers"
```cs
var jObject = new JsonObject()
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

string json = jObject.ToJsonString();
```
## New DOM + dynamic
```cs
dynamic jObject = new JsonObject();
jObject.MyString = "Hello!";
jObject.MyNull = null;
jObject.MyBoolean = false;
jObject.MyInt = 43;
jObject.MyDateTime = new DateTime(2020, 7, 8);
jObject.MyGuid = new Guid("ed957609-cdfe-412f-88c1-02daca1b4f51");
jObject.MyArray = new int[] { 2, 3, 42 };
jObject.MyObject = new JsonObject();
jObject.MyObject.MyString = "Hello!!"
jObject.MyObject.Child = new JsonObject();
jObject.MyObject.Child.ChildProp = 1;

string json = jObject.ToJsonString();
```

## Get\Set JsonValue<T>
Getting a value:
```cs
JsonValue jValue = JsonValue.Parse("42");

// Since backed by JsonElement, these will both work:
int i = jValue.GetValue<int>();
double d = jValue.GetValue<double>();

// Any type with a converter can be used.
MyEnum enum = jValue.GetValue<MyEnum>();

// Explicit operator:
double d2 = (double)jValue;
```

Creating a value:
```cs
// Explicit syntax:
JsonValue<int> jValue = new JsonValue<int>(42);

// Implicit operator:
JsonValue jValue = 42; // Creates JsonValue<int>(42)
jValue.Value = 3.14; // Can also mutate
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
    JsonArray allOrders = (JsonArray)JsonNode.Parse(Linq_Query_Json);
    IEnumerable<JsonNode> orders = allOrders.Where(o => o["Customer"]["City"].GetValue<string>() == "Fargo");

    Debug.Assert(2 == orders.Count());
    Debug.Assert(100 == orders.ElementAt(0)["OrderId"].GetValue<int>());
    Debug.Assert(300 == orders.ElementAt(1)["OrderId"].GetValue<int>());
    Debug.Assert("Customer1" == orders.ElementAt(0)["Customer"]["Name"].GetValue<string>());
    Debug.Assert("Customer3" == orders.ElementAt(1)["Customer"]["Name"].GetValue<string>());
}

{
    var options = new JsonSerializerOptions();
    options.EnableDynamicTypes();

    IEnumerable<dynamic> allOrders = (IEnumerable<dynamic>)JsonNode.Parse(Linq_Query_Json, options);
    IEnumerable<dynamic> orders = allOrders.Where(o => ((string)o.Customer.City) == "Fargo");

    Debug.Assert(2 == orders.Count());
    Debug.Assert(100 == (int)orders.ElementAt(0).OrderId);
    Debug.Assert(300 == (int)orders.ElementAt(1).OrderId);
    Debug.Assert("Customer1" == (string)orders.ElementAt(0).Customer.Name);
    Debug.Assert("Customer3" == (string)orders.ElementAt(1).Customer.Name);
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
- `ExpandoObject` does not support case insensitivity and throws an exception when accessing missing properties (a `null` is desired instead). This could be fixed, however.
- `DynamicObject` prevents potential optimizations including property-lookup.

Thus, the design presented here for `dynamic` assumes explicit interface implementation of `IDynamicMetaObjectProvider`.

Note that Newtonsoft has a single object model with [JToken](https://www.newtonsoft.com/json/help/html/T_Newtonsoft_Json_Linq_JToken.htm) which implements `IDynamicMetaObjectProvider`.

## Prototype
A prototype for 6.0 that is available at https://github.com/steveharter/runtime/tree/WriteableDomAndDynamic.

## Referencing the `System.Linq.Expressions` assembly
Having `System.Text.Json.dll` directly reference `System.Linq.Expressions.dll` is feasible because the ILLinker removes the reference to the large `System.Linq.Expressions.dll` when the dynamic functionality is not used. A stand-alone console app not using `dynamic` was ~10.5MB and using dynamic was ~11.5MB.

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
    dynamic obj = JsonNode.Parse<JsonNode>("{\"MyProp\":42}", options);
    dynamic obj = JsonNode.Parse<JsonObject>("{\"MyProp\":42}", options);
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

A deserialized `JsonNode` that is not modified will be re-serialized using the existing `JsonElement` semantics where essentially the raw UTF-8 is written back. This is important because the above serializer features will not be in effect for these values. A node needs to be created to replace the original `JsonElement` in order for the above serializer features to interop. This is a performance optimization that avoids having to expand the entire tree for cases when the `JsonSerializerOptions` supports the above options.

Normally POCOs will round-trip the same JSON anyway, so in most cases not invoking custom converters, checking for null handling, etc should be fine.

### `JsonNode.GetValue<TypeToReturn>`
This method has 3 stages.

**First stage: return internal value directly from JsonValue<T>**
If the `GetValue<TypeToReturn>` generic type matches the `JsonValue<T>` type the raw value is returned:
```cs
var jValue = new JsonValue<int>(42);
int i = jValue.GetValue<int>(); // returns the internal <T> value
```

Note that `JsonValue<T>` has a `Value` property that can be used instead:
```cs
var jValue = new JsonValue<int>(42);
int i = jValue.Value; // returns the internal <T> value
```

The implementation assumes a direct Type match, not `IsAssignableFrom()` semantics.

Also note that there is no stage for returning the current node instance if the `<T>` is assignable from the current node type (e.g. `System.Object`, `IList<JNode>`, etc). Specifying such a `<T>` value results in a deep clone.

**Second stage: JsonElement support**
For `JsonValue<JsonElement>` special logic exists to obtain the known primitives:
```cs
JsonObject jObject = JsonObject.Parse(...);
JsonNode jNode = jObject["MyStringProperty"];
Debug.Assert(jNode is JsonValue<JsonElement>);
string s = jNode.GetValue<string>(); // calls JsonElement.GetString()
```

This is necessary because the serializer doesn't support deserializing values from a `JsonElement` instance.

**Third stage: serializer fallback**
Use the serializer to obtain the value. This stage is expensive compared to the other two but is necessary to support custom converters.

```cs
JsonNode jNode = JsonNode.Parse("\"42\""); // a quoted number.
Debug.Assert(jNode is JsonValue<JsonElement>);

// This works through the serializer (if quoted numbers enabled):
int i = jNode.GetValue<int>();
Debug.Assert(i == 42);
```

### Internal custom converter
Note that a custom converter can be specified to override the built-in custom converters for `JsonValue.GetValue<>(converter)` although this should be a rare case.

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
`JsonElement` is used as the internal deserialized value. Since it supports lazy creation of values, it is performant for scenarios that don't fully access each property or element. For example, the contents of a `JsonValue<string>` which internally is a UTF-8 `byte[]`, not a `string`, is not "cracked" open until the value is requested.

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
These feature are reasonable, but not proposed (at least for the first round):
- Reading\writing JSON comments. Not implemented since `JsonDocument` doesn't support it although the reader and writer support them. 
  - Cost to implement is not known (no known prototype or PR).
  - For the public API, a `JsonValueKind.Comment` enum value would be added and a setter added to `JsonNode.ValueKind` (the getter is already there).
  - A comment would be represented by a `JsonValue<string>` with the `ValueKind` property value == `JsonValueKind.Comment`
- `IEqualityComparer<T>` implementation such as a `JsonNodeComparer` class. This would imply a deep compare which is expensive, although that would be expected.
- "JsonPath" support. Newtonsoft has this as a way to parse a subset of JSON into a `JToken`.
- Annotations. Newtonsoft has this to provide LineNumber and Position and user-specified values.
- LineNumber and Position. These are not preserved in `JsonNode` although any inner reader or JsonExceptions will have that information. Note that a `Path` property, however, is supported on `JsonNode`, including determining the path at runtime even after modifications to a property name, element ordering, etc.
- Non-generic overloads on `JsonNode`. They will not be fast since the internal representation is a generic <T> value:
  - `public virtual object GetValue(Type type, int index);`
  - `public virtual object GetValue(Type type, string propertyName);`
  - `public abstract object GetValue(Type type);`
  - `public abstract bool TryGetValue(Type type, out object? value);`

These features will likely never be implemented:
- `JsonNode.ReadFromAsync()` and `WriteToAsync()`. Adding every current and future variant doesn't seem like the best action. Note the corresponding serializer methods (including the async ones) can be used instead of the helpers on `JsonNode`.
- Support for `System.ComponentModel.TypeConverter` in the `GetValue<TypeToReturn>()` method or another new method. Currently the serializer does not support this, so it doesn't make sense to add it here. Note that Newtonsoft does support this.
- Support for reference handling because:
  - Performance. When reference handling is enabled, all nodes must be materialized eagerly.
  - Likely not expected since a DOM is lower-level than an object graph. Consumers would expect the metadata to be available to them programmatically, not hidden.
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

# Todos
- [X] Review general direction of API. Primarily using the serializer methods vs. new `node.Parse()` \ `node.Write()` methods and having a sealed `JsonValue` class instead of separate number, string and boolean classes.
- [X] Update API based on feedback and additional prototyping.
- [X] Provide more samples (LINQ).
- [X] Create API issue and review.
- [ ] Prototype and usabilty study?
- [ ] Ensure any future "new dynamic" C# support (being considered for intellisense support based on schema) is forward-compatible with the work here which uses the "old dynamic".
