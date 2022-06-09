# Upgrade `Vector` API SIMD Acceleration Capability in .NET

**Owner** [Anthony Canino](https://github.com/anthonycanino) 

.NET support for SIMD acceleration through `Vector<T>` API allows developers to transparently harness the power of advanced SIMD ISAs without expert-level knowledge of low-level hardware details and optimization techniques. Given its variable-length nature, `Vector<T>` allows to adapt to the most performant SIMD ISA implementation available for the platform. However, internal .NET libraries use `Vector<T>` as a fallback SIMD code path, where hand-optimized intrinsic code paths, e.g, SSE or AVX2, provide more performance due to `Vector<T>` platform-agnostic API. 

This is problematic for a number of reasons:

  1. As new SIMD ISAs release, .NET developers must write additional SIMD code paths *per optimized library*, increasing code complexity and maintenance burden. At best, much time has to be dedicated to these libraries; at worst, libraries do not get upgraded for latest hardware advancements.

  2. Hand-optimized intrinsics lock software into a specific ISA, often with fixed thresholds for determining when to select a SIMD accelerated codepath for performance gains. In a JIT environment where we can glean performance characteristics of the underlying platform, intrinsics prevent adapting to the most _performant_ SIMD ISA available for a given _workload_.

In this design document, we propose to extend `Vector<T>` to serve as a vessel for frictionless SIMD adoption, both internal to .NET libraries, and to external .NET developers. As a realization of this goal, we propose the following:

  1. Upgrading `Vector<T>` to serve as a sufficiently powerful interface for writing both internal hardware accelerated libraries and external developer code. 

  2. Allowing for `Vector<T>` to select the best available `VectorXX` for the platform, with support for dynamically selecting the vector width based on information available at runtime, e.g,. the size of the `Span` being vectorized.


We detail two alternative designs to achieve the aforementioned goals: (1) a templated approach, where the JIT treats `Vector<T>` as a template to statically generate alternative vectorized code paths per-available SIMD ISA that select for the size of the workload at runtime, and (2) a profile-guided optimization approach where the JIT dynamically selects the most performant SIMD ISA for `Vector<T>` at runtime per-method, with the capability to re-compile a `Vector<T>` method as needed. 

## Scenarios and User Experience

### `Vector<T>` as an SIMD Optimization Interface

#### 1. Templated Codegen from `Vector<T>`

The following code snippet --- taken from the fallback optimization path of UTF16 to ASCII narrowing --- posses problems as the _sole_ SIMD optimization pathway in the `ASCIIUtility` library. Namely, the check on whether we should vectorize or not will create different performance characteristics depending upon the size the JIT chooses for `Vector<T>`. If `Vector256` is chosen, then buffers with 64 elements and above will be optimized; if `Vector128` is chosen, then buffers of 32 elements and above will be optimized. 


```C#
public static unsafe nuint NarrowUtf16ToAscii(char* pUtf16Buffer, byte* pAsciiBuffer, nuint elementCount)
{
  // ...

  nuint currentOffset = 0;
  uint SizeOfVector = (uint)Unsafe.SizeOf<Vector<byte>>(); // JIT will make this a const

  // Only bother vectorizing if we have enough data to do so.
  if (elementCount >= 2 * SizeOfVector)
  {
    // ...

    Vector<ushort> maxAscii = new Vector<ushort>(0x007F);

    nuint finalOffsetWhereCanLoop = elementCount - 2 * SizeOfVector;
    do
    {
        Vector<ushort> utf16VectorHigh = Unsafe.ReadUnaligned<Vector<ushort>>(pUtf16Buffer + currentOffset);
        Vector<ushort> utf16VectorLow = Unsafe.ReadUnaligned<Vector<ushort>>(pUtf16Buffer + currentOffset + Vector<ushort>.Count);

        if (Vector.GreaterThanAny(Vector.BitwiseOr(utf16VectorHigh, utf16VectorLow), maxAscii))
        {
            break; // found non-ASCII data
        }

        // TODO: Is the below logic also valid for big-endian platforms?
        Vector<byte> asciiVector = Vector.Narrow(utf16VectorHigh, utf16VectorLow);
        Unsafe.WriteUnaligned<Vector<byte>>(pAsciiBuffer + currentOffset, asciiVector);

        currentOffset += SizeOfVector;
    } while (currentOffset <= finalOffsetWhereCanLoop);

    // ...
  }
}
```

This prevents the JIT from generating code from `Vector<T>` with consistent performance behavior, both internal to .NET libraries and for external .NET developers. Particularly with the addition of `Vector512`, workloads that previously would have been optimized would no longer clear the threshold. To aid `Vector<T>` to act as a single generic SIMD framework, we propose to add a `Vectorize.If` intrinsic that instructs the JIT to generate _multiple_ SIMD acceleration pathways. 

For example, in the following snippet:

```C#
public static unsafe nuint NarrowUtf16ToAscii(char* pUtf16Buffer, byte* pAsciiBuffer, nuint elementCount)
{
  // ...

  nuint currentOffset = 0;

  // Only bother vectorizing if we have enough data to do so.
  if (Vectorize.If(elementCount >= 2 * Unsafe.Sizeof<Vector<byte>>())
  {
    // ...

    Vector<ushort> maxAscii = new Vector<ushort>(0x007F);

    nuint finalOffsetWhereCanLoop = elementCount - 2 * SizeOfVector;
    do
    {
        Vector<ushort> utf16VectorHigh = Unsafe.ReadUnaligned<Vector<ushort>>(pUtf16Buffer + currentOffset);
        Vector<ushort> utf16VectorLow = Unsafe.ReadUnaligned<Vector<ushort>>(pUtf16Buffer + currentOffset + Vector<ushort>.Count);

        if (Vector.GreaterThanAny(Vector.BitwiseOr(utf16VectorHigh, utf16VectorLow), maxAscii))
        {
            break; // found non-ASCII data
        }

        // TODO: Is the below logic also valid for big-endian platforms?
        Vector<byte> asciiVector = Vector.Narrow(utf16VectorHigh, utf16VectorLow);
        Unsafe.WriteUnaligned<Vector<byte>>(pAsciiBuffer + currentOffset, asciiVector);

        currentOffset += SizeOfVector;
    } while (currentOffset <= finalOffsetWhereCanLoop);


    // ...
  }
}
```

the JIT to generate would generate the following depending upon the availability of the SIMD ISA of the platform:

```C#
public static unsafe nuint NarrowUtf16ToAscii(char* pUtf16Buffer, byte* pAsciiBuffer, nuint elementCount)
{
  // ...

  // Only bother vectorizing if we have enough data to do so.
  if (elementCount >= 2 * Vector512<byte>.Count)
  {
    // Execute a Vector512 pathway
    Vector512<ushort> maxAscii = new Vector512<ushort>(0x007F);

    // ...
  }
  else if (elementCount >= 2 * Vector256<byte>.Count)
  {
    // Execute a Vector256 pathway
    Vector256<ushort> maxAscii = new Vector256<ushort>(0x007F);

    // ...
  }
  else if (elementCount >= 2 * Vector128<byte>.Count)
  {
    // Execute a Vector128 pathway
    Vector128<ushort> maxAscii = new Vector128<ushort>(0x007F);

    // ...
  }
  else 
  {
    Vector1<ushort> maxAscii = new Vector1<ushort>(0x007F);

    // ...
  }
}
```

In this way, we can collapse multiple SIMD ISA checks into a single "template" SIMD ISA code which the JIT may expand. We use a new `Vectorize.If` intrinsic that allows backward compatibility with `Vector<T>`, i.e., a `Vectorize.If` JIT intrinsic instructs the JIT that the code inside its block is a template for multiple vector sizes. If `Vectorize.If` is not present, `Vector<T>` will select the best ISA for the platform determined by the JIT.

Note that `Vectorize.If` includes a fallback path where no vectorization is performed (which we represent as `Vector1`). This is consistent with the philosophy that `Vector<T>` allow a developer to express their algorithm from a single source, and let the JIT handle the underlying implementation which reduces developer burden and maintaince costs.

#### 2. PGO Codegen from `Vector<T>`

We propose to introduce a `#[Vectorize]` attribute which instructs the JIT to dynamically profile a method to select an optimal length for `Vector<T>`. Returning to the example above, we add `#[Vectorize]` to the `NarrowUtf16ToAscii` method like so:

```C#
#[Vectorize]
public static unsafe nuint NarrowUtf16ToAscii(char* pUtf16Buffer, byte* pAsciiBuffer, nuint elementCount)
{
  // ...
  nuint currentOffset = 0;
  uint SizeOfVector = (uint)Unsafe.SizeOf<Vector<byte>>(); // JIT will make this a const

  // Only bother vectorizing if we have enough data to do so.
  if (elementCount >= 2 * SizeOfVector)
  {
    // ...

    Vector<ushort> maxAscii = new Vector<ushort>(0x007F);

    nuint finalOffsetWhereCanLoop = elementCount - 2 * SizeOfVector;
    do
    {
        Vector<ushort> utf16VectorHigh = Unsafe.ReadUnaligned<Vector<ushort>>(pUtf16Buffer + currentOffset);
        Vector<ushort> utf16VectorLow = Unsafe.ReadUnaligned<Vector<ushort>>(pUtf16Buffer + currentOffset + Vector<ushort>.Count);

        if (Vector.GreaterThanAny(Vector.BitwiseOr(utf16VectorHigh, utf16VectorLow), maxAscii))
        {
            break; // found non-ASCII data
        }

        // TODO: Is the below logic also valid for big-endian platforms?
        Vector<byte> asciiVector = Vector.Narrow(utf16VectorHigh, utf16VectorLow);
        Unsafe.WriteUnaligned<Vector<byte>>(pAsciiBuffer + currentOffset, asciiVector);

        currentOffset += SizeOfVector;
    } while (currentOffset <= finalOffsetWhereCanLoop);


    // ...
  }
}
```

which will enable PGO for the `NarrowUtf16ToAscii` method. This will create the following _conceptual_ stub (actual implementation will adhere to existing PGO infrastructure).

```C#
#[Vectorize]
public static unsafe nuint NarrowUtf16ToAscii_Stub(char* pUtf16Buffer, byte* pAsciiBuffer, nuint elementCount)
{
  nuint averageElementCountSample = Probe(elementCount);
  if (averageElementCountSample >= 2 * Unsafe.SizeOf<Vector512<byte>>())
  {
    // Recompile NarrowUtf16ToAscii with Vector<T> length set to Vector512
  }
  else if (averageElementCountSample >= 2 * Unsafe.SizeOf<Vector256<byte>>())
  {
    // Recompile NarrowUtf16ToAscii with Vector<T> length set to Vector512
  } 
  else if (averageElementCountSample < 2 * Unsafe.SizeOf<Vector128<byte>>())
  {
    // Recompile NarrowUtf16ToAscii with Vector<T> length to to Vector1
  }

  NarrowUtf16ToAscii(pUtf16Buffer, pAsciiBuffer, elementCount);
}
```

that will sample and recompile `NarrowUtf16ToAscii` to select a larger vector size for `Vector<T>` if the average workload to the method is determined to be worth the cost. 

Philosophically, both approaches allow the developer to focus less on the implementation of the underlying SIMD algorithm and more on its behavior. 

### Additional `Vector<T>` Methods for Near-Intrinsic Performance

(TODO: Revisit this once we have some idea of APIs required to mitigate `MoveMask`)

### A Complete Example

We refer to the current implementation of `IndexOf` in `SpanHelper.Byte.cs` (https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/SpanHelpers.Byte.cs#L15) to illustrate the benefit of the proposed `Vector<T>` improvements.

In the following code snippet, we collapse the existing implemention into a single code path using `Vector<T>` new APIs, and let the JIT determine the most performant SIMD ISA at runtime with the `#[Vectorize]` attribute.

(The example should use the `#[Vectorize]`, a single `Vector<T>` code path, and ideally handle trailing elements with the trailing element API).

```C#
public static int IndexOf(ref byte searchSpace, int searchSpaceLength, ref byte value, int valueLength)
{
  // TODO
}
```

## Requirements

### Goals

1. `Vector<T>` should allow to generate code that reaches performance within some threshold of hand-optimized intrinsics. 

2. In combination with the JIT, `Vector<T>` written code should allow to adapt to the best performance of underlying platform, i.e., the JIT may generate multiple codepaths and thresholds that select at runtime which SIMD ISA to use, be it through static templated expansion or dynamic method recompilation.

3. `Vector<T>` API surface should include sufficiently expressive methods to reach the first goal above. These methods allow to use `Vector<T>` generically without explicit knowledge of vector length or intrinsics.

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

### `Vector<T>` Codegen Improvements

#### 1. Templated Codegen from `Vectorize.If` Code Block

- The presence of `Vectorize.If` in the conditional of an `if` statement drives the expansion, which creates a `Vectorize` code block.

- The `Vectorize.If` intrinsic accepts a boolean expression as a parameter. The boolean expression must contain a reference to `Vector`, as the `Vector` will be replaced per arm as discussed below and allows the selection of alternative code paths. If no reference to `Vector` is found, the JIT will throw an `OperationNotSupportedException`.

- When the JIT encounters a `Vectorize.If` intrinsic in a conditional block, the JIT will:

  - Clone the if condition and associated arm with the `Vectorize.If`, creating an arm for each available vector length on the platform. 

  - In each cloned arm any `Vector<T>` operations will be translated to `VectorXX<T>` operations, where XX represents the vector length for that arm.

  - The following example demonstrates the expansion:

    ```C#
    void unsafe foo(Span<int> sp)
    {
      if (Vectorize.If(sp.Length >= Vector<int>.Count))
      {
        int blocks = sp.Length / Vector<int>.Count;
        for (int i = 0; i < blocks; i ++)
        {
          Vector<int> v = new Vector<int>(sp.Slice(i * Vector<int>.Count));
          // ...
        }              
      }
    }
    ```

    expands to

    ```C#
    void unsafe foo(Span<int> sp)
    {
      if (sp.Length >= Vector512<int>.Count)
      {
        int blocks = sp.Length / Vector512<int>.Count;
        for (int i = 0; i < blocks; i ++)
        {
          Vector512<int> v = new Vector512<int>(sp.Slice(i * Vector512<int>.Count));
          // ...
        }
      }
      else if (sp.Length >= Vector256<int>.Count)
      {
        int blocks = sp.Length / Vector256<int>.Count;
        for (int i = 0; i < blocks; i ++)
        {
          Vector256<int> v = new Vector256<int>(sp.Slice(i * Vector256<int>.Count));
          // ...
        }
      }
      else if (sp.Length >= Vector128<int>.Count)
      {
        int blocks = sp.Length / Vector128<int>.Count;
        for (int i = 0; i < blocks; i ++)
        {
          Vector128<int> v = new Vector128<int>(sp.Slice(i * Vector128<int>.Count));
          // ...
        }
      }
      else
      {
        int blocks = sp.Length / Vector1<int>.Count;
        for (int i = 0; i < blocks; i ++)
        {
          Vector1<int> v = new Vector1<int>(sp.Slice(i * Vector1<int>.Count));
          // ...
        }
      }
    }
    ```

- If the JIT encounters a `Vectorize.If` outside of an `if` conditional, the JIT will throw an `OperationNotSupportedException`.

- If the JIT encounters `Vectorize.If` with `else if` or `else` arms, the JIT will throw an `OperationNotSupportedException`.

- Any fixed length VectorXX will not be adjusted in any way if present in a `Vectorize.If` block.

- Nested `Vectorize.If` is allowed. Expansion begins at the most nested block.

#### 2. PGO Codegen from `Vector<T>` and `#[Vectorize]`

In order to perform profile guided optimization for methods annotated with `#[Vectorize]`, the JIT must detect which method parameters to sample and what thresholds should trigger recompilation based on those sample points.
##### Detecting Instrumentation Points and Thresholds

The presence of a `#[Vectorize]` attribute instructs the JIT to perform a dependence analysis upon first encountering the method to determine what method parameters to sample.

- If an `if` statement's boolean condition contains a reference to a `Vector<T>.Count` or `Unsafe.SizeOf<<Vector<T>>>`, we mark all variables used in the condition as sinks.

- Vector memory operations (Load, Write, `new`) input (source) are traced through the Use-Def chain. If the chain reaches a parameter, it is marked as possible instrumentation parameter.

  - If the parameter is one whose type has a queryable size, e.g., `Span` we mark it for sampling.

  - Raw pointers are not marked for sampling. (TODO: Anyway to handle this).

- Sinks are traced through use-def chains to reach a source.

- Valid sources are method parameters only.

- A source must either be a primitive value whose sink is used in an if statement, or a memory type with a size that can be checked at runtime, i.e., raw pointers cannot be sources.

- The presence of an `if` sink/source will take precedence over the presence of `Vector` memory sinks, and will serve as the primary threshold check for sampling.

- If no `if` sink/source is established, the default threshold check will be (2 * vector size). 


For example, in the following method, the result of `span.Slice` is used as a argument to a `Vector` creation. The expression is marked as a sink:

```C#
#[Vectorize]
public int SumVector(ReadOnlySpan<int> span) // <-- Source
{
  Vector<int> vresult = Vector<int>.Zero;

  int lastBlockIndex = span.Length - (span.Length % Vector<int>.Count);
  int i = 0;

  for (i; i < lastBlockIndex; i += Vector<int>.Count)
  {
    vresult += new Vector<int>(span.Slice(i));  // <-- Sink
  }

  int result = 0;
  for (int j = 0; j < Vector<int>.Count; j++)
  {
    result += vresult[j];
  }

  // Handle tail
  while (i < span.Length)
  {
    result += span[(span.Length - rem)+i];
  }

  return result;
}
```

will result in `span` being selected as a source for sampling. The JIT will _conceptually_ perform the following sampling and recompling --- we elide implementation details for now, which we recognize must fit into the existing PGO architecture (no actual "stub" will exist, instead the recompile logic will take place inside RyuJIT during a Tier1 compile check etc. )

```C#
public int SumVector_Stub(ReadOnlySpan<int> span)
{
  int averageSpanLength = Sample(span.Length, spanLengthGlobal);
  if (averageSpanLength >= 2 * Vector512<int>.Count )
  {
    // Recompile
  }
  // ...
  SumVector(span);
}
```

<br/>

In the following sample, and `if` conditional contains a sink:

```C#
#[Vectorize]
public static unsafe nuint NarrowUtf16ToAscii(char* pUtf16Buffer, byte* pAsciiBuffer, nuint elementCount)
{
  // ...
  nuint currentOffset = 0;
  uint SizeOfVector = (uint)Unsafe.SizeOf<Vector<byte>>(); // JIT will make this a const

  // Only bother vectorizing if we have enough data to do so.
  if (elementCount >= 4 * SizeOfVector) // <-- Sink
  {
    // ...
    
  }
}
```

which will result in `elementCount` being selected as a source, with a threshold defined based on the `if` conditional:

```C#
#[Vectorize]
public static unsafe nuint NarrowUtf16ToAscii_Stub(char* pUtf16Buffer, byte* pAsciiBuffer, nuint elementCount)
{
  int averageElementCount = Sample(elementCount, elementCountGlobalSample);
  if (averageSpanLength >= 4 * Vector512<byte>.Count)
  {
    // Recompile
  }
  NarrowUtf16ToAscii(pUtf16Buffer, pAsciiBuffer, elementCount);
}
```



#### 3. Codegen Design for Both

##### Transitive Method Codegen

As both proposals allow `Vector<T>` to be specialized per-method, we cannot simply pass `Vector<T>` as an argument to helper methods as before. To address this issue, we propose an additional `[Vectorizeable]` attribute which allows to JIT to specialize the method per selected vector width if used in a `Vectorize.If` or `[Vectorize]`. 

```C#
#[Vectorize]
public int SumVector(ReadOnlySpan<int> span) // <-- Source
{
  Vector<int> vresult = Vector<int>.Zero;

  int lastBlockIndex = span.Length - (span.Length % Vector<int>.Count);
  int i = 0;

  for (i; i < lastBlockIndex; i += Vector<int>.Count)
  {
    vresult += new Vector<int>(span.Slice(i));  // <-- Sink
  }

  int result = ReduceVector(vresult);

  // Handle tail
  while (i < span.Length)
  {
    result += span[(span.Length - rem)+i];
  }

  return result;
}

#[Vectorizeable]
public int ReduceVector(Vector<int> vec)
{
  int result = 0;
  for (int i = 0; i < Vector<int>.Count; i++)
  {
    result += vec.GetElement(i);
  }
  return result;
}

```

will allow the JIT to specialize `ReduceVector` per selected `Vector<T>` vector length. For example, the JIT may create the following

```C#
#[Vectorizeable]
public int ReduceVector_Vector128(Vector128<int> vec)
{
  int result = 0;
  for (int i = 0; i < Vector128<int>.Count; i++)
  {
    result += vec.GetElement(i);
  }
  return result;
}

#[Vectorizeable]
public int ReduceVector_Vector256(Vector256<int> vec)
{
  int result = 0;
  for (int i = 0; i < Vector256<int>.Count; i++)
  {
    result += vec.GetElement(i);
  }
  return result;
}
```

which it will select per vector length selected for `SumVector`.

The JIT will transitively specialize methods marked `#[Vectorizeable]`. Methods that accept `Vector<T>` as a parameter or return a `Vector<T>` _must_ be marked as `#[Vectorizeable]` if used within a `Vectorize.If`, `#[Vectorize]`, or `#[Vectorizeable]` context.

#### Challenges for `Vector<T>`

Currently, `Vector<T>` implementation is determined per-process, and both the templated and pgo approach offer a way to perform per-method implemenation. This poses problems when a `Vector<T>` type flows is created outside but flows into a `Vectorize.If` or `#[Vectorize]` boundary.

Consider the following example, where we create a `Vector<int>` field initialized with some `startVal` but then proceed to use it in the `SumVector` method.

```C#
class Foo()
{
  Vector<int> _mvector;

  public Foo(int startVal)
  {
    _mvector = new Vector<int>(startVal);
  }

  #[Vectorize]
  public int SumVector(ReadOnlySpan<int> span) // <-- Source
  {
    Vector<int> vresult = _mvector;

    int lastBlockIndex = span.Length - (span.Length % Vector<int>.Count);
    int i = 0;

    for (i; i < lastBlockIndex; i += Vector<int>.Count)
    {
      vresult += new Vector<int>(span.Slice(i));  // <-- Sink
    }

    int result = ReduceVector(vresult);

    // Handle tail
    while (i < span.Length)
    {
      result += span[(span.Length - rem)+i];
    }

    return result;
  }
}
```

It's possible that `_mvector` was created using `Vector128` as its underlying implementation, but the JIT selects `Vector256` for `SumVector`. To address this issue, when performing templated/pgo codegen with `Vectorize.If` and `#[Vectorize]` , any uses of `Vector<T>` must be defined from within the "Vectorization" scope, i.e., inside the `Vectorize.If` code block, or inside a `#[Vectorize]` and `#[Vectorizeable]` method.

### New `Vector<T>` API Methods

(TODO: Revisit this once we have some idea of APIs required to mitigate `MoveMask`)

## Q & A

<!--
Features evolve and decisions are being made along the road. Add the question
as a subheading and provide the explanation for the decision below. This way,
you can easily link to specific questions.

When you find yourself having to explain something in a GitHub discussion or in
email, consider to update your proposal and link to your answer instead. This
way, you avoid having to explain the same thing over and over again.
-->