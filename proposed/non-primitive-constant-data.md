# Non-primitive Constant Data

## Introduction
Current implementations of languages in .NET are limited to a small set of possible constant types. It would be useful for computational scenarios to provide a mechanism to specify efficiently accessible pre-computed data in more complex data structures. This proposal describes a mechanism for supporting user data structures of more arbitrary data structures, supporting both array production, and constant data access to a single constant as well as a ReadOnlySpan of data.

## Current valid constant forms
Currently accepted are 4 forms of constant
- Integer/Float constants
```csharp
void Constants()
{
   int x = 3;
   double y = 4.0;
}
```

Which is represented in IL via the various ldc opcodes.
```
IL_0001: ldc.i4.s 3
IL_0003: stloc.s 0
IL_0005: ldc.r8 4.0
IL_000E: stloc.s 1
```

- String constants
```csharp
void Constants()
{
    string z = "z";
}
```

Which is represented in IL using the ldstr instruction and #US metadata table.
```
IL_0001: ldstr "z"
```
- Byte data constants
```csharp
void Constants()
{
    ReadOnlySpan<byte> byte_data = new ReadyOnlySpan<byte>(new byte[]{0,1,2,3});
}
```
Which is represented in IL using an RVA static field on an anonymous type and the accessed via ldsflda to get its address.
```
IL_0001: ldloca.s 0
IL_0003: ldsflda valuetype '<PrivateImplementationDetails>'/'__StaticArrayInitTypeSize=3' '<PrivateImplementationDetails>'::'0C7A623FD2BBC05B06423BE359E4021D36E721AD'
IL_0008: ldc.i4.3
IL_0009: call instance void valuetype [System.Memory]System.ReadOnlySpan`1<uint8>::.ctor(void*, int32)
```


- Primitive data for a dynamically constructed array
```csharp
void Constants()
{
    int[] data = new byte[]{0,1,2,4};
}
```

Which is represented in IL using an RVA static field on an anonymous type which is used in conjunction with the System.Runtime.CompilerServices.RuntimeHelpers.InitializeArray function to dynamically create an array based on a set of data which was statically placed into the assembly.
```
IL_0001: ldc.i4.4
IL_0002: newarr [mscorlib]System.Byte
IL_0007: dup
IL_0008: ldtoken field int32 '<PrivateImplementationDetails>'::'12DADA1FFF4D4787ADE3333147202C3B443E376F'
IL_000d: call void [mscorlib]System.Runtime.CompilerServices.RuntimeHelpers::InitializeArray(class [mscorlib]System.Array, valuetype [mscorlib]System.RuntimeFieldHandle)
IL_0012: stloc.0
```
## New valid constant forms

The proposal is to provide a set of CompilerServices apis implemented as intrinsics (in most cases) which will allow the use of more complex constants. This api will accept as a byref argument a reference to data in a well defined type layout, and return a byref to data in the correct platform layout.

### Definition of types which can be represented as constants
- Must be a struct
- Generic structures are permitted, and only those generic type parameters which contribute to field layout shall have impact on whether the type can be represented as a constant.
- The struct must have sequential layout, auto layout, or explicit layout without any overlap.
- Without pointers of any form (No object reference, IntPtr, UIntPtr, pointer, function pointer)
- All fields must be public, or the type must be a primitive type, or a type well known to the runtime/language as having special layout (Vector128<T>, Vector256<T>)
- All fields must also be of a type which can be represented as a constant

### Well known type layout
Given that IL binaries may be loaded on many different platforms with different type layout rules, constant data must be represented in a consistent manner across all of them. For consistent type layout, types shall be laid out in precisely a sequential manner as described in metadata, with *no* packing between any fields. For instance, this struct will utilize 9 bytes when stored. In addition, if the type uses explicit layout, the layout used for "well known type layout" 

```csharp
struct NonAlignedStruct
{
    byte b;
    double d;
}
```

### RuntimeHelpers apis

```csharp
class RuntimeHelpers
{
    // Fundamental new api capability
    // Behavior if the values pointed at by inData changes over time is undefined
    // This function is expected to be implemented as a compiler intrinsic with the behavior of the c# written below. The proposal
    // expects that implementors will make this work for arbitrary calls, not just as a jit intrinsic, but that's possibly not completely necessary.
    ReadOnlySpan<TOutput> LoadConstantData<TOutput, TInput>(ref TInput inData, int count) where TOutput:struct where TInput:struct
    {
        if (!VerifyThatTypeIsValidForConstantRepresentation(typeof(TOutput)))
            throw new InvalidProgramException();

        if (count < 0)
            throw new InvalidProgramException();

        if (checked(GetSizeOfConstantRepresentation(typeof(TOutput)) * count) > sizeof(TInput))
            throw new InvalidProgramException();

        if (HasGCPointers(typeof(TInput))
            throw new InvalidProgramException();

        if (!VerifyInDataPointsInsideOfSomeLoadedManagedAssembly(ref inData))
            throw new InvalidProgramException();

        if (IsAlignedForTOutput(ref inData, typeof(TOutput)) && WellKnownTypeLayoutMatchesPlatformLayout(typeof(TOutput)))
        {
            return new ReadOnlySpan<TOutput>(Unsafe.As<TOutput>(inData), count);
        }
        else
        {
            // Convert inData pointer to raw pointer, and use it as an entry in a hashtable to store converted constant data
            // The converted constant data will be constructed in some fashion like...
            IntPTr inDataPtr = (IntPtr)Unsafe.AsPtr(inData);
            lock(hashtable)
            {
                TOutput [] data;
                if (!hashtable.TryGetValue(inDataPtr, out data) || data.Length < count)
                {
                    data = new TOutput[count];
                    // Read data from inData into count, handling the type layout transition
                    hashtable[inDataPtr] = data;
                }
                return data.AsSpan();
            }
        }
    }

    // Should be treated as an intrinsic by the compiler. Should allow more efficient encoding of single constants in an IL stream
    TOutput LoadIndividualConstant<TOutput, TInput>(ref TInput inData) where TOutput:struct where TInput:struct
    {
        return LoadConstantData(ref inData, 1)[0];
    }

    // General replacement for ArrayInitialize pattern, also handles non-primitive constants.
    TOutput[] AllocateArrayFromConstantData<TOutput, TInput>(ref TInput inData, int count)
    {
        TOutput[] result = new TOutput[count];
        LoadConstantData(ref inData, count).CopyTo(result.AsSpan());
        return result;
    }
}
```

These runtime helpers will be used in a manner which closely resembles how the byte array initialization works

```
IL_0003: ldsflda valuetype '<PrivateImplementationDetails>'/'__StaticArrayInitTypeSize=27' '<PrivateImplementationDetails>'::'0C7A623FD2BBC05B06423BE359E4021D36E721AD'
IL_0008: ldc.i4.3
IL_0009: call valuetype [System.Memory]System.ReadOnlySpan`1<!!0> System.Runtime.CompilerServices.RuntimeHelpers::LoadConstantData<NonAlignedStruct, valuetype '<PrivateImplementationDetails>'/'__StaticArrayInitTypeSize=27'>(ref !!1, int32)
```
