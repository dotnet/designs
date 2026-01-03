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

## Graph design point

The release notes information graph is based on the restrictive idea that the entrypoint of the graph should be skeletal and rarely changing. That's workable for LLMs but not ideal. The motivation for the restrictive approach is that it should support an n-9s level of reliability and be subject to rigorous engineering practices (git workflows, peer review, merge gates). However, we're in the early days of AI and subject to repeated waves of externally-driven change that may require significant and quick re-evaluation and re-work of the entrypoint to maintain high-quality LLM enablement. These modalities are in firm opposition.

We can instead view the core graph as a well-defined data-layer that honors the desired reliability requirements, while exposing a separate application-layer entrypoint for LLMs that can evolve over time without the heavy compatibility burden.

The graph as a whole is based on a somewhat traditional schema design, utilizing both normalized and denormalized approaches in (hopefully) informed service of consumers. After the graph was realized, it became possible to test it with `jq` as a sort of passive and syntactic consumer and with LLMs as a much more active and semantic consumer. The graph was successively and successfully adapted to improve performance for both consumption styles. Performance is primarily measured in terms of terseness of query and quickness (fetches and data cost) of response. Much of the feedback could be considered fundamental in nature. The overall character of the graph remains a pure information-oriented data design, but with a significant tilt towards semantic consumers.

The choice of hypermedia as the grounding format is a case-in-point of the overall approach. Hypermedia long pre-dates LLMs, however, it has always held semantic consumers (humans) as a key design cohort. Hypermedia formats provide a conceptual framework that is easy to flavor towards semantic consumption. This flexibility proved useful as the design was adapted with LLM feedback. It should also be noted that LLM feedback is by far the cheapest and most accessible form of feedback. LLMs are happy to provide usage feedback in response to iterative adaptation and at any time of day or night.

A few strong design principles emerged from observed LLM behavior from eval:

- Consistent application of a conceptual model creates familiarity for semantic consumers. It is a comfort to find a concept exposed where it is expected.
- It is possible to expose links that jump from one part of the graph to another, like a wormhole. LLMs seem to need to develop _comprehension_ and _trust_ as a pre-requisite for relying on them. The more attractive the wormhole link, the more the LLM may be skeptical. This was observed most with the `latest-security-disclosures` relation since it provides high value and because the it has an inherent half-life.
- Resources can be dual-mapped in terms of structural kind, like `latest-security-month`, and desired output, like `latest-security-disclosures`. A given prompt may bias towards different concerns. Differentiated mappings are more likely to present a similar match to semantic consumers.
- LLMs will acquire multiple resources in a single turn if a good strategy for doing so is evident.
- LLMs operate on a model of scarcity, with tokens at a premium. Smaller graph nodes encourage greater graph navigation by creating a sense that growing comprehension is outstripping consumption cost.
- Differentiating token cost by category of nodes makes it cheaper for LLMs to navigate a large graph. The `month` node with the graph is weightier than all other nodes making it easier to develop an exploration plan among other nodes before making a final decision on which month(s) to read or to skip months altogether and to prefer to exit the graph (with a graph exit link), for example, to read our monthly `cve.json` files.

### LLM entrypoints

There are two entrypoints provided for LLMs:

- [llms.txt](https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/llms.txt) -- Prose explanation of how to use the graph, including a link to llms.json.
- [llms.json](https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/llms.json) -- The LLM index (AKA "application-layer entrypoint for LLMs"). It also includes guidance inline.

