# Overview and goals

April 21th, 2020.

This document provides options and recommendations for 5.0 to handle overlapping serializer goals including reach, faster cold startup performance, reducing memory usage, and smaller size-on-disk.

## Reach
Achieved by limiting, removing or adding fallbacks for runtime code generation so that AOT optimized platforms including [Xamarin iOS](https://github.com/dotnet/runtime/issues/31326) can use System.Text.Json.

In particular, iOS does not allow for any type of code generation whether Reflection.Emit, IL generation or IL->native through JITing. This means the IL is either compiled AOT (Ahead-Of-Time) to native code before deploying to the device, or that the IL is interpreted at runtime.

The options to consider, in priority order include:
1) Minimize the serializer's runtime code generation. For example, in 5.0, we are planning on avoiding `Type.MakeGenericType` when instantiating the converter for POCOs -- the converter will close the `JsonConverter<T>` generic at build time with `JsonConverter<System.Object>`.
2) Add "loosely-typed" fallbacks to the serializer for cases that cannot be refactored; these fallacks will box to Object when using value types or use standard invoke-based reflection. This results in slower performance. Some serializers do have a Reflection.Emit fallback (Newtonsoft) and others do not (Jil and Utf8Json). System.Text.Json has a fallback for its usages of Reflection.Emit, but it does not have a fallback for its usage of `Type.MakeGenericType and .MakeGenericMethod` which does cause issues on iOS.
3) Generate code for POCOs that avoids using the unsupported features. This also has higher runtime performance than the loosely-typed fallbacks.
4) Add a high-performance replacement for targeted scenarios including property getters, property setters, constructors and as a stretch `Type.MakeGenericType and .MakeGenericMethod`.
5) If possible, add or extend runtime interpreter support for missing cases. Xamarin (and .NET Native) has an interpreter mode.
6) Throw PlatformNotSupportedException for when there is no alternative.

## Faster startup performance

