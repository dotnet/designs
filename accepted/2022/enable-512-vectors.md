# Upgrade `Vector` API SIMD Acceleration Capability in .NET

**Owner** [Anthony Canino](https://github.com/anthonycanino) 

.NET support for SIMD acceleration through `Vector<T>` API allows developers to transparently harness the power of advanced SIMD ISAs without expert-level knowledge of low-level hardware details and optimization techniques. Given its variable length nature, `Vector<T>` allows to adapt to the most performant SIMD ISA implementation available for the platform. However, internal .NET libraries use `Vector<T>` as a fallback SIMD code path, where hand-optimized intrinsic code paths, e.g, SSE or AVX2, provide more performance due to `Vector<T>` platform-agnostic API. 

This is problematic for a number of reasons:

  1. As new SIMD ISAs release, .NET developers must write additional SIMD code paths *per optimized library*, increasing code complexity and maintainence burden. At best, much time has to be dedicated to these libraires; At worst, libraries do not get upgraded for latest hardware advancements.

  2. Hand-optimized intrinsics lock software into a specific ISA, often with fixed thresholds for determining when to select a SIMD accelerated codepath for performance gains. In a JIT environment where we can glean performance characteristics of the underlying platform, intrinsics prevent adapting to the most _performant_ SIMD ISA available for a given _workload_.

In this design document, we propose to extend `Vector<T>` to serve as a vessel for frictionless SIMD adoption, both internal to .NET libraries, and to external .NET developers. As a realization of this goal, we propose the following:

  1. Upgrading `Vector<T>` to serve as a sufficiently powerful interface for writing both internal hardware accelerated libraries and external developer code. 

  2. Introducing `Vector512<T>`, analogous to `Vector128<T>` and `Vector256<T>` but for 512-bit SIMD ISA generation. 

  3. Allowing for `Vector<T>` to select the best available `VectorXX` for the platform, with support for dynamically selecting the vector width based on information available at runtime, e.g,. the size of the `Span` being vectorized.

(Updated edits end here)

## Scenarios and User Experience

### Vector512<T> Usage

Exhisting code that utilizes `Vector256<T>` to perform vectorized sum over a span might look something like the following snippet, where `ReduceVector256<T>` is a method that elides the details of a sum reduction of the elements in the vector:

```C#
public int SumVector256T(ReadOnlySpan<int> source)
{
  Vector256<int> vresult = Vector256<int>.Zero;
  for (int i = 0; i < source.Length; i += Vector256<int>.Count)
  {
    vresult += new Vector256<int>(source.Slice(i));
  }

  int result = ReduceVector256T(vresult);

  // Handle tail
  int rem = source.Length % Vector256<int>.Count;
  for (int i = 0; i < rem; i++)
  {
    result += source[(source.Length - rem)+i];
  }

  return result;
}
```

Like the above snippet, the `Vector512<T>` API would function in much the same way, with most of the `Vector256` API simply replaced by `Vector512` methods:


```C#
public int SumVector512T(ReadOnlySpan<int> source)
{
  Vector512<int> vresult = Vector512<int>.Zero;
  for (int i = 0; i < source.Length; i += Vector512<int>.Count)
  {
    vresult += new Vector512<int>(source.Slice(i));
  }

  int result = ReduceVector512T(vresult);

  // Handle tail
  int rem = source.Length % Vector512<int>.Count;
  for (int i = 0; i < rem; i++)
  {
    result += source[(source.Length - rem)+i];
  }

  return result;
}

```

We expect developers who manually write `Vector128<T>` and `Vector256<T>` code to use `Vector512<T>` with minimal effort.

### Variable Length `Vector<T>` Width Selection

Developers who use the variable width vector API, i.e., `Vector<T>`, should transparently benefit from the most performant underlying SIMD implementation. In platforms where that happens to be 512-bit vectors, we propose that `Vector<T>` select `Vector512` for its code generation, much as `Vector<T>` selects `Vector256` when `AVX` is available, and `Vector128` if `SSE` is available etc.

However, developers often encode some threshold checks to decide when to execute vectorized code versus fallback to a scalar implementation because the latter may be more performant for smaller array/span sizes. For example, in the following code, we will only execute the vectorized code if the number of elements in the span is greater than twice the `Vector<T>` width:


```C#
public int SumVectorT(ReadOnlySpan<int> source)
{
  if (source.Length >= Vector<int>.Count * 2)
  {
    Vector<int> vresult = Vector<int>.Zero;
    for (int i = 0; i < source.Length; i += Vector<int>.Count)
    {
      vresult += new Vector<int>(source.Slice(i));
    }

    int result = ReduceVectorT(vresult);

    // Handle tail
    int rem = source.Length % Vector<int>.Count;
    for (int i = 0; i < rem; i++)
    {
      result += source[(source.Length - rem)+i];
    }

    return result;
  }

  int result = 0;
  for (int i = 0; i < source.Length; i++)
  {
    result += source[i];
  }
  return result;
}
```

If we change the underlying `Vector<T>` width to 512-bit vectors, this would create an unexpected performance regression on spans with 32 - 63 elements as they would have been vectorized when the underlying width was 256-bit vectors.

Given the variable length / transparent nature of `Vector<T>` to the developer, we propose to let the compiler generate multiple `Vector<T>` code paths when a method is annotated with the `VectorPaths` attribute. For example,

```C#
#[VectorPaths]
public int SumVectorT(ReadOnlySpan<int> source)
{
  if (source.Length >= Vector<int>.Count * 2)
  {
    Vector<int> vresult = Vector<int>.Zero;
    for (int i = 0; i < source.Length; i += Vector<int>.Count)
    {
      vresult += new Vector<int>(source.Slice(i));
    }

    int result = ReduceVectorT(vresult);

    // Handle tail
    int rem = source.Length % Vector<int>.Count;
    for (int i = 0; i < rem; i++)
    {
      result += source[(source.Length - rem)+i];
    }

    return result;
  }

  int result = 0;
  for (int i = 0; i < source.Length; i++)
  {
    result += source[i];
  }
  return result;
}
```

would tell the compiler to conceptually generate code along these lines:

```C#
public int SumVectorT(ReadOnlySpan<int> source)
{
  if (source.Length >= Vector512<int>.Count * 2)
  {
    Vector512<int> vresult = Vector512<int>.Zero;
    for (int i = 0; i < source.Length; i += Vector512<int>.Count)
    {
      vresult += new Vector512<int>(source.Slice(i));
    }

    int result = ReduceVector512T(vresult);

    // Handle tail
    int rem = source.Length % Vector512<int>.Count;
    for (int i = 0; i < rem; i++)
    {
      result += source[(source.Length - rem)+i];
    }

    return result;
  }
  else if (source.Length >= Vector256<int>.Count * 2)
  {
    Vector256<int> vresult = Vector256<int>.Zero;
    for (int i = 0; i < source.Length; i += Vector256<int>.Count)
    {
      vresult += new Vector256<int>(source.Slice(i));
    }

    int result = ReduceVector256T(vresult);

    // Handle tail
    int rem = source.Length % Vector256<int>.Count;
    for (int i = 0; i < rem; i++)
    {
      result += source[(source.Length - rem)+i];
    }

    return result;

  }
  else if (source.Length >= Vector128<int>.Count * 2)
  {
    Vector128<int> vresult = Vector128<int>.Zero;
    for (int i = 0; i < source.Length; i += Vector128<int>.Count)
    {
      vresult += new Vector128<int>(source.Slice(i));
    }

    int result = ReduceVector128T(vresult);

    // Handle tail
    int rem = source.Length % Vector128<int>.Count;
    for (int i = 0; i < rem; i++)
    {
      result += source[(source.Length - rem)+i];
    }

    return result;

  }

  int result = 0;
  for (int i = 0; i < source.Length; i++)
  {
    result += source[i];
  }
  return result;
}
```

As it currently stands, `Vector<T>` generates vectorized code where the developer need not concern themselves with even the length of the vector, i.e., future ISAs could be 1024-bit, 2049-bit etc. We propose that the compiler fill some of the edge case gaps related to `Vector<T>` to allow it to truly grow into a vessel for transparent SIMD code generation without requireing the developer manually rewrite their code when new SIMD ISAs and hardware are released.

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

## Requirements

### Goals

1. `Vector<T>` should allow to generate code that reaches some threshold of hand-optimized intrinsics. 

2. In combination with the JIT, `Vector<T>` written code should allow to adapt to the best performance of underlying platform, i.e., the JIT may generate multiple codepaths and thresholds that select at runtime which SIMD ISA to use.

3. `Vector512<T>` should expose at a minimum the same API operations that `Vector128<T>` and `Vector256<T>` expose.

### Non-Goals

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

## Q & A

<!--
Features evolve and decisions are being made along the road. Add the question
as a subheading and provide the explanation for the decision below. This way,
you can easily link to specific questions.

When you find yourself having to explain something in a GitHub discussion or in
email, consider to update your proposal and link to your answer instead. This
way, you avoid having to explain the same thing over and over again.
-->