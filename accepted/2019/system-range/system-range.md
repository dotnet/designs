# System.Index and System.Range

**Owner** [Immo Landwerth](https://github.com/terrajobst)

The C# team is adding [language syntax for specifying ranges][csharp-range] to
represent parts of a string, array, or span. In order to make this work, the
language has to be bound to a .NET type, which we call `System.Range`.

[csharp-range]: https://github.com/dotnet/csharplang/blob/master/proposals/ranges.md

## Scenarios and User Experience

### Indexing from the end

Sometimes it's useful to index from the end. This requires a new type `Index`
that can represent both, indexing from the start as well as from the end. It can
be implicitly converted from a normal `int`, in which case it means indexed from
the start:

```C#
Index i = new Index(1); // Could be written as: Index i = 1;
Index lastElement = new Index(1, fromEnd: true);
```

The language syntax for *from end* will use the `^` unary operator, like so:

```C#
Index lastElement = ^1;
```

Logically, you can think of the indexing from end as subtracting the value from
the collection's length:

```C#
var lastElement = array[^1]; // Same as array[array.Length - 1];
```

### Ranges

```C#
// Ranges are constructed with two indexes:

var fiveToTen = Range.Create(5, 11);

// The upper bound is considered exclusive so this will print 1, 2, 3:

foreach (var value in Range.Create(1, 4))
    Console.WriteLine(value);
```

With C# syntax this will look as follows:

```C#
var fiveToTen = 5..11; // Equivalent to Range.Create(5, 11)
```

### Unbounded ranges

```C#
// Ranges can be unbounded:

var fiveToEnd = 5..;   // Equivalent to Range.FromStart(5) i.e. missing upper bound
var startToTen = ..11; // Equivalent to Range.ToEnd(11) i.e. missing lower bound
var everything = ..;   // Equivalent to Range.All() i.e. missing upper and lower bound
```

## Requirements

### Goals

* We need a type that both, plays well with existing concepts in the base class
  library, as well as the intended syntax for C#
* We need to ensure we can extend the notion of ranges in the base class library
  in the future (for example, by supporting element types other than `Int32`).

### Non-Goals

* Supporting other numeric types or even offer a generic range type
* Creating overloads on existing types that will accept `Range` (that doesn't
  mean we shouldn't be doing it, but that it will be covered by a separate
  design proposal).
* Supporting `Index` and `Range` on multi-dimensional arrays and non-zero based
  arrays (e.g. instances of `System.Array`).
    - This work needs more thought and is currently scoped out.

## Design

### Proposed API

```C#
namespace System
{
    public readonly struct Index
    {
        private readonly int _value;

        public int Value => _value < 0 ? ~_value : _value;
        public bool FromEnd => _value < 0;

        public Index(int value, bool fromEnd = false)
        {
            if (value < 0)
                throw new ArgumentException("Index must not be negative.", nameof(value));

            _value = fromEnd ? ~value : value;
        }

        public static implicit operator Index(int value) => new Index(value);
    }

    public readonly struct Range
    {
        public Index Start { get; }
        public Index End { get; }

        private Range(Index start, Index end)
        {
            Start = start;
            End = end;
        }

        public static Range Create(Index start, Index end) => new Range(start, end);
        public static Range FromStart(Index start) => new Range(start, new Index(0, fromEnd: true));
        public static Range ToEnd(Index end) => new Range(new Index(0, fromEnd: false), end);
        public static Range All() => new Range(new Index(0, fromEnd: false), new Index(0, fromEnd: true));
    }
```

### Indexing with SZ-arrays

SZ-arrays (i.e. `T[]` in C#) do not have an API that represents the indexer.
Instead, they are specialized as IL opcodes to load and store elements in such
an array. The indexer is part of the language semantics and thus doesn't have an
API representation.

For indexing using `Index` the compiler doesn't have to do much, thus the
handling is done directly by the compiler, i.e. this code:

```C#
T[] array = GetArray();
Index index = GetIndex();
T element = array[index];
```

will be compiled down to the following code:

```C#
T[] array = GetArray();
Index index = GetIndex();
int $index = index.FromEnd
                ? array.Length - index.Value
                : index.Value;
T element = array[$index];
```

For `Range` the code-gen would be a more a bit more involved, thus we propose
to give the compiler a `RuntimeHelper` API to call, so this code:

```C#
T[] array = GetArray();
Range range = GetRange();
T[] elements = array[range];
```

will be compiled down to:

```C#
T[] array = GetArray();
Range range = GetRange();
T[] elements = RuntimeHelpers.GetArrayRange<T>(array, range);
```

We propose the following method:

```C#
namespace System.Runtime.CompilerServices
{
    public sealed class RuntimeHelpers
    {
        public static T[] GetArrayRange<T>(T[] array, Range range);
    }
}
```

### Companion APIs

To decide which indexers we offer, we followed the following principles:

* Scalar indexers should return the element type
* Range indexers should return the type they are on

If developers wish to optimize for allocations, they can convert the type to an
instance of `Span<T>`, `ReadOnlySpan<T>`, `Memory<T>` or `ReadOnlyMemory<T>` by
calling the appropriate API (e.g. `AsSpan()` or `AsMemory()`).

We'll also provide overloads to common operations that take indexes and ranges,
such as `String.Substring(Index)`, `String.Substring(Range)` and
`AsSpan(Range)`.

```C#
namespace System
{
    public partial class String
    {
        public char this[Index index] { get; }
        public String this[Range range] { get; }
        public String Substring(Index startIndex);
        public String Substring(Range range);
    }
    public readonly ref partial struct Span<T>
    {
        public ref T this[Index index] { get; }
        public Span<T> this[Range range] { get; }
        public Span<T> Slice(Index startIndex);
        public Span<T> Slice(Range range);
    }
    public readonly ref partial struct ReadOnlySpan<T>
    {
        public readonly ref T this[Index index] { get; }
        public ReadOnlySpan<T> this[Range range] { get; }
        public ReadOnlySpan<T> Slice(Index startIndex);
        public ReadOnlySpan<T> Slice(Range range);
    }
    public readonly struct Memory<T>
    {
        public Memory<T> Slice(Index startIndex);
        public Memory<T> Slice(Range range);
    }
    public readonly struct ReadOnlyMemory<T>
    {        
        public ReadOnlyMemory<T> Slice(Index startIndex);
        public ReadOnlyMemory<T> Slice(Range range);
    }
    public static partial class MemoryExtensions
    {
        public static Memory<T> AsMemory<T>(this T[] array, Index startIndex);
        public static Memory<T> AsMemory<T>(this T[] array, Range range);
        public static ReadOnlyMemory<char> AsMemory(this string text, Index startIndex);
        public static ReadOnlyMemory<char> AsMemory(this string text, Range range);
        public static Span<T> AsSpan<T>(this T[] array, Index startIndex);
        public static Span<T> AsSpan<T>(this T[] array, Range range);
        public static Span<T> AsSpan<T>(this ArraySegment<T> segment, Index startIndex);
        public static Span<T> AsSpan<T>(this ArraySegment<T> segment, Range range);
    }    
}
```
