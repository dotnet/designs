# Overview and goals

Apri 20th, 2020.

This document provides options and recommendations for 5.0 to handle overlapping serializer goals including reach, faster cold startup performance, reducing memory usage, and smaller size-on-disk.

## Reach
Achieved by limiting or removing dependencies on Reflection.Emit and `Type.MakeGenericType`. This allows the serializer to be used on additional runtimes including [Xamarin iOS](https://github.com/dotnet/runtime/issues/31326).

Ideally we add a high-performance emit replacement for targeted scenarios such as property setters, getters and constructors.

Some serializers do not have a non-emit fallback. Although System.Text.Json has a fallback for emit, it does not have a fallback for its usage of `Type.MakeGenericType`.

## Faster startup performance

This is important for scenarios including [micro-services](https://github.com/dotnet/runtime/issues/1568) where processes start and exit frequently. It is also important for benchmarks including TechEmpower.

The serializer has fast startup compared to most other serializers including Newtonsoft's JSON.Net, JIL and Utf8Json. Startup perf is mostly affected by generating IL - the Jil serializer is the most aggressive here and has the highest startup cost.

There is no stated goal for 5.0 to make the steady-state CPU performance faster than it is now in 5.0 for general sceanrios, although we do not want to noticably regress steady-state performance while addressing the stated goals including improving startup performance. It is possible some code-gen options described later may have steady-state performance gains, but again that is not a stated goal.

<details>
  <summary>5.0 master startup benchmarks</summary>

A small POCO was used (`LoginViewModel` benchmark test class).

| Serializer |  Serialize (us)  | Serialize ratio | Deserialize (us) | Deserialize ratio |
| :-- | :-- | :-- | :-- | :--
| **System.Text.Json** | 4,720 | 1.00 | 19,379 | 1.00 
| **Json.NET** | 24,916 | 5.28 | 102,226 | 5.28 
| **Utf8Json** | 7,290 | 1.54 | 106,817 | 5.51 
| **Jil** | 66,456 | 14.08 | 170,548 | 8.80 
</details>

## Reduced memory usage
Reducing private bytes (non-shared or per-process) memory is important for Bing scenarios amongst others.
    - This can be done by reducing emit\`Type.MakeGenericType` and by capturing POCO metadata in generated code instead of RAM.

## Reduced size-on-disk
Primarily this is for faster downloads of the runtime and applications

The runtime can be made smaller by removing emit support. Some runtimes including iOS do not support dynamic code generation (emit or jitting) due to policy and thus require AOT (Ahead-Of-Time compilation).

The application can be made smaller by support aggressive linking behavior (e.g. "tree-shaking"). This can be done in a number of ways outside the scope of this document. However, approaches in this document that do code-gen may automatically handle this due to generated calls to members.

# Serializer design background
Today the serializer avoids using Reflection.Emit on non-.NET scenarios through the use of an #ifdef. However, the serializer still uses emit indirectly through `Type.MakeGenericType`. Note that Xamarin stubs `MakeGenericType` and tries to support these on iOS by pre-generating types via AOT but there are still issues with this (including not being able to determine all possible `<T>` variants).

The existing serializer makes use of custom converters which is a class that knows how to implement a Read(...) and Write(...) method. There are 3 types of converters:
1. Value. The only type of custom converters that can be implemented publically today (by the community).
2. Collection. Used internally for IEnumerable Types.
3. Object. Used internally for all other Types.

The converters are stored on the `JsonSerializerOptions` in a dictionary keyed on the Type to convert. A single converter may know how to convert multiple Types.

Internal converters use metadata for a given Type which is obtained from using reflection (to look for properties and JsonAttribute-derived attributes on a Type and its members). This metadata is then merged with values from the `JsonSerializerOptions` instance which is possible since options instance is immutable once the first (de)serialization occurs.

The immutable semantics of the options also help in other ways:
- The metadata could be embedded into code generation (to avoid the reflection cost of looking up the metadata).
- The metadata could be used to control how code is generated if the generated code has branching or other logic that can be determined based upon the metadata (for example, having a custom property lookup mechanism for each Type).
- Avoids locks and issues that would occur if another thread changes the options.

A custom Value converter (implemented by the community) does not have access to the metadata since the metadata types are internal. This has been an issue for some scenarios and opening up the metadata APIs has been requested.

The built-in converters do have access to metadata. The metadata is maintained internally by a `JsonClassInfo` object for every type. For Object converters (meaning non-Value and non-Collection converters) there is also an instance of a `JsonPropertyInfo` object for every property.

# Startup performance costs
The overhead of deserializing a simple 4-property POCO is around **22ms** for the first run (<1ms for second run). This was measured in 5.0 master as of 3/27/2020 and tested on an Intel i7 3.2GHz.

Breaking this apart:
- System (non-serializer related) (~5ms)
  - ~2ms due to a one-time hit of loading and initializing SSE2 hardware intrinsics (in System.Runtime.Intrisics). SSE2 support was added during 5.0 to improve peformance of escaping JSON during serialization. It is commonly used elsewhere, however, so this can normally be subtracted from raw serializer numbers.
  - ~3ms for loading\initializing other assembly dependencies including using the Reader for the first time. There is some additional research here to determine where this hit is coming from, as 3.1 does not have this overhead. It may be that SSE2 contributes more than 2ms overhead.
- Serializer (~17ms)
  - ~5ms for main API overhead and initialization of the `JsonSerializerOptions`. This includes cache creation for built-in converters. Also, this is also the amount of time it takes to use a custom converter (that uses the writer) for the test POCO.
    - Includes ~1ms for creating the built-in `System.Uri` converter (loads the `System.Private.Uri.dll` assembly).
  - ~3ms due to direct and indirect use Reflection.Emit.
    - Direct: Reflection.Emit for property setters\getters and constructors.
    - Indirect: usage of `Type.MakeGenericType` and `Type.MakeGenericMethod` so converters for value types can avoid boxing.
  - ~9ms usage of System.Reflection (and perhaps other items not yet accounted for) to initialize metadata. This includes inspecting each property for attributes.

# General options to consider
## Minimal
**This option should be assumed as a prerequisite for all of the other options below.**

Basic features include:
- Avoid reflection emit when it is not supported
- Improve caching
- Other smaller features TBD.

Reflection emit is used both directly and indirectly by the serializer:
  - Directly: used to generate getters, setters and calls to constructors.
  - Indirectly: used to support strongly-typed converters for value types that avoid boxing. This includes usages of `Type.MakeGenericType` (and possibly `Type.MakeGenericMethod` pending additional research).

Some early issues created to start tracking the work:
- [Avoid Type.MakeGenericType when (de)serializing objects](https://github.com/dotnet/runtime/issues/35028)
- [Add deferred loading of System.Private.Uri assembly by the serializer](https://github.com/dotnet/runtime/issues/35029). Minor startup gains; low-hanging fruit.

## Reflection features
As described in the [System.Reflection roadmap](https://github.com/dotnet/runtime/issues/31895) it is possible to add new APIs that will eliminate common usages of Reflection.Emit including getters\setter\constructors. This will help with the direct usage of Reflection.Emit, but not the indirect usage.

Note that AOT code-gen options likely eliminate the need for new reflection features for those POCOs that are code-gen'd. However, we can't assume or force all POCOs to be code-gen'd.

Misc other thoughts:
    - A fallback (not recommended currently) is to change the internal converters and box for value types (and generic collections of value types). This will create many more short-lived allocs and decrease steady-state performance but allow the serializer to at least function in a "limp-mode" (roughly half of the throughput).
    - Another fallback would apply to a code-gen option where the generated POCO code closes the generic collections at build time. i.e. instantiate (or reference) a `JsonConverter<List<int>>` directly in the generated code instead of relying on the serializer to close `JsonConverter<T>` to `JsonConverter<List<int>>`.

## Converter code-gen
A prototype was created for this by @layomia.

It used a simple (non-Roslyn) code generator using the existing custom Value converter model. Various serializer features (null handling, property lookup on deserialization, reference handling, async support, use of custom converters, etc) would need to be baked into the generated code. Thus the resuling code generation will be large, complex and not servicable. At the extreme (with no new helper methods exposed the serializer), each converter would need to implement all of the various features of the serializer.

## Metadata provider code-gen
A run-time only prototype (not the actual generator) was created for this by @steveharter (see sample generated code below). It makes public the existing internal metadata classes including `JsonClassInfo` and `JsonPropertyInfo`.

It allows for minimal code gen and overlaps with other requested features including property ordering, before\after callbacks and programmatic reading\writing of metadata.

<details>
  <summary>Snippets from prototype</summary>

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

    // A nested code-generated private class to expose metadadata and handle get\set.
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
                // These delegates are nice that they work with aggressive linker
                // but slower due to the extra hop (which may be avoided with
                // new reflection features).
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
This is simular to "Metadata provider code-gen" but includes exposing a new type of "object converter". This type of converter, however, exposes the raw reader\writer and has performance issues with Streams due to requiring read-ahead.

# Code-Gen
For the options that use code-gen there are several issues to discuss; this is covered in other documention but one open issue is to determine what Types to add code-gen for. The two basic approaches are automatic and explicit:
- Automatic options:
  - Look to calls to the serialzer and code-gen types passed to it (and all other Types reachable those roots).
  - Generate code for all candidate types (concrete non-struct types?).
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
| **Goal: reach (without code-gen)** | yes | no | N\A | N\A | N\A |
| *Removes all Reflection.Emit?* | no | yes | yes | yes | yes |
| *Removes all Type.MakeGenericType?* | no | no &#x274C; (possibly with other new features) | possibly | possibly | possibly |
| | | | | | |
| **Goal: faster startup perf**
| *Baseline startup overhead* | 22ms | 22ms | 22ms | 22ms | 22ms |
| *Expected startup overhead* | ~18ms | <=~18ms (need to protoype) | ~6ms | 7-10ms | 7-10ms |
| *Startup overhead for additional properties* | pay-per-property | pay-per-property | none (or minor) | pay-per-property | pay-per-property |
| *Startup overhead for additional POCOs* | pay-per-type | pay-per-type | none (or minor) | pay-per-type | pay-per-type |
| *Steady state CPU improvement?* | no | no | yes | no | no |
| *Steady state regression?* | no | not expected | not expected but needs fast property lookup design | expected minor on property getter\setters | expected minor on property getter\setters |
| *Fast Stream perf (no read-ahead)* | yes | yes | possible but hard &#x274C; | yes | possible but hard &#x274C; |
| | | | | | |
| **Goal: reduced memory usage** | 
| *Private bytes reduction* | yes (less MakeGenericType) | yes (less Emit) | yes (metadata baked into code) | TBD | TBD|
| | | | | | |
| **Goal: reduced size-on-disk**
| *Supports smaller runtime with no Emit* | no | yes | no | no | no
| *Could support linker "tree shaking"* | no | possible for the default ctor only | yes | yes | yes
| *POCO code gen size* | n\a | n\a | large &#x274C; | small | small |
| | | | | | |
| **Serviceability** | best | best | poor\none (needs a "kill" feature for security) &#x274C; | good | good |
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

The "Metadata code-gen" option does not require "Reflection features" although if "Reflection features" is implemented then the code generation may be reduced (i.e. no code generation necessary for property setters\getters\constructors).

**The recommended plan for 5.0 (in sequence):**
1. *Required*: implement the required features for the "Minimal" option.
2. *Optional*: Implement "Reflection features". If the result is noticably slower in the steady-state scenarios then we may need to consider contingency plans such as options to control whether to use IL emit or not -- i.e. startup vs. steady-state tradeoffs.
3. *Optional*: pending performance and\or overlap with other scenarios, implement "Metadata code-gen". This avoids all usages of System.Reflection including looking up properties and attributes. There may or may not be generated code for property getters\setters\constructors depending on the performance of "Reflection features" and linker "tree shaking" requirements.
