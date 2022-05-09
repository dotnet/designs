# Capability APIs

**Owner** [Immo Landwerth](https://github.com/terrajobst)

*This is a revamped version of a [previous draft](https://github.com/dotnet/designs/pull/111).*

In .NET 6, we introduced the [Platform Compatibility Analyzer][platform-compat].
It allows us to mark platform specific APIs. This works well for APIs that are
intrinsically platform specific (e.g. the Windows registry) or are wrappers
around OS specific APIs that are only implemented for a specific set of
platforms. The idea is that consumers will either mark themselves as platform
specific or dynamically check by calling, for example,
`OperatingSystem.Is<Platform>()`.

For cross-cutting concerns, such as code generation or threading, this pattern
doesn't work super well because the list of platforms can change over time or
because the platform concept isn't sufficient, when, for example, the runtime
behavior depends on orthogonal choices such as when the code is compiled
ahead-of-time.

## Scenarios and User Experience

### Thread Creation

Maddie is building a Blazor WebAssembly application that she is porting from a
legacy Windows desktop application. Part of the application is uploading
relatively large data files to a cloud backend. To avoid disappointment down the
road, the UI performs some upfront validation that parses the input files. To
prevent the UI from freezing up, someone wrote some code that offloaded that
logic to a worker thread. While that is not ideal, that worked well for the last
decade in the .NET Framework app. She copy and pastes a good chunk of code that
includes the following snippet:

```C#
void StartValidation()
{
    InputValidationConfiguration args = GetConfiguration();
    SynchronizationContext syncContext = SynchronizationContext.Current;

    Thread thread = new Thread(() =>
    {
        var result = Validator.Validate(args);
        syncContext.Post(_ => ValidationComplete(result));
    });
    
    thread.Start();
}
```

She immediately gets a diagnostic:

    CAXXXX: The API 'Thread.Start()' requires the capability 'ThreadCreation'. Either mark the consumer with `[RequiresThreadCreation]` or check by calling 'Thread.IsCreationSupported'.

After some research into what this means she realizes that not all runtime
targets allow creating new threads, which includes her desired target, which is
WebAssembly. After some reading, she realizes that she will get away with
replacing this code with `Task.Run()`.

## Requirements

### Goals

* The analyzer should be on default and help developers targeting WebAssembly to
  find places where they rely on thread creation but don't run in a browser that
  supports threads.

### Non-Goals

* [Platform Compatibility Analyzer][platform-compat]. For some annotations we
  might choose to move to the capability based model, but we generally want to
  keep the platform analyzer to represent platform specific APIs. In general,
  `[UnsupportedOSPlatform]` are the most likely candidates for capability based
  annotation.

## Design

### Analyzer IDs and Severity

Similar to the [Platform Compatibility Analyzer][platform-compat], this analyzer
should be enabled by default. The IDs should start with CA and use the next
available slot.

Diagnostic ID | Severity | Description
--------------|----------|--------------
CA_____       | Warning  | The API '{0}' requires the capability '{1}'. Either mark the consumer with `{2}` or check by calling '{3}'.

### Capability Attributes

```C#
namespace System.Diagnostics.CodeAnalysis
{
    public abstract class CapabilityAttribute : Attribute
    {
        protected CapabilityAttribute();
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class CapabilityCheckAttribute : Attribute
    {
        public CapabilityCheckAttribute(Type type, string propertyName);
        public Type Type { get; }
        public string PropertyName { get; }
    }

    [AttributeUsage(AttributeTargets.Method |
                    AttributeTargets.Property |
                    AttributeTargets.Field,
                    AllowMultiple = true, Inherited = false)]
    public sealed class CapabilityGuardAttribute : Attribute
    {
        public CapabilityGuardAttribute(Type assertedCapabilityAttributeType);
        public Type AssertedCapabilityAttributeType { get; }
    }
}
```

The behavior is as follows:

* Capabilities are represented as types extending from `CapabilityAttribute`
* Capabilities should be prefixed with `Requires` followed by the capability,
  such as `RequiresThreadCreation` or `RequiresDynamicCode`.
* APIs that should only be used when this capability is available should be
  marked with this attribute.
* Capability attributes should indicate the type and member of the capability
  check, which is expressed by applying the `[CapabilityCheck]` attribute on the
  capability attribute itself. The referenced member can either be a method with
  no parameters or a property. In either case the return type must be of type
  `bool`. A value of `true` indicates that the capability exists. The
  `[CapabilityCheck]` attribute can be applied multiple times, which would
  indicate different ways the caller can check.
* In addition, a consumer can introduce their own capability checks by marking
  their APIs with the `[CapabilityGuard]` attribute. They reference the type of
  capability attribute the guard is asserting. The guard can either be a method,
  property, or a field. In case of methods, parameters are also permissible.
  However, the return value must of be of type `bool`. A value of `true` implies
  the capability is there. Custom guards are useful for cases where a feature
  requires multiple checks. We recently added this to our [platform
  compatibility analyzer][platform-compat] and it has proven to be invaluable
  because it makes it much easier to enable code analysis for existing code
  bases where capabilities are already checked as part of the infrastructure.

### Threading Annotations

In order to model the thread creation annotation, we need an attribute for the
capability and a new capability API on `Thread`:

```C#
namespace System.Threading
{
    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = false)]
    [CapabilityCheck(typeof(Thread), nameof(Thread.IsCreationSupported))]
    public sealed class RequiresThreadCreationAttribute : CapabilityAttribute
    {
        public RequiresThreadCreationAttribute();
    }

    public partial class Thread
    {
        public static bool IsCreationSupported { get; }

        [RequiresThreadCreation]
        public void Start();
    }
}
```

### Reflection Emit

In order to model dynamic code generation we can probably reuse the existing
linker annotation `RequiresDynamicCodeAttribute` and change the attribute to
inherit from `CapabilityAttribute`. For the capability check we can reuse the
existing `IsDynamicCodeSupported` and `IsDynamicCodeCompiled` members on
`RuntimeFeature`.

**OPEN QUESTION** Can we reuse the `RequiresDynamicCodeAttribute`? Currently
that's only used by the linker, but the semantics would be equivalent.

```C#
namespace System.Reflection.Emit
{
    [CapabilityCheck(typeof(RuntimeFeature), nameof(RuntimeFeature.IsDynamicCodeSupported))]
    [CapabilityCheck(typeof(RuntimeFeature), nameof(RuntimeFeature.IsDynamicCodeCompiled))]
    public sealed class RequiresDynamicCodeAttribute : CapabilityAttribute
    {
        public RequiresDynamicCodeAttribute();
    }

    [RequiresDynamicCode]
    public partial class DynamicMethod : MethodInfo
    {
    }

    [RequiresDynamicCode]
    public partial class AssemblyBuilder : Assembly
    {
    }

    // Would also mark
    // - Other *Builder types
    // - AppDomain.DefineDynamicAssembly
    // - ILGenerator
}
```

[platform-compat]: https://docs.microsoft.com/en-us/dotnet/standard/analyzers/platform-compat-analyzer
