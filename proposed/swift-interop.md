# Swift Interop

<!--
Provide the primary contacts here. Linking to the GitHub profiles is useful
because it allows tagging folks on GitHub and discover alternative modes of
communication, such as email or Twitter, if the person chooses to disclose that
information.

The bolded roles makes it easier for people to understand who the driver of the
proposal is (PM) and who can be asked for technical questions (Dev). At
Microsoft, these roles happen to match to job titles too, but that's irrelevant.
-->

**Owner** [Andy Gocke](https://github.com/agocke) | [Jeremy Koritzinsky](https://github.com/jkoritzinsky)

<!--
Provide a broad problem statement here. This might include some history and the
description of the current state of the product. The goal is to give the reader
the ability to judge how (and how well) your feature will address the problem.
It might also give rise to tweaks or even alternative solutions that could move
your proposal in a different direction -- and that's a good thing. After all, if
the direction needs to change it's best identified when your proposal is still
being reviewed as opposed to after much of it has already been implemented. So
it's in your best interest to ensure the reader has enough context to properly
critique your idea.

Your problem statement should be followed by the solution you're proposing to
solve it. Don't describe specific user scenarios here but provide enough
information so that the reader can get an overview of what you have in mind.
It's very much desirable to make the readers curious and come up with questions.
That puts them in the right state of mind to read the following sections
actively.

Ensure your first paragraph is a short summary of the problem and the proposed
solution so that readers can gain a quick understanding and decide whether your
proposal is relevant to them and thus worth spending time on. It's usually best
to write the first paragraph last because that means you already know the punch
line and can do a better job stating it succinctly.
-->
The [Swift](https://developer.apple.com/swift/) programming language is developed primarily by Apple for use in their product lines. It is a successor to the previous official language for these platforms, Objective-C. C# has had limited Objective-C interop support for quite a while and it powers frameworks like MAUI and Xamarin Forms that run on Apple devices.

Swift is now becoming the dominant language on Apple devices and eclipsing Objective-C. Many important libraries are now Swift-first or Swift-only. Objective-C binding is also becoming more difficult for these libraries. To continue to interoperate with external libraries and frameworks, C# needs to be able to interoperate directly with Swift, without going through an intermediate language.

## Scenarios and User Experience

<!--
Provide examples of how a user would use your feature. Pick typical scenarios
first and more advanced scenarios later.

Ensure to include the "happy path" which covers what you expect will satisfy the
vast majority of your customer's needs. Then, go into more details and allow
covering more advanced scenarios. Well designed features will have a progressive
curve, meaning the effort is proportional to how advanced the scenario is. By
listing easy things first and more advanced scenarios later, you allow your
readers to follow this curve. That makes it easier to judge whether your feature
has the right balance.

Make sure your scenarios are written in such a way that they cover sensible end-
to-end scenarios for the customer. Often, your feature will only cover one
aspect of an end-to-end scenario, but your description should lead up to your
feature and (if it's not the end result) mention what the next steps are. This
allows readers to understand the larger picture and how your feature fits in.

If you design APIs or command line tools, ensure to include some sample code on
how your feature will be invoked. If you design UI, ensure to include some
mock-ups. Do not strive for completeness here -- the goal of this section isn't
to provide a specification but to give readers an impression of your feature and
the look & feel of it. Less is more.
-->

We would expect that users would be able to write C# code that can do simple Swift interop. Additionally, we would expect that for cases where the interop system does not support seamless interop, developers could write a shim in Swift that could be called from C# code. Developers should not need to write a shim in C or Assembly to interact with Swift APIs.

## Requirements

### Goals

<!--
Provide a bullet point list of aspects that your feature has to satisfy. This
includes functional and non-functional requirements. The goal is to define what
your feature has to deliver to be considered correct.

You should avoid splitting this into various product stages (like MVP, crawl,
walk, run) because that usually indicates that your proposal tries to cover too
much detail. Keep it high-level, but try to paint a picture of what done looks
like. The design section can establish an execution order.
-->

In short, we should completely eliminate the required C/assembly sandwich that's currently required to call Swift from C# code, and potentially vice versa. In particular, neither C# nor Swift users should have to deal with lower-level system state, like registers or stack state, to call Swift from C#.

### Non-Goals

<!--
Provide a bullet point list of aspects that your feature does not need to do.
The goal of this section is to cover problems that people might think you're
trying to solve but deliberately would like to scope out. You'll likely add
bullets to this section based on early feedback and reviews where requirements
are brought that you need to scope out.
-->
C# and Swift are different languages with different language semantics. It is not a goal to map every construct from one language to the other. However, there are some terms in both languages that are sufficiently similar that they can be mapped to an identical semantic term in the other language. Interop should be seen as a Venn diagram where each language forms its own circle, and interop is in the (much smaller) space of equivalent terms that are shared between them.

## Stakeholders and Reviewers

<!--
We noticed that even in the cases where we have specs, we sometimes surprise key
stakeholders because we didn't pro-actively involve them in the initial reviews
and early design process.

Please take a moment and add a bullet point list of teams and individuals you
think should be involved in the design process and ensure they are involved
(which might mean being tagged on GitHub issues, invited to meetings, or sent
early drafts).
-->

- @dotnet/interop-contrib
- @lambdageek
- @SamMonoRT
- @kotlarmilos
- @dotnet/macios
- @JulieLeeMSFT
- @davidortinau
- @jkotas

## Design

<!--
This section will likely have various subheadings. The structure is completely
up to you and your engineering team. It doesn't need to be complete; the goal is
to provide enough information so that the engineering team can build the
feature.

If you're building an API, you should include the API surface, for example
assembly names, type names, method signatures etc. If you're building command
line tools, you likely want to list all commands and options. If you're building
UI, you likely want to show the screens and intended flow.

In many cases embedding the information here might not be viable because the
document format isn't text (for instance, because it's an Excel document or in a
PowerPoint deck). Add links here. Ideally, those documents live next to this
document.
-->

### Runtime

#### Calling Conventions

Swift has two calling conventions at its core, the "Swift" calling convention, and the "SwiftAsync" calling convention. We will begin by focusing on the "Swift" calling convention, as it is the most common. The Swift calling convention has a few elements that we need to contend with. Primarily, it allows passing arguments in more registers. Additionally, it has two dedicated registers for specific components of the calling convention, the self register and the error register. The runtime support for this calling convention must support all of these features. The additional registers per argument is relatively easy to support, as each of our compilers must already have some support for this concept to run on Linux or Apple platforms today. We have a few options for the remaining two features we need to support.

The "SwiftAsync" calling convention has an additional "async context" register and specific behaviors expected around how the Swift async state machine is called. Full investigation into the differences needed to implement it are still TBD.

##### Self register

We have two options for supporting the Self register in the calling convention:

1. Use an attribute like `[SwiftSelf]` on a parameter to specify it should go in the self register.
2. Specify that combining the `Swift` calling convention with the `MemberFunction` calling convention means that the first argument is the `self` argument.

The first option seems like a natural fit, but there is one significant limitation: Attributes cannot be used in function pointers. Function pointers are a vital scenario for us to support as we want to support virtual method tables as they are used in scenarios like protocol witness tables. The second option is a natural fit as the `MemberFunction` calling convention combined with the various C-based calling conventions specifies that there is a `this` argument as the first argument. Defining `Swift` + `MemberFunction` to imply/require the `self` argument is a perfect conceptual extension of the existing model.

###### Error register

We have many options for handling the error register in the Swift calling convention:

1. Use an attribute like `[SwiftError]` on a by-ref or `out` parameter to indicate the error parameter.
2. Use a special type like `SwiftError<T>` on a by-ref or `out` parameter to indicate the error parameter.
3. Use a special return type `SwiftReturn<T, TError>` to indicate the error parameter and combine it with the return value.
4. Use special helper functions `Marshal.Get/SetLastSwiftError` to get and set the last Swift error. Our various compilers will automatically emit a call to `SetLastSwiftError` from Swift functions and emit a call to `GetLastSwiftError` in the epilog of `UnmanagedCallersOnly` methods. The projection generation will emit calls to `Marshal.Get/SetLastSwiftError` to get and set these values to and from exceptions.

We have a prototype that uses the first option; however, using an attribute has the same limitations as the attribute option for the self register. As a result, we should consider an alternative option.

Options 2 and 3 have similar characteristics: They both express that the to-be-called Swift function uses the Error Register in the signature and they both require signature manipulation in the JIT/AOT compilers. Option 2 is likely cheaper, as the argument with `ref/out SwiftError<T>` type can easily be associated with the error register. With Option 2 though, we'd have to determine the behavior if multiple parameters used the `SwiftError` type (likely throw an exception). Option 3 provides a more functional-programming-style API that is more intuitive, but likely more expensive to implement.

Option 4 provides an alternative approach. As the "Error Register" is always register-sized, we can use runtime-supported helper functions to stash away and retrieve the error register. Unlike options 2 or 3, we don't need to do any signature manipulation as the concept of "this Swift call can return an error" is not represented in the signature. Responsibility to convert the returned error value into a .NET type, such as an exception, would fall to the projection tooling. Since option 4 does not express whether or not the target function throws at the signature level, the JIT/AOT compilers would always need to emit the calls to the helpers when compiling calls to and from Swift code. If the projection of Swift types into .NET will always use exception and not pass Swift errors as the error types directly, then Option 4 reduces the design burden on the runtime teams by removing the type that would only be used at the lowest level of the Swift/.NET interop.

##### Tuples

In the Swift language, tuples are "exploded" out in the calling convention. Each element of a tuple is passed as a separate parameter. C# has its own language-level tuple feature in the ValueTuple family of types. The Swift/.NET interop story could choose to automatically handle tuple types in a similar way for a `CallConvSwift` function call. However, processing the tuple types in the JIT/AOT compilers is complicated and expensive. It would be much cheaper to defer this work upstack to the language projection, which can split out the tuple into individual parameters to pass to the underlying function. The runtime and libraries teams could add analyzers to detect value-tuples being passed to `CallConvSwift` functions at compile time to help avoid pits of failure.

##### Automatic Reference Counting and Lifetime Management

Swift has a very strongly-defined lifetime and ownership model. This model is specified in the Swift ABI and is similar to Objective-C's ARC (Automatic Reference Counting) system. The Binding Tools for Swift tooling handles these explicit lifetime semantics with some generated Swift code. In the new Swift/.NET interop, management of these lifetime semantics will be done by the Swift projection and not by the raw calling-convention support.

### Projecting Swift into .NET

The majority of the work for Swift/.NET interop is determining how a type that exists in Swift should exist in .NET and what shape it should have. This section is a work in progress and will discuss how each feature in Swift will be projected into .NET, particularly in cases where there is not a corresponding .NET or C# language feature. Each feature should have a subheading describing how the projection will look and how any mechanisms to make it work will be designed.

All designs in this section should be designed such that they are trimming and AOT-compatible by construction. We should work to ensure that no feature requires whole-program analysis (such as custom steps in the IL Linker) to be trim or AOT compatible.

#### Swift to .NET Language Feature Projections

TBD

#### Projection Tooling Components

The projection tooling should be split into these components:

##### Importing Swift into .NET

1. A tool that takes in a `.swiftinterface` file or Swift sources and produces C# code.
2. A library that provides the basic support for Swift interop that the generated code builds on.
3. User tooling to easily generate Swift projections for a given set of `.framework`s.
4. (optional) A .NET shared framework package when targeting macOS, Mac Catalyst, iOS, or tvOS platforms that exposes the platform APIs or NuGet packages for each `.framework` that is exposed from Swift to .NET.
    - This would be required to provide a single source of truth for Swift types so they can be exposed across an assembly boundary.

#####  Exporting .NET to Swift

There are two components to exporting .NET to Swift: Implementing existing Swift types in .NET and passing instances of those types to Swift, and exposing novel types from .NET code to Swift code to be created from Swift. Exposing novel types from .NET code to Swift code is considered out of scope at this time.

For implementing existing Swift types in .NET, we will require the following tooling:

1. A Roslyn source generator to generate any supporting code needed to produce any required metadata to pass instances of Swift-type-implementing .NET types to Swift.

### Distribution

The calling convention work will be implemented by the .NET runtimes in dotnet/runtime.

The projection tooling will not ship as part of the runtime. It should be available as a separate NuGet package, possibly as a .NET CLI tool package. The projections should either be included automatically as part of the TPMs for macOS, iOS, and tvOS, or should be easily referenceable.

## Q & A

<!--
Features evolve and decisions are being made along the road. Add the question
as a subheading and provide the explanation for the decision below. This way,
you can easily link to specific questions.

When you find yourself having to explain something in a GitHub discussion or in
email, consider to update your proposal and link to your answer instead. This
way, you avoid having to explain the same thing over and over again.
-->

## Related GitHub Issues

- Top level issue in dotnet/runtime: https://github.com/dotnet/runtime/issues/93631
- API proposal for CallConvSwift calling convention: https://github.com/dotnet/runtime/issues/64215
