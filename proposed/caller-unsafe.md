
# Annotating members as `unsafe`

## Background

C# has had the `unsafe` feature since 1.0. There are two different syntaxes for the feature: a block syntax that can be used inside methods and a modifier that can appear on members and types. The original semantics only concern pointers. An error is produced if a variable of pointer type appears outside an unsafe context. For the block syntax, this is anywhere inside the block; for members this is inside the member; for types this is anywhere inside the type. Pointer operations are not fully validated by the type system, so this feature is useful at identifying areas of code needing more validation. Unsafe has subsequently been augmented to also turn off lifetime checking for ref-like variables, but the fundamental semantics are unchanged -- the `unsafe` context serves only to avoid a compiler error that would otherwise occur.

While existing `unsafe` is useful, it is limited by only applying to pointers and ref-like lifetimes. Many methods may be considered unsafe, but the unsafety may not be related to pointers or ref-like lifetimes. For example, almost all methods in the System.RuntimeServices.CompilerServices.Unsafe class have the same safety issues as pointers, but do not require an `unsafe` block. The same is true of the System.RuntimeServices.CompilerServices.Marshal class.

## Goals

The overall goal is to ensure .NET code is "valid" with respect to certain properties, namely:

* Memory safety
* No access to uninitialized memory

The complete definition of these properties is in [Global invariants](#global-invariants).

The existing unsafe system does not clearly identify which methods need hand-verification to ensure these properties are preserved, and it doesn't indicate which methods claim to provide that verification (and produce a safe API).

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

`unsafe` will also be allowed on field declarations. This allows code to indicate that part of an unsafe contract is carried within a field. For example, the following simplified code appears in the dotnet/runtime core framework:

```C#
public class ArrayWrapper<T>
{
    private Array _array;

    public T GetItem(int index)
    {
        var typedArray = Unsafe.As<T[]>(ref _array);
        return typedArray[index];
    }
}
```

The important details in the above code are:

1. `Unsafe.As` will be an unsafe function, and therefore require an unsafe context
2. The `ArrayWrapper<T>` type and `GetItem` are intended to be safe. The use of `Unsafe.As` is intended to be an interior implementation detail.
3. The `GetItem` method relies on `_array` always having the runtime type of `T[]` for safety. The use of the `Array` type and `Unsafe.As` is a performance optimization to avoid generic specialization costs and conversion costs.

Knowing the above, `_array` is a sensitive contract location -- modifying it to contain a value without type `T[]` produces undefined behavior. In this case, the use of `unsafe` on a field is appropriate. The effect would be to require an unsafe context for accessing a field. `unsafe` on a field indicates that the field carries a contract, so any access to the field may require additional verification to prevent memory access violations.

### Details

When the feature is enabled, the `unsafe` keyword will now only be allowed in the following places:

  * As a modifier in a method or local function declaration
  * As part of the "unsafe block" syntax
  * As a modifier on property declarations
  * As a modifier on field declarations

As detailed below, pointer types themselves are no longer unsafe, only pointer dereferences.

## Implementation

In addition to compiler enforcement, the following attribute will be added for annotating unsafe members. It is an error to use this attribute directly in C#. Instead, the `unsafe` keyword should be used.

```C#
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Constructor)]
public sealed class RequiresUnsafeAttribute : System.Attribute
{
}
```

### Global invariants

Two properties which should always hold in .NET programs are:

* Memory safety
* No access to uninitialized memory

The "safe" subset of C# must guarantee these properties by construction. The unsafe subset cannot be guaranteed entirely by the system -- it needs external validation by the user or other tooling.

In this document **memory safety** is strictly defined as: code never accesses memory that is not managed by the runtime. "Managed" here does not refer to solely to heap-allocated, garbage collected memory, but also includes stack-allocated variables that are considered allocated by the runtime, or memory that is acquired for legal use by the runtime through any other means.

No access to uninitialized memory means that all memory is either never read before it has been initialized, or it has been initialized to a zero value. "Uninitialized memory" means memory which contains an undefined value. Memory that has been zeroed by the runtime will always have a defined zero value. Memory initialized by the user will have a defined value if the user code has defined behavior. Memory subject to race conditions in .NET managed code has multiple potential defined values, which is considered safe.

Therefore, allocating zeroed memory using .NET safe methods is considered safe. This would include things like the .NET `new` operator and `NativeMemory.AllocZeroed`. Reading memory from a function which does not guarantee zero initialization would not, like `NativeMemory.Alloc`. Reusing memory that has previously been initialized but now may hold an unknown value due to previous application execution is considered safe. This includes object pooling functions. Bugs concerning incorrect reuse of pooled objects are not considered memory safety issues and the `unsafe` feature will not provide further protection against these vulnerabilities.

These properties are particularly important for security purposes. Any methods that can potentially violate these guarantees should be unsafe. Additionally, all such methods should have documentation that describes what conditions need to be satisfied to ensure that these guarentees are preserved.

These properties are guaranteed by "safe" code through a combination of compiler and .NET runtime enforcement. For `unsafe` code, these properties must be guaranteed by the programmer.

`unsafe` members are used to identify the places that cannot be automatically checked by the compiler and runtime for validity. Inside unsafe blocks, the programmer is responsible for ensuring that all requirements of the unsafe code are met, and that all code outside the block will have validity properly enforced by the system.

### Non-goals

The new definition of `unsafe` is centered around memory safety, specifically ensuring access to valid memory and avoiding memory corruption. There are some properties that may be desirable but are not covered by memory safety. This includes:

- Type safety, specifically type system violations that can occur due to data races. All data races that result in memory safety problems *are* covered, but not data races that would produce invalid data combinations. For example, consider the following struct:

```C#
struct S
{
    public int X, Y;

    public S(int x)
    {
        this.X = X;
        this.Y = X * 2;
    }
}
```

According to the above definition, there are two valid constructors: the explicit constructor `S(int x)` and the default zero-constructor for structs. Therefore, we would always expect the equation `s.Y == s.X * 2` to hold. Because of data races, this is not true. When assigning fields of type `S` the subfields `X` and `Y` are not assigned atomically, so many alternate combinations could be witnessed between assignments.

These data races are considered fundamental to the .NET type system. The safe subset of C# does not protect against them.

- Resource lifetime. Some code patterns, like object pools, require manual lifetime management. When this management is done incorrectly bad behaviors can occur, including improper memory reuse. Notably, none of those behaviors include invalid memory access, although it can include symptoms that look like memory corruption. Because invalid memory access is not possible, this is considered safe.

### Evolution

This proposal details changes to the existing C# unsafe syntax to reconsider the meaning and provide new benefits. The question is whether we plan to change this in the future to incorporate a new meaning or more coverage and how users will need to respond to such potential changes.

There are two types of changes we could make that would impact users:

1. Language/semantic changes
2. Adding annotations in libraries
3. Unsafe in source generation

Regarding language and semantic changes, [Non-goals](#non-goals) covers features which are considered safe and not covered by the proposal. These would be the likeliest area of semantic change as they are known problematic patterns. However, there are multiple reasons why these cases are explicitly out of scope.

Preventing all possible data races in .NET would be a tremendous task and would be a hugely incompatible change. Any such change would be far beyond the currently proposed impact of unsafe and would be very unlikely to fit into any future evolutions either.

For resource lifetime, it would also be a large change, but more practical. However, it would also be a new feature. Mere code annotation is not enough to ensure lifetime correctness -- a full lifetime tracking feature is necessary. Given that a new feature would be needed, we can use that feature to circumscribe the safe bounds.

The conclusion is that altering the `unsafe` feature is not practical or desirable. Significant new features can be planned and implemented separately, with appropriate user opt-in points.

The second type of changes would be adding annotations, meaning adding the `unsafe` keyword to core libraries. Unlike changing the meaning of `unsafe`, these changes would be narrowly scoped to the location of the additional annotation. Nonetheless, they would result in compiler errors or warnings, meaning they would be considered breaking changes. Making specific breaking changes of this kind is not unheard of in .NET, but must be carefully considered. All together, we expect a high density of these changes in the first release when the feature is first introduced, then an exponential decay of such changes afterwards, as most of needed annotations are flushed through the system. This process will be replicated slowly throughout the ecosystem as new libraries update and see new changes.

The last notable area of impact is source generation. Most users will only see changes to `unsafe` when updating libraries that have new unsafe annotations. The major exception to that is source generation, where the changes to the semantics of method bodies will be visible directly. The other limitation of source generation is that, unlike library updates, the problem cannot be fixed by reverting a reference update.

One concession we could make to source generation would be to allow more fine-grained enabling and disabling of the warning scope. This proposal does **not** recommend such configuration switches. The `unsafe` feature is specifically designed to catch dangerous situations and source generation does not eliminate that risk. Therefore our recommendation would be to **avoid enabling the feature** until all source generators are updated to produce compatible code.

### Examples and APIs

**ArrayPool.Rent**

This method is unsafe because it returns an array with unintialized memory. Code must not read the contents of the returned array without initialization.

**stackalloc**

This language feature is unsafe if used in a `SkipLocalsInit` context because the stack allocated buffer is uninitialized. If an initializer is used and the converted type is `Span<T>`, this code is safe.

**P/Invoke**

All P/Invoke methods are unsafe because they may compromise memory safety if the callee function does not match the P/Invoke method specification.
