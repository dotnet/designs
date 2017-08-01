# .NET Platform Dependent Intrinsics

## Introduction
The dotnet ecosystem supports many platforms and hardware architectures, and
through its runtime implementation, ensuring that any MSIL has reasonable
performance on different platform/hardware combinations. This consistency is
one of the key selling points of the dotnet stack, but for some high end apps
platform specific functionality needs to be available to achieve peak
performance.  Examples of this are particular hardware accelerated encoders
like crc32 on intel, or the particulars of the NEON SIMD instructions.  At the
low level these are not consistent across platforms so a general functionality
is not possible, and a higher-level, more abstract implementation, while
consistent, imposes a performance penalty unacceptable to implementors seeking
maximum app/service throughput.  To enable this last bit of performance
improvement dotnet defines platform dependent intrinsics. This allows
platform/hardware providers to define low level intrinsics that map to
particular hardware features that are not consistent - i.e. target dependent.
This document outlines the process for proposing and implementing these
intrinsics. This process is intended to be open and can be initiated and
implemented by any contributor or partner.

## Guidelines for Platform Dependent Intrinsics

1. A platform intrinsic should expose a specific feature or semantic that is
not in common with other platforms or hardware implementations.  
   * If the functionality is common and performant - make it platform
   independent. 
   * If instead the semantic is platform dependent, or there is a platform
   dependent high performance implementation, then there is a clear argument
   for making an assuming that the next point holds.
2. A platform intrinsic should be impactful.  Ideally they solve a particular 
user problem.  Stated another way, platform intrinsics add complexity to the 
runtime implementation and language stack so they should help a concrete user 
scenario.
3. A platform independent way of determining whether the current executing 
platform supports dependent functionality needs to be included.  Users need 
to be able to easily check for hardware acceleration.
4. Executing platform dependent APIs on a non supporting platform may result in 
a System.PlatformNotSupportedException or invalid instruction fault. Fallback
implementations maybe provided but are not required.

### Example:

On the intel platform there is a built in CRC32 implementation that the below 
example would expose for use in C#.  At high-level intrinsics are methods of 
static classes that are marked with the [Intrinsic] attribute.

```csharp
// SSE42.cs
namespace System.Runtime.CompilerServices.Intrinsics.X86
{
    public static class SSE42
    {
        public static bool IsSupported() { throw new NotImplementedException(); }

        // unsigned int _mm_crc32_u8 (unsigned int crc, unsigned char v)
        [Intrinsic]
        public static uint Crc32(uint crc, byte data) { throw new NotImplementedException(); }
        // unsigned int _mm_crc32_u16 (unsigned int crc, unsigned short v)
        [Intrinsic]
        public static uint Crc32(uint crc, ushort data) { throw new NotImplementedException(); }
        // unsigned int _mm_crc32_u32 (unsigned int crc, unsigned int v)
        [Intrinsic]
        public static uint Crc32(uint crc, uint data) { throw new NotImplementedException(); }
        // unsigned __int64 _mm_crc32_u64 (unsigned __int64 crc, unsigned __int64 v)
        [Intrinsic]
        public static ulong Crc32(ulong crc, ulong data) { throw new NotImplementedException(); }

        ......
    }
}
```

Note: This example hasn't been implemented yet - thus NotImplementedException 
rather than PlatformNotSupportedException.  So details could change going 
forward.

## Process

1. Design the API in the `System.Runtime.CompilerServices.Intrinsics` namespace. 
Take care to try and reuse what you can from the current system and ensure 
that other platforms can implement their functionality as well.
2. Open an issue in CoreFX for 
[API review](https://github.com/dotnet/corefx/blob/master/Documentation/project-docs/api-review-process.md#api-review-process)
3. Open an [issue](https://github.com/dotnet/coreclr/issues) 
in CoreCLR for implementation of intrinsic in Runtime/JIT link to CoreCLR
issues
4. After API review approval implement intrinsic in CoreCLR

## Cross Platform vs Platform Dependent
Dotnet favors cross platform functionality that has full ecosystem support.
Our current Vector<T> SIMD support for instance provides higher level access
to hardware acceleration. In general it is preferable to use this kind of
common functionality, but when dictated by performance, platform dependent
implementations needs to be available.  As a consequence of this there can be
multiple ways to implement functionality so careful consideration of the
trade-offs needs to be made.

### FAQ:

Q: What type and namespace should be used?  
A: System.Runtime.CompilerServices.Intrinsics is the top namespace, but below that 
we would expect a breakout based on architecture and platform.  Care should be 
taken to avoid collisions - an example might be an architecture like ARM, 
where there are multiple licensees - with the best recommendation being 
getting feedback from the community early.

Q: What should the method/parameters be named  
A: Simple and clear is the best bet but that doesn't always help. There
are a lot of cases where there is prior art in C++ for these intrinsics so
having something that parallels that implementation (maybe a bit more clearly
named) can make the intrinsics easier to use.  That being said developers
should try and follow regular C# naming conventions and choose names that
indicate the semantic usage.

Q: Is a software fallback implementation allowed? (Discussed above a bit)  
A: Fallback is allowed but not required.  For very low level
implementations a fallback could even be misleading.

Q: How are immediate operands handled?  
A: We're planning to add support for this through Roslyn but haven't 
settled on an implementation yet.  Stay tuned.

Q: How are overloads handled?  
A: Overloads are allowed but expected to be rare.  Lots of intrinsics are
likely to be determined by method name on their input type but as we see
in the example above, the overload case can be helpful.

Q: What happens when platform dependent intrinsics are compiled ahead of time (AOT)?  
A: There are two cases here.  First, the target independent checks for platform
capability are used.  These checks then become runtime checks and the
accelerated code as well as the user provided independent fallback are
preserved in the output and selected between based on the runtime check result.
Second, an unguarded platform intrinsic is used. If this is run on an platform
that doesn't support it then an low level illegal instruction fault is
generated.