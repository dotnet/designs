# Add Hardware Accelerated `Vector512<T>` in .NET

(This document is a draft and WIP)

<!--
Provide the primary contacts here. Linking to the GitHub profiles is useful
because it allows tagging folks on GitHub and discover alternative modes of
communication, such as email or Twitter, if the person chooses to disclose that
information.

The bolded roles makes it easier for people to understand who the driver of the
proposal is (PM) and who can be asked for technical questions (Dev). At
Microsoft, these roles happen to match to job titles too, but that's irrelevant.
-->

**Owner** [John Doe](https://github.com/johndoe) | [Jane Doe](https://github.com/janedoe)

.NET support for SIMD acceleration through `Vector128<T>` and `Vector256<T>` APIs allows developers to transparently harness the power of advanced SIMD ISAs without expert-level knowledge of low-level hardware details and optimization techniques. Current and future processors expose new 512-bit SIMD ISAs --- Intel XX microarchecture and onward are powered by AVX512 and its various extenions --- which are not currently utilized by the .NET platform. We propose that a hardware accelerated `Vector512<T>` API be added to the .NET runtime and JIT compiler which allows developers to harness the power of the 512-bit SIMD ISAs in a programable manner similar to the existing `Vector128<T>` and `Vector256<T>` APIs. Furthermore, we propose that the `Vector<T>` API be extended to automatically select the most performant vector width for a given platform without requiring the developer to adjust their exhisting code. 

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

1. `Vector512<T>` should expose at a minimum the same API operations that `Vector128<T>` and `Vector256<T>` expose.

2. 


### Non-Goals

<!--
Provide a bullet point list of aspects that your feature does not need to do.
The goal of this section is to cover problems that people might think you're
trying to solve but deliberately would like to scope out. You'll likely add
bullets to this section based on early feedback and reviews where requirements
are brought that you need to scope out.
-->

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