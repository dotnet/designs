# Swift Interop

**Owner** [Andy Gocke](https://github.com/agocke) | [Jeremy Koritzinsky](https://github.com/jkoritzinsky)

The [Swift](https://developer.apple.com/swift/) programming language is developed primarily by Apple for use in their product lines. It is a successor to the previous official language for these platforms, Objective-C. C# has had limited Objective-C interop support for quite a while and it powers frameworks like MAUI and Xamarin Forms that run on Apple devices.

Swift is now becoming the dominant language on Apple devices and eclipsing Objective-C. Many important libraries are now Swift-first or Swift-only. Objective-C binding is also becoming more difficult for these libraries. To continue to interoperate with external libraries and frameworks, C# needs to be able to interoperate with Swift.

We will aim to provide a solution where C# needs to be able to interoperate directly with Swift, without going through an intermediate language.

## Scenarios and User Experience

We would expect that users would be able to write C# code that can do simple Swift interop. Additionally, we would expect that for cases where the interop system does not support seamless interop, developers could write a shim in Swift that could be called from C# code. Developers should not need to write a shim in C or Assembly to interact with Swift APIs.

## Requirements

### Goals

In short, we should completely eliminate the required C/assembly sandwich that's currently required to call Swift from C# code, and potentially vice versa. In particular, neither C# nor Swift users should have to deal with lower-level system state, like registers or stack state, to call Swift from C#. This support must be added into Mono, NativeAOT, and CoreCLR. Ready-to-Run support would be preferable, but is not required for the first release. Support on all Apple platforms (macOS, iOS, tvOS, macCatalyst) is the first priority.

### Non-Goals

C# and Swift are different languages with different language semantics. It is not a goal to map every construct from one language to the other. However, there are some terms in both languages that are sufficiently similar that they can be mapped to an identical semantic term in the other language. Interop should be seen as a Venn diagram where each language forms its own circle, and interop is in the (much smaller) space of equivalent terms that are shared between them. Support on non-Apple platforms is not required for the first release of this support.

## Stakeholders and Reviewers

- [@dotnet/interop-contrib](https://github.com/orgs/dotnet/teams/interop-contrib)
- [@dotnet/macios](https://github.com/orgs/dotnet/teams/macios)
- [@lambdageek](https://github.com/lambdageek)
- [@SamMonoRT](https://github.com/SamMonoRT)
- [@kotlarmilos](https://github.com/kotlarmilos)
- [@JulieLeeMSFT](https://github.com/JulieLeeMSFT)
- [@amanasifkhalid](https://github.com/amanasifkhalid)
- [@davidortinau](https://github.com/davidortinau)
- [@jkotas](https://github.com/jkotas)
- [@stephen-hawley](https://github.com/stephen-hawley)

## Design

We plan to split the work into at least three separate components. There will be work at the runtime layer to handle the basic calling-convention and register-allocation work to enable calling basic Swift functions without needing to write custom C or Assembly code or wrapping every Swift function with a C-style shape. Upstack, there will be suite of code-generation tools to provide a higher-level projection of Swift concepts into .NET. Finally, the product will include some mechanism to easily reference Swift libraries from .NET on platforms that natively provide a Swift runtime and libraries, Apple platforms in particular.

### Runtime

#### Calling Conventions

Swift has two calling conventions at its core, the "Swift" calling convention, and the "SwiftAsync" calling convention. We will begin by focusing on the "Swift" calling convention, as it is the most common. The Swift calling convention has a few elements that we need to contend with. Primarily, it allows passing arguments in more registers. Additionally, it has two dedicated registers for specific components of the calling convention, the self register and the error register. The runtime support for this calling convention must support all of these features. The additional registers per argument is relatively easy to support, as each of our compilers must already have some support for this concept to run on Linux or Apple platforms today. We have a few options for the remaining two features we need to support. The `CallConvSwift` modifier can be applied in all contexts where other existing calling convention modifiers can be used. This means it can be used with [`UnmanagedCallConvAttribute`](https://learn.microsoft.com/dotnet/api/system.runtime.interopservices.unmanagedcallconvattribute) for P/Invoke (`[UnmanagedCallConv(CallConvs = new Type[] { typeof(CallConvSwift) })]`), [`UnmanagedCallersOnlyAttribute.CallConv`](https://learn.microsoft.com/dotnet/api/system.runtime.interopservices.unmanagedcallersonlyattribute.callconvs) for reverse P/Invoke (`[UnmanagedCallersOnly(CallConvs = new Type[] { typeof(CallConvSwift) })]`), and with unmanaged function pointers (`delegate* unmanaged[Swift]<>`).

The "SwiftAsync" calling convention has an additional "async context" register. When a "SwiftAsync" function is called by a "SwiftAsync" function, it must be tail-called. A function that uses this calling convention also pops the argument area. In LLVM-IR, this calling convention is referred to as `swifttailcc`. In Clang, this convention is specified as `swiftasynccall`. Additionally, the "SwiftAsync" calling convention does not have the error register and must not have a return value. See the [LLVM-IR language reference](https://github.com/llvm/llvm-project/blob/54fe7ef70069a48c252a7e1b0c6ed8efda0bc440/llvm/docs/LangRef.rst#L452) and the [Clang attribute reference](https://clang.llvm.org/docs/AttributeReference.html#swiftasynccall) for an explaination of the calling convention.

In addition to these two calling conventions, Swift allows developers to export their functions with different calling conventions. The `@convention(c)` option exports a function using the standard `C` calling convention. In .NET, developers can call functions with this calling convention using traditional interop solutions. The `@convention(block)` option exports a function using the Objective-C Block calling convention. The Swift interop experience will not support calling methods exposed with this option as the primary use case is for interop with Objective-C. The `@convention(swift)` option is the same as not specifying the option.

Swift also provides a strong Objective-C interop story through the `@objc` attribute. Types with the `@objc` attribute are exposed through the Swift ABI as well as through Objective-C selectors. We will not call into Swift APIs using the Objective-C interop in this interop story. However, the upstack projection tooling may need to do additional work for types that may need to have Objective-C interop support exposed (in the case that a .NET user type derives from a Swift type that had the `@objc` attribute).

##### Self register

We have selected the following option for supporting the Self register in the calling convention:

1. Use specially-typed argument with a type, `SwiftSelf`, to represent which parameter should go into the self register.

We will provide a `SwiftSelf` type to specify "this argument goes in the self register". Specifying the type twice in a signature would generate an `InvalidProgramException`. This would allow the `self` argument to be specified anywhere in the argument list. Alternatively, many sections of the Swift ABI documentation, as well as the Clang docs refer to this parameter as the "context" argument instead of the "self" argument, so an alternative name could be `SwiftContext`. Methods annotated with `UnmanagedCallersOnly` will be capable of handling the self register; the value stored in the self register will be loaded into the `SwiftSelf` argument.

For reference, explicitly declaring a function with the Swift or SwiftAsync calling conventions in Clang requires the "context" argument, the value that goes in the "self" register, as the last parameter or the penultimate parameter followed by the error parameter.

The self register, like the error register and the async context register discussed later, is always a pointer-sized value. As a result, if we introduce any special intrinsic types for the calling convention, we don't need to make the type generic as we can always use a `void*` to represent the value at the lowest level.

Additionally, we have selected this design as this provides consistency with the error register and async context register handling, discussed below.

We have rejected the following designs for the reasons mentioned below:

1. Use an attribute like `[SwiftSelf]` on a parameter to specify it should go in the self register.
2. Specify that combining the `Swift` calling convention with the `MemberFunction` calling convention means that the first argument is the `self` argument.

Rejected Option 1 seems like a natural fit, but there is one significant limitation: Attributes cannot be used in function pointers. Function pointers are a vital scenario for us to support as we want to support virtual method tables as they are used in scenarios like protocol witness tables.

Rejected Option 2 is a natural fit as the `MemberFunction` calling convention combined with the various C-based calling conventions specifies that there is a `this` argument as the first argument. Defining `Swift` + `MemberFunction` to imply/require the `self` argument is a great conceptual extension of the existing model. Although, in Swift, sometimes the `self` register is used for non-instance state. For example, in static functions, the type metadata is passed as the `self` argument. Since static functions are not member functions, we may want to not use the `MemberFunction` calling convention. As a result, we have rejected this option.

##### Error register

We have selected an approach for handling the error register in the Swift calling convention:

1. Use a special type named something like `SwiftError*` to indicate the error parameter

This approach expresses that the to-be-called Swift function uses the Error Register in the signature and they both require signature manipulation in the JIT/AOT compilers. Like with `SwiftSelf`, we would throw an `InvalidProgramException` for a signature with multiple `SwiftError` parameters. We use a pointer-to-`SwiftError` type to indicate that the error register is a by-ref/out parameter. We don't use managed pointers as our modern JITs can reason about unmanaged pointers well enough that we do not end up losing any performance taking this route. The `UnmanagedCallersOnly` implementation will require a decent amount of JIT work to emulate a local variable for the register value, but we have prior art in the Clang implementation of the Swift error register that we can fall back on. Methods annotated with `UnmanagedCallersOnly` will pass the `SwiftError*` value in the error register.

Additionally, we have selected this design as this provides consistency with the self register and async context register handling, discussed below.

We've rejected the following designs for the reasons mentioned below:

1. Use an attribute like `[SwiftError]` on a `ref` or `out` parameter to indicate the error parameter.
2. Use a special return type `SwiftReturn<T>` to indicate the error parameter and combine it with the return value.
3. Use special helper functions `Marshal.Get/SetLastSwiftError` to get and set the last Swift error. Our various compilers will automatically emit a call to `SetLastSwiftError` from Swift functions and emit a call to `GetLastSwiftError` in the epilog of `UnmanagedCallersOnly` methods. The projection generation will emit calls to `Marshal.Get/SetLastSwiftError` to get and set these values to and from exceptions.
4. Implicitly transform the error register into an exception at the interop boundary.
5. Use `SwiftError` on a `ref` or `out` parameter to indicate the error parameter.

We have a prototype that uses Rejected Option 1; however, using an attribute has the same limitations as the attribute option for the self register.

Rejected Option 2 provides a more functional-programming-style API that is more intuitive than the `SwiftError` options above, but likely more expensive to implement. As a result, Option 2 would be better as a higher-level option and not the option implemented by the JIT/AOT compilers.

Rejected Option 3 provides an alternative approach. As the "Error Register" is always register-sized, we can use runtime-supported helper functions to stash away and retrieve the error register. Unlike options 2 or 3, we don't need to do any signature manipulation as the concept of "this Swift call can return an error" is not represented in the signature. Responsibility to convert the returned error value into a .NET type, such as an exception, would fall to the projection tooling. Since option 4 does not express whether or not the target function throws at the signature level, the JIT/AOT compilers would always need to emit the calls to the helpers when compiling calls to and from Swift code. If the projection of Swift types into .NET will always use exception and not pass Swift errors as the error types directly, then Option 3 reduces the design burden on the runtime teams by removing the type that would only be used at the lowest level of the Swift/.NET interop. However, Option 3 would leave some performance on the table as it would effectively require us to store and read the error value into thread-local storage when we stash and retrieve the error value instead of reading the error value from the register directly. Due to the performance hit of reading/writing thread-local storage at each transition, we have rejected this option.

Rejected Option 4 would be most similar to the Objective-C interop experience. However, this experience would require more work in the JIT/AOT compilers and would make the translation between .NET exception and Swift error codes inflexible. Modern .NET interop solutions generally push error-exception translation mechanisms to be controlled by higher-level interop code generators instead of the runtime for flexibility. Not selecting this option would require all `CallConvSwift` `UnmanagedCallersOnly` methods to wrap their contents in a `try-catch` to translate any exceptions. This is already done for the COM source generator and had to be done by Binding Tools for Swift, so the pattern has a lot of implementation expertise.

Rejected Option 5 would have provided a better ".NET" shape than our selected alternative; however, the additional complexity in the UnmanagedCallersOnly case is not worth the cost.

##### Async Context Register

In the SwiftAsync calling convention, there is also an Async Context register, similar to the Self and Error registers. Like the error register, the async context must be passed by a pointer value. As a result, similar options apply here, with the same constraints. Additionally, we don't already have an existing `CallConvAsync` calling convention modifier, so going the calling convention route like proposed for the self register is not practical. As a result, we will likely need to use a special type like `SwiftAsyncContext` to represent the async context register, similar to the proposals for the error register and the self register.

##### Tuples

In the Swift language, tuples are "unpacked" in the calling convention. Each element of a tuple is passed as a separate parameter. C# has its own language-level tuple feature in the ValueTuple family of types. The Swift/.NET interop story could choose to automatically handle tuple types in a similar way for a `CallConvSwift` function call. However, processing the tuple types in the JIT/AOT compilers is complicated and expensive. It would be much cheaper to defer this work upstack to the language projection, which can split out the tuple into individual parameters to pass to the underlying function. The runtime and libraries teams could add analyzers to detect value-tuples being passed to `CallConvSwift` functions at compile time to help avoid pits of failure. As we expect most developers to use the higher-level tooling and to not use `CallConvSwift` directly, we would likely defer any analyzer work until we have a suitable use case.

##### Structs/Enums

Like .NET, Swift has both value types and class types. The value types in Swift include both structs and enums. When passed at the ABI layer, they are generally treated as their composite structure and passed by value in registers. However, when Library Evolution mode is enabled, struct layouts are considered "opaque" and their size, alignment, and layout can vary between compile-time and runtime. As a result, all structs and enums in Library Evolution mode are passed by a pointer instead of in registers. Frozen structs and enums (annotated with the `@frozen` attribute) are not considered opaque and will be enregistered. We plan to interoperate with Swift through the Library Evolution mode, so we will generally be able to pass structures using opaque layout.

When calling a function that returns an opaque struct, the Swift ABI always requires the struct to be passed by a return buffer. Since we cannot know the size at compile time, we'll need to handle this case manually in the calling convention signature as well by manually introduce the struct return buffer parameter. Alternatively, we could enlighten the JIT/AOT compilers about how to look up the correct buffer size for an opaque struct. This would likely be quite expensive, so we should avoid this option and just manually handle the return buffer. We can determine the return buffer size by using the value witness table for the struct type. We could always move to the JIT-intrinsic model in the future if we desired.

At the lowest level of the calling convention, we do not consider Library Evolution to be a different calling convention than the Swift calling convention. Library Evolution requires that some types are passed by a pointer/reference, but it does not fundamentally change the calling convention. Effectively, Library Evolution forces the least optimizable choice to be taken at every possible point. As a result, we should not handle Library Evolution as a separate calling convention and instead we can manually handle it at the projection layer.

For frozen structs and enums, Swift has a complicated lowering process where the struct or enum type's layout are recursively flattened to a sequence of primitives. If this sequence is length 4 or less, the values of this type are split into the elements of this sequence for parameter passing instead of passing the struct as a whole. Structs and enums that cannot be broken down in this way are passed by-reference to their specified frozen layout. When a frozen struct or enum with a primitive sequence of 4 elements or less is returned from a function, it is returned as if it were a structure of the elements of the primitive sequence. We will implement this pass in the VM/JIT layer. Direct users of the `CallConvSwift` calling convention will be allowed to specify blittable struct types, which the runtime and JIT/AOT compilers will inspect and classify to create identical behavior as the Swift compiler's primitive sequence lowering combined with LLVM calling convention register allocation.

The projection tooling will provide blittable representations of any projected frozen struct or enum types, at minimum for any of these types that are passed by value to any Swift APIs, to support the above ABI handling. These blittable types are not required to be lowered to primitive types; they may have fields of other blittable representations of other Swift types or other blittable structs.  These blittable representations will be the "struct or enum type layouts" mentioned in the paragraph above.

##### SIMD Types

We will pass the `System.Runtime.Intrinsics.VectorX<T>` types in SIMD registers in the same way the default unmanaged calling convention specifies. We will treat the `Vector2/3/4` types as non-SIMD types as we will not project any Swift SIMD types to these types (see the SIMD Types section in the projection design).

CoreCLR and NativeAOT currently block the `VectorX<T>` types from P/Invokes as this behavior is currently not well-supported by RyuJIT. As the target libraries for .NET 9 do not use the Swift `SIMDX<T>` types, we can defer SIMD support until a future release of Swift interop. Unlike the existing P/Invoke blocking behavior however, we should block these types in the JIT if possible as the existing `VectorX<T>` blocking for P/Invokes is not robust.

##### Automatic Reference Counting and Lifetime Management

Swift has a strongly-defined lifetime and ownership model. This model is specified in the Swift ABI and is similar to Objective-C's ARC (Automatic Reference Counting) system. When .NET calls into Swift, the .NET GC is responsible for managing all managed objects. Types projected from Swift into C# should adhere to the `IDisposable` interface. The projection tooling will generate finalizers, and it's the responsibility of users to utilize finalizers and manage memory explicitly from the managed code. Furthermore, it should employ thin wrappers over the Swift memory allocator, accessible through the `NativeMemory` class, to ensure explicit memory release.. It's important to ensure that when a Swift callee function allocates an "unsafe" or "raw" pointer types, such as UnsafeMutablePointer and UnsafeRawPointer, where explicit control over memory is needed, and the pointer is returned to .NET, the memory is not dereferenced after the call returns. Also, if a C# managed object is allocated in a callee function and returned to Swift, the .NET GC will eventually collect it, but Swift will keep track using ARC, which represents an invalid case and should be handled by projection tools.

The Binding Tools for Swift tooling handles these explicit lifetime semantics with generated Swift code. In the new Swift/.NET interop, management of these lifetime semantics will be done by the Swift projection layer and not by the raw calling-convention support. If any GC interaction is required to handle the lifetime semantics correctly, we should take an approach more similar to the `ComWrappers` support (higher-level, less complex interop interface) rather than the Objective-C interop support (lower-level, basically only usable by the ObjCRuntime implementation).

### Projecting Swift into .NET

The majority of the work for Swift/.NET interop is determining how a type that exists in Swift should exist in .NET and what shape it should have. This section is a work in progress and will discuss how each feature in Swift will be projected into .NET, particularly in cases where there is not a corresponding .NET or C# language feature. Each feature should have a subheading describing how the projection will look and how any mechanisms to make it work will be designed.

All designs in this section should be designed such that they are trimming and AOT-compatible by construction. We should work to ensure that no feature requires whole-program analysis (such as custom steps in the IL Linker) to be trim or AOT compatible.

#### Swift to .NET Language Feature Projections

##### Structs/Value Types

Unlike .NET, Swift's struct types have strong lifetime semantics more similar to C++ types than .NET structs. At the Swift ABI layer, there are broadly three types of structs/enums: "POD/Trivial" structs, "Bitwise Takable/Movable" structs, and non-bitwise movable structs. The [Swift documentation](https://github.com/apple/swift/blob/main/docs/ABIStabilityManifesto.md#layout-and-properties-of-types) covers these different kinds of structs. Let's look at how we could map each of these categories of structs into .NET.

"POD/Trivial" structs have no memory management required and no special logic for copying/moving/deleting the struct instance. Structs of this category can be represented as C# structs with the same field layout.

"Bitwise Takable/Movable" structs have some memory management logic and require calls to Swift's ref-counting machinery to maintain expected lifetimes. Structs of this category can be projected into C# as a struct. When creating this C# struct, we would semantically treat each field as a separate local, create the C# projection of it, and save this "local" value into a field in the C# struct.

Structs that are non-bitwise-movable are more difficult. They cannot be moved by copying their bits; their copy constructors must be used in all copy scenarios. When mapping these structs to C#, we must take care that we do not copy the underlying memory and to call the deallocate function when the C# usage of the struct falls out of scope. These use cases best match up to C# class semantics, not struct semantics.

We plan to interop with Swift's Library Evolution mode, which brings an additional wrinkle into the Swift struct story. Swift's Library Evolution mode abstracts away all type layout and semantic information unless a type is explicitly marked as `@frozen`. In the Library Evolution case, all structs have "opaque" layout, meaning that their exact layout and category cannot be determined until runtime. As a result, we need to treat all "opaque" layout structs as possibly non-bitwise-movable at compile time as we will not know until runtime what the exact layout is. Swift/C++ interop is not required to use the Library Evolution mode in all cases as it can statically link against Swift libraries, so it is not limited by opaque struct layouts in every case. The size and layout information of a struct is available in its [Value Witness Table](https://github.com/apple/swift/blob/main/docs/ABIStabilityManifesto.md#value-witness-table), so we can look up this information at runtime for allocating struct instances and manipulating struct memory correctly.

##### Tuples

If possible, Swift tuples should be represented as `ValueTuple`s in .NET. If this is not possible, then they should be represented as types with a `Deconstruct` method similar to `ValueTuple` to allow a tuple-like experience in C#.

##### SIMD types

Swift has its own built-in SIMD types; however they're named based on the number of elements, not based on the width of the vector type. For example, Swift has `SIMD2<T>`, `SIMD4<T>`, up to `SIMD64<T>`. When the instantiations of these types correspond to an intrinsic vector type, they are treated as that type. Otherwise, they are treated as a struct of vectors. In .NET, our vector types are named based on their vector with, so `Vector128<T>`, `Vector256<T>`, etc.

For instantiated generic types that are within the size of an processor intrinsic vector type, there exists a correspondence between a Swift SIMD type and a .NET SIMD type. For example, `SIMD4<Int32>` corresponds to `Vector128<Int32>`.
However, this correspondence breaks down for SIMD types larger than the largest vector register width (i.e. larger than 512 bytes) or for unconstrained generic types like `SIMD4<T>`. These cases; however, should be quite rare. In the "too-large" case, the values are passed into Swift as though the type is a struct of vectors. In the case of unconstrained generic types, the SIMD values are passed indirectly. Both of these cases are suboptimal and we don't know of any public Swift APIs that fall into either of these scenarios.

We recommend that the projection tooling will map each Swift SIMD instantiation to the corresponding `VectorX<T>` type in .NET. For cases where there is no corresponding type or where an API takes or returns an unconstrained generic `SIMDX<T>` value, we can map the APIs to regular projected structs for the SIMD types based on the above rules.

As mentioned in the calling-convention section above, none of the libraries we are targetting in .NET 9 expose APIs that take the SIMD types. As a result, we can defer this support until the future and block projecting of SIMD types in the meantime.

#### Projection Tooling Components

The projection tooling should be split into these components:

##### Importing Swift into .NET

1. A tool that takes in a `.swiftinterface` file or Swift sources and produces C# code.
2. A library that provides the basic support for Swift interop that the generated code builds on.
3. User tooling to easily generate Swift projections for a given set of `.framework`s.
    - This tooling would build a higher-level interface on top of the tool in item 1 that is more user-friendly and project-system-integrated.
4. (optional) A NuGet package, possibly referencable by `FrameworkReference` or automatically included when targeting macOS, Mac Catalyst, iOS, or tvOS platforms that exposes the platform APIs for each `.framework` that is exposed from Swift to .NET.
    - This would be required to provide a single source of truth for Swift types so they can be exposed across an assembly boundary.

#####  Exporting .NET to Swift

There are two components to exporting .NET to Swift: Implementing existing Swift types in .NET and passing instances of those types to Swift, and exposing novel types from .NET code to Swift code to be created from Swift. Exposing novel types from .NET code to Swift code is considered out of scope at this time.

For implementing existing Swift types in .NET, we will require one of the following tooling options:

1. A Roslyn source generator to generate any supporting code needed to produce any required metadata, such as type metadata and witness tables, to pass instances of Swift-type-implementing .NET types defined in the current project to Swift.
2. An IL-post-processing tool to generate the supporting code and metadata from the compiled assembly.

If we were to use an IL-post-processing tool here, we would break Hot Reload in assemblies that implement Swift types, even for .NET-only code, due to introducing new tokens that the Hot Reload "client" (aka Roslyn) does not know about. As a result, we should prefer the Roslyn source generator approach.

### Distribution

The calling convention work will be implemented by the .NET runtimes in dotnet/runtime.

The projection tooling will not ship as part of the runtime. It should be available as a separate NuGet package, possibly as a .NET CLI tool package. The projections should either be included automatically as part of the TPMs for macOS, iOS, and tvOS, or should be easily referenceable.

## Q & A

- How does this interop interact with the existing Objective-C interop experience?
    - This interop story will exist separately from the Objective-C interop story. We will not provide additional support for passing representations of Swift types to Objective-C projections or vice-versa. We may re-evaluate this based on user pain points and cost.
- Library Evolution mode seems to add a lot of complexity. Do we need to interact with it for our v1 solution?
    - We need to use LibraryEvolution mode for our Swift interop as that is the only ABI-stable story for Swift. Otherwise we'd need to re-compile the Swift code per-OS-version for each possible target OS, instead of building the managed code once for all target iOS or macOS versions (which is how .NET generally works today). Also, we wouldn't be able to use the documented `.swiftinterface` files. We'd need to use the compiler-specific `.swiftmodule` files or parse Swift code directly, both of which are much more expensive. Additionally, many core Swift libraries are only exposed with Library Evolution enabled.

## Related GitHub Issues

- Top level issue in dotnet/runtime: https://github.com/dotnet/runtime/issues/93631
- API proposal for CallConvSwift calling convention: https://github.com/dotnet/runtime/issues/64215
