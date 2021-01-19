# Overview and goals

January 19th, 2021.

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
- It is not possible to have a single class hierachy that are not tied to `dynamic` and thus `System.Linq.Expressions`:
  - If combined, there would need to be an assembly reference to the very large `System.Linq.Expressions.dll` event when `dynamic` features are not needed (the ILLinker will not be able to remove the reference to `System.Linq.Expressions`).
  - If separate standalone classes, the `dynamic` class would likely forward to a separate writable DOM class, which doubles the object instances and increases the concept count around interop between the types returned from `dynamic` vs. the writable DOM classes.
- `ExpandoObject` does not support case insensitivity and throws an exception when accessing missing properties (a `null` is desired instead). This could be fixed, however.
- `DynamicObject` prevents potential optimizations including property-lookup.

Thus, the design presented here for `dynamic` assumes:
- Implementation of `IDynamicMetaObjectProvider`.
- Use of the writable DOM without requiring a reference to `System.Linq.Expressions.dll` (when `dynamic` support is not used).
- Efficient implementation of tying both `IDynamicMetaObjectProvider` and the writable DOM classes into a single instance and type hierarchy. Note that Newtonsoft has a single object model with [JToken](https://www.newtonsoft.com/json/help/html/T_Newtonsoft_Json_Linq_JToken.htm) which implements `IDynamicMetaObjectProvider`.

## Prototype
A prototype for 6.0 that is available at https://github.com/steveharter/runtime/tree/WriteableDomAndDynamic.

## Depending on `System.Linq.Expressions.dll`
Having `System.Text.Json.dll` directly reference `System.Linq.Expressions.dll` is only feasible if the ILLinker can remove the reference to the large `System.Linq.Expressions.dll` when the dynamic functionality is not used.
- Implementations of Blazor and stand-alone apps that do not use `dynamic` features do not want to carry the large `System.Linq.Expressions.dll`.
  - Referencing `System.Linq.Expressions.dll` will require ~5.5MB of disk size. `System.Linq.Expressions.dll` is around 5MB in size but since it also references to `System.Linq` (~400K) and `System.ObjectModel` (~80K) (which that are not referenced by STJ) it becomes ~5.5MB (crossgen'd size).
  - STJ is considering removing its reference to using reflection emit, so referencing `System.Linq.Expressions.dll` would require keeping the the various System.Reflection.* references. Since reflection emit is currently implemented in the runtime (`System.Private.CoreLib`), disk savings by avoiding the API surface layer in `SREmit.ILGeneration` and `SREmit.Lightweight` is limited to ~30K.
- Existing usages of STJ such as by the SDK and Visual Studio that do not use `dynamic` also do not want to carry `System.Linq.Expressions.dll`.

The preferred approach is to ensure ILLinker can remove the dependency to `System.Linq.Expressions.dll` when `dynamic` is not needed. This is achieved by having an opt-in method `EnableDynamicTypes()` on `JsonSerializerOptions` that roots all internal usages of `System.Linq.Expressions.dll`.

## Proposed API to enable dynamic mode
All classes to be located in STJ.dll.

```cs
namespace System.Text.Json
{
    public partial class JsonSerializerOptions
    {
        // The root method that if not called, ILLinker will trim out the reference to `System.Linq.Expressions.dll`.
        // STJ does not expose any types from SLE publically.
        // Any System.Object-qualified type specified in a called to JsonSerializer.Deserialize() will create either a DynamicJsonArray, DynamicJsonObject or DynamicJsonValue.
        public void EnableDynamicTypes();
    }
}
```

Sample code to enable dynamic types:
```cs
    var options = new JsonSerializerOptions();
    options.EnableDynamicTypes();

    dynamic obj = JsonSerializer.Deserialize<JsonNode>("{\"MyProp\":42}", options);
    int i = obj.MyProp;
    Debug.Assert(i == 42);

    string json = obj.Serialize();
```

### Varying the `T` in `Deserialize<T>`
This
```cs
    dynamic obj = JsonSerializer.Deserialize<object>("{\"MyProp\":42}", options);
```
is equivalent to
```cs
    dynamic obj = JsonSerializer.Deserialize<dynamic>("{\"MyProp\":42}", options);
```
and these (assuming `options.EnableDynamicTypes()` was called)
```cs
    dynamic obj = JsonSerializer.Deserialize<DynamicJsonNode>("{\"MyProp\":42}", options);
    dynamic obj = JsonSerializer.Deserialize<DynamicJsonObject>("{\"MyProp\":42}", options);
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
    dynamic obj = JsonSerializer.Deserialize<JsonNode>(utf8, options);
```

# Writable DOM
A deserialized DOM is based upon JSON token types (object, array, number, string, true, false) since the corresponding CLR types are not known, unlike deserializing against a strongly-typed POCO. This is consistent with `JsonElement` semantics as well.

Although a deserializated DOM in "read mode" fully maps to the JSON token types, once in "edit mode", CLR values to be assigned to nodes which do not necessarily map to JSON token types. Two common examples:
- Quoted numbers. A CLR number (`Double`, `Int32`, etc) is normally serialized to JSON as a number. However, a number may be serialized as a JSON string instead - which is necessary to support CLR numbers that can't be represented in JSON as a number, such as `Double.NaN` or `Double.PositiveInfinity`.
- String Enums. A CLR `Enum` is normally serialized to JSON as a number, but through a custom converter (including `JsonStringEnumConverter`), an `Enum` can be serialized as the string representation.

For example, consider this **hypothetical API** that has a `JsonNumber` class (and not the proposal of using just `JsonNode`)
```cs
// Ask for a number with a JSON number token type.
// Internally probably stored as a byte[] or JsonElement.
JsonNumber number = JsonSerializer.Deserialize<JsonNumber>("1.2");
double dlb = number.GetValue<double>();

// Change to "NaN".
number.Value = double.Nan;
string json = number.Serialize();
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

Thus the DOM is based on CLR types (not JSON tokens) in edit mode and during serialization. The JSON "value" types (number, string, boolean) are represented as a common `JsonValue` sealed class, and **not** separate `JsonNumber`, `JsonString` and `JsonBoolean` classes as shown in the example above.

During serialization, the DOM is written using the serializer and uses any custom converters that may be configured. In all but idiocentric scenarios (e.g. a custom converter can do anything), the behavior is natural and JSON is round-tripped as expected.

In the example, a JSON "NaN" string is deserialized into a `JsonValue` and when `GetValue<double>` is called, a `double` is returned, assuming the appropriate `JsonSerializerOptions` are configured for quoted numbers. When the node is serialized, it produces a JSON string (again, if configured). The consumer of the DOM is not necessarily aware that `double` values assigned to the node may be serialized to JSON as either a string or a JSON number, and after deserialization is not aware what the internal state is - it could be a `byte[]`, a `JsonElement` or a `string` depending on implementation and\or the JSON token (string or number).

## Primary API
All types live in STJ.dll.
```cs
namespace System.Text.Json
// Also consider existing S.T.J.Serialization namespace instead of S.T.J.
// since it is now coupled to the serializer rather than stand-alone.
{
    public abstract class JsonNode
    {
        internal JsonNode(); // prevent external derived classes.
        public JsonNode(JsonSerializerOptions options = null);

        public JsonSerializerOptions Options { get; }

        // Support terse syntax for JsonArray (no cast necessary to JsonArray).
        public System.Text.Json.Serialization.JsonNode? this[int index] { get; set; }

        // Support terse syntax for JsonObject (no cast necessary to JsonObject).
        public virtual System.Text.Json.Serialization.JsonNode? this[string key] { get; set; }

        // Support terse syntax for JsonValue (no cast necessary to JsonValue to change a value).
        // This represents the internal value, and no conversion is performed.
        public virtual object Value {get; set;}

        // Return the internal value or convert to T as necessary.
        public abstract T GetValue<T>();
        public abstract object GetValue<Type type>();
        public abstract bool TryGetValue<T>(out T? value);
        public abstract bool TryGetValue(Type type, out object? value);

        // Convert a non-dynamic node to a dynamic.
        public object ToDynamic();
        // This would also work assume the linker will remove the reference to SLE if not called:
        // public dynamic ToDynamic();

        // The token type from deserialization; otherwise JsonValueKind.Unspecified.
        public JsonValueKind ValueKind { get; }

        // Serialize() wrappers which pass the value and options.
        // These are helpers and thus can be considered an optional API feature.
        // There are no wrappers for JsonSerializer.Deserialize* methods since the
        // existing Deserialize* methods can be called.
        public string Serialize();
        public void Serialize(System.Text.Json.Utf8JsonWriter writer);
        public System.Threading.Tasks.Task SerializeAsync(Stream utf8Json, CancellationToken cancellationToken = default);
        public byte[] SerializeToUtf8Bytes();
    }

    public sealed class JsonArray : JsonNode, IList<object>
    {
        public JsonArray(JsonSerializerOptions options = null);

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
        public JsonValue(object value, JsonSerializerOptions options = null);

        // The internal raw value.
        // During deserialization, this will be a JsonElement (pending design) since that supports
        // delayed creation of string and on-demand creation of numbers.
        // In edit mode, can be set to any object.
        public override object Value {get; set;}

        // Can be used to obtain any compatible value, including values from custom converters.
        public override T GetValue<T>();
        public override object GetValue<Type type>();
        public override bool TryGetValue<T>(out T? value);
        public override bool TryGetValue(Type type, out object? value);
    }

    // These types are used with dynamic support.
    // JsonSerializerOptions.EnableDynamicTypes must be called before using.
    // These types are public for "edit mode" scenarios when using dynamic.
    public class JsonDynamicObject : JsonObject, IDynamicMetaObjectProvider { }
    public class JsonDynamicArray : JsonArray, IDynamicMetaObjectProvider { }
    public class JsonDynamicValue : JsonValue, IDynamicMetaObjectProvider { }
}
```

## Serializer interop
For deserializating JSON to a `JsonNode` instance, the existing static `JsonSerializer.Deserialize()` methods are used.

For serializing a `JsonNode` instance to JSON, instance helpers on `JsonNode` are provided that pass in the value and options:
```cs
JsonObject obj = ...

// short version:
string json = obj.Serialize();

// equivalent longer versions:
string json = JsonSerializer.Serialize(obj, obj.GetType(), obj.Options);
string json = JsonSerializer.Serialize<JsonObject>(obj, obj.Options);
string json = JsonSerializer.Serialize<JsonNode>(obj, obj.Options);
```

Serializer-specific functionality including custom converters and quoted numbers just work.

The `JsonSerializerOptions` is passed to `JsonNode` and used to specify any run-time added custom converters, handle other options such as quoted numbers, and to respect the `PropertyNameCaseInsensitive` to determine whether the string-based lookup of property names on `JsonObject` are case-sensitive (for both direct use of `JsonObject` and also indirect use through `dynamic`). If we have `static JsonValue Parse()` methods, those would also need an override to specify the `JsonSerializerOptions`.

Although the serializer can be used to directly (de)serialize `JsonNode` types, once a `JsonNode` instance is created, it is expected the `JsonNode.GetValue<T>()` method is used to change types instead of using the serializer:
```cs
    JsonNode node = ... // any value
    string str;

    // This works, but has no caching:
    byte[] utf8 = node.SerializeToUtf8Bytes();
    str = JsonSerializer.Deserialize<string>(utf8, options);

    // For ease-of-use and performance, use GetValue().
    // It may return the raw internal value or the last converted value instead of deserializing.
    str = node.GetValue<string>();
```

### Cast operators
No explicit or implicit cast operators exist since `GetValue<T>` allows for a single, consistent programming model. This also works on not just the "known" types in the CLR but also custom data types known by the serializer.

Cast operators do not work in all languages such as F# and even for C# would not work for all types:
```cs
JsonArray jArray = ...
jArray[0] = "hello"; // Possible though a string implicit operator.
jArray[1] = myCustomDataType; // We can't know all types, so this won't work.

string v0 = jArray[0];
MyCustomDataType v1 = jArray[1]; // We can't know all types, so this won't work.
```

Proposed (non-dynamic):
```cs
JsonArray jArray = ...
jArray[0] = new JsonValue("hello");
jArray[1] = new JsonValue(myCustomDataType);

// Get the internal values based on above.
string v0 = (string)jArray[0].Value;
MyCustomDataType v1 = (MyCustomDataType)jArray[1].Value;

// If the type is not known, call GetValue() which supports a possible conversion:
MyCustomDataType v1 = jArray[1].GetValue<MyCustomDataType>();
```

Proposed (dynamic):
```cs
DynamicJsonArray jArray = ...
jArray[0] = "hello";
jArray[1] = myCustomDataType;

string v0 = jArray[0];
MyCustomDataType v1 = jArray[1];
```

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

It is required that all option instances are the same across a given `JsonNode` tree.

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
    string json = jArray.Serialize();
```

## Changing the deserialization of System.Object
The serializer has always deserialized JSON that maps to a `System.Object` as a `JsonElement`. This will remain the default behavior going forward, however there will be a way to specify `JsonNode` instead:

```cs
namespace System.Text.Json
{
    public partial class JsonSerializerOptions
    {
        public bool UseJsonNodeForUnknownTypes { get; set;}
    }
}
```

This setting has no effect if `JsonSerializerOptions.EnableDynamicTypes()` is called since it overrides the handling of unknown types. When polymorphic deserialization is added as a separate feature, the meaning may change slightly to handle the case of a type being deserialized with JSON "known type metadata" specifying an unknown or unhandled type on the CLR side. i.e. for polymorphic deserialization, a `System.Object` property during deserialization may be set to a concrete POCO type and not `JsonNode` or `JsonElement`.

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
The indexer for `JsonObject` and `DynamicJsonObject` return `null` for missing properties. This aligns with:
- Expected support for `dynamic`.
- Newtonsoft.
- The serializer today when deserializing JSON mapping to `System.Object`; i.e. a `JsonElement` with `JsonValueKind.Null` is not created.

However, for some scenarios, it is important to distinguish between a `null` value deserialized from JSON vs. a missing property. Since `JsonObject` implements `IDictionary<string, JsonNode>` it can be inspected:
```cs
JsonObject jObject = ...
bool found = jObject.TryGetValue("NonExistingProperty", out object value);
```

An alternative, not currently recommended, is to add a `JsonNull` node type along with a `JsonSerializerOptions.DeserializeNullAsJsonNull` option to configure.

## Interop with JsonElement
The constructors of `JsonNode` allow any type to be specified including a `JsonElement`.

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

    string json = jObject.Serialize();
}
```
## Same result as above, with using dynamic instead of explicit `JsonNode`:
```cs
    var options = new JsonSerializerOptions();
    options.EnableDynamicTypes();

    dynamic jObject = new DynamicJsonObject(options);
    jObject.MyString = "Hello!"; // converted to: new JsonValue("Hello!")
    jObject.MyNull = null;
    jObject.MyBoolean = false;
    jObject.MyInt = 43;
    jObject.MyDateTime = new DateTime(2020, 7, 8);
    jObject.MyGuid = new Guid("ed957609-cdfe-412f-88c1-02daca1b4f51");
    jObject.MyArray = new int[] { 2, 3, 42 };
    jObject.MyObject = new JsonDynamicObject();
    jObject.MyObject.MyString = "Hello!!"
    jObject.MyObject.Child = new DynamicJsonObject();
    jObject.MyObject.Child.ChildProp = 1;

    string json = jObject.Serialize();
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
    int i1 = (int)jValue.Value; // cast is not always safe since returning internal value
    int i2 = jValue.GetValue<int>(); // safe since a conversion occurs if necessary

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

    string s1 = (string)jValue.Value; // not always safe
    string s2 = jValue.GetValue<string>(); // safe
    Enum e = jValue.GetValue<MyEnum>();
```

## Boolean
```cs
    JsonValue jValue = JsonSerializer.Deserialize<JsonValue>("true");

    bool b1 = (bool)jValue.Value; // not always safe
    bool b2 = jValue.GetValue<bool>(); // safe
    MyBoolCompatibleType o = jValue.GetValue<MyBoolCompatibleType>();
```

## LINQ
`JObject` and `JElement` implement `IEnumerable` so no extra work for LINQ there. Currently, there is no "Parent" property support so that should be considered.

Dynamic types support LINQ expressions that can call methods; we need to spec whether we want to support that or not.

## Interop with custom converter + dynamic
```cs
    internal class PersonConverter : JsonConverter<Person>
    {
        public override Person Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            dynamic jObject = JsonSerializer.Deserialize<object>(ref reader, options);

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
            writer.Serialize(writer, value, options);
        }
    }
```

# Performance notes
- `JsonElement` for 6.0 is [now ~2x faster](https://github.com/dotnet/runtime/pull/42538) which may make it suitable to use internally. Since it supports delayed creation of values, it is performant for scenarios that don't fully access each property or element, or even the contents of a string-based value.
  - If `JsonElement` is not used internally, the design should add a delayed creation mechanism to `JsonValue` for `string` (`JsonTokenType.String`). This would have a `UTF-8` value until the string is requested to lazily create the `string`. The same applies to the underlying dictionary in `JsonObject` and the underlying list in `JsonArray`.
- `JsonValue` should cache the last return value, so subsequent calls to `GetValue<T>` return the previous value if `T` is the same type as the previous value's type.
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
- [x] Review general direction of API. Primarily using the serializer methods vs. new `node.Parse()` \ `node.Write()` methods and having a sealed `JsonValue` class instead of separate number, string and boolean classes.
- [ ] Provide more samples (LINQ).
- [ ] Prototype and usabilty study?
- [ ] Create API issue and review.
- [ ] Ensure any future "new dynamic" C# support (being considered for intellisense support based on schema) is forward-compatible with the work here which uses the "old dynamic".
