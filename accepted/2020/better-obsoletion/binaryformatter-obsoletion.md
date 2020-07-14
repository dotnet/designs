# BinaryFormatter Obsoletion Strategy

This document applies to the following framework versions:

* .NET 5.0+

Applications which target .NET Framework 2.x - 4.x or .NET Core 2.1 - 3.1 are _not affected_ by this proposal. `BinaryFormatter` will continue to work as expected in those applications. Consumers of `BinaryFormatter` are still advised to consult the [`BinaryFormatter` security guide](https://github.com/dotnet/docs/pull/19442) to help inform their risk assessment.

## Introduction

As part of modernizing the .NET development stack and improving the overall health of the .NET ecosystem, it is time to sunset the `BinaryFormatter` type. `BinaryFormatter` is the mechanism by which many .NET applications find themselves exposed to critical security vulnerabilities, and its continued usage results in numerous such incidents every year across both first-party and third-party code.

We have messaged previously that `BinaryFormatter` is not safe for untrusted input, but outside of the security community this guidance hasn't really taken root. This is partly because there's already a critical mass of code using `BinaryFormatter`, and developers are generally disincentivized from changing existing code. It's also partly due to the fact that threat modeling is an advanced topic not well understood by the majority of developers. Without the .NET team introducing a forcing function, the ecosystem holistically does not feel the need to improve.

See the [`BinaryFormatter` security guide](https://github.com/dotnet/docs/pull/19442) for more information on the problem space.

`BinaryFormatter` also leads to the creation of fragile and non-versionable types. Its operation relies primarily on private reflection over an object's instance fields. This ties the serialized payload format to internal implementation details of the target types. Updating or improving the implementation details of the target type often creates compatibility problems between serialized versions.

Encouraging migration away from `BinaryFormatter` has the additional benefit of making applications more friendly to linker trimming. .NET is [heavily invested in linker trimming](https://github.com/dotnet/designs/blob/master/accepted/2020/feature-switch.md) as a means of reducing the deployed footprint of .NET-based applications and web services. This capability relies upon the compiler being able to statically analyze what code paths are reachable from the application. `BinaryFormatter` and other serializers which perform unbounded reflection based on input from the wire are not conducive to this goal, as the real type information isn't known until the `Type.GetType` call at runtime. We would like to move these applications onto other serializers (such as those in [the _System.Text.Json_ package](https://docs.microsoft.com/en-us/dotnet/standard/serialization/system-text-json-how-to)) to help drive our ability to enable trimming throughout the ecosystem.

## Timeline overview

### .NET 5 (Nov 2020)

- Allow disabling `BinaryFormatter` via an opt-in feature switch
  - ASP.NET projects disable `BinaryFormatter` by default but can re-enable
  - WASM projects disable `BinaryFormatter` with no ability to re-enable
  - All other project types (console, WinForms, etc.) enable `BinaryFormatter` by default
- .NET produces guidance document on migrating away from `BinaryFormatter`
- All outstanding `BinaryFormatter`-related issues resolved _won't fix_
- Introduce a `BinaryFormatter` tracing event source
- `Serialize` and `Deserialize` marked obsolete _as warning_

### .NET 6 (Nov 2021)

- No new `[Serializable]` types introduced
- No new calls to `BinaryFormatter` from any first-party dotnet org code base
- All first-party dotnet org code bases begin migration away from `BinaryFormatter`

### .NET 7 (Nov 2022)

- All first-party dotnet org code bases complete migration away from `BinaryFormatter`
- `BinaryFormatter` disabled by default across all project types
- Entirety of `BinaryFormatter` type obsolete _as warning_

### .NET 8 (Nov 2023)

- `BinaryFormatter` infrastructure removed from .NET

## Timeline specifics

### Allow disabling BinaryFormatter via an opt-in feature switch (.NET 5)

In .NET 5 we will introduce a [feature switch](https://github.com/dotnet/designs/blob/master/accepted/2020/feature-switch.md) to selectively allow `BinaryFormatter` to be disabled in an application. This can be accomplished in one of three ways, as demonstrated below.

__Disabling `BinaryFormatter` via the application's .csproj__:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net5.0</TargetFramework>
    <EnableUnsafeBinaryFormatterSerialization>false</EnableUnsafeBinaryFormatterSerialization>
  </PropertyGroup>
</Project>
```

__Disabling `BinaryFormatter` via the application's *runtimeConfig.template.json*__:

```json
{
    "configProperties": {
        "System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization": false
    }
}
```

__Disabling `BinaryFormatter` via an explicit call to `AppContext.SetSwitch`__:

```cs
AppContext.SetSwitch("System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization", false);
```

If this switch is set to _true_, `BinaryFormatter` serialization + deserialization will be enabled for the application. If this switch is set to _false_, `BinaryFormatter` serialization + deserialization will be disabled for the application and will throw `NotSupportedException`. Setting the value to _false_ also allows the .NET linker to trim away `BinaryFormatter`-related code paths, reducing the total disk footprint of the resulting binary.

If the switch is not set, the target project type will determine whether `BinaryFormatter` serialization + deserialization is allowed or disallowed. Note that this is determined by the application type. Libraries cannot set this switch via their own _.csproj_ or _runtimeConfig.json_ files.

> We expect high-sensitivity applications to set this switch to __false__ as part of an overall attack surface reduction strategy. Any document we produce that details how to configure high-sensitivity applications should recommend setting this switch. The scenario for setting this switch to false eagerly is that the developer does not expect the application to invoke `BinaryFormatter`-related code paths during normal operations, and they want some extra assurance that a latent bug or untested edge case in a dependency cannot re-activate the `BinaryFormatter` infrastructure.

> Certain project types - such as WinForms projects - may use `BinaryFormatter` as an implementation detail. Disabling `BinaryFormatter` for these project types in .NET 5 may interfere with the correct functionality of the application. See later in this document for the approach the runtime will take for ensuring this switch works correctly in future .NET versions.

Disabling `BinaryFormatter.Serialize` and `Deserialize` via the feature switch doesn't affect the behavior of any other APIs on `BinaryFormatter`. For instance, the existing `BinaryFormatter` property getters and setters will continue to work. Types like `ObjectManager` which are intended to support the `BinaryFormatter` infrastructure will also work if the application has direct calls into those APIs. If the application does not have direct calls to these APIs, the linker will likely prune these now-unused types when it prunes the `BinaryFormatter` implementation.

There is no API to query whether a call to `BinaryFormatter.Serialize` or `BinaryFormatter.Deserialize` will fail due to this switch being set. Callers can instead catch the `NotSupportedException` and query the `AppContext` manually to see if support for serialization has been disabled. An example of this is provided below.

```cs
BinaryFormatter formatter = new BinaryFormatter();
try
{
    formatter.Serialize(/* ... */);
}
catch (NotSupportedException ex)
{
    if (AppContext.TryGetSwitch("System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization", out bool isEnabled)
        && !isEnabled)
    {
        // BinaryFormatter serialization support is disabled; handle it here
    }
    else
    {
        throw; // BinaryFormatter serialization is enabled; rethrow exception
    }
}
```

The libraries team will also document known scenarios that will be broken when the `BinaryFormatter` code paths are disabled. One such example is attempting to read _.resx_ files with embedded `BinaryFormatter`-serialized objects. These scenarios are not expected to be common in web applications, but they do nevertheless exist to a limited extent.

#### Disablement of BinaryFormatter for certain project types (.NET 5)

For WASM (Blazor Client) projects, the BinaryFormatter feature switch will be hardcoded to __disabled__. The implementations of `BinaryFormatter.Serialize` and `BinaryFormatter.Deserialize` will throw `NotSupportedException`. There is no ability for WASM applications to re-enable this feature through configuration or through a runtime switch. This hardcoded behavior extends to all other novel application types that might be introduced after the .NET 5 timeframe, including mobile apps and other restricted environment configurations.

Starting with ASP.NET 5.0, the implementations of `BinaryFormatter.Serialize` and `BinaryFormatter.Deserialize` will _by default_ throw `NotSupportedException`. This is being done to reduce the attack surface area of web applications. This project type has historically been the largest target of deserialization exploits.

To re-enable the `BinaryFormatter` code paths in ASP.NET projects, the application must set the feature switch described above in their _.csproj_, and as demonstrated below.

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net5.0</TargetFramework>
    <!-- WARNING: re-enabling the switch below could subject your application to critical security vulnerabilities -->
    <EnableUnsafeBinaryFormatterSerialization>true</EnableUnsafeBinaryFormatterSerialization>
  </PropertyGroup>
</Project>
```

For other project types - such as console, WinForms, and WPF - `BinaryFormatter` will continue to be enabled by default. No configuration is necessary if the app wishes to continue using the `BinaryFormatter` APIs. However, developers can still disable `BinaryFormatter` by using the feature switch mechanism mentioned previously.

### .NET produces guidance document on migrating away from BinaryFormatter (.NET 5)

The .NET team will produce a document showing how to accomplish various serialization scenarios in manners that don't involve `BinaryFormatter`. Some examples of scenarios that we can show:

- Reading the raw binary contents of a graphic and constructing an Image from it.
- Using _System.Text.Json_ to serialize / deserialize object models, including dictionaries and other collection types.
- Demonstrating how to serialize / deserialize `CultureInfo` via just the culture name.
- Transmitting `Exception` objects safely across the wire.

We need to be prepared for the fact that not every scenario will be achievable. For example, .NET Framework allows serializing delegates, but .NET Core doesn't. This is by design and no replacement is intended. As another example, _System.Text.Json_ doesn't allow deserializing cyclic object graphs by default. We would demonstrate how to opt in to these features while calling attention to the fact that consumers should consider the security impact of doing so.

We are also intentionally _not_ providing sample code on the use of a `SerializationBinder` as a security mechanism, as we cannot guarantee the safety of this design and should not promote its usage as such.

### All outstanding BinaryFormatter-related issues resolved won't fix (.NET 5)

We'll perform a final pass through all the `BinaryFormatter`-related issues opened on GitHub, resolving all of them _won't fix_ and pointing them to this document. There are only three exclusions to this pass:

- Functional regressions introduced as part of an in-place servicing release.
- Severe performance regressions introduced as part of an in-place servicing release.
- Open issues which track implementing this deprecation plan.

All other issues, including incompatibility between .NET Framework + .NET Core, or asking for new types to be marked `[Serializable]`, or asking for new features or bug fixes, will be closed.

### Introduce a BinaryFormatter tracing event source (.NET 5)

To help app developers detect unexpected calls to `BinaryFormatter` within dependencies, we will enable a tracing event source within `BinaryFormatter` itself. This tracing source will provide notification that `BinaryFormatter.Serialize` or `BinaryFormatter.Deserialize` was called, and developers can use this information as part of their own process of auditing `BinaryFormatter` usage.

It is anticipated that this mechanism will use the _System.Diagnostics.DiagnosticSource_ infrastructure. More information on using this infrastructure is available at [the user guide](https://github.com/dotnet/runtime/blob/master/src/libraries/System.Diagnostics.DiagnosticSource/src/DiagnosticSourceUsersGuide.md).

App developers can test-run their application with this trace listener enabled, and if they see no unexpected calls to `BinaryFormatter` they can confidently set the AppContext setting to disable `BinaryFormatter` in production. This allows them to limit their exposure to `BinaryFormatter`-related vulnerabilities within their own services.

### Serialize and Deserialize marked obsolete as warning (.NET 5)

The `Serialize` and `Deserialize` methods on `BinaryFormatter`, `Formatter`, and `IFormatter` will be marked obsolete _as warning_, as shown below.

```cs
namespace System.Runtime.Serialization.Formatters.Binary
{
    public sealed class BinaryFormatter : IFormatter
    {
        [Obsolete("...", error: false, DiagnosticId = "...")]
        public object Deserialize(Stream serializationStream);
        [Obsolete("...", error: false, DiagnosticId = "...")]
        public void Serialize(Stream serializationStream, object graph);
    }
}

namespace System.Runtime.Serialization
{
    public interface IFormatter
    {
        [Obsolete("...", error: false, DiagnosticId = "...")]
        object Deserialize(Stream serializationStream);
        [Obsolete("...", error: false, DiagnosticId = "...")]
        void Serialize(Stream serializationStream, object graph);
    }

    public abstract class Formatter : IFormatter
    {
        [Obsolete("...", error: false, DiagnosticId = "...")]
        public abstract object Deserialize(Stream serializationStream);
        [Obsolete("...", error: false, DiagnosticId = "...")]
        public abstract void Serialize(Stream serializationStream, object graph);
    }
}
```

The error message text is TBD but will direct callers to the documentation available at https://aka.ms/binaryformatter. The diagnostic id will be set per the [better obsoletion](https://github.com/dotnet/designs/pull/62) specification.

This is likely to be very noisy since it's the first change that will begin to produce warnings in our developer audience's own code. We will continue updating the guidance document beyond the .NET 5.0 timeframe so that it's clear what kinds of changes developers should make within their own code bases.

The behavior of the obsoleted APIs will not change. Applications which suppress or ignore the warning will see no runtime behavioral difference, assuming the "disable `BinaryFormatter` entirely" feature switch mentioned previously in this document has not been utilized.

### No new \[Serializable\] types introduced (.NET 6)

.NET 5.0 will be the final version of .NET that introduces `[Serializable]` types. Beginning with .NET 6.0, no new types will be marked `[Serializable]`, nor will the `[Serializable]` attribute be added to existing types or to types ported from .NET Framework. Introducing serializable annotations runs counter to our goal of spinning down usage of `BinaryFormatter` framework-wide.

This prohibition on new serializable types applies only to projects in the _dotnet_ org on GitHub. Other frameworks, libraries, or applications built on .NET may continue to use `[Serializable]` in their own code bases.

The exclusion is for dotnet org first-party projects which target _netstandard2.0_ or earlier. For these projects, `Exception`-derived types should continue to be annotated as `[Serializable]` so that they may be remoted across app domain boundaries. Any new `Exception`-derived types that are defined in a library which is not available to _netstandard2.0_ callers will not be annotated with \[Serializable\]. We should be following our own guidance on how to safely serialize exception information without involving `BinaryFormatter`.

### No new calls to BinaryFormatter from any first-party dotnet org code base (.NET 6)

Beginning with .NET 6.0, no _new_ code introduced into the _dotnet_ org GitHub repos may call into `BinaryFormatter`, _regardless of target runtime_. Introducing such code would run counter to our goal of spinning down usage of `BinaryFormatter` framework-wide. This prohibition does not apply to user or partner code. Those code bases may continue to use `BinaryFormatter` as needed, though they are strongly discouraged from doing so.

This should not prevent us from refactoring existing first-party code which uses `BinaryFormatter`. The intent of this policy is to prevent `BinaryFormatter`'s usage in new scenarios, not to prevent teams from maintaining their own code bases. Documentation, tests, and other "non-shipping" code is exempt.

### - All first-party dotnet org code bases begin migration away from BinaryFormatter (.NET 6)

The WinForms designer should enable new .resx formats which do not involve `BinaryFormatter`, following our guidance on how to serialize such resources safely. These new .resx formats should be the default for any scenario which previously required `BinaryFormatter` involvement.
MSBuild and other components which handle `BinaryFormatter`-serialized payloads opaquely should continue to process these payloads. No action is required on their end unless they attempt to deserialize these payload.

If `BinaryFormatter` is used in any other data persistence scenario (e.g., clipboard or app saved state), the area owners should consult the guidance document to create a safe alternative. All code paths which involve `BinaryFormatter` must have equivalent non-`BinaryFormatter` alternatives available, though these alternatives needn't have 100% feature parity. Area owners should make every effort to enable the non-`BinaryFormatter` code paths as the default for creating new data. It remains allowable for these libraries to read existing data via `BinaryFormatter`.

#### Auditing first-party code bases for Type.GetType calls

As part of this, dotnet org developers should audit the area of ownership for calls to `Type.GetType` and related APIs, as these could function as unrestricted deserialization equivalents. Any call sites which deal with untrusted data should clearly be annotated as such in code comments. If necessary, the owners should open issues to track removing the dangerous code patterns or documenting the API as dangerous. This will allow us to make more informed estimates of our libraries' own exposure to potential RCE attacks.

### All first-party dotnet org code bases complete migration away from BinaryFormatter (.NET 7)

No first-party _dotnet_ org code base contains an enabled-by-default calls to `BinaryFormatter`. The call sites may remain in the library but must be guarded by an opt-in switch. This opt-in switch must be specified by _the app developer_, not by an "I'm serialized using `BinaryFormatter`" flag within the payload itself. All other payload formats must be written or read using a `BinaryFormatter` alternative.

Importantly, none of our project types (WinForms / WPF / ASP.NET / mobile / etc.) can require developers to opt back in to allowing `BinaryFormatter` for any greenfield development. Applications which require `BinaryFormatter` for app-compat reasons must require developers to perform an explicit opt-in gesture to re-enable these code paths.

### BinaryFormatter disabled by default across all project types (.NET 7)

Beginning with .NET 7.0, the "disable `BinaryFormatter`" feature switch is activated by default across all project types. Developers must perform an explicit opt-in gesture to re-enable these code paths. See the earlier feature switch section for more information.

### Entirety of BinaryFormatter type obsolete as warning

In .NET 7.0, the following APIs will be marked `[Obsolete]` _as warning_.

- The entirety of the `BinaryFormatter` type.
- The entirety of the `ISerializable` type.
  - Includes any public or explicitly-implemented `GetObjectData` methods.
- `OnSerializingAttribute`, `OnDeserializingAttribute`, `OnSerializedAttribute`, and `OnDeserializedAttribute`
- Any methods the above attributes are applied to.
- The entirety of the types `FormatterServices`, `ISerializationSurrogate`, `ISurrogateSelector`, `ObjectManager`, `SurrogateSelector`, `FormatterAssemblyStyle`, `FormatterTypeStyle`, `IFieldInfo`, and `TypeFilterLevel`.
- Any serialization ctors (`".ctor(SerializationInfo, StreamingContext)"`) on public types.

As mentioned previously, members on `Exception`-derived types in projects which are consumed by _netstandard2.0_ or earlier will not be subject to obsoletion.

For packages which perform asset harvesting or which otherwise aren't always compiled against _NetCoreAppCurrent_, obsoleting the serialization ctors and `GetObjectData` methods should be done on a best-effort basis. We'll have to see how difficult the task will be, but let's not force ourselves to perform unnatural contortions in the packaging system.

This will also follow a "better obsoletion" mechanism so that applications can suppress the entire category of warnings if needed.

### BinaryFormatter infrastructure removed from .NET (.NET 8)

In .NET 8.0, the entirety of the `BinaryFormatter` infrastructure will be removed from the product. Here, "`BinaryFormatter` infrastructure" refers to any API which was obsoleted as part of an earlier stage of this proposal, even if that API is not itself on the `BinaryFormatter` type. The implementation of all such APIs will be to throw `PlatformNotSupportedException`. The internal `BinaryFormatter` implementation will no longer exist in .NET in any form, and there will not exist a feature switch to re-enable the implementation.

All code bases which target _net8.0_ or above must be fully migrated away from `BinaryFormatter` at this time. This applies to _dotnet_ org first-party code, partner team code, and third-party user code.

There is currently no plan to remove the now-dead APIs themselves from the SDK. However, this may be suggested in the future either concurrent with or occurring after the final "obsolete as error" behavior.

## Q\&A

__What does this mean for already-deployed applications?__

Nothing. There are no behavioral changes being proposed to `BinaryFormatter` for existing in-support versions of .NET, including .NET Framework 2.x - 4.x or .NET Core 2.1 - 3.1. Applications already deployed on these frameworks will continue to work correctly and will enjoy full support for the lifetime of the product. We will not modify the behavior of `BinaryFormatter` as part of any servicing release.

However, per the [`BinaryFormatter` security guide](https://github.com/dotnet/docs/pull/19442), we recommend that all applications migrate off of `BinaryFormatter` as they find the opportunity to do so.

__What serializer should be used instead of `BinaryFormatter`?__

This really depends on the scenario. XML and JSON serializers follow open standards and are often appropriate when the client and the server are written by two different entities. Compressed binary formats may be appropriate for proprietary protocols which want to minimize bandwidth usage. Some serializers opt for speed (total objects serialized or deserialized per second) at the expense of producing slightly larger payloads.

The [`BinaryFormatter` security guide](https://github.com/dotnet/docs/pull/19442) contains recommendations for choosing a serializer appropriate for the given scenario.

__Is `BinaryFormatter` officially supported in .NET 5.0 and beyond?__

Yes, per the limitations described in this document. As long as the type exists in the framework and has an implementation, support is available. For example, if an app which uses `BinaryFormatter` behaves as expected in .NET 5.0 but fails after a .NET 5.0.x patch is applied, this regression should be reported to the product team for resolution.

See the earlier section section titled "All outstanding BinaryFormatter-related issues resolved won't fix" for more information on what categories of issues qualify for support.

__What about other `BinaryFormatter`-equivalent types like `LosFormatter`, `ObjectStateFormatter`, `SoapFormatter`, and `NetDataContractSerializer`?__

These types exist only in .NET Framework 2.x - 4.x. They do not exist in .NET Core or .NET 5.0+. These types will not be reintroduced in any future version of .NET. Applications which wish to migrate to .NET 5.0+ are required to move off of these types. Applications which continue targeting .NET Framework 2.x - 4.x are advised to move off of these types at their earliest opportunity. See the [`BinaryFormatter` security guide](https://github.com/dotnet/docs/pull/19442) for more information.

__Why not make `BinaryFormatter` safe for untrusted payloads?__

The `BinaryFormatter` protocol works by specifying the values of an object's raw instance fields. In other words, the entire point of `BinaryFormatter` is to bypass an object's typical constructor and to use private reflection to set the instance fields to the contents that came in over the wire. Bypassing the constructor in this fashion means that the object cannot perform any validation or otherwise guarantee that its internal invariants are satisfied. One consequence of this is that `BinaryFormatter` is unsafe even for seemingly innocuous types such as `Exception` or `List<T>` or `Dictionary<TKey, TValue>`, regardless of the actual types of _T_, _TKey_, or _TValue_. Restricting deserialization to a list of allowed types will not resolve this issue.

This behavior is intrinsic to `BinaryFormatter` and cannot practically be addressed without changing both the serialized payload format itself and its supported capability set. And once we do that, what we have is essentially a brand new serializer incompatible with `BinaryFormatter`. This would not help existing consumers of `BinaryFormatter`.
