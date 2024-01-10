# Attribute-based model for feature switches

.NET has [feature switches](https://github.com/dotnet/designs/blob/main/accepted/2020/feature-switch.md) which can be set to turn on/off areas of functionality in our libraries, with optional support for removing unused features when trimming or native AOT compiling.

Feature switches suffer from a poor user experience:
- defining a feature switch with trimming support requires embedding an unintuitive XML file into the library, and
- there is no analyzer support for feature switches

This document proposes an attribute-based model for feature switches that will significantly improve the user experience, by removing the need for this XML and enabling analyzer support.

The attribute model is heavily inspired by the capability-based analyzer [draft](https://github.com/dotnet/designs/pull/261).

**Table of Contents**

- [Background](#background)
  - [Existing feature switch functionality](#existing-feature-switch-functionality)
  - [Terminology](#terminology)
  - [Warning behavior](#warning-behavior)
- [Goals](#goals)
  - [Non-goals](#non-goals)
  - [Use cases for feature guards](#use-cases-for-feature-guards)
  - [Use cases for feature switches](#use-cases-for-feature-switches)
- [Feature guard attribute](#feature-guard-attribute)
  - [Danger of incorrect usage](#danger-of-incorrect-usage)
  - [Validating correctness of feature guards](#validating-correctness-of-feature-guards)
  - [Feature guards and constant propagation](#feature-guards-and-constant-propagation)
- [Feature switch attributes](#feature-switch-attributes)
- [Relationship between feature switches and feature guards](#relationship-between-feature-switches-and-feature-guards)
  - [Feature switches are also guards](#feature-switches-are-also-guards)
  - [Feature guards may also be feature switches](#feature-guards-may-also-be-feature-switches)
  - [Referencing features from FeatureGuard and FeatureSwitch](#referencing-features-from-featureguard-and-featureswitch)
- [Unified view of features](#unified-view-of-features)
  - [Unified attribute model for feature switches and guards](#unified-attribute-model-for-feature-switches-and-guards)
- [Comparison with "capability-based analyzer"](#comparison-with-capability-based-analyzer)
- [Comparison with platform compatibility analyzer](#comparison-with-platform-compatibility-analyzer)
- [Namespace and visibility of feature switches](#namespace-and-visibility-of-feature-switches)
- [Possible future extensions](#possible-future-extensions)
  - [Feature switches with inverted polarity (false means supported/available)](#feature-switches-with-inverted-polarity-false-means-supportedavailable)
  - [Feature guards with inverted polarity](#feature-guards-with-inverted-polarity)
  - [Feature attributes with inverted polarity](#feature-attributes-with-inverted-polarity)
  - [Validation or generation of feature switch implementation](#validation-or-generation-of-feature-switch-implementation)
  - [Versioning support for feature attributes/checks/guards](#versioning-support-for-feature-attributeschecksguards)
  - [Feature attribute schemas](#feature-attribute-schemas)
- [Alternate API shapes](#alternate-api-shapes)
  - [Separate types for feature and Requires attribute](#separate-types-for-feature-and-requires-attribute)
  - [Feature switches without feature attributes](#feature-switches-without-feature-attributes)
  - [Generic attributes with interface constraint](#generic-attributes-with-interface-constraint)
- [Implementation notes](#implementation-notes)

## Background

### Existing feature switch functionality

What we describe overall as "feature switches" have many pieces which fit together to enable this:

- MSBuild property
- `RuntimeHostConfigurationOption` MSBuild item group
- `runtimeconfig.json` setting
- `AppContext` feature setting
- **ILLink.Substitutions.xml**
- **static Boolean property**
- **Requires attributes**

The bold pieces are the focus of this document. [Feature switches](https://github.com/dotnet/designs/blob/main/accepted/2020/feature-switch.md) describes how settings flow from the MSBuild property through the `AppContext` (for runtime feature checks) or `ILLink.Substitutions.xml` (for feature settings baked-in when trimming). This document aims to describe an attribute-based model to replace some of the functionality currently implemented via ILLink.Substitutions.xml, used for branch elimination in ILLink and ILCompiler to remove branches that call into `Requires`-annotated code when trimming.

### Terminology

We'll use the following terms to describe specific bits of functionality related to feature switches:
- Feature switch property: the IL property whose value indicates whether a feature is enabled/supported
  - For example: `RuntimeFeature.IsDynamicCodeSupported`
- Feature switch name: the string that identifies a feature in `RuntimeHostConfigurationOption` and AppContext
  - For example: `"System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported"`
- Feature attribute: an attribute associated with a feature, used to annotate code that is directly related to the feature.
  - For example: `RequiresDynamicCodeAttribute`
- Feature guard property: an IL property whose value _depends_ on a feature being enabled, but isn't necessarily _defined_ by the availability of that feature.
  - For example: `RuntimeFeature.IsDynamicCodeCompiled` depends on `IsDynamicCodeSupported`. It should return `false` when `IsDynamicCodeSupported` returns `false`, but it may return `false` even if `IsDynamicCodeSupported` is `true`.

We'll say that a "feature switch property" is also necessarily a "feature guard property" for its defining feature, but not all "feature guard properties" are "feature switch properties".

A "feature switch property" may also be a "feature guard property" for a feature _other than_ the defining feature. For example, we introduced the feature switch property `StartupHookProvider.IsSupported`, which is defined by the feature switch named `"System.StartupHookProvider.IsSupported"`, but additionally serves as a guard for code in the implementation that has `RequiresUnreferencedCodeAttribute`. Startup hook support is disabled whenever we trim out unreferenced code, but may also be disabled independently by the feature switch.

Similarly, one could imagine introducing a feature switch for `IsDynamicCodeCompiled`.

`IsDynamicCodeSupported` is an example of a feature that has an attribute, a feature switch, and a separate feature guard (`IsDynamicCodeCompiled`), but not all features have all of these bits of functionality.

### Warning behavior

Typically, feature switches come with support (via XML substitutions) for treating feature properties and feature guards as constants when publishing with ILLink/ILCompiler. These tools will eliminate guarded branches. This is useful as a code size optimization, and also as a way to prevent producing warnings for features that have attributes designed to produce warnings at callsites:

```csharp
UseDynamicCode(); // warns

if (RuntimeFeature.IsDynamicCodeSupported)
  UseDynamicCode(); // OK, no warning

if (RuntimeFeature.IsDynamicCodeCompiled)
  UseDynamicCode(); // OK, no warning in ILCompiler (for now this warns in the analyzer)

[RequiresDynamicCode("Uses dynamically generated code")]
static void UseDynamicCode() { }
```

The ILLink Roslyn analyzer has built-in support for treating `IsDynamicCodeSupported` as a guard for `RequiresDynamicCodeAttribute`, but has no other built-in support.

## Goals

- Allow libraries to define their own feature guard properties

  Libraries should be able to introduce their own properties that can act as guards for `RequiresDynamicCodeAttribute`, or for other features that might produce warnings in the analyzer

- Define an attribute-based model for such feature guards

- Take into account how this would interact with an attribute-based model for feature switches

  We will explore what an attribute-based model for feature switches would look like to ensure that it interacts well with a model for feature guards. It's possible that we would design both in conjunction if they are naturally related.

### Non-goals

- Support branch elimination in the analyzer for all feature switches

  The most important use case for the analyzer is analyzing libraries. Libraries typically don't bake in constants for feature switches, so the analyzer needs to consider all branches. It should support feature guards for features that produce warnings, but doesn't need to consider feature settings passed in from the project file to treat some branches as dead code.

- Teach the ILLink Roslyn Analyzer about the substitution XML

  We don't want to teach the analyzer to read the substitution XML. The analyzer is the first interaction that users typically have with trimming and AOT warnings. This should not be burdened by the XML format. Even if we did teach the analyzer about the XML, it would not solve the problem; for example, the analyzer must not globally assume that `IsDynamicCodeSupported` is false as ILCompiler does.

- Define a model with the full richness of the supported OS platform attributes

  We will focus initially on a model where feature switches are booleans that return `true` if a feature is enabled. We aren't considering supporting version checks, or feature switches of the opposite polarity (where `true` means a feature is disabled/unsupported). We will consider what this might look like just enough to gain confidence that our model could be extended to support these cases in the future, but won't design this fully in the first iteration.

- Define a model with substantially different semantics than the existing XML-based approach

  The XML substitutions have been successfully used to define feature switches in our libraries and third-party libraries. We want to ensure that a new attribute-based model can be a drop-in replacement for the relevant subset of substitution XMLs, but with a better user experience.

### Use cases for feature guards

Treat features that depend on the availability of dynamic code support as guards for `RequiresDynamicCodeSupportAttribute`:

- `RuntimeFeature.IsDynamicCodeCompiled`:
- `LambdaExpression.CanCompileToIL`
- `DelegateHelpers.CanEmitObjectArrayDelegate`
- `CallInstruction.CanCreateArbitraryDelegates`

Treat features which depend on the availability of unreferenced code as guards for `RequiresUnreferencedCodeAttribute`:

- `StartupHookProvider.IsSupported`
- `ResourceManager.AllowCustomResourceTypes`
- `DesigntimeLicenseContextSerializer.EnableUnsafeBinaryFormatterInDesigntimeLicenseContextSerialization`
- `Marshal.IsBuiltInComSupported`
- `InMemoryAssemblyLoader.IsSupported` (for C++/CLI support)
- `ComponentActivator.IsSupported`
- `JsonSerializer.IsReflectionEnabledByDefault`

### Use cases for feature switches

Most feature switches (those that are static Boolean properties) could be defined without XML substitutions:
- All of the feature switches mentioned in [Use cases for feature guards](#use-cases-for-feature-guards)
- Most of the features mentioned in https://github.com/dotnet/runtime/blob/main/docs/workflow/trimming/feature-switches.md
- Various features defined outside of dotnet/runtime. Some examples:
  - `ObjCRuntime.Runtime.IsManagedStaticRegistrar` in [xamarin-macios](https://github.com/xamarin/xamarin-macios/blob/885723b5313788bf645dd06a04b7ae3512b0a152/src/ILLink.Substitutions.ios.xml#L13)
  - `Android.Runtime.AndroidEnvironment.VSAndroidDesignerIsEnabled` in [xamarin-android](https://github.com/xamarin/xamarin-android/blob/c0aefeaaeef1acbbbbdf7ae589d15133cdc3064f/src/Mono.Android/ILLink/ILLink.Substitutions.xml#L4)
  - `ComputeSharp.Configuration.IsGpuTimeoutEnabled` in [ComputeSharp](https://github.com/Sergio0694/ComputeSharp/blob/45455abda911d8e73b92e9a17600f862eef8bf57/src/ComputeSharp/Properties/ILLink.Substitutions.xml#L14)
  - `PictureBox.UseWebRequest` in [winforms](https://github.com/dotnet/winforms/blob/85c155eef5de2dc0163a60147fa9bbc045323ef8/src/System.Windows.Forms/src/ILLink.Substitutions.xml#L5)
  - `DragDropExtensions.IsExternalDragAndDropSupported` in [uno](https://github.com/unoplatform/uno/blob/5e3a9e6785cc3550d09ec4cf5f3dc63bc93eeaf7/src/Uno.UI/LinkerSubstitution.Wasm.xml#L5)

## Feature guard attribute

In order to treat a property as a guard for a feature that has a `Requires` attribute, there must be a semantic tie between the guard property and the attribute. ILLink and ILCompiler don't have this requirement because they run on apps, not libraries, so the desired warning behavior just falls out, thanks to substitution XML, branch elimination and the fact that `IsDynamicCodeSupported` is set to false from MSBuild.

We could allow placing `FeatureGuardAttribute` on the property to indicate that it should act as a guard for a particular feature. The intention with any of these approaches is for the guard to prevent analyzer warnings:

```csharp
if (Feature.IsSupported) {
    APIWhichRequiresDynamicCode(); // No warnings
}

[RequiresDynamicCode("Does something with dynamic codegen")]
static void APIWhichRequiresDynamicCode() {
    // ...
}
```

The attribute instance needs to reference the feature somehow, whether as:

- a reference to the feature attribute:

  ```csharp
  class Feature {
      [FeatureGuard(typeof(RequiresDynamicCodeAttribute))]
      public static bool IsSupported => RuntimeFeature.IsDynamicCodeSupported;
  }
  ```

  This tells the analyzer enough that it can treat this as a guard without any extra information.

  The analyzer wouldn't know about the relationship between this check and `RuntimeFeature.IsDynamicCodeSupported` or `"System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported"`, but encoding this relationship isn't strictly necessary if we are only interested in representing feature _guards_ via attributes.

  On its own this is not enough for ILLink/ILCompiler to do branch elimination, because there's no tie to the feature switch name.

- a reference to the feature name string:

  ```csharp
  class Feature {
      [FeatureGuard("System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported")]
      public static bool IsSupported => RuntimeFeature.IsDynamicCodeSupported;
  }
  ```

  The analyzer would need to hard-code the fact that this string corresponds to `RequiresDynamicCodeAttribute`, unless we model this relationship via attributes that represent feature switches.

  This would be sufficient for ILLink/ILCompiler to treat `IsSupported` as a constant based on the feature switch name.

- a reference to the existing feature check property:

  ```csharp
  class Feature {
      [FeatureGuard(typeof(RuntimeFeature), nameof(RuntimeFeature.IsDynamicCodeSupported))]
      public static bool IsSupported => RuntimeFeature.IsDynamicCodeSupported;
  }
  ```

  The analyzer would need to hard-code the fact that `RuntimeFeature.IsDynamicCodeSupported` corresponds to `RequiresDynamicCodeAttribute`, unless we model this relationship via attributes that represent feature switches.

  This would be sufficient for ILLink/ILCompiler to treat `IsSupported` as a constant based on the feature switch name, assuming it has existing knowledge of the fact that `RuntimeFeature.IsDynamicCodeSupported` is controlled by the feature switch named `"System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported"`, either from a substitution XML or from a separate attribute that encodes this.

### Danger of incorrect usage

Since a feature guard silences warnings from the analyzer, there is some danger that `FeatureGuardAttribute` will be used carelessly as a way to silence warnings, even when the definition of the `IsSupported` property doesn't have any tie to the existing feature. For example:

```csharp
class Feature {
    [FeatureGuard(typeof(RequiresDynamicCodeAttribute))]
    public static bool SilenceWarnings => true; // BAD
}
```

```csharp
if (Feature.SilenceWarnings) {
    APIWhichRequiresDynamicCode(); // No warnings
}
```

This will silence warnings from the analyzer without any indication that `RequiresDynamicCode`-annotated code might be reached at runtime. We should be extremely clear in our documentation that this is not the intended use.

### Validating correctness of feature guards

Better would be for the analyzer to validate that the implementation of a feature check includes a check for `IsDynamicCodeSupported`. This could be done using the analyzer infrastructure we have in place without much cost for simple cases:

```csharp
class Feature {
    [FeatureGuard(typeof(RequiresDynamicCodeAttribute))]
    public static bool IsSupported => RuntimeFeature.IsDynamicCodeSupported && SomeOtherCondition(); // OK

    static bool SomeOtherCondition() => // ...
}
```

```csharp
class Feature {
    [FeatureGuard(typeof(RequiresDynamicCodeAttribute))]
    public static bool IsSupported => SomeOtherCondition(); // warning

    static bool SomeOtherCondition() => // ...
}
```

Note that this analysis does require the analyzer to understand the relationship between `RequiresDynamicCodeAttribute` and `RuntimeFeature.IsDynamicCodeSupported`, so it would still require either hard-coding this relationship in the analyzer, or representing it via an attribute model for feature switches.

There may be more complex implementations of feature guards that the analyzer would not support. In this case, the analyzer would produce a warning on the property definition that can be silenced if the author is confident that the return value of the property reflects the referenced feature.

We may also want to add support for such validation in ILLink and ILCompiler, though this may be slightly costlier than adding support in the analyzer.

### Feature guards and constant propagation

ILLink performs interprocedural constant propagation, but ILCompiler does not. To treat a property as a feature guard, ILCompiler currently requires substitution XML to eliminate guarded branches. A natural use of the attribute-based feature guards is to influence ILCompiler's constant propagation, replacing the use of substitution XML for properties that are feature guards only.

A separate concept is still required to replace the substitution XML for properties that are feature switches in addition to being feature guards.

## Feature switch attributes

Allow placing `FeatureSwitchAttribute` on the property to indicate that it should be treated as a constant if the feature setting is passed to ILLink/ILCompiler at publish time. The Roslyn analyzer could also respect it by not analyzing branches that are unreachable with the feature setting, though we don't have an immediate use case for this. It could be useful when analyzing application code, but the analyzer is most important for libraries, where feature switches are usually not set.

The feature switch property would be usable as a guard for calls to `RequiresDynamicCode` APIs:

```csharp
if (RuntimeFeature.IsDynamicCodeSupported) {
    APIWhichRequiresDynamicCode(); // No warnings
}

[RequiresDynamicCode("Does something with dynamic codegen")]
static void APIWhichRequiresDynamicCode() {
    // ...
}
```

The attribute needs to reference the feature somehow, whether as:

- a reference to the feature name string:

  ```csharp
  class RuntimeFeature {
      [FeatureSwitch("RuntimeFeature.IsDynamicCodeSupported")]
      public static bool IsDynamicCodeSupported => AppContext.TryGetSwitch("RuntimeFeature.IsDynamicCodeSupported", out bool isEnabled) ? isEnabled : false;
  }
  ```

  Since we set `"RuntimeFeature.IsDynamicCodeSupported"` to `false` when running ILCompiler, this is enough for ILCompiler to use it for branch elimination and avoid warning for guarded calls to `RequiresDynamicCodeAttribute`. ILLink would behave similarly if there were a feature switch for `RequiresUnreferencedCodeAttribute`.

  The analyzer would still need to separately encode the fact that `"RuntimeFeature.IsDynamicCodeSupported"` corresponds to `RequiresDynamicCodeAttribute`.

- a reference to the feature attribute, for those feature switches that are associated with attributes:

  ```csharp
  class RuntimeFeature {
      [FeatureSwitch(typeof(RequiresDynamicCodeAttribute))]
      public static bool IsDynamicCodeSupported => AppContext.TryGetSwitch("RuntimteFeature.IsDynamicCodeSupported", out bool isEnabled) ? isEnabled : true;
  }
  ```

  In this case, ILCompiler would need to hard-code the fact that `RequiresDynamicCodeAttribute` corresponds to `"RuntimeFeature.IsDynamicCodeSupported"`, and use this knowledge to treat the property as returning a constant.

  However, the Roslyn analyzer would have enough information from this attribute alone.

## Relationship between feature switches and feature guards

### Feature switches are also guards
A feature switch is also a feature guard for the feature it is defined by. We could encode this in one of two ways:
- `FeatureSwitch` only, with a unified representation that includes the mapping to both the attribute and feature name, or
- Require both `FeatureSwitch` and `FeatureGuard`, where both attributes together provide a mapping to the attribute and the feature name

### Feature guards _may_ also be feature switches

A feature guard might also come with its own independent feature switch. We should be careful to avoid violating the assumptions of the feature guard by controlling the property via a feature switch.

For example, if we had a separate feature switch for `IsDynamicCodeCompiled`, this would allow setting `IsDynamicCodeCompiled` to `true` even when `IsDynamicCodeSupported` is `false`.

This is essentially what we did for features like `StartupHookSupport`, which can be set even in trimmed apps:

```csharp
class StartupHookProvider
{
    [FeatureSwitch(typeof(RequiresStartupHookSupport))]
    [FeatureGuard(typeof(RequiresUnreferencedCodeSupport))]
    private static bool IsSupported => AppContext.TryGetSwitch("System.StartupHookProvider.IsSupported", out bool isSupported) ? isSupported : true;

    private static void ProcessStartupHooks()
    {
        if (!IsSupported)
            return;

        var startupHooks = // parse startup hooks...

        for (int i = 0; i < startupHooks.Count; i++)
            CallStartupHook(startupHooks[i]);
    }

    [RequiresUnreferencedCode("The StartupHookSupport feature switch has been enabled for this app which is being trimmed. " +
            "Startup hook code is not observable by the trimmer and so required assemblies, types and members may be removed")]
    private static void CallStartupHook(StartupHookNameOrPath startupHook) {
        // ...
    }
}

[FeatureName("System.StartupHookProvider.IsSupported")]
class RequiresStartupHookSupport : RequiresFeatureAttribute {}
```

In this example, `FeatureGuard` would prevent analyzer warnings at the `CallStartupHook` callsite, due to the `IsSupported` check earlier in the method. The default settings for ILCompiler and ILLink ensure the same by setting `"System.StartupHookProvider.IsSupported"` to `false` in trimmed apps from MSBuild.

However, it is possible to bypass the defaults and set `StartupHookSupport` to `true` even in a trimmed app. We rely on trim warnings to alert the app author to the proble in this case.

If we have an attribute-based model for feature guards, we may want to consider inferring these defaults from the `FeatureGuard` instead (so a guard for `RequiresUnreferencedCode` that is also a feature switch would be `false` by default whenever "unreferenced code" is unavailable). In the above example, this would mean that ILLink and ILCompiler could treat `System.StartupHook.Provider.IsSupported` as `false` by default without any MSBuild logic.

The proposal for now is not to infer any defaults and just take care to set appropriate defaults in the SDK. This means that custom feature guards that are also feature switches will still need to do the same. For example, a library that defines a feature switch which guards `RequiresUnreferencedCode` will need to ship with MSBuild targets that disable the feature by default for trimmed apps.

### Referencing features from `FeatureGuard` and `FeatureSwitch`

We saw a few cases that required a link between feature guards and the functionality related to feature switches:
- To support feature guards in the analyzer, there must be a tie to the guarded `Requires` attribute
- To support eliminating branches guarded by feature guards in ILLink/ILCompiler, there must be a tie to the name of the feature setting.
- To support detecting incorrect implementations of the feature guard property in the analyzer, there must be a tie to the feature switch property of the guarded feature.

| How `FeatureGuard` references the guarded feature | Analyzer | ILLink/ILCompiler |
| - | - | - |
| Attribute | OK; needs mapping to feature name/property for validation | needs mapping to feature name |
| Feature switch name | needs mapping to attribute | OK |
| Feature switch property | needs mapping to attribute | needs mapping to feature name |

| How `FeatureSwitch` references the defining feature | Analyzer | ILLink/ILCompiler |
| - | - | - |
| Feature switch name | needs mapping to attribute | OK |
| Feature attribute | OK | needs mapping to feature name |


It seems natural to define a model where all three of these represent the same concept.

## Unified view of features

We take the view that `RequiresDynamicCodeAttribute`, `RuntimeFeature.IsDynamicCodeSupported`, and `"System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported"` all conceptually represent the same "feature".

- The "feature" of dynamic code support is represented as:
  - `RequiresDynamicCodeAttribute`
  - `RuntimeFeature.IsDynamicCodeSupported` property
  - `"System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported"`

Currently, "dynamic code" is the main example of a feature that defines all three of these components.

Not all features come with a feature switch. For example:

- The "feature" of unreferenced code being available is represented as:
  - `RequiresUnreferencedCodeAttribute`

It's easy to imagine adding a feature check property like `RuntimeFeature.IsUnreferencedCodeSupported` that would be set to `false` in trimmed apps. We don't necessarily want to do this, because there is value in organizing features that call into `RequiresUnreferencedCode` APIs in a more granular way, so that they can be toggled independently.

- The "feature" of individual assembly files being available in an app is represented as:
  - `RequiresAssemblyFilesAttribute`

Similarly, not all features come with an attribute. Some features don't tie into functionality which is designed to produce warnings if called. For example:

- The "feature" of verifying that open generics are dynamically instantiated with trim-compatible arguments is represented as:
  - `VerifyOpenGenericServiceTrimmability` property
  - `"Microsoft.Extensions.DependencyInjection.VerifyOpenGenericServiceTrimmability"`

- The "feature" of supporting or not supporting globalization is represented as:
  - `GlobalizationMode.Invariant` property
  - `"System.Globalization.Invariant"`

No warnings are produced just because these features are enabled/disabled. Instead, they are designed to be used to change the behavior, and possibly remove unneccessary code when publishing. It's easy to imagine adding an attribute like `RequiresCultureData` that would be used to annotate APIs that rely on globalization support, such that analysis warnings would prevent accidentally pulling in the globalization stack from code that is supposed to be using invariant globalization. We don't want to do this for every feature; typically we only do this for features that represent large cross-cutting concerns, and are not available with certain app models (trimming, AOT compilation, single-file publishing).

However, any attribute-based model that we pick to represent features should be able to tie in with all three representations.

### Unified attribute model for feature switches and guards

We would like for the attribute model to have a consistent way to refer to a feature in `FeatureGuardAttribute` or `FeatureSwitchAttribute`. The proposal is to support an attribute model for features that have an associated `Requires` attribute, and use that attribute type uniformly to refer to the feature from `FeatureGuardAttribute` and `FeatureSwitchAttribute`.

For example, the feature switch for "dynamic code support" might look like this:

```csharp
public class RuntimeFeature
{
    [FeatureSwitch(typeof(RequiresDynamicCodeAttribute))]
    public static bool IsDynamicCodeSupported => // ...
}

[FeatureName("System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported")]
class RequiresDynamicCodeAttribute : RequiresFeatureAttribute { }
```

and a feature guard for "dynamic code compilation" might look like this:
```csharp
public class RuntimeFeature
{
    [FeatureGuard(typeof(RequiresDynamicCodeAttribute))]
    public static bool IsDynamicCodeCompiled => // ...
}
```

The attribute definitions to support this might look like this:
```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
abstract class RequiresFeatureAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property, Inherited = false)]
class FeatureSwitchAttribute : Attribute {
    public Type FeatureAttributeType { get; }

    public FeatureSwitchAttribute(Type featureAttributeType) {
        FeatureAttributeType = featureAttributeType;
    }
}

[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = true)]
class FeatureGuardAttribute : Attribute
{
    public Type FeatureAttributeType { get; }

    public FeatureGuardAttribute(Type featureAttributeType) {
        FeatureAttributeType = featureAttributeType;
    }
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
class FeatureNameAttribute : Attribute
{
    public string Name { get; }

    public FeatureNameAttribute(string name) {
        Name = name;
    }
}
```

The analyzer would validate that `FeatureGuardAttribute` and `FeatureSwitchAttribute` attribute arguments are derived classes of `FeatureAttribute`, and that `FeatureNameAttribute` is only placed on derived classes of `FeatureAttribute`.

We could use this model even for feature switches similar to `StartupHookSupport`, which don't currently have a `StartupHookSupportAttribute`. There's no need to actually annotate related APIs with the attribute if that is overkill for a small feature. In this case the attribute definition would just serve as metadata that allows us to define a feature switch in a uniform way.

Note that this makes it possible to define custom `Requires` attributes for arbitrary features. While the immediate goal of this proposal is not to enable analyzer support for analysis warnings based on such custom attributes, the model intentionally allows for this so that we could easily open up the analyzer to work for custom features. However, initially support for analysis warnings would be limited to `RequiresUnreferencedCodeAttribute`, `RequiresDynamicCodeAttribute`, and `RequiresAssemblyFilesAttribute`. Analyzers currently require all supported diagnostic IDs to be declared up-front, so allowing analysis for arbitrary features would mean that all features share the same analyzer diagnostic ID. We would need to consider solutions to this problem.

## Comparison with "capability-based analyzer"

This is fundamentally the same idea outlined in https://github.com/dotnet/designs/pull/261. The main difference is that this document approaches the idea specifically from the point of view of trimming support. The hope is that this document provides the motivation for using a unified representation for the attributes that are related to feature switches and trim/AOT analysis.

- Terminology: "feature" vs "capability"

  We use "feature" instead of "capability" because "capability" suggests a quality of the underlying platform or runtime, while "feature" seems slightly more general, and can refer to settings toggled in the application config. This lines up with what we have been describing as "feature switches", and with the naming of `RuntimeFeature.IsDynamicCodeSupported` and similar APIs.

- Terminology: "check" vs "switch"

  We use `FeatureSwitch` in essentially the same way that the capability API draft uses `CapabilityCheck`. Maybe `FeatureCheck` would be a better name for the attribute, since the "switch" is really the MSBuild property that can be toggled, while the attribute should indicate that the property is a check for that setting.

- Representing `RequiresDynamicCodeAttribute` as a feature/capability

  The capability API draft mentions the possibility of using a capability model for `RequiresDynamicCodeAttribute`, just as we suggest here. This is the same idea.

- Tie-in to feature switches

  The capability API draft doesn't include a tie-in to the feature names.

- API shape: target of `CapabilityCheck` vs `FeatureSwitch`

  The capability API draft places `CapabilityCheck` on the capability attribute definition, for example:

  ```csharp
  [CapabilityCheck(typeof(RuntimeFeature), nameof(RuntimeFeature.IsDynamicCodeSupported))]
  public sealed class RequiresDynamicCodeAttribute : CapabilityAttribute
  {
      public RequiresDynamicCodeAttribute();
  }
  ```

  We instead suggest placing the equivalent `FeatureSwitch` attribute on the property definition:

  ```csharp
  public sealed class RuntimeFeature
  {
      [FeatureSwitch(typeof(RequiresDynamicCodeAttribute))]
      public bool IsDynamicCodeSupported => // ...
  }
  ```

  Both models allow multiple "feature switch" properties to be defined by the same feature, but the latter can prevent the same property from being marked as the "feature check" for multiple different features that could potentially conflict, if we set `AllowMultiple = false` on `FeatureSwitchAttribute`.

- API shape: target of `CapabilityGuard` vs `FeatureGuard`

  These are the same idea, and are represented very similarly in both models, targeting the guard property, with `AllowMultiple = true` to support a property that guards multiple features. The capability API draft lists the attribute targets as `AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field`. We might want to start with just `AttributeTargets.Property`.

## Comparison with platform compatibility analyzer

The platform compatibility analyzer is semantically very similar to the behavior described here, except that it doesn't come with ILLink/ILCompiler support for removing branches that are unreachable when publishing for a given platform.

"Platforms" (instead of "features") are represented as strings, with optional versions.

- `SupportedOSPlatformAttribute` is similar to the `RequiresDynamicCodeAttribute`, etc, and will produce warnings if called in an unguarded context.

  ```csharp
  CallSomeAndroidAPI(); // warns if not targeting android

  [SupportedOSPlatform("android")]
  static void CallSomeAndroidAPI() {
    // ...
  }
  ```

- Platform checks are like feature switch properties, and can guard calls to annotated APIs:

  ```csharp
  if (OperatingSystem.IsAndroid())
    CallSomeAndroidAPI(); // no warning for guarded call
  ```

  The analyzer has built-in knowledge of fact that `IsAndroid` corresponds to the `SupportedOSPlatform("android")`. This is similar to the ILLink analyzer's current hard-coded knowledge of the fact that `IsDynamicCodeSupported` corresponds to `RequiresDynamicCodeAttribute`.

- Platform guards are like feature guards, allowing libraries to introduce custom guards for existing platforms:

  ```csharp
  class Feature {
      [SupportedOSPlatformGuard("android")]
      public static bool IsSupported => SomeCondition() && OperatingSystem.IsAndroid();
  }
  ```

  ```csharp
  if (Feature.IsSupported)
    CallSomeAndroidAPI(); // no warning for guarded call
  ```

The platform compatibility analyzer also has some additional functionality, such as annotating _unsupported_ APIs, and including version numbers.

## Namespace and visibility of feature switches

As defined in the substitution XML, the feature switch names are all public and inhabit a shared global namespace. The IL properties for feature switches and guards can of course be private, since they follow IL visibility rules. This means that there can be private feature switches which are toggled by "public" feature names passed in from MSBuild, allowing these settings to change implementation details of a library.

Another consequence is that any library can define a feature switch that is controlled by any feature name. For example, the private feature switch property `StartupHookProvider.IsSupported` is controlled by the "public" feature name `"System.StartupHookProvider.IsSupported"`, and there is nothing preventing another library from defining its own feature switch property that is also controlled by `"System.StartupHookProvider.IsSupported"`.

Using types to represent features allows the use of access restrictions to signal the intended use of a feature. For example, we might have an internal type representing startup hook support:

```csharp
[FeatureName("System.StartupHookProvider.IsSupported")]
class RequiresStartupHookSupportAttribute : RequiresFeatureAttribute {}

class StartupHookProvider
{
    [FeatureSwitch(typeof(RequiresStartupHookSupportAttribute))]
    private static bool IsSupported => // ...
}
```

This allows the library that defines the feature `RequiresStartupHookSupportAttribute` to also define a `FeatureSwitch`, but prevents other libraries from defining a feature switch or guard property that references the same attribute.

However, it would still be possible to get around this restriction by defining a new attribute type that uses the same feature name:

```csharp
[FeatureName("System.StartupHookProvider.IsSupported")]
class MyLibraryStartupHookSupportAttribute : RequiresFeatureAttribute {} // BAD

class Library
{
    [FeatureSwitch(typeof(MyLibraryStartupHookSupportAttribute))]
    public static bool IsSupported => // ...
}
```

The extra steps required arguably make it clear that this is violating the intentions of the original definition of `RequiresStartupHookSupportAttribute`.

This could also happen accidentally as the result of a name clash. To mitigate this we generally recommend that feature names resemble a fully qualified member name, including namespace. We could consider adding some enforcement of this, or make feature names local to an assembly and require the MSBuild settings to be assembly-qualified feature names. However, we consider this orthogonal to the current proposal (it would apply equally to the XML-based feature switch support), and make no attempt to change the visibility properties of the current model, except to suggest that a typed representation of features can help signal the intended visibility.

## Possible future extensions

We might eventually want to extend the semantics in a few directions:

### Feature switches with inverted polarity (`false` means supported/available)

  `GlobalizationMode.Invariant` is an example of this. `true` means that globalization support is not available.

  This could be done by adding an extra boolean argument to the feature switch attribute constructor:

  ```csharp
  class GlobalizationMode {
      [FeatureSwitch("Globalization.Invariant", negativeCheck: true)]
      public static bool InvariantGlobalization => AppContext.TryGetSwitch("Globalization.Invariant", out bool value) ? value : false;
  }
  ```

  ```csharp
  if (GlobaliazationMode.Invariant) {
      UseInvariantGlobalization();
  } else {
      UseGlobalization(); // no warning
  }

  [RequiresGlobalizationSupport]
  static void UseGlobalization() { }
  ```

### Feature guards with inverted polarity

  This could work similarly to feature switches:

  ```csharp
  class Feature {
      [FeatureGuard("RuntimeFeature.IsDynamicCodeSupported", negativeCheck: true)]
      public bool IsDynamicCodeUnsupported => !RuntimeFeature.IsDynamicCodeSupported;
  }
  ```

### Feature attributes with inverted polarity

  It would be possible to define an attribute that indicates _lack_ of support for a feature, similar to the `UnsupportedOSPlatformAttribute`. The attribute-based model should make it possible to differentiate these from the `Requires` attributes, for example with a different base class.

  It's not clear whether we have a use case for such an attribute, so these examples aren't meant to suggest realistic names, but just the semantics:

  ```csharp
  class RequiresNotAttribute : Attribute {}

  class RequiresNoDynamicCodeAttribute : RequiresNotAttribute {}
  ```

### Validation or generation of feature switch implementation

  The recommended pattern for implementing feature switches is to check `AppContext`, for example:
  
  ```csharp
  [FeatureSwitch(typeof(RequiresDynamicCodeAttribute))]
  static bool IsDynamicCodeSupported => AppContext.TryGetSwitch("RuntimeFeature.IsDynamicCodeSupported", out bool isEnabled) ? isEnabled : false;
  ```

  We could consider adding validation that the body correctly checks `AppContext` for the feature name associated with the feature attribute, or adding a source generator that would generate the implementation from the `FeatureSwitchAttribute`.

### Versioning support for feature attributes/checks/guards

  The model here would extend naturally to include support for version checks the same way that the platform compatibility analyzer does. Versions would likely be represented as strings because they are encodable in custom attributes:

  ```csharp
  class RequiresWithVersionAttribute : Attribute {
      public RequiresWithVersionAttribute(string version) {}
  }

  class RequiresFooVersionAttribute : RequiresWithVersionAttribute {
      public RequiresFooVersionAttribute(string version) : base(version) {}
  }

  class Foo {
      [FeatureSwitch(typeof(RequiresFooVersion))]
      public static bool IsSupportedWithVersionAtLeast(string version) => return VersionIsLessThanOrEquals(version, "2.0");

      [RequiresFooVersion("2.0")]
      public static void Impl_2_0() {
          // Do some work
      }

      [RequiresFooVersion("1.0")]
      public static void Impl_1_0() {
        // Breaking change was made in version 2.0, where this API is no longer supported.
          throw new NotSupportedException();
      }
  }
  ```

  Code that was originally built against the 1.0 version, and broken on the upgrade to the 2.0 version, could then be updated with a feature check like this:
  ```csharp
  if (Foo.IsSupportedWithVersionAtLeast("2.0")) {
      Foo.Impl_2_0();
  } else {
      Foo.Impl_1_0();
  }
  ```

  Although it's not clear in practice where this would be useful. This is not meant as a realistic example.

  The platform compatibility analyzer represents version ranges via a combination of attributes as described in [advanced scenarios for attribute combinations](https://learn.microsoft.com/dotnet/standard/analyzers/platform-compat-analyzer#advanced-scenarios-for-attribute-combinations) (in addition to representing combinations of support or lack of support for various platforms). This can encode a supported or unsupported version range for a given platform, which might alternately be encoded by a single attribute that takes starting and ending versions for support. In any case, the model seems neatly extensible to version numbers should we need them.

### Feature attribute schemas

  We could consider unifying this model with the platform compatibility analyzer. One difference is that the `SupportedOSPlatformAttribute` takes a string indicating the platform name. We would likely need to extend the understanding of feature attributes to support treating "features" differently based on this string, effectively supporting feature attributes which define not a single feature, but a schema that allows representing a class of features. For example:

  ```csharp
  class NamedFeatureAttribute : RequiresFeatureAttribute {
      public string FeatureName { get; }

      public NamedFeatureAttribute(string name) => FeatureName = name;
  }

  [AttributeUsage(AttributeTargets.Property, Inherited = false)]
  class FeatureSwitchAttribute : Attribute {
      public Type FeatureAttributeType { get; }

      public string FeatureName { get; }

      public FeatureSwitchAttribute(Type featureAttributeType) {
          FeatureAttributeType = featureAttributeType;
      }
  }
  ```

  These could be support defining arbitrary named features, where the platform analyzer attributes are special cases:

  ```csharp
  class OSPlatformAttribute : NamedFeatureAttribute {
      private protected OSPlatformAttribute(string platformName) : base(platformName)
      {
          PlatformName = platformName;
      }

      public string PlatformName { get; }
  }
  ```

  And this might be used as follows to define a feature switch:

  ```csharp
  [FeatureSwitch(typeof(SupportedOSPlatform), FeatureName = "ios")]
  static bool IsIOS => // ...

  [SupportedOSPlatform("ios")]
  static void ApiOnlyAvailableOnIOS() {
      // ...
  }
  ```

  In this example, `FeatureName` indicates that the instances of `SupportedOSPlatformAttribute` should be differentiated based on the value of this parameter.

## Alternate API shapes

### Separate types for feature and Requires attribute

Since not all feature switches are designed to support annotating code with a `RequiresFeatureAttribute`, we could add a level of indirection to separate the definition of a feature from the attribute definition. For example, to support expressing `"System.StartupHookProvider.IsSupported"` as a feature switch without requiring a `RequiresStartupHookSupportAttribute`, we could define a separate type that just acts as metadata representing the feature:

```csharp
[FeatureName("System.StartupHookProvider.IsSupported")]
static class StartupHookSupported { }

class StartupHookProvider {
    [FeatureSwitch(typeof(StartupHookSupported))]
    [FeatureGuard(typeof(RequiresUnreferencedCodeAttribute))]
    private static bool IsSupported => // ...
}
```

For features that do define an analysis attribute, this could be linked to the feature defining type with another attribute:

```csharp
[FeatureName("System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported")]
[FeatureRequirement(typeof(RequiresDynamicCode))]
static class DynamicCodeSupportedFeature { }

class RequiresDynamicCodeAttribute : Attribute { }

class RuntimeFeature {
    [FeatureSwitch(typeof(DynamicCodeSupportedFeature))]
    public static bool IsDynamicCodeSupported => // ...
}
```

The advantage of this model is that it doesn't require defining an attribute type, and would allow the name of the type referenced in the `FeatureSwitch` and `FeatureGuard` attributes to be more aligned with the feature name instead of the `Requires` attribute naming convention. However, for the features which do have attributes it is another level of indirection that might not be necessary. Perhaps another option is to allow the referenced type to _optionally_ derive from `RequiresFeatureAttribute`. This would allow the definition of a feature without an attribute type, but would still require giving thought to the name of the feature defining type, in case it was later changed to derive from `RequiresFeatureAttribute` and used for analysis.

### Feature switches without feature attributes

The proposed model for feature attributes requires introducing a separate attribute type for each feature switch. An alternative is to uniformly use the feature name for both `FeatureSwitch` and `FeatureGuard`. For example:

```csharp
class RuntimeFeature {
    [FeatureSwitch("System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported")]
    public static bool IsDynamicCodeSupported => // ...

    [FeatureGuard("System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported")]
    public static bool IsDynamicCodeCompiled => // ...
}
```

The link between the feature name and `RequiresDynamicCodeAttribute` would be created on the `Requires` attribute definition, for example via another attribute:

```csharp
[FeatureName("System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported")]
class RequiresDynamicCodeAttribute : RequiresFeatureAttribute { }
```

The analyzer would then discover the relationship between the feature name and the `Requires` attribute if it sees a call to a method annotated with `RequiresDynamicCodeAttribute`. This model has the advantage that it doesn't require defining an attribute type for features that don't need one. It also would allow us to define a [feature attribute schema](#feature-attribute-schemas) with a uniform appearance that is more strongly analogous to preprocessor symbols:
```csharp
class Feature {
    [FeatureSwitch("MY_LIBRARY_FEATURE")]
    public static bool IsSupported => // ...

    [IfDefined("MY_LIBRARY_FEATURE")]
    public static void DoSomething() {
        // ...
    }
}

class Consumer {
    static void Main() {
        if (Feature.IsSupported)
          Feature.DoSomething();
    }
}
```

Here `IfDefinedAttribute` plays the same role that `RequiresDynamicCodeAttribute` plays for `"RuntimeFeature.IsDynamicCodeSupported"`, but for the feature `"MY_LIBRARY_FEATURE"`. `IfDefinedAttribute` is meant to illustrate the analogy with preprocessor symbols (and is not a proposed attribute name):

```csharp
class Feature {
#if MY_LIBRARY_FEATURE
    public static void DoSomething() {
        // ...
    }
#endif
}

class Consumer {
    static void Main() {
#if MY_LIBRARY_FEATURE
        Feature.DoSomething();
#endif
    }
}
```

If we went this route, we might define `RequiresFeatureAttribute` (instead of `IfDefinedAttribute`) that takes a string argument, and make the existing `Requires` attributes inherit from it to indicate the feature name, instead of using `FeatureNameAttribute`:

```csharp
class RequiresFeatureAttribute : Attribute {
    public string FeatureName { get; }

    public RequiresFeatureAttribute(string featureName) => FeatureName = featureName;
}

class RequiresDynamicCodeAttribute : RequiresFeatureAttribute {
    public RequiresDynamicCodeAttribute(string message)
      : base("System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported")
    {
        // ...
    }
}
```

A potential disadvantage is that this model makes it slightly easier for a library to define a feature switch/guard property for an existing feature name, regardless of the intended visibility of the original feature switch definition; however, this is no worse than the model we have today with substitution XML. See [namespace and visibility of feature switches](#namespace-and-visibility-of-feature-switches) for further discussion.

This model would also require us to define a feature name for `RequiresUnreferencedCodeAttribute`.

### Generic attributes with interface constraint

An earlier version of this proposal had the following API shape:

```csharp
class RuntimeFeatures {
    [FeatureSwitch<RequiresDynamicCodeAttribute>]
    public static bool IsDynamicCodeSupported => // ...

    [FeatureGuard<RequiresDynamicCodeAttribute>]
    public static bool IsDynamicCodeCompiled => // ...
}
```

```csharp
public class FeatureGuardAttribute<T> : Attribute
    where T : Attribute, IFeatureAttribute<T> { }

public class FeatureSwitchAttribute<T> : Attribute
    where T : Attribute, IFeatureAttribute<T> { }

interface IFeatureAttribute<TSelf> where TSelf : Attribute {
    static abstract string FeatureName { get; }
}
```

This would use the type system to validate that `FeatureSwitch` and `FeatureGuard` are only used with attribute arguments that implement `IFeatureAttribute<T>`.

The use of an interface method for `FeatureName` would require more work for the tooling to retrieve the string, so we would rather encode this in a custom attribute blob.

The use of generic attributes would also restrict this form of the API to runtimes which supports generic attributes, excluding for example `netstandard2.0` libraries.

## Implementation notes

We can implement this incrementally by separating this into a few implementation phases. The implementation can be split up in numerous ways by, hard-coding various pieces of the associations between feature names and attributes. For example:

- `FeatureGuard` in analyzer for `RequiresDynamicCodeAttribute`

  We can start with only `FeatureGuard` support in the analyzer. If the `FeatureGuardAttribute` references a feature attribute rather than a property or name, this is enough information for the analyzer to silence guarded calls. We can begin by leaving the existing `Requires` feature attributes unmodified, and hard-coding support for only `RequiresDynamicCodeAttribute` as a guarded feature. We can keep the existing hard-coded association between `RequiresDynamicCodeAttribute` and `RuntimeFeature.IsDynamicCodeSupported` to validate the implementation of feature guards for dynamic code.

- `FeatureGuard` in analyzer for other features

  We can also add support for feature guards for `RequiresUnreferencedCodeAttribute` and `RequiresAssemblyFilesAttribute`, but without support for feature switches, and lacking a corresponding `RuntimeFeature` check, these guards would initially produce warnings at the definition site that would need to be suppressed.

- `FeatureGuard` in ILLink/ILCompiler

  We can independently implement support for constant propagation based on `FeatureGuard` in ILCompiler. We could start by adding support for guarding `RequiresDynamicCodeAttribute` only, which would mean treating such feature guards as known `false` constants, replacing XML substitutions that currently serve this purpose. We would rely on the analyzer validation to ensure that this corresponds to `RuntimeFeature.IsDynamicCodeSupported`, and probably not do such validation in ILCompiler.

- `FeatureSwitch` in ILLink/ILCompiler for `RequiresDynamicCodeAttribute`

  Once we add a `FeatureSwitchAttribute` API, we can begin reading these attributes in ILLink and ILCompiler, and replacing some of the existing XML feature switch definitions for `IsDynamicCodeSupported`.

- `FeatureSwitch` in analyzer

  We can teach the analyzer to respect `FeatureSwitch`. `FeatureSwitch` on `RuntimeFeature.IsDynamicCodeSupported` would replace the initially hard-coded assocaition between this and `RequiresDynamicCodeAttribute`.

- Open up `FeatureGuard` in analyzer in combination with `FeatureSwitch`

  Once `FeatureSwitch` is supported in the analyzer, the presence of both `FeatureSwitch` and `FeatureGuard` on a property will indicate that the switch is meant to be disabled whenever the guarded feature is disabled. We can then silence warnings on feature guards for `RequiresUnreferencedCodeAttribute` guards that have their own feature switches, relying on them being externally disabled for trimmed apps.

- Replace hard-coded support for existing feature attributes

  Once we have an approved API for feature attributes, we can change `RequiresDynamicCodeAttribute`, `RequiresUnreferencedCodeAttribute`, and `RequiresAssemblyFilesAttribute` to be implemented as feature attributes according to the new API. The new API should allow this as a non-breaking change (by deriving from a base class, or annotating the attribute class definitions with another attribute). Then the analyzer, ILLink, and ILCompiler can all be updated to respect this pattern instead of these specific features.