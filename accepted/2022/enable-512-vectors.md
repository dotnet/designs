# Enable EVEX and Vector512 in RyuJIT

(TODO: Fill in)

**Owner** [Anthony Canino](https://github.com/anthonycanino) 

## Scenarios and User Experience

### Vector512<T> Usage

Existing code that utilizes `Vector256<T>` to perform vectorized sum over a span might look something like the following snippet (assuming the addition of a `ReduceAdd` method as described above):

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


1. For the JIT to generate alternative SIMD pathways given a `Vectorize` and `Vector<T>` code block, it should not rely on auto vectorization techniques or advanced analysis. `Vectorize` is meant to serve as a hint to treat `Vector<T>` as a template for multiple ISAs given some thresholds.


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

### `Vector512<T>` API Methods

The `Vector512<T>` API surface should be defined as the same API that `Vector128` and `Vector256` expose, with the types and operations adjusted for the 512-bit vector length. 

### Internals Upgrades for EVEX/AVX512 Enabling

Broadly speaking, we can break the implementation of EVEX/Vector512 enabling into the following components:

1. Enable EVEX encoding for Vector128/Vector256 in the xarch emitter.

2. Extend register support for additional 16 registers

3. Extend register support to Vector512

4. Extend register support to Mask Registers

In particular, implementing (1) above allows for the most isolated set of changes and lays the foundation for the remaining work: EVEX encoding can be implemented for the Vector128  and Vector256 for any `AVX512VL` instructions without requiring the addition of new types and registers to the dotnet runtime. 


We expand on each item in turn below:

### Enable EVEX for Vector128/Vector256

### Enable Register Support for Additional 16 Registers

### `Internals Upgrades for Vector512<T>`

Once the framework for EVEX encoding is in place for the xarch codegen pipeline, introducing `Vector512` consists of the following modifications

1. Introduction of a 512 bit (64 byte) type to the runtime, analogous to the types defined for 256-bit and 128-bit SIMD vectors.

2. Expanding the register allocator to allocate registers for the new 64 byte types.

3. Design the `Vector512<T>` API surface.

4. Define the `Vector512` operations (realized through code generation and intrinsics).

As we propose to develop `Vector512<T>` as an instantiation of the `AVX512` ISA for x86/x64 architecture, most of the initial modification will take place in the xarch porition of the JIT, particularly in the lowering, code gen, and emitting stages. Adding the `EVEX` prefix to the xarch emitter (which allows to generate AVX512 family of instructions) will consume most of the initial work. 

`EVEX` encoding allows for more features than just emitting `AVX512` family of instructions; however, as a first pass, we propose to implement the minimum EVEX encoding to generate the instructions for the `Vector512<T>` API surface above. This represents an "MVP" for 512-bit vectors and EVEX encoding in RyuJIT. 

In the following section, we detail the additional features of `EVEX` encoding that we plan to incorporate into RyuJIT to realize the full power of AVX512, but these will be added after the MVP. 

### `Internals Upgrades for additional mask registers

### Further Internal Upgrades Specific to `AVX512`/`EVEX` for Additional Optimizations

Once the framework for `EVEX` encoding is implemented in the xarch code emitter, we propose to introduce the following capabilities of `EVEX` into RyuJIT:

- Opmask registers for masking `AVX512` instructions. 

- Embedded broadcasts and explicit rounding control.

- `AVX512VL` which extends much of the existing AVX512 instructions and capabilities, e.g., embedded broadcast, to 128-bit and 256-bit vector registers.

As `EVEX` capability will already be part of the xarch code emitter, most of the work to realize the aforementioned features will involve:

1. Introduction of a masked register type to the runtime.

2. Define an API associated with the masked register type, e.g., `KMask8`, which represents an 8-bit mask value to be assigned a mask register.

3. Expand the register allocator to allocate mask registers for the masked register types, whether generated by the JIT or programmatically declared via the API and intrinsics.

4. Extend instruction descriptors to encode the the presence of: (1) an opmask register; (2) embedded broadcasting; and (3) explicit rounding.

5. Add the additional code paths to decide whether to emit VEX or EVEX prefix for 128-bit, 256-bit, or 512-bit vector instructions given the features in (4) above.

These upgrades will allow the following optimizations:

- Folding of a scalar load, SIMD broadcast, then SIMD operation into a single embedded load, for 128-bit, 256-bit, and 512-bit vector lengths.

- Accelerated processing of trailing elements with an opmask that performs a partial load and partial operation on a vector register.

- Accelerated scalar operations, e.g., computation of approximate reciprocals, conversion of double float to unsigned long.

## Q & A

<!--
Features evolve and decisions are being made along the road. Add the question
as a subheading and provide the explanation for the decision below. This way,
you can easily link to specific questions.

When you find yourself having to explain something in a GitHub discussion or in
email, consider to update your proposal and link to your answer instead. This
way, you avoid having to explain the same thing over and over again.
-->