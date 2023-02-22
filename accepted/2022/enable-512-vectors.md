# Enable EVEX and 512-bit Vectors in RyuJIT

.NET support for SIMD acceleration through its Vector APIs (`Vector<T>`, `Vector64`, `Vector128`, and `Vector256`) allow developers to harness the power of SIMD hardware acceleration without expert-level knowledge of underlying complex instruction set architectures and extensive platform-dependent intrinsics. 

We propose to extend .NET support for SIMD acceleration to 512-bit vectors, where `Vector<T>` can transparently select for this wider vector width if available. In addition, we expose a new `VectorMask` API, which allows for a more declarative and programmer-friendly method for performing conditional SIMD operations.

One such realization of 512-bit SIMD acceleration is the `AVX512` family of instruction sets for `X86` architecture which this proposal addresses for implementation details.

**Owner** [Anthony Canino](https://github.com/anthonycanino) 

## Scenarios and User Experience

### Vector<T> Usage

Existing code that utilizes `Vector<T>` to perform vectorized sum over a span might look something like the following snippet:

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

  int result += vresult.Sum();

  // Handle tail
  while (i < source.Length)
  {
    result += source[(source.Length - rem)+i];
  }

  return result;
}
```

where `Vector<T>` might select an internal implementation of 256-bit vectors to perform its SIMD operations. We envision that with the inclusion of 512-bit vectors, `Vector<T>` can transparently select for 512-bit SIMD vectors and operations when generating code.

### VectorMask Usage


For each `Vector` API, we introduce a corresponding `VectorMask`, which abstracts away low-level bit-masking and instead allows to express conditional SIMD processing as boolean logic over `Vector` APIs.

For example, in the following snippet `SumVector`, we perform a conditional add of `Vector` elements if the source `Vector` `v1`'s element is greater than 0 and not equal to 5:


```C#
public Vector<int> SumVector(ReadOnlySpan<int> source)
{
  Vector<int> vresult = Vector<int>.Zero;

  for (int i = 0; i < source.Length;; i += Vector<int>.Count)
  {
    Vector<int> v1 = new Vector<int>(source.Slice(i));
    VectorMask<int> mask = v1.GreaterThanByVectorMask(0) & v1.NotEqualsByVectorMask(5);
    vresult = Vector.Add(vresult, v1, mask);
  }

  return vresult;
}
```

Note that the `VectorMask` is built directly expressing the condition of the source vector `v1`. This roughly translates to a scalar loop...

```C#
for (int i = 0; i < source.Length; i++)
{
  if (source[i] > 0 && source[i] != 5)
    result[i] += source[i];
}
```

Importantly, this conditional logic is not limited to comparing `Vector` against constants. In the following example, we perform a conditional add if the elements of `v1` are less than `v2`, i.e., `v2`:

```C#
public Vector256<int> SumTwoVector256(ReadOnlySpan<int> s1, ReadOnlySpan<int> s2)
{
  Vector256<int> vresult = Vector<int>.Zero;

  for (int i = 0; i < s1.Length;; i += Vector256<int>.Count)
  {
    Vector256<int> v1 = new Vector256<int>(s1.Slice(i));
    Vector256<int> v2 = new Vector256<int>(s2.Slice(i));

    VectorMask256<int> mask = v1.LessThanByVectorMask(v2);
    
    vresult = Vector256.Add(vresult, v1, mask);
  }

  return vresult;
}
```

which roughly translates to the following scalar loop logic...

```C#
for (int i = 0; i < s1.Length; i++)
{
  if (s1[i] < 0 && s2[i])
    result[i] += s1[i];
}
```

As `VectorMask256` expresses that we have a masked condition for `Vector256`, so to will we have `VectorMask128`, and `VectorMask64`. In addition, when using the variable length `Vector<T>` API, we have `VectorMask<T>`, as seen the in the following example:


```C#
public Vector<int> SumVector(ReadOnlySpan<int> source)
{
  Vector<int> vresult = Vector<int>.Zero;

  for (int i = 0; i < source.Length;; i += Vector<int>.Count)
  {
    Vector<int> v1 = new Vector<int>(source.Slice(i));
    VectorMask<int> mask = v1.GreaterThanByVectorMask(0) & v1.NotEqualByVectorMask(5);
    vresult = Vector.Add(vresult, v1, mask);
  }

  return vresult;
}
```

Where `VectorMask<T>` expresses that the number of elements the condition applies to is variable-length, and determined by the JIT at runtime (though it must be compatible with `Vector<T>` selected length).

Lastly, we propose to create a `VectorMask` using builtin C# boolean expressions by passing a lambda to a special `MaskExpr` API:

```C#
public Vector256<int> SumVector256(ReadOnlySpan<int> source)
{
  Vector256<int> vresult = Vector256<int>.Zero;

  for (int i = 0; i < source.Length;; i += Vector256<int>.Count)
  {
    Vector256<int> v1 = new Vector256<int>(source.Slice(i));
    VectorMask256<int> mask = v1.MaskExpr(x => x > 0 & x != 5);
    vresult = Vector256.Add(vresult, v1, mask);
  }

  return vresult;
}
```

Logically, the lambda passed to `MaskExpr` selects for which elements of `v1` to include in the `VectorMask`, and allows developers to program conditional SIMD logic with familiar boolean condition operations.

### Leading/Trailing Element Processing with `VectorMask<T>`

The `VectorMask<T>` API allows to perform leading and trailing element processing on SIMD vectors, which becomes particularly useful when operating with wider-length SIMD vectors.

For the following example, let us assume our `source` span contains 7 integers (source.Length = 8), such as [0 1 2 3 4 5 6]. We include some comments in the code for clarity:

```C#
public int SumVector(ReadOnlySpan<int> source)
{
  Vector128<int> vresult = Vector128<int>.Zero;

  int lastBlockIndex = source.Length - (source.Length % Vector128<int>.Count);
  int i = 0;

  for (i; i < lastBlockIndex; i += Vector128<int>.Count)
  {
    vresult += new Vector<int>(source.Slice(i));
  }

  // vresult = [0 1 2 3]

  VectorMask128<int> trailMask = VectorMask128<int>.CreateTrailingMask(source.Length - lastBlockIndex);
  Vector128<int> tail = Vector128<int>.LoadTrailing(source.Slice(lastBlockIndex), trailMask);

  // tail = [4 5 6 X]

  vresult = Vector128<int>.Add(vresult, tail, trailMask);

  // vresult = [4 6 8 3]

  // Handle reduce
  int result = vresult.Sum();

  return result;
}
```

The JIT decides how to implement the `LoadTrailing` and condition operations depending upon hardware features. 

On architectures where masked loads are supported, `VectorMask<T>` allows to easily load just a partial vector, and perform conditional SIMD processing using the same mask (as seen in the example above). On architectures where masked loads are not supported, the JIT can backtrack the source point in `LoadTrailing` to load a full vector (where some of the vector is redundant from the last loop iteration) and corresponding adjust the `VectorMask128<int>`, something more akin to: 

```C#
// vresult = [0 1 2 3]