This is important for scenarios including [micro-services](https://github.com/dotnet/runtime/issues/1568) where processes start and exit frequently.

The serializer has fast startup compared to most other serializers including Newtonsoft's Json.NET, Jil and Utf8Json. Startup perf is mostly affected by generating IL - the Jil serializer is the most aggressive here and has the highest startup cost (but in many cases has the fastest steady-state performance).

There is no stated goal for 5.0 to make the steady-state CPU performance faster than it is now in 5.0 for general scenarios, although we do not want to noticably regress steady-state performance while addressing the stated goals including improving startup performance. It is possible some code-gen options described later may have steady-state performance gains, but again that is not a stated goal.

<details>
  <summary>5.0 master startup benchmarks (click to expand)</summary>

A small POCO was used (`LoginViewModel` benchmark test class). Measurements were taken "cold" from deserializing hard-coded JSON directly and from serializing a populated object directly meaning there was no previous loading of the serialization assembly or inspecting POCO types for custom attributes beforehand.

| Serializer |  Serialize Mean (us)  | Serialize ratio | Deserialize Mean (us) | Deserialize ratio |
| :-- | :-- | :-- | :-- | :--
| **System.Text.Json** | 27,887 | 1.00 | 26,864 | 1.00 
| **Json.NET** | 81,872 | 2.94 | 80,307 | 2.99
| **Utf8Json** | 69,681 | 2.50 | 69,383 | 2.58
| **Jil** | 103,760 | 3.72 | 97,679 | 3.64
</details>

## Reduced memory usage
Reducing private bytes (non-shared per-process) memory is important for Bing scenarios and other scenarios that have many process using the same POCOs or start and stop many processes. Shared memory can be amortized across many processes plus is already there when a new processes starts and thus requires no additional overhead to generate.

This can be done by:
- Avoiding runtime code generation including Reflection.Emit or JITting.
- Minimize cached state held by the serializer if it can call into generated code to obtain or process the values. For example, it is possible to write a JSON property name with hard-coded escaped values instead of allocating a string or `JsonEncodedText` instance to hold the value.

## Reduced size-on-disk
Primarily this is for faster downloads of the runtime and applications.

The runtime and BCL can be made smaller by removing Reflection.Emit support.

Although outside the scope of this document, an application can be made smaller by running ILLinker (e.g. "tree-shaking"). Serializers generated at runtime are generally problematic for linking but can be somewhat mitigated by using metadata to declare what members to preserve. The code-gen approaches in this document, depending on implementation, may help with this since they generate direct calls to members that are detectable by the linker.

# Serializer design background
## Runtime code dependencies
For performance, today the serializer uses Reflection.Emit when calling property getters, property setters and constructors. When field support is added, Reflection.Emit will also be used to get and set fields.

For reach scenarios, the serializer avoids Reflection.Emit in the NetStandard 2.0 library through the use of an #ifdef. It is possible to do this without an #ifdef through use of `RuntimeFeature.IsDynamicCodeSupported` but since that is only supported on NetStandard 2.1 the #ifdef is currently more practical (instead of generating an additional 2.1 library) especially considering the 5.0 plan for unified libraries.

However, even in the NetStandard build, the serializer still depends on runtime code generation through `Type.MakeGenericType and .MakeGenericMethod` thus this "reach" support is broken today in the serializer.

Note that Xamarin stubs `MakeGenericType` and tries to support these on iOS by pre-generating types via AOT but there are still issues with this (including not being able to determine all possible `<T>` variants).

## Custom converters
The existing serializer makes use of custom converters which is a class that knows how to implement a Read(...) and Write(...) method. There are 3 types of converters:
1. Value. The only type of custom converters that can be implemented publically today (by the community).
2. Collection. Used internally for IEnumerable Types.
3. Object. Used internally for all other Types -- these contain serializable members and are often called "POCO" types.

All serialization and deserialization of every Type goes through a single base class `JsonConverter` which has many benefits including the ability for types to compose though generics (`List<Dictionary<string,MyPOCO>>`) or properties (`MyProc.MyArray[0].MyChildPoco.MyProperty`), the ability to replace a converter and overall code re-use and consistency.

Instances of the converters are stored on `JsonSerializerOptions` in a dictionary keyed on the Type to convert. A single converter may know how to convert multiple Types.

## Metadata APIs
Internal converters inspect a combined view of metadata for a given Type. This metadata was originally obtained from using reflection (to look for properties and JsonAttribute-derived attributes on a Type and its members) and then merged with values from the `JsonSerializerOptions` instance. Merging the static reflection data with the run-time options is possible since options instance is immutable once the first (de)serialization occurs.

The immutable semantics of the options class also helps in other ways:
- The metadata could be embedded into code generation. This avoids the reflection cost of looking up the metadata and moves the memory storage from private bytes (per-process) to on-disk shared bytes (readonly memory accessed by several processes).
- The metadata could be used to control how code is generated if the generated code has branching or other logic that can be determined based upon the metadata. For example, a custom property lookup mechanism can be created per Type in an optimal fashion by building a B-tree from the beginning characters of the Type's known properties.
- Avoids locks and issues that would occur if another thread is alloed to change the options once serialization starts.

A custom Value converter (implemented by the community) does not have access to the metadata since these APIs are internal. This has been an issue for some scenarios and opening up the metadata APIs has been requested.

The built-in converters have access to metadata APIs. The metadata is maintained internally by a `JsonClassInfo` object for every type. For Object converters (meaning non-Value and non-Collection converters) there is also an instance of a `JsonPropertyInfo` object for every property.

# Startup performance costs
The overhead of deserializing a simple 4-property POCO is around **22ms** for the first run (<1ms for second run). This was measured in 5.0 master as of 3/27/2020 and tested on an Intel i7 3.2GHz.

Breaking this apart:
- System (non-serializer related) (~5ms)
  - ~2ms due to a one-time hit of loading and initializing SSE2 hardware intrinsics (in System.Runtime.Intrisics). SSE2 support was added during 5.0 to improve peformance of escaping JSON during serialization. It is commonly used elsewhere, however, so this can normally be subtracted from raw serializer numbers.
  - ~3ms for loading and initializing other assembly dependencies including using the Reader for the first time. There is some additional research here to determine where this hit is coming from, as 3.1 does not have this overhead. It may be that SSE2 contributes more than 2ms overhead.
- Serializer (~17ms)
  - ~5ms for main API overhead and initialization of the `JsonSerializerOptions`. This includes cache creation for built-in converters. Also, this is also the amount of time it takes to use a custom converter (that uses the writer) for the test POCO.
    - Includes ~1ms for creating the built-in `System.Uri` converter (loads the `System.Private.Uri.dll` assembly).
  - ~3ms due to direct and indirect use Reflection.Emit.
    - Direct: Reflection.Emit for getters, setters and constructors.
    - Indirect: usage of `Type.MakeGenericType` and `Type.MakeGenericMethod` so converters for value types can avoid boxing.
  - ~9ms usage of System.Reflection (and perhaps other items not yet accounted for) to initialize metadata. This includes inspecting each property for attributes.

# General options to consider
## Minimal
**This option should be assumed as a prerequisite for all of the other options below.**

Basic features include:
- Avoid runtime code generation when it is not supported.
- Improve caching.
- Other smaller features TBD.

Runtime code generation is used:
  - Directly: used to generate getters, setters and calls to constructors via Reflection.Emit APIs.
  - Indirectly: used to support strongly-typed converters for value types that avoid boxing. This includes usages of `Type.MakeGenericType` (and possibly `Type.MakeGenericMethod` pending additional research).

Some early issues created to start tracking the work:
- [Avoid Type.MakeGenericType when (de)serializing objects](https://github.com/dotnet/runtime/issues/35028)
- [Add deferred loading of System.Private.Uri assembly by the serializer](https://github.com/dotnet/runtime/issues/35029). Minor startup gains; low-hanging fruit.

## Reflection features
As described in the [System.Reflection roadmap](https://github.com/dotnet/runtime/issues/31895) it is possible to add new APIs that will eliminate common usages of Reflection.Emit including getters, setters and constructors. This will help with the direct useage of runtime code generation, but not the indirect usage.

Note that AOT code-gen options likely eliminate the need for new reflection features for those POCOs that are code-gen'd. However, we can't assume or force all POCOs to be code-gen'd.

Misc other thoughts:
    - A fallback (not recommended currently) is to change the internal converters and box for value types (and generic collections of value types). This will create many more short-lived allocs and decrease steady-state performance but allow the serializer to at least function in a "limp-mode" (roughly half of the throughput).
    - Another fallback would apply to a code-gen option where the generated POCO code closes the generic collections at build time. i.e. instantiate (or reference) a `JsonConverter<List<int>>` directly in the generated code instead of relying on the serializer to close `JsonConverter<T>` to `JsonConverter<List<int>>`.

## Converter code-gen
A (prototype)[https://github.com/layomia/jsonconvertergenerator] was created for this by @layomia. It used a simple (non-Roslyn) code generator using the existing custom Value converter model.

Various serializer features would need to be baked into the generated code including:
- Setting and getting properties.
- Calling the constructor, possibly with values from JSON.
- Null handling (e.g. if a null property should be serialized).
- Property lookup on deserialization (match JSON property name to Type property) potenially case-insensitive.
- Property naming policies (such as camel-casing).
- Escaping and unescaping property names and values (potentially using a custom escaper).
- Object reference handling (to preserve object references).
- Extension data (preserving umatched JSON during deserialization so it can be written during serialization).
- Async support (supporting a mode that doesn't drain a Stream upfront).
- Use of custom converters (where logic\code is not known so it can't be pushed to code-gen).
- Composition of objects (`List<Dictionary<string, Poco>>` or `SalesOrder.Customer.Address.City`).
- New features over time (default values, quoted numbers, ...)

Thus the resuling code generation will be large, complex and not servicable. At the extreme (with no new helper APIs exposed), every converter would need to implement the various serializer features above. However, a given converter's generated code could be made very specific to its Type and the environment expected to execute in (known `JsonSerializerOptions` values and features used), so much of the logic could be omitted.

## Metadata provider code-gen
A run-time only [prototype](https://github.com/steveharter/runtime/tree/ExtConverters) which does not include the actual code generator was created by @steveharter (see sample generated code below). It makes public the existing internal metadata classes including `JsonClassInfo` and `JsonPropertyInfo`.

It allows for minimal code gen and overlaps with other requested features including property ordering, before and after callbacks, and programmatic reading and writing of metadata.

<details>
  <summary>Snippets from prototype (click to expand)</summary>

```cs
public class WeatherForecast
{
    // The normal POCO properties:
    public DateTime Date { get; set; }
    public int TemperatureC { get; set; }
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
    public string Summary { get; set; }

    // Static constructor code-generated; on first use of WeatherForcecast Type it is automatically registered.
    static WeatherForecast()
    {
        // We need a way to get the correct options class; assume a global Serializer.Options for prototype (can vary per application).
        JsonSerializerOptions options = Serializer.Options;

        ClassInfo classInfo = new ClassInfo(options);
        classInfo.Initialize();
        options.Classes.TryAdd(typeof(WeatherForecast), classInfo);
    }

    // A nested code-generated private class to expose metadadata and handle get and set.
    private class ClassInfo : JsonClassInfo
    {
        public ClassInfo(JsonSerializerOptions options) : base(typeof(WeatherForecast), options)
        {
            CreateObject = () => { return new WeatherForecast(); };
        }

        protected override IList<JsonPropertyInfo> GetProperties()
        {
            var p = new JsonPropertyInfo<DateTime>();
            p.Options = Options;
            p.Converter = Serializer.DateTimeConverter;
            p.NameAsString = nameof(WeatherForecast.Date);

            var properties = new List<JsonPropertyInfo>(4);

            properties.Add(new JsonPropertyInfo<DateTime>
            {
                Options = Options,
                Converter = Serializer.DateTimeConverter,
                NameAsString = nameof(WeatherForecast.Date),
                HasGetter = true,
                HasSetter = true,
                ShouldSerialize = true,
                ShouldDeserialize = true,
                // These delegates are nice that they work with an aggressive linker.
                Get = (obj) => { return ((WeatherForecast)obj).Date; },
                Set = (obj, value) => { ((WeatherForecast)obj).Date = value; }
            });

            properties.Add(new JsonPropertyInfo<int>
            {
                Options = Options,
                Converter = Serializer.Int32Converter,
                NameAsString = nameof(WeatherForecast.TemperatureC),
                HasGetter = true,
                HasSetter = true,
                Get = (obj) => { return ((WeatherForecast)obj).TemperatureC; },
                Set = (obj, value) => { ((WeatherForecast)obj).TemperatureC = value; }
            });

            properties.Add(new JsonPropertyInfo<int>
            {
                Options = Options,
                Converter = Serializer.Int32Converter,
                HasGetter = true,
                NameAsString = nameof(WeatherForecast.TemperatureF),
                Get = (obj) => { return ((WeatherForecast)obj).TemperatureF; },
            });

            properties.Add(new JsonPropertyInfo<string>
            {
                Options = Options,
                Converter = Serializer.StringConverter,
                NameAsString = nameof(WeatherForecast.Summary),
                HasGetter = true,
                HasSetter = true,
                Get = (obj) => { return ((WeatherForecast)obj).Summary; },
                Set = (obj, value) => { ((WeatherForecast)obj).Summary = value; }
            });

            return properties;
        }
    }
}
```
</details>

## Object converter + code-gen
This is similar to "Metadata provider code-gen" but includes exposing a [new type of "object converter"](https://github.com/dotnet/runtime/issues/1562) for POCOs. This type of converter, however, exposes the raw reader and writer and thus has a slight performance degradation with Streams due to requiring "read-ahead". Read-ahead means the serializer ensures the JSON that will be obtained by the converter through the reader will not encounter end-of-buffer.

# Code-Gen
For the options that use code-gen there are several issues to discuss; this is covered in [other documentation](https://gist.github.com/layomia/5221e73374fd74dc360e74731dacfdb5) but one open issue is to determine what Types to add code-gen for. The two basic approaches are automatic and explicit:
- Automatic options:
  - Look to calls to the serializer and code-gen the Types passed to it, and all other Types reachable from those roots.
  - Generate code for all candidate types such as concrete reference types.
  - ...
- Explicit options:
  - Require an attribute to determine POCOs.
  - Specify in a config file.
  - ...

Once we determine the runtime flow and format of the generated code, there is still much tooling to get the Roslyn-based code generator working.

# Comparison of options
Below is the listing of options; severe limimations are adorned with an &#x274C;.

| | Minimal | Reflection features | Code-gen option: explicit | Code-gen option: metadata provider | Code-gen option: "object" custom converter |
| :-- | :-- | :-- | :-- | :-- | :-- |
| **Goal: reach (without code-gen)** | yes | no | N/A | N/A | N/A |
| *Removes all Reflection.Emit?* | no | yes | yes | yes | yes |
| *Removes all Type.MakeGenericType?* | no | no &#x274C; (possibly with other new features) | possibly | possibly | possibly |
| | | | | | |
| **Goal: faster startup perf**
| *Baseline startup overhead* | 22ms | 22ms | 22ms | 22ms | 22ms |
| *Expected startup overhead* | ~18ms | <=~18ms (need to protoype) | ~6ms | 7-10ms | 7-10ms |
| *Startup overhead for additional properties* | pay-per-property | pay-per-property | none (or minor) | pay-per-property | pay-per-property |
| *Startup overhead for additional POCOs* | pay-per-type | pay-per-type | none (or minor) | pay-per-type | pay-per-type |
| *Steady state CPU improvement?* | no | no | yes | no | no |
| *Steady state regression?* | no | not expected | not expected but needs fast property lookup design | expected minor on property get and set | expected minor on property get and set |
| *Fast Stream perf (no read-ahead)* | yes | yes | possible but hard &#x274C; | yes | possible but hard &#x274C; |
| | | | | | |
| **Goal: reduced memory usage** | 
| *Private bytes reduction* | yes (less MakeGenericType) | yes (less Emit) | yes (metadata baked into code) | TBD | TBD|
| | | | | | |
| **Goal: reduced size-on-disk**
| *Supports smaller runtime with no Emit* | no | yes | no | no | no
| *Could support linker "tree shaking"* | no | possible for the default ctor only | yes | yes | yes
| *POCO code gen size* | N/A | N/A | large &#x274C; | small | small |
| | | | | | |
| **Serviceability** | best | best | poor or none (needs a "kill" feature for security) &#x274C; | good | good |
| | | | | | |
| **Investment cost**
| *Investment level* | several smaller features | medium - likely will be done eventually | larger | larger | larger |
| *Public API additions* | none | minor | minor | major | major |
| *Layers on existing internal object converter logic** | yes | yes | no &#x274C; | yes | somewhat |
| | | | | | |
| **Dependencies**
| *Requires feature: MakeGenericType replacement or fallback to avoid boxing* | yes | yes | yes | yes | yes |
| *Requires feature: faster converter initialization* | yes | yes | yes | yes | yes |
| *Requires feature: improve detection of lack of reflection emit (netstandard 2.0 build of S.T.Json)* | yes | TBD | TBD | TBD | TBD |
| | | | | | |
| **Other**
| *Other useful features?* | New reflection APIs can be leveraged by community | no | no | Property ordering, callbacks, metadata inspection | Property ordering, callbacks |

# Recommendations for 5.0
The "Minimal" option should be the starting point - this will help reduce startup time and help on the road to improving "reach". However, the serializer will continue to use Reflection.Emit for .NET runtimes (and slower, standard invoke-based reflection for other runtimes).

The "Minimal" option has the subfeature "improve detection of lack of reflection emit" but this is only necessary if we don't implement the "Reflection features" option that removes usage of Reflection.Emit.

The "Reflection features" option may or may not be implemented for 5.0, but is likely to be implemented eventually due to high demand.

The "Metadata code-gen" option does not require "Reflection features" although if "Reflection features" is implemented then the code generation may be reduced (i.e. no code generation necessary for getters, setters and constructors).

**The recommended plan for 5.0 (in sequence):**
1. *Required*: implement the required features for the "Minimal" option.
2. *Optional*: Implement "Reflection features". If the result is noticeably slower in the steady-state scenarios then we may need to consider contingency plans such as options to control whether to use IL emit or not -- i.e. startup vs. steady-state tradeoffs.
3. *Optional*: pending performance or overlap with other scenarios, implement "Metadata code-gen". This avoids all usages of System.Reflection including looking up properties and attributes. There may or may not be generated code for getters, setters or constructors depending on the performance of "Reflection features" and linker "tree shaking" requirements.
