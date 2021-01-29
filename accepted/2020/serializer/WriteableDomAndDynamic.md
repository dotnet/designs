# Overview and goals

January 29th, 2021.

This document provides options and recommendations for 6.0 to support both a writable DOM and the C# `dynamic` keyword.

Although the writable DOM and `dynamic` features are somewhat separate, considering both during design should result in a single API for both scenarios that is optimized to have minimal state and has intuitive interop between the DOM classes and those returned when using `dynamic`.

The existing `JsonDocument` and `JsonElement` types represent the DOM support today which is read-only. It maintains a single immutable UTF-8 buffer and returns `JsonElement` value types from that buffer on demand, which minimizes the initial `JsonDocument.Parse()` time and heap allocs but also makes it slow to re-obtain the same value (which is common with LINQ) and does not lend itself to being extended to support writability.

This document proposes a new API based on learning and scenarios from:
- The writable DOM prototype.
  - During 5.0, there was a [writable DOM effort](https://github.com/dotnet/runtime/issues/30436). Unlike `JsonDocument`, the design centered around writable aspects, usability and LINQ support, and not performance and allocations. It was shelved due to time constraints and outstanding work.
- The dynamic support code example.
  - During 5.0, [dynamic support](https://github.com/dotnet/runtime/issues/29690) was not able to be implemented due to schedule constraints. Instead, a [code example](https://github.com/dotnet/runtime/pull/42097) was provided that enables `dynamic` and a writeable DOM that can be used by any 3.0+ runtime in order to unblock the community and gather feedback for a 6.0 feature.
- Azure prototyping.
  - The Azure team needs a writable DOM, and supporting `dynamic` is important for some scenarios but not all. A [prototype](https://docs.microsoft.com/en-us/dotnet/api/azure.core.dynamicjson?view=azure-dotnet-preview) has been created. Work is also being coordinated with the C# team to support Intellisense with `dynamic`, although that is not expected to be implemented in time to be used in 6.0.

## High-level API
The DOM is represented by an abstract base class `JsonNode` along with derived classes for objects, arrays and values:
```cs
namespace System.Text.Json.Serialization
{
    // Abstract base class for all types:
    public abstract class JsonNode {...};

    // Concrete classes for non-dynamic and base class for dynamic:
    public class JsonObject : JsonNode, IDictionary<string, JsonNode> {...}
    public class JsonArray : JsonNode, IList<JsonNode> {...};
    public class JsonValue : JsonNode {...};

    // Concrete classes for C# 'dynamic':
    public sealed class JsonDynamicObject : JsonObject, IJsonDynamicMetaObjectProvider {...}
    public sealed class JsonDynamicArray : JsonArray, IJsonDynamicMetaObjectProvider {...};
    public sealed class JsonDynamicValue : JsonValue, IJsonDynamicMetaObjectProvider {...};
}
```

# Writable DOM
A deserialized `JsonNode` value internally contains the JSON token types (object, array, number, string, true, false) and the raw UTF8 value. When the consumer asks for the value through a method such as `node.To<Double>()` the CLR `Double` value is deserialied from the token type and raw UTF8. Thus the mapping between UTF8 JSON and the CLR object model is deferred. Deferring is consistent with `JsonElement` semantics where, for example, a JSON number is not automatically mapped to any CLR type until the consumer calls a method such as `JsonElement.GetDecimal()` or `JsonElement.GetDouble()`.

When serializing a `JsonNode`, the internal value is deserialied as its internal UTF8 value unless it has been modified.

A `JsonNode` can be set to any CLR value including primitives such as `String` and `Double` but also POCOs and various collection types supported by the serializer. Once a `JsonNode` is set to a CLR value, that value is serialized by using the serializer which includes custom converters and options from `JsonSerializerOptions`. This supports interop including:
- Quoted numbers. A CLR number (`Double`, `Int32`, etc) is normally serialized to JSON as a number. However, by setting `JsonSerializerOptions.NumberHandling = JsonNumberHandling.WriteAsString`. This is useful to support CLR numbers that can't be represented in JSON as a number, such as `Double.NaN` or `Double.PositiveInfinity`.
- String Enums. A CLR `Enum` is normally serialized to JSON as a number, but through a custom converter (including `JsonStringEnumConverter`), an `Enum` can be serialized as the string representation.

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

// However what about round-tripping?
// Can a JSON string be deserialized into a JsonNumber? If so, "JsonNumber" no longer means a "JSON number".
// When deserializing a string there is no known CLR type, so the internal value would need be based on a string\byte[].
// So a "JsonString" node would normally be created, not "JsonNumber" and thus throw here:
number = JsonNode.Parse<JsonNumber>(json); // Throws

// Even if the above could be made to work by the knowledge of '<T>' being 'JsonNumber', it wouldn't work when deserializing
// a property on a JsonObject node. For example, a JsonObject would create a 'JsonString' property nodes for "NaN" JSON strings
// since it has no way of knowing these should map to a 'JsonNumber' (or a CLR number such as 'Double').
```

In the example below, a JSON "NaN" string is deserialized into a `JsonValue` and when `To<double>` is called, a `double` is returned, assuming the appropriate `JsonSerializerOptions` are configured for quoted numbers. When the node is serialized, it produces a JSON string (again, if configured). The consumer of the DOM is not necessarily aware that `double` values assigned to the node may be serialized to JSON as either a string or a JSON number, and after deserialization is not aware what the internal state is - it could be a `byte[]`, a `JsonElement` or a `string` depending on implementation and\or the JSON token (string or number).
```cs
// Proposed programming model.

// Write numbers as strings.
JsonSerializerOptions options = new JsonSerializerOptions();
options.NumberHandling = JsonNumberHandling.WriteAsString;

// Obtain node with a JSON number token type.
JsonNode number = JsonNode.Parse("1.2", options);
Debug.Assert(numer is JsonValue);
double dlb = number.To<double>();

// Change to "NaN".
number.Value = double.Nan;
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
        public JsonNode(JsonSerializerOptions options = null);

        public JsonSerializerOptions Options { get; }

        // JsonArray terse syntax support (no JsonArray cast necessary ).
        public virtual System.Text.Json.Serialization.JsonNode? this[int index] { get; set; }
        public virtual T To<T>(int index); // perf: avoid creating 'JsonNode' for read-only scenarios.
        public virtual object To(Type type, int index);

        // JsonObject terse syntax (no JsonObject cast necessary).
        public virtual System.Text.Json.Serialization.JsonNode? this[string propertyName] { get; set; }
        public virtual T To<T>(string propertyName); // perf: avoid creating 'JsonNode' for read-only scenarios.
        public virtual object To(Type type, string propertyName);

        // This represents the internal value, and no conversion is performed.
        // By default on deserialize this a JsonElement but can be set to any CLR object in edit mode.
        public virtual object Value {get; set;}

        // Return the internal value or convert to the provided type as necessary.
        public abstract T To<T>();
        public abstract object To(Type type));
        public abstract bool TryTo<T>(out T? value);
        public abstract bool TryTo(Type type, out object? value);

        // Return the parent node; useful for LINQ.
        // This is set internally once added to a JsonObject or a JsonElement.
        public JsonNode? Parent { get; }

        // Convert a non-dynamic node to a dynamic.
        // Not used internally; the ILLinker will remove reference to if not used which is important
        // here since 'dynamic' implies reference to System.Linq.Expressions.
        public dynamic ToDynamic();

        // The token type from deserialization; for new instances JsonValueKind.Unspecified.
        public JsonValueKind ValueKind { get; }

        // Serialize\deserialize wrappers. These are helpers and thus are an optional API feature.
        // Parse() terminology consistent with Utf8JsonReader\JsonDocument.
        // WriteTo() terminology consistent with Utf8JsonWriter.
        // Also consider naming variants:
        // - Serialize() \ Deserialize()
        // - FromJson() \ ToJson()
        // - ToString() override in addition or to replace "public string Write()"
        public string Write();
        public void WriteTo(System.Text.Json.Utf8JsonWriter writer);
        public System.Threading.Tasks.Task WriteToAsync(Stream utf8Json, CancellationToken cancellationToken = default);
        public byte[] WriteUtf8Bytes();

        public static JsonNode? Parse(string? json, JsonSerializerOptions options = null);
        public static JsonNode? Parse(System.Text.Json.Utf8JsonWriter writer, JsonSerializerOptions options = null);
        public static System.Threading.Tasks.Task ParseAsync(Stream utf8Json, CancellationToken cancellationToken = default, JsonSerializerOptions options = null);
        public static JsonNode? ParseUtf8Bytes(ReadOnlySpan<byte> utf8Json, JsonSerializerOptions options = null);
    }

    public class JsonArray : JsonNode, IList<JsonNode>
    {
        public JsonArray(JsonSerializerOptions options = null);

        // Can be used to return:
        // - Any compatible collection type
        // - JsonElement
        // - string or byte[] (as JSON)
        public override T To<T>();
        public override object To<Type type>();
        public override bool TryTo<T>(out T? value);
        public override bool TryTo(Type type, out object? value);
    }

    public class JsonObject : JsonNode, IDictionary<string, JsonNode>
    {
        public JsonObject(JsonSerializerOptions options = null);

        // Can be used to return:
        // - Any compatible dictionary type
        // - POCO including anonymous type or record
        // - JsonElement
        // - string or byte[] (as JSON)
        public override T To<T>();
        public override object To<Type type>();
        public override bool TryTo<T>(out T? value);
        public override bool TryTo(Type type, out object? value);
    }

    public class JsonValue : JsonNode
    {
        public JsonValue(object value, JsonSerializerOptions options = null);

        // The internal raw value.
        // During deserialization, this will be a JsonElement since that supports
        // delayed creation of string and on-demand creation of numbers.
        // In edit mode, can be set to any object.
        public override object Value {get; set;}

        // Can be used to obtain any compatible value, including values from custom converters.
        public override T To<T>();
        public override object To<Type type>();
        public override bool TryTo<T>(out T? value);
        public override bool TryTo(Type type, out object? value);
    }

    // These types are used with dynamic support.
    // JsonSerializerOptions.EnableDynamicTypes must be called before using.
    // These types are public for "edit mode" scenarios when using dynamic.
    public sealed class JsonDynamicObject : JsonObject, IDynamicMetaObjectProvider
    {
        public JsonDynamicObject(JsonSerializerOptions options = null);
    }

    public sealed class JsonDynamicArray : JsonArray, IDynamicMetaObjectProvider
    {
        public JsonDynamicArray(JsonSerializerOptions options = null);
    }

    public sealed class JsonDynamicValue : JsonValue, IDynamicMetaObjectProvider
    {
        public JsonDynamicValue(object? value, JsonSerializerOptions options = null);
    }
}
```
## Serializer interop
For serializing a `JsonNode` instance to JSON, instance helpers on `JsonNode` are provided that pass in the value and options:
```cs
JsonObject obj = ...

// short version:
string json = obj.Write();

// equivalent longer versions:
string json = JsonSerializer.Serialize(obj, obj.GetType(), obj.Options);
string json = JsonSerializer.Serialize<JsonObject>(obj, obj.Options);
string json = JsonSerializer.Serialize<JsonNode>(obj, obj.Options);
```

Serializer-specific functionality including custom converters and quoted numbers just work.

The `JsonSerializerOptions` is passed to `JsonNode` and used to specify any run-time added custom converters, handle other options such as quoted numbers, and to respect the `PropertyNameCaseInsensitive` to determine whether the string-based lookup of property names on `JsonObject` are case-sensitive (for both direct use of `JsonObject` and also indirect use through `dynamic`). If we have `static JsonValue Parse()` methods, those would also need an override to specify the `JsonSerializerOptions`.

Although the serializer can be used to directly (de)serialize `JsonNode` types, once a `JsonNode` instance is created, it is expected the `JsonNode.To<T>()` method is used to change types instead of using the serializer:
```cs
    JsonNode node = ... // any value
    string str;

    // This works, but has no caching:
    byte[] utf8 = node.WriteUtf8Bytes();
    str = JsonSerializer.Deserialize<string>(utf8, options);

    // For ease-of-use and performance, use To().
    // It may return the raw internal value or the last converted value instead of deserializing.
    str = node.To<string>();
