# Overview and goals

October 30th, 2020.

This document provides options and recommendations for 6.0 to support both a writable DOM and the C# `dynamic` keyword.

Although the writable DOM and `dynamic` features are somewhat separate, considering both during design should result in a single API for both scenarios that is optimized to have minimal state and has intuitive interop between the DOM classes and those returned when using `dynamic`.

The existing `JsonDocument` class represents the DOM support today, however it is read-only. It maintains a single immutable UTF-8 buffer and returns `JsonElement` value types from that buffer on demand, which minimizes the initial `JsonDocument.Parse()` time and heap allocs but also makes it slow to re-obtain the same value (which is common with LINQ) and does not lend itself to being extended to support writability.

This document proposes a new API based on learning and scenarios from:
- The writable DOM prototype.
  - During 5.0, there was a [writable DOM effort](https://github.com/dotnet/runtime/issues/30436). Unlike `JsonDocument`, the design centered around writable aspects, usability and LINQ support, and not performance and allocations. It was shelved due to time constraints and outstanding work.
- The dynamic support code example.
  - During 5.0, [dynamic support](https://github.com/dotnet/runtime/issues/29690) was not able to be implemented due to schedule constraints. Instead, a [code example](https://github.com/dotnet/runtime/pull/42097) was provided that enables `dynamic` and a writeable DOM that can be used by any 3.0+ runtime in order to unblock the community and gather feedback for a 6.0 feature.
- Azure prototyping.
  - The Azure team needs a writable DOM, and supporting `dynamic` is important for some scenarios but not all. A [prototype](https://docs.microsoft.com/en-us/dotnet/api/azure.core.dynamicjson?view=azure-dotnet-preview) has been created. Work is also being coordinated with the C# team to support Intellisense with `dynamic`, although that is not expected to be implemented in time to be used in 6.0.

# Dynamic
The dynamic feature in C# is enabled by both language features and implementation in the `System.Linq.Expressions` assembly. The core interface detected by the language to enable this support is [IDynamicMetaObjectProvider](https://docs.microsoft.com/en-us/dotnet/api/system.dynamic.idynamicmetaobjectprovider?view=net-5.0).

There are implementations of `IDynamicMetaObjectProvider` including [DynamicObject](https://docs.microsoft.com/en-us/dotnet/api/system.dynamic.dynamicobject?view=net-5.0) and [ExpandObject](https://docs.microsoft.com/en-us/dotnet/api/system.dynamic.expandoobject?view=netcore-3.1). However, these implementations are not ideal:
- There are many public members used for wiring up dynamic support, but are of little to no value for consumers so they would be confusing when mixed in with other methods intended to be used for writable DOM support.
- It is not possible to efficiently combine writable DOM classes that are not tied to `dynamic` and `System.Linq.Expressions`:
  - If combined, there would need to be an assembly reference to the very large `System.Linq.Expressions.dll` event when `dynamic` features are not needed.
  - If separate, the `dynamic` classes delegate to separate writable DOM classes, which doubles the object instances and increases the concept count around interop between the types returned from `dynamic` vs. the writable DOM classes.
- `ExpandoObject` does not support case insensitivity and throws an exception when accessing missing properties (a `null` is desired instead). This could be fixed, however.
- `DynamicObject` prevents potential optimizations including property-lookup.

Thus, the design presented here for `dynamic` assumes:
- Implementation of `IDynamicMetaObjectProvider`.
- Use of the writable DOM without requiring a reference to `System.Linq.Expressions.dll` (when `dynamic` support is not desired).
- Efficient implementation of tying both `IDynamicMetaObjectProvider` and the writable DOM classes into a single instance. Newtonsoft also has a single object model with [JToken](https://www.newtonsoft.com/json/help/html/T_Newtonsoft_Json_Linq_JToken.htm) which implements `IDynamicMetaObjectProvider`.

## Adding a System.Text.Json.Dynamic.dll (STJD) assembly
Having `System.Text.Json.dll` directly reference `System.Linq.Expressions.dll` is not desired:
- Implementations of Blazor and stand-alone apps that do not use `dynamic` features do not want to carry the large `System.Linq.Expressions.dll`.
  - Referencing `System.Linq.Expressions.dll` will require ~5.5MB of disk size. `System.Linq.Expressions.dll` is around 5MB in size but since it also references to `System.Linq` (~400K) and `System.ObjectModel` (~80K) (which that are not referenced by STJ) it becomes ~5.5MB (crossgen'd size).
  - STJ is considering removing its reference to using reflection emit, so referencing `System.Linq.Expressions.dll` would require keeping the the various System.Reflection.* references. Since reflection emit is currently implemented in the runtime (`System.Private.CoreLib`), disk savings by avoiding the API surface layer in `SREmit.ILGeneration` and `SREmit.Lightweight` is limited to ~30K.
- If STJ is needs to be used downstack by other framework assemblies, there may be basic questions around the assembly dependency tree and potential cycles.

Thus the proposal is to add a `System.Text.Json.Dynamic.dll` assembly that will have a reference to both `System.Text.Json.dll` and `System.Linq.Expressions.dll` and contain the implementation supporting `dynamic`.

An alternative which may or may not be feasible in the 6.0 timeframe is to extend the linker to remove dependencies on applications such as Blazor or a stand-alone app if the `dynamic` feature is not used. Currently the linker is only ran against framework assemblies, not application assemblies. This will help the stand-alone scenario and perhaps Blazor, but not the downstack concerns.

_For naming, also consider `S.T.J.DynamicSerialization.dll`._

A more open-ended name of `S.T.J.Extensions.dll` was considered but is not discoverable, so a more feature-specific name was considered a better choice. By going with feature-specific names like `S.T.J.Dynamic.dll`, we may end up with additional assemblies in the future. If the need arises, these extension assemblies could forward to private common implementation assembly(s).

## Proposed API to enable dynamic mode
In the new STJD assembly:
```cs
namespace System.Text.Json
{
    public static class JsonExtensions
    {
        public static void EnableDynamic(JsonSerializerOptions this options);
    }
}
```

Thus a consumer would reference STJD.dll and then author code such as:
```cs
    var options = new JsonSerializerOptions();
    options.EnableDynamicTypes();

    dynamic obj = JsonSerializer.Deserialize<dynamic>("{\"MyProp\":42}", options);
    int i = obj.MyProp;
    Debug.Assert(i == 42);

    string json = JsonSerializer.Serialize(obj);
```

### Internal implementation
The types that are used at runtime to implement `dynamic` can be made `internal`. Instances are created by the System.Object custom converter which understands `dynamic`.
```cs
internal class JsonDynamicObject : JsonObject, IDynamicMetaObjectProvider {}
internal class JsonDynamicArray : JsonArray, IDynamicMetaObjectProvider {}
internal class JsonDynamicValue : JsonValue, IDynamicMetaObjectProvider {}
```

where `JsonObject`, `JsonArray`, and `JsonValue` are new public types in STJ.dll.

### Varying the `T` in `Deserialize<T>`
This
```cs
    dynamic obj = JsonSerializer.Deserialize<dynamic>("{\"MyProp\":42}", options);
```
is equivalent to
```cs
    dynamic obj = JsonSerializer.Deserialize<object>("{\"MyProp\":42}", options);
```
and these (assuming `options.EnableDynamicTypes()` was called)
```cs
    dynamic obj = JsonSerializer.Deserialize<JsonNode>("{\"MyProp\":42}", options);
    dynamic obj = JsonSerializer.Deserialize<JsonObject>("{\"MyProp\":42}", options);
```

### Supporting non-dynamic to dynamic
If a previously created non-dynamic `JsonNode` object needs dynamic support, an extension could be provided:
```cs
namespace System.Text.Json
{
    public static class JsonExtensions
    {
        public static dynamic ToDynamic(JsonNode this value);
    }
}
```

Called like this:
```cs
    JsonNode node = ... // a non-dynamic JsonNode
    dynamic obj = node.ToDynamic();
```

The implementation of `ToDynamic()` is somewhat slow since it does a deep clone. It would be similar to the code below except a bit faster since some overhead of the serialize\deserialize methods may be avoided:
```cs
    JsonNode node = ... // a non-dynamic JsonNode
    byte[] utf8 = JsonSerializer.SerializeToUtf8Bytes(node, options);
    dynamic obj = JsonSerializer.Deserialize<JsonNode>(utf8, options);
```

# Writable DOM
_This section is preliminary and not yet ready for an API review._

A deserialized DOM is based upon JSON token types (object, array, number, string, true, false) since the corresponding CLR types are not known, unlike deserializing against a strongly-typed POCO. This is consistent with `JsonElement` semantics as well.

Although a deserializated DOM in "read mode" fully maps to the JSON token types, once in "edit mode", CLR values to be assigned to nodes which do not necessarily map to JSON token types. Two common examples:
- Quoted numbers. A CLR number (`Double`, `Int32`, etc) is normally serialized to JSON as a number. However, a number may be serialized as a JSON string instead - which is necessary to support CLR numbers that can't be represented in JSON as a number, such as `Double.NaN` or `Double.PositiveInfinity`.
- String Enums. A CLR `Enum` is normally serialized to JSON as a number, but through a custom converter (including `JsonStringEnumConverter`), an `Enum` can be serialized as the string representation.

For example, consider this hypothetical API that has a `JsonNumber` class:
```cs
// Ask for a number with a JSON number token type.
// Internally probably stored as a byte[] or JsonElement.
JsonNumber number = JsonSerializer.Deserialize<JsonNumber>("1.2");
double dlb = number.GetValue<double>();

// Change to "NaN".
number.Value = double.Nan;
string json = JsonSerializer.Serialize(number);
Debug.Assert(json == "\"NaN\""); // Due to quoted number support, this works.

// However what about round-tripping?
// Can a JSON string be deserialized into a JsonNumber? If so, "JsonNumber" no longer means a "JSON number".
// When deserializing a string there is no known CLR type, so the internal value would need be based on a string\byte[].
// So a "JsonString" node would normally be created, not "JsonNumber" and thus throw here:
number = JsonSerializer.Deserialize<JsonNumber>(json); // Throws

// Even if the above could be made to work, it wouldn't work with a JsonObject node.
// A JsonObject would create "JsonString" nodes for "NaN" JSON strings since it has
// no way of knowing these should map to a JsonNumber (or a CLR number).
```

Thus the DOM is based on CLR types (not JSON tokens) in edit mode and during serialization. The JSON "value" types (number, string, boolean) are represented as a common `JsonValue` sealed class, and not separate `JsonNumber`, `JsonString` and `JsonBoolean` classes as shown in the example above.

During serialization, the DOM is written using the serializer and uses any custom converters that may be configured. In all but idiocentric scenarios (e.g. a custom converter can do anything), the behavior is natural and JSON is round-tripped as expected.

In the example, a JSON "NaN" string is deserialized into a `JsonValue` and when `GetValue<double>` is called, a `double` is returned, assuming the appropriate `JsonSerializerOptions` are configured for quoted numbers. When the node is serialized, it produces a JSON string (again, if configured). The consumer of the DOM is not necessarily aware that `double` values assigned to the node may be serialized to JSON as either a string or a JSON number, and after deserialization is not aware what the internal state is - it could be a `byte[]`, a `JsonElement` or a `string` depending on implementation and\or the JSON token (string or number).

## Primary API
These types live in STJ.dll.
```cs
namespace System.Text.Json
// Also consider existing S.T.J.Serialization namespace instead of S.T.J.
// since it is now coupled to the serializer rather than stand-alone.
{
    public abstract class JsonNode
    {
        public JsonNode(JsonSerializerOptions options = null);

        public JsonSerializerOptions Options { get; }

        // Return the internal value or convert to T as necessary.
        public abstract T GetValue<T>();
        public abstract object GetValue<Type type>();
        public abstract bool TryGetValue<T>(out T? value);
        public abstract bool TryGetValue(Type type, out object? value);

        // The token type from deserialization; otherwise JsonValueKind.Unspecified.
        public JsonValueKind ValueKind { get; }
    }

    public sealed class JsonArray : JsonNode, IList<object>
    {
        public JsonArray(JsonSerializerOptions options = null);
        public JsonArray(JsonElement value, JsonSerializerOptions options = null);

        // Can be used to return:
        // - Any compatible collection type
        // - JsonElement
        // - string or byte[] (as JSON)
        public override T GetValue<T>();
        public override object GetValue<Type type>();
        public override bool TryGetValue<T>(out T? value);
        public override bool TryGetValue(Type type, out object? value);
    }

    public sealed class JsonObject : JsonNode, IDictionary<string, object>
    {
        public JsonObject(JsonSerializerOptions options = null);
        public JsonObject(JsonElement value, JsonSerializerOptions options = null);

        // Can be used to return:
        // - Any compatible dictionary type
        // - POCO including anonymous type or record
        // - JsonElement
        // - string or byte[] (as JSON)
        public override T GetValue<T>();
        public override object GetValue<Type type>();
        public override bool TryGetValue<T>(out T? value);
        public override bool TryGetValue(Type type, out object? value);
    }

    public sealed class JsonValue : JsonNode
    {
        public JsonValue(JsonSerializerOptions options = null);
        public JsonValue(JsonElement value, JsonSerializerOptions options = null);
        public JsonValue(object value, JsonSerializerOptions options = null);

        // The internal raw value.
        // During deserialization, this will be a JsonElement (pending design) since it supports
        // delayed creation of string and on-demand creation of numbers.
        // In edit mode, can be set to any object.
        public object Value {get; set;}

        // Can be used to obtain any compatible value, including values from custom converters.
        public override T GetValue<T>();
        public override object GetValue<Type type>();
        public override bool TryGetValue<T>(out T? value);
        public override bool TryGetValue(Type type, out object? value);

        // These match the methods on JsonElement.
        // They can be used instead of GetValue<T> for performance since they don't cause generic
        // method expansion, and when the internal value is a JsonElement it avoids multiple `if`
        // statements that need to match typeof(T) to the appropriate JsonElement method.
        public bool GetBoolean();
        public byte GetByte();
        public byte[] GetBytesFromBase64();
        public System.DateTime GetDateTime();
        public System.DateTimeOffset GetDateTimeOffset();
        public decimal GetDecimal();
        public double GetDouble();
        public System.Guid GetGuid();
        public short GetInt16();
        public int GetInt32();
        public long GetInt64();
        public string GetRawText();
        public sbyte GetSByte();
        public float GetSingle();
        public string? GetString();
        public ushort GetUInt16();
        public uint GetUInt32();
        public ulong GetUInt64();
        public bool TryGetByte(out byte value);
        public bool TryGetBytesFromBase64(out byte[]? value);
        public bool TryGetDateTime(out System.DateTime value);
        public bool TryGetDateTimeOffset(out System.DateTimeOffset value);
        public bool TryGetDecimal(out decimal value);
        public bool TryGetDouble(out double value);
        public bool TryGetGuid(out System.Guid value);
        public bool TryGetInt16(out short value);
        public bool TryGetInt32(out int value);
        public bool TryGetInt64(out long value);
        public bool TryGetSByte(out sbyte value);
        public bool TryGetSingle(out float value);
        public bool TryGetUInt16(out ushort value);
        public bool TryGetUInt32(out uint value);
        public bool TryGetUInt64(out ulong value);
    }
}
```

## Serializer interop
Unlike the 5.0 `JsonNode` prototype and the existing `JsonDocument`, the `JsonNode` types are intended to be used by the serializer directly and do not contain `Parse()` or `Write()` methods themselves because:
- The existing serializer methods can be used which support UTF8, string, Utf8JsonReader\Writer and Stream. Any future overloads will just work.
- Serializer-specific functionality including custom converters and quoted numbers just work.

The `JsonSerializerOptions` is passed to `JsonNode` and used to specify any run-time added custom converters, handle other options such as quoted numbers, and to respect the `PropertyNameCaseInsensitive` to determine whether the string-based lookup of property names on `JsonObject` are case-sensitive (for both direct use of `JsonObject` and also indirect use through `dynamic`). If we have `static JsonValue Parse()` methods, those would also need an override to specify the `JsonSerializerOptions`.

Although the serializer can be used to directly (de)serialize `JsonNode` types, once a `JsonNode` instance is created, it is expected the `JsonNode.GetValue<T>()` method is used to change types instead of using the serializer:
```cs
    JsonNode node = ... // any value
    string str;

    // This works, but has no caching:
    byte[] utf8 = JsonSerializer.SerializeToUtf8Bytes(node, options);
    str = JsonSerializer.Deserialize<string>(utf8, options);

    // For ease-of-use and performance, use GetValue().
    // It may return the raw internal value or the last converted value instead of deserializing.
    str = node.GetValue<string>();
```

### Cast operators
No explicit or implicit cast operators exist since `GetValue<T>` allows for a single, consistent programming model. This also works on not just the "known" types in the CLR but also custom data types known by the serializer.

Cast operators do not work in all languages such as F# and even for C# would not work nicely since `System.Object` is allowed for `JsonArray` and `JsonCollection` elements. For example:
```cs
JsonArray jArray = ...
jArray[2] = "hello";
// Is jArray[2] a string or a JsonValue?

jArray[2] = 2;
// jArray[2] is always an int since we can't have implicit cast operators for number types.
```

Although it is possible to add cast operators for `JsonValue` to\from string\bool, number types should not have any implicit operator since they may throw - for example, an internal value of "3.14" cannot be converted into `int` so an exception must be thrown.

### Specifying the `JsonSerializerOptions` instance
The `JsonSerializerOptions` instance needs to be specified in order for any run-time added custom converters to be found and to use `PropertyNameCaseInsensitive` for property lookup with `JsonObject`.

It is cumbersome and error-prone to specify the options instance for every new element or property:
```cs
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

Pending design, it may be required that all option instances are the same across a given `JsonNode` tree.

### Using `System.Object` for values
Both `JsonObject` and `JsonArray` allow `System.Object` to be specified for the property\array values. This allows a terse and more performant model, plus compatibility with Newtonsoft and `dynamic`:
```cs
    var jObject = new JsonObject(options)
    {
        ["MyString"] = "Hello!",
        ["MyBoolean"] = false,
        ["MyArray"] = new int[] {2, 3, 42};
    }
```

The values remain in the original form and are not converted to `JsonNode` instances:
```cs
    var jObject = new JsonObject(options)
    {
        ["MyString"] = "Hello!",
    }

    Debug.Assert(jObject["MyString"].GetType() == typeof(string));
```

During serialization, the appropriate converter is used to serialize either the primitive value or the underlying value held by a `JsonNode`.

An alternative is to have `JsonObject` implement `IDictionary<string, JsonNode>` instead of `IDictionary<string, object>` and `JsonArray` implement `IList<JsonNode>` instead of `IList<object>`. However, if that is done, `dynamic` cases will compile without using `JsonNode` types but will cause an unexpected run-time exception since the underlying array\dictionary does not support non-`JsonNode` types:
```cs
    dynamic obj = JsonSerializer.Deserialize(...);
    obj.Foo = true; // compiles fine, but exception at runtime since 'true' is not a 'JsonNode'
    
    MyPoco poco = JsonSerializer.Deserialize<MyPoco>(json);
    obj.MyPoco = poco; // compiles fine, but exception at runtime since 'MyPoco' is not a 'JsonNode'
```

For the latter case with `MyPoco`, without support for `object`, the poco would need to be converted into a `JsonObject` and there isn't a performant way to do this. One slow workaround:
``` cs
    MyPoco poco = JsonSerializer.Deserialize<MyPoco>(...);

    dynamic obj = JsonSerializer.Deserialize<object>(...);

    // We want to set obj.MyPoco to poco, but can't do that directly:
    string json = JsonSerializer.Serialize(poco);
    JsonObject jObject = JsonSerializer.Deserialize<JsonObject>(json);
    obj.MyPoco = jObject;
    JsonSerializer.Serialize(jObject);
```

Thus having support for `object` in the object and collection cases instead of `JsonNode` helps in several ways:
- Avoid a class of run-time errors where non-`JsonObject` derived types are used.
- Consistent with Newtonsoft.
- More terse and in some cases intuitive programming model. For example, a literal `true` or `false` can be used (without implicit operators) instead of `JsonBoolean(true)` and `JsonBoolean(false)`.
- More performant that being forced to use JsonNode types (removes a somewhat unnecessary abstraction) such as the example above with `MyPoco`.

## Changing the deserialization of System.Object
The serializer has always deserialized JSON mapping to a `System.Object` as a `JsonElement`. This will remain the default behavior going forward, however there will be a way to specify `JsonNode` instead:

```cs
namespace System.Text.Json
{
    public partial class JsonSerializerOptions
    {
        public bool UseJsonNodeForUnknownTypes { get; set;}
    }
}
```

This setting has no effect if the extension method `JsonSerializerOptions.EnableDynamic` is called since it overrides the handling of unknown types. When polymorphic deserialiazation is added as a separate feature, the meaning may change slightly to handle the case of a type being deserialized with JSON "known type metadata" specifying an unknown or unhandled type on the CLR side.

The serializer has always supported placing "overflow" properties in JSON into a dictionary on a property that has the `[JsonExtensionData]` attribute. The property itself can either be declared as:
- `Dictionary<string, object>`
- `IDictionary<string, object>`
- `Dictionary<string, JsonElement>`
- `IDictionary<string, JsonElement>`

Going forward, `JsonNode` can also be specified as the property type, which is useful if `JsonNode` adds value to the missing properties such as being writable or supporting LINQ.

If `UseJsonObjectForUnknownTypes` is `true`, and the property is declared with
- `Dictionary<string, object>`
- `IDictionary<string, object>`

then `JsonNode` is used instead of `JsonElement`.

## Missing vs. null
`JsonObject` semantics return `null` for missing properties. This aligns with:
- Expected support for `dynamic`.
- Newtonsoft.
- The serializer today when deserializing JSON mapping to `System.Object`; i.e. a `JsonElement` with `JsonValueKind.Null` is not created.

However, for some scenarios, it is important to distinguish between a `null` value deserialized from JSON vs. a missing property. Since `JsonObject` implements `IDictionary<string, object>` it can be inspected:
```cs
JsonObject jObject = ...
bool found = jObject.TryGetValue("NonExistingProperty", out object value);
```

An alternative, not currently recommended, is to add a `JsonNull` node type along with a `JsonSerializerOptions.DeserializeNullAsJsonNull` option to configure.

## Interop with JsonElement
The constructors of `JsonNode` have a `JsonElement` overload.

`JsonNode.GetValue<JsonElement>()` can be used to obtain the current `JsonElement`, or create one if the internal value is not a `JsonElement`.

# Examples
## New DOM, explicit `JsonNode` types
```cs
    var options = new JsonSerializerOptions();
    options.EnableDynamicTypes();

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

    string json = JsonSerializer.Serialize(jObject, options);
}
```
## New DOM, using raw CLR types:
```cs
    var options = new JsonSerializerOptions();
    options.EnableDynamicTypes();

    var jObject = new JsonObject(options)
    {
        ["MyString"] = "Hello!",
        ["MyNull"] = null,
        ["MyBoolean"] = false,
        ["MyInt"] = 43,
        ["MyDateTime"] = new DateTime(2020, 7, 8),
        ["MyGuid"] = new Guid("ed957609-cdfe-412f-88c1-02daca1b4f51"),
        ["MyArray"] = new int[] {2, 3, 42}, // direct IEnumerable assignment supported via serializer.
        ["MyObject"] = new JsonObject()
        {
            ["MyString"] = "Hello!!"
        },
        ["Child"] = new JsonObject()
        {
            ["ChildProp"] = 1
        }
    };

    string json = JsonSerializer.Serialize(jObject, options);
```

## Number
GetValue:
```cs
    JsonValue jValue = JsonSerializer.Deserialize<JsonValue>("42");

    int i = jValue.GetValue<int>();
    double d = jValue.GetValue<double>();

    // Any type with a converter can be used.
    MyEnum enum = jValue.GetValue<MyEnum>();

    // Casts are NOT supported (doesn't compile):
    double d2 = (double)jValue;
    double d3 = jValue;
```

Setting values:
```cs
    JsonValue jValue = new JsonValue(42);
    int i1 = (int)jValue.Value; // cast is not always safe
    int i2 = jValue.GetValue<int>(); // safe

    jValue.Value = 3.14;

    // A quoted number can also be specified.
    jValue.Value = "42";
    int i3 = jValue.GetValue<int>();
    Debug.Assert(i3 = 42);

    // Or an enum
    jValue = (MyEnum)42;
    MyEnum myEnum = jValue.GetValue<MyEnum>();
```

## String
```cs
    JsonValue jValue = JsonSerializer.Deserialize<JsonValue>("Hello");

    string s1 = (string)jValue.Value; // cast is not always safe
    string s2 = jValue.GetValue<string>(); // safe
    Enum e = jValue.GetValue<MyEnum>();
```

## Boolean
```cs
    JsonValue jValue = JsonSerializer.Deserialize<JsonValue>("true");

    bool b1 = (bool)jValue.Value; // cast is not always safe
    bool b2 = jValue.GetValue<bool>(); // safe
    MyBoolCompatibleType o = jValue.GetValue<MyBoolCompatibleType>();
```

## LINQ
todo; `JObject` and `JElement` implement `IEnumerable`...

## Interop with custom converter + dynamic
```cs
    internal class PersonConverter : JsonConverter<Person>
    {
        public override Person Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            dynamic jObject = JsonSerializer.Deserialize(ref reader, options);

            // Pass a value into the constructor, which was hard to do before in a custom converter
            // since all of the values need to be manually read and cached ahead of time.
            Person person = new Person(jObject.Id);

            // Any missing properties will be assigned 'null'.
            person.Name = jObject.name; // JSON property names exactly match JSON or be case insensitive depending on options.
            person.AddressLine1 = jObject.addr1; // support 'JsonPropertyName["addr1"]'
            person.AddressLine2 = jObject.addr2; // support 'JsonPropertyName["addr2"]'
            person.City = jObject.city;
            person.State = jObject.state;
            person.Zip = jObject.zip;

            return person;
        }

        public override void Write(Utf8JsonWriter writer, Person value, JsonSerializerOptions options)
        {
            // Default serialization is fine in this example.
            writer.Serialize(writer, value, options);
        }
    }
```

# Performance notes
- `JsonElement` for 6.0 is [now ~2x faster](https://github.com/dotnet/runtime/pull/42538) which may make it suitable to use internally. It supports delayed creation of values which is important for cases that don't fully access each property or element.
  - If `JsonElement` is not used internally, add a delayed creation to `JsonValue` for `string` (`JsonTokenType.String`), meaning the `UTF-8` is preserved until the string is requested. The same applies to the underlying dictionary in `JsonObject` and the underlying list in `JsonArray`.
- `JsonValue` caches the last return value, so subsequent calls to `GetValue<T>` return the previous value if `T` is the same type as the previous value's type.
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
- [ ] Review "Adding a System.Text.Json.Dynamic.dll (STJD) assembly".
- [ ] Review general direction of API. Primarily using the serializer methods vs. new `node.Parse()` \ `node.Write()` methods and having a sealed `JsonValue` class instead of separate number, string and boolean classes.
- [ ] Provide more samples (LINQ).
- [ ] Prototype and usabilty study?
- [ ] Create API issue and review.
- [ ] Determine whether we should support `System.ComponenentModel.TypeConverter`.
- [ ] Ensure any future "new dynamic" C# support for intellisense support is forward-compatible with the work here which uses the "old dynamic".
