# Better Obsoletion

**PM** [Immo Landwerth](https://github.com/terrajobst)

No framework, no matter how carefully crafted, is immune to making mistakes. For
a framework to last the test of time it's vital that it can be evolved to enable
developers to build the kind of applications that matter to their business,
which is an ever changing landscape and often puts additional constraints on the
developer's code that didn't exist before.

The approach to productivity in .NET is called "the pit of success", which is
the idea that developers will more less stumble their way via code completion
towards the general vicinity of a correct solution. However, as the platform
grows and ages, the amount of mistakes in the platform also grows. And more
often than not the API that looks the most promising might be the wrong one to
use. So it becomes important that we can point developers the right way.

Since .NET Framework 1.0, we were able to mark APIs as obsoleted via a built-in
attribute. Any usage of such an API results in the compiler emitting a warning
telling the developer that the API is obsoleted, which can include a small piece
of text pointing to an alternative.

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
  something doesn't work. Only few developers research guidance and
  best-practice proactively. But even if they did: guidance changes over time
  and some technologies that looked like good ideas turn out to be unviable. We
  need a mechanism that informs developers after the fact.

Obsoletion via the `[Obsolete]` attribute works pretty well when: only a few
APIs are affected, it's easy to remove their usage, and guidance can be provided
as a short one-line string.

In practice these requirements are often not satisfiable:

* **Technologies have large API surfaces**. Obsoleting an entire technology
  means that we'll have to annotate all entry points to that technology, which
  often means obsoleting a *set of APIs*, such as types and static members.

* **Removing obsoleted APIs isn't always practical**. It would be nice if we
  could always remove dependencies on obsoleted technologies but this isn't
  always possible if they are used heavily in existing code bases. In those
  cases, you typically want a way to ignore obsoletion warnings regarding
  specific technologies but not others so that you don't accidentally start
  depending on obsoleted technologies when you're evolving your code base.

* **Guidance is complex**. Obsoleted APIs rarely have a drop-in replacement.
  After all, we don't obsolete based on their naming or shape but usually due to
  fundamental design issues. That means the replacements are typically a nuanced
  choice and require at least a page of explanation, rather than a one-liner. 

This document proposes changes to how APIs can be obsoleted in a more structured
way.

## Scenarios and User Experience

### Targeted Suppression

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

Abigail would like to use the brand new JSON APIs so she updates the project
from .NET Core 2.1 to .NET Core 3.1. However, after upgrading the notices that
we obsoleted non-generic collections and she gets a bunch of warnings against
her code:

| Code   | Description              | Project                        |
|--------|--------------------------|--------------------------------|
| DE0006 | 'ArrayList' is obsolete  | Fabrikam.Billing.BusinessLogic |
| DE0006 | 'Dictionary' is obsolete | Fabrikam.Billing.BusinessLogic |

Due to the heavy use, there are dozens of warnings like this throughout her
code, so she doesn't want to use `#pragma warning disable` for each affected
call site. Instead, she decides to suppress all obsoletion warnings for
non-generic collections by adding the ID `DE0006` to the
`Fabrikam.Billing.BusinessLogic` project's `NoWarn` list.

Now the build produces zero warnings and she starts to add the networking
component. After spending a few minutes online she finds a code sample from
Stack Overflow that looks promising. After copy & pasting that into the code
editor, she immediately gets another obsoletion warning:

| Code   | Description                                     | Project                        |
|--------|-------------------------------------------------|--------------------------------|
| DE0003 | 'HttpWebRequest' is obsolete. Use 'HttpClient'. | Fabrikam.Billing.BusinessLogic |

Glad to have learned that, she continues to build her networking layer with the
recommendation of `HttpClient`.

## More Details

Bill is an architect and maintains the authentication and authorization related
components in Fabrikam Billing. Based on guidance from 2005, the code uses
`SecureString`.

Bill is now tasked with refactoring this code to support Azure Active Directory
based authentication. Since most of the code base is now being upgraded to .NET
Core 3.1, he starts by retargeting the project. After doing so, he gets a bunch
of obsoletion warnings:

| Code   | Description                 | Project               |
|--------|-----------------------------|-----------------------|
| DE0001 | 'SecureString' is obsolete  | Fabrikam.Billing.Auth |

Trying to understand why, he clicks the warning ID `DE0001` in the error list
which takes him to a documentation page explaining the issues with
`SecureString`. There he learns that this type doesn't work well for
cross-platform scenarios due to the lack of automatic encryption facilities and
the recommendation is to stop using it altogether. Bill knows that moving to the
cloud also means they are considering moving to Linux, so he takes note that
they also need to plan for removing their dependency on `SecureString`.

## Requirements

### Goals

* **Global suppressions are specific**. Developers must be able to suppress
  obsoletion warnings for all obsoletion warning that relate to the same
  technology, rather than having to turn all obsoletion warnings off or having
  to resort to suppress every single call site individually.

* **Documentation pages are easily accessible**. Developers must be able to go
  from the warning/error in the error list straight to the documentation that
  provides the details.

### Non-Goals

* **Detecting specific usage patterns**. Sometimes, an API isn't obsoleted, just
  a particular combination of arguments or specific values. If we need to
  identify these, we'll author a custom analyzer.

* **Being able to obsolete APIs in already shipped versions of .NET**. The
  attribute must be applied to the API surface that the compiler sees and thus
  must be present in the reference assemblies. We can only obsolete in new
  versions of the platform. In other words, this doesn't support a side car file
  that can be applied to an existing version of .NET.

## Design

The design proposal piggy backs on the existing `ObsoleteAttribute` that is
already supported by all .NET compiler. The idea is to expand this a little to
support the desired scenarios. Alternatives are listed afterwards.

### API Changes

We'd add the following properties to `ObsoleteAttribute`:

```C#
namespace System
{
    public sealed class ObsoleteAttribute : Attribute
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
        public string Url => UrlFormat == null ? null : string.Format(UrlFormat, DiagnosticId);
    }
}
```

Instead of taking the URL directly, the API takes a format string. This allows
having a generic URL that includes the diagnostic ID. This avoids having to
repeat the ID twice and thus making a copy & paste mistake.

### Compiler Changes

The compilers would be enlightened about these new properties and use them as
follows:

* If `DiagnosticId` is `null`, use the existing diagnostic ID (e.g. `CS618` in
  C#). Otherwise, use the specified warning ID.
* If `Url` is not `null`, use it as the diagnostic link when rendering them in
  the IDE.

### Samples

Here is how the attribute would be applied:

```C#
namespace System.Collections
{
    [Obsolete(DiagnosticId = "DE0006", UrlFormat="http://aka.ms/obsolete/{0}")]
    public class ArrayList
    {
        // ...
    }

    [Obsolete(DiagnosticId = "DE0006", UrlFormat="http://aka.ms/obsolete/{0}")]
    public class Hashtable
    {
        // ...
    }
}

namespace System.Security
{
    [Obsolete(DiagnosticId = "DE0001", UrlFormat="http://aka.ms/obsolete/{0}")]
    public sealed class SecureString
    {
        // ...
    }
}
```

### Possible Alternatives

#### String Encoding

Instead of introducing new APIs, we could invent a textual format that we put
in the attribute's message property, such as:

```C#
namespace System.Collections
{
    [Obsolete("::DE0006|http://aka.ms/obsolete/{0}")]
    public class ArrayList
    {
        // ...
    }
}
```

The benefit is that it doesn't require new API and that that the call sites
are slightly more compact.

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
        public string Url => UrlFormat == null ? null : string.Format(UrlFormat, DiagnosticId);
    }
}
```

The irony is that this would effectively mean that we're obsoleting the
`ObsoleteAttribute` which now begs the question whether we should mark it
`[Obsolete]` or `[Deprecated]`.