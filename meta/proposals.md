# Proposals

This document explains what proposals are and why it's important that we spend
the time writing & sharing them. We also have a [template for proposals](template.md).

## What are proposals?

Developers produce code. Program managers (PM) produce proposals. It's an over
simplification and doesn't cover what each discipline does, but it's a good way
to think about the deliverables each discipline has:

* **Developers** are expected to produce working code. This includes defining
  and building an architecture, authoring unit tests and reviewing code changes.
  Most people have a good working understanding of that role.

* **Program managers** on the other hand is a role that is harder to define.
  They aren't managers in the traditional sense (meaning they don't have people
  reporting to them) and they are clearly not programmers (in the sense that
  they are writing code that is shipped). The job of a program manager is
  defining the product and driving the feature work to make the vision a
  reality. Program manager usually don't come up with the vision by themselves
  but instead define them as a result of requirements that they gathered from
  customers. In some cases that's done via face-to-face interaction, in other
  cases via market research. Program managers are engineers, but they don't
  engineer the product's implementation but rather the product's public face.

Most of you have heard the phrase "don't build what the customer asks for, build
what the customer needs". The job of a program manager is to do the translation
from "here is a problem a customer has" to "here is the set of features our
product needs in order to make our customers successful". Proposals are the
output of such a translation and capture the identified problems and how the
feature (or set of features) will allow the customer to solve them.

Of course, this doesn't mean that only PMs are allowed to write proposals;
anyone inside or outside of Microsoft is welcome to submit them. But as it is
with code: we have a bar for what constitutes a good proposal and whether we're
willing to accept it.

We do have a [template for proposals](template.md).

## Why do we need proposals?

There seems to be a general trend in our industry to move away from rigid rules
and processes in order to be flexible and adaptive. For many people that seems
to indicate that any form of written product definition that isn't source code
is a waste of time and instead should be spent on coding.

But reduction of process and paper work isn't a goal in and of itself. The goal
is to generate customer value faster in order to adapt to an ever faster
changing world. There is no point in doing process for the sake of process,
that's overhead. No customer ever said "I love your product because your
engineering process is so pristine." But equally, we need to avoid wasting time
because we built the feature the wrong way or -- worse -- the wrong feature.

We need to appreciate that .NET isn't a simple web site. Microsoft alone has a
large offering surrounding .NET development, which includes runtimes,
programming languages, IDEs, build tools, and cloud services. Features in any of
these components do not exist in a vacuum and often have to interact with other
features in other components to solve a specific end-to-end customer scenario.
Since .NET was open sourced and added cross-platform support, the ecosystem has
become even bigger and our customers expect integration well beyond what is
provided by Microsoft. This includes both, open source projects (such as
JSON.NET and xUnit) but also includes commercial offerings (such as Rider and
RedHat). This makes proposals we can share across a larger set of parties even
more important.

In order for the engineering team to be efficient, it's vital that they can
focus. A good PM will help them by avoiding last minute design changes by
identifying the requirements early. A great PM will also reach out to other
teams owning different components in a given end-to-end scenario and help align
the features across that scenario.

All of this is much easier if there is a written form of a proposal:

* **Writing is structured thinking**. We all had the experience that a problem
  looks simple and the solution obvious -- until you're actually forcing
  yourself to go through the motions of applying the solution. Problems also
  rarely come in singletons, there is often a problem class that results in many
  different, but related problems. Identifying the set helps shaping a versatile
  feature set instead of a bunch of one-offs.

* **Peripheral vision**. As PMs we're only as effective as we have visibility
  into what we need to achieve as an organization. Peripheral vision, which is
  the ability to see what other people are working on, is vital to allow PMs to
  identify overlap and conflicting requirements early. If enough PMs share their
  proposals in a central place, it's much easier to develop that vision simply
  by reading the titles and abstracts.

* **Customer feedback**. We all want to see our features in customer hands as
  early as possible. Proposals are a first draft of the feature and shows a
  glimpse of what *could be*. Since .NET is now open source, we can also use the
  proposals to ask for customer feedback *before the first line of code is even
  written*. In fact, that's the sole purpose of the `dotnet/design` repository
  in the first place.

In short, writing proposals can help us in being more efficient and more
effective with the resources we have. It might be counter intuitive, but it's a
tool that helps us to be more agile.