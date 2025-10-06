
# Annotating members as `unsafe`

## Background

C# has had the `unsafe` feature since 1.0. There are two different syntaxes for the feature: a block syntax that can be used inside methods and a modifier that can appear on members and types. The original semantics only concern pointers. An error is produced if a variable of pointer type appears outside an unsafe context. For the block syntax, this is anywhere inside the block; for members this is inside the member; for types this is anywhere inside the type. Pointer operations are not fully validated by the type system, so this feature is useful at identifying areas of code needing more validation. Unsafe has subsequently been augmented to also turn off lifetime checking for ref-like variables, but the fundamental semantics are unchanged -- the `unsafe` context serves only to avoid an compiler error that would otherwise occur.

While existing `unsafe` is useful, it is limited by only applying to pointers and ref-like lifetimes. Many methods may be considered unsafe, but the unsafety may not be related to pointers or ref-like lifetimes. For example, almost all methods in the System.RuntimeServices.CompilerServices.Unsafe class have the same safety issues as pointers, but do not require an `unsafe` block. The same is true of the System.RuntimeServices.CompilerServices.Marshal class.

## Goals

The existing unsafe system does not clearly identify which methods need hand-verification to use safely, and it doesn't indicate which methods claim to provide that verification (and produce a safe API).

We want to achieve all of the following goals:

* Clearly annotate which methods require extra verification to use safely
* Clearly identify in source code which methods are responsible for performing extra verification
* Provide an easy way to identify in source code if a project doesn't use unsafe code

## Proposal

We need to be able to annotate code as unsafe, even if it doesn't use pointers.

Mechanically, this would be done with a modification to the C# language and a new property to the compilation. When the compilation property "EnableRequiresUnsafe" is set to true, the `unsafe` keyword on C# _members_ would require that their uses appear in an unsafe context. An `unsafe` block would be unchanged -- the statements in the block would be in an unsafe context, while the code outside would have no requirements.

For example, the code below would produce an error:

```C#
void Caller()
{
    M(); // error, the call to M() is not in an unsafe context
}

unsafe void M() { }
```

This can be addressed by callers in two ways:

```C#
unsafe void Caller1()
{
    M();
}
void Caller2()
{
    unsafe
    {
        M();
    }
}
unsafe void M() { }
```

In the case of `Caller1`, the call to `M()` doesn't produce an error because it is inside an unsafe context. However, calls to `Caller1` will now produce an error for the same reason as `M()`.

`Caller2` will also not produce an error because `M()` is in an unsafe context. However, this code creates a responsibility for the programmer: by presenting a safe API around an unsafe call, they are asserting that all safety concerns of `M()` have been addressed.

Notably, unsafe did not change the requirement that the code in the block must be correct. It merely offset the responsibility from the language and the runtime to the user in verification.

### Details

When the feature is enabled, the `unsafe` keyword will now only be allowed in the following places:

  * As a modifier in a method or local function declaration
  * As part of the "unsafe block" syntax
  * As a modifier on property declarations

As detailed below, pointer types themselves are no longer unsafe, only pointer dereferences. Therefore, `unsafe` is only necessary to annotate executable code.

## Implementation

In addition to compiler enforcement, the following attribute will be added for annotating unsafe members. It is an error to use this attribute directly in C#. Instead, the `unsafe` keyword should be used.

```C#
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Constructor)]
public sealed class RequiresUnsafeAttribute : System.Attribute
{
}
```

### Global invariants

The overall goal is to ensure .NET code is "valid," in the sense that certain properties are always true. Generating a complete list of such properties is out of scope of this document. However, at least the following properties are required:

* Memory safety
* No access to uninitialized memory

In this document **memory safety** is strictly defined as: code never accesses memory that is not managed by the runtime. "Managed" here does not refer to solely to heap-allocated, garbage collected memory, but also includes stack-allocated variables that are considered allocated by the runtime, or memory that is acquired for legal use by the runtime through any other means.

No access to uninitialized memory means that all memory is either never read before it has been initialized, or it has been initialized to a zero value. "Uninitialized memory" means memory which contains an undefined value. Memory that has been zeroed by the runtime will always have a defined zero value. Memory initialized by the user will have a defined value if the user code has defined behavior. Memory subject to race conditions in .NET managed code has multiple potential defined values, which is considered safe.

Therefore, allocating zeroed memory using .NET safe methods is considered safe. This would include things like the .NET `new` operator and `NativeMemory.AllocZeroed`. Reading memory from a function which does not guarantee zero initialization would not, like `NativeMemory.Alloc`. Reusing memory that has previously been initialized but now may hold an unknown value due to previous application execution is considered safe. This includes object pooling functions. Bugs concerning incorrect reuse of pooled objects are not considered memory safety issues and the `unsafe` feature will not provide further protection against these vulnerabilities.

These properties are particularly important for security purposes. Any methods that can potentially violate these guarantees should be unsafe. Additionally, all such methods should have documentation that describes what conditions need to be satisfied to ensure that these guarentees are preserved.

These properties are guaranteed by "safe" code through a combination of compiler and .NET runtime enforcement. For `unsafe` code, these properties must be guaranteed by the programmer.

`unsafe` members are used to identify the places that cannot be automatically checked by the compiler and runtime for validity. Inside unsafe blocks, the programmer is responsible for ensuring that all requirements of the unsafe code are met, and that all code outside the block will have validity properly enforced by the system.


### Examples and APIs

**ArrayPool.Rent**

This method is unsafe because it returns an array with unintialized memory. Code must not read the contents of the returned array without initialization.

**stackalloc**

This language feature is unsafe if used in a `SkipLocalsInit` context because the stack allocated buffer is uninitialized. If an initializer is used and the converted type is `Span<T>`, this code is safe.

**P/Invoke**

All P/Invoke methods are unsafe because they may compromise memory safety if the callee function does not match the P/Invoke method specification.
