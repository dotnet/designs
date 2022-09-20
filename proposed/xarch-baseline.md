# Revise the baseline machine for x86/x64 Hardware

**Owner** [Tanner Gooding](https://github.com/tannergooding)

## Proposal

The minimum required hardware for x86/x64 on .NET should be changed from `x86-x64-v1` to `x86-x64-v2`

## Historical Context

x86 hardware is well-known for its backwards compatiblity and has been incrementally versioned on the same baseline since the introduction of the Intel 8086 in 1978 with its 16-bit instruction set. When the Intel 80386 was released in 1985, it was a direct extension of the original chips and you had to explicitly transition out of 16-bit mode and into 32-bit mode. The same was true for when the AMD Athlon was released in 2003 and exposed support for the 64-bit instruction set. In between these "key" milestones, various optional instruction sets have been added, most notably of which are typically the SIMD instruction sets starting with SSE in 1999 and SSE2 in 2000.

With the introduction of the 64-bit instruction set, the minimum baseline changed from simply "base instructions" to also include `CMOV`, `CMPXCHG8B`, `SSE`, and `SSE2` (among other less important ones). The 3 major compilers (MSVC, Clang, and GCC) have also, over time, migrated their 32-bit baselines to match this and thus all native x86/x64 apps compiled today will target this "common demoninator". However, since the introduction of the 64-bit instruction set we have not had a major architectural revision that has forced apps to recompile and thus no newer baseline has been defined in 19 years.

That being said, there have been newer "versions" of the 64-bit ISA defined. That is, much as ARM64 has defined `armv8.0-a`, `armv8.1-a`, ..., `armv8.6-a`, etc; x64 has defined `x86-x64`, `x86-x64-v2`, ..., `x86-x64-v3`. These versions are defined as part of the `x86-x64-ABI` for Linux and were created as a joint effort between `AMD`, `Intel`, `Red Hat`, and `SUSE`. They are as follows (noting dates are approximate as "first introduced" to when both AMD and Intel provided hardware with the required support):
* `x86-x64-v2` (~2007-2009+): `CMPXCHG16B`, `LAHF/SAHF`, `SSE4.2`, `POPCNT`
* `x86-x64-v3` (~2013-2015+): `AVX2`, `BMI1`, `BMI2`, `F16C`, `FMA`, `LZCNT`, `MOVBE`
* `x86-x64-v4` (~2017-????+): `AVX512F`, `AVX512BW`, `AVX512CD`, `AVX512DQ`, `AVX512VL`

## .NET's instruction usage

Since SIMD support was first introduced for .NET in 2014 customers have added varying levels of dependence on the functionality to accelerate core algorithms. With these types being moved in box in .NET Core 2.1 and the introduction of platform specific hardware intrinsics in .NET Core 3.0, the usage of these instructions to accelerate core BCL algorithms has become increasingly more prevalent and has been the backing factor for many of the performance wins each release. With the introduction of Crossgen 2, we have likewise updated the default R2R baseline to be a semi-custom set of instructions that are compatible with the base instruction encoding mechanism; this notably includes SSE4.1. However, .NET continues targeting `x86-x64-v1` as its minimum baseline (for 64-bit and an equivalent set for 32-bit).

This has normally not been a large consideration since .NET has typically been expected to run in an environment where a JIT exists and therefore where code could be "re-jitted" for the current hardware, thus allowing it to take advantage of newer instruction sets, especially for known hot code. However, with the introduction of Native AOT we now have a higher consideration for scenarios where recompilation is not possible and therefore whatever the precompiled code targets is what all it has access to. There are some ways around this such as dynamic ISA checks or compiling a given method twice with dynamic dispatch selecting the appropriate implementation, but this comes with various downsides and often requires restructuring code in a way that can make it less maintainable.

Likewise, SSE2 only provides a small subset of functionality primarily targeting `float`, `double`, and a handful of basic integer operations. Many of the instructions required to "complete" the integer support are provided in newer ISAs such as `SSE3`, `SSSE3`, `SSE4.1`, and `SSE4.2`. This is in contrast to a newer platform like `armv8.0-a` which was introduced in 2011 and therefore was able to provide more functionality as part of their baseline. What this means is that various SIMD acceleration techniques can easily become pessimized on the `x86-x64-v1` baseline because core instructions aren't available and alternatives must be substituted instead resulting in harder to maintain code.

## Modern Hardware/Software Expectations

Windows 8.1+ requires `CMPXCHG16B` and `LAHF/SAHF` for 64-bit and due to these being newer instruction sets, they imply a post `x86-64-v1` (but potentially pre `x86-x64-v2`) dependency. Windows Server 2008 R2+ and Windows 11+ both only support 64-bit processors. Windows 11 additionally only supports processors that are newer than Intel Coffee Lake or AMD Zen+. Windows versions prior to 8.1 are out of support for normal scenarios. Windows 7 remains in support only with a special license but was itself released in 2009 and only officially supported hardware from around the same timeframe. Notably the Operating System may run on other hardware, but doing so is technically unsupported.

macOS is on an approximate 3 year support cycle and owns their entire supply chain. macOS 11 is currently the oldest supported version and only supports hardware dating back to ~2013 and therefore which provides `x86-x64-v3` support. One would have to go back to before `macOS 10.12` (that is to `OS X 10.11`) to find hardware that doesn't meet the `x86-x64-v2` baseline.

Linux is an interesting scenario where they continue supporting older hardware by design and indeed the kernel purports to continue supporting the `80386` and therefore a pre `x86-x64-v1` baseline.

For the cloud market, Azure, AWS, and Google Cloud all provide `x86-x64-v3` compatible or newer hardware as a minimum (to the best of my knowledge, based on publicly exposed hardware information).

For customers reporting to `Steam` on their hardware data shows the following:
* `SSE3` - 100%
* `LAHF/SAHF` - 99.99%
* `CMPXCHG16B` - 99.99%
* `SSSE3` - 99.45%
* `SSE4.1` - 99.15%
* `SSE4.2` - 98.83%
* `AVX` - 94.89%
* `AVX2` - 88.15%
* `AVX512F` - 8.3%

From the above, we can see that only 1.17% users don't meet the `x86-x64-v2` baseline and by comparison only 11.85% of users don't meet the `x86-x64-v3` baseline. We can presume that not all users are reporting and that some of them may be on hardware that is more than 13-15 years old (or which otherwise doesn't meet the `x86-x64-v2` baseline). However, the expectation is that such hardware is rapidly decreasing in prevalence and that such users will have many more complications with trying to run modern versions of .NET or Operating Systems.

## Benefits and Drawbacks

The main drawback of updating the baseline will be that older hardware will no longer be able to target new versions of .NET and thus may be left behind if they have hardware without support for the `x86-x64-v2` instructions. Such users are already likely in a pessimized scenario however as this it not hardware we currently test against nor which we profile/benchmark for and thus it is increasingly likely that  perf or other regressions are introduced.

However, there are many potential benefits for updating the baseline the primary of which is that it will provide an overall more consistent for users targeting AOT scenarios. Additionally, it would allow code to be simplified in the JIT as we could remove code paths designed to support such older hardware and would in turn make the product easier to maintain, version, and test.

## Alternatives

We could maintain the `x86-x64-v1` baseline for the JIT (optionally removing pre `v2` SIMD acceleration) while changing the default for AOT. This could emit a diagnostic by default elaborting to users that it won't support older hardware and indicate how they could explicitly retarget to `x86-64-v1` that is important for their domain.
