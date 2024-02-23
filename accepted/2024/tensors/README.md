# Tensors

**Owners** [Tanner Gooding](https://github.com/tannergooding) | [Michael Sharp](https://github.com/michaelgsharp) | [Luis Quintanilla](https://github.com/luisquintanilla)

[!NOTE]
This is a continuation of https://github.com/dotnet/runtime/issues/89639 which started the effort with `System.Numerics.Tensors.TensorPrimitives`

.NET provides a broad range of support for various development domains, ranging from the creation of performance-oriented framework code to the rapid development of cloud native services, and beyond.

In recent years, especially with the rise of AI and machine learning, there has been a prevalent push towards improving numerical support and allowing developers to more easily write and consume general purpose and reusable algorithms that work with a range of types and scenarios. While .NET's support for scalar algorithms and fixed-sized vectors/matrices is strong and continues to grow, its built-in library support for other concepts, such as tensors and arbitrary length vectors/matrices, could benefit from additional improvements. Today, developers writing .NET applications, services, and libraries currently may need to seek external dependencies in order to utilize functionality that is considered core or built-in to other ecosystems. In particular, for developers incorporating AI and copilots into their existing .NET applications and services, we strive to ensure that the core numerics support necessary to be successful is available and efficient, and that .NET developers are not forced to seek out non-.NET solutions in order for their .NET projects to be successful.

## Designs

The overall design has been influenced by the existing .NET surface area and the generic math feature (https://learn.microsoft.com/en-us/dotnet/standard/generics/math).

### Base Interface

At it's core there is an `ITensor<TSelf, T>` interface that forms the basis of interchange between types. This interface defines the minimal surface area expected for users to be able interact with a tensor.

Different libraries support different sets of `T`. For example, some only support numeric `T`, some also support `string` or `bool`, and some even support more esoteric concepts like `DateTime`. .NET does not, today, have a system where we can say that members are available when a `T` meets a given constraint and so, much like with `generic math`, the number of interfaces we expose here "could" become quite expansive (potentially mirroring the generic math interface hierarchy). Extension methods do largely work, but they aren't possible today for `properties` or `operators` and so this somewhat limits our choices. However, `extension everything` has been a long standing request and this might be the right time to push for the support as it greatly simplifies our surface area here.

To keep things simple, and in hopes of some support for extension `properties` and `operators`, this initially just defines the core tensor type which allows access to the data following a particular shape, to take slices of that data, and to reshape that data.

```csharp
namespace System.Numerics.Tensors;

public interface ITensor<TSelf, T>
    : IEnumerable<T>,
      IEquatable<TSelf>,
      IEqualityOperators<TSelf, TSelf, bool>
    where TSelf : ITensor<TSelf, T>
    where T : IEquatable<T>, IEqualityOperators<T, T, bool>
{
    // TODO: Determine if we can implement `IEqualityOperators<TSelf, T, bool>`.
    // It looks like C#/.NET currently hits limitations here as it believes TSelf and T could be the same type
    // Ideally we could annotate it such that they cannot be the same type and no conflicts would exist

    static TSelf Empty { get; }

    bool IsEmpty { get; }
    bool IsPinned { get; }
    int Rank { get; }
    ReadOnlySpan<nint> Strides { get; }

    ref T this[params ReadOnlySpan<nint> indices] { get; }

    static implicit operator TensorSpan<T>(TSelf value);
    static implicit operator TensorReadOnlySpan<T>(TSelf value);

    TensorSpan<T> AsSpan(params ReadOnlySpan<Range<nint>> ranges);
    ReadOnlyTensorSpan<T> AsReadOnlySpan(params ReadOnlySpan<Range<nint>> ranges);
    ref T GetPinnableReference();
    TSelf Slice(params ReadOnlySpan<Range<nint>> ranges);

    void Clear();
    void CopyTo(TensorSpan<T> destination);
    void Fill(T value);
    bool TryCopyTo<T>(TensorSpan<T> destination);

    // IEnumerable

    IEnumerator<T> GetEnumerator();

    // IEquatable

    bool Equals(TSelf other);

    // IEqualityOperators

    static bool operator ==(TSelf left, TSelf right);
    static bool operator ==(TSelf left, T right);

    static bool operator !=(TSelf left, TSelf right);
    static bool operator !=(TSelf left, T right);
}
```

### Built-in Implementations

After the core interfaces, we have the built-in tensor types that follow the interface.

Most APIs are then supported via extensions or static methods exposed on `static class Tensor`. This allows users convenient access to most APIs, allows us future flexibility to make some APIs accessible like instance methods (i.e. `Tensor.Abs(x)` vs `x.Abs()`, allows us to expose concrete overloads where convenient, etc.

Due to how generic inference works and there being no way to say that `<TTensor, T>` is really more `TTensor<T>`, we currently expose these as being over `ITensor<TSelf, T>`. This works fine for `Tensor<T>` since it is a class, but doesn't allow efficient reusue with `TensorSpan`. If C# had proper support for the "self constraint" or better generic inference support this might be a non-issue and we could expose it the more desirable way.

`Tensor<T>` is notably explicitly sealed as extensibility must be an intentional design point. The extensibility point here is then `ITensor<TSelf, T>` and `Tensor<T>` is only our own friendly wrapper over arbitrary memory. This allows it to be more efficient and also more easily control lifetimes, ownership, and more freely extend the type in the future as needed.

```csharp
namespace System.Numerics.Tensors;

public sealed class Tensor<T>
    : IDisposable,
      ITensor<Tensor<T>, T>
    where T : IEquatable<T>, IEqualityOperators<T, T, bool>
{
    // IDisposable

    void Dispose();

    // ITensor

    public static Tensor<T> Empty { get; }

    public bool IsEmpty { get; }
    public bool IsPinned { get; }
    public int Rank { get; }
    public ReadOnlySpan<nint> Strides { get; }

    public ref T this[params ReadOnlySpan<nint> indices] { get; }

    public static implicit operator TensorSpan<T>(Tensor<T> value);
    public static implicit operator TensorReadOnlySpan<T>(Tensor<T> value);

    public TensorSpan<T> AsSpan(params ReadOnlySpan<Range<nint>> ranges);
    public ref T GetPinnableReference();
    public Tensor<T> Slice(params ReadOnlySpan<Range<nint>> ranges);

    public void Clear<T>();
    public void CopyTo<T>(TensorSpan<T> destination);
    public void Fill<T>(T value);
    public bool TryCopyTo<T>(TensorSpan<T> destination);

    // IEnumerable

    public Enumerator GetEnumerator();

    // IEquatable

    public bool Equals(Tensor<T> other);

    // IEqualityOperators

    public static bool operator ==(Tensor<T> left, Tensor<T> right);
    public static bool operator ==(Tensor<T> left, T right);

    public static bool operator !=(Tensor<T> left, Tensor<T> right);
    public static bool operator !=(Tensor<T> left, T right);

    public struct Enumerator
    {
        public ref readonly T Current { get; }

        public bool MoveNext();
    }
}

public static partial class Tensor
{
    // Effectively mirror the TensorPrimitives surface area. Following the general pattern
    // where we will return a new tensor and an overload that takes in the destination explicitly.
    // * public static ITensor<T> Abs<T>(ITensor<T> x) where T : INumberBase<T>;
    // * public static void Abs<T>(ReadOnlyTensorSpan<T> x, TensorSpan<T> destination) where T : INumberBase<T>;

    // The language ideally has extension operators such that we can define `operator +` instead of `Add`
    // Without this support, we end up having to have `TensorNumber<T>`, `TensorBinaryInteger<T>`, etc
    // as we otherwise cannot correctly expose the operators based on what `T` supports

    // APIs that would return `bool` like `GreaterThan` are split into 3. Following the general
    // pattern already established for our SIMD vector types.
    // * public static ITensor<T> GreaterThan(ITensor<T> x, ITensor<T> y);
    // * public static bool GreaterThanAny<T>(ITensor<T> x, ITensor<T> y);
    // * public static bool GreaterThanAll<T>(ITensor<T> x, ITensor<T> y);

    // We need to expose some reshaping APIs and finalize on the names:
    // * Reshape   - The most common operations are covered by slicing but can generally do many of the below
    // * Expand    - Effectively ensures a tensor is compatible with another tensor's shape, requires a copy or understanding sparse data
    // * Permute   - Effectively changes the index ordering as specified, requires a copy and a better name
    // * Transpose - Effectively reversing the index order, sometimes refers to only the last two indices, requires a copy
    // * Flip      - Effectively reverses the elements in a given dimension, requires a copy
    // * Squeeze   - Effectively removes empty dimensions, just a simple view change and completely safe without copying
    // * Unsqueeze - EFfectively adds an empty dimension, just a simple view change and completely safe without copying
}
```

### Views

Much like with `System.Array` and `System.Span`, we have a general desire to get access to a particular "view" of the tensor without allocating. To facilitate that we define a `TensorSpan` type which allows for efficient slicing and general access to the data.

It may be desirable to expose this instead as some form of `MDSpan` that works over any type of multi-dimensional memory, including `System.Array` and it may also be desirable to split out a separate `TensorSpan2D<T>` and `TensorSpan3D<T>` type. This consideration comes in that most tensors will either be `1D` (effectively just a `Span<T>` but which can represent more than `int.MaxValue` elements), `2D`, or `3D`. As the number of dimensions increases, the likelyhood of encountering a tensor with that rank decreases dramatically. -- Particularly if we go with `Span2D<T>`, this may be a good reason to revisit having a `NativeSpan<T>` since we need to track `nint` based indices for tensors.

However, when you have a general purpose span that can go over any rank of data, you end up with needing to now allocate and track an array representing the lengths/strides. This is much less efficient and can incur a non-trivial amount of overhead in some patterns. It also makes slicing have more considerations than may be desired by most users. Having `TensorSpan2D` or similar, however, allows us to special case for the common sizes and we can abstract over it via `ITensor<TSelf, T>` still. -- This is in part assuming that we implementing interfaces on `ref struct` does become possible in .NET 9.

```csharp
namespace System.Numerics.Tensors;

public readonly ref struct TensorSpan<T>
{
    public TensorSpan(Tensor<T>? tensor);

    public static TensorSpan<T> Empty { get; }

    public bool IsEmpty { get; }
    public bool IsPinned { get; }
    public ReadOnlySpan<nint> Lengths { get; }
    public int Rank { get; }
    public ReadOnlySpan<nint> Strides { get; }

    public ref T this[params ReadOnlySpan<nint> indices] { get; }

    public static implicit operator ReadOnlyTensorSpan<T>(TensorSpan<T> span);

    public Enumerator GetEnumerator();
    public ref T GetPinnableReference();
    public TensorSpan<T> Slice(params ReadOnlySpan<Range<nint>> ranges);

    public ref struct Enumerator
    {
        public ref readonly T Current { get; }

        public bool MoveNext ();
    }
}

public readonly ref struct ReadOnlyTensorSpan<T>
{
    public ReadOnlyTensorSpan(Tensor<T>? tensor);

    public static ReadOnlyTensorSpan<T> Empty { get; }

    public bool IsEmpty { get; }
    public bool IsPinned { get; }
    public ReadOnlySpan<nint> Lengths { get; }
    public int Rank { get; }
    public ReadOnlySpan<nint> Strides { get; }

    public ref readonly T this[params ReadOnlySpan<nint> indices] { get; }

    public void Clear();
    public void CopyTo(ReadOnlyTensorSpan<T> destination);
    public void Fill(T value);
    public Enumerator GetEnumerator();
    public ref readonly T GetPinnableReference();
    public ReadOnlyTensorSpan<T> Slice(params ReadOnlySpan<Range<nint>> ranges);
    public bool TryCopyTo(ReadOnlyTensorSpan<T> destination);

    public ref struct Enumerator
    {
        public ref readonly T Current { get; }

        public bool MoveNext ();
    }
}
```

### Creation and Interop

Interop with other tensor libraries is a pretty crucial factor and while the `TensorSpan<T>` and `ITensor<TSelf, T>` types do cover a large majority of cases, there also exists some more general considerations around unsafely wrapping or getting other access to the data.

For external consumers getting access to the underlying `Tensor<T>`, they can use the combination of `bool IsPinned { get; }` and `ref T GetPinnableReference` alongside `System.Runtime.CompilerServices.Unsafe` to get the underlying data and handle it correctly. A `Tensor<T>` then has a few creation APIs exposed allowing some minimal customization of how the data is allocated. For creating a `Tensor<T>` over other data, some explict unsafe creation APIs are then exposed.

Given the below, the first two APIs allow creating a `Tensor<T>` over runtime allocated memory. If `mustPin` is true then the data is required to be pinned and `bool IsPinned { get; }` will return `true`. This does not necessarily mean the memory has been allocated using native memory and where the memory is allocated remains at the discression of the runtime. There exists a version that only takes `lengths` and assumes there are no gaps between the dimensions. There then exists a separate API which takes the strides in order to support other types of contiguous memory.

There are then separate APIs which directly take a `T*` and which allow creation of a `Tensor<T>` over existing memory. In such a scenario, the tensor assumes no ownership of the data and it remains the responsibiltiy of the caller to free the data when they are done with it. It may be desirable to consider a separate set of APIs that take some `IMemoryAllocator` like interface or even a `Free` delegate where the user can customize the free behavior, but that is not considered in scope for this design. It may be desirable to only

```csharp
public static partial class Tensor
{
    public static Tensor<T> Create(bool mustPin, ReadOnlySpan<nint> lengths);
    public static Tensor<T> Create(bool mustPin, ReadOnlySpan<nint> lengths, ReadOnlySpan<nint> strides);

    public static Tensor<T> Create(T* address, ReadOnlySpan<nint> lengths);
    public static Tensor<T> Create(T* address, ReadOnlySpan<nint> lengths, ReadOnlySpan<nint> strides);

    public static Tensor<T> CreateUninitialized(bool mustPin, ReadOnlySpan<nint> lengths);
    public static Tensor<T> CreateUninitialized(bool mustPin, ReadOnlySpan<nint> lengths, ReadOnlySpan<nint> strides);

    public static TensorSpan<T> CreateSpan(T* address, ReadOnlySpan<nint> lengths);
    public static TensorSpan<T> CreateSpan(T* address, ReadOnlySpan<nint> lengths, ReadOnlySpan<nint> strides);

    public static ReadOnlyTensorSpan<T> CreateReadOnlySpan(T* address, ReadOnlySpan<nint> lengths);
    public static ReadOnlyTensorSpan<T> CreateReadOnlySpan(T* address, ReadOnlySpan<nint> lengths, ReadOnlySpan<nint> strides);
}
```

### Helper types

Tensors are expected to work with large amounts of memory and may be backed by native. As such, some core types need to be expanded to support larger than `int` lengths. These would benefit greatly from language support. It may be beneficial to explicitly type this as `NativeIndex` and just make it `nint` instead so we can still specially represent starts/ends. The need for arbitrary integer types is less common and may not be appropriate in many case.

```csharp
namespace System;

public readonly struct Index<T> : IEquatable<Index<T>>
    where T : IBinaryInteger<T>
{
    public Index(T value, bool fromEnd = false);

    public static Index End { get; }
    public static Index Start { get; }

    public bool IsFromEnd { get; }
    public T Value { get; }

    public static implicit operator Index(T value);

    public static Index FromEnd(T value);
    public static Index FromStart(T value);

    public T GetOffset (T length);
}

public readonly struct Range<T> : IEquatable<Range<T>>
    where T : IBinaryInteger<T>
{
    public Range(Index<T> start, Index<T> end);

    public static Range<T> All { get; }

    public Index<T> End { get; }
    public Index<T> Start { get; }

    public static Range<T> EndAt(Index<T> end);
    public (T Offset, T Length) GetOffsetAndLength(T length);
    public static Range<T> StartAt(Index<T> start);
}
```

## Future Directions

The potential support that could be added to this feature is essentially limitless; however, there are some well known and frequently requested scenarios that should be explicitly called out and discussed.

### Optimizations

One of the key concerns is optimizing complex expression trees. While we can efficiently handle (on the CPU) any individual exposed operation like `x + y`, supporting other operations like `(x * y) + z` requires a new API to be exposed (`MultiplyAdd`) to ensure we walk memory as few times as possible. It is not feasible for us to expose a custom "compute kernel" for every scenario. However, we do have a few powerful features available that could be explored in the future to enable efficient handling.

Naturally there are some "obvious" potential solutions like dynamic code generation. However, this isn't AOT friendly and does come with sometimes expensive runtime overhead. However, there are now also newer options that are available which (particularly with the right language team coordination) should allow this to be addressed.

One potential avenue to explore is `source generators`. Much like with `regex`, it should be possible to support a user providing a `partial method` or field and some minimal syntax that can be recognized in a string. For example, `MultiplyAdd` could be supported as the below. This would require us defining a mini-DSL of supported mathematical expressions from which we would generate the code.
```csharp
[TensorKernel("(x * y) + addend")]
public static partial Tensor<T> MultiplyAdd(Tensor<T> x, Tensor<T> y, Tensor<T> addend);
```.

One could also use source generators with a C# based such as the below, which would be more expressive but would result in dead internal methods needing to be trimmed away:
```csharp
[TensorKernel(nameof(MultiplyAddImpl))]
public static partial Tensor<T> MultiplyAdd(Tensor<T> x, Tensor<T> y, Tensor<T> addend);

private static Tensor<T> MultiplyAdd(Tensor<T> x, Tensor<T> y, Tensor<T> addend)
{
    return (x * y) + addend;
}
```

Another option is to use `interceptors` to achieve much the same thing. This would be particularly feasible given the extension method approach proposed as they all return `ITensor<TSelf, T>` and thus we could take `Tensor<T>` and return something else like `TensorBuilder<T>`. One can then track what is effectively the actual computation expressions being done and "execute" the builder at the end, which does the efficient thing. This would come with some overhead, but it is extremely flexible. Such an approach may also work with things like GPU compute and could be explicitly used if we made such a `TensorBuilder<T>` type public.

### Extending the surface Area

The general approach taken is designed to put the minimum burden on the core libraries while still maximizing the ability to reuse code, the ability to allow interchange with types, and the ability to expose additional APIs in the future without much concern for breaking the world. For example, some general concepts we may want to support include things like a potential `Complex<T>` type.

Given that the actual `ITensor<TSelf, T>` interface + the `TensorSpan<T>` types are the main exchange points, it should be feasible for most algorithm to be written in a way that the actual underlying memory doesn't really matter. It could come from `Tensor<T>` and it could be a completely separate type provided by an external library.

The approach of then providing most APIs via static or extension methods that simply operate over the data view means that there is only a very small surface area other libraries actually need to implement. Users then get access to a number of generally efficient APIs (much like LINQ) for core operations over that data. Individual libraries can then override that behavior by defining their own "closer" extension method or defining a concrete instance/static API on the type.

This results in very nice versioning, a fair amount of behavior customization, and little concern over introducing new future APIs as needed.

### Additional APIs

There exists some concepts not covered above that will likely need to be handled. This includes potential interop with LINQ, being able to convert the data to a linear array (which may fail depending on the underlying size), complex reshaping, or unsafe view APIs.

For reshaping, ECMA-335 notably requires arrays to be "row-major" order. That is, given a `1920x1080` (1920 wide by 1080 tall) image, you iterate the columns in row 0, then the columns in row 1, and so on. Thus `[0, 0]` and `[0, 1]` are sequential in memory (as the most significant axis is listed first and you are effectively indexing as `[y, x]`). A developer is required to transpose their data (or otherwise rearrange their indices) if they wish to access the data in a different order. In many cases, this is simply sorting their indices. FOr example, if the user had data such that `y` indices were sequential in memory, they should simply access it as `[x, y]` instead of `[y, x]` and everything will "just work". Tracking whether the user intends `[a, b]` to be treated as `(b * stride[0]) + a` or as `(a * stride[0]) + b` is actually quite expensive and can trivially lead to pits of failure due to mismatches between what is the row, what is the column, and so on.  Many tensor libraries treat operates like `Permute` and `Transpose` as explicitly allocating and copying and it is expected that .NET do the same.

However, there are then concepts like `View` which do not copy and only return a "compatible" alias of the data. In such a case a tensor with `4` elements could be viewed as `1x4`, `2x2`, or `4x1`. While this is a common operation, it is also unsafe (strictly speaking) and so such APIs likely need to be more explicit about this unsafeness. APIs like `Reshape` notably will try to return a `view` if possible and may copy the data otherwise (i.e. it cannot be represented simply by changing the tracked strides).

## Type and Memory Comparison for other Frameworks

Below are two tables sharing some introspections into the types and memory layouts supported by other common tensor frameworks.

Some additional notes:
 * Both Numpy and TensorFlow support Non-Contiguous memory, but its often less performant
 * OnnxRuntime technically supports sparse tensors, but no kernels actually use them and they are not exposed in C#
 * Other frameworks that do support sparse tensors each supports various versions of "sparse"
 * Long double is either 96 or 128 bits based on underlying hardware

### Types Supported

| | libTorch | ONNX | Numpy | TensorFlow | Julia |
| --- | --- | --- | --- | --- | --- |
| byte | X | X | | X | X | X |
| sbyte | X | X | X | X | X |
| ushort | X | X | X | X | X |
| short | X | X | X | X | X |
| int | X | X | X | X | X |
| uint | X | X | X | X | X |
| long | X | X | X | X | X |
| ulong | X | X | X | X | X |
| bool (byte) | X | X | X | X | X |
| string (bytes) |  | X | X | X
| string (unicode) |  |  | X |
| string (c++ std::string) |  | X |  |
| float | X | X | X | X | X |
| float16 | X | X | X | X | X |
| bfloat16 | X | X |  | X |
| double | X | X | X | X | X |
| complex64 | X |  | X | X | X |
| complex128 | X |  | X | X
| long double (either 96 or 128 bits) |  |  | X | X |
| long double complex (either 192 or 256 bits) |  |  | X |  |
| Timedelta |  |  | X |  |
| Datetime |  |  | X |  |
| PythonObjects |  |  | X |  |
| Raw Data |  |  | X |  |
| qint8 (quantized int) | X |  |  | X |  |
| qint16 (quantized int) |  |  |  | X |  |
| qint32 (quantized int) |  |  |  | X |  |
| qint32 (quantized int) | X |  |  | X |  |
| quint8 (quantized unsigned int) | X |  |  | X |  |
| quint16 (quantized unsigned int) |  |  |  | X |  |
| quint32 (quantized unsigned int) |  |  |  | X |  |
| quint64 (quantized unsigned int) |  |  |  | X |  |
| quint2x4 | X |  |  |  |
| quint4x2 | X |  |  |  |
| Handle to mutable resource |  |  |  | X |  |

### Memory Layouts Supported

| | libTorch | ONNX | Numpy | TensorFlow | Julia |
| --- | --- | --- | --- | --- | --- |
| Supports Contiguous Memory | X | X | X | X |  |
| Supports Non-Contiguous Memory |  |  | X | X |  |
| Supports Sparse Tensors | X |  | X | X |  |

## Scenarios

**TODO: Port the many excelent samples that Niklas and others have put together showing coding patterns we want to support. The surface area proposed above has taken most of these scenarios into account and while not everything exactly follows the pseudo-syntax, most of it flows fairly easily and only requires minor adjustments (often simply accounting for places where the proposed surface follows the Framework Design Guidelines).
