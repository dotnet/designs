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

The [metadata proposal](https://github.com/dotnet/csharplang/pull/9814) for C# details that we will stamp an attribute into modules that are compiled with
the new rules enabled, `MemorySafetyRulesAttribute`. Enabling these new rules will be controlled at the csc level by a new flag: `/memorysafetyrules:number`,
telling the compiler that version `number` of the memory safety rules should be used. This will then be controlled in the project file via the SDK, using a new
property:

```xml
<MemorySafetyRules>_number_</MemorySafetyRules>
```

We propose using a number, rather than a simple on/off flag, as there may be future updates to the rules to tighten controls around ownership and use-after-free
that aren't in the plan for .NET 11. We do not propose a `latest` or similar flag, as this an explicit breaking change that should be intentionally opted into,
rather than letting it float. For .NET 11, we expect that this flag will be `preview`, as we do not expect that the feature will be ready for broad, unconditional
adoption until .NET 12 or 13.

### Open question: version number scheme

Right now, there are two different approaches being considered for the version number. These approaches are:

1. C# language version. This would follow the precedent set by `RefSafetyRulesAttribute`. Languages that want to interact with C#'s understanding of memory safety
   rules will need to know and understand what that version number means to C#.
2. .NET Runtime version. This would reflect the .NET-wide understanding of what memory safety means, and any languages (C# included) would need to know what the
   runtime's safety guarantees are in order to interact with the setting.

### AllowUnsafeBlocks

The `AllowUnsafeBlocks` flag is related to this new feature, but is not directly impacted by it: the flag simply controls whether `unsafe` blocks are permitted
in the program itself. We want projects to opt into the new memory safety rules, regardless of whether `AllowUnsafeBlocks` is set, so that they get appropriate
errors when using the expanded definitions of `unsafe` APIs that properly covers things like `Unsafe.As`, the `Marshal` type, and others that hide memory
unsafety behind `IntPtr`.

## Default enablement

In order to drive adoption of the feature, we will take the following approach:

* In .NET 11, the feature will be off-by-default and in preview, and we will not be recommending broad adoption by arbitrary users until we have collected more
  usability feedback and dialed in the enforcement. Users will be able to opt-in to the feature preview by putting `<MemorySafetyRules>preview</MemorySafetyRules>`
  into their project files.
* In the .NET 12 or 13 timeframe, once we have the ecosystem experience that we are fully confident in, we will have an opt-in flag `<MemorySafetyRules>2</MemorySafetyRules>`. Users that take no action will see no changes.

### File-based programs

For .NET 11, the new rules are not on by default. Users will be able to opt-in to the preview by adding `#:property MemorySafetyRules=preview` to their `.cs` files.

In .NET 12, file-based programs will behave differently from projects. We enable the new rules by default for file-based programs. Users that wish to opt-out of the new rules can do so by adding `#:property MemorySafetyRules=1`
to their `.cs` files. File-based programs are inherently subject to their runtime environment (their TFM and defaults are dictated by their SDK), so we feel free to do
enablements like this both now and in the future.

**Open question** [ ] Is it appropriate to have differing behavior for file-based apps?
