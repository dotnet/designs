The following is a thought process for allowing tail processing on `Vector<T>` but it relies on masking operations. We would be curious to discuss other thoughts on handling tail processing.

### Vector Methods for Tail Processing

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

With the addition of more powerful masking capability (in the specific case, opmask registers for EVEX), we can use the mask registers to collapse the vector body and vector tail prcoessing into a single loop, eases the burden on the developer.

We propose to extend the `Vector` API constructors with an optional index and length:

```C#
public int SumVector(ReadOnlySpan<int> source)
{
  Vector<int> vresult = Vector256<int>.Zero;
  for (int i = 0; i < source.Length; i += Vector256<int>.Count)
  {
    vresult += new Vector<int>(source.Slice(i), i, source.Length);
  }

  int result = 0;
  for (int i = 0; i < Vector<int>.Count; i++)
  {
    result += vresult[i];
  }

  return result;
}
```

which will allow the JIT to create a mask based on the remaining elements and the vector length. This will allow to perform a partial load and update into a vector register using the opmask registers. 
