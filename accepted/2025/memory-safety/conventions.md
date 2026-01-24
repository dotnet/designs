
# Unsafe coding conventions

Outside of the language destails, there's a question of recommendations and conventions in how to use the feature, particularly the scope of unsafe blocks.

We can split the annotations into two categories:
* Unsafe member annotations
* Unsafe blocks

Member declarations are simpler because the contract has a relatively clear dichotomy: the caller and callee. Both sides participate in a contract. The contract is structured around the aforementioned memory guarantees: an unsafe method may cause memory access violations unless all preconnditions are satisfied. The caller promises to fulfill all preconditions and the callee promises to fully enumerate all necessary preconditions. Assuming both parties discharge their obligations, the property holds and no access violations should occur. Commensurate notions of blame follow -- if a violation does occur, at least one party is at fault. If the caller did not satisfy all preconditions, they are at fault. If the callee did not fully specify all preconditions, they are at fault. In either case, the property can be repaired by identifying the cause of the failure and addressing it.

Unsafe blocks are more complicated. The are only used within functions, so when writing an unsafe block there is no natural boundary to apply. In fact, there are two contradictory sensible rules that might be used to decide the size of an unsafe block. On the one hand, there is value in making the block as small as possible -- containing precisely the call to the unsafe method and nothing else. On the other hand, the reason why that particular call might be safe may lie in preceding code that ensures required preconditions are true. You may want to enlarge the block to encompass both the unsafe call and the required preconditions.

Take [this example](https://github.com/dotnet/runtime/blob/b19edc085666acaf215ff60edce24cada71e2f93/src/libraries/System.Private.CoreLib/src/System/Runtime/InteropServices/MemoryMarshal.cs#L290C51-L290C70) from the .NET core libraries. The `Unsafe.As` method is an unsafe method that can only be used correctly if it is guaranteed that the input type has a legal conversion to the target type. In this case, we call an internal method that guarantees the input is a compatible array. Therefore, the call to `Unsafe.As` is valid, the containing method is safe, and we can enclose this call in an unsafe block. The question is, how large should the block be? We could narrowly scope the block, encompassing only the `Unsafe.As`, e.g.:

```diff
if (RuntimeHelpers.ObjectHasComponentSize(obj))
{
    // The object has a component size, which means it's variable-length, but we already
    // checked above that it's not a string. The only remaining option is that it's a T[]
    // or a U[] which is blittable to a T[] (e.g., int[] and uint[]).

    // The array may be prepinned, so remove the high bit from the start index in the line below.
    // The ArraySegment<T> ctor will perform bounds checking on index & length.
+   unsafe
+   {
        segment = new ArraySegment<T>(Unsafe.As<T[]>(obj), index & ReadOnlyMemory<T>.RemoveFlagsBitMask, length);
+   }
    return true;
}
```

We could also expand it to include the `ObjectHasComponentSize` call:

```diff
+unsafe
+{
    if (RuntimeHelpers.ObjectHasComponentSize(obj))
    {
        // The object has a component size, which means it's variable-length, but we already
        // checked above that it's not a string. The only remaining option is that it's a T[]
        // or a U[] which is blittable to a T[] (e.g., int[] and uint[]).

        // The array may be prepinned, so remove the high bit from the start index in the line below.
        // The ArraySegment<T> ctor will perform bounds checking on index & length.

        segment = new ArraySegment<T>(Unsafe.As<T[]>(obj), index & ReadOnlyMemory<T>.RemoveFlagsBitMask, length);
        return true;
    }
    else
    {
        // The object isn't null, and it's not variable-length, so the only remaining option
        // is MemoryManager<T>. The ArraySegment<T> ctor will perform bounds checking on index & length.

        Debug.Assert(obj is MemoryManager<T>);
        if (Unsafe.As<MemoryManager<T>>(obj).TryGetArray(out ArraySegment<T> tempArraySegment))
        {
            segment = new ArraySegment<T>(tempArraySegment.Array!, tempArraySegment.Offset + index, length);
            return true;
        }
    }
+}
```

If we keep the block small, we can easily see which parts are dangerous and which parts are not. As we expand the block, we capture more of the critical dependencies that flow into unsafe calls, but we also risk capturing pieces that are either not part of the safety contract, or may even be part of a different safety contract. Note that the call `Unsafe.As<MemoryManager<T>>(obj)` contains assumptions that don't even fit in the wider block. We would have to widen the scope even larger.

The principle of capturing all dependencies of unsafe calls is also tricky to pin down. Most safety contracts are much larger than it may seem. For instance, a lot of code requires arrays to meet their language guarantees (e.g., an array of type `T` will not contain an element of unrelated type `U`) or that GC pinning works as promised. If these guarantees were to break the safety guarantees would be lost. However, expanding unsafe blocks to the entire runtime is neither possible nor desirable.

In sum, the guidance is to **keep unsafe blocks as small as reasonably possible**. In the example above, this would mean preferring the first diff to the second. In some cases the preconditions and unsafe calls are very close together and isolated. If so, it may be acceptable to widen the scope slightly. However, the starting point for unsafe blocks should be to keep them as small as reasonably possible.