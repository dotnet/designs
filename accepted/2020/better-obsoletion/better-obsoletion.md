# Better Obsoletion

**Owner** [Immo Landwerth](https://github.com/terrajobst) |
**Work Items** [Framework](https://github.com/dotnet/runtime/issues/33089) |
[Roslyn](https://github.com/dotnet/roslyn/issues/42119) |
[F#](https://github.com/dotnet/fsharp/issues/8654) |
[IDE](https://github.com/dotnet/roslyn/issues/42103)

No framework, no matter how carefully crafted, is immune to making mistakes. For
a framework to last the test of time it's vital that it can be evolved to enable
developers to build the kind of applications that matter to their business,
which is an ever-changing landscape and often puts additional constraints on the
developer's code that didn't exist before.

The approach to productivity in .NET is called "the pit of success", which is
the idea that developers will stumble their way via code completion towards the
general vicinity of a correct solution. However, as the platform grows and ages,
the number of mistakes in the platform also grows. And often the API that looks
the most promising might be the wrong one to use. So, it becomes important that
we can point developers the right way.

Since .NET Framework 1.0, we were able to mark APIs as obsoleted via a built-in
attribute. Any usage of such an API results in the compiler emitting a
diagnostic telling the developer that the API is obsoleted, which can include a
small piece of text pointing to an alternative.

This obsoletion approach has a few benefits over the alternatives:

* **API Removal**. Removing APIs is a very disrupting event. Some argue that
  SemVer solves this problem but it doesn't work well for platforms as it ties
  unrelated decisions together (e.g. in order to get access to new APIs, an
  unrelated area in the code needs to be rewritten to avoid using the removed
  APIs). Other technologies, such as C++, can use the preprocessor to remove
  features unless the customer explicitly opts-in to legacy behavior. This
  doesn't work well for .NET as this ecosystem is built around the reuse of
  binaries, not source code.

* **Documentation**. Let's face it, developers only read documentation when
  something doesn't work. Only few developers research guidance and best
  practice proactively. But even if they did: guidance changes over time and
  some technologies that looked like clever ideas turn out to be unviable. We
  need a mechanism that informs developers after the fact.

Obsoletion via the `[Obsolete]` attribute works well when:

1. Only a few APIs are affected
2. It's easy to remove their usage
3. Guidance can be provided as a short one-line string

In practice these requirements are often not satisfiable:

* **Technologies have large API surfaces**. Obsoleting an entire technology
  means that we'll have to annotate all entry points to that technology, which
  often means obsoleting a *set of APIs*, such as types and static members.

* **Not using obsoleted APIs isn't always practical**. It would be nice if we
  could always remove dependencies on obsoleted technologies, but this isn't
  always possible if they are used heavily in existing code bases. In those
  cases, you typically want a way to ignore obsoletion diagnostics regarding
  specific technologies but not others so that you don't accidentally start
  depending on obsoleted technologies when you're evolving your code base.

* **Guidance is complex**. Obsoleted APIs rarely have a drop-in replacement.
  After all, we don't obsolete based on their naming or shape but usually due to
  fundamental design issues. That means the replacements are typically a nuanced
  choice and require at least a page of explanation, rather than a one-liner.

This document proposes changes to how APIs can be obsoleted in a more structured
way.

## Scenarios and User Experience

### Targeted suppressions

Abigail is a developer and maintains the business logic layer component for
Fabrikam Billing Services. It was written in 2004 and makes heavy use of
non-generic collections by inheriting from them. Refactoring the code base to
use generic collections would be possible but a ton of work of dubious value as
the code is well-tested. Last year, the code was ported to .NET Core 2.1.

The component is designed for a traditional two-tier environment and includes
data access that handles retrieving and storing business objects from and to SQL
Server. Abigail is now tasked to refactor that code to use REST-based services
instead so that the application can eventually be converted to a cloud-based
offering.

Abigail would like to use the brand new JSON APIs, so she updates the project
from .NET Core 2.1 to .NET Core 3.1. However, after upgrading she notices that
we obsoleted non-generic collections and she gets a bunch of warnings against
her code:

| Code    | Description             | Project                        |
|---------|-------------------------|--------------------------------|
| BCL0006 | 'ArrayList' is obsolete | Fabrikam.Billing.BusinessLogic |
| BCL0006 | 'Hashtable' is obsolete | Fabrikam.Billing.BusinessLogic |

Due to the heavy use, there are dozens of warnings like this throughout her
code, so she doesn't want to use `#pragma warning disable` for each affected
call site. Instead, she decides to suppress all obsoletion warnings for
non-generic collections by adding the ID `BCL0006` to the
`Fabrikam.Billing.BusinessLogic` project's `NoWarn` list.

Now the build produces zero warnings and she starts to add the networking
component. After spending a few minutes online, she finds a code sample from
Stack Overflow that looks promising. After copy & pasting that into the code
editor, she immediately gets another obsoletion warning:

| Code    | Description                                     | Project                        |
|---------|-------------------------------------------------|--------------------------------|
| BCL0003 | 'HttpWebRequest' is obsolete. Use 'HttpClient'. | Fabrikam.Billing.BusinessLogic |

Glad to have learned that, she continues to build her networking layer with the
recommendation of `HttpClient`.

### More details

Bill is an architect and maintains the authentication and authorization related
components in Fabrikam Billing. Based on guidance from 2005, the code uses
`SecureString`.

Bill is now tasked with refactoring this code to support Azure Active Directory
based authentication. Since most of the code base is now being upgraded to .NET
Core 3.1, he starts by retargeting the project. After doing so, he gets a bunch
of obsoletion warnings:

| Code    | Description                | Project               |
|---------|----------------------------|-----------------------|
| BCL0001 | 'SecureString' is obsolete | Fabrikam.Billing.Auth |

Trying to understand why, he clicks the warning ID `BCL0001` in the error list
which takes him to a documentation page explaining the issues with
`SecureString`. There he learns that this type doesn't work well for
cross-platform scenarios due to the lack of automatic encryption facilities and
the recommendation is to stop using it altogether. Bill knows that moving to the
cloud also means they are considering moving to Linux, so he takes note that
they also need to plan for removing their dependency on `SecureString`.

## Requirements

### Goals

* **Global suppressions are specific**. Developers must be able to suppress
  obsoletion diagnostics for all APIs that relate to the same technology, rather
  than having to turn off all obsoletion diagnostics or having to resort to
  suppressing all call sites individually.

* **Documentation pages are easily accessible**. Developers must be able to go
  from the diagnostic in the error list straight to the documentation that
  provides the details.

### Non-Goals

* **Detecting specific usage patterns**. Sometimes, an API isn't obsoleted, just
  a combination of arguments or specific values. If we need to identify these,
  we'll author a custom analyzer.

* **Being able to obsolete APIs in already shipped versions of .NET**. The
  attribute must be applied to the API surface that the compiler sees and thus
  must be present in the reference assemblies. We can only obsolete in new
  versions of the platform. In other words, this doesn't support a sidecar file
  that can be applied to an existing version of .NET.

* **Being able to remove APIs in later versions**. See [the
  discussion](#why-is-being-able-to-delete-apis-not-a-goal) in the Q&A section.

* **Hiding all obsoleted APIs from IntelliSense**. Whether or not an API should
  be hidden is an independent decision. For more details, see [the
  discussion](#should-obsoleted-members-be-hidden-from-intellisense) in the Q&A
  section.

## Design

The design proposal piggybacks on the existing `ObsoleteAttribute` that is
already supported by all .NET compilers. The idea is to expand this a little to
support the desired scenarios. Alternatives are listed afterwards.

### API Changes

We'd add the following properties to `ObsoleteAttribute`:

```C#
namespace System
{
    public partial class ObsoleteAttribute : Attribute
    {
        // Existing:
        //
        // public ObsoleteAttribute();
        // public ObsoleteAttribute(string message);
        // public ObsoleteAttribute(string message, bool error);
        // public bool IsError { get; }
        // public string Message { get; }

        // New:
        public string DiagnosticId { get; set; }
        public string UrlFormat { get; set; }
    }
}
```

* `DiagnosticId`. Represents the ID the compiler will use when reporting a use
  of the API. This relies on diagnostic IDs being unique, just like the rest of
  the Roslyn analyzer facility. See [this
  discussion](#what-happens-when-multiple-parties-accidentally-use-the-same-diagnostic-id)
  on how we're planning to address the potential for conflicts.

* `UrlFormat`. The URL that should be used by an IDE for navigating to
  corresponding documentation. Instead of taking the URL directly, the API takes
  a format string. This allows having a generic URL that includes the diagnostic
  ID. This avoids having to repeat the ID twice and thus making a copy & paste
  mistake.

### Compiler/IDE Changes

The compilers would be enlightened about these new properties and use them as
follows:

* If `DiagnosticId` is `null`, use the existing diagnostic ID (e.g. `CS0618` in
  C#). Otherwise, use `DiagnosticId`.
* The compiler should honor suppressions for the specified diagnostic id.
* When the user suppresses the existing generic diagnostic ID for obsoletion
  (e.g. `CS0618` in C#), the compiler should not suppress any obsoletions with
  an explicitly set diagnostic ID (unless that explicit ID happens to match the
  generic diagnostic, but that would be bad practice from the API author).
  Instead, the developer has to add a suppression for the specified diagnostic
  ID. As such, adding a diagnostic is the same as changing a diagnostic ID and
  thus a source breaking change.
* The compiler doesn't report diagnostics for use of obsoleted APIs when the use
  site itself is marked obsolete. On the one hand, it seems logical to amend
  this behavior to only skip the reporting if the diagnostic ID of the use site
  and the declaration site are the same. On the other hand, that's probably
  overkill because a developer who marked a type or method as obsoleted makes it
  clear that the code is only provided for backwards compatibility. Having to
  keep adding new suppressions for other obsoleted APIs seems unhelpful.
* If `UrlFormat` is not `null`, use it as the diagnostic link when rendering
  them in the IDE.
* The compiler should assume that `UrlFormat` and `DiagnosticId` are independent
  features, in other words both can be used, either, or neither.
* If `UrlFormat` is provided without a `DiagnosticId` the compiler will use the
  `DiagnosticId` that it uses by default as the format argument to `UrlFormat`.
  For C# this could be `CS0618`, for example, but will vary depending on context.
  The internal error codes don't consider leading zeroes, but
  the padding is important when we actually do the format substitution.
* It's legal for `UrlFormat` to not contain a `{0}` placeholder, in which case
  the provided `UrlFormat` is treated as the final URL. If more than one format
  specifier is included, the compiler will ignore `UrlFormat` entirely.

### Samples

Here is how the attribute would be applied:

```C#
internal static class BclObsolete
{
    public const string Url = "https://aka.ms/obsolete/{0}";

    // NOTE: Keep this sorted by the the ID's value,
    //       not the name of the field!
    public const string SecureStringId = "BCL0001";
    public const string NonGenericCollectionsId = "BCL0006";
}

namespace System.Collections
{
    [Obsolete(DiagnosticId = BclObsolete.NonGenericCollectionsId,
              UrlFormat = BclObsolete.Url)]
    public class ArrayList
    {
        // ...
    }

    [Obsolete(DiagnosticId = BclObsolete.NonGenericCollectionsId,
              UrlFormat = BclObsolete.Url)]
    public class Hashtable
    {
        // ...
    }
}

namespace System.Security
{
    [Obsolete(DiagnosticId = BclObsolete.SecureStringId,
              UrlFormat = BclObsolete.Url)]
    public sealed class SecureString
    {
        // ...
    }
}
```

_**OPEN ISSUE**: We need to decide on guidance to avoid ID conflicts with the
community. We should talk to the Roslyn analyzer team for best practices. Stake
in the ground could be that we should use a prefix like `BCL` that is owned by a
single party and responsible to avoid duplicated numbers. Other parties would
need to choose a different prefix._

### XML Serializer

According to [@mconnew](https://github.com/mconnew), the XML Serializer throws
an exception when serializing types that are marked as `[Obsolete]`. There seems
to be two options to avoid breaking customers:

1. We carefully only apply the `[Obsolete]` attribute to reference assemblies
   and don't apply this on the implementation assembly. That's not ideal, but
   doable.
2. We change the XML serializer to not throw.

_**OPEN ISSUE**: Close the loop with XML Serializer team and lock down what we
should do._

### Rejected Alternatives

#### String Encoding

Instead of introducing new APIs, we could invent a textual format that we put in
the attribute's message property, such as:

```C#
namespace System.Collections
{
    [Obsolete("::BCL0006|https://aka.ms/obsolete/{0}")]
    public class ArrayList
    {
        // ...
    }
}
```

The benefit is that it doesn't require new API and that that the call sites are
slightly more compact.

The downside is more cryptic use and weak contract between the compiler and the
API surface, which might also result in accidental recognition.

#### Separate Attribute

We could also introduce a new attribute and move detection to an analyzer that
is referenced by default. The shape could look as follows:

```C#
namespace System
{
    public sealed class DeprecatedAttribute : Attribute
    {
        public DeprecatedAttribute(string diagnosticId);
        public DeprecatedAttribute(string diagnosticId, string urlFormat);

        public string DiagnosticId { get; set; }
        public string UrlFormat { get; set; }
    }
}
```

The irony is that this would effectively mean that we're obsoleting the
`ObsoleteAttribute` which would then raise the question whether we should mark
it `[Obsolete]` or `[Deprecated]`.

## Q & A

### Why is providing coder fixers not a goal?

Code fixers are actions in Roslyn's lightbulb menu that changes the code. It
would sure be nice if we'd offer them for obsoleted APIs, but we don't believe
this is viable for the vast amount of obsoletions:

* **SecureString**. The fix is to not use passwords to begin with. Just making
  the `Password` property of type `string` isn't the right thing to do.

* **Non-generic collections**. One can't just replace the types (first, generic
  inference would be hard) but also, we'd have to update usage patterns, for
  example that `Hashtable` returns `null` for missing keys.

* **Remoting**. Requires moving to some other IPC technology. The code needs to
  be written very differently.

One could argue that even if the fixer could only get you 50% there, it's still
better than nothing but we're not convinced that's the case. Sure, a fixer isn't
necessarily obligated to introduce zero issues, but over promising by offering
the fixer could easily result in subtle bugs and thus customer frustration. We
should be honest here and don't claim it's easier than it is.

### Why is being able to delete APIs not a goal?

Different layers of the stack have drastically different requirements for
breaking changes. For the lower layers, our ability to make breaking changes and
removing obsoleted features has proven exceedingly difficult. This has to do
with the fact that .NET's success is largely built around the idea of sharing
libraries, usually in binary form. Making changes there can disrupt the
ecosystem for years. So, our hopes to be able to remove problematic tech here
seem slim.

At the same time, we've been able to remove APIs in higher layers, such as
ASP.NET. It's much easier there because dependent code there tends to live in
application code, as opposed to libraries, so absorbing breaks there is easier.

### If we can't remove an API, why obsolete it in the first place?

Obsoletion is a process that informs customers about problematic APIs. It's most
useful for first time users of the API so that they don't take dependencies on
less optimal technologies. However, it's also a powerful tool for informing
customers about potential issues they have in their code base. Neither of these
goals require removing the API eventually.

### Should obsoleted members be hidden from IntelliSense?

Not always. Using `[EditorBrowsable(EditorBrowsableState.Never)]` one can hide
APIs from IntelliSense. The rationale is that it makes it harder for new code to
take a dependency on it. While that's true, it makes it harder for existing code
that can't remove the dependency (yet). Consider Abigail's example above. If she
needs to evolve the existing code base hiding on all generic collections
wouldn't be helpful at all.

More specifically, this feature is about enabling developers to suppress
specific obsoletions but not others. We could consider expanding
`EditorBrowsable` by adding a diagnostic ID to it which determines would disable
the hiding if the specific ID is suppressed (in other words, we'd link their
behaviors). But that feels a bit over the top.

### What happens when multiple parties accidentally use the same diagnostic ID?

The feature needs to handle that multiple APIs use the same diagnostic ID (in
fact, that's the feature so that related obsoletions can be grouped). If
different parties inadvertently use the same diagnostic ID it just means that
customers have no way to suppress them independently via NoWarn. They can still
use `#pragma` suppressions if they want to.

For 1st parties, the Roslyn team already tracks [diagnostic ranges in
GitHub](https://github.com/dotnet/roslyn-analyzers/blob/master/src/Utilities/Compiler/DiagnosticCategoryAndIdRanges.txt).
We should give clear guidance to developers to pick a prefix that they own. The
Roslyn team [might also want to reserve
prefixes](https://github.com/dotnet/roslyn/issues/40351).
