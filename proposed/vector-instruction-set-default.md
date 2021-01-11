# Target AVX2 in R2R images

[Vector instructions](https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-5/#intrinsics) have become one of the key pillars of performance in the .NET libraries. Because of that, .NET apps light up a greater percentage of the transistors in a CPU/SOC than they previously did. That's progress. However, we've left performance opportunities on the table that we should consider picking up to make .NET apps run faster and more cheaply (in hosted environments).

Related: [Initial JIT support for SIMD](https://devblogs.microsoft.com/dotnet/the-jit-finally-proposed-jit-and-simd-are-getting-married/)

The minimum supported [SIMD instruction set](https://en.wikipedia.org/wiki/SIMD) for and required by .NET is [SSE2](https://en.wikipedia.org/wiki/SSE2). SSE2 predates the x64 processor by a couple years. SSE2 is old! However, since it is the minimum supported SIMD instruction set, the native code present and executed in ready-to-run images is (you guessed it) SSE2-based. That means your machine uses slower SSE2 instructions even if it (and it almost certainly does) supports the newer [AVX2](https://en.wikipedia.org/wiki/Advanced_Vector_Extensions#Advanced_Vector_Extensions_2) instructions.We rely on the JIT to take advantage of newer (and wider) SIMD instructions than SSE2, such as AVX2. The way the JIT does this is good but not aggressive enough to matter for startup or for apps with shorter life spans (measured in minutes).

SSE4 was introduced with the [Intel Core 2](https://en.wikipedia.org/wiki/Intel_Core_(microarchitecture)) launch in 2006, along with x64. That means that all x64 chips are SSE4 capable, and that SSE2 is really just a holdover from our 32-bit computer heritage. That would make SSE2 a poor baseline for x64 software, but a good one for 32-bit.

AVX2 was released in late 2013 with [Intel Haswell](https://en.wikipedia.org/wiki/Advanced_Vector_Extensions#CPUs_with_AVX2) processors. After more than seven years in market, we should be able to take advantage of AVX2 more actively, to the end of improved performance, and lighting up even more CPU transistors.

The advantage of AVX2 is not tens of percentage points. It is important not to oversell the importance of using later SIMD instructions. In the general case, using AVX2 might deliver low double-digit wins. For server apps that run all day, this could be important, and might allow for using a lower-cost tier of cloud machine and/or delivering higher performance. We also expect a significant startup improvement.

You might be wondering about [AVX-512](https://en.wikipedia.org/wiki/AVX-512). Hardware intrinsics have not yet been defined (for .NET) for AVX-512. AVX-512 is also known to have more [nuanced performance](https://lemire.me/blog/2018/08/25/avx-512-throttling-heavy-instructions-are-maybe-not-so-dangerous/). For now, our vectorized code tops out at AVX2 for the x86-x64 [ISA](https://en.wikipedia.org/wiki/Instruction_set_architecture). Also, too few machines support AVX-512 to consider making it a default choice.

You might be wondering about Arm processors. Assuming we adopted this plan for x86-x64, we'd do something similar for Arm64. The Arm ISA defines [NEON](https://en.wikipedia.org/wiki/ARM_architecture#Advanced_SIMD_(Neon)) as part of [Armv7](https://en.wikipedia.org/wiki/ARM_architecture#32-bit_architecture), NEON (enhanced) in [Armv8A](https://en.wikipedia.org/wiki/AArch64#AArch64_features) and [SVE](https://en.wikipedia.org/wiki/AArch64#Scalable_Vector_Extension_(SVE)) as part of [Armv8.2A](https://en.wikipedia.org/wiki/AArch64#ARMv8.2-A). [SVE2](https://en.wikipedia.org/wiki/AArch64#Scalable_Vector_Extension_2_(SVE2)) will appear sometime in the future. The .NET code base isn't vectorized much or at all for Arm32, such that this proposal doesn't apply for 32-bit Arm. On Arm64, we'd need to ensure that we make choices that take Raspberry Pi, laptops and any other form factors into consideration.

This proposal is primarily oriented around the Intel ISA because it is the first order of business to determine a solution for Intel, and more people are familiar with the Intel SIMD instructions.

There are possible downsides to this proposal. If we target AVX2 by default, people with non-AVX2 machines will have a significantly worse experience. The goal of this document is to propose a solution that delivers most of the expected benefit of AVX2 without much performance regression pain. It is easy to fall into the trap that we only need to worry about developers and think that developers only use the very latest machines. That's not true. There are also developers building client apps for end-users. We also continue to support Windows Server 2012 R2. Some of those machines are likely very old. There is also likely significant differences by country, possibly correlating to GDP. Needless to say, people using .NET as a developer or an end-user use a great variety of machines.

At the same time, we need to make choices that move .NET metrics and capabilities forward. Let's explore this topic.

Supporting ecosystem data:

- [Steam hardware survey](https://store.steampowered.com/hwsurvey) (see "Other Settings")
- [Azure VMs](https://azure.microsoft.com/en-us/pricing/details/virtual-machines/series/) -- these all support AVX2; we expect same for other clouds

Note: "SSEx" is used to refer to any of the SSE versions, like SSE2 and SSE4.

## .NET 5.0 behavior

Let's start with the SIMD characteristics for .NET 5.0:

- Ready-to-run images target SSE2 instructions for vectorized code (not just by default; it's the only option). That means ready-to-run images  include processor-specific machine code with SSE2 instructions and never any AVX/AVX2 instructions.
- The JIT (via tiered compilation) can re-jit methods so that they take advantage of a newer SIMD instruction set (if present on a given machine), like SSE4, AVX, or AVX2. The JIT doesn't do this proactively, but will only rejit methods that are used a lot, per its normal behavior. It doesn't re-jit methods solely to use better SIMD instructions, but will use the best SIMD instructions available (on a given machine) when it does rejit methods, again, per its normal behavior.
- As a result, applications rarely run at peak performance initially (since most machines support newer SIMD instructions than SSE2) and it takes a long time for an app to get to peak performance (via tiering to SSE4, AVX, or AVX2-enabled code).
- This behavior is fine for machines with only SSE2, and is leaving performance on the table for newer machines.

## Context, characteristics and constraints

The most obvious solution is to target AVX2 by default, and this approach would be the clear winner for anyone using a machine that was bought after 2015. The challenge is that this change would be significant regression for SSEx machines, even more significant than the win it would provide for AVX2-capable machines, in the general case.

The primary issue is that ready to run code for (what is likely to be) performance sensitive methods would no longer be usable on SSEx machines, but would require JITing IL methods instead. The resultant native code would be the same (actually, it would be better; we'll leave that topic for another day), but would require time to produce via JIT compilation. As a result, startup time for apps on SSEx machines would be noticeably degraded. We guess the startup regression would be unacceptable, but need to measure it.

Making AVX2 the default would be the same as dropping support for SSEx. If we do that, we'd need to announce it, and reason about what an SSEx offering looks like, if any.

This discussion on [AVX and AVX2 requirements from Microsoft Teams](https://medium.com/365uc/why-cant-i-use-background-blur-or-custom-backgrounds-in-microsoft-teams-a514e9da3921) provides a sense of how hardware requirements can affect users.

The [Steam hardware survey](https://store.steampowered.com/hwsurvey) tells us that ~98% of Steam users have SSE4, 94% have AVX, and ~80% have AVX2. We could instead target one of the SSE4 variants. The performance win wouldn't be as high, but the effective regression would be much more limited. Targeting the middle is unappealing when we know almost all developers and all clouds are AVX2-capable. We need a more appealing solution, without as much tension between tradeoffs.

Note: Stating the obvious, Steam users and .NET users don't fully overlap, however, the Steam hardware survey provides a great view on PC computing in the world. One can guess that .NET users might be using 5 points more or less (that's a 10 point swing) modern hardware than Steam users, but not much more than that (in either direction). Also, if Unity was ever to take a dependency on upstream .NET, they'd need it to work on Steam machines. That's definitely not an announcement, just emphasizing that using the Steam data is reasonable.

It is important to realize that this proposal is for a product that will be released in November 2021, and not likely used significantly until 2022. Early 2022 should be the primary time frame kept in mind. The Steam survey, for example, will report different numbers at that time. It's possible AVX2 will have reached today's AVX value by next year. To that point, PC sales have had a [surprising resurgence](https://www.idc.com/getdoc.jsp?containerId=prUS47274421) due to the pandemic, resulting in a lot of old machines being removed from the worldwide fleet (at least in countries with high GDP).

An important degree of freedom is making different choices, per OS and hardware combination. Different OSes have a different hardware dynamic, different use cases, and a different future. Let's look at that. It's the heart of the proposal.

## Differ behavior by OS

It is reasonable to vary SIMD defaults, per operating system. There are many aspects of the product that differ by operating system or hardware platform. In fact, the JIT dynamically targeting different SIMD instructions per machine is such an example. For this case, the key consideration is the ready-to-run native code produced for a given operating system + CPU consideration. We could make one decision for Linux x64, another for Windows x64, and another for macOS Arm64. We already have to target different SIMD instructions for x64 vs Arm64, so targeting different SIMD instructions across OSes isn't much of a leap.

It is useful to look at hardware dates again. Continuing with the Intel focus, we can see the following delivery of SIMD instructions in chips:

* 2006: SSE4 was introduced with Core 2 x64 chips at the initial [Intel Core architecture](https://en.wikipedia.org/wiki/Intel_Core_(microarchitecture)) launch.
* 2008: SSE4.2 was introduced with [Nehalem](https://en.wikipedia.org/wiki/Nehalem_(microarchitecture)) chips.
* 2011: AVX was introduced with [Sandy Bridge](https://en.wikipedia.org/wiki/Sandy_Bridge) chips.
* 2013: AVX2 was introduced with [Haswell](https://en.wikipedia.org/wiki/Haswell_(microarchitecture)) chips.

Let's take a look at each operating system, assuming the Intel ISA.
 
* On Windows, we need to be conservative. There are a lot of .NET users on Windows, and the majority of .NET desktop apps target Windows. Windows 10 requires AVX, but Win7 requires only SSE2. It is reasonable to expect that we'll continue supporting Windows 7 with .NET 6.0 (and probably not after that).
* On [macOS](https://en.wikipedia.org/wiki/MacOS_version_history#Version_10.13:_%22High_Sierra%22), it appears that [Mac machines have had AVX2 since late 2013](https://en.wikipedia.org/wiki/List_of_Macintosh_models_grouped_by_CPU_type#Haswell), when they adopted [Haswell chips](https://en.wikipedia.org/wiki/Advanced_Vector_Extensions#CPUs_with_AVX2). In terms of macOS, [macOS Big Sur (11.0)](https://support.apple.com/kb/sp833?locale=en_US) appears to be the first version to require Haswell. .NET 5.0, for example, supports [macOS High Sierra (10.13)](https://support.apple.com/kb/SP765?locale=en_US) and later. macOS 10.13 supports hardware significantly before Haswell. For .NET 6.0, we'll likely continue to choose a "10." macOS version as the minimum, possibly [macOS Catalina (10.15)](https://support.apple.com/kb/SP803?locale=en_US), aligning with our past practice of slowly (not aggressively) moving forward OS requirements. Zooming out, macOS x64 is now a legacy platform. We shouldn't rock the boat with a change that could cause a performance regression at this late stage of the macOS x64 lifecycle.
* On Linux, we can assume that there is at least as much diversity of hardware as Windows, however, we can also assume that .NET usage on Linux is more narrow, targeted at developers and (mostly) production deployments.

The following is a draft plan on how to approach different systems for this proposal:

* Windows x86 -- Target SSE2 (status-quo; match Windows 7)
* Windows x64 -- Target AVX2
* Linux x64 -- Target AVX2
* macOS x64 -- Target SSE2 (status-quo; alternatively, target SSE4 if it is straightforward and has significant value)
* Arm64 (all OSes) -- Target Armv8 NEON
* Linux Arm32 -- N/A (there is very little vectorized code)
* Windows Arm32 -- N/A (unsupported platform)

Note on [NEON](https://en.wikipedia.org/wiki/ARM_architecture#Advanced_SIMD_(Neon)): The [Raspberry Pi 3 (and later) supports Armv8 NEON](https://en.wikipedia.org/wiki/Raspberry_Pi#Specifications). Apple M1 chips apparently have [great NEON performance](https://lemire.me/blog/2020/12/13/arm-macbook-vs-intel-macbook-a-simd-benchmark/).

There may be a significant set of users that have very old machines, either developers (all OSes) or end-users (primarily Windows). The Windows 32-bit offering should satisfy developers on very old Windows machines. We have no such offering for Linux. macOS developers should be unaffected. Developers may also have concerns about their end-users. They will be able to generate self-contained apps that are re-compiled (via crossgen2) to target an earlier SIMD instruction set (such as SSE2).

In conclusion, this plan should have the following outcomes:

- .NET apps will run at peak performance (as it related to vector instructions) on modern hardware.
- Developers who only have access to very old machines will have a satisfactory option on Windows.
- Developers have a supported path to satisfy end-users on very old machines.

## Next steps

- Productize crossgen2 (the new version of crossgen that includes the capability to target higher SIMD instructions than SSE2).
- Determine the performance wins and regressions of various SIMD instruction sets. For example, what is the difference between SSE2 and SSE4, and between AVX and AVX2? How large is the performance regression for SSE2-only machines?
- Determine the distribution of machines in the wild that support various SIMD instruction set. It is expected that nearly all developer machines and all cloud VMs (all clouds) support AVX2. Sites like [statcounter.com](https://gs.statcounter.com/os-version-market-share/macos/desktop/worldwide) may prove useful.