```

## Deserialize interop
For deserializing JSON to a `JsonNode`, static helpers on `JsonNode` are provided:
```cs
JsonSerializerOptions options = ...
string json = ...
JsonNode obj;

// short version:
obj = JsonNode.Parse(json, options);

// equivalent longer version:
obj = JsonSerializer.Deserialize<JsonNode>(json, options);
```

## Cast operators
No explicit or implicit cast operators exist since `To<T>` allows for a single, consistent programming model. This works on "known" types in the CLR such as `String` and also custom data types known by the serializer.

Cast operators do not work in all languages such as F# and even for C# would not work for all types. Consider this hypothetical programming model:
```cs
// Hypothetical programming model (not proposed):
JsonArray jArray = ...
jArray[0] = "hello"; // Possible though a string implicit operator.
jArray[1] = myCustomDataType; // No cast operator; this wouldn't compile.

string v0 = jArray[0];
MyCustomDataType v1 = jArray[1]; // No cast operator; this wouldn't compile.
```

Proposed (non-dynamic):
```cs
JsonArray jArray = ...
jArray[0] = new JsonValue("hello");
jArray[1] = new JsonValue(myCustomDataType);

// Get the internal values based on above.
string v0 = (string)jArray[0].Value;
MyCustomDataType v1 = (MyCustomDataType)jArray[1].Value;

// If the internal value type is not known, call To() which supports a possible conversion:
MyCustomDataType v1 = jArray[1].To<MyCustomDataType>();
```

Proposed (dynamic):
```cs
JsonDynamicArray jArray = ...
jArray[0] = "hello";
jArray[1] = myCustomDataType;

string v0 = jArray[0];
MyCustomDataType v1 = jArray[1];
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

# Performance notes
- `JsonElement` for 6.0 is [now ~2x faster](https://github.com/dotnet/runtime/pull/42538) and thus is currently assumed to be the internal deserialized value. Since it supports delayed creation of values, it is performant for scenarios that don't fully access each property or element, or even the contents of a string-based value (which internally is a UTF8 `byte[]`, not a `string`).
  - If `JsonElement` is not used internally, the design should add a delayed creation mechanism to `JsonValue` for `string` (`JsonTokenType.String`). This would have a `UTF-8` value until the string is requested to lazily create the `string`. The same applies to the underlying dictionary in `JsonObject` and the underlying list in `JsonArray`.
- `JsonValue` should cache the last return value obtained from `To()`, so subsequent calls to `To()` return the previous value if the type (specified as `<T>` or the `Type` parameter) is the same and the value has not changed.
- Accessing properties on `JsonObject` and elements on `JsonArray` in a read-only manner should not require `JsonNode` instances to be created. This is important to improve performance primarily for very large collections. This is achieved by `JsonValue.To(int index)` and `JsonValue.To(string propertyName)`.
- The property-lookup algorithm can be made more efficient than the standard dictionary by using ordering heuristics (like the current serializer) or a bucketless B-Tree with no need to call `Equals()`. The Azure `dynamic` prototyping effort has been thinking of the B-Tree approach.

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
