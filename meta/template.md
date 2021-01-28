# Proposal Template

This document contains the template for a proposal. The goal isn't to enforce
uniformness but to make sure that we use similar wording and structure so that
readers can be efficient when reading multiple proposals, written by different
people. Try to understand what a particular section intents to cover and deviate
when it's sensible. But avoid deviation just because you aren't willing to spend
time with the template -- the goal is to optimize for the reader.

If you don't know what proposals are and why you should care in the first place,
check out [this document](proposals.md).

## Examples

* [Windows Compatibility Pack](../accepted/2018/compat-pack/compat-pack.md)
* [Package Information in API Reference](../accepted/2018/package-doc/package-doc.md)

## Template with Instructions

Here is the template with placeholder text that also explains what the sections
should contain:

```Markdown
# Your Feature

<!--
Provide the primary contacts here. Linking to the GitHub profiles is useful
because it allows tagging folks on GitHub and discover alternative modes of
communication, such as email or Twitter, if the person chooses to disclose that
information.

The bolded roles makes it easier for people to understand who the driver of the
proposal is (PM) and who can be asked for technical questions (Dev). At
Microsoft, these roles happen to match to job titles too, but that's irrelevant.
-->

**PM** [John Doe](https://github.com/johndoe) |
**Dev** [Jane Doe](https://github.com/janedoe)

<!--
Provide a broad problem statement here. This might include some history and the
description of the current state of the product. The goal is to give the reader
the ability to judge how (and how well) your feature will address the problem.
It might also give rise to tweaks or even alternative solutions that could move
your proposal in a different direction -- and that's a good thing. After all, if
the direction needs to change it's best identified when your proposal is still
being reviewed as opposed to after much of it has already been implemented. So
it's in your best interest to ensure the reader has enough context to properly
critique your idea.

Your problem statement should be followed by the solution you're proposing to
solve it. Don't describe specific user scenarios here but provide enough
information so that the reader can get an overview of what you have in mind.
It's very much desirable to make the readers curious and come up with questions.
That puts them in the right state of mind to read the following sections
actively.

Ensure your first paragraph is a short summary of the problem and the proposed
solution so that readers can gain a quick understanding and decide whether your
proposal is relevant to them and thus worth spending time on. It's usually best
to write the first paragraph last because that means you already know the punch
line and can do a better job stating it succinctly.
-->

## Scenarios and User Experience

<!--
Provide examples of how a user would use your feature. Pick typical scenarios
first and more advanced scenarios later.

Ensure to include the "happy path" which covers what you expect will satisfy the
vast majority of your customer's needs. Then, go into more details and allow
covering more advanced scenarios. Well designed features will have a progressive
curve, meaning the effort is proportional to how advanced the scenario is. By
listing easy things first and more advanced scenarios later, you allow your
readers to follow this curve. That makes it easier to judge whether your feature
has the right balance.

Make sure your scenarios are written in such a way that they cover sensible end-
to-end scenarios for the customer. Often, your feature will only cover one
aspect of an end-to-end scenario, but your description should lead up to your
feature and (if it's not the end result) mention what the next steps are. This
allows readers to understand the larger picture and how your feature fits in.

If you design APIs or command line tools, ensure to include some sample code on
how your feature will be invoked. If you design UI, ensure to include some
mock-ups. Do not strive for completeness here -- the goal of this section isn't
to provide a specification but to give readers an impression of your feature and
the look & feel of it. Less is more.
-->

## Requirements

### Goals

<!--
Provide a bullet point list of aspects that your feature has to satisfy. This
includes functional and non-functional requirements. The goal is to define what
your feature has to deliver to be considered correct.

You should avoid splitting this into various product stages (like MVP, crawl,
walk, run) because that usually indicates that your proposal tries to cover too
much detail. Keep it high-level, but try to paint a picture of what done looks
like. The design section can establish an execution order.
-->

### Non-Goals

<!--
Provide a bullet point list of aspects that your feature does not need to do.
The goal of this section is to cover problems that people might think you're
trying to solve but deliberately would like to scope out. You'll likely add
bullets to this section based on early feedback and reviews where requirements
are brought that you need to scope out.
-->

## Stakeholders and Reviewers

<!--
We noticed that even in the cases where we have specs, we sometimes surprise key
stakeholders because we didn't pro-actively involve them in the initial reviews
and early design process.

Please take a moment and add a bullet point list of teams and individuals you
think should be involved in the design process and ensure they are involved
(which might mean being tagged on GitHub issues, invited to meetings, or sent
early drafts).
-->

## Design

<!--
This section will likely have various subheadings. The structure is completely
up to you and your engineering team. It doesn't need to be complete; the goal is
to provide enough information so that the engineering team can build the
feature.

If you're building an API, you should include the API surface, for example
assembly names, type names, method signatures etc. If you're building command
line tools, you likely want to list all commands and options. If you're building
UI, you likely want to show the screens and intended flow.

In many cases embedding the information here might not be viable because the
document format isn't text (for instance, because it's an Excel document or in a
PowerPoint deck). Add links here. Ideally, those documents live next to this
document.
-->

## Q & A

<!--
Features evolve and decisions are being made along the road. Add the question
as a subheading and provide the explanation for the decision below. This way,
you can easily link to specific questions.

When you find yourself having to explain something in a GitHub discussion or in
email, consider to update your proposal and link to your answer instead. This
way, you avoid having to explain the same thing over and over again.
-->

```

## Blank Template

If you're a minimalist, just copy and paste the code below:

```Markdown
# Your Feature

**PM** [John Doe](https://github.com/johndoe) |
**Dev** [Jane Doe](https://github.com/janedoe)

## Scenarios and User Experience

## Requirements

### Goals

### Non-Goals

## Stakeholders and Reviewers

## Design

## Q & A
```
