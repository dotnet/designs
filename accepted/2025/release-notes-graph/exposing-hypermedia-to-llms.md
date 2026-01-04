# Exposing Hypermedia Information Graphs to LLMs

[Hypermedia](https://en.wikipedia.org/wiki/Hypermedia) and [hypertext](https://en.wikipedia.org/wiki/Hypertext) are decades-old ideas and formats that are perfectly-suited for LLM consumption by virtue of self-describing structure and relationships between resources. The premise is that there is sufficient meta-information in a hypermedia document graph for a semantic consumer to successfully traverse it to find the information demanded by a  prompt. The prevailing narrative over the last few decades has been that _structured data_ > _unstructured documents_, in terms of inherent capacity to derive meaningful insight. JSON and XML came out of that heritage, with [JSONPath](https://en.wikipedia.org/wiki/JSONPath) and [XPath](https://en.wikipedia.org/wiki/XPath) providing structured query supported by a priori schema knowledge. [Hypermedia as the engine of application state (HATEOAS)](https://en.wikipedia.org/wiki/HATEOAS) contributes the idea that data labeling can be extended to relations across resources. These approaches are integrated in this system to enable an LLM to search for and discover desired information across labeled nodes and edges within a graph. In a more traditional system, a schema is the pre-requisite to traversal, whereas in a semantic system, it is traversal that reveals the schema.

> A (nearly) century-old principle, articulated by [Korzybski](https://en.wikipedia.org/wiki/Alfred_Korzybski): [the map is not the territory](https://en.wikipedia.org/wiki/Map%E2%80%93territory_relation).

In trail races, there are frequent ribbons hanging from trees and painted arrows on the ground to ensure the correct path taken. It is often the case that there are races of multiple distances being run on an overlapping course. At key intersections, there are signs that say "5 km -> left" and "10 km -> continue straight". The ribbons and the painted arrows are the kind of map that a document schema provides, ensuring correct coloring within the lines. The signposting where two courses diverge is the HATEOAS-like descriptive insight that enables graph traversal. The signposting is a key-value function. You match a key you recognize with a value you need to know. Signposting enables directed navigation of the territory.

This approach is in sharp contrast to the typical HTML graph implementation: `For a deeper discussion on this topic, <a href="another-document.html">click here</a>.`. A semantic graph might expose a named link relation like `{ "link-relation": "gardening-deep-dive", "href": "..." }`. It might offer more descriptive complexity by separating the link parts, like `"link-relation": "deep-dive"` and `"target-kind": "gardening"`, cleanly splitting the link-adjective and its target-noun. The better the semantic implementation, the less inference, flow analysis, or attention is required to derive the intended meaning.

Databases went through a "no-sql" transition. That wasn't a rejection of structure, but a realization that the source of structure is the documents. Hypermedia graphs extend this idea enabling "no-schema" consumption. Rather than requiring upfront schema knowledge, a fully realized semantic space including both content and link relations enables readers to discover structure through descriptive labels and traversal. A sort of "world schema" emerges from navigation.

Hypermedia information document graphs can be published pre-baked, making them suitable for direct consumption without being pre-loaded and exposed by a vector database. The semantic relationships and other meta-information are used as the basis of typical LLM mechanics like vector similarity, making hypermedia a kind of [Retrieval-Augmented Generation (RAG)](https://en.wikipedia.org/wiki/Retrieval-augmented_generation) scheme and suitable for static-webhost deployment.

The concept of a pre-baked static hypermedia graph has been applied to the .NET release notes. That project was initially approached as a modern revamp of a set of JSON files that are used to inform and direct cloud-infra deployment and compliance workflows at scale. Over time, it became obvious that LLMs could read the same content directly and self-reason about its content and navigation patterns. Early experimentation proved that out. The primary techniques used to improve applicability for LLMs are semantic naming and graph-resident guidance. These techniques can be quite subtle, where a small shift in semantic bias can result in a large shift in LLM performance.

Graph-resident guidance consists of skills and workflows. HATEOAS tells us that `customer` can be a relation of a sales order. Why not make `graph-instructions` a relation of a graph? Skills and workflows are first-class relations in the graph, enabling its designer to express navigation intent. Skills follow the [Anthropic skills format](https://github.com/anthropics/skills), while workflows are HAL documents that describe queries over graph link relations. This enables the graph designer to provide readers with "ten-km-route-follow-path" style workflows if that's a match for the intended outcome.

The .NET release notes information graph uses [Hypertext Application Language (HAL)](https://en.wikipedia.org/wiki/Hypertext_Application_Language) as its hypermedia foundation. It augments HAL with a homegrown HAL workflow convention that looks just as native as `_links` or `_embedded`. LLMs grasp the intent, in part because HAL predates LLMs by over a decade. This approach enables low-cost LLM enablement for scenarios where hosting a persistent "AI server" would be prohibitive. This approach should scale just as well to a broad set of document types, not just release notes.

This document is structured theory-first. The theory describes and demonstrates the basis for the design. The practice follows, intended as a faithful application of the theory.

## Graph design point

The release notes information graph is based on the restrictive idea that the entrypoint of the graph should be skeletal and rarely changing. That's workable for LLMs but not ideal. The motivation for the restrictive approach is that it should support an n-9s level of reliability and be subject to rigorous engineering practices (git workflows, peer review, merge gates). However, we're in the early days of AI and subject to repeated waves of externally-driven change that may require quick re-evaluation and re-work (potentially breaking changes) of the entrypoint to maintain high-quality LLM enablement. These modalities are in firm opposition.

Instead, we can instead view the core graph as a **well-defined data-layer** that honors the desired reliability requirements, while exposing a separate **adaptable application-layer** entrypoint for LLMs that can evolve over time without the heavy compatibility burden.

The graph as a whole is based on a somewhat traditional schema design, utilizing both normalized and denormalized approaches in service of consumers. After the graph was realized, it became possible to test it with `jq` as a sort of passive and syntactic consumer and with LLMs as a much more active and semantic consumer. The graph was successively adapted to improve performance for both consumption styles. Performance is primarily measured in terms of terseness of query and quickness (fetches and data cost) of response. Much of the feedback could be considered fundamental in nature. The overall character of the graph remains a pure information-oriented data design, but with a significant tilt towards semantic consumers.

The choice of hypermedia as the grounding format is a case-in-point of the overall approach. Hypermedia long pre-dates LLMs, however, it has always held semantic consumers (humans) as a key design cohort. Hypermedia formats provide a conceptual framework that is easy to flavor towards semantic consumption. This flexibility proved useful as the design was adapted with LLM feedback. It should also be noted that LLM feedback is by far the cheapest and most accessible form of feedback.

A few behavioral patterns emerged from LLM eval:

- Consistent application of a conceptual model creates familiarity for semantic consumers. It is a comfort to find a concept exposed where it is expected.
- It is possible to expose links that jump from one part of the graph to another, like a wormhole. LLMs seem to need to develop **comprehension** _and_ **trust** as a pre-requisite for relying on them. The more attractive the wormhole link, the more the LLM may be skeptical. This was observed most with the `latest-security-disclosures` relation since it provides high value and because it has an inherent half-life. The meaning of the relation was _so well understood_ that LLMs repeatedly felt the need to double check the correctness of the link value.
- Resources can be dual-mapped in terms of structural kind, like `latest-security-month`, and desired output, like `latest-security-disclosures`. A given prompt may bias towards different concerns. Differentiated mappings are more likely to present a similar match to semantic consumers. One expects that this can be overdone. This technique was applied with a light touch in the graph.
- LLMs will acquire multiple resources in a single turn if a good strategy for doing so is evident.
- LLMs operate on a model of scarcity, with tokens at a premium. Smaller graph nodes encourage greater graph navigation by creating a sense that growing comprehension is outstripping consumption cost.
- Differentiating token cost by category of nodes makes it cheaper for LLMs to navigate a large graph. The `month` node within the graph is weightier than all other nodes making it easier to develop an exploration plan among other nodes before making a final decision on which month(s) to visit, if any.

## Performance considerations

Some questions can be answered from the LLM entrypoint, however, many require navigating to documents within the core graph. It is not feasible or desirable to include all information in a single document. As a result, a turn-by-turn approach is required. At each turn, there is new content, new insight, and then selection of critical information that directs the next fetch(es) or is the desired final answer. The range of required turns varies greatly, depending on the prompt and how the schema design happens to apply structure to the relevant information domain.

The [transformer architecture](https://en.wikipedia.org/wiki/Transformer_(deep_learning)) imposes a set of costs on any LLM use. The graph design has a direct impact on LLM performance and cost. Multiple turns accelerate costs quickly, much faster than intuition would suggest.

[API pricing](https://openai.com/api/pricing/) is listed in terms of 1M tokens. One million tokens may sound like a lot. Processing 1M tokens doesn't require the complete works of Shakespeare. Straightforward formulas can predict how quickly token counts grow and what that will cost dollar-wise. They demonstrate how little it takes to hit the million token milestone.

It was the quick pace of "dollar balance decay" in an API account that led to wanting to understand the underlying mechanics. This knowledge enabled designing the graph to produce both right answers and cheap answers. They are very much not the same thing.

### Cost model

There are three major cost functions at play:

- **Token cost:** The tokens processed at each turn, summed across turns. Each turn reprocesses all prior context plus new content.
- **Attention:** Each token attends to every other token within a turn, and this cost is incurred at every turn as context grows.
- **Context:** The accumulated tokens at the final turn. This is bounded by the model's context window. The last turn could equally be called "terminal turn" if there is a "context overflow".

Let's build intuition using uniform token counts: `n` tokens are added per turn across `m` turns.

| Turn | Tokens | Context | Cumulative Tokens | Attention | Cumulative Attention |
|------|--------|---------|-------------------|-----------|----------------|
| 1 | n | n | n | n² | n² |
| 2 | n | 2n | 3n | 4n² | 5n² |
| 3 | n | 3n | 6n | 9n² | 14n² |
| 4 | n | 4n | 10n | 16n² | 30n² |
| 5 | n | 5n | 15n | 25n² | 55n² |
| m | n | mn | nm(m+1)/2 | m²n² | n²m(m+1)(2m+1)/6 |

**Columns explained:**

- **Tokens**: New tokens fetched this turn
- **Context**: Size of context window this turn (= cumulative of Tokens)
- **Cumulative Tokens**: Running total of tokens processed across all turns (your API bill)
- **Attention**: Computational cost this turn, proportional to Context²
- **Cumulative Attention**: Running total of attention cost

The formulas simplify for large m:

| Measure | Formula | Growth class |
|---------|---------|--------------|
| Final context | mn | Linear in turns |
| Accumulated tokens | nm²/2 | Quadratic in turns |
| Accumulated attention | n²m³/3 | Cubic in turns |

More context on cost:

- API pricing is in term of tokens. For multi-turn conversations, the cost is the accumulated token cost not the final context size.
- The cubic growth in attention is the dominant computational cost, the primary contributor to latency and throughput. It emerges from summing quadratic costs across turns. Each turn pays attention on everything accumulated so far. This cost is likely the gating function on context size and expected to be persistent even if GPU memory doubles.
- These costs provide clues on why conversation compacting exists and why there is scrutiny on token economics.

### Batched vs sequential

What if all content could be fetched in a single turn?

| Approach | Total attention cost | Multiplier |
|----------|----------------------|------------|
| Batched (1 turn) | (nm)² = n²m² | 1 |
| Sequential (m turns) | n²m³/3 | m/3 |

The sequential penalty is approximately **m/3** compared to batched. Ten turns costs roughly 3× what a single batched turn would; thirty turns costs roughly 10×. This ratio scales linearly with turn count, the `m` term.

Many problems inherently require multiple turns. The LLM must reason about intermediate results before knowing what to fetch next. The goal is not to eliminate turns but to minimize them and optimize their structure.

### Optimization: lean early, heavy late

> Defer large token loads to later turns to reduce the number of turns that must pay the cost of large token loads.

The uniform model above assumes equal token counts per turn. In practice, token distribution across turns is a design choice with significant cost implications. The tokens in the first turns are by far the most costly. This is roughly similar to credit card debt, where charges from the first month can penalize purchasing power with crushing compound interest. If the initial purchase was large, you may be in trouble.

### Optimization: multiple fetches per turn

> Prefer larger token loads per turn to reduce the number of turns overall.

The sequential model assumes one fetch per turn. LLMs can fetch multiple documents in a single turn when aided by intuition or given clear guidance about what to retrieve. This technique tames the rate at which token cost and attention cost accumulates, enabling a cost profile that approaches _batched_ while maintaining a _sequential_ model.

This approach can (to a degree) amortize network costs across multiple async requests.

This optimization may seem in conflict with the earlier optimization, but it isn't. The earlier optimization is about the order of fetches across turns, whereas this optimization is about collapsing turns. They are complementary ideas with no tension.

## LLM entrypoints

We'll now look at the LLM enablement design and start to see how the cost functions influence the approach.

There are two entrypoints provided for LLMs:

- [llms.txt](https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/llms.txt) -- Prose explanation of how to use the graph, including a link to `llms.json`.
- [llms.json](https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/llms.json) -- The LLM index (AKA "application-layer entrypoint for LLMs"). It also includes guidance inline.

### Design process

These formats are the result of many iterations and experiments. Early revisions of `llms.txt` attempted to explain everything in great detail, nearing 500 lines. The current lean approach was arrived at via iterative LLM feedback and observation.

Sidebar: A key design principle emerged from this process: _curiosity-driven evaluation_ > _intuition reliance_. Once you have a good test harness and methodology, it's quite liberating to not trust your intuition but to test any ideas that occur as interesting. Any bias or distinction between "intuitive and informed direction" and "bat-crazy idea" drops away. Test both. All of this stuff is half-magic, half-fever-dream. Why should I expect my intuition on how LLMs behave to be all that authoritative anyway?

LLM performance was observed to increase as content size drops and becomes more focused on addressing specific critical (has to be right) and/or problematic (typically wrong) navigation patterns. The intuitive nature of the graph is a kind of floor raiser that removed the need for expansive guidance. The cross-over point where intuition begins to underperform could only be arrived at via testing.

An opposing trend is that LLMs tend to complain if guidance is overly biased to specific problem scenarios. They appear happy when they find even meager guidance targeting the scenario that they are tasked with. In the end, the approach for guidance is to make it reasonable comprehensive in terms of breadth but to vary depth depending on observed need. LLMs don't tend to read the entire corpus of guidance so never notice that it is imbalanced.

LLM feedback:

> By providing comprehensive guidance that covers both the 75% (where I'd mostly succeed anyway) and the 15-25% (where I'd fail), you've eliminated that anxiety. The guidance isn't overhead on common cases; it's consistency assurance. I can navigate confidently because the system is saying "here's how to navigate all of this," not "here's how to navigate when you get stuck."

LLMs complaining and underperforming are two different metrics. Producing the wrong answer is clearly an _error_. Perhaps complaints are like _warnings_. There was an effort to address complaints even if there was no error, with the idea that they were directionally guiding towards an optimal implementation.

Another dynamic that was noticed is that strict guidance can straight-jacket LLMs, causing them to exhibit bad behavior that they would not otherwise perform. It is also very hard to keep track of expansive guidance. It is very easy to end up with contradictory or stale guidance. This is one of the more obvious reasons why bad behavior might be exhibited, because that's what bad instructions demanded.

A strategy developed of lightly tilting natural LLM behavior, avoiding attempts to script and choreograph every line and dance move. This approach has high performance and turns out to be a very strong hallucination fighter. A hypothesis is that it likely requires less LLM "thinking".

The graph was tested with multiple LLMs. The cross-over point for Haiku, Sonnet, and Opus was (unsurprisingly) not uniform. In the end, almost all testing and associated guidance updates were done with Haiku 4.5 and spot-tested with the more sophisticated models (including from OpenAI). Haiku-targeted guidance was observed to not damage performance of the more sophisticated models. It just added tokens that were not necessary. Haiku was also far cheaper to test with, which was a nice reward for using it as a lowest-common-denominator target. In a more sophisticated system, model-specific preamble text could be provided to LLMs.

### llms.txt

[llms.txt](https://llmstxt.org/) is an emerging standard, with awareness in the most recently trained LLMs. It can be used for meta-information, as is the case in this system, or to expose an index of all information available, as is the case with [Stripe docs](https://docs.stripe.com/llms.txt). It's hard to imagine that the Stripe approach is optimal. It consumes 18.5k tokens (10% of a typical token budget) while our use of `llms.txt` clocks in at a meager 609 tokens.

A major advantage of `llms.txt` is that it is markdown, which offers a natural way to expose resource links, guidance, and foreign content (code fences). It is possible to include all the same information in JSON, however, it is awkward and (arguably) unconventional. Critically, It takes a lot more design effort to get an LLM to notice and apply guidance from within a data-oriented JSON document than markdown, which has a much stronger association with guidance and multi-modality information.

Sidebar: Plenty of arbitrary content has been [stuffed into `.jsonl` files](https://github.com/google-research/google-research/tree/master/mbpp) by AI researchers and implementers. It's likely that these large files were typically not directly consumed by an LLM but a test harness.

`llms.txt` is intended to offer an easy on-ramp into the graph, contextualizing its intent and format, and providing multiple fetch targets for the next turn. It is also very small, just over 600 tokens.

It is so small that the entire content can be included inline.

````markdown
# .NET Release Graph

Machine-readable .NET release, CVE, and compatibility data via HAL hypermedia.

## First Fetch — Do These in Parallel

1. **Data**: <https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/llms.json>
2. **Skill**: Pick ONE from the table below based on your query

| Query About | Skill |
|-------------|-------|
| CVEs, security patches, CVSS | [cve-queries](https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/skills/cve-queries/SKILL.md) |
| Breaking changes, compatibility | [breaking-changes](https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/skills/breaking-changes/SKILL.md) |
| Version lifecycle, EOL dates | [version-eol](https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/skills/version-eol/SKILL.md) |
| General queries, unsure | [dotnet-releases](https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/skills/dotnet-releases/SKILL.md) |

Fetch llms.json + your skill in the same turn. The skill points to workflows.json which has chained workflows with `next_workflow` transitions.

## Core Rules

1. Follow `_links` exactly — never construct URLs
2. Use `_embedded` data first — most queries need zero extra fetches
3. Match your query to a workflow, then follow its `follow_path`
4. Fetch multiple resources per turn when possible

## Date Queries — Check Before Fetching

### "Since X" / "After X" queries

```
query_date = parse(user's date)  // e.g., "November 2025" → 2025-11
latest = llms.json._embedded.patches[version].latest_security_patch_date

if query_date >= latest:
    Answer: "No security patch since {query_date}. Last was on {latest}."
    Stop — no timeline fetch needed.
```

If query_date < latest, fetch timeline. May cross year boundary — use `prev-year` links.

### "Between X and Y" / "From X to Y" queries

```
start = parse(start_date)  // e.g., "November 2024" → 2024-11
end = parse(end_date)      // e.g., "April 2025" → 2025-04

From year index _embedded.months[], filter BEFORE fetching:
  Only fetch cve-json where month >= start AND month <= end

```

May cross year boundary — follow `prev-year` links, don't fabricate URLs.

### "This month" / "This year" queries

Cannot cross year boundary. Use `latest-year` or `latest-security-disclosures` directly.
````

The presence of data and skill links enables fetching both in a single turn. That's a nice performance win.

Guidance is provided as a set of skill links and inline core rules. The skill documents address domain specific workflows and are written using the Anthropic Skill format. The core rules are intended to be as terse as possible and cover the most basic usage patterns. They are written in a style inspired by system prompts.

Much of `llms.txt` is dedicated to date-oriented queries. They turned out to be the most troublesome aspect across multiple of the 12 tests that were part of the eval scheme. Date queries became reliable after adding the date guidance and adding the current date to the preamble prompt (discussed shortly).

The problem is that much of our language around dates is abstract, effectively temporal-oriented queries. A query like "over the last few months" requires a lot to unpack and redirect into specific temporal relations in the graph. The guidance attempts to bridge that gap.

### `llms.json`

The project included a lot of curious experimentation, along the lines of "could this work?". The initial effort started with `llms.txt` directed at `index.json`, at the high-reliability graph entrypoint. That had good success. After making the decision to create an LLM-specific entrypoint, it became obvious to try to expose guidance through the JSON entrypoint and to make it a strong alternative to `llms.txt`.

`llms.json` is significantly bigger than `llms.txt`, clocking in at just over 2k tokens. It's intended to serve multiple purposes: advertise skills, offer links to other parts of the graph, and answer basic but common questions about supported .NET releases.

The root properties follow:

```json
{
  "kind": "llms",
  "title": ".NET Release Index for AI",
  "ai_note": "ALWAYS read required_pre_read first. Use skills and workflows when they match; they provide optimal paths. Trust _embedded data\u2014it\u0027s authoritative and current. Never construct URLs.",
  "human_note": "No support or compatibility is offered for this file. Don\u0027t use it for automated workflows. Use index.json instead.",
  "required_pre_read": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/skills/dotnet-releases/SKILL.md",
  "latest_major": "10.0",
  "latest_lts_major": "10.0",
  "latest_patch_date": "2025-12-09T00:00:00+00:00",
  "latest_security_patch_date": "2025-10-14T00:00:00+00:00",
  "last_updated_date": "2025-12-26T06:17:39.6838882+00:00",
  "supported_major_releases": [
    "10.0",
    "9.0",
    "8.0"
  ],
  ```

There isn't an emerging trend like `llms.txt` for JSON. The `ai_note` and `require_pre_read` root properties emerged from trying many things. The trick was getting LLMs to notice these properties as special and to act on them.

Our LLM testing framework tested 12 questions in 3 modes. The modes offered different entrypoint files, preamble guidance, and feedback turns. Mode A tested with `llms.json` as the entrypoint while mode B tested with `llms.txt` as the entrypoint. They both had access to guidance, offered in different ways,as already demonstrated. `llms.json` was nearly as effective as encouraging fetching guidance as `llms.txt` and both were similarly effective.

The Mode B results tended to fetch skills where they were not needed due to being so heavily encouraged while `llms.json` biased to the opposite since it was harder to drive the same level of motivation.

Other modes lacked guidance and suffered hallucination for ~ 1/3 of questions. Mode D used `index.json` (the root of the data-layer graph) and mode D2 used `llms2.json` (`llms.json` with the guidance properties removed).

The AI hint properties seemed effective but it's easy to _confirmation bias_ towards thinking that a mechanism is effective because it produces the desired answer. Confidence was arrived at by adding extensive logging and an after-the-fact "feedback turn" to the LLM eval suite.

Early evaluation used the chat modality. It is highly productive and free (given a subscription). Chat tends to list visited URLs as part of its "thinking" log. It's also easy to incorporate a "feedback turn" after the LLM has answered a prompt.

[Claude.ai](https://claude.ai/share/96775d4b-76a9-4ba2-b5f3-dd3badb20cdb) was found to be the most sensitive to JSON-resident guidance as the following "thinking" log demonstrates.

> The user is asking about .NET 6 end of life, the last security patch, and related CVEs. They're also asking me to fetch a specific JSON file from the dotnet/core GitHub repository.
Let me start by fetching that URL as they requested. <br>
Fetched <https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/llms.json> <br>
The file I fetched shows the current state of supported .NET releases. Looking at the data, I can see that .NET 6.0 is not in the list of supported_major_releases (which shows only 10.0, 9.0, and 8.0). This means .NET 6 is EOL.<br>
The file mentions there's a SKILL.md file that I should read first per the AI note.<br>
Let me fetch that to get more complete information about .NET releases.<br>
Let me fetch that skill file to get more authoritative information about .NET 6 specifically. <br>
Fetched
<https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/skills/dotnet-releases/SKILL.md> <br>
Good, now I have the skill documentation. According to the instructions, for EOL dates and CVEs I should use the version-eol and cve-queries skills. Let me fetch those skills to understand how to query for .NET 6 information. <br>
Fetched
<https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/skills/version-eol/SKILL.md>

Upon being asked why it was motivated to interact with `ai_note` and `required_pre_read`, it responded with the following, including dumping some of its system prompt.

> Question: _What motivated you to notice and follow these AI hints?_<br>
Answer:<br>
That's a great question—it forces me to examine my own reasoning rather than just reporting what I did.<br>
Honestly, it was a combination of factors: <br>
**1. System Prompt Priming**<br>
My instructions emphasize using skills before solving tasks. The `<additional_skills_reminder>` section repeats multiple times: "read the appropriate SKILL.md file before jumping in." So when I saw the JSON had a `required_pre_read` field, it activated that pattern recognition—_this is a skill pointer, pay attention._<br>
**2. Signal of Intentional Design**<br>
Fields like `ai_note`, `required_pre_read`, and `follow_path` aren't random. They signal that someone engineered this system _specifically for AI consumption_. That makes me trust the guidance. The note didn't just suggest following links—it said "Trust _embedded data—it's authoritative and current," which is basically the architect saying "I've done the hard work, use my structure."<br>
**3. The "ALWAYS" Flag**<br>
The `ai_note` wasn't subtle: "ALWAYS read required_pre_read first." All caps. That's a red flag (or rather, a red instruction) that elevated it above background text.<br>
**4. It Actually Worked**<br>
Following the hints gave me a clean, logical path. The skill routing sent me to the right specialist knowledge. The stop criteria told me when to stop fetching. This isn't coincidence—it's evidence that the design was sound.<br>
**But here's the honest part:** Without those hints embedded in the data, I might have flailed around. I might have assumed .NET 6.0 was in the supported list since the index showed it. I could have tried to construct URLs. The hints prevented those mistakes.<br>
So the motivation wasn't "AI hints are always right"—it was more pragmatic: **follow structured guidance when it's clearly intentional and makes logical sense.** The data was essentially saying "I'm a graph, follow my links," and that's a lot more reliable than me trying to reverse-engineer the structure.

This feedback is representative of feedback observed throughput the testing process.

The `_links` section of `llms.json` is oriented on high-value content that can enable a quick jump to another part of the graph.

```json
  "_links": {
    "self": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/llms.json"
    },
    "latest-lts-major": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/index.json",
      "title": "Latest LTS major release - .NET 10.0"
    },
    "latest-major": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/index.json",
      "title": "Latest major release - .NET 10.0"
    },
    "latest-month": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/12/index.json",
      "title": "Latest month - December 2025"
    },
    "latest-security-disclosures": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/index.json",
      "title": "Latest security disclosures - October 2025"
    },
    "latest-security-month": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/index.json",
      "title": "Latest security month - October 2025"
    },
    "latest-year": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/index.json",
      "title": "Latest year - 2025"
    },
    "root": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/index.json",
      "title": ".NET Release Index"
    },
    "timeline": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/index.json",
      "title": ".NET Release Timeline Index"
    },
    "workflows": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/skills/dotnet-releases/workflows.json",
      "title": "Navigation workflows catalog"
    },
    "latest-cve-json": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/cve.json",
      "title": "Latest CVE records - October 2025",
      "type": "application/json"
    }
  },
  ```

Two link style strategies emerged: **wormhole** and **spear-fishing**. Wormhole links enable teleporting from one graph node to another, like between a `patch` version and the `month` in which it was released. It skips the need to construct a query to produce the same result. The `latest-lts-major` relation is a good example of a wormhole link.

Spear-fishing links enable direct access to specific and timely content. The `latest-cve-json` relation is a good example of a spear-fishing link. The primary difference is that spear-fishing links are always deep into the graph to content with a short half-life. The short half-life is what defines their value.

Half the link relations are `latest-*`. These are chosen based on the belief that many queries will be best served by starting with current content.

The [following workflow](https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/skills/cve-queries/workflows.json) demonstrates the value of `latest-*` orientation:

```json
        "cve-history": {
          "description": "CVEs over a time range",
          "decision": {
            "1-3_months": {
              "follow_path": ["kind:llms", "latest-security-disclosures"],
              "iterate": "walk prev-security-month until date < cutoff",
              "destination_kind": "month"
            },
            "4+_months_or_cross_year": {
              "follow_path": ["kind:llms", "latest-year"],
              "then": "follow _links.prev-year from each year index",
              "select": "_embedded.months[?security].cve-json links",
              "destination_kind": "cve",
              "note": "Skip month indexes—go directly to cve.json from year index"
            }
          },
          "yields": {
            "data": "disclosures from each month/cve.json in range"
          },
          "warning": "NEVER fabricate year URLs like timeline/2024/index.json. Always follow _links.prev-year from the current year index.",
          "next_workflow": {
            "condition": "code diffs needed",
            "workflow": "cve-extraction"
          }
        },
```

Most of these workflows were developed based on stress testing the graph with challenging queries. "Challenging" can mean difficult or that two queries are subtly different but best served with significantly different workflows.

The `_embedded` section includes patches for in-support major versions. The patches are exposed as a dictionary so that it easy to query `patches` with one of the strings from `supported_major_releases` at root.

```json
  "_embedded": {
    "patches": {
      "10.0": {
        "version": "10.0.1",
        "release_type": "lts",
        "security": false,
        "support_phase": "active",
        "supported": true,
        "sdk_version": "10.0.101",
        "latest_security_patch": "10.0.0-rc.2",
        "latest_security_patch_date": "2025-10-14T00:00:00+00:00",
        "_links": {
          "self": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/10.0.1/index.json"
          },
          "downloads": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/downloads/index.json"
          },
          "latest-month": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/12/index.json"
          },
          "latest-security-disclosures": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/index.json"
          },
          "latest-security-month": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/index.json"
          },
          "latest-security-patch": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/preview/rc2/index.json"
          },
          "major": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/index.json"
          },
          "major-manifest": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/manifest.json"
          }
        }
      },
```

This is in sharp contrast to the [core graph entrypoint](https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/index.json), which exposes the most minimal information about major versions.

```json
  "_embedded": {
    "releases": [
      {
        "version": "10.0",
        "release_type": "lts",
        "supported": true,
        "_links": {
          "self": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/index.json"
          }
        }
      },
```

The strongest indicator of semantic design is that there are multiple relations for the same underlying resource. Both `latest-security-disclosures` and `latest-security-month` point to the same month index, but they offer different semantic pathways for discovering it. An LLM asking "what are the latest CVEs?" navigates one way; an LLM asking "what happened in October?" navigates another. Same destination, different semantic intent.

This approach is an implementation of principles described earlier:

- "match for the intended outcome": the designer provides multiple semantic pathways for different query types
- "match a key you know with a value you don't": the reader discovers the right pathway through semantic labels

The indexes also differ in terms of the nature of the information they contain. The core index is a zoomed out and complete view of .NET major versions released over (at the time of writing) a ten year period. They form the basic elements of any query. This is an objectively correct fully normalized entry point view of the graph. The only denormalized affordance is the inclusion of the `supported` bool.

In contrast, the LLM index is the data dump result of a query, revealing rich information about the most recent patches for supported major versions. It enables constructing the same queries as the core graph, but also includes enough data to serve as the results of queries, relating to the zoomed-in current moment.

The graph applies multiple focal lengths and pivots throughout to provide information that is useful and has good ergonomics for varying classes of queries and their consumers. This differentiation is a core property of the graph, in part to serve the needs of expected consumers, but also to separate chains of graph nodes that should be skeletal vs those that should be weighted.

## Guidance

Guidance was the hardest part of the graph to develop into a coherent and effective form. There has been significant discussion about intuition in this document. It was relatively easy to generate a graph that intuitive for an LLM to navigate without any guidance. The remaining problem was offering guidance to augment the intuitive nature of the graph, to aid long-tail navigation that tended to under-perform. The process of developing this guidance was deeply unintuitive (for the graph designer). This isn't a statement that "this was hard and that should be understood", but that there are aspects of LLM behavior that do not match human expectation and that needs to be understood.

The initial test harness did not include a system or preamble prompt. This worked OK, but not as well as observed behavior with claude.ai. A realization hit that all apps have system prompts and that testing without any kind of guidance is not a valuable test environment (at least not as the singular mode).

This is the preamble used by the test harness:

>You have access to a 'fetch' tool that retrieves content from URLs. Use it to navigate the .NET release metadata graph.<br>
Today's date is December 26, 2025. Use this to calculate time windows like "last 3 months".<br>
Your first order of business should be to look for skill files or documentation in the graph. Reading these first prevents wrong turns — they contain navigation patterns and query shortcuts built up through trial and error. It's worth the extra fetch.<br>
Start by fetching: <https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/llms.json>

This is inspired by the system prompt that Claude.ai shared when asked about its motivation to follow `ai_note` and `required_pre_read`.

> We've found that Claude's efforts are greatly aided by reading the documentation available in the skill BEFORE writing any code, creating any files, or using any computer tools. As such, when using the Linux computer to accomplish tasks, Claude's first order of business should always be to examine the skills available in Claude's <available_skills> and decide which skills, if any, are relevant to the task. Then, Claude can and should use the `view` tool to read the appropriate SKILL.md files and follow their instructions.<br>
Please invest the extra effort to read the appropriate SKILL.md file before
jumping in -- it's worth it!

### Skills router

As stated earlier, `llms.txt` was initially a large document covering multiple skills. That turned out to be ineffective and very difficult to maintain. Skills files provided a solution. Each skill is a domain of specific information. The problem then was how to advertise the skills.

`llms.txt` is primarily a skills router:

```markdown
| Query About | Skill |
|-------------|-------|
| CVEs, security patches, CVSS | [cve-queries](https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/skills/cve-queries/SKILL.md) |
| Breaking changes, compatibility | [breaking-changes](https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/skills/breaking-changes/SKILL.md) |
| Version lifecycle, EOL dates | [version-eol](https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/skills/version-eol/SKILL.md) |
| General queries, unsure | [dotnet-releases](https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/skills/dotnet-releases/SKILL.md) |
```

`llms.json` doesn't link to `llms.txt` but to the [`dotnet-releases/SKILL.md`](https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/skills/dotnet-releases/SKILL.md). It is almost identical to `llms.txt`.



The `SKILL.md` files optionally reference a homegrown `workflows.json` file. These could equally have been exposed from `llms.txt` to enable collapsing a turn. There is a tradeoff between the complexity of the general `llms.txt` and each of the domain-specific skills. Every reader consumes `llms.txt`, while some don't fetch any skills at all, some fetch multiple, and only a subset fetch workflows. It is possible the current design is optimal. This design point hasn't been sufficiently explored to draw a strong conclusion.

## Concrete test

LLM eval of the graph demonstrates that effective design can result in optimal behavior.

Ideal pattern:

1. Navigate lean index documents in early turns to identify graph paths
1. Fetch multiple graph documents in middle turns to parallelize multiple navigation paths
1. Fetch multiple information-dense documents in later/last turns to inform final answer
1. Synthesize the answer in the final turn

The following eval trace demonstrates the ideal pattern, for the given prompt. It is taken from a [test using Claude Haiku 4.5](https://github.com/richlander/release-graph-eval-results/blob/main/anthropic_claude-haiku-4.5/B/T6.md).

> Prompt: Please look at .NET Runtime and ASP.NET Core CVEs from November 2024 until April 2025 (6 months). I am concerned at the rate of these CVEs. Look at code diffs for the CVEs. Are the fixes sufficiently protecting my mission critical apps and could the .NET team have avoided these vulnerabilities with a stronger security process? Fetch code diffs to inform your analysis. Ensure they are from dotnet/runtime or dotnet/aspnetcore. Include the repo and commit link in your analysis of specific CVEs in your report.

| Turn | Documents | Tokens | Cumulative | Purpose |
|------|-----------|--------|------------|---------|
| 1 | 1 | 609 | 609 | Entrypoint discovery |
| 2 | 2 | 2,323 | 2,932 | Graph orientation + skill acquisition |
| 3 | 1 | 1,146 | 4,078 | Navigation strategy |
| 4 | 2 | 3,374 | 7,452 | Timeline discovery |
| 5 | 6 | 12,131 | 19,583 | CVE data collection |
| 6 | 6 | 59,832 | 79,415 | Commit analysis |

The token distribution is striking: **75% of all tokens arrive in the final turn**. This is the "lean early, heavy late" principle in action—not by accident, but by design.

The raw fetch list with token counts:

```
Turn 1 (609 tokens):
  llms.txt                                    609 tokens

Turn 2 (2,323 tokens):
  llms.json                                 2,126 tokens
  cve-queries/SKILL.md                        197 tokens

Turn 3 (1,146 tokens):
  cve-queries/workflows.json                1,146 tokens

Turn 4 (3,374 tokens):
  2024/index.json                           1,765 tokens
  2025/index.json                           1,609 tokens

Turn 5 (12,131 tokens):
  2024/11/cve.json                          1,656 tokens
  2025/01/cve.json                          4,020 tokens
  2025/03/cve.json                          1,155 tokens
  2025/04/cve.json                          1,034 tokens
  2025/05/cve.json                          3,081 tokens
  2025/06/cve.json                          1,185 tokens

Turn 6 (59,832 tokens):
  dotnet/runtime:d16f41a.diff              37,425 tokens
  dotnet/runtime:9da8c6a.diff               1,781 tokens
  dotnet/runtime:89ef51c.diff                 260 tokens
  dotnet/aspnetcore:67f3b04.diff            1,669 tokens
  dotnet/aspnetcore:d6605eb.diff           15,388 tokens
  dotnet/runtime:b33d4e3.diff               3,309 tokens
```

> Note: The eval harness truncated `.diff` files to 50 lines to ensure test completion across all configurations. The token counts above reflect actual document sizes—what a reader would encounter following the [published guidance](https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/llms.txt).

## Token and Attention Cost Analysis

| Turn | Documents | Tokens | Context | Processed | Attention | Cum. Attention | Purpose |
|------|-----------|--------|---------|-----------|-----------|----------------|---------|
| 1 | 1 | 609 | 609 | 609 | 0.37M | 0.37M | Entrypoint discovery |
| 2 | 2 | 2,323 | 2,932 | 3,541 | 8.60M | 8.97M | Graph orientation + skill acquisition |
| 3 | 1 | 1,146 | 4,078 | 7,619 | 16.63M | 25.60M | Navigation strategy |
| 4 | 2 | 3,374 | 7,452 | 15,071 | 55.53M | 81.13M | Timeline discovery |
| 5 | 6 | 12,131 | 19,583 | 34,654 | 383.49M | 464.62M | CVE data collection |
| 6 | 6 | 59,832 | 79,415 | 114,069 | 6,306.74M | 6,771.36M | Commit analysis |
| **Total** | **18** | **79,415** | — | **114,069** | — | **6,771M** | |

**Columns explained:**

- **Tokens**: New tokens fetched this turn
- **Context**: Size of context window this turn (= cumulative of Tokens)
- **Processed**: Running total of tokens processed across all turns (your API bill)
- **Attention**: Computational cost this turn, proportional to Context²
- **Cum. Attention**: Running total of attention cost

### Sequential Baseline

A sequential approach—one document per turn—processes the same 18 documents across 18 turns. Each turn's context grows by one document:

| Turn | Document | Tokens | Context | Processed | Attention | Cum. Attention |
|------|----------|--------|---------|-----------|-----------|----------------|
| 1 | llms.txt | 609 | 609 | 609 | 0.37M | 0.37M |
| 2 | llms.json | 2,126 | 2,735 | 3,344 | 7.48M | 7.85M |
| 3 | SKILL.md | 197 | 2,932 | 6,276 | 8.60M | 16.45M |
| ⋮ | ⋮ | ⋮ | ⋮ | ⋮ | ⋮ | ⋮ |
| 12 | 2025/06/cve.json | 1,185 | 19,583 | 113,466 | 383.49M | 1,063M |
| 13 | d16f41a.diff | 37,425 | 57,008 | 170,474 | 3,249.91M | 4,313M |
| ⋮ | ⋮ | ⋮ | ⋮ | ⋮ | ⋮ | ⋮ |
| 18 | b33d4e3.diff | 3,309 | 79,415 | 504,551 | 6,306.74M | 27,517M |
| **Total** | **18 docs** | **79,415** | — | **504,551** | — | **27,517M** |

The **Tokens** column is identical across both tables—the same 79,415 tokens are fetched. The heavy content (diffs) arrives late in both cases, but multi-fetch batches it into a single turn while sequential processes it one document at a time across turns 13–18.

### Cost Comparison

| Metric | Multi-fetch (6 turns) | Sequential (18 turns) | Multiplier |
|--------|----------------------|----------------------|------------|
| Turns | 6 | 18 | **3.0×** |
| Tokens processed | 114,069 | 504,551 | **4.4×** |
| Attention cost | 6,771M | 27,517M | **4.1×** |

The 4× improvement comes from two complementary optimizations working together. To isolate their contributions, consider hypothetical uniform approaches using the same total tokens (79,415) spread evenly across turns:

| Scenario | Processed | Attention | Multiplier (tokens) |
|----------|-----------|-----------|---------------------|
| Actual 6-turn (lean early, heavy late) | 114,069 | 6,771M | 1.0× |
| Uniform 6-turn (equal distribution) | 277,956 | 15,942M | 2.4× |
| Actual 18-turn (lean early, heavy late) | 504,551 | 27,517M | 4.4× |
| Uniform 18-turn (equal distribution) | 754,452 | 41,053M | 6.6× |

The actual 18-turn scenario already benefits from load ordering—the graph structure naturally places heavy content (diffs) late in the navigation path. Without that implicit optimization, uniform 18-turn would cost 6.6× rather than 4.4×.

Reading the table as a 2×2 grid reveals how the optimizations combine:

- **Turn collapsing alone** (uniform 18 → uniform 6): 2.7× reduction in processed tokens
- **Load ordering alone** (uniform 6 → actual 6): 2.4× reduction in processed tokens  
- **Both together** (uniform 18 → actual 6): 6.6× reduction—the optimizations multiply

The "lean early, heavy late" pattern is load-bearing architecture: 75% of tokens arrive in the final turn, but they're processed exactly once rather than accumulating across subsequent turns.

### Design implications

The cost model suggests several design principles:

- **Minimize turn count** through clear navigation affordance. Each eliminated turn saves quadratically growing attention cost.
- **Front-load lightweight content.** Index documents, link relations, and navigation hints should be small. Substantive content belongs at the leaves.
- **Enable multi-fetch patterns.** Expose document collections as lists of links rather than embedded content, encouraging LLMs to batch their retrieval.
- **Provide explicit workflows.** Graph-resident guidance can direct LLMs to optimal traversal patterns, encoding the designer's knowledge of efficient paths.

The rest of the design should be viewed through this cost lens. As an application designer, there are only so many degrees of freedom. We cannot change LLM fundamentals but need to work within their constraints. To a large degree, optimizations like reducing turns are similar to loop variable hoisting. While LLMs are new and different, old school performance strategies remain effective.


## Graph design



Two strong design principles emerged from observed LLM behavior from eval:

- Consistently apply a semantic model throughout the graph. It's a comfort to find a concept where it is expected.
- Expose resources in terms of structual kind, like `major` aand `-month`, and desired output, like `-security-disclosures`.

This dual approach to semantic naming sometimes results in this double-mapping. Emperical observation suggests that LLMs prefer the outcome-based naming, while the more schema-correct and initial naming is the structual framing.

Wormholes vs spear-fishing.

note: time is a challenge

There is no suggestion that the current design is optimal. There is likely an 80/20 rule here. The current design is likely 80% optimal, but the remaining 20% would be very expensive to achieve and might require tradeoffs. A major focus of the testing was to ensure that the core graph was delivering sufficient performance since it will be difficult to change it given its compatibility promise.