[llms.txt](https://llmstxt.org/) is an emerging standard, with awareness in the most recently trained LLMs. It can be used for meta-information (as is the case in this system) or to expose an index of all information available (as is the case with [Stripe docs](https://docs.stripe.com/llms.txt)). It's hard to imagine that the Stripe approach is optimal. It uses 18.5k tokens (10% of a typical token budget) while our use of `llms.txt` clocks in at a meager 609 tokens.

A major advantage of `llms.txt` is that it is markdown, which offers a natural way to expose resource links, guidance, and foreign content (code fences). It is possible to include all the same information in JSON, however, it is awkward and (critically) unconventional. It takes a lot more design effort to get an LLM to notice and apply guidance from within a data-oriented JSON document than markdown, which has a much stronger association with guidance and multi-modality information.

Our use of `llms.txt` includes an entrypoint link to the data entrypoint (`llms.json`), a table of skills content, and basic initial guidance. LLMs will often fetch the data URL and one or more skills files in a single turn. Fetching multiple documents in a single turn is a useful tactic for token optimization.

## Performance considerations

Some questions can be answered from the LLM entrypoint, however, many require navigating to documents within the core graph. It is not feasible or desirable to include all information in a single document. As a result, a turn-by-turn approach is required. At each turn, there is new content, new insight, and then selection of critical information that directs the next fetch(es) or is the desired final answer. The range of required turns varies greatly, a join of information design over the body of data and comprehension of the overall information framework by the LLM.

There is a cost function for LLMs based on the mechanics of the [transformer architecture](https://en.wikipedia.org/wiki/Transformer_(deep_learning)). The graph design has a direct impact on LLM performance and cost. Multiple turns accelerate costs extremely quickly, much faster than intuition would suggest.

### Cost model

There are three major cost functions at play:

- **Token cost:** The tokens processed at each turn, summed across turns. Each turn reprocesses all prior context plus new content.
- **Context:** The accumulated tokens at the final turn (could equally be called "terminal turn" if there is a "context overflow"). This is bounded by the model's context window.
- **Attention:** Each token attends to every other token within a turn (quadratic), and this cost is incurred at every turn as context grows.

Let's build intuition using uniform token counts: `n` tokens added per turn across `m` turns.

| Turn | New tokens | Conversation tokens | Context size | Accumulated token cost | Attention cost | Accumulated attention cost |
|------|------------|---------------------|--------------|------------------------|----------------|---------------------------|
| 1 | n | n | n | n | n² | n² |
| 2 | n | 2n | 2n | 3n | 4n² | 5n² |
| 3 | n | 3n | 3n | 6n | 9n² | 14n² |
| 4 | n | 4n | 4n | 10n | 16n² | 30n² |
| 5 | n | 5n | 5n | 15n | 25n² | 55n² |
| m | n | mn | mn | nm(m+1)/2 | m²n² | n²m(m+1)(2m+1)/6 |

The formulas simplify for large m:

| Measure | Formula | Growth class |
|---------|---------|--------------|
| Final context | mn | Linear in turns |
| Accumulated token cost | nm²/2 | Quadratic in turns |
| Accumulated attention | n²m³/3 | Cubic in turns |

More context on cost:

- API pricing is in term if tokens. For a multi-turn conversation, the cost is the accumulated token cost not the final context.
- The cubic growth in attention is the dominant computational cost. It emerges from summing quadratic costs across turns—each turn pays attention on everything accumulated so far.

### Batched vs sequential

Consider an alternative for the attention cost: what if all content could be fetched in a single turn?

| Approach | Total attention cost | Relative cost |
|----------|---------------------|---------------|
| Batched (1 turn) | (nm)² = n²m² | 1× |
| Sequential (m turns) | n²m³/3 | m/3 × |

The sequential penalty is approximately **m/3** compared to batched. Ten turns costs roughly 3× what a single batched turn would; thirty turns costs roughly 10×. This ratio scales linearly with turn count.

Many problems genuinely require multiple turns—the LLM must reason about intermediate results before knowing what to fetch next. The goal is not to eliminate turns but to minimize them and optimize their structure.

### Optimization: lean early, heavy late

The uniform model above assumes equal token counts per turn. In practice, token distribution across turns is a design choice with significant cost implications. The principle: defer large token loads to later turns where possible to reduce the number of turns that must pay the cost of large token loads.

### Optimization: multiple fetches per turn

The sequential model assumes one fetch per turn. LLMs can fetch multiple documents in a single turn when given clear guidance about what to retrieve. This approach tames the rate at which the attention cost accumulates, enabling a cost profiles that approaches batches while maintaining a sequence. The principle: prefer larger token loads per turn to reduce the number of turns overall.

### Concrete application

LLM eval of the graph demonstrates that effective design can result in optimal behavior.

Observed pattern:

1. Navigate lean index documents in early turns to identify graph paths
1. Fetch multiple graph documents in middle turns to parallelize multiple navigation paths
1. Fetch multiple information-dense documents in later/last turns to inform final answer
1. Synthesize the answer in the final turn

The following eval trace demonstrates this behavior. The prompt asked the LLM to analyze CVE fix patterns across .NET releases:

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
  workflows.json                            1,146 tokens

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
  dotnet/runtime d16f41a.diff              37,425 tokens
  dotnet/runtime 9da8c6a.diff               1,781 tokens
  dotnet/runtime 89ef51c.diff                 260 tokens
  dotnet/aspnetcore 67f3b04.diff            1,669 tokens
  dotnet/aspnetcore d6605eb.diff           15,388 tokens
  dotnet/runtime b33d4e3.diff               3,309 tokens
```

> Note: The eval harness truncated `.diff` files to 50 lines to ensure test completion across all configurations. The token counts above reflect actual document sizes—what a reader would encounter following the [published guidance](https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/llms.txt).

18 documents retrieved across 6 turns. A naive sequential approach would require 18 turns. The multi-fetch pattern reduced turn count by 3×, which translates to roughly **6× reduction in attention cost** (since the sequential penalty scales as m/3).

The entrypoint design—skeletal and rarely changing—takes on new significance in this light. A lean entrypoint enables rapid initial orientation with  minimal attention cost. Subsequent navigation through lightweight index nodes preserves token budget for the final multi-fetch turn where substantive content is gathered.

### Design implications

The cost model suggests several design principles:

- **Minimize turn count** through clear navigation affordance. Each eliminated turn saves quadratically growing attention cost.
- **Front-load lightweight content.** Index documents, link relations, and navigation hints should be small. Substantive content belongs at the leaves.
- **Enable multi-fetch patterns.** Expose document collections as lists of links rather than embedded content, encouraging LLMs to batch their retrieval.
- **Provide explicit workflows.** Graph-resident guidance can direct LLMs to optimal traversal patterns, encoding the designer's knowledge of efficient paths.

The rest of the design should be viewed through this cost lens. As an application designer, there are only so many degrees of freedom. We cannot change LLM fundamentals but need to work within their constraints. To a large degree, optimizations like reducing turns are similar to loop variable hoisting. While LLMs are new and different, old school performance strategies remain effective.

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
