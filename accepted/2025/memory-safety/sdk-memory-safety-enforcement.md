# SDK Unsafe Adoption

**Owner** [Fred Silberberg](https://github.com/333fred) | [Richard Lander](https://github.com/richlander)

## Background

Related documents:
* [Caller unsafe proposal](./caller-unsafe.md)
* [C# language proposal](https://github.com/dotnet/csharplang/blob/main/proposals/unsafe-evolution.md)

In .NET 11, we are adjusting the meaning of `unsafe` in C# and the .NET BCL to properly convey areas of memory unsafety, not just the presence of pointer
types. These rules will need to be controllable by end users as they decide to enable them for their projects in .NET 11 and beyond, and we will want to be
able to turn them on by default for new projects created with .NET 11. This document discusses how the SDK controls that and tells the compiler what
set of rules to use.

## Memory Safety Rules Version

The [metadata proposal](https://github.com/dotnet/csharplang/blob/main/proposals/unsafe-evolution.md#metadata) for C# details that we will stamp an attribute into modules that are compiled with
the new rules enabled, `MemorySafetyRulesAttribute`. Enabling these new rules will be controlled at the csc level by a new flag: `/memorysafetyrules:number`,
telling the compiler that version `number` of the memory safety rules should be used. This will then be controlled in the project file via the SDK, using a new
property:

```xml
<MemorySafetyRules>_number_</MemorySafetyRules>
```

We propose using a number, rather than a simple on/off flag, as there may be future updates to the rules to tighten controls around ownership and use-after-free
that aren't in the plan for .NET 11. We do not propose a `latest` or similar flag, as this an explicit breaking change that should be intentionally opted into,
rather than letting it float. For .NET 11, we expect that this flag will also need `LangVersion=preview`, as we do not expect that the feature will be ready for broad, unconditional
adoption until .NET 12 or 13.

### Open question: version number scheme

Right now, there are multiple different approaches being considered for the version number. These approaches are:

1. C# language version. This would follow the precedent set by `RefSafetyRulesAttribute`. Languages that want to interact with C#'s understanding of memory safety
   rules will need to know and understand what that version number means to C#.
2. .NET Runtime version. This would reflect the .NET-wide understanding of what memory safety means, and any languages (C# included) would need to know what the
   runtime's safety guarantees are in order to interact with the setting.
3. A different version. For example, 1 for legacy memory safety rules, 2 for the currently proposed updated rules, 3 and more for future refinements of the rules.

### Alternative: different name and value

Instead of `MemorySafetyRules`, some other proposed candidates for the name were `CallerUnsafe`, `UnsafeEnforcement`, `MemorySafety`,
although usually paired with something else than a number (like `true`/`false` or `enable`/`disable`).

### AllowUnsafeBlocks

The `AllowUnsafeBlocks` flag is related to this new feature, but is not directly impacted by it: the flag simply controls whether `unsafe` blocks are permitted
in the program itself. We want projects to opt into the new memory safety rules, regardless of whether `AllowUnsafeBlocks` is set, so that they get appropriate
errors when using the expanded definitions of `unsafe` APIs that properly covers things like `Unsafe.As`, the `Marshal` type, and others that hide memory
unsafety behind `IntPtr`.

## Memory Safety Diagnostic Severity

As discussed in [LDM 2026-04-29](https://github.com/dotnet/csharplang/blob/main/meetings/2026/LDM-2026-04-29.md#more-gradual-opt-in),
we might also introduce a separate flag for severity of diagnostics related to updated memory safety rules to toggle between errors (the default) and warnings (to be used during migration).
We could have a new flag (e.g., `MemorySafetySeverity`) or simply have the errors be downgradable to warnings via `WarningsNotAsErrors`,
ideally with some shorthand to specify all unsafe evolution related warning IDs at once (like we have the `nullable` shorthand).

## Compiler APIs

The Roslyn compiler will gain [APIs](https://github.com/dotnet/roslyn/issues/82791) and command-line options that match these project properties, specifically:
- `CSharpCompilationOptions.MemorySafetyRulesVersion` property,
- `/memorysafetyrules:number` command-line argument,
- `ModuleSymbol.MemorySafetyRulesVersion` property.

## Default enablement

In order to drive adoption of the feature, we will take the following approach:

* In .NET 11, the feature will be off-by-default and in preview, and we will not be recommending broad adoption by arbitrary users until we have collected more
  usability feedback and dialed in the enforcement. Users will be able to opt-in to the feature preview by putting `<Features>$(Features);updated-memory-safety-rules</Features>`
  and `<LangVersion>preview</LangVersion>` into their project files.
* In the .NET 12 or 13 timeframe, once we have the ecosystem experience that we are fully confident in, we will make the feature available via `<MemorySafetyRules>2</MemorySafetyRules>`, first as an opt-in.
  Our aspiration is to enable the new memory safety rules by default with opt-out eventually. The approach will be informed by the ecosystem experience we gain with .NET 11.

### File-based programs

For .NET 11, the new rules are not on by default. Users will be able to opt-in to the preview by adding `#:property Features=$(Features);updated-memory-safety-rules`
and `#:property LangVersion=preview` to their `.cs` files.

For .NET 12 and beyond, we are still evaluating the right approach for file-based programs. One possibility is to enable the new rules by default. Users would be able to opt-out by adding `#:property MemorySafetyRules=1` to their `.cs` files.
File-based programs are inherently subject to their runtime environment. Their TFM and defaults are dictated by their SDK, which provides more flexibility for enablement decisions. However, we will learn significantly more between now and then. The final approach will be informed by the ecosystem experience we gain with .NET 11.

**Open question**: Is it appropriate to have differing behavior for file-based apps?
Should templates of `dotnet new` also opt into the updated rules?
