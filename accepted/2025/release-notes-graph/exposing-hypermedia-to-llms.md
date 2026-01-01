# Exposing Hypermedia Information Graphs to LLMs

[Hypermedia](https://en.wikipedia.org/wiki/Hypermedia) and [hypertext](https://en.wikipedia.org/wiki/Hypertext) are decades-old ideas and formats that are perfectly-suited for LLM consumption by virtue of self-describing structure and relationships between resources. The premise is that there is sufficient meta-information in a hypermedia document graph for a semantic consumer to successfully traverse it to find the information demanded by a user prompt. The prevailing narrative over the last few decades has been that _structured data_ > _unstructured documents_, in terms of inherent capacity to derive meaningful insight. JSON and XML came out of that heritage, with [JSONPath](https://en.wikipedia.org/wiki/JSONPath) and [XPath](https://en.wikipedia.org/wiki/XPath) providing structured query supported by a priori schema knowledge. [Hypermedia as the engine of application state (HATEOAS)](https://en.wikipedia.org/wiki/HATEOAS) contributes the idea that data labeling can be extended to relations across resources. These approaches are integrated in this system to enable an LLM to search for and discover desired information across labeled nodes and edges within a graph. In a more traditional system, a schema is the pre-requisite to traversal, whereas in a semantic system, it is traversal that reveals the schema.

> A (nearly) century-old principle, articulated by [Korzybski](https://en.wikipedia.org/wiki/Alfred_Korzybski): [the map is not the territory](https://en.wikipedia.org/wiki/Map%E2%80%93territory_relation).

In trail races, there are frequent ribbons hanging from trees and painted arrows on the ground to ensure the correct path taken. It is often the case that there are races of multiple distances being run on an overlapping course. At key intersections, there are signs that say "5 KM -> left" and "10 KM -> continue straight". The ribbons and the painted arrows are the kind of map that a document schema provides, ensuring correct coloring within the lines. The signposting where two courses diverage is the extra descriptive insight that enables graph traversal. The signposting is a key-value function. You match a key you recognize with a value you cannot predict. Signposting provides comprehension that enables directed navigation of the territory.

This approach is in sharp contrast to the typical HTML graph implementation: `For a deeper discussion on this topic, <a href="another-document.html">click here</a>.`. A semantic graph might expose a named link relation like `{ "link-relation": "gardening-deep-dive", "href": "..." }` or expose more descriptive complexity by separating the parts, like `"link-relation": "deep-dive"` from `"target-kind": "gardening"`, cleanly splitting the link-adjective and its target-noun. The better the semantic implementation, the less inference, flow analysis, or attention is required to derive the intended meaning.

Databases went through a "no-sql" transition. That wasn't a rejection of structure, but a realization that the source of structure is the documents. Hypermedia graphs extend this idea enabling "no-schema" consumption. Rather than requiring upfront schema knowledge, a fully realized semantic space -- both content and link relations -- allows readers to discover structure through descriptive labels and traversal. A sort of "world schema" emerges from navigation.

Hypermedia information document graphs can be published pre-baked, making them suitable for direct consumption without being pre-loaded and exposed by a vector database. The semantic relationships and other meta-information are used as the basis of typical LLM mechanics like vector similarity, making hypermedia a kind of RAG scheme and suitable for static-webhost deployment.

The concept of a pre-baked static hypermedia graph has been applied to the .NET release notes. That project was initially approached as a modern revamp of a set of JSON files that are used to inform and direct cloud-infra deployment and compliance workflows at scale. Over time, it became obvious that LLMs could read the same content directly and self-reason about its content and navigation patterns. Early experimentation proved that out. The primary techniques used to improve applicability for LLMs are semantic naming and graph-resident guidance. These techniques can be quite subtle, where a small shift in semantic bias can result in a large shift in LLM performance.

Graph-resident guidance consists of skills and workflows. HATEOAS tells us that "customer" can be a relation of a sales order. Why not make "graph-instructions" a relation of a graph? Skills and workflows are first-class relations in the graph, enabling graph designers to express navigation intent. Skills follow the Anthropic skill format, while workflows are HAL documents that describe queries over graph link relations. This enables the graph designer to provide readers with "ten-km-route" style workflows if that's a match for the intended outcome.

The .NET release notes information graph uses [Hypertext Application Language (HAL)](https://en.wikipedia.org/wiki/Hypertext_Application_Language) as its hypermedia foundation. It augments HAL with a homegrown HAL workflow convention that looks just as native as `_links` or `_embedded`. LLMs grasp the intent, in part because HAL predates LLMs by over a decade. This approach enables low-cost LLM enablement for scenarios where hosting a persistent "AI server" would be prohibitive. And, of course, this approach has utility beyond release notes.

## Graph design

The release notes information graph is based on the restrictive idea that the entrypoint of the graph should be skeletal and rarely changing. That's workable for LLMs but far from ideal. The restrictive idea of the core graph is that it should support an n-9s level of reliability and be subject to rigorous engineering practices (git workflows, peer review, merge gates). However, we're in the early days of AI and subject to repeated waves of externally-driven change that may require significant and quick re-evaluation and re-work to maintain high-quality LLM enablement. These modalities are in firm opposition.

We can instead view the core graph as a well-defined data-layer that honors the desired reliability requirements, while exposing a separate application-layer entrypoint for LLMs that can evolve over time without the heavy compatibility burden.

The graph as a whole is based on a largely traditional schema design, utilizing both normalized and denormalized approaches in (hopefully informed) service of consumers. After the graph was realized, it became possible to test it with `jq` as a sort of passive and syntactic consumer and with LLMs as a much more active and semantic consumer. The graph was successively (and hopefully successfully) adapted to improve performance for both consumption styles. Performance is primarily measured in terms of terseness of query and quickness (fetches and data cost) of response. Much of the feedback was fundamental in nature. The overall character of the graph remains a pure information-oriented data design, but with a minor tilt towards semantic consumers.

The choice of hypermedia as the grounding format is a case-in-point of the overall approach. Hypermedia long pre-dates LLMs, however, it has always held semantic consumers (humans) as a key design cohort. Hypermedia formats provide a conceptual framework that is easy to flavor towards semantic consumption. This flexibility proved useful as the design was adapted with LLM feedback. It should also be noted that LLM feedback is by far the cheapest and most accessible form of feedback. LLMs are happy to provide usage feedback throughout the night while other semantic consumers are sleeping.

A few strong design principles emerged from observed LLM behavior from eval:

- Consistent application of a conceptual model creates familiarity for semantic consumers. It is a comfort to find a concept exposed where it is expected.
- Resources can be dual-mapped in terms of structual kind, like `major` aand `-month`, and desired output, like `-security-disclosures`. Prompts can bias towards different concerns. Differentiated mappings are more to present a similar match to semantic consumers.
- LLMs operate on a model of scarcity, with tokens at a premium. Smaller graph nodes encourage greater graph navigation. Comprehension can be made to outperform consumption cost.
- LLMs will acquire multiple resources in a single turn if a good strategy for doing so is evident.

### LLM entrypoints

There are entypoints provided for LLMs:

- [llms.txt](https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/llms.txt) -- Prose explanation of how to use the graph, including a link to llms.json.
- [llms.json](https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/llms.json) -- The LLM index (AKA "application-layer entrypoint for LLMs"). It also includes guidance inline.

[llms.txt](https://llmstxt.org/) is an emerging standard, with awareness in the most recently trained models. It can be used for meta-information (as is the case in this system) or to expose an index of all information available (as is the case with [Stripe docs](https://docs.stripe.com/llms.txt)). It's hard to imagine that the Stripe approach is optimal. It uses 18.5k tokens (10% of a typical token budget) while our use of `llms.txt` clocks in at a meager 626 tokens.

A major advantage of `llms.txt` is that it is markdown, which offers a natural way to expose resource links, guidance, and foreign content (code fences). It is possible to include all the same information in JSON, however, it is awkward and (critically) unconventional. It takes a lot more design effort to get an LLM to notice and apply guidance from within a data-oriented JSON document than markdown, which has a much stronger association with guidance and multi-modality information.

Our use of `llms.txt` includes an entrypoint link to `llms.json`, a table of skills content, and basic initial guidance. LLMs will often fetch the data URL and one or more skills files in a single turn. Fetching multiple documents in a single turn is a useful tactic for token optimization.

## Performance implications

Some questions can be answered from the LLM entrypoint, however, many require navigating to documents within the core graph. It is not feasible or desirable to include all information in a single document. As a result, a turn-by-turn approach is required. At each turns, there is new content, new insight, and then selection of critical information that directs the next fetch(es) or is the desired answer. The range of required turns varies greatly, a join of information design over data and comprehension of the overall information framework by the LLM.

There is a cost function for LLMs based of the mechanics of the [transformer architecture](https://en.wikipedia.org/wiki/Transformer_(deep_learning)). The cost of multiple turns can be prohibitive, resulting in conversation failures/termination, poor performance, or high cost. The graph design has a direct impact on LLM performance and cost.

Let's develop an intuitive for cost. There are three major cost functions at play:

- Token cost, which is additive across turns, and summed across turns
- Context, which is the accumulated token cost of the final turn
- Attention computation, which is the square of the tokens in any one turn and the sum of those squares across turns

Let's simplify, assuming that tokens (`n`) are uniform per turn (`m`).

Context (accumulated tokens): `mn`

Total token cost (summed tokens / turn): ?

Attention (number of interactions required): `n² * m³ / 3`

I need the m turns table here.

Total: `n²(1 + 4 + 9 + ... + m²)` = `n² × m³/3`

What's actually happening:

`n²` is the quadratic attention factor (tokens attending to tokens)
`m³` emerges from summing squares across turns

Intuitive model:

Batched: (total tokens)² = (nm)² = n²m² — pure quadratic
Sequential: sum of growing quadratics = n²m³/3 — cubic in turns

Batched is the cheapest form. It's the mode where all fetches can be collapsed into a single term. We escape the successive cost of attention. Sequential is the more realistic but costly model, where each fetch happens in sequence. The cost difference is very large.

One can draw the conclusion that any system that requires the turn-by-turn approach is bad because it forces the sequential approach. That's not really a correct framing. Many problems require multiple turns. For example, many ChatGPT conversations go on for quite some time. A more useful framing is determining if there is a way to reduce turns or apply some other optimization.

> The release notes information graph is based on the restrictive idea that the entrypoint of the graph should be skeletal and rarely changing.

This design approach was selected in service of n-9s reliability and performance. It takes on a new light when viewed through various LLM cost functions. The formulas above assume that token count is uniform. If it isn't, then you'd want lean token counts up front to guide planning and navigation and then more weighty token counts at the end (at the hopefully final turn) used to derive an answer. The ability to fetch multiple documents in a single turn further optimizes the cost function. It helps us split the difference between the batched and seqential cost models.

The rest of the design should be viewed in terms of these cost functions. It is to a large degree the whole game at play.

## llms.txt

The following are exerpts of the two files, enough to provide a sense of their approach.

`llms.txt`:

```markdown
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
```

`llms.json`:

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
  "last_updated_date": "2025-12-24T12:33:04.8560376+00:00",
  "supported_major_releases": [
    "10.0",
    "9.0",
    "8.0"
  ],
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
```

## Graph entrypoint tension

We can compare the embedded resource section of the two entrypoints.

[Core graph entrypoint](https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/index.json):

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

That's how the core graph exposes a major version. As suggested, it's skeletal. The graph entrypoint only needs to be updated once or twice a year. Even if the file is regenerated daily, git won't notice any changes.

[LLM entrypoint](https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/llms.json):

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

The LLM graph exposes a lot more useful information. The semantic data and link relations are on clear display.

The strongest indicator of semantic design is that there are multiple relations for the same underlying resource. Both `latest-security-disclosures` and `latest-security-month` point to the same month index, but they offer different semantic pathways for discovering it. An LLM asking "what are the latest CVEs?" navigates one way; an LLM asking "what happened in October?" navigates another. Same destination, different semantic intent.

This approach is an implementation of principles described earlier:

- "match for the intended outcome": the designer provides multiple semantic pathways for different query types
- "match a key you know with a value you don't": the reader discovers the right pathway through semantic labels

The indexes also differ in terms of the nature of the information they contain. The core index is a zoomed out view of .NET versions released over (at the time of writing) a ten year period. They form the basic elements of any query. This is an objectively correct normalized entry point view of the graph. In contrast, the LLM index is the result of a query, revealing rich information about the most recent patches for supported major versions. It enables constructing the same queries as the core graph, but also includes enough data to serve as the results of queries, relating to the zoomed-in current moment.

The graph applies multiple focal lengths and pivots throughout to provide information that is useful and has good ergonomics for varying classes of queries and their consumers. This differentation is a core property of the graph, in part to serve the needs of expected consumers, but also to separate chains of graph nodes that should be skeletal vs those that should be weighted.

## Graph design



Two strong design principles emerged from observed LLM behavior from eval:

- Consistently apply a semantic model throughout the graph. It's a comfort to find a concept where it is expected.
- Expose resources in terms of structual kind, like `major` aand `-month`, and desired output, like `-security-disclosures`.

This dual approach to semantic naming sometimes results in this double-mapping. Emperical observation suggests that LLMs prefer the outcome-based naming, while the more schema-correct and initial naming is the structual framing.

Wormholes vs spear-fishing.

note: time is a challenge
