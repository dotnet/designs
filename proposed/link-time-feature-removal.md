# Link-time framework feature removal

[Self-contained deployment](https://docs.microsoft.com/en-us/dotnet/core/deploying/) of .NET Core applications (a deployment that includes both the runtime/framework, and app code) provides a unique challenge and opportunity to tailor the deployed bits to the needs of the application. A whole program analysis step that runs as part of publishing can identify parts of the application and libraries (framework or otherwise) that are not used – and remove them from the deployment package. This has a positive impact on the size of the deployment and saves considerable amounts of storage space and bandwidth for the end user.

[IL Linker](https://github.com/dotnet/announcements/issues/30) (for CoreCLR-based .NET Core applications) and Dependency Reducer (for .NET Native-based .NET Core applications) have demonstrated that performing static reachability analysis of methods and types can lead to significant savings. Static reachability analysis assumes that there are certain roots in the application (places that are always reachable, such as the Main method of the app); the analysis then builds a transitive closure of all the methods and types reachable from roots. Methods and types that are not reachable from roots are considered unused and can be discarded.

## Beyond static reachability analysis

Static reachability analysis has its limits though: the fact that something is statically reachable doesn’t mean it’s going to be used at runtime. While sometimes it’s possible to change the library code in a way that enables removal of unused code through static reachability analysis alone ([example](https://github.com/dotnet/corefx/pull/23867)), this doesn’t always work, and often leads to clunky and fragile code.

Proving a certain part of the program that is statically reachable won’t be used at runtime is a hard problem. If the library author however can identify parts of the codebase responsible for supporting a certain feature, the static analyzer can use this information to remove more code.

This design doc is proposing two things:
*	A way for library authors to annotate removable features within their libraries.
*	A way for developers to opt out of certain features at the time of publishing their app.

Note that these are optimizations that the app developers must explicitly opt into and will not be enabled by default.

## Removable method bodies

.NET provides a mechanism by which a single source code file can be compiled in multiple ways by passing switches to the source compiler. This is the [ConditionalAttribute](https://msdn.microsoft.com/en-us/library/system.diagnostics.conditionalattribute(v=vs.110).aspx): an attribute that lets the user specify a call to a certain method is conditioned on a flag passed to the source compilers. Having a call to a method be conditional is a concept that is relatively easy to grasp and can be pretty powerful.

The concept behind how `ConditionalAttribute` works at source compilation time can be naturally extended to publishing time.

Moving the concept out of source compilation time into publishing time will come with a subtle behavioral difference:
*	`ConditionalAttribute` is processed at source compilation time. When the source compiler sees a callsite to a method annotated as conditional, it removes the entire callsite. As a result, code such as `MyConditionalMethod(SomeOtherMethod())` will remove both the call to `MyConditionalMethod` and `SomeOtherMethod`.
*	Doing this step at publishing time won't give us much insight into how the callsite looked like in the source code. Both `var x = SomeOtherMethod(); MyRemovableMethod(x);` and `MyRemovableMethod(SomeOtherMethod(x))` could be represented by the same IL, depending on source compiler's choices. As such, these will still look like a function call after removal, except the function call won't do anything because the body gets replaced by a NOP.

An advantage of this is that an IL rewriting pass (independent on static reachability analysis) can apply this transformation in a single pass through all the methods in the library and doesn't need to care about callsites outside of the library.
Because the function call will still be there, we can also allow removable methods to have a return value. That value will always be `default` when the method body was removed.

In general, we can't preserve all language semantics when the removal happens, so I propose we just don't bother:
*	Initializing C# `out` parameters for removed methods is out of scope.
*	Non-nullable reference types in return parameters are out of scope.

## System.Runtime.CompilerServices.RemovableAttribute
(Exact naming subject to API review in the future.)

```csharp
// Instructs IL linkers that the method body decorated with this attribute can be removed.
// The return value of the method with removed body is replaced with the default value
// that corresponds to the type (0 for int, null for reference types, etc.).
[AttributeUsage(AttributeTargets.Method)]
public class RemovableAttribute : Attribute
{
    public string FeatureSwitchName { get; }
    
    public RemovableAttribute(string featureSwitchName)
    {
        FeatureSwitchName = featureSwitchName;
    }
}
```

IL linkers will match the attribute by name. We’ll want to add this attribute to the framework, but to support libraries that target downlevel frameworks, we want to have the ability to define the attribute anywhere.

The guidelines for deciding feature switch names will follow the [naming guidelines for AppContext switches](https://msdn.microsoft.com/en-us/library/system.appcontext.setswitch(v=vs.110).aspx).

We can potentially also extend this by defining well-known feature switches that the publishing process would define (e.g. “Am I publishing for Windows?”, “Am I publishing for a platform that has a JIT?” etc.)

## System.Runtime.CompilerServices.CodeRemovedException
(Exact naming subject to API approval.)

To provide a unified experience for situations when a feature got removed but is required at runtime, a new exception type will be defined.

```csharp
public class CodeRemovedException : NotSupportedException
{
    public string FeatureSwitchName { get; }

    public CodeRemovedException(string featureSwitchName)
        : base(SR.Format(SR.CRE_CodeRemoved, featureSwitchName))
    {
        FeatureSwitchName = featureSwitchName;
    }
}
```

We will update the debugging tools to have them break on first chance exceptions of this type by default (same way they already do for e.g. `XamlParseException`, or `MissingMetadataException`).

Again, we’ll want to add this exception type to the core framework, but to support downlevel frameworks, we’ll ship with local copies. Visual Studio performs first chance exception filtering by name, and user code should not be trying to catch these, so the exact identity shouldn’t matter.

It will be up to the library code to throw this exception when appropriate since the removed method body will be a NOP, not a throw of an exception of this type.

We want the removed body to be a NOP to provide a choice to the library author. There might be feature switches that are optional and provide a graceful fallback (for example, a library functionality that relies on Reflection.Emit might have a more compact fallback without runtime code generation - when the switch is defined, we no longer need to include Reflection.Emit in the deployment, but the library still works as expected).

## Special considerations

Setting a feature switch name could also set the corresponding AppContext switch to allow the library to emulate the behavior with code removed before publishing (I.e. in F5 Debug scenarios).

In a fully ahead of time compiled scenario where an app can be composed of multiple native images, each native image can be compiled with a different removal setting: consider .NET Native SharedLibrary (where multiple IL assemblies get compiled into a single native module that app can compile against), or the multi-object compilation mode of CoreRT (where each IL assembly is compiled into a single .obj/.o file and linked using a platform linker). For this to work:
*	Methods marked removable cannot be inlined outside of their home native module.
*	Methods marked removable should not be on generic types, or be generic methods. Their presence or absence can have effects on the shape of generic dictionaries (lookup tables used for shared code). Having the same shape of generic dictionaries across all native modules that are part of the same application is required for correctness.
