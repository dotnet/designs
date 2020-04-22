# Feature switch
The functionality available in the .NET libraries is getting bigger and wider with every release. Applications typically don't need or want all of it, which means when an app includes the libraries as part of it, app size is unnecessarily increased. With technologies like the linker, we have the ability to remove parts of the framework which the app doesn't use. These technologies have limitations based on existing public API patterns and code behavior, sometimes they can't determine what the app really needs. This proposal adds a way to explicitly disable functionality in the framework.

## Goals
* The ability for developers to declaratively remove functionality in the framework, 3rd party libraries or final app
* Consistent behavior across different build configurations - the app should get the exact same behavior whether the linker was used on it or not
* Ability to set different defaults based on the type of application (Blazor, Xamarin, …)


## Expected usage
### Size
By disabling functionality which is otherwise always available the linker can trim more code from the app reducing its size.
For example
* Support for compiled regular expressions. The constructor for regular expression [`Regex(string, RegexOptions)`](https://docs.microsoft.com/en-us/dotnet/api/system.text.regularexpressions.regex.-ctor?view=netcore-3.1#System_Text_RegularExpressions_Regex__ctor_System_String_System_Text_RegularExpressions_RegexOptions_) uses the second parameter to determine if it should pre-compile the regular expression or interpret it each time. The code to pre-compile the expression is relatively large and if the app doesn't use this feature it can be removed. But linker typically won't be able to determine that since it's a dynamic behavior. (This is already [conditionally enabled](https://github.com/dotnet/runtime/blob/a83598dd151628672b1d2ff72310cc8c2065a019/src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/Regex.cs#L77-L81) only when dynamic code compilation is supported by the runtime.)
* Security algorithms which are not in use by the app. For example [`AsymmetricalAlgorithm.Create(string)`](https://docs.microsoft.com/en-us/dotnet/api/system.security.cryptography.asymmetricalgorithm.create?view=netcore-3.1#System_Security_Cryptography_AsymmetricAlgorithm_Create_System_String_) can create a large list of algorithms (another factory API).

### Security
Disable functionality which is not recommended anymore for security reasons. The app may choose to disable such functionality and thus guarantee that nothing in the app can use it.
For example:
* `BinaryFormatter`
* Some cryptography algorithms which are not considered secure anymore. For example [`HashAlgorithm.Create(string)`](https://docs.microsoft.com/en-us/dotnet/api/system.security.cryptography.hashalgorithm.create?view=netcore-3.1#System_Security_Cryptography_HashAlgorithm_Create_System_String_) can create SHA1

### Linker friendliness
There are smaller features which are problematic from the linker perspective (they use reflection in a hard-to-analyze manner) and are only very rarely used. Disabling the feature would allow the linker to analyze the app without reporting warnings and decrease the application size.
For example:
* Disable the ability to read random types from resources (`ResourceManager`) - almost all apps only ever read strings from resources. In fact this functionality is already disabled when reading resources from a standalone files.


## Similar functionality
Runtime/libraries already contains several similar "feature switches". There are places where the code detects availability of some functionality - for example extended instruction sets. Based on the presence of the functionality the code has two branches, one using the new functionality to get better behavior and a fallback version. These are effectively also feature switches, but these are based on presence of either hardware or runtime functionality and not turned off explicitly.

An example of usage of these switches in the [`Vector` classes](https://github.com/dotnet/runtime/blob/8a83488465f101e3ec2620cb1eb2a8c3d7cc1a1c/src/libraries/System.Private.CoreLib/src/System/Runtime/Intrinsics/Vector128.cs#L399-L413).


## Existing functionality
### Linker can propagate constant values
Linker has an ability to propagate constant values throughout the code and based on them remove branches in the code which are not reachable. This is implemented in the [`RemoveUnreachableBlocksStep`](https://github.com/mono/linker/blob/master/src/linker/Linker.Steps/RemoveUnreachableBlocksStep.cs). This can be used for example to remove branches based on availability of hardware features (see the example above with `Vector`), but really any branch dependent on a value which can be determined to be constant can be trimmed with this.

### Linker can substitute method bodies
Linker has a feature where it can take an XML file which specifies methods (and fields) which should be replaced with a constant value (or throw). This can be used for example for the `GlobalizationMode.get_Invariant` to make it always return `true` on certain platforms. See [mono/linker#848](https://github.com/mono/linker/pull/848) for the initial change. The main implementation is in [`BodySubstituterStep`](https://github.com/mono/linker/blob/master/src/linker/Linker.Steps/BodySubstituterStep.cs).

When combined with the constant value propagation this can be used to remove "features" from the code based on input to the build.

### Runtime/libraries already have some feature switches
Globalization has a feature where it can always use only invariant culture and nothing else. For reference: [design doc](https://github.com/dotnet/runtime/blob/master/docs/design/features/globalization-invariant-mode.md) and the [base of the implementation](https://github.com/dotnet/runtime/blob/4f9ae42d861fcb4be2fcd5d3d55d5f227d30e723/src/coreclr/src/System.Private.CoreLib/src/System/Globalization/GlobalizationMode.cs). This has SDK integration to set it up in `.runtimeconfig.json` but it doesn't have linker integration.

There's also a discussion to setup the build of runtime/libraries to automatically trim based on target configuration - [here](https://github.com/dotnet/runtime/issues/31785). The intent of this change is not feature switches, but it's in the same area.

### Runtime/libraries already have compatibility switches
For example `CoreLib` already has several switches like `Switch.System.Globalization.FormatJapaneseFirstYearAsNumber`. These are typically small "tweaks" to the behavior of the runtime/libraries. They are internally exposed as static properties on [`LocalAppContextSwitches`](https://github.com/dotnet/runtime/blob/master/src/libraries/System.Private.CoreLib/src/System/LocalAppContextSwitches.cs) with the value coming from `.runtimeconfig.json` via [`AppContext.TryGetSwitch`](https://docs.microsoft.com/en-us/dotnet/api/system.appcontext.trygetswitch?view=netcore-3.1).


## Proposal
The purpose of the proposed changes is to introduce patterns and functionality to support them to make introduction of new feature switches easier and consistent.

### Unified pattern for defining switches and their properties
Each feature switch should have a read-only static property defined and all the decisions about the feature's availability should be based on a simple condition using such property. Depending on the impact of the feature switch the property may or may not be a public API.

Feature switches for large areas of functionality which may span multiple libraries should be exposed as public APIs to make it easy and consistent. For example if there would be a feature switch to disable support for HTTP2 there should be a public API property indicating the desired behavior. This is because it's likely that even libraries outside of core libraries may need to change their behavior based on the property. Having one property in this case would enable linker to trim branches across multiple libraries in a consistent way.

Feature switches for localized functionality which is not likely to span multiple libraries can use only internal properties.

It's up to each feature what is the behavior when it's turned off. In lot of cases the code would probably throw an exception, but in some cases it may be able to fallback to different implementation. For example if HTTP2 is disabled the code may be able to use HTTP1 transparently.

The property's value will be burned in by the linker when a corresponding feature switch is set at link time. The value to burn in is defined by an embedded `substitutions.xml` carried with the assembly that defines the property.

*Implementation note: The property which is used to determine if the feature is on or off should be written and used in such a way that tiering optimizations would be possible and JIT could trim away the "unused" code. Pattern which should achieve this in most cases is to get a read-only static bool property.*

### Unified pattern for feature definition in MSBuild
Introduce a standard pattern how to define these properties in the SDK. Basically a way to define a property and have it automatically passed through to `.runtimeconfig.json` as well as linker substitutions.

This requires a mapping between the MSBuild property name, the full name of the feature switch for runtime configuration (which would show up in `.runtimeconfig.json`) and the read-only static property in the managed code which is used to branch the behavior. The mappings will be defined by [`RuntimeHostConfigurationOptions`](https://github.com/dotnet/sdk/blob/36ef8b2aa8e5d579c921704bdab69a7407936889/src/Tasks/Microsoft.NET.Build.Tasks/targets/Microsoft.NET.Sdk.targets#L347) which includes an AppContext configuration name and its value based on the values of user-facing MSBuild properties, and by `substitutions.xml` files embedded in the assembly defining the features.

*Note that it seems likely that some of switches may require 1:many mapping because they're targeting existing code which uses multiple properties to determine the presence of the feature. The `substitutions.xml` will allow 1:many mappings if needed.*

Ultimately some of these switches may also need VS UI integration, but for now most of the feature switches should be perfectly fine with only MSBuild properties.

The ability to specify these from MSBuild projects as properties also means that different templates and SDKs can set different defaults - so for example Blazor or Xamarin SDKs may choose to turn off some of these features by default.

Example definition of a feature flag with SDK support in [`Microsoft.NET.Sdk.targets`](https://github.com/dotnet/sdk/blob/36ef8b2aa8e5d579c921704bdab69a7407936889/src/Tasks/Microsoft.NET.Build.Tasks/targets/Microsoft.NET.Sdk.targets) or by SDK components:
```xml
<ItemGroup>
    ...
    <RuntimeHostConfigurationOption Include="System.Runtime.OptionalFeatureBehavior"
                                    Condition="'$(OptionalUserFacingBehavior)' != ''"
                                    Value="$(OptionalUserFacingBehavior)" />
    ...
</ItemGroup>
```

The name of the property should be picked so that it's clear what `true`/`false` mean. In this case, the idea is that `false` means the feature will be disabled. Other cases (for example `InvariantGlobalization`) might have the opposite polarity.

### Generate the right input for the linker in SDK

All names/values from `RuntimeHostConfigurationOptions` will be passed to the ILLink task, which will apply any feature substitutions defined in [`substitutions.xml`](https://github.com/mono/linker/blob/master/src/linker/README.md#using-custom-substitutions). The substitutions file format will be extended to condition the substitutions based on the feature name/value. Any `RuntimeHostConfigurationOptions` which do not have feature implementations in `substitutions.xml` will not result in any modifications to the IL.

Example of a feature implementation:

```xml
<linker>
  <assembly fullname="System.Runtime.FeatureDefiningAssembly" feature="System.Runtime.OptionalFeatureBehavior" featurevalue="false">
    <type fullname="System.Runtime.FeatureDefiningType">
      <method signature="System.Boolean get_IsOptionalFeatureEnabled()" body="stub" value="false">
      </method>
    </type>
  </assembly>
</linker>
```

Example of setting a feature property from MSBuild.
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net5.0</TargetFramework>
    <!-- disable the OptionalUserFacingBehavior feature -->
    <OptionalUserFacingBehavior>false</OptionalUserFacingBehavior>
  </PropertyGroup>
</Project>
```

Similarly, SDK components could set default values for feature properties:
```xml
<Project>
  <!-- build logic for an SDK component -->
...
  <PropertyGroup>
    <OptionalUserFacingBehavior Condition="'$(OptionalUserFacingBehavior)' == ''">false</OptionalUserFacingBehavior>
  </PropertyGroup>
...
</Project>
```
SDK authors as always need to ensure that logic is imported in the correct order to allow users to override these default values, if allowed.

## Open questions

### Testing challenges
Having feature switches which are exposed as public knobs means that the number of valid and supported configurations of libraries grows in a significant way. Testing each feature switch in isolation is not that costly, but making sure there are no unintended interactions might be problematic.

### Stability
Using `AppContext.TryGetSwitch` as the underlying runtime store of these switches is consistent with the intent of that API (and the API has been present in the .NET libraries for a long time). Unfortunately there are no guarantees that the value will never change, in fact there's an API `AppContext.SetSwitch` which is designed to change the value at runtime. Fixing the values of these switches in the linker would make usage of the `SetSwitch` API ineffective and rather confusing.

*Note that the existing compatibility switches in `CoreLib` already have this problem and the current design seems to be to ignore it. That is the value is read from `AppContext` the first time it's needed and cached.*


## References

Very similar functionality has been proposed before in [dotnet/designs#42](https://github.com/dotnet/designs/pull/42). The main differences proposed here is to use "if/else" branches in the code and with IL properties to represent feature switches. The previous proposal used attributes on methods.


## Considered designs

### Boolean only or more complex values
Should the feature switches always represent simple on/off statements and thus be represented as boolean properties? In some cases the simple boolean may necessitate presence of multiple similar feature switches. For example the security algorithms case described above. In the extreme each such algorithm would have its own feature switch - this would obviously create confusion. We could potentially allow feature switches to have string values from a predefined set to solve this problem.

*For now we'll start with support for simple boolean and possibly also numeric values (enums). The design as proposed does not prevent any type of value to be used, it's just a matter of supporting it in the linker.*

### Which components own the mapping between different representations of feature switches
There are 3 different representations to each feature switch
* The MSBuild property - for example `OptionalUserFacingBehavior` - this is used by developers to turn the feature on/off.
* The runtime property in `.runtimeconfig.json` - for example `System.Runtime.OptionalFeatureBehavior` - this is used at runtime to alter the behavior of the code (via the IL property below)
* The IL property which is used in the code - for example `System.Runtime.FeatureDefiningType.get_IsOptionalFeatureEnabled, System.Runtime.FeatureDefiningAssembly` - this is used by the linker to fix the value of the property and propagate that to remove unused code.

The mapping between MSBuild property and runtime property pretty much has to be part of the SDK (in the MSBuild somewhere) as the SDK already generates the `.runtimeconfig.json` and this needs to be part of it. The second mapping to the IL property can take several options:
* Also in the SDK - becomes an input to the ILLink task
* Encoded as attributes in the BCL - we could add a new attribute to the IL properties to mark which runtime options they map to - linker can create the mapping from these attributes. In this case the ILLink task would take the runtime properties as input. For example:
```C#
internal static class FeatureDefiningType
{
    [FeatureSwitch("System.Runtime.OptionalFeatureBehavior")]
    internal static bool IsOptionalFeatureEnabled { get; }
}
```

* Inferred by the linker - in the extreme linker might be able to track the calls to `AppContext.TryGetSwitch` and perform enough data flow analysis to determine the value of the IL property from the runtime property value. In this case the ILLink task would also take runtime properties as input.
* Encoded in the `substitutions.xml` file (typically embedded in the assembly which it applies to).

*For now we decided to store these in the `substitutions.xml` as it allows for great flexibility. If this proves too complex for 3rd party feature switches we could add the attribute approach as an alternative.*