# Function Pointers
#### Steve Harter
#### December 16, 2022

# Background
For additional context, see:
- [Original feature ask](https://github.com/dotnet/runtime/issues/11354)
- [MetadataLoadContext feature ask](https://github.com/dotnet/runtime/issues/43791)
- [7.0 introspection attempt](https://github.com/dotnet/runtime/pull/71516)
- [C# function pointer spec](https://learn.microsoft.com/dotnet/csharp/language-reference/proposals/csharp-9.0/function-pointers)

Function pointers, which have been in ECMA-335 and used by the CLR since the beginning were only recently supported by C# in C# v9 and .NET v6. However, the .NET type system has not been updated to directly support them and they are currently exposed as the `IntPtr` type:
```cs
Type type = typeof(delegate*<int, bool>); // System.IntPtr (currently)
```

Reflection also returns `IntPtr` for both runtime-reflection through `FieldInfo\ParameterInfo\PropertyInfo` and static-reflection through `MetadataLoadContext`:
```cs
  FieldInfo fi = typeof(MyClass).GetField("_fn");
  Type type = fi.FieldType; // System.IntPtr (currently)

  unsafe class MyClass
  {
      public delegate*<int, bool> _fn; // A field as a function pointer
  }
```

Treating function pointers as `IntPtr` essentially makes reflection introspection impossible, blocking scenarios that care about function pointers. The proposal is to change from `IntPtr` to `Type` which is a **breaking change**:
```cs
Type type = typeof(delegate*<int, bool>); // System.Type (proposed design)
bool isFunctionPointer = type.IsFunctionPointer; // A new method which returns 'true' here
```

Note that Mono returns "System.MonoFNPtrFakeClass" for the type name, not "IntPtr". This design applies to Mono as well and is expected that both runtimes are consistent going forward.

## Function pointer metadata
A function pointer, as an ECMA-335 signature, is non-trivial and contains the following metadata:
- The return type
- The parameter count
- The types of all parameters
- Custom modifiers for each parameter type and the return type  **(contentious area)**
- The calling convention(s) **(legacy enum encoding + newer encoding using custom modifiers)**

An instance of a function pointer type, like a standard pointer type, is trivial, containing just the pointer to the function (thus the original and current use of `IntPtr`).

# Design
## Treating a function pointer as a type
From previous API discussions, the proposal is to treat a function pointer as a `System.Type` class and not, for example, by exposing a new "MethodSignature" class that represents a function pointer. This is consistent in ECMA and C# where a function pointer is a type, where can be specified as the type for parameters, variables, properties and fields in addition to composing with other types:
```cs
void CallMe(delegate*<int, bool> fn) { } // As a parameter type
delegate*<int, bool> fn = &MyFunctionPointer; // As a variable type
delegate*<int, bool>* pfn = &fn; // Pointer to function pointer
delegate*<int, bool>[] afn; // Array of function pointers
delegate*<int, bool>*[] apfn; // Array of pointers to function pointers
delegate*<Action<int>> gfn; // Function pointer with generic type
```

Using `Type` also allows the [`TypeDelegator`](https://learn.microsoft.com/dotnet/api/system.reflection.typedelegator) functionality to be easily extended to support the new methods added to `Type`.

## Exposing metadata on Type
Previous API discussions covered whether the metadata for a function pointer should be "lifted" and directly exposed on `Type` or whether the metadata should abstracted into another class, like a "MethodSignature" class but which would still be returned from `Type` and tied 1:1. From those discussions, it was decided to use the "lifted" approach which is consistent with how `Type` exposes metadata and helper methods for other type representations:
- Pointers: `ElementType`, `IsPointer`, `IsPointerImpl`, `MakePointerType()`
- Arrays: `ElementType`, `IsArray`, `IsSZArray`,  `IsVariableBoundArray`, `GetArrayRank()`, `IsArrayImpl()`, `MakeArrayType()`
- Enums: `IsEnum`, `GetEnumName()`, `GetEnumNames()`, `GetEnumUnderlyingType()`, `GetEnumValues()`, `GetEnumValuesAsUnderlyingType()`, `IsEnumDefined()`
- Generics: `ContainsGenericParameters`, `GenericParameterAttributes`, `GenericParameterPosition`, `GenericTypeArguments`, `IsConstructedGenericType`, `IsGenericMethodParameter`, `IsGenericParameter`, `IsGenericType`, `IsGenericTypeDefinition`, `IsGenericTypeParameter`, `GetGenericArguments()`, `GetGenericParameterConstraints()`, `GetGenericTypeDefinition()`, `MakeGenericMethodParameter()`, `MakeGenericSignatureType()`, `MakeGenericType()`

The original design, however, did include a `FunctionPointerParameterInfo` that contained a mechanism to obtain both the type and custom modifiers for each parameter. The proposed design here no longer directly exposes custom modifiers for parameters, so the `FunctionPointerParameterInfo` is no longer necessary since the only state remaining is just the parameter type, which is now directly lifted to `Type` via `Type[] GetFunctionPointerParameterTypes()`.

## Custom modifiers
The [7.0 introspection attempt](https://github.com/dotnet/runtime/pull/71516) returned all custom modifiers including modifiers not used or needed by the runtime, such as parameter modifiers for `ref`, `out` and `in`. Due to concerns with that approach, the function pointer feature was pulled from late in 7.0 in order to properly vet and approve a new design (this design) for v8. The intent is to add this functionality early in v8 which will also allow time for feedback.

Consider the following where `ref\out\in` all map to a reference (`&`) by the runtime:
```cs
class MethodHolder
{
    public static void M1(in int i) { }
    public static void M2(ref int i) { }
    public static void M3(out int i) { i = 42; }
}

Type t1 = typeof(MethodHolder).GetMethod("M1").GetParameters()[0].ParameterType;
Type t2 = typeof(MethodHolder).GetMethod("M2").GetParameters()[0].ParameterType;
Type t3 = typeof(MethodHolder).GetMethod("M3").GetParameters()[0].ParameterType;

Debug.Assert(t1 == t2 && t2 == t3); // True today; all are "int reference type" (int&)
```

Also consider [ILGenerator.EmitCalli()](https://docs.microsoft.com/dotnet/api/system.reflection.emit.ilgenerator.emitcalli#system-reflection-emit-ilgenerator-emitcalli(system-reflection-emit-opcode-system-runtime-interopservices-callingconvention-system-type-system-type())) as a reference implementation which does not support modifiers:
```cs
void EmitCalli(
    System.Reflection.Emit.OpCode opcode,
    System.Runtime.InteropServices.CallingConvention unmanagedCallConv,
    Type? returnType,
    Type[]? parameterTypes)
```

So custom modifiers, except for calling convensions, are not exposed in the runtime. They are not necessary for the key scenario of passing or invoking a function pointer and would also interfere with equality and castability. If function pointers expose all custom modifiers, such as `ref\out\in` and the C++/CLI `const`, then equality and castability will be based on exact matches to that which is considered overly restrictive for runtime use and may break C++/CLI usage.

## Custom modifiers for calling convention
The only custom modifiers exposed by the runtime are the built-in classes representing calling conventions - these classes are named `System.Runtime.InteropServices.CallConv*`:
- [CallConvCdecl](https://docs.microsoft.com/dotnet/api/system.runtime.compilerservices.callconvcdecl)
- [CallConvStdCall](https://docs.microsoft.com/dotnet/api/system.runtime.compilerservices.callconvstdcall)
- [CallConvThiscall](https://docs.microsoft.com/dotnet/api/system.runtime.compilerservices.callconvthiscall)
- [CallConvFastcall](https://docs.microsoft.com/dotnet/api/system.runtime.compilerservices.callconvfastcall)
- [CallConvSuppressGCTransition](https://docs.microsoft.com/dotnet/api/system.runtime.compilerservices.callconvsuppressgctransition)
- [CallConvMemberFunction](https://docs.microsoft.com/dotnet/api/system.runtime.compilerservices.callconvmemberfunction)

C# in particular just maps calling conventions specified in a function pointer to `System.Runtime.InteropServices.CallConv*` types as metadata. These CallConv* types must live in the same assembly as defines `System.Object` (i.e. `System.Private.Corelib.dll`).

### Normalization of calling conventions
Calling conventions can be specified in one of two ways in metadata:
- The older "CallKind" enum (which has issues with not being large enough to hold the newer calling conventions).
- The newer approach of using modopts on the return parameter.

C# only uses the newer modopts approach when necessary; see the [c# spec section](https://learn.microsoft.com/dotnet/csharp/language-reference/proposals/csharp-9.0/function-pointers#metadata-representation-of-calling-conventions). However, we should assume any valid encoding.

When returning the calling conventions through `Type.GetFunctionPointerCallingConventions()` any calling convention encoded using "CallKind" will return the appropriate CallConv* type, just like it was added through a modopt. This normalization avoids having to expose the "CallKind" in the API and makes it simpler for callers since there is just a single method to return the calling conventions no matter how they were encoded.

### _Off-topic thoughts on using custom modifiers to represent calling conventions_
Using the CallConv* classes to represent calling conventions was added in V6 because new callling conventions needed to be added and would not fit within the 4 bits that were being used to encode the existing calling conventions based on ECMA-335. So the decision, arguably not the best, was made to encode new calling conventions using custom modifiers since that would not affect existing metadata readers or require a ECMA-335 change. However, there are alternative approaches which may or may not have been considered -- one such approach would be to change the encompassing byte to be a 32-bit compressed integer encoding instead which .NET/CLI uses extensively elsewhere. This compressed integer approach is possible since the encompassing byte did not use bit 7 (`0x80`) which is the flag for a compressed integer to switch from one byte to two bytes. This would require a ECMA-335 change which would break other metadata readers once two+ bytes are used.

## Signature types
Although we don't want to expose custom modifiers from function pointer types that are not calling conventions, obtaining the other custom modifiers is useful for those who read metadata for varying reasons. The proposal is to expose functionality that return "signature types" which are an extended function pointer type that has `GetRequiredCustomModifiers()` and `GetOptionalCustomModifiers()` methods which return all custom modifiers. These APIs only work with signature types that are returned from `FieldInfo.GetSignatureFieldType()`, `PropertyInfo.GetSignaturePropertyType()` and `ParameterInfo.GetSignatureParameterType()`.

The "signature type" terminology is used in ECMA-335 but also called a "modified type" elsewhere including the CLR, `MetadataLoadContext` and `MetadataReader`.

In CLI metadata, only fields, properties, local variables, method parameters and function pointer parameters allow custom modifiers. A type, method or function pointer itself cannot have custom modifiers; function pointers use the "return parameter" to contain its custom modifiers used for calling conventions. Thus, the new `GetSignature*Type()` methods could be called `GetFunctionPointerSignature*Type()` instead. However, the shorter version is used here just in case this no longer holds to just function pointers.

See also see ECMA-335 `II.23.2 Blobs and signatures` for additional information. Note that in C# a function pointer is emitted as an ECMA "StandaloneMethodSig". There are several other types of custom modifiers besides the calling convention ones -- for example, in C#, a field is also a "signature" and can have a `volatile` modifier which is emitted as a custom modifier and would be returned via `FieldInfo.GetSignatureFieldType().GetRequiredCustomModifiers()` as the `System.Runtime.CompilerServices.IsVolatile` type, and in C++\CLI there is `System.Runtime.CompilerServices.IsConst` and others.

## Supporting both runtime- and and static-reflection
Previous discussions included the suggestion that both calling conventions and signature types are only exposed from the [`MetadataLoadContext`](https://learn.microsoft.com/dotnet/api/system.reflection.metadataloadcontext) class since it is the preferred approach to read metadata (vs. runtime reflection) due to being agnostic to the runtime referenced by the various reflected types. However, this design proposes full fidelity between the runtime and `MetadataLoadContext` (other runtimes besides the CoreCLR, including Mono and AOT runtimes, will need to be updated of course).

The reason to support calling conventions through `Type.GetFunctionPointerCallingConventions()` with the runtime are that the calling conventions may be useful in runtime-cases (not just introspection cases) and because there is a larger design factor with the CoreClr: the internal function pointer implementation uses the "CallKind" byte to hold the legacy calling conventions for things like determining if the VARARG convention is used. In addition, the CoreClr has a concept of "TypeKey" and "TypeHandle" and they are 1:1 today meaning that all `Type` instances are fully self-describing and thus, we can't have additional state hanging off of `Type` that is not part of the "key", such as calling conventions -- thus also the reason for bifurcation of signature types and non-signature types.

This means that if we don't expose the calling conventions on `Type` for function pointers, some function pointer types will be castable and others not, and with no obvious way to inspect the `Type` to determine why. We could exposed just the "CallKind" byte on `Type`, but that is not desired since that will confuse and\or complicate the consumer's usage -- see "Normalization of calling conventions".

Since we must already do the work of exposing the calling conventions including the newer ones that are read via custom modifiers, it makes sense then to expose all custom modifieres (via the new `Type.GetRequiredCustomModifiers()` and `Type.GetOptionalCustomModifiers()`.

## Type identity 
### Using of void*
A function pointer addresscan be cast to any function pointer type when using `void*`:
```cs
delegate*<string> fn_string = &StringMethod;
int i = ((delegate*<int>)(void*)fn_string)(); // Unsafe code allows any cast once void* is used
static string StringMethod() => "Forty-Two";
```
And the IL just does a `ldftn` with no check:
```
.locals init (method string *() V_0, int32 V_1)
ldftn      string ConsoleApp104.Program::'<Main>g__StringMethod|0_0'()
stloc.0
ldloc.0
calli      int32()
stloc.1
```

### Equality:
```cs
bool f = typeof(delegate*<string>) == typeof(delegate*<int>); // Expect 'false' (today 'true' since they are both IntPtr)
```
Note the use of `ldtoken`:
```
ldtoken    method string *()
call       class [System.Runtime]System.Type [System.Runtime]System.Type::GetTypeFromHandle(valuetype [System.Runtime]System.RuntimeTypeHandle)
ldtoken    method int32 *()
call       class [System.Runtime]System.Type [System.Runtime]System.Type::GetTypeFromHandle(valuetype [System.Runtime]System.RuntimeTypeHandle)
call       bool [System.Runtime]System.Type::op_Equality(class [System.Runtime]System.Type, class [System.Runtime]System.Type)
```

### Casting
Using arrays to help:
```cs
object arrInt = new delegate*<int>[1];
var yikes = (delegate*<string>[])arrInt; // Expect InvalidCastException
```
```
.locals init (object V_0, method string *()[] V_1)
ldc.i4.1
newarr     method int32 *()
stloc.0
ldloc.0
castclass  method string *()[]
stloc.1
```
### `Is` keyword
```cs
object arrInt = new delegate*<int>[1];
bool f = arrInt is delegate*<string>[]; // Expect 'false'
```
IL:
```
.locals init (object V_0, bool V_1)
ldc.i4.1
newarr     method int32 *()
stloc.0
ldloc.0
isinst     method string *()[]
ldnull
cgt.un
stloc.1
```

In all cases above* any calling conventions are included in type identity. For example,
`delegate* unmanaged[Cdecl]<void>`
and
`delegate* unmanaged[FastCall]<void>`are not equal.

_* not sure how the `is` (`isinst`) is implemented in the CLR and whether we can get the same behavior as `castclass`._

All non-CallConv* modifiers, such as `ref\in\out\const` are not considered part of the type identity and are ignored unless the type is a signature type.

## Invoke capabilities
### ILGenerator
Overloads will be added to [`ILGenerator.EmitCalli()`](https://learn.microsoft.com/dotnet/api/system.reflection.emit.ilgenerator.emitcalli) to support new calling conventions that are modifier-based instead of enum-based. Since the enum-based calling conventions through `System.Runtime.InteropServices.CallingConvention` have not scaled well, specifying "CallConv*" types from `System.Runtime.InteropServices` is assumed.

### Late-bound invoke
No new invoke functionality is expected to be added to v8 for invoking a function pointer in a late-bound manner. Today this can be done with usage of:
- [`MethodBase.MethodHandle`](https://learn.microsoft.com/dotnet/api/system.reflection.methodbase.methodhandle) for managed methods. Also see the `MethodHandle` property on `DynamicMethod`, `ConstructorBuilder` and `MethodBuilder`.
- [`[UnmanagedFunctionPointer]`](https://learn.microsoft.com/dotnet/api/system.runtime.interopservices.unmanagedfunctionpointerattribute) applied to delegates for native methods
- [`RuntimeMethodHandle.GetFunctionPointer()`](https://learn.microsoft.com/dotnet/api/system.runtimemethodhandle.getfunctionpointer)
- [`Marshal.GetDelegateForFunctionPointer()`](https://learn.microsoft.com/dotnet/api/system.runtime.interopservices.marshal.getdelegateforfunctionpointer)
- [`Delegate.CreateDelegate()`](https://learn.microsoft.com/dotnet/api/system.delegate.createdelegate)
- [`Delegate.DynamicInvoke()`](https://learn.microsoft.com/dotnet/api/system.delegate.dynamicinvoke)

### Marshalling
To pass a function pointer or a standard pointer with reflection, it can be converted to `IntPtr` which is then boxed. At that point, the pointer can be passed to parameters of type `void*` or `IntPtr`.

Reflection does not automatically marshal standard pointers, such as `int*`, when invoking a method, unlike similar cases with references such as `int&`. 

To manually marshal a standard pointer, [Pointer.Box()](https://learn.microsoft.com/dotnet/api/system.reflection.pointer.box) is used. Essentially it is a `Tuple<Type, IntPtr>` and used to validate the pointer during invoke, preventing an invalid pointer type from being passed. 

Samples to illustrate current behavior of function pointers with `System.Object`-based reflection invoke:
```cs
static bool CallMe(int n) { return n == 42; }

static void Main()
{
    delegate*<int, bool> fn = &CallMe;
    bool ret;

    MethodInfo m1 = typeof(Callee).GetMethod("VoidStar")!;
    // CS0029: Cannot implicitly convert delegate*<int, bool> to `object`
    // ret = (bool)m1.Invoke(null, new object[] { m })!;

    // CS0029: Cannot implicitly convert void* to `object`
    // ret = (bool)m1.Invoke(null, new object[] { (void*)m })!;

    // Must be cast to IntPtr first
    ret = (bool)m1.Invoke(null, new object[] { (IntPtr)fn, 42 })!;
    Console.WriteLine(ret); // True

    // Similar, but call a method taking an IntPtr instead of void*
    MethodInfo m2 = typeof(Callee).GetMethod("IntPtr")!;
    ret = (bool)m2.Invoke(null, new object[] { (IntPtr)fn, 42 })!;
    Console.WriteLine(ret); // True

    // By taking a pointer to a function pointer, we can use Pointer.Box
    MethodInfo m3 = typeof(Callee).GetMethod("DelegateStar")!;
    ret = (bool)m3.Invoke(null, new object[] { Pointer.Box(&fn, typeof(delegate*<int, bool>*)), 42 })!;
    Console.WriteLine(ret); // True

    MethodInfo m4 = typeof(Callee).GetMethod("DelegateStarMismatch")!;
    // ArgumentException (as expected)
    ret = (bool)m4.Invoke(null, new object[] { Pointer.Box(&fn, typeof(delegate*<int, bool>*)), 42 })!;
}

public unsafe class Callee
{
    public static bool VoidStar(void* doit, int v) => ((delegate*<int, bool>) doit)(v);
    public static bool IntPtr(IntPtr doit, int v) => ((delegate*<int, bool>) doit)(v);
    public static bool DelegateStar(delegate*<int, bool>* doit, int v) => (*doit)(v);
    public static bool DelegateStarMismatch(delegate*<string, bool>* doit, int v) => (*doit)("");
}
```

There is no proposal to extend `Pointer` for use with function pointers, and as shown above a pointer to a function pointer can be used to support strongly-typed, validated function pointers.

There is also no proposal to add an analogous `FunctionPointer` type to contain both the type and address which would allow passing a function pointer directly without using `IntPtr`. This is considered a lower-priority feature and can be added later but would require all runtimes to add special behavior for this.

For [the proposed byref reflection APIs](https://github.com/dotnet/runtime/issues/45152) to be added in V8, which do not require boxing and perform little to no marshalling, reflection would allow a strongly-typed, validated function pointer parameter without requiring `Pointer\FunctionPointer` since it performs simple type equality checks against the caller's and callee's parameter types.

## Overidden Type members
Below are the non-trivial behaviors overridden by a function pointer type.

### `ToString()`
Returns a friendly string in the format `return_type + "(" + optional_parameter_types_comma_separated + ")"` and compose with the `ToString()` of other types:
ToString() result | Delegate signature
---|---
"System.Void()" | `delegate*<void>`
"System.Int32()" | `delegate*<int>`
"System.Int32(System.String)" | `delegate*<string, int>`
"System.Int32(System.String, System.Boolean*&)" | `delegate*<string, ref bool*, int>`
"System.Int32()*" | `delegate*<int>*`
"System.Int32()[]" | `delegate*<int>[]`
"System.Int32()*[]" | `delegate*<int>*[]`
"System.Int32()()" | `delegate*<delegate*<int>>`
"System.Boolean(System.String(System.Int32))" | `delegate*<delegate*<int, string>, bool>`

### `FullName` and `AssemblyQualifiedName` properties
Returns `null`. This is a simplification that avoids a [type name grammar update](https://docs.microsoft.com/dotnet/framework/reflection-and-codedom/specifying-fully-qualified-type-names) that would need to include grammar for custom modifiers as well as supporting  `Type.GetType()`, `Assembly.GetType()` and any other similar APIs that parse those strings in order to construct a function pointer from it.

Support for a grammar update is possible in the future, with a minimal breaking change here.

Note that this property is already nullable and does return `null` in open generic parameters cases as well.

### `Name` property
Returns `String.Empty()`. Any other syntax here is somewhat arbitrary, and since the property is not nullable, the empty string seems fine unless a compelling reason is found. 

Other options discussed include "`*()`" and "`*(comma_for_each_parameter)`". Consider that generics do not provide parameter type information either:
```cs
    string s = typeof(Action<int>).Name; // "Action`1
```

### `Namespace` property
Returns `null`.

A standard pointer returns the namespace of the referenced type, but we don't have that with function pointers since they are declared inline.

### `IsPointer` property
Returns `false`. Per past design discussions, this should be `false` since function pointers and standard pointers are not the same, and this also is non-breaking for existing code that needs to determine if a type is a standard pointer type.

### `IsValueType` and `IsClass` properties
Use the same behavior as standard pointers which is `IsValueType == false` and `IsClass == true`.

The type hierarchy is covered somewhat [here](https://learn.microsoft.com/previous-versions/visualstudio/visual-studio-2008/2hf02550(v=vs.90)) and in ECMA\CLI: 
- Type
  - Value types
    - Built-in value types
    - User-defined value types
    - Enumerations
  - Reference types
    - **Pointer types**
    - Interface types
    - Self-describing types
      - Arrays
      - Class types
        - User-defined classes
        - Boxed value types
        - Delegates

# Proposed APIs
## System.Type
```diff
namespace System
{
    public abstract class Type
    {
        // These throw InvalidOperationException if IsFunctionPointer is false.
+       public virtual Type GetFunctionPointerReturnType();
+       public virtual Type[] GetFunctionPointerParameterTypes();
+       public virtual bool IsFunctionPointer { get; }

+       public virtual bool IsUnmanagedFunctionPointer { get; }

        // These only work for types obtained from GetSignature*Type().
        // An empty Type[] is returned otherwise.
+       public virtual Type[] GetFunctionPointerCallingConventions();
+       public Type[] GetRequiredCustomModifiers();
+       public Type[] GetOptionalCustomModifiers();
    }
}
```
### Examples
```cs
// Simple managed function pointer
Type type = typeof(delegate*<int, bool>);
Debug.Assert(type.IsFunctionPointer == true);
Debug.Assert(type.IsUnmanagedFunctionPointer == false);
Debug.Assert(type.GetFunctionPointerReturnType() == typeof(bool));
Debug.Assert(type.GetFunctionPointerParameterTypes().Length == 1);
Debug.Assert(type.GetFunctionPointerParameterTypes()[0] == typeof(int));
Debug.Assert(type.GetFunctionPointerCallingConventions().Length == 0);
```

```cs
// Simple unmanaged function pointer
Type type = typeof(delegate* unmanaged[Cdecl, MemberFunction]<int, bool>);
Debug.Assert(type.IsFunctionPointer == true);
Debug.Assert(type.IsUnmanagedFunctionPointer == true);
Debug.Assert(type.GetFunctionPointerReturnType() == typeof(bool));

Debug.Assert(type.GetFunctionPointerParameterTypes().Length == 1);
Debug.Assert(type.GetFunctionPointerParameterTypes()[0] == typeof(int));

Debug.Assert(type.GetFunctionPointerCallingConventions().Length == 2);
Debug.Assert(type.GetFunctionPointerCallingConventions[0] == typeof(CallConvCdecl));
Debug.Assert(type.GetFunctionPointerCallingConventions[1] == typeof(CallConvMemberFunction));
```

## GetSignature*Type() methods for `FieldInfo`, `PropertyInfo` and `ParameterInfo`
These return a function pointer `Type` that has references to custom modifiers.

If not a function pointer, these methods return the value from the appropriate `FieldInfo.FieldType`, `PropertyInfo.PropertyType` or `ParameterInfo.ParameterType` property.

```diff
namespace System.Reflection
{
    public class FieldInfo
    {
+       public Type GetSignatureFieldType();
    }

    public class PropertyInfo
    {
+       public Type GetSignaturePropertyType();
    }

    public class ParameterInfo
    {
+       public Type GetSignatureParameterType();
    }
}
```

### Examples
```c#
public class Holder
{
    public unsafe delegate*<in int, out int, void> _field;
}

FieldInfo fieldInfo = typeof(Holder).GetField("_field");

// Get the signature type from the field; this Type is like fieldInfo.FieldType but it includes custom mods
Type fnType = fieldInfo.GetSignatureFieldType();

// Get the function pointer's first parameter's type
Type p1Type = fnType.GetFunctionPointerParameterTypes()[0];

// Get the required modifiers from the signature type
Type[] p1ReqMods = p1Type.GetRequiredCustomModifiers();

// The required modifiers should include the 'in' modifier
Debug.Assert(p1ReqMods[0].GetType() == typeof(Runtime.InteropServices.InAttribute));
```

## System.Reflection.TypeDelegator
No new APIs, but new overrides to support delegation.
```diff
namespace System.Reflection
{
    public class TypeDelegator : TypeInfo
    {
+       public override Type GetFunctionPointerReturnType();
+       public override Type[] GetFunctionPointerParameterTypes();
+       public override bool IsFunctionPointer { get; }
+       public override bool IsUnmanagedFunctionPointer { get; }
+       public override Type[] GetFunctionPointerCallingConventions();
+       public override Type[] GetRequiredCustomModifiers();
+       public override Type[] GetOptionalCustomModifiers();
    }
}
```

## System.Reflection.Emit.ILGenerator
See the existing [ILGenerator.EmitCalli()](https://docs.microsoft.com/dotnet/api/system.reflection.emit.ilgenerator.emitcalli#system-reflection-emit-ilgenerator-emitcalli) for reference.
```diff
namespace System.Reflection.Emit
{
    public class ILGenerator
    {
+       public virtual void EmitCalli(
            // Must be OpCodes.Calli which matches existing EmitCalli convention
            // Not sure why this is redundant (for future Calli opcodes?)
+           System.Reflection.Emit.OpCodes opcode,
+           Type[] callingConventions,
+           Type? returnType,
+           Type[]? parameterTypes);

+       public virtual void EmitCalli(
+           System.Reflection.Emit.OpCodes opcode,
+           Type functionPointerType)
    }
}
```

# Breaking changes
The breaking change is: **`typeof(delegate*<...>)` and `Type.GetType()` will return a `Type` instance instead of `IntPtr`**

The fallout from this is that for any general-purpose reflection code that wants to support function pointers will likely need to call `Type.IsFunctionPointer` if there is special treatment for function pointers. 

# Potential future features
Various features not planned for 8.0; somewhat discussed earlier.

## Support `Type.FullName` and `Type.AssemblyQualifiedName`
Including `Type.GetType()` and similar APIs to parse that string to construct a function pointer type dynamically.

## `System.Reflection.FunctionPointer` for `System.Object`-based invoke
See the existing [System.Reflection.Pointer](https://docs.microsoft.com/dotnet/api/system.reflection.pointer) for reference. This would allow passing strongly-typed, validated function pointers when using `System.Object`-based invoke. Naming of Box\Unbox vs. constructors etc. is TBD.

 This is a value type, not a reference type like `Pointer`, since newer proposed reflection APIs will not force boxing to occur.

 Like `Pointer` there is no property to obtain the type since it is used in a temporary manner to manually marshal.

```diff
namespace System.Reflection
{
+   public struct FunctionPointer
+   {
+       public static FunctionPointer Box(void* ptr, Type type);
+       public static void* Unbox (FunctionPointer ptr);
+   }
}
```

## System.Reflection.MethodSignature
Currently this is called "MethodSignature" with the intention that this may also support `MethodBase` and\or `Delegate` in the future.  If that was not the case, this would be called "FunctionPointerSignature".

Shown is support for a late-bound invoke analogous to [Delegate.DynamicInvoke](https://docs.microsoft.com/dotnet/api/system.delegate.dynamicinvoke) and [MethodBase.Invoke](https://docs.microsoft.com/dotnet/api/system.reflection.methodbase.invoke).

We may also want to add an overload to `ILGenerator.EmitCalli()` to take a "MethodSignature".

`Type` exposes various methods to construct pointers, arrays and generics through `Type.MakePointerType()` etc. However, there is no proposal for V8 to add similar methods to function pointers. The "MethodSignature" class would assist with that, and could either be passed as-is to represent function pointer metadata, or we could expose a `Type CreateFunctionPointerType()` method.

```diff
namespace System.Reflection
{
+   public sealed class MethodSignature
+   {
+       public MethodSignature(Type functionPointerType);
+       public MethodSignature(Type returnType, MethodParameter[]? parameters = null, Type[]? callingConventions = null);
+       public MethodParameter ReturnType { get; }
+       public MethodParameter[] Parameters { get; }
+       public Type[] CallingConventions { get; }
+       public bool IsUnmanaged { get; }

        // We could expose this mechanism if useful.
+       public Type CreateFunctionPointerType();

        // MethodInvoker class defined elsewhere
        // Throws InvalidOperation when used with MetadataLoadContext types
+       public MethodInvoker Invoker { get; }
+   }

    // Also consider using ParameterInfo instead or adding a base class to it.
+   public sealed class MethodParameter
+   {
+       public MethodParameter(
+           Type parameterType,
+           Type[]? requiredCustomModifiers = null,
+           Type[]? optionalCustomModifiers = null);

+       public Type ParameterType { get; }
+   }
}
```
