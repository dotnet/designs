# .NET Platform Dependent Intrinsics

**Dev** [Russell Hadley](https://github.com/russellhadley)

## Introduction
The dotnet ecosystem supports many platforms and hardware architectures, and
through its runtime implementation, ensuring that any MSIL has reasonable
performance on different platform/hardware combinations. This consistency is
one of the key selling points of the dotnet stack, but for some high end apps
platform specific functionality needs to be available to achieve peak
performance.  Examples of this are particular hardware accelerated encoders
like crc32 on Intel, or the particulars of the NEON SIMD instructions.  At the
low level these are not consistent across platforms so a general functionality
is not possible, and a higher-level, more abstract implementation, while
consistent, imposes a performance penalty unacceptable to implementors seeking
maximum app/service throughput.  To enable this last bit of performance
improvement dotnet defines platform dependent intrinsics. This allows
platform/hardware providers to define low level intrinsics that map to
particular hardware features that are not available on all platforms.
This document outlines the process for proposing and implementing these
intrinsics. This process is intended to be open and can be initiated and
implemented by any contributor or partner.

## Guidelines for Platform Dependent Intrinsics

1. A platform intrinsic should expose a specific feature or semantic that is not universally available on all platforms, and for which the implemented function is not easily recognizable by the JIT.  
   * If the functionality is common and performant - make it platform
   independent.
     * Note that, in some cases, there may be intrinsics that are broadly available, but 
       which differ enough that providing only a platform-independent API constrains its use.
       In these cases, it may be best to provide both: a platform-independent API that
       calls the platform-dependent implementation that provides full flexibility.
   * If instead the semantic is platform dependent, or there is a platform
   dependent high performance implementation, then there is a clear argument
   for making an assuming that the next point holds.
2. A platform intrinsic should be impactful.  Ideally they solve a particular 
user problem.  Stated another way, platform intrinsics add complexity to the 
runtime implementation and language stack so they should help a concrete user 
scenario.
   * If a set of intrinsics are logically associated (e.g. a vector intrinsic that operates over multiple base types), those intrinsics should generally be implemented together, time and resources permitting, even if only a subset have compelling impact.
3. A platform independent way of determining whether the current executing 
platform supports dependent functionality needs to be included.  Users need 
to be able to easily check for hardware acceleration. This is accomplished by grouping into a single class all the instrutions that are exposed as a group by the platform (e.g. `SSE4.2`), and providing an `IsSupported` property to indicate that they are available.
4. Executing platform dependent APIs on a non supporting platform will result in a System.PlatformNotSupportedException. Fallback
implementations are provided only to support diagnostic tools (e.g. via reflection). See [Supporting Diagnostic Tools](#supporting-diagnostic-tools)
5. The C# implementation of a platform intrinsic is recursive. When a call to such an intrinsic is originally encountered, if it
doesn't meet the constraints for directly generating the target instruction, the JIT will not expand the intrinsic. When the JIT
is called to compile the recursive method, the VM will tell the compiler that it `mustExpand` the recursive intrinsic call,
in which case the JIT will either generate a full expansion (in the case of a non-constant argument for an instruction that
requires an immediate), or generate the code to throw the appropriate exception.

### Usage

It is expected that the platform intrinsics will be used by developers for whom runtime performance is critical, and who will
carefully analyze their usage to ensure that they are meeting their requirements.

The following exceptions may be thrown by platform intrinsics:
* `PlatformNotSupportedException` - when the intrinsic is not supported on the current hardware.
* `TypeNotSupportedException` - when the intrinsic is instantiated with a type parameter that is not supported on the current hardware.
* `ArgumentRangeException` - when an argument of the intrinsic (e.g. an immediate) is out of the expected range of values.

### Example:

On the Intel platform there is a built in CRC32 implementation that the below 
example would expose for use in C#.
This code resides in the coreclr/src/mscorlib/src/System/Runtime/Intrinsics/X86 directory.
The first snippet shows a partial implementation for the Intel (X86) platform.
The second snippet shows a partial implementation for other platforms, for which the
JIT need not recognize these as unsupported intrinsics.

```csharp
// SSE42.cs
namespace System.Runtime.Intrinsics.X86
{
    public static class SSE42
    {
        public static bool IsSupported { get => IsSupported; }
        ...
        /// <summary>
        /// unsigned int _mm_crc32_u8 (unsigned int crc, unsigned char v)
        ///   CRC32 reg, reg/m8
        /// </summary>
        public static uint Crc32(uint crc, byte data) => Crc32(crc, data);
        /// <summary>
        /// unsigned int _mm_crc32_u16 (unsigned int crc, unsigned short v)
        ///   CRC32 reg, reg/m16
        /// </summary>
        public static uint Crc32(uint crc, ushort data) => Crc32(crc, data);
        /// <summary>
        /// unsigned int _mm_crc32_u32 (unsigned int crc, unsigned int v)
        ///   CRC32 reg, reg/m32
        /// </summary>
        public static uint Crc32(uint crc, uint data) => Crc32(crc, data);
        /// <summary>
        /// unsigned __int64 _mm_crc32_u64 (unsigned __int64 crc, unsigned __int64 v)
        ///   CRC32 reg, reg/m64
        /// </summary>
        public static ulong Crc32(ulong crc, ulong data) => Crc32(crc, data);

        ...
    }
}
// SSE42.PlatformNotSupported.cs

namespace System.Runtime.Intrinsics.X86
{
    public static class SSE42
    {
        public static bool IsSupported { get { return false; } }
        ...
        /// <summary>
        /// unsigned int _mm_crc32_u8 (unsigned int crc, unsigned char v)
        ///   CRC32 reg, reg/m8
        ...
    }
}
```
## Process

1. Design the API in the `System.Runtime.Intrinsics` namespace. 
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

### Commonality across platforms

Platform intrinsics are intended to provide a 1-to-1 mapping to instructions on the target platform. While many of these will be highly platform-specific, some functionality is likely to be common across platforms. It is desirable to use consistent names and signatures where possible.

## Supporting Generic Intrinsics

Many platform intrinsics support vectors over all primitive numeric types, in which case a generic API is the clear choice. However, some intrinsics are only available for a limited set of types, or the different element types are not all provided in the same platform class. In this case, there is no clear choice between offering a generic API and "exploding" the specific supported types. Some examples:
- On Intel architecture, there are intrinsics that are only available on float vectors in earlier classes, but are offered for a broader range of types in later classes (e.g. Add over 256-bit vectors on AVX vs. AVX2). For these, it makes sense to explicitly declare the supported types.
- ARM64 intrinsics include comparisons against zero, but they do not support unsigned types. For these, a trivial JIT expansion could be used to enable all the primitive types to be supported.

In general, it is preferable for a particular instantiation not to be visible in the API, rather than for the use of an unsupported type parameter to result in a runtime error.

## Versioning and Partial Implementation

The platform intrinsics will be versioned with the Target Framework. New intrinsics can be added within an existing platform class, but all intrinsics declared in the platform class must be implemented in the JIT at the same time that it is exposed in the library.

## Optionality of Expansion

In general, it is expected that platform intrinsics will be expanded regardless of optimization level. This differs from the handling of other kinds of intrinsics primarily due to the fact that the performance difference between direct execution and fallback (even simply introducing a call) can be quite high, along with the fact that the JIT uses the non-optimizing path for methods that are very large. The combination presents an undesirable performance cliff.

## Supporting Diagnostic Tools

The implementation of the intrinsic must enable indirect invocation (e.g. via diagnostic tools or reflection). This is accomplished as follows:
   * The source implementation (e.g. in C#) invokes itself recursively for the target platform. On other platforms, the source implementation directly throws `PlatformNotSupportedException`.
   * When the JIT encounters an intrinsic that it recognizes, it checks the constraints that it requires to generate the simple expansion. This may include:
      * Checking whether one or more operands is an immediate. This is required for some instructions, such as shuffle, that require an immediate and have no corresponding register form.
      * Checking whether the generic type parameters, if any, match those
      supported for the current architecture. See [Supporting Generic Intrinsics](#suporting-generic-intrinsics) above.
   * If the intrinsic fails to meet the necessary criteria:
      * If the method is recursive, i.e. `mustExpand` is true:
         * If the constraint is one that the JIT must expand in order to support reflection and diagnostic tools (currently only the immediate operand case), it will produce an expanded implementation (e.g. a switchtable of possible immediate values).
           * Note that it is undesirable for the reflection and diagnostic tool scenario to be supported via a software emulation
             of the instruction in managed code. This is because the switchtable approach ensures that each case of the
             switch will execute using the target instruction with the given inputs, ensuring fidelity.
         * Otherwise, the JIT throws the appropriate exception.
      * Otherwise (the method is not recursive), the JIT generates a regular call. This will cause the IL implementation to be invoked, and will trigger the recursive case above if it has not yet been JIT'd.
    * Otherwise the intrinsic will be expanded in its default simple form.

If the intrinsic is supported only for the constant case, and the argument passed is not a constant, then it should be treated as a regular call. This will then cause the IL implementation to be JIT'd (if it has not already been). Then we will land in the "we are recursive" case and the JIT will generate the switch statement.
If the intrinsic is not supported for some other reason, e.g. for the given generic base type, the JIT must generate code to throw the appropriate exception.

### FAQ:

Q: What type and namespace should be used?  
A: System.Runtime.Intrinsics is the top namespace. Below that 
is a breakout based on architecture and platform.  Care should be 
taken to avoid collisions - an example might be an architecture like ARM, 
where there are multiple licensees - with the best recommendation being 
getting feedback from the community early.

Q: What should the method/parameters be named?  
A: Simple and clear is the best bet but that doesn't always help. There
are a lot of cases where there is prior art in C++ for these intrinsics so
having something that parallels that implementation (maybe a bit more clearly
named) can make the intrinsics easier to use.  That being said developers
should try and follow regular C# naming conventions and choose names that
indicate the semantic usage. In addition, if an equivalent intrinsic has been defined for another platform, *with the same functionality*, the naming should be consistent.

Q: Is a software fallback implementation allowed? (Discussed above a bit)  
A: Fallback is expressly discouraged (note that the immediate operand fallback described above is not strictly a "software" fallback,
as it utilizes the same target instruction, generated by the same JIT). Ensuring fidelity with the
hardware semantics is difficult to ensure, and for very low level implementations a fallback could be misleading.

Q: If C# adds support for a const qualifier, will this eliminate the need for switchtable expansion in the JIT?  
A: No. While it might improve the usability, not all compilers may recognize the attribute, and we would still have
to support the non-constant case for the reflection and diagnostic tools scenarios. It is expected that analysis tools
will be developed to identify cases where an intrinsic is called with a non-constant value when a constant is expected.

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
