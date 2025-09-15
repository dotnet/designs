# Memory Safety in .NET

> **Memory safety** is the state of being protected from various software bugs and security vulnerabilities when dealing with memory access, such as buffer overflows and dangling pointers. Source: [Wikipedia](https://en.wikipedia.org/wiki/Memory_safety)

Security is our top priority. The .NET platform is a secure runtime environment, grounded in [memory and type safety](https://devblogs.microsoft.com/dotnet/why-dotnet/#safety), [defense-in-depth mitigations](https://github.com/dotnet/designs/blob/main/accepted/2021/runtime-security-mitigations.md), and other industry best practices. .NET enforces memory safety through automatic memory management, safety-aware code generation, and the separation of safe and [unsafe code](https://learn.microsoft.com/dotnet/csharp/language-reference/unsafe-code). We intend to improve safety by reducing the cases where unsafe code is required and providing developers with actionable feedback on the safety of their projects.

This document provides an overview of the current state of memory safety in .NET and C#, the risks associated
with unsafe code, and proposes a set of improvements.

## Adapting to industry change

Memory safety is a critical priority for public and private sectors. [Cybersecurity and Infrastructure Security Agency (CISA)](https://www.cisa.gov/resources-tools/resources/memory-safe-languages-reducing-vulnerabilities-modern-software-development),  [Google](https://security.googleblog.com/2024/10/safer-with-google-advancing-memory.html), [OpenSSF](https://openssf.org/blog/2025/04/28/announcing-the-release-of-the-memory-safety-continuum/), and [Microsoft](https://msrc.microsoft.com/blog/2019/07/a-proactive-approach-to-more-secure-code/) have released reports defining the scale of memory-safety vulnerabilities and the direction of solutions, encouraging the use of safe languages.

Large Language Models (LLMs) add a new dimension to memory safety. Safe code is well-suited to generative AI. It is easier to understand, review, and modify with confidence. We recommend that developers configure their AI systems and build tools to permit only safe code. In the new AI paradigm, the compiler and analyzers become the final authority on safety.

We expect that code safety requirements will become more strict over time and that current guidance is primarily oriented on code-execution for client and cloud apps, but that LLM-based source-code-generation has not yet been "priced in". This project is intended as a proactive answer to new code safety requirements that we expect in upcoming years.

## Project plan

Minimum requirements:

- Introduce new [C# unsafe annotation and compiler feedback](https://github.com/dotnet/designs/pull/330).
- Annotate unsafe public APIs in dotnet/runtime libraries so that the compiler can provide feedback about their use.

Opportunistically:

- Transform legacy unsafe code to safe in dotnet/runtime
- Improve the [capability and performance of safe code](https://github.com/dotnet/runtime/issues/94941).
- Introduce ownership and/or lifetime concepts for mutable shared data (for example for `ArrayPool`).

This project is expected to start with .NET 11. The opportunistic work can occur in parallel and over multiple releases. We have no plans to backport these improvements to earlier .NET versions.

The .NET libraries are primarily written in C#, making our own team the first user of this project. A side-effect is that adopting this new model in the .NET libraries will deliver improved safety for all users. This a much shorter path to delivering an improved safety than if the libraries were written in an unsafe language. We expect a drop in annual .NET CVEs as a result of the project, on the order seen in the referenced papers.

## C\# language

C# is classified as a memory safe language by [an international partnership of government cybersecurity agencies](https://www.nsa.gov/Press-Room/Press-Releases-Statements/Press-Release-View/Article/3608324/us-and-international-partners-issue-recommendations-to-secure-software-products) and [OpenSSF](https://github.com/ossf/Memory-Safety/blob/main/docs/definitions.md). We intend for these classifications to remain, even as the criteria for memory safety evolves.

C# is recognized as memory-safe due to:

- Strong typing.
- Explicit initialization of all locals.
- Automatic memory management via garbage collection.
- Bounds checking for all memory accesses including arrays, strings and spans.
- Escape analysis for references and ref struct types.
- Unsafe code requires explicit opt-in via the `unsafe` keyword using the `AllowUnsafeBlocks` compiler option.

The discussion in this document primarily focuses on C#, the most popular .NET language. However, the same principles and concepts are applicable to other .NET languages such as F# and VB.NET.

## Performance

Safety and performance can appear to be in tension. For example, bounds checks are required to maintain safe array access. We intend to provide safe alternatives for common unsafe patterns with little or no performance penalty. We've also found that newer _safe_ patterns outperform some older _unsafe_ patterns that may have once been considered required (and still used).

Over time, we have developed an alternative programming model using APIs (and keywords) that are memory-safe, deterministic, and high-performance. This trend began with the introduction of value types and generics as early .NET features and continued with the later introduction of `ReadOnlySpan<T>`, `Span<T>`, `ref struct`, `ref return`, `ref fields`, and the `scoped`/`unscoped` keywords. These features enable developers to write high performance code without sacrificing memory safety. We intend to make idiomatic safe code fast (often relying on these capabilities), while also offering developers greater control with these specialized APIs.

We believe that performance-oriented developers will choose safe code if it is fast and has low or no allocations. We've consistently published [annual updates on performance](https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-10/). Beginning with .NET 11, annual updates will include how we've improved the performance for safe code.

## Unsafe code

Unsafe code has the characteristic that the responsibility of soundness and safety shifts to the programmer. Unsafe code is a reality in all general-purpose language platforms.

Unsafe C# (in an `unsafe` context) can:

- Bypass bounds checks for arrays and spans
- Perform zero-overhead (unchecked) casts
- Reinterpret types
- Coalesce loads/stores

Interop and [vectorization with SIMD APIs](https://github.com/dotnet/runtime/issues/111309) are common scenarios for using unsafe code.

The C# compiler does not provide any additional hints or warnings about potential memory safety issues in unsafe code besides requiring the explicit `unsafe` context and `AllowUnsafeBlocks` compilation flag. We are unable to provide a complete solution for validating the correctness of unsafe code and want to avoid a false sense of safety for code that doesn't raise any warnings. Potential additional tools that look for specific bad patterns in unsafe code will be a lower priority. Developers writing and reviewing unsafe code take on the sole responsibility for ensuring that the code is sound.

The [Unsafe class](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.unsafe) provides generic, low-level functionality for manipulating managed and unmanaged pointers. It **does not** (at the time of writing) require an `unsafe` context around consuming code, even though it presents similar risk as code allowed in an `unsafe` context (as suggested by the class name). Other types are similar, tracked by [dotnet/runtime #41418](https://github.com/dotnet/runtime/issues/41418). We plan to change these APIs to require callers use an `unsafe` context.

## Open questions

The following topics will be considered as the project progresses.

- Reflection can be used to access private object state and other implementation details in ways which can lead to memory safety vulnerabilities.
- C# does not have a system for tracking "ownership" and "lifetime"
for heap-allocated objects that can lead use-after-free violations, for example with `ArrayPool<T>.Shared.Rent` and `Return`.
- There is no built-in concurrency safety in .NET. Instead, developers need to follow patterns, adopt analyzers, or use thread-safe APIs to avoid undefined behavior.

## References

- [What is memory safety and why does it matter?](https://www.memorysafety.org/docs/memory-safety/#how-common-are-memory-safety-vulnerabilities). memorysafety.org
- [The Case for Memory Safe Roadmaps](https://www.cisa.gov/sites/default/files/2023-12/The-Case-for-Memory-Safe-Roadmaps-508c.pdf). CISA, NSA, FBI, et al. December 2023.
- [Exploring Memory Safety in Critical Open Source Projects](https://www.cisa.gov/sites/default/files/2024-06/joint-guidance-exploring-memory-safety-in-critical-open-source-projects-508c.pdf). CISA, FBI, ACSC, CCCS. June 26, 2024
- [2023 CWE Top 10 KEV Weaknesses](https://cwe.mitre.org/top25/archive/2023/2023_kev_list.html). MITRE. 2023.
- [Future of Memory Safety. Challenges and Recommendations](https://advocacy.consumerreports.org/wp-content/uploads/2023/01/Memory-Safety-Convening-Report.pdf). Yael Grauer. January 2023.
- [Safer with Google: Advancing Memory Safety](https://security.googleblog.com/2024/10/safer-with-google-advancing-memory.html)
- [Memory safety in The Chromium Projects](https://www.chromium.org/Home/chromium-security/memory-safety/). chromium.org.
- [Memory Safe Languages in Android 13](https://security.googleblog.com/2022/12/memory-safe-languages-in-android-13.html). Google Security Blog. December 2022.
- [Unsafe Rust](https://doc.rust-lang.org/book/ch19-01-unsafe-rust.html). The Rust Programming Language.
- [A Proactive Approach to More Secure Code](https://msrc.microsoft.com/blog/2019/07/a-proactive-approach-to-more-secure-code/). Microsoft Security Response Center (MSRC) Blog. July 16, 2019.
- [JEP 471: Deprecate the Memory-Access Methods in sun.misc.Unsafe for Removal](https://openjdk.java.net/jeps/471). OpenJDK. 2024.
