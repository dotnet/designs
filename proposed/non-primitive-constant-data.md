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

The proposal is to provide a set of CompilerServices apis implemented as intrinsics (in most cases) which will allow the use of more complex constants. The intention is to permit more complex constant forms for ReadOnlySpan<T> access, constant access, and through IL compiler magic, add new forms of array initialization that are not currently supported through the InitializeArray pattern.

### Definition of types which can be represented as constants
- Must be a struct
- Generic structures are permitted, and only those generic type parameters which contribute to field layout shall have impact on whether the type can be represented as a constant.
- The struct may have any form of layout: sequential layout, auto layout, or explicit layout.
- Without pointers of any form (No object reference, IntPtr, UIntPtr, pointer, function pointer, or TypedReference)
- The type must be attributed with either WellKnownLayoutTypeAttribute or WellKnownLayoutVectorTypeAttribute
- All fields must also be of a type which can be represented as a constant

### Well known type layout
Given that IL binaries may be loaded on many different platforms with different type layout rules, constant data must be represented in a consistent manner across all of them. For consistent type layout, types shall be laid out in precisely a sequential manner as described in metadata, with *no* packing between any fields. For instance, this struct will utilize 9 bytes when stored. In addition, if the type uses explicit layout, even if there are overlapping fields, those fields are not to be overlapped in the WellKnownLayout, the layout of the final form will be based on copying out of the data stored in the PE file in metadata order into the runtime layout. An empty structure will consume 0 bytes of storage, and thus while it may be used as a field within a constant, is not itself permitted to be used to instantiate CreateSpan<T> or LoadConstant<T>

```csharp
struct NonAlignedStruct
{
    byte b;
    double d;
}
```

### As part of this proposal attributes will be placed on various types
The WellKnownLayoutTypeAttribute will be placed onto

System.Byte
System.SByte
System.Boolean
System.Int16
System.UInt16
System.Char
System.Int32
System.UInt32
System.Float
System.Int64
System.UInt64
System.Double

System.Numerics.Matrix3x2
System.Numerics.Matrix4x4
System.Numerics.Plane
System.Numerics.Quaternion
System.Numerics.Vector2
System.Numerics.Vector3
System.Numerics.Vector4

The WellKnownLayoutVectorTypeAttribute will be placed onto
System.Runtime.Intrinsics.Vector64<T>
System.Runtime.Intrinsics.Vector128<T>
System.Runtime.Intrinsics.Vector256<T>

### RuntimeHelpers apis