VectorMask128<int> trailMask = VectorMask128<int>.CreateTrailingMask(source.Length - lastBlockIndex);
Vector128<int> tail = Vector128<int>.LoadTrailing(source.Slice(lastBlockIndex), trailMask);

// tail = [3 4 5 6]
// tail = [0 1 1 1] & tail

vresult = Vector128<int>.Add(vresult, tail, trailMask);

// vresult = [0 5 7 9]

```

In addition, `StoreTrailing` functions analogously to `LoadTrailing`. We also propose `CreateLeadingMask`, `LoadLeading`, and `StoreLeading` which function analogously to `CreateTrailingMask`, `CreateTrailing` and `StoreTrailing` respectively.

## Requirements

### Goals

1. Enable 512-bit vector support in .NET.

2. Expose type safe and expressible API for declaring conditional SIMD processing.

### Non-Goals

1. No automatic vectorization is performed. Conditional SIMD processing done through declarative APIS.

2. Focus of the work is on enabling 512-bit SIMD acceleration and `VectorMask`, including optimization passes needed to make `VectorMask` performant on all platforms. Additional optimizations enabled by `EVEX` encoding (such as folding multiple SIMD operations into a single embedded broadcast ) are desired but outside the scope of the initial work.


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

### New API Methods and Types

#### `VectorMask<T>` Type and API Methods

`VectorMask<T>` is meant to provide an API for working with conditional SIMD processing. Like `Vector<T>`, `VectorMask<T>` is defined generically, where its type parameter `T` represents the type of the elements that the condition applies to within a SIMD vector. This allows to both consider conditions as a "boolean" logic, but also perform lower-level processing and vector element selection typically seen in vectorized code.

For example, in the following code snippet, a vector `vector` is checked against multiple candidate vectors `v1`, `v2`, and `v3`. The results are `or` together and converted to a `byte` vector so we can extract a bitmask of the most significant bits. The goal is to iterate through the elements of the `source` vector that are two.

```C#
Vector128<ushort> vector = Vector128.LoadUnsafe(ref source, offset);
Vector128<ushort> v1Eq = Vector128.Equals(vector, v1);
Vector128<ushort> v2Eq = Vector128.Equals(vector, v2);
Vector128<ushort> v3Eq = Vector128.Equals(vector, v3);
Vector128<byte> cmp = (v1Eq | v2Eq | v3Eq).AsByte();

