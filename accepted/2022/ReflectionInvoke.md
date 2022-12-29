# Reflection Invoke for 8.0 (draft \ in progress)
#### Steve Harter
#### December 15, 2022

# Background
For additional context, see:
- [Developers using reflection invoke should be able to use ref struct](https://github.com/dotnet/runtime/issues/45152)
- [Originally library issue; needs refreshing\updating](https://github.com/dotnet/runtime/issues/10057)

The invoke APIs and capabilities have essentially remained the same since inception of .NET Framework. Basically, [`MethodBase.Invoke`](https://learn.microsoft.com/dotnet/api/system.reflection.methodbase.invoke):
```cs
public object? Invoke(object? obj, object?[]? parameters);

public object? Invoke(
    object? obj,
    BindingFlags invokeAttr,
    Binder? binder,
    object?[]? parameters,
    CultureInfo? culture);
```
which is implemented by [MethodInfo](https://learn.microsoft.com/dotnet/api/system.reflection.methodinfo) and [ConstructorInfo](https://learn.microsoft.com/dotnet/api/system.reflection.constructorinfo). Fields do not have an `Invoke()` and instead have `GetValue()` and `SetValue()` methods since there is no invokable code around field access.

The `Invoke()` APIs are easy to use and flexible:
- Based on `System.Object` to support both reference types (as a base type) and value types (through boxing).
    - Note that boxing does an automatic `Nullable<T>` to `null`.
- Automatic conversions:
    - Implicit casts between primitives such as from `int` to `long`.
    - `Enum` to\from its underlying type.
    - Pointer (*) types to\from `IntPtr`.

However the object-based Invoke() is not very performant:
- Boxing is required for value types.
  - Requires a heap allocation and associated GC overhead.
  - A cast is required during unbox.
  - Value types require a copy during box and unbox.
- An `object[]` must be allocated (or manually cached by the caller) to contain parameter values.
- The automatic conversions add overhead.
- `ref` and `out` parameters require overhead after the invoke due to re-assignment (or "copy back") to the `parameters` argument.
  - `Nullable<T>` is particularly expensive due to having to convert the boxed `null` to `Nullable<T>` in order to invoke methods with `Nullable<T>`, and when used with `ref` \ `out`, having to box `Nullable<T>` after invoke to "copy back" to the `parameters` argument.
- The additional arguments (`BindingFlags`, `Binder`, `CultureInfo`) add overhead even when not used. Plus using those are actually quite rare with the exception of `BindingFlags.DoNotWrapExceptions` which is proposed to be the default going forward.
  
and has limitations and issues:
- Cannot be used with byref-like types like `Span<T>` either has the target or as an argument. This is because by-ref like types cannot be boxed. **This is the key limitation expressed in this document.**
- `ref` and `out` parameters are retrieved after `Invoke()` through the `parameters` argument. This is a manual mechanism performed by the user and means there is no argument or return value "aliasing" to the original variable.
- Boxing of value types makes it not possible to invoke a mutable method and have the target `obj` parameter updated.
- [`System.Reflection.Pointer.Box()`](https://learn.microsoft.com/dotnet/api/system.reflection.pointer.box?view=net-7.0) and `UnBox()` must be used to manually box and unbox a pointer (`*`) type.
- When an exception occurs in the target method, the exception is wrapped with a `TargetInvocationException` and re-thrown. This approach is not desired in most cases and somewhat recently, `BindingFlags.DoNotWrapExceptions` was added to change this behavior as an opt-in. Not having a `try\catch` would help a bit with performance as well.

Due to the performance issues and usability issues, workarounds and alternatives are used including:
- [MethodInfo.CreateDelegate()](https://learn.microsoft.com/dotnet/api/system.reflection.methodinfo.createdelegate) which supports a direct method invocation and thus is strongly-typed and not appropriate for loosely-typed invoke scenarios.
- [Dynamic methods](https://learn.microsoft.com/dotnet/framework/reflection-and-codedom/how-to-define-and-execute-dynamic-methods) which are IL-emit based and include a non-trivial implementation.
- Compiled [expression trees](https://learn.microsoft.com/dotnet/csharp/programming-guide/concepts/expression-trees/) which use dynamic method if IL Emit is available but also conveniently fallback to standard reflection when IL Emit is not available.

# 8.0 Goals
The 7.0 release had a 3-4x perf improvement for `Invoke()` by using IL Emit when available and falling back to standard reflection when not available, keeping the same object-based APIs. Although this improvement is significant, it doesn't replace the need to use the IL-Emit based alternatives for the highly-performance-sensitive scenarios such as the `System.Text.Json` serializer. New APIs are required.

For 8.0, there are two primary goals:
1) Support byref-like types both for invoking and passing as arguments. An unsafe approach may need to be used for V8. This unblocks scenarios.
2) Support "fast invoke" so that dynamic methods no longer have to be used for performance. Today both `System.Text.Json` and [dependency injection](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection) use IL emit for performance. As a litmus test, these areas will be changed to use the new APIs proposed here.

# Design
In order to address the limitations and issues:
- A least-common-denominator approach of using stack-based "interior pointers" for the invoke parameters, target and return value. This supports `in\ref\out` aliasing and the various classifications of types including value types, reference types, pointer types and byref-like types (byref-like types may require additional Roslyn support). Essentially this is a different way to achieve "boxing" in order to have a common representation of any parameter type.
- Optionally, depending on final performance of the "interior pointer" approach, add support for creating an object-based delegate for properties and fields, like `MethodInfo.CreateDelegate()` but taking `object` as the target. This would be used for `System.Text.Json` and other areas that have strongly-typed `parameters` but a weakly-typed `target` and need maximum performance from that.

## Interior pointers
An interior pointer differs from a managed reference:
- Can only exist on the stack (not the heap)
- References an existing storage location, either a method parameter, a field, a variable, or an array element.
- To be GC-safe, either requires pinning the pointer or letting the runtime track it by various means (more on this below). This may require overhead during a GC when physical memory locations change, but that is somewhat rare due to the nature of it being stack-only.

See also [C++\CLI documentation](https://learn.microsoft.com/en-us/cpp/extensions/interior-ptr-cpp-cli?view=msvc-170).

An interior pointer is created in several ways:
- [System.TypedReference](https://learn.microsoft.com/en-us/dotnet/api/system.typedreference). Since this a byref-like type, it is only stack-allocated. Internally, it uses the `ref byte` approach below.
- Using `ref byte`. This is possible in 7.0 due to the new "ref field" support. Previously, there was an internal `ByReference<T>` class that was used. This `ref byte` approach is used today in reflection invoke when there are <=4 parameters.
- Using `IntPtr` with tracking or pinning. Currently, tracking is only supported internally through the use of a "RegisterForGCReporting()" mechanism. This approach is used today in reflection invoke when there are >=5 parameters.

This design proposes the use of `TypedReference` for the safe version of APIs, and either `ref byte` or `IntPtr` approaches for the unsafe version. The `ref byte` and `IntPtr` approaches require the use of pointers, through the internal `object.GetRawData()` and other means.

## TypedReference
The internal reflection implementation will not likely be based on `TypedReference` -- instead it will take, from public APIs, `TypedReference` instances and "peel off" the interior pointer as an `IntPtr` along with tracking to support GC. The use of `TypedReference` is essentially to not require the use of unsafe code in the public APIs. Unsafe pubic APIs will likely be added as well that should have slightly better performance characteristics.

### Existing usages
`TypedReference` is used in `FieldInfo` via [`SetValueDirect(TypeReference obj, object? value)`](https://learn.microsoft.com/dotnet/api/system.reflection.fieldinfo.setvaluedirect) and [`object? GetValueDirect(TypeReference obj)`](https://learn.microsoft.com/dotnet/api/system.reflection.fieldinfo.getvaluedirect). The approach taken by `FieldInfo` will be expanded upon in the design here. The existing approach supports the ability to get\set a field on a target value type without boxing. Boxing the target through through [`FieldInfo.SetValue()`](https://learn.microsoft.com/dotnet/api/system.reflection.fieldinfo.setvalue) instead is useless since the changes made to the boxed value type would not be reflected back to the original value type instance.

`TypedReference` is also used by the undocumented `__arglist` along with `System.ArgIterator`. The approach taken by `__arglist` will not be leveraged or expanded upon in this design. It would, however, allow a pseudo-strongly-typed approach like
```cs
string s = "";
int i = 1;
object o = null;
// This is kind of nice, but not proposed for 8.0:
methodInfo.Invoke(__arglist(s, ref i, o));
```

### API additions
Currently `TypedReference` is not designed for general use, although it has been part of the CLR since inception. It must be created through the undocumented `__makeref` C# keyword. There is also a `__refvalue` to obtain the value and a `__reftype` to obtain the underlying `Type`. The design here will add new APIs that are expected to be used instead of these keywords to make the APIs more mainstream and accessible from other languages.

### Byref-like (or `ref struct`) support
Currently, C# allows one to construct a `TypedReference` to a reference type or value type, but not a byref-like type:
```cs
int i = 42;
TypedReference tr1 = __makeref(i); // okay

Span<int> span = stackalloc int[42];
TypedReference tr2 = __makeref(span); // Error CS1601: Cannot make reference to variable of type 'Span<int>'
```

An 8.0 ask from Roslyn is to allow this to compile. See [C# ask for supporting TypedReference + byref-like types](https://github.com/dotnet/roslyn/issues/65255).

Since `TypedReference` internally stores a reference to the **storage location**, and not the actual value or managed-reference-passed-by-value, it effectively supports the `ref\out\in` modifiers without using the `ref` keyword. Attempting to use the `ref` keyword is not allowed:
```cs
int i = 42;
TypedReference tr = __makeref(ref i); // error CS1525: Invalid expression term 'ref'
```
and not necessary due to `__refvalue`:
```cs
int i = 42;
TypedReference tr = __makeref(i);
__refvalue(tr, int) = 100;
Console.WriteLine(i); // 100
```
or by using `ref` along with `__refvalue`:
```cs
int i = 42;
TypedReference tr = __makeref(i);
ChangeIt(tr);
Console.WriteLine(i); //100

public static void ChangeIt(TypedReference tr)
{
    ref int i = ref __refvalue(tr, int);
    i = 100;
}
```

### Byref-like support + simplification constraints
Below are some simplfication constraints that may help with any Roslyn implementation around lifetime rules.

**A `TypedReference` can't reference another `TypedReference`**
```cs
TypedReference tr = ...
TypedReference tr2 = __makeref(tr); //  error CS1601: Cannot make reference to variable of type 'TypedReference'
```

This is similar to trying to make a `TypedReference` to a `Span<int>` above -- both are byref-like types.

However, this limitation for `TypedReference-to-TypedReference` does **not** need to be removed for the goals in this design if it helps with simplifying lifetime rules.

**Just support what is allowed today.** No new capabilities are expected; reflection invokes existing methods which must have already been compiled according to existing rules:
```cs
internal class Program
{
    static void Main()
    {
        // Case 1
        MyRefStruct rs = default;
        TypedReference tr = __makeref(rs._span); // Assume we allow instead of CS1601
        ChangeIt(tr);

        // Case 2
        Span<int> heap = new int[42];
        Span<int> stack = stackalloc int[42];
        CallMe(ref stack, heap); // okay
        CallMe(ref heap, stack); // CS8350 as expected
    }

    public static void ChangeIt(TypedReference tr)
    {
        // This compiles today:
        ref Span<int> s = ref __refvalue(tr, Span<int>);

        // And can be assigned to default
        s = default;

        Span<int> newspan = stackalloc int[42];

        // But this causes CS8352 as expected:
        s = ref newspan;
    }

    public static void CallMe(ref Span<int> span1, Span<int> span2) { }
}

public ref struct MyRefStruct
{
    public Span<int> _span;
}
```

If allowed today in strongly-typed manner, then reflection should also allow:
```cs
internal class Program
{
    static void Main()
    {

        Span<int> span = stackalloc int[42];
        TypedReference tr = __makeref(span); // Assume we allow instead of CS1601

        // Using a proposed invoke API; calling should be supported passing byvalue
        MethodInfo mi1 = typeof(Program).GetMethod(nameof(ChangeIt1));
        mi1.InvokeDirect(target: default, arg1: tr);

        // and supported passing byref
        MethodInfo mi2 = typeof(Program).GetMethod(nameof(ChangeIt2));
        mi2.InvokeDirect(target: default, arg1: tr);

        // Just like these methods can be called today:
        ChangeIt1(span);
        ChangeIt2(ref span);
    }

    public static void ChangeIt1(Span<int> span) { }
    public static void ChangeIt2(ref Span<int> span) { }
}
```

FWIW `__arglist` currently compiles with byref-like types:
```cs
Span<byte> s1 = stackalloc byte[1];
Span<byte> s2 = stackalloc byte[11];
Span<byte> s3 = stackalloc byte[111];
CallMe(__arglist(s1, s2, s3));

static unsafe void CallMe(__arglist)
{
    // However, when enumerating __arglist here, the runtime throws when accessing a byref-like
    // type although that limitation could be removed (just a runtime limitation; not compiler)
```

## Variable-length, safe collections
(todo; see https://github.com/dotnet/runtime/issues/75349. _I have a local prototype that does a callback that doesn't require any new language or runtime features however it is not a clean programming model. The callback is necessary to interop with hide our internal GC "tracking" feature via "RegisterForGCReporting()"._)

## Exception handling
Todo; discuss using `BindingFlags.DoNotWrapExceptions` semantics only (no wrapping of exceptions).

# Proposed APIs
(work in progress; various prototypes exist here)
## TypedReference
```diff
namespace System
{
    public ref struct TypedReference
    {
        // Equivalent of __makeref (except for a byref-like type since they can't be a generic parameter)
+       public static TypedReference Make<T>(ref T? value);
        // Helper used for boxed or loosely-typed cases
+       public static TypedReference Make(ref object value, Type type);

        // Equivalent of __refvalue
+       public ref T GetValue<T>();
        // Used for byref-like types or loosely-typed cases with >4 parameters
+       public readonly unsafe ref byte TargetRef { get; }
    }
}
```

## MethodInfo
(these may be extension methods instead)

```diff
namespace System.Reflection
{
    public abstract class MethodBase
    {
        // Helpers for <= 4 parameters

        // Note that 'default(TypedReference)' can be specified for any "not used" arguments, such as for
        // static methods ('target' is 'default'), 'void'-returning methods ('result' is 'default') or
        // for any trailing unused arguments (e.g. 'arg4' can be 'default').

        // We may also want to consider using 'ref byte' instead of or in addition to 'TypedReference'
        // along with new helper methods like 'public static ref byte GetRawData(object o)'
+       [System.CLSCompliantAttribute(false)]
+       public virtual void InvokeDirect(
+           TypedReference target,
+           TypedReference result);

+       [System.CLSCompliantAttribute(false)]
+       public virtual void InvokeDirect(
+           TypedReference target,
+           TypedReference arg1,
+           TypedReference result);

+       [System.CLSCompliantAttribute(false)]
+       public virtual void InvokeDirect(
+           TypedReference target,
+           TypedReference arg1,
+           TypedReference arg2,
+           TypedReference result);

+       [System.CLSCompliantAttribute(false)]
+       public virtual void InvokeDirect(
+           TypedReference target,
+           TypedReference arg1,
+           TypedReference arg2,
+           TypedReference arg3,
+           TypedReference result);

+       [System.CLSCompliantAttribute(false)]
+       public virtual void InvokeDirect(
+           TypedReference target,
+           TypedReference arg1,
+           TypedReference arg2,
+           TypedReference arg3,
+           TypedReference arg4,
+           TypedReference result);

        // Unsafe (todo: more on this; samples)
+       public virtual unsafe void InvokeDirect(
+           TypedReference target,
+           TypedReference* parameters,
+           TypedReference result);

       // Also consider a safe variable-length callback that does tracking internally
       // since we don't support a safe variable-length collection mechanism
       // or the ability to use a Span<TypedReference>
       // (todo: more on this?; prototype exists)
    }
}
```

## PropertyInfo \ FieldInfo
```diff
namespace System.Reflection
{
    public abstract class PropertyInfo
    {
+       [System.CLSCompliantAttribute(false)]
        public virtual void GetValueDirect(TypedReference target, TypedReference result);

+       [System.CLSCompliantAttribute(false)]
        public virtual void SetValueDirect(TypedReference target, TypedReference value);

        // Possible for performance in System.Text.Json:
+       public virtual Func<object, TValue> CreateGetterDelegate<TValue>();
+       public virtual Action<object, TValue> CreateSetterDelegate<TValue>();
    }

    public abstract class FieldInfo
    {
+       [System.CLSCompliantAttribute(false)]
        public virtual void GetValueDirect(TypedReference target, TypedReference result);

+       [System.CLSCompliantAttribute(false)]
        public virtual void SetValueDirect(TypedReference target, TypedReference value);

        // Possible adds to get max performance in System.Text.Json:
+       public virtual Func<object, TValue> CreateGetterDelegate<TValue>();
+       public virtual Action<object, TValue> CreateSetterDelegate<TValue>();
    }
}
```