```csharp
class RuntimeHelpers
{
    // Placing this attribute on a type indicates that it can be stored and loaded using the CreateSpan<T>, LoadConstant<T> and InitializeArray helper functions
    public WellKnownLayoutTypeAttribute : Attribute
    {
    }

    // Placing this attribute on a type indicates that it can be stored and loaded using the 
    // CreateSpan<T>, LoadConstant<T> and InitializeArray helper functions. This attribute
    // indicates that the layout is that of a vector of the type parameter of the type it is placed
    // upon.
    public WellKnownLayoutVectorTypeAttribute : Attribute
    {
    }

    // Fundamental new api capability
    // Behavior if the value represented by the field handle changes over time is undefined
    // The behavior of this function is to provide a span upon the data stored into the RVA static specified
    // by RuntimeFieldHandle. The span is permitted to point directly into the PE file if it so happens that the
    // data stored in the PE file is in the correct memory layout, but if it isn't it is capable of adjusting the
    // layout to match the layout of the actual running process. The layout in the PE is based on a strictly sequential
    // processing of the types described as instance fields in the metadata, and must not contain any pointer like entities.
    // That layout shall also have no padding. 
    // 
    // This function is expected to be implemented as a compiler intrinsic with the behavior of the c# written below. The proposal
    // expects that implementors will make this work for arbitrary calls
    public ReadOnlySpan<T> CreateSpan<T>(RuntimeFieldHandle fieldHandle) where T:struct, unmanaged
    {
        return CreateSpanImpl<T>(fieldHandle);
    }

    // Should be treated as an intrinsic by the compiler. Should allow more efficient encoding of single constants in an IL stream
    T LoadConstant<T>(RuntimeFieldHandle fieldHandle) where T : struct, unmanaged
    {
        ReadOnlySpan<T> ros = CreateSpan<T>(fieldHandle);
        if (ros.Length != 0)
            throw new InvalidProgramException();

        return ros[0];
    }

    //------------------------------------------------------------------------
    //------------------------------------------------------------------------
    // Helper functions for the above... these are private implementation details
    public ReadOnlySpan<T> CreateSpanImpl<T>(RuntimeFieldHandle fieldHandle) where T:struct, unmanaged
    {
        // This will check that this is a valid type to layout, and produce the layout information necessary to process the layout
        PrimitiveAlignmentInfo [] layoutInfo = GetOffsetsAndAlignments(typeof(T).TypeHandle);

        int sizeOfWellKnownLayout = GetSizeOfWellKnownLayout(layoutInfo);
        int sizeOfSpecifiedData = SizeOfRuntimeTypeHandle(TypeOfRuntimeFieldHandle(fieldHandle.GetType()));

        int count = 0;

        try
        {
            if (checked(sizeOfSpecifiedData % sizeOfWellKnownLayout) != 0)
                throw new InvalidProgramException();

            count = sizeOfSpecifiedData / sizeOfWellKnownLayout;      
        }
        catch(OverflowException)
        {
            throw new InvalidProgramException();
        }

        ReadOnlySpan<byte> spanToRawData = GetRawDataPtrFromRVAStatic(fieldHandle); // This function returns empty span to null if fieldHandle isn't rva static field

        if (pointerToRawData.AsPtr() == IntPtr.Zero)
            throw new InvalidProgramException();

        int requiredAlignment = AlignmentOfRuntimeTypeHandle(typeof(T).TypeHandle);
        
        if (IsAligned(pointerToRawData) && WellKnownTypeLayoutMatchesPlatformLayout(typeof(T).TypeHandle, layoutInfo))
        {
            return new ReadOnlySpan<TOutput>(Unsafe.As<T>(spanToRawData.GetPinnableReference()), count);
        }
        else
        {
            Dictionary<RuntimeFieldHandle, T[]> hashtable = WellKnownLayoutHashtable<T>.Hashtable;
            lock(hashtable)
            {
                TOutput [] data;
                if (!hashtable.TryGetValue(inDataPtr, out data) || data.Length < count)
                {
                    data = new TOutput[count];
                    fixed(byte* sourceDataPtr = &spanToRawData[0])
                    {
                        for (int i = 0; i < count; i++)
                        {
                            fixed(T* tPtr = &data[i])
                            {
                                byte* tAddress = (byte*)new IntPtr(tPtr);
                                byte* sourceElementPtr = sourceDataPtr + (i * GetSizeOfWellKnownLayout(layoutInfo));
                                foreach(PrimitiveAlignmentInfo layoutEntry in layoutInfo)
                                {
                                    byte *runtimeLayoutAddress = tAddress + layoutEntry.RuntimeLayoutOffset;
                                    byte *wellKnownLayoutAddress = sourceElementPtr + layoutEntry.WellKnownLayoutOffset;

                                    // Implementation of this switch statement is particular to the particular architecture in use
                                    // little endian architectures which allow arbitrary unaligned memory access may work like the following
                                    // big endian, or archictectures which have strict alignment requirements will need a more complex implementation
                                    switch (layoutEntry.CorElementType)
                                    {
                                        case ELEMENT_TYPE_I1:
                                        case ELEMENT_TYPE_U1:
                                            *runtimeLayoutAddress = *wellKnownLayoutAddress;
                                            break;

                                        case ELEMENT_TYPE_I2:
                                        case ELEMENT_TYPE_U2:
                                        case ELEMENT_TYPE_CHAR:
                                            *(short*)runtimeLayoutAddress = *(short*)wellKnownLayoutAddress;
                                            break;

                                        case ELEMENT_TYPE_I4:
                                        case ELEMENT_TYPE_U4:
                                        case ELEMENT_TYPE_R4:
                                            *(int*)runtimeLayoutAddress = *(int*)wellKnownLayoutAddress;
                                            break;

                                        case ELEMENT_TYPE_I8:
                                        case ELEMENT_TYPE_U8:
                                        case ELEMENT_TYPE_R8:
                                            *(long*)runtimeLayoutAddress = *(long*)wellKnownLayoutAddress;
                                            break;
                                    }
                                }
                            }
                        }
                    }

                    // Read data from inData into count, handling the type layout transition
                    hashtable[inDataPtr] = data;
                }
                return data.AsSpan();
            }
        }
    }

    class WellKnownLayoutHashtable<T>
    {
        public static Dictionary<RuntimeFieldHandle, T[]> Hashtable = new Dictionary<RuntimeFieldHandle, T[]>();
    }

    bool IsAligned(IntPtr ptr, int alignment)
    {
        // alignment must be a power of 2 for this implementation to work (need modulo otherwise)
        return 0 == ((int)ptr & (alignment - 1));
    }

    struct PrimitiveAlignmentInfo
    {
        public PrimitiveAlignmentInfo(CorElementType corElementType, int wellKnownLayoutOffset, int wellKnownLayoutFieldEnd, int runtimeLayoutOffset)
        {
            CorElementType = corElementType;
            WellKnownLayoutOffset = wellKnownLayoutOffset;
            WellKnownLayoutFieldEnd = wellKnownLayoutFieldEnd;
            RuntimeLayoutOffset = runtimeLayoutOffset;
        }

        public readonly CorElementType CorElementType;
        public readonly int WellKnownLayoutOffset;
        public readonly int WellKnownLayoutFieldEnd;
        public readonly int RuntimeLayoutOffset; 
    }

    int GetSizeOfWellKnownLayout(PrimitiveAlignmentInfo[] layout)
    {
        if (layout.Length == 0) return 0;
        return layout[layout.Length - 1].WellKnownLayoutFieldEnd;
    }

    bool WellKnownTypeLayoutMatchesPlatformLayout(RuntimeTypeHandle runtimeType, PrimitiveAlignmentInfo[] layout)
    {
        if (GetSizeOfWellKnownLayout(layout) != SizeOfRuntimeTypeHandle(runtimeType))
            return false;
        
        foreach (PrimitiveAlignmentInfo entry in layout)
        {
            if (entry.WellKnownLayoutOffset != entry.RuntimeLayoutOffset)
                return false;
        }

        return true;
    }

    IEnumerable<PrimitiveAlignmentInfo> GetOffsetsAndAlignments(RuntimeTypeHandle type)
    {
        if (TypeHasAttribute(type, "System.Runtime.CompilerServices.WellKnownLayoutTypeAttribute"))
        {
            int currentWellKnownLayoutOffset = 0;
        
            var fields = GetInstanceFieldsOfType(type);
            foreach(RuntimeFieldHandle field in fields)
            {
                int currentRuntimeLayoutOffset = OffsetOfFieldHandle(field);
                RuntimeTypeHandle fieldType = TypeOfRuntimeFieldHandle(field);
                CorElementType fieldElementType = GetElementTypeOfRuntimeTypeHandle(fieldType);
                int fieldSize = 0;
                switch (fieldElementType)
                {
                    case ELEMENT_TYPE_I1:
                    case ELEMENT_TYPE_I2:
                    case ELEMENT_TYPE_I4:
                    case ELEMENT_TYPE_I8:
                    case ELEMENT_TYPE_U1:
                    case ELEMENT_TYPE_U2:
                    case ELEMENT_TYPE_U4:
                    case ELEMENT_TYPE_U8:
                    case ELEMENT_TYPE_R4:
                    case ELEMENT_TYPE_R8:
                    case ELEMENT_TYPE_CHAR:
                    case ELEMENT_TYPE_BOOLEAN:
                        int newWellKnownLayoutEnd = currentWellKnownLayoutOffset + GetElementTypeSizeForPrimitiveElementType(fieldElementType);
                        yield return new PrimitiveAlignmentInfo(fieldElementType, 
                            currentWellKnownLayoutOffset, 
                            newWellKnownLayoutOffset, 
                            currentRuntimeLayoutOffset); 
                        currentWellKnownLayoutOffset = newWellKnownLayoutEnd;
                        break;

                    case ELEMENT_TYPE_VALUETYPE:
                    {
                        CorElementType lastElementType = ELEMENT_TYPE_END;
                        int newWellKnownLayoutEnd = currentWellKnownLayoutOffset;
                        foreach (PrimitiveAlignmentInfo alignmentInfo in GetOffsetsAndAlignments(TypeOfRuntimeFieldHandle(fieldType)))
                        {
                            newWellKnownLayoutEnd = alignmentInfo.WellKnownLayoutFieldEnd + currentWellKnownLayoutOffset;
                            yield return new PrimitiveAlignmentInfo(alignmentInfo.CorElementType, 
                                alignmentInfo.WellKnownLayoutOffset + currentWellKnownLayoutOffset, 
                                newWellKnownLayoutEnd,
                                alignmentInfo.RuntimeLayoutOffset + currentRuntimeLayoutOffset);
                        }

                        currentWellKnownLayoutOffset = newWellKnownLayoutEnd;
                        break;
                    }
                    default:
                    {
                        throw new InvalidProgramException();
                    }
                }
            }
        }
        else if (TypeHasAttribute(type, "System.Runtime.CompilerServices.WellKnownVectorLayoutTypeAttribute"))
        {
            RuntimeTypeHandle typeParameter = GetFirstTypeParameter(type)
            CorElementType vectorElementType = GetElementTypeOfRuntimeTypeHandle(fieldType);
            int elementSize = GetElementTypeSizeForPrimitiveElementType(vectorElementType);
            int overallTypeSize = SizeOfRuntimeTypeHandle(TypeOfRuntimeFieldHandle(fieldHandle.GetType()));

            // Check to ensure that the vector type has an appropriate layout for being a vector type
            if ((overallTypeSize % elementSize) != 0)
                throw new InvalidProgramException();

            // Ensure that the "vector" type doesn't have GC pointers in it, and that it solely consists of primitive types
            foreach (RuntimeFieldHandle field in GetInstanceFieldsOfType(type))
            {
                RuntimeTypeHandle fieldType = TypeOfRuntimeFieldHandle(field);
                CorElementType implementationSubtypeOfVectorType = GetElementTypeOfRuntimeTypeHandle(fieldType);
                if (implementationSubtypeOfVectorType == ELEMENT_TYPE_VALUETYPE ||
                    implementationSubtypeOfVectorType == ELEMENT_TYPE_CLASS)
                    throw new InvalidProgramException();
            }

            for (int iElement = 0; iElement < (overallTypeSize / elementSize); iElement++)
            {
                yield return new PrimitiveAlignmentInfo(vectorElementType, 
                                iElement * elementSize, 
                                (iElement + 1) * elementSize,
                                (iElement) * elementSize);
            }
        }
        else
        {
            throw new InvalidProgramException();
        }
    }

    int GetElementTypeSizeForPrimitiveElementType(CorElementType corElementType)
    {
        switch(corElementType)
        {
        case ELEMENT_TYPE_I1:
        case ELEMENT_TYPE_U1:
        case ELEMENT_TYPE_BOOLEAN:
            return 1;
        case ELEMENT_TYPE_I2:
        case ELEMENT_TYPE_U2:
        case ELEMENT_TYPE_CHAR:
            return 2;
        case ELEMENT_TYPE_I4:
        case ELEMENT_TYPE_U4:
        case ELEMENT_TYPE_R4:
            return 4;
        case ELEMENT_TYPE_I8:
        case ELEMENT_TYPE_U8:
        case ELEMENT_TYPE_R8:
            return 8;
        default:
            throw new InvalidArgumentException();
        }
    }

    // Runtime access features necessary

    RuntimeTypeHandle TypeOfRuntimeFieldHandle(RuntimeFieldHandle fieldHandle)
    { // Runtime Magic }

    // Get pointer to RVA data of field, or IntPtr.Zero if field is not RVA static field
    IntPtr GetRawDataPtrFromRVAStatic(RuntimeFieldHandle fieldHandle)
    { // Runtime Magic }

    int OffsetOfFieldHandle(RuntimeFieldHandle fieldHandle)
    { // Runtime Magic }

    // Returns the size of the data of the type. A field of the type will take this much space, unless it is a reference type, in which case it will
    // such a field will be of pointer size 
    int SizeOfRuntimeTypeHandle(RuntimeTypeHandle runtimeTypeHandle)
    { // Runtime Magic }

    // Return element type of field. All enums shall report as their associated primitive type, all non-enum valuetypes shall report as ELEMENT_TYPE_VALUETYPE, all reference types shall report as ELEMENT_TYPE_CLASS
    CorElementType GetElementTypeOfRuntimeTypeHandle(RuntimeTypeHandle runtimeTypeHandle)
    { // Runtime Magic }

    int AlignmentOfRuntimeTypeHandle(RuntimeTypeHandle runtimeTypeHandle)
    { // Runtime Magic }

    RuntimeFieldHandle[] GetInstanceFieldsOfType(RuntimeTypeHandle typeHandle)
    { // Runtime Magic }

    bool TypeHasAttribute(RuntimeTypeHandle typeHandle, string customAttributeType)
    { // Runtime Magic }

    // Return first type parameter or throw InvalidProgramException() if it doesn't have one
    RuntimeTypeHandle GetFirstTypeParameter(RuntimeTypeHandle typeHandle)
    { // Runtime Magic }
}
```

These runtime helpers will be used in a manner which closely resembles how the byte array initialization works

```
IL_0003: ldtoken valuetype '<PrivateImplementationDetails>'/'__StaticArrayInitTypeSize=27' '<PrivateImplementationDetails>'::'0C7A623FD2BBC05B06423BE359E4021D36E721AD'
IL_0008: call valuetype [System.Memory]System.ReadOnlySpan`1<!!0> System.Runtime.CompilerServices.RuntimeHelpers::CreateSpan<NonAlignedStruct>(RuntimeFieldHandle)
```