if (cmp != Vector128<byte>.Zero)
{
    // Skip every other bit
    uint mask = cmp.ExtractMostSignificantBits() & 0x5555; 
    do
    {
        uint bitPos = (uint)BitOperations.TrailingZeroCount(mask) / sizeof(char);
        sepListBuilder.Append((int)(offset + bitPos));
        mask = BitOperations.ResetLowestSetBit(mask);
    } while (mask != 0);
}
```

However, this is cumbersome for the developer for two reasons: the first, is that because the bitmask is built from the most significant bit of each `byte` element in a vector, we first have to mask off every other element in the mask with `0x5555` to link this properly back to our vector of `ushort`; the second, is when we perform a `TrailingZeroCount` (to get the first pos of the first true element in the `byte` vector) we have to _map it back_ to an offset in the `ushort` vector (done by dividing `sizeof(char)).

The following example shows how `VectorMask128` allows to perform the same functionality without dealing with these lower level details:

```C#
Vector128<ushort> vector = Vector128.LoadUnsafe(ref source, offset);
VectorMask128<ushort> v1Eq = Vector128.EqualsByVectorMask(vector, v1);
VectorMask128<ushort> v2Eq = Vector128.EqualsByVectorMask(vector, v2);
VectorMask128<ushort> v3Eq = Vector128.EqualsByVectorMask(vector, v3);
VectorMask128<ushort> cmp = (v1Eq | v2Eq | v3Eq);

if (cmp != VectorMask128<ushort>.Zero)
{
    // Skip every other bit
    do
    {
        uint elmPos = cmp.FirstIndexOf(true);
        sepListBuilder.Append((int)(offset + elmPos));
        cmp = cmp.SetElementCond(elmPos, false);
    } while (cmp != 0);
}
```

`VectorMask128<ushort>` properly links conditional SIMD processing with `Vector128<ushort>`. The JIT best determines how to lower the `VectorMask` API based on available hardware of the system; however, because `VectorMask` encodes conditional length and element type, it allows the JIT to lower the code optimally.

The following lists the new classes we propose;


| Class  | Associated `Vector` API | 
| ------ | ---------- |
| `VectorMask64<T>`  | `Vector64<T>`  | 
| `VectorMask128<T>` | `Vector128<T>` | 
| `VectorMask256<T>` | `Vector256<T>` | 
| `VectorMask<T>`    | `Vector<T>`    |  

The following lists the methods for `VectorMask<T>` API that allow for combining `VectorMask<T> into complex conditions, e.g., boolean logic:

| Method  |
| ------ | 
| `VectorMask<T> VectorMask<T>.And(VectorMask<T>, VectorMask<T>)`  | 
| `VectorMask<T> VectorMask<T>.Or(VectorMask<T>, VectorMask<T>)`  | 
| `VectorMask<T> VectorMask<T>.Not(VectorMask<T>, VectorMask<T>)`  | 
| `VectorMask<T> VectorMask<T>.Xor(VectorMask<T>, VectorMask<T>)`  | 

The following lists the methods that allow to use `VectorMask<T>` for some lower-level processing typically done in SIMD code

(OPEN: What else?)

| Method  |
| ------ | 
| `VectorMask<T> VectorMask<T>.FirstIndexOf(VectorMask<T> mask, bool val)`  | 
| `VectorMask<T> VectorMask<T>.SetElemntCond(VectorMask<T> mask, ulong pos, bool val)` | 


#### Conditional Processing API Methods

As `VectorMask<T>` allows to build conditional logic over SIMD `Vector`, we propose the following additions to the `Vector` API that allow to related one vector to another:

| Method  |
| ------ | 
| `VectorMask<T> Vector<T>.EqualsByVectorMask(Vector<T> v1, Vector<T> v2)`  | 
| `VectorMask<T> Vector<T>.NotEqualsByVectorMask(Vector<T> v1, Vector<T> v2)`  | 
| `VectorMask<T> Vector<T>.GreaterThanByVectorMask(Vector<T> v1, Vector<T> v2)`  | 
| `VectorMask<T> Vector<T>.GreaterThanEqualsByVectorMask(Vector<T> v1, Vector<T> v2)`  | 
| `VectorMask<T> Vector<T>.LessThanByVectorMask(Vector<T> v1, Vector<T> v2)`          | 
| `VectorMask<T> Vector<T>.LessThanEqualsByVectorMask(Vector<T> v1, Vector<T> v2)`    |  

We overload each method to make it easier to compare vectors against constants:

| Method  |
| ------ | 
| `VectorMask<T> Vector<T>.EqualsByVectorMask(Vector<T> v1, T val)`  | 
| `VectorMask<T> Vector<T>.NotEqualsByVectorMask(Vector<T> v1, T val)`  | 
| `VectorMask<T> Vector<T>.GreaterThanByVectorMask(Vector<T> v1, T val)`  | 
| `VectorMask<T> Vector<T>.GreaterThanEqualsByVectorMask(Vector<T> v1, T val)`  | 
| `VectorMask<T> Vector<T>.LessThanByVectorMask(Vector<T> v1, T val)`          | 
| `VectorMask<T> Vector<T>.LessThanEqualsByVectorMask(Vector<T> v1, T val)`    |  

In order to make use of the `VectorMask<T>` for conditional processing, we propose to extend the `Vector` operation API with overloaded methods to consume a `VectorMask`, e.g. `Vector<T>`:

| Method  |
| ------ | 
| `VectorMask<T> Vector<T>.Add(Vector<T> v1, Vector<T> v2, VectorMask<T> cond)` | 
| `VectorMask<T> Vector<T>.And(Vector<T> v1, Vector<T> v2, VectorMask<T> cond)` | 

(OPEN: What API methods to extend here?)



#### MaskExpr API

`MaskExpr` is meant to start a small domain specific language (DSL) for Vector SIMD processing. To keep the implementation simple, we choose to directly use a `C#` expression (embedded DSL) encoded through a lambda. 

In the following example:

```C#
public Vector256<int> SumVector256(ReadOnlySpan<int> source)
{
  Vector256<int> vresult = Vector256<int>.Zero;

  for (int i = 0; i < source.Length;; i += Vector256<int>.Count)
  {
    Vector256<int> v1 = new Vector256<int>(source.Slice(i));
    VectorMask256<int> mask = v1.MaskExpr(x => x > 0 & x != 5);
    vresult = Vector256.Add(vresult, v1, mask);
  }

  return vresult;
}
```

to JIT should logically create a `VectorMask` equivalent to `v1.GreaterThanByVectorMask(0) & v1.NotEqualByVectorMask(5)`.

Given a usage of `MaskExpr`, `v.MaskExpr(x => e)`, where `v` represents a `Vector` and `e` represents an expression:

- `v` defines the vector that we perform the conditional SIMD for (the implementation determines how the masking must be done)

- `x` can be logically thought of as `v[i]` and is used to hint to the JIT on how to build the mask.

- `e` defines an expression that the JIT will lift up in an appropriate masking. `e` must be a boolean expression, and is restricted to the standard boolean comparision operations.

- If `e` does not type check to a boolean expression, and uses expressions other than boolean comparison operations and `x`, the JIT will throw `OperationNotSupportedError`.

There are more advanced uses of a true "embedded DSL" and if the idea seems applicable, we can extend `MaskExpr` to handle many more interesting cases (at the cost of more advanced lifting from C# expression to something the JIT can work with).

#### Leading and Trailing Processing API Methods

We propose the following methods for each `Vector` API for processing leading and trailing elements. 

| Method  |
| ------ | 
| `VectorMask<T> Vector<T>.CreateLeadingMask(int rem)` | 
| `VectorMask<T> Vector<T>.LoadLeading(Span<T> v1, VectorMask<T> mask)` | 
| `VectorMask<T> Vector<T>.StoreLeading(Span<T> v1, VectorMask<T> mask)` | 
| `VectorMask<T> Vector<T>.CreateTrailingMask(int rem)` | 
| `VectorMask<T> Vector<T>.LoadTrailing(Span<T> v1, VectorMask<T> mask)` | 
| `VectorMask<T> Vector<T>.StoreTrailing(Span<T> v1, VectorMask<T> mask)` | 

### Internals Upgrades for EVEX/AVX512 Enabling

Broadly speaking, we can break the implementation of 512-bit vectors and `VectorMask` into the following components:

0. Enable VM recogniztion and tracking of the AVX-512 ISAs

1. Enable EVEX encoding for Vector128/Vector256 in the xarch emitter.

2. Extend register support for additional 16 registers

3. Introduce 512-bit SIMD types, extend register support to them, and allow for `Vector<T>` to lower to 512-bit SIMD types.

4. Introduce `VectorMask` API, associated types, and extend register support to mask registers

In particular, implementing (1) above allows for the most isolated set of changes and lays the foundation for the remaining work: EVEX encoding can be implemented for the Vector128  and Vector256 for any `AVX512VL` instructions without requiring the addition of new types and registers to the dotnet runtime. 

We expand on each item in turn below:

#### Enable VM Recognition of AVX-512 ISAs



#### Enable EVEX for Vector128/Vector256

`AVX512VL` allows to use `EVEX` encoding (which includes AVX512 instructions, opmask registers, embedded broading etc.) on 128-bit and 256-bit SIMD registers. As the infrastructure for 128-bit and 256-bit SIMD types is already present in the entire runtime and JIT, enabling `EVEX` for 128-bit and 256-bit SIMD types allows to lay the foundation for the rest of the 512-bit and vector mask enabling work in isolation, i.e., the initial `EVEX` encoding work will be done in the `xarch` code emitter. This allows to develop and test the changes to the xarch emitter before introducing further reaching changes to the JIT which we discuss below.



#### Enable Register Support for Additional 16 Registers

As AVX512 and EVEX allows for an additional 16 SIMD registers (`mm16`-`mm32`) the register allocator and runtime need to be expanded to support the greater number of registers.

1. Expand the number of registers the runtime and JIT understand.

2. Introduce support to the runtime to properly allow for context switching across managed and native boundaries with the new registers.

3. Introduce debugger support for the new registers.

This will lay foundation for adding 512-bit register support (as 128-bit, 256-bit, and 512-bit share the same registers on x86) and provide insight into adding the opmask register support.

#### Internals Upgrades for 512-bit Vectors

Once the framework for EVEX encoding is in place for the xarch codegen pipeline, introducing 512-bit vectors consists of the following modifications

1. Introduction of a 64 byte type (TYP_SIMD64) to the runtime, analogous to the types defined for 32 byte and 16 byte SIMD vectors.

2. Expanding the register allocator to allocate registers for the new 64 byte types.

3. Define the 512-bit vector operations (realized through code generation and intrinsics).

4. Allow `Vector<T>` to select for 512-bit vectors as its underlying implementation.

As we propose to develop 512-bit vectors as an instantiation of the `AVX512` ISA for x86/x64 architecture, most of the initial modification will take place in the xarch portion of the JIT, particularly in the lowering, codegen, and emitting stages. Adding the `EVEX` prefix to the xarch emitter (which allows to generate AVX512 family of instructions) will consume most of the initial work. 

`EVEX` encoding allows for more features than just emitting `AVX512` family of instructions; however, as a first pass, we propose to implement the minimum EVEX encoding to generate the instructions necessary to implement 512-bit vectors operations for `Vector<T>`. This represents an "MVP" for 512-bit vectors and EVEX encoding in RyuJIT. 

We leave the discussion of how `Vector<T>` selects 512-bit vectors as its underlying implement to another proposal ["enhance vector codegen"](https://github.com/anthonycanino/designs/blob/main/accepted/2022/enhance-vector-codegen.md), however, one simple approach is to introduce a flag to enable `Vector<T>` to select for 512-bit vectors as default.

In the following section, we detail the additional features of `EVEX` encoding that we plan to incorporate into RyuJIT to realize the full power of AVX512, but these will be added after the MVP. 

#### Internals Upgrades for `VectorMask<T>`

1. Introduction of a new vector mask type (`TYP_VECTORMASK8`, `TYP_VECTORMASK16`, `TYP_VECTORMASK32`, and `TYP_VECTORMASK64`) to the runtime. 

    - Similar to the API/implementation of `VectorXX<T>`, `VectorMaskXX<T>` `XX` expresses the number width of the condition, and `<T>` expresses the size of each element the condition applies to.

    - This encoding allows the JIT to address implementation details such as building the proper masks and associated offsets for a width and underlying element, and linking those back to a SIMD `Vector`.

2. Expanding the register allocator to allocate AVX512 opmask registers for the new vector mask types.

3. Introduce JIT compiler passes for handling `VectorMask<T>` conditionals and leading and trailing masks.

4. Introduce fallback JIT compiler pass that will allow `VectorMask<T>` to degenerate into `Vector<T>` and less efficient operations but still allow for the `VectorMask<T>` API to be used on all architectures.

5. Extend the `xarch` emitter to use `EVEX` encoding for opmask registers when using the for the conditional `Vector` API.

6. Implement `MaskExpr` DSL interpretation (lift provided C# expression into internals used for the rest of the `VectorMask` operations).


### Alternative Design

In this design, we have focused on using `Vector<T>` as a vessel to realize 512-bit vectors in .NET. Orthogonally, an explicit public `Vector512` API may be introduced analogous to `Vector256`, `Vector128`, and `Vector64`, which allows developers to explicitly operate with 512-bit vectors.

For example:

```C#
public int SumVector512(ReadOnlySpan<int> source)
{
  Vector512<int> vresult = Vector512<int>.Zero;

  int lastBlockIndex = source.Length - (source.Length % Vector512<int>.Count);
  int i = 0;

  for (i; i < lastBlockIndex; i += Vector512<int>.Count)
  {
    vresult += new Vector512<int>(source.Slice(i));
  }

  int result += vresult.Sum();

  // Handle tail
  while (i < source.Length)
  {
    result += source[(source.Length - rem)+i];
  }

  return result;
}
```

We expect developers who manually write `Vector128<T>` and `Vector256<T>` code to use `Vector512<T>` with minimal effort.

## Q & A

<!--
Features evolve and decisions are being made along the road. Add the question
as a subheading and provide the explanation for the decision below. This way,
you can easily link to specific questions.

When you find yourself having to explain something in a GitHub discussion or in
email, consider to update your proposal and link to your answer instead. This
way, you avoid having to explain the same thing over and over again.
-->
