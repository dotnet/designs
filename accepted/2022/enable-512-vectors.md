# Upgrade `Vector` API SIMD Acceleration Capability in .NET

**Owner** [Anthony Canino](https://github.com/anthonycanino) 

.NET support for SIMD acceleration through `Vector<T>` API allows developers to transparently harness the power of advanced SIMD ISAs without expert-level knowledge of low-level hardware details and optimization techniques. Given its variable-length nature, `Vector<T>` allows to adapt to the most performant SIMD ISA implementation available for the platform. However, internal .NET libraries use `Vector<T>` as a fallback SIMD code path, where hand-optimized intrinsic code paths, e.g, SSE or AVX2, provide more performance due to `Vector<T>` platform-agnostic API. 

This is problematic for a number of reasons:

  1. As new SIMD ISAs release, .NET developers must write additional SIMD code paths *per optimized library*, increasing code complexity and maintainence burden. At best, much time has to be dedicated to these libraires; at worst, libraries do not get upgraded for latest hardware advancements.

  2. Hand-optimized intrinsics lock software into a specific ISA, often with fixed thresholds for determining when to select a SIMD accelerated codepath for performance gains. In a JIT environment where we can glean performance characteristics of the underlying platform, intrinsics prevent adapting to the most _performant_ SIMD ISA available for a given _workload_.

In this design document, we propose to extend `Vector<T>` to serve as a vessel for frictionless SIMD adoption, both internal to .NET libraries, and to external .NET developers. As a realization of this goal, we propose the following:

  1. Upgrading `Vector<T>` to serve as a sufficiently powerful interface for writing both internal hardware accelerated libraries and external developer code. 

  2. Introducing `Vector512<T>`, analogous to `Vector128<T>` and `Vector256<T>` but for 512-bit SIMD ISA generation. 

  3. Allowing for `Vector<T>` to select the best available `VectorXX` for the platform, with support for dynamically selecting the vector width based on information available at runtime, e.g,. the size of the `Span` being vectorized.

## Scenarios and User Experience

### `Vector<T>` as an SIMD optimization template

The following code snippet --- taken from the fallback optimization path of UTF16 to ASCII narrowing --- posses problems as the _sole_ SIMD optimization pathway in the `ASCIIUtility` library. Namely, the check on whether we should vectorize or not will create different performance characteristics depending upon the size the JIT chooses for `Vector<T>`. If `Vector256` is choosen, then buffers with 64 elements and above will be optimized; if `Vector128` is chosen, then buffers of 32 elements and above will be optimized. 


```C#
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
```

This prevents the JIT from generating code from `Vector<T>` with consistent performance behavior, both internal to .NET libraries and for external .NET developers. Partciularly with the addition of `Vector512`, workloads that previously would have been optimized would no longer clear the threshold. To aid `Vector<T>` to act as a single generic SIMD framework, we propose to add a `Vectorize.If` intrinsic that instructs the JIT to generate _multiple_ SIMD acceleration pathways. 

For example, in the following snippet:

```C#
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
```

the JIT to generate would generate the following depending upon the availability of the SIMD ISA of the platform:

```C#
// Only bother vectorizing if we have enough data to do so.
if (elementCount >= 2 * Vector512<byte>.Count)
{
  // Execute a Vector512 pathway
  Vector512<ushort> maxAscii = new Vector512<ushort>(0x007F);

  // ...
}
else if (elementCount >= 2 * Vector256<byte>.Count)
{
  // Execute a Vector512 pathway
  Vector256<ushort> maxAscii = new Vector256<ushort>(0x007F);

  // ...
}
else if (elementCount >= 2 * Vector128<byte>.Count)
{
  // Execute a Vector512 pathway
  Vector128<ushort> maxAscii = new Vector128<ushort>(0x007F);

  // ...
}
```

In this way, we can collapse multiple SIMD ISA checks into a single "template" SIMD ISA code which the JIT may expand. We use a new `Vectorize.If` intrinsic that allows backward compatability with `Vector<T>`, i.e., a `Vectorize.If` JIT intrinsic instructs the JIT that the code inside its block is a template for multiple vector sizes. If `Vectorize.If` is not present, `Vector<T>` will select the best ISA for the platform determined by the JIT.

### Additional `Vector<T>` Methods for Near-Intrinsic Performance

In the following example:

```C#
public int SumVector(ReadOnlySpan<int> source)
{
  Vector<int> vresult = Vector<int>.Zero;

  int lastBlockIndex = source.Length - (source.Length % Vector<int>.Count);
  int i = 0;

  for (i; i < lastBlockIndex; i += Vector<int>.Count)
  {
    vresult += new Vector<int>(source.Slice(i));
  }

  int result = 0;
  for (int j = 0; j < Vector<int>.Count; j++)
  {
    result += vresult[j];
  }

  // Handle tail
  while (i < source.Length)
  {
    result += source[(source.Length - rem)+i];
  }

  return result;
}
```

The reduction loop will cost much of the performance gain from the vectorized loop. If we use fixed size vectors and hardware intrinsics, we can perform the reduction via horizontal adds, but this locks us to specific SIMD ISAs. Instead, we propose adding methods in these cases, i.e.,

```C#
public int SumVector(ReadOnlySpan<int> source)
{
  Vector<int> vresult = Vector256<int>.Zero;

  int lastBlockIndex = source.Length - (source.Length % Vector<int>.Count);
  int i = 0;

  for (i; i < lastBlockIndex; i += Vector256<int>.Count)
  {
    vresult += new Vector<int>(source.Slice(i));
  }

  result += vresult.ReduceAdd();

  // Handle tail
  while (i < source.Length)
  {
    result += source[(source.Length - rem)+i];
  }

  return result;
}
```

which will generate code to handle the necessary reduction per platform. 

### Vector512<T> Usage

Exhisting code that utilizes `Vector256<T>` to perform vectorized sum over a span might look something like the following snippet (assuming the addition of a `ReduceAdd` method as described above):

```C#
public int SumVector256(ReadOnlySpan<int> source)
{
  Vector256<int> vresult = Vector<int>.Zero;

  int lastBlockIndex = source.Length - (source.Length % Vector256<int>.Count);
  int i = 0;

  for (i; i < lastBlockIndex; i += Vector256<int>.Count)
  {
    vresult += new Vector256<int>(source.Slice(i));
  }

  result += vresult.ReduceAdd();

  // Handle tail
  while (i < source.Length)
  {
    result += source[(source.Length - rem)+i];
  }

  return result;
}
```

Like the above snippet, the `Vector512<T>` API would function in much the same way, with most of the `Vector256` API simply replaced by `Vector512` methods:


```C#
public int SumVector512(ReadOnlySpan<int> source)
{
  Vector512<int> vresult = Vector<int>.Zero;

  int lastBlockIndex = source.Length - (source.Length % Vector512<int>.Count);
  int i = 0;

  for (i; i < lastBlockIndex; i += Vector512<int>.Count)
  {
    vresult += new Vector512<int>(source.Slice(i));
  }

  result += vresult.ReduceAdd();

  // Handle tail
  while (i < source.Length)
  {
    result += source[(source.Length - rem)+i];
  }

  return result;
}
```

We expect developers who manually write `Vector128<T>` and `Vector256<T>` code to use `Vector512<T>` with minimal effort.

## Requirements

### Goals

1. `Vector<T>` should allow to generate code that reaches performance within some threshold of hand-optimized intrinsics. 

2. In combination with the JIT, `Vector<T>` written code should allow to adapt to the best performance of underlying platform, i.e., the JIT may generate multiple codepaths and thresholds that select at runtime which SIMD ISA to use.

3. `Vector<T>` API surface should include sufficiently expressive methods to reach the first goal above. 

4. `Vector512<T>` should expose at a minimum the same API operations that `Vector128<T>` and `Vector256<T>` expose.


### Non-Goals


1. For the JIT to generate alternative SIMD pathways given a `Vectorize` and `Vector<T>` code block, it should not rely on autovectorization techniques or advanced analysis. `Vectorize` is meant to serve as a hint to treat `Vector<T>` as a template for multiple ISAs given some thresholds.


## Stakeholders and Reviewers

[Tanner Gooding]() 

[Bruce Forstall]() 

[Drew Kersnar]() 


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

#### `Vectorize` Code Block

- The presence of `Vectorize.If` in the conditional of an `if` statement drives the expansion, which creates a `Vectorize` code block.

- The `Vectorize.If` intrinsic accepts a boolean expression as a parameter. The boolean expression must contain a reference to `Vector`, as the `Vector` will be replaced per arm as discussed below and allows the selection of alternative code paths. If no reference to `Vector` is found, the JIT will throw an `OperationNotSupportedException`.

- When the JIT encounters a `Vectorize.If` intrinsic in a conditional block, the JIT will:

  - Clone the if condition and associated arm with the `Vectorize.If`, creating an arm for each available vector length on the platform. 

  - In each cloned arm any `Vector<T>` operations will be translated to `VectorXX<T>` operations, where XX represents the vector length for that arm.

  - Any remaining `else if` and `else` arms remain in place.

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
      else
      {
        for (int i = 0; i < sp.Length; i++)
        {
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
        for (int i = 0; i < sp.Length; i++)
        {
          // ...
        }
      }
    }
    ```

- If the JIT encounters a `Vectorize.If` outside of an `if` conditional, the JIT will throw an `OperationNotSupportedException`.

- If the JIT encounters `Vectorize.If` in multiple arms of the conditional, the JIT will throw an `OperationNotSupportedException`.

- Any fixed length VectorXX will not be adjusted in any way if present in a `Vectorize.If` block.

- Nested `Vectorize.If` is allowed. Expansion begins at the most nested block.

### New `Vector<T>` API Methods

We propose that `Vector<T>` be extended with methods that help faciliate its use as a cross platform generic variable length vector to help reduce situations that require casting `Vector` to a fixed-length vector for hand optimization with ISA-specific intrincis. 

One such case is a reduction of the elements of the vector, which requires hand optimization per vector size.

We propose the following methods:

- `ReduceXX`, where `XX` is the reduction operation, e.g., `Add`, where the JIT will generate performant reduction code for the ISA.

(Anthony: Discuss APIs for trailing / tail elements)

### `Vector512<T>` API Methods

The `Vector512<T>` API surface should be defined as the same API that `Vector128` and `Vector256` expose, with the types and operations adjusted for the 512-bit vector length. 

### `Internals Upgrades for Vector512<T>`

Adding 512 bit width vectors to `RyuJIT` largely consists of the following (generic) modifications:

1. Introduction of a 512 bit (64 byte) type to the runtime, analogous to the types defined for 256-bit and 128-bit SIMD vectors.

2. Expanding the register allocator to allocate registers for the new 64 byte types.

3. Design the `Vector512<T>` API surface.

4. Define the `Vector512` operations (realized through code generation and intrinsics).

5. Extending the code emitters to emit instructions for the associated 512 bit ISA.

As we propose to develop `Vector512<T>` as an instantiation of the `AVX512` ISA for x86/x64 architecture, most of the initial modification will take place in the xarch porition of the JIT, particularly in the lowering, code gen, and emitting stages. Adding the `EVEX` prefix to the xarch emitter (which allows to generate AVX512 family of instructions) will consume most of the initial work. 

`EVEX` encoding allows for more features than just emitting `AVX512` family of instructions; however, as a first pass, we propose to implement the mininum EVEX encoding to generate the instructions for the `Vector512<T>` API surface above. This represents an "MVP" for 512-bit vectors and EVEX encoding in RyuJIT. 

In the following section, we detail the additional features of `EVEX` encoding that we plan to incorporate into RyuJIT to realize the full power of AVX512, but these will be added after the MVP. 

### Further Internal Upgrades Specific to `AVX512`/`EVEX` for Additional Optimizations

Once the framework for `EVEX` encoding is implemented in the xarch code emitter, we propose to introduce the following capabilities of `EVEX` into RyuJIT:

- Opmask registers for masking `AVX512` instructions. 

- Embedded broadcasts and explicit rounding control.

- `AVX512VL` which extends much of the exhisting AVX512 instructions and capabilities, e.g., embedded broadcast, to 128-bit and 256-bit vector registers.

As `EVEX` capability will be part of the xarch code emitter, most of the work to realize the aformentioned features will involve:

1. Introduction of a masked register type to the runtime.

2. Define an API associated with the masked register type, e.g., `KMask8`, which reprents an 8-bit mask value to be assigned a mask register.

3. Expand the register allocator to allocate mask registers for the masked register types, whether generated by the JIT or programmatically declared via the API and intrinsics.

4. Extend instruction descriptors to encode the the presence of: (1) opmask register; (2) embedded broadcasti; and (3) explicit rounding.

5. Add the additional code paths to decide whether to emit VEX or EVEX prefix for 128-bit, 256-bit, or 512-bit vector instructions given the features in (4) above.

These upgrades will allow the following optimizations:

- Folding of a scalar load, SIMD broadcast, then SIMD opertion into a single embedded load, for 128-bit, 256-bit, and 512-bit vector lengths.

- Accelerated processing of trailing elements with an opmask that performs a partial load and partial operation on a vector register.

- Accelerated scalar operations, e.g., computation of approximate recipriocals, conversion of double float to unsigned long, with 

## Q & A

<!--
Features evolve and decisions are being made along the road. Add the question
as a subheading and provide the explanation for the decision below. This way,
you can easily link to specific questions.

When you find yourself having to explain something in a GitHub discussion or in
email, consider to update your proposal and link to your answer instead. This
way, you avoid having to explain the same thing over and over again.
-->