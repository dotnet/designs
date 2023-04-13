# Reflection Invoke for 8.0 (draft \ in progress)
#### Steve Harter
#### April 13, 2023

# Background
For additional context, see:
- [Developers using reflection invoke should be able to use ref struct](https://github.com/dotnet/runtime/issues/45152)
- [Originally library issue; needs refreshing\updating](https://github.com/dotnet/runtime/issues/10057)

The invoke APIs and capabilities have essentially remained the same since inception of .NET Framework with the primary API being [`MethodBase.Invoke`](https://learn.microsoft.com/dotnet/api/system.reflection.methodbase.invoke):
```cs
public object? Invoke(object? obj, object?[]? parameters);

public object? Invoke(
    object? obj,
    BindingFlags invokeAttr,
    Binder? binder,
    object?[]? parameters,
    CultureInfo? culture);
```
which is implemented by [MethodInfo](https://learn.microsoft.com/dotnet/api/system.reflection.methodinfo) and [ConstructorInfo](https://learn.microsoft.com/dotnet/api/system.reflection.constructorinfo).

Properties expose their `get` and `set` accessors from `PropertyInfo` via [`GetMethod()`](https://learn.microsoft.com/dotnet/api/system.reflection.propertyinfo.getmethod) and [`SetMethod()`](https://learn.microsoft.com/dotnet/api/system.reflection.propertyinfo.setmethod).

Unlike properties, fields do not expose `MethodInfo` acessors since since there is no invokable code around field access. Instead, fields expose their `get` and `set` accessors from [`object? FieldInfo.GetValue(object? obj)`](https://learn.microsoft.com/dotnet/api/system.reflection.fieldinfo.getvalue) and [`FieldInfo.SetValue(object? obj, object? value)`](https://learn.microsoft.com/dotnet/api/system.reflection.fieldinfo.setvalue).

The `Invoke()` APIs are easy to use and flexible:
- Based on `System.Object` to support both reference types (as a base type) and value types (through boxing).
    - Note that boxing does an automatic `Nullable<T>` to `null`.
- Automatic conversions:
    - Implicit casts between primitives such as from `int` to `long`.
    - `Enum` to\from its underlying type.
    - Pointer (*) types to\from `IntPtr`.

However the object-based Invoke() is not very performant:
- Boxing is required for value types.
  - A box requires a heap allocation and associated GC overhead.
  - A cast is required during unbox, although unbox is fast since it doesn't allocate.
- An `object[]` must be allocated (or manually cached by the caller) for the `parameters` argument.
- The automatic conversions add overhead.
- `ref` and `out` parameters require overhead after the invoke due to re-assignment (or "copy back") to the `parameters` argument.
  - `Nullable<T>` is particularly expensive due to having to convert the boxed `null` to `Nullable<T>` in order to invoke methods with `Nullable<T>`, and when used with `ref` \ `out`, having to box `Nullable<T>` after invoke to "copy back" to the `parameters` argument.
- The additional invoke arguments (`BindingFlags`, `Binder`, `CultureInfo`) add overhead even when not used. Plus using those is quite rare with the exception of `BindingFlags.DoNotWrapExceptions`, which is the proposed behavior for the new APIs proposed here.
  
and has limitations and issues:
- Cannot be used with byref-like types like `Span<T>` either as the target or an argument. This is because by-ref like types cannot be boxed. **This is the key limitation expressed in this document.**
- `ref` and `out` parameters are retrieved after `Invoke()` through the `parameters` argument. This is a manual mechanism performed by the user and means there is no argument or return value "aliasing" to the original variable.
- Boxing of value types makes it impossible (without using work-arounds) to invoke a mutable method on a value type, such as a property setter, and have the target `obj` updated.
- [`System.Reflection.Pointer.Box()`](https://learn.microsoft.com/dotnet/api/system.reflection.pointer.box) and `UnBox()` must be used to manually box and unbox a pointer (`*`) type.
- When an exception originates within the target method during invoke, the exception is wrapped with a `TargetInvocationException` and re-thrown. In hindsight, this approach is not desired in most cases. Somewhat recently, the `BindingFlags.DoNotWrapExceptions` flag was added to change this behavior as an opt-in. Not having a `try\catch` would help a bit with performance as well.

Due to the performance and usability issues, workarounds and alternatives are used including:
- [MethodInfo.CreateDelegate()](https://learn.microsoft.com/dotnet/api/system.reflection.methodinfo.createdelegate) or [`Delegate.CreateDelegate()`](https://learn.microsoft.com/dotnet/api/system.delegate.createdelegate) which supports a direct, fast method invocation. However, since delegates are strongly-typed, this approach does not work for loosely-typed invoke scenarios where the signature is not known at compile-time.
- [`System.TypedReference`](https://learn.microsoft.com/dotnet/api/system.typedreference) along with `MakeTypedReference()` or `__makeref` can be used to modify a field or nested field directly. The `FieldInfo.SetValueDirect()` and `GetValueDirect()` can be used with `TypedReference` to get\set fields without boxing the value (but still boxes the target since that is still `object`).
 - [Dynamic methods](https://learn.microsoft.com/dotnet/framework/reflection-and-codedom/how-to-define-and-execute-dynamic-methods) are used which are IL-emit based and require a non-trivial implementation for even simple things like setting property values. Those who use dynamic methods must have their own loosely-typed invoke APIs, which may go as far as using generics with `Type.MakeGenericType()` or `MethodInfo.MakeGenericMethod()` to avoid boxing. In addition, a fallback to standard reflection is required to support those platforms where IL Emit is not available.
- Compiled [expression trees](https://learn.microsoft.com/dotnet/csharp/programming-guide/concepts/expression-trees/) which use dynamic methods if IL Emit is available but also conveniently falls back to standard reflection when not available. Using an expression to invoke a member isn't intuitive and brings along the large `System.Linq.Expressions.dll` assembly.

# .NET 8 Goals
The .NET 7 release had a 3-4x perf improvement for the existing `Invoke()` APIs by using IL Emit when available and falling back to standard reflection when IL Emit is not available. Although this improvement is significant, it still doesn't replace the need to use the IL-Emit based alternatives (dynamic methods and expression trees) for highly-performance-sensitive scenarios including the `System.Text.Json` serializer. New APIs are required that don't have the overhead of the existing `Invoke()` APIs.

For .NET 8, there are two primary goals:
1) Support byref-like types both for invoking and passing as arguments; this unblocks various scenarios. An unsafe approach may used for .NET 8 if support for `TypedReference` isn't addressed by Roslyn (covered later).
2) Support "fast invoke" so that using IL Emit with dynamic methods has little to no performance advantage. Today both `STJ (System.Text.Json)` and [DI (dependency injection)](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection) use IL Emit for performance although DI uses emit through expressions while STJ uses IL Emit directly.
    - Although the new APIs will be zero alloc (no `object` and boxing required) and performace faster than standard reflection, it will not necessarily be as fast as hand-coded IL Emit for specific scenarios that can optimize for any constraints such as fixed-size list of parameters or by not doing full defaulting and validation of values. Note that property get\set is a subset of this case since there is either a return value (for _get_) or a single parameter for _set_ and since property get\set is such a common case with serialization, this design does propose a separate API for this common case to enable maximum performance.

# Design with managed pointers
In order to address the limitations and issues, a least-common-denominator approach of using _managed pointers_ for the parameters, target and return value. This supports `in\ref\out` aliasing and the various classifications of types including value types, reference types, pointer types and byref-like types. Essentially this is a different way to achieve "boxing" in order to have a single representation of any parameter.

However, since managed pointers require a reference to a storage location, they does not directly support the same loosely-coupled scenarios as using `object` + boxing for value types. The proposed APIs, however, do make interacting with `object` possible with the new API for the cases that do not require `in\ref\out` variable aliasing for example.

Today, a managed pointer is obtained safely through the `ref` keyword in C#. It references a storage location of an object or value type which can be a stack variable, static variable, parameter, field or array element. If the storage location is a field or array element, the managed pointer is referred to as an "interior pointer" which is supported by GC meaning that GC won't collect the owning object even if there are only interior pointers to it.

A managed pointer can becreated in several ways:
- [System.TypedReference](https://learn.microsoft.com/en-us/dotnet/api/system.typedreference). Since this a byref-like type, it is only stack-allocated. Internally, it uses a `ref byte` approach along with a reference to a `Type`. Note `TypeReference` is a special type and has its own opcodes (mkrefany, refanytype, refanyval) which translate to C# keyworkds (`__makeref`, `__reftype`, `__refvalue`).
- Using `ref <T>` for the strongly typed case or `ref byte` for the loosely-typed case. This is expanded in 7.0 due to the new "ref field" support. Previously, there was an internal `ByReference<T>` class that was used and in 7.0 this was changed to `ByReference` in 8.0 which no longer maintains the `<T>` type and internally just contains `ref byte`. This `ByReference` type is used today in reflection invoke when there are <=4 parameters.
- Using unsafe `void*` (or `IntPtr`) with GC tracking or pinning to make the use GC-safe. Tracking is supported internally through the use of a newer "RegisterForGCReporting()" mechanism. This approach is used today in reflection invoke when there are >=5 parameters.

# Proposed APIs

## MethodInvoker
This ref struct is the mechanism to specify the target + arguments (including return value) and supports these mechanisms:
- `object` (including boxing). Supports loose coupling scenarios are supported, like reflection today.
- `ref <T>`. Supports new scenarios as mentioned earlier; type must be known ahead-of-time and due to no language support, cannot be a byref-like type like `Span<T>`.
- `void*`. Unsafe cases used to support byref-like types in an unsafe manner.
- `TypedReference`. Optional for now; pending language asks, it may make supporting byref-like types a safe operation.

```cs
namespace System.Reflection
{
     public ref struct MethodInvoker
     {
        // Zero-arg case:
        public MethodInvoker()

        // Variable-length number of arguments:
        public unsafe MethodInvoker(ArgumentValue* argumentStorage, int argCount)

        // Fixed length (say up to 8)
        public MethodInvoker(ref ArgumentValuesFixed values)

        // Dispose needs to be called with variable-length case
        public void Dispose()

        // Target
        public object? GetTarget()
        public ref T GetTarget<T>()

        public void SetTarget(object value)
        public void SetTarget(TypedReference value)
        public unsafe void SetTarget(void* value, Type type)
        public void SetTarget<T>(ref T value)

        // Arguments
        public object? GetArgument(int index)
        public ref T GetArgument<T>(int index)

        public void SetArgument(int index, object? value)
        public void SetArgument(int index, TypedReference value)
        public unsafe void SetArgument(int index, void* value, Type type)
        public void SetArgument<T>(int index, ref T value)

        // Return
        public object? GetReturn()
        public ref T GetReturn<T>()

        public void SetReturn(object value)
        public void SetReturn(TypedReference value)
        public unsafe void SetReturn(void* value, Type type)
        public void SetReturn<T>(ref T value)

        // Invoke direct (limited validation and defaulting)
        public unsafe void InvokeDirect(MethodBase method)

        // Invoke (same validation and defaulting as reflection today)
        public void Invoke(MethodBase method)
     }

     // This is used to define the correct storage requirements for the MethodInvoker variable-length cases.
     // Internally it is based on 3 IntPtrs that are for 'ref', 'object value' and 'Type':
     // - 'ref' points to either the 'value' location or a user-provided location with "void*", "ref <T>" or TypedReference.
     // - 'object value' captures any user-provided object value in a GC-safe manner.
     // - 'Type' is used in "void*", "ref <T>" and TypedReference cases for validation and in rare cases to prevent
     //    Types from being GC'd.
     public struct ArgumentValue { }
```

## ArgumentValuesFixed
This class is used for cases where the known arguments are small.

```cs
namespace System.Reflection
{
    public ref partial struct ArgumentValuesFixed
    {
        public const int MaxArgumentCount; // 8 shown here (pending perf measurements to find optimal value) 
        
        // Used when non-object arguments are specified later.
        public ArgumentValuesFixed(int argCount)
        
        // Fastest way to pass objects:
        public ArgumentValuesFixed(object? obj1)
        public ArgumentValuesFixed(object? obj1, object? o2) // ("obj" not "o" assume for naming)
        public ArgumentValuesFixed(object? obj1, object? o2, object? o3)
        public ArgumentValuesFixed(object? obj1, object? o2, object? o3, object? o4)
        public ArgumentValuesFixed(object? obj1, object? o2, object? o3, object? o4, object? o5)
        public ArgumentValuesFixed(object? obj1, object? o2, object? o3, object? o4, object? o5, object? o6)
        public ArgumentValuesFixed(object? obj1, object? o2, object? o3, object? o4, object? o5, object? o6, object? o7)
        public ArgumentValuesFixed(object? obj1, object? o2, object? o3, object? o4, object? o5, object? o6, object? o7, object? o8)
    }
}
```

## TypedReference
This is currently optional and being discussed. If `TypedReference` ends up supporting references to byref-like types like `Span<T>` then it will be much more useful otherwise just the existing `ref <T>` API can be used instead. The advantage of `TypedReference` is that it does not require generics so it can be made to work with `Span<T>` easier than adding a feature that would allowing generic parameters to be a byref-like type.

To avoid the use of C#-only "undocumented" keywords, wrappers for `__makeref`, `__reftype`, `__refvalue` which also enable other languages.
```diff
namespace System
{
    public ref struct TypedReference
    {
        // Equivalent of __makeref except for a byref-like type since they can't be a generic parameter - see
        // see https://github.com/dotnet/runtime/issues/65112 for reference.
+       public static TypedReference Make<T>(ref T? value);
+       public static unsafe TypedReference Make(Type type, void* value);
        // Helper used for boxed or loosely-typed cases
+       public static TypedReference Make(ref object value, Type type);

        // Equivalent of __refvalue
+       public ref T GetValue<T>();

        // Equivalent of __reftype
+       public Type Type { get; };
    }
}
```

## PropertyInfo \ FieldInfo
For `PropertyInfo`, this is an alternative of using more heavy-weight `MethodInvoker`. For `FieldInfo`, this expands on the existing `Set\GetValueDirect` to also use `TypedReference` for the `value`.

```diff
namespace System.Reflection
{
    public abstract class PropertyInfo
    {
+       [System.CLSCompliantAttribute(false)]
        public virtual void GetValueDirect(TypedReference obj, TypedReference result);

+       [System.CLSCompliantAttribute(false)]
        public virtual void SetValueDirect(TypedReference obj, TypedReference value);

        // Possible for performance in System.Text.Json:
+       public virtual Func<object, TValue> CreateGetterDelegate<TValue>();
+       public virtual Action<object, TValue> CreateSetterDelegate<TValue>();
    }

    public abstract class FieldInfo
    {
+       [System.CLSCompliantAttribute(false)]
        public virtual void GetValueDirect(TypedReference obj, TypedReference result);

+       [System.CLSCompliantAttribute(false)]
        public virtual void SetValueDirect(TypedReference obj, TypedReference value);

        // Possible for performance in System.Text.Json:
+       public virtual Func<object, TValue> CreateGetterDelegate<TValue>();
+       public virtual Action<object, TValue> CreateSetterDelegate<TValue>();
    }
}
```

## Examples
### Fixed-length arguments
```cs
MethodInfo method = ... // Some method to call
ArgumentValuesFixed values = new(4); // 4 parameters
InvokeContext context = new InvokeContext(ref values);
context.SetArgument(0, new MyClass());
context.SetArgument(1, null);
context.SetArgument(2, 42);
context.SetArgument(3, "Hello");

// Can inspect before or after invoke:
object o0 = context.GetArgument(0);
object o1 = context.GetArgument(1);
object o2 = context.GetArgument(2);
object o3 = context.GetArgument(3);

context.InvokeDirect(method);
int ret = (int)context.GetReturn();
```

### Fixed-length object arguments (faster)
```cs
ArgumentValuesFixed args = new(new MyClass(), null, 42, "Hello");
InvokeContext context = new InvokeContext(ref args);
context.InvokeDirect(method);
```

### Variable-length object arguments
Unsafe and slightly slower than fixed-length plus requires `using` or `try\finally\Dispose()`.
```cs
unsafe
{
    ArgumentValue* args = stackalloc ArgumentValue[4];
    using (InvokeContext context = new InvokeContext(ref args))
    {
        context.SetArgument(0, new MyClass());
        context.SetArgument(1, null);
        context.SetArgument(2, 42);
        context.SetArgument(3, "Hello");
        context.InvokeDirect(method);
    }
}
```

### Avoiding boxing
Value types can be references to avoid boxing.

```cs
int i = 42;
int ret = 0;
ArgumentValuesFixed args = new(4);
InvokeContext context = new InvokeContext(ref args);
context.SetArgument(0, new MyClass());
context.SetArgument(1, null);
context.SetArgument<int>(2, ref i); // No boxing (argument not required to be byref)
context.SetArgument(3, "Hello");
context.SetReturn<int>(ref ret); // No boxing; 'ret' variable updated automatically
context.InvokeDirect(method);
```

### Pass a `Span<T>` to a method
```cs
Span<int> span = new int[] { 42, 43 };
ArgumentValuesFixed args = new(1);

unsafe
{
    InvokeContext context = new InvokeContext(ref args);
#pragma warning disable CS8500    
    void* ptr = (void*)new IntPtr(&span);
#pragma warning restore CS8500    
    // Ideally we can use __makeref(span) instead of the above.

    context.SetArgument(0, ptr, typeof(Span<int>));
    context.InvokeDirect(method);
}
```
# Design ext
## STJ and DI
As a litmus test, STJ and DI will be changed (or prototyped) to use the new APIs proposed here. This is more important to DI since, unlike STJ which has a source generator that can avoid reflection, DI is better suited to reflection than source generation. See also https://github.com/dotnet/runtime/issues/66153 which should be addressed by having a fast constructor invoke that can be used by DI.

### STJ use of reflection
See the [source for the non-emit strategy](https://github.com/dotnet/runtime/blob/3f0106aed2ece86c56f9f49f0191e94ee5030bff/src/libraries/System.Text.Json/src/System/Text/Json/Serialization/Metadata/ReflectionMemberAccessor.cs) which includes:
- [`Activator.CreateInstance(Type type, nonPublic: false)`](https://learn.microsoft.com/dotnet/api/system.activator.createinstance?#system-activator-createinstance(system-type-system-boolean)). Note that this is used instead of `ConstructorInfo` for zero-parameter public constructors since it is already super fast and does not use IL Emit.
- [`ConstructorInfo.Invoke(object?[]?)`](https://learn.microsoft.com/dotnet/api/system.reflection.constructorinfo.invoke?#system-reflection-constructorinfo-invoke(system-object())) for binding to an explicitly selected constructor during deserialization for cases where property setters or fields are not present.
- [`MethodBase.Invoke(object? obj, object?[]? parameters)`](https://learn.microsoft.com/dotnet/api/system.reflection.methodbase.invoke?view=system-reflection-methodbase-invoke(system-object-system-object())) for property get\set.
- [`FieldInfo.GetValue(object? obj)`](https://learn.microsoft.com/dotnet/api/system.reflection.fieldinfo.getvalue).
- [`FieldInfo.SetValue(object? obj, object? value)`](https://learn.microsoft.com/dotnet/api/system.reflection.fieldinfo.setvalue).

### DI use of reflection
- [`Array.CreateInstance(Type elementType, int length`](https://learn.microsoft.com/en-us/dotnet/api/system.array.createinstance?view=net-7.0#system-array-createinstance(system-type-system-int32)) via the [source](https://github.com/dotnet/runtime/blob/5b8ebeabb32f7f4118d0cc8b8db28705b62469ee/src/libraries/Microsoft.Extensions.DependencyInjection/src/ServiceLookup/CallSiteRuntimeResolver.cs#L165).
- [`ConstructorInfo.Invoke(BindingFlags.DoNotWrapException, binder: null, object?[]?, culture:null)`](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.constructorinfo.invoke?view=net-7.0#system-reflection-constructorinfo-invoke(system-reflection-bindingflags-system-reflection-binder-system-object()-system-globalization-cultureinfo)) via the [source](https://github.com/dotnet/runtime/blob/5b8ebeabb32f7f4118d0cc8b8db28705b62469ee/src/libraries/Microsoft.Extensions.DependencyInjection/src/ServiceLookup/CallSiteRuntimeResolver.cs#L69).

### Byref-like (or `ref struct`) support
Currently, C# allows one to construct a `TypedReference` to a reference type or value type, but not a byref-like type:
```cs
int i = 42;
TypedReference tr1 = __makeref(i); // okay

Span<int> span = stackalloc int[42];
TypedReference tr2 = __makeref(span); // Error CS1601: Cannot make reference to variable of type 'Span<int>'
```

An 8.0 ask from Roslyn is to allow this to compile. See [C# ask for supporting TypedReference + byref-like types](https://github.com/dotnet/roslyn/issues/65255).

Since `TypedReference` internally stores a reference to the **storage location**, and not the actual value or managed-reference-passed-by-value, it effectively supports the `ref\out\in` modifiers. Also, it does this with an implicit `ref` - attempting to use the `ref` keyword is not allowed:
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

## Byref-like support + simplification constraints
Below are some simplification constraints that may help with any Roslyn implementation around lifetime rules.

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

Wherever `__makeref` is allowed today then it should also support byref-like types:
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

FWIW `__arglist` (only supported on Windows) currently compiles with byref-like types:
```cs
Span<byte> s1 = stackalloc byte[1];
Span<byte> s2 = stackalloc byte[11];
Span<byte> s3 = stackalloc byte[111];
CallMe(__arglist(s1, s2, s3));

static unsafe void CallMe(__arglist)
{
    // However, when enumerating __arglist here, the runtime throws when accessing a byref-like
    // type although that limitation is easily fixable on Windows (just a runtime limitation; not compiler)
```


# Future
Holding area of features discussed but not planned yet.

## Variable-length, safe collections
The API proposal below does have a variable-lenth stack-only approach that uses an internal GC tracking mechanism. A easier-to-pass or callback version is not expected in 8.0; see https://github.com/dotnet/runtime/issues/75349.

## `__arglist`
`TypedReference` is also used by the undocumented `__arglist` along with `System.ArgIterator` although `__arglist` is Windows-only. The approach taken by `__arglist` will not be leveraged or expanded upon in this design. It would, however, allow a pseudo-strongly-typed approach like
```cs
string s = "";
int i = 1;
object o = null;
// This is kind of nice, but not proposed for 8.0:
methodInfo.Invoke(__arglist(s, ref i, o));
```
