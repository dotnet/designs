
# Annotating unsafe code with CallerUnsafe

## Background

C# has had the `unsafe` feature since 1.0. There are two different syntaxes for the feature: a block syntax that can be used inside methods and a modifier that can appear on members and types. The original semantics only concern pointers. An error is produced if a variable of pointer type appears outside an unsafe context. For the block syntax, this is anywhere inside the block; for members this is inside the member; for types this is anywhere inside the type. Pointer operations are not fully validated by the type system, so this feature is useful at identifying areas of code needing more validation. Unsafe has subsequently been augmented to also turn off lifetime checking for ref-like variables, but the fundamental semantics are unchanged -- the `unsafe` context serves only to avoid an error that would otherwise occur.

While existing `unsafe` is useful, it is limited by only applying to pointers and ref-like lifetimes. Many methods may be considered unsafe, but the unsafety may not be related to pointers or ref-like lifetimes. For example, almost all methods in the System.RuntimeServices.CompilerServices.Unsafe class has the same safety issues as pointers, but do not require an `unsafe` block. The same is true of the System.RuntimeServices.CompilerServices.Marshal class.

## Goals

The existing unsafe system does not clearly identify which methods need hand-verification to use safely, and it doesn't indicate which methods claim to provide that verification (and produce a safe API).

We want to achieve all of the following goals:

* Clearly annotate which methods require extra verification to use safely
* Clearly identify in source code which methods are responsible for performing extra verification
* Provide an easy way to identify in source code if a project doesn't use unsafe code

## Proposal

We need to be able to annotate code as unsafe, even if it doesn't use pointers.

Mechanically, this would be done with a new attribute:

```C#
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Constructor)]
public sealed class CallerUnsafe : System.Attribute
{
    public WarnByDefault { get; init; } = true;
}
```

This would be the dual of the existing `unsafe` context. The `unsafe` context eliminates unsafe errors, indicating that the code outside of the unsafe context should be considered safe. The `CallerUnsafe` attribute does the opposite: it indicates that the code inside is unsafe unless specific obligations are met. These obligations cannot be represented in C#, so they must be listed in documentation and manually verified by the user of the unsafe code. Only when all obligations are discharged can the code be wrapped in an `unsafe` context to eliminate any warnings.

The `WarnByDefault` flag is needed for backwards-compatibility. If an existing API is unsafe, adding warnings would be a breaking change. If `WarnByDefault` is set to `false`, warnings are not produced unless a project-level property, `ShowAllCallerUnsafeWarnings`, is set to true.

### Implementation

Since only warnings are an output of the above design, the feature could be implemented as an analyzer for C#.

### Definition of Unsafe

`CallerUnsafe` is only useful if there is an accepted definition of what is considered unsafe. For .NET there are two properties that we already consider safe code to preserve:

* Memory safety
* No access to uninitialized memory

In this document **memory safety** is strictly defined as: safe code can never acquire a reference to memory that is not managed by the application. "Managed" here does not refer to solely to heap-allocated, garbage collected memory, but also includes stack-allocated variables that are considered allocated by the runtime.

No access to uninitialized memory means that all managed memory is either never read before it has been initialized by C# code, or it has been initialized to a zero value.

Some examples of APIs or features that are unsafe due to exposing uninitialized memory include:

* ArrayPool.Rent
* The `stackalloc` C# feature used with `SkipLocalsInit` and no initializer

### Detailed semantics

The exact rules on when a warning would be produced will follow the rules defined for "Requires" attributes defined in [Feature attribute semantics](https://github.com/dotnet/runtime/blob/main/docs/design/tools/illink/feature-attribute-semantics.md#requiresfeatureattribute).