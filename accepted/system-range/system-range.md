# System.Range

**PM** [Immo landwerth](https://github.com/terrajobst)

The C# team is considering providing language syntax for specifying ranges to
represent parts of a string, array, or span. In order to make this work, the
language has to be bound to a .NET type, which we call `System.Range`.

## Scenarios and User Experience

### Construcing ranges

```csharp
// Ranges are constructed with an index and a length

var fiveToTen = new Range(index: 5, length: 6);

// By using a factory method, they can also be constructed from two indices.

var alsoFiveToTen = Range.Between(5, 11);

// The upper bound is considered exclusive so this will print the 1, 2, 3

foreach (var value in Range.Between(1, 4))
    Console.WriteLine(value);

```

The C# syntax for ranges isn't finalized and is actively worked on. While this
is part of a separate design group it makes sense to introduce the syntax here
so that we can look at source code as it's likely written:

```csharp
var fiveToTen = 5..11; // Equivalent to Range.Between(5, 11)
```

### Open ended ranges

```csharp
// Ranges can be open ended:

var fiveToEnd = 5..;  // Equivalent to Range.From(5) i.e. missing upper bound
var startToTen = ..1; // Equivalent to Range.To(11) i.e. missing lower bound
var everything = ..;  // Equivalent to Range.All i.e. missing upper and lower bound

// Asking for Index, Length or enumerating an open range will throw
// NotSupportedException. The consumer of the range is expected to 
// close it over the particular data:

void Print(int[] data, Range range)
{
    if (range.IsOpen)
        range = range.Close(0..data.Length);

    foreach (var index in range)
        Console.WriteLine(data[index]);
}

var data = new [] { 'a', 'b', 'c' };
Print(data, 2..); // Prints c
Print(data, ..2); // Prints a, b
Print(data, ..);  // Prints a, b, c
```

### Using ranges in APIs

Ranges provide a concise way to provide an `index` and a `length`. This allows
for making common operations more compact and less confusing at the call side.
For example, consider this version of the indexer for `String`:

```csharp
partial class String
{
    public string this[Range range] => Substring(range.Index, range.Length);
}
```

This allows creating substrings simply by using the indexer:

```csharp
var helloWorld = "Hello, World!";
var hello = helloWorld[..5];
var world = helloWorld[7..12];
```

While this might appear to just be minor syntactic sugar it allows for creating
powerful APIs, such as the indexer for `Tensor<T>` (which you can think of as
multidimensional array where the dimensions aren't statically known).

```csharp
partial class Tensor<T>
{
    public Tensor<T> this[params Range[] ranges] { get; }
}
```

The indexer takes a list of ranges that can be used to get slices of the tensor.
Imagine you have a two dimensional tensor (in other words a table). Imagine it
represents test data where each row is a test case and the columns are
representing the inputs with the last column containing the expected result. Now
let's say you want to split this data into two tensors, one representing the
inputs and one the outputs.

This might look like this:

```csharp
Tensor<float> testDataTable = GetTestData();
int columnCount = testDataTable.GetLength(1);
int outputColumn = columnCount - 1;

Tensor<float> inputTable = testDataTable[.., ..outputColumn];
Tensor<float> outputTable = testDataTable[.., outputColumn..];
```

## Requirements

### Goals

* We need a type that both, plays well with existing concepts in the base class
  library, as well as the intended syntax for C#
* We need to ensure we can extend the notion of ranges in the base class library
  in the future (for example, by supporting negative ranges or element types
  other than `Int32`).

### Non-Goals

* Supporting other numeric types or even offer a generic range type
* Creating overloads on existing types that will accept `Range` (that doesn't
  mean we shouldn't be doing it, but that it will be covered by a separate
  design proposal).

## Design

### Proposed API

```csharp
namespace System {
    public struct Range : IEnumerable, IEnumerable<int>, IEquatable<Range> {
        public static Range Empty { get; }
        public static Range All { get; }
        public static Range Between(int start, int end);
        public static Range From(int start);
        public static Range To(int end);
        public Range(int index, int length);
        public int Index { get; }
        public int Length { get; }
        public bool IsClosed { get; }
        public Range Close(Range range);
        public override string ToString();
        public override int GetHashCode();
        public bool Equals(Range other);
        public override bool Equals(object other);
        public static bool operator==(Range left, Range right);
        public static bool operator!=(Range left, Range right);
    }
}
```

### Companion APIs

```csharp
namespace System {
    public partial class String {
        public string this[Range range] { get; }
    }
    public partial class T[] {
        public T[] this[Range range] { get; }
    }
    public partial struct ArraySegment<T> {
        public ArraySegment<T> this[Range range] { get; }
    }      
    public partial struct Span<T> {
        public Span<T> this[Range range] { get; }
    }
}
```

### Semantics of negative indices

The semantics of negative indices would be as follows:

* Both `start` and `end` can be negative.
* If the value is negative, the size of container is added. So `-1` is the last
  element.
* Logically, negative bounds requires the range to be closed.

The internal encoding would be as follows:

* `start..end`
  - `index := start`
  - `length := (start < 0 || end < 0) ? end : end - start`

* `..end`
  - `index := int.MinValue`
  - `length := end`

* `start..`
  - `index := start`
  - `length := int.MinValue`

This is unambiguous because `int.MinValue` isn't a valid index (the largest
length of a range is `int.MaxValue` which negated is `int.MinValue + 1`.

`Range.Between()` will normally verify that `start <= end`. This can only be
done if either both are positive or both are negative. As soon if only one side
is negative one needs to know the container size in order to verify that `start`
is indeed less than or equal to `end`. However, it seems bad form to throw from
the `Close()` method. Instead, we should follow what Python does and simply
produce the range `start..start`.

This encoding ensures that checking whether the range is open only requires
checking if either `index` or `length` is negative. This will cover both missing
bounds as well as bounds that require adding the container size.

## Q & A

### Open Design Points

* Right now, we cannot retrieve `Index` or `Length` if the range is open on any
  side (it throws `NotSupportedException`).
    - We could expose `Start: int?` and `End: int?` and then add a factory
      method `Between(int? start, int? end)`

* We should check whether we can handle negative numbers for `Length` so that we
  can support addressing relative to the end. Check Python to see what they did
  there.

* Should `Range` support containment checks?
    - Positions and/or ranges?
    - Question is how to define semantics for open ended ranges.

* Should `Range` support combining/intersecting open ended ranges?
    - Be careful though. For open ended slices this might produce invalid
      values. For instance, you might think combining `3..` and `..12` would
      produce `3..12` but this would mean indexing into an array of size 5 would
      fail, while `3..` would have worked.
    - We could define the operation as `Close(Range)` though.

* Should we add an interface for range access?
    ```csharp
    interface IRangeable<T>
    {
        T this[Range range] { get; }
    }
    ```
* How should collections represent the result of indexing using a range?
    - `Span<T>` and `ArraySegment<T>` are no-brainers and just return properly
      adjusted versions of themselves.
    - `String` should probably return a new `String`.
    - For `T[]` it seems heavy to return an array. Also, people might want no-
      copy semantics. Same goes for `List<T>`. Maybe we should have a type that
      is enumerable and holds on to the original data. This allows consumers to
      to just enumerate it, makes it reasonably cheap to create, and allows
      methods accepting an `IEnumerable<T>` to optimize when it's baked by a
      `ListRange<T>`.
        ```csharp
        public struct ListRange<T> : IList<T>
        {
            public ListRange(IList<T> list, Range range);
            public IList<T> List { get; }
            public Range Range { get; }
        }
        ```
* Should we suppport steps in ranges? Syntax is unclear and so is whether this
  means we'd need a separate type.
* For the companion types, should we support setters for range-based indexers?
  `myarray[0..10] = Enumerable.Range(1, 10)`
* Python doesn't create errors for cases where the indexes aren't valid. It
  looks like when they close ranges the just clamp the start and end position to
  the container. And if the start:end are overlapping, they simply produce an
  empty set.