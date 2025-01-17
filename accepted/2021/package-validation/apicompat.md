# API Compatibility

**Owner** [Santiago Fernandez Madero](https://github.com/safern)

In an effort to provide tools to help customers write long term compatible libraries there are different tools. Currently we have [`Microsoft.DotNet.ApiCompat`](https://github.com/dotnet/arcade/tree/0a67fb1cd008b0d30577f53d1633ed00db8bc1e6/src/Microsoft.DotNet.ApiCompat#microsoftdotnetapicompat) and [`Microsoft.CodeAnalysis.PublicApiAnalyzers`](https://github.com/dotnet/roslyn-analyzers/blob/master/src/PublicApiAnalyzers/PublicApiAnalyzers.Help.md). In between these two there is a huge difference on how these work, what rules they have and how to integrate it to libraries. 

`Microsoft.DotNet.ApiCompat` is heavily used in `ASP.NET`, `dotnet/runtime`, `ML.NET` and a few other products across the `dotnet` orgs. While `ApiCompat` has a lot of useful rules and functionality, it uses `CCI` as its metadata object model which brings some limitations on maintainibility and extensibility when new language features are added because `CCI` doesn't know about them and we have to implement those ourselves (i.e ref structs, readonly, nullable annotations, etc).

`Microsoft.CodeAnalysis.PublicAnalyzers` is used in `dotnet/roslyn` and it is a pure roslyn analyzer.

The biggest difference in between these is the inputs they take for the contract to know if the current implementation is a breaking change. `PublicAnalyzers` looks for two files in the project, `PublicAPI.Shipped.txt` to check for compat against previous versions of the library and `PublicAPI.Unshipped.txt` to check for compat on the current release. Wheareas `ApiCompat` takes two binaries, one for the contract and the other for the implementation and checks for the differences in between those two. These two different inputs are useful in different ways and customers have their own preference.

The proposal in this document is to create a tool that will help customers build compatible libraries by helping them identify breaking changes as part of their builds. This tool, will make sure that the public APIs in between an implementation and a contract (which can be a previous version or a current version of the library) are compatible to avoid introducing an accidental breaking change.

## How is this tool going to be consumed

We are proposing a tool that can be consumed as a Roslyn analyzer and also providing a dotnet tool that people can integrate to their build systems as an out of proc tool or run directly from the console. 

Something to consider would be adding an MSBuild Task entrypoint so that libraries that need to run it in their build, they don't need to shell out a process to invoke it and just run in-proc.

## Tool input scenarios

### 1. Text files

Just as `Microsoft.CodeAnalysis.PublicAnalyzers` currently accepts a `PublicAPI.Unshipped.txt` and `Public.Shipped.txt` files as inputs, we should preserve this behavior, but extend some of the rules and rule settings that we support so that all inputs have equal support.

### 2. Binary inputs

Having binaries as inputs is really helpful for compatibility, as this allows you to run a reference assembly against multiple runtime implementations (i.e win-x64, linux-x64, etc) and make sure you are compatible across all runtimes with previous versions of your library and also with the reference assembly.

### 3. Source inputs

Instead of binaries or text files, it might be helpful to capture what the contract is as a C# minimal file (somewhat like a native header file) that contains the API definitions and metadata. For this we would provide a separate tool, aka `GenAPI` that given an binary input produces minimal C# code. This tool will also be Roslyn based, protype can be found here: https://github.com/safern/roslyn-genapi. This provides a contract that is "easier" to read and is also buildable and produces a minimal reference assembly, currently what `dotnet/runtime` uses as a contract.

## Compatibility Rules

After looking at the existing tools, these is an overview of the proposed rules for the first version of the tool and that will run by default.

### 1. Attribute Difference
This rules checks attributes on public members and gives an error if there is a miss match on the attributes. This rule checks attributes on:
- Types (Class, Struct, Enum, Interface, Record, Delegate)
- Fields
- Properties
- Methods
- Event
- ReturnValue
- Constructor
- Generic Parameters

Should we check for assembly attributes maybe as opt-in?

It should check for the following:
1. Attribute does not exist in the contract or implementation.
2. Attribute arguments does not match in both the contract and implementation.
    2.1 For flag arguments (i.e AtrributeTargets) we should decide whether expanding or removing a flag is breaking or not.

We should allow a Rule setting to disable attribute diffing for a list of attributes where libraries can add attributes they don't care about compat, i.e `EditorBrowsableAttribute`. 

### 2. Cannot remove abstract members
This rules check for abstract members in types. This rule validates that a type has the same abstract members in both the contract and implementation when the type can be used as base type:
1. Is marked as sealed.
2. Has no non static visible constructors outside the assembly.

### 3. Cannot make member non virtual
This rule checks that a member should be virtual on both the implementation and in the contract when the member is overridable:
1. Is virtual.
2. Is not sealed.
3. Type is not sealed.

### 4. Cannot remove base type or interface
This rule should check that the base types and interfaces match on both the implementation and in the contract.

### 5. Cannot seal type
This rule checks if the type is non inheritable on both the implementation and contract:
1. Has the sealed modifier
2. Has no non static visible constructors outside the assembly.

### 6. Enum type must match
Enum underlying type must match on both the implementation and contract.

### 7. Enum values must match
Enum values most match on both the implementation and contract.

### 8. Interface should have same members
Interface members must match on both the implementation and contract.

We should have a rule setting that users can set to support allowing adding a new member to the interface if the member includes a default implementation. Adding a member to an interface on new releases is a breaking change unless the member has a default implementation, this requires C# 8.0.

### 9. Members must exist
This rules checks that all the public members should exist on both the implementation and contract. This applies to any member that can be accessible within outside of the assembly, excluding interface members as those are handled in a separate rule.

1. This rules makes sure that the member exists on the type or any of it's base types.
2. Parameter types are equal.
3. Return types match.

### 10. Modifiers cannot change
This rule checks for differences in marshaling attributes like `in`, `out`, `ref`, `readonly` and custom modifiers like `const` and `volatile` in parameters and member return's type.

1. Return's type modifiers can be ref, readonly or none. 
2. Parameter modifiers can be in, out, ref and custom modifiers like const and volatile can exist.

### 11. Parameter names cannot change
This rule makes sure that constructor, property or method parameter names match.

### 12. Type cannot change classification
This rule makes sure that type's classification doesn't change. Type classification refers to, `class`, `struct`, `ref struct`, `readonly`, `interface` or `delegate`.

### 13. Nullable annotations should match
This rule should make sure that nullable annotations match in between the implementation and the contract.

Also, this rule should allow differences in nullable annotations that are not breaking, for example, relaxing a nullable reference type annotation on an in parameter (i.e `public string Foo(string a) -> public string Foo(string? a)`). However, this should be configurable via rule settings to disable this as in some cases where you are comparing a reference assembly vs it's implementation you want them to match in all cases.

### 14. Constructor makes base class instantiable.
Rule that checks if a public API is a constructor that makes a class instantiable, even though the base class is not instantiable. That API pattern is not allowed, because it causes protected members of the base class, which are not considered public APIs, to be exposed to subclasses of this class.

## Configurability

Given that we are proposing a Roslyn analyzer a dotnet tool and potentially an MSBuild Task, we should support a friendly format for all three possible scenarios to baseline warnings/errors, configure rules and severity of rules.

To baseline errors we could support:

1. All analyzer's disable mechanism when using the tool as an analyzer.
2. A txt file that lists the errors to baseline (maybe we could support this for the analyzer scenario as well).

Configuring rules involves settings like, ignoring certain attributes as describe in the attribute compat rule, rules number 8 in the document, etc. I think these could be part of the `.editorconfig` with configuration for rules' severity.

### MSBuild support

We should provide MSBuild first class configurability via properties so that costumers can just add a `PackageReference` and set properties on how they want to consume the tool (via MSBuild Task, Analyzer, etc), what the inputs are going to be, where the baseline errors file can be found, etc. This would make it very simple for customers to include in their libraries and configure without having to write complicated MSBuild targets to run the tool on an MSBuild process if that is their best way to hook it up into their build system.

With this we could extend support to make it easier for packages to run validation by automatically download previous version(s) of the package and then run compatibility checks vs the current version by just setting a few MSBuild properties.

## Diagnostics consistency

When running as an analyzer's we are going to provide compiler diagnostics, so when running outside of an analyzer context, we should provide diagnostics that are consistent on all scenarios so that baselining these diagnostics and understanding them is clear within an editor, console or msbuild process.

So something like:

```
error RS0017: <helpful error description that doesn't depend of a squiggle to tell what's going on>
```

## Open questions

1. Where should these tools live? Maybe [dotnet/roslyn-analyzers](https://github.com/dotnet/roslyn-analyzers)?
