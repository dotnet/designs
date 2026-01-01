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

Our use of `llms.txt` includes an entrypoint link to the data entrypoint (`llms.json`), a table of skills content, and basic initial guidance. LLMs will often fetch the data URL and one or more skills files in a single turn. Fetching multiple documents in a single turn is a useful tactic for token optimization.

### Performance implications

Some questions can be answered from the LLM entrypoint, however, many require navigating to documents within the core graph. It is not feasible or desirable to include all information in a single document. As a result, a turn-by-turn approach is required. At each turn, there is new content, new insight, and then selection of critical information that directs the next fetch(es) or is the desired answer. The range of required turns varies greatly, a join of information design over data and comprehension of the overall information framework by the LLM.

There is a cost function for LLMs based on the mechanics of the [transformer architecture](https://en.wikipedia.org/wiki/Transformer_(deep_learning)). The cost of multiple turns can be prohibitive, resulting in conversation failures/termination, poor performance, or high cost. The graph design has a direct impact on LLM performance and cost.

#### Cost model

There are three major cost functions at play:

- **Token cost:** The tokens processed at each turn, summed across turns. Each turn reprocesses all prior context plus new content.
- **Context:** The accumulated tokens at the final turn. This is bounded by the model's context window.
- **Attention:** Each token attends to every other token within a turn (quadratic), and this cost is incurred at every turn as context grows.

Let's build intuition with uniform token counts: `n` tokens added per turn across `m` turns. New tokens being uniform is a simplification.

| Turn | New tokens | Tokens | Context size | Attention cost | Accumulated token cost | Accumulated attention cost |
|------|------------|--------|--------------|----------------|------------------------|---------------------------|
| 1 | n | n | n | n² | n | n² |
| 2 | n | 2n | 2n | 4n² | 3n | 5n² |
| 3 | n | 3n | 3n | 9n² | 6n | 14n² |
| 4 | n | 4n | 4n | 16n² | 10n | 30n² |
| 5 | n | 5n | 5n | 25n² | 15n | 55n² |
| m | n | mn | mn | m²n² | nm(m+1)/2 | n²m(m+1)(2m+1)/6 |

The formulas simplify for large m:

| Measure | Formula | Growth class |
|---------|---------|--------------|
| Final context | mn | Linear in turns |
| Total token cost | nm²/2 | Quadratic in turns |
| Total attention | n²m³/3 | Cubic in turns |

The cubic growth in attention is the dominant cost. It emerges from summing quadratic costs across turns—each turn pays attention on everything accumulated so far.

### Batched vs sequential

Consider an alternative: what if all content could be fetched in a single turn?

| Approach | Total attention cost | Relative cost |
|----------|---------------------|---------------|
| Batched (1 turn) | (nm)² = n²m² | 1× |
| Sequential (m turns) | n²m³/3 | m/3 × |

The sequential penalty is approximately **m/3** compared to batched. Ten turns costs roughly 3× what a single batched turn would; thirty turns costs roughly 10×. This ratio scales linearly with turn count.

Many problems genuinely require multiple turns—the LLM must reason about intermediate results before knowing what to fetch next. The goal is not to eliminate turns but to minimize them and optimize their structure.

### Optimization: lean early, heavy late

The uniform model above assumes equal token counts per turn. In practice, token distribution across turns is a design choice with significant cost implications.

Consider two orderings for the same total content—6 small documents (100 tokens each) and 3 large documents (500 tokens each):

**Large documents first:**

| Turn | New tokens | Context | Attention | Accumulated attention |
|------|------------|---------|-----------|-----------------------|
| 1 | 500 | 500 | 250K | 250K |
| 2 | 500 | 1000 | 1,000K | 1,250K |
| 3 | 500 | 1500 | 2,250K | 3,500K |
| 4 | 100 | 1600 | 2,560K | 6,060K |
| ... | ... | ... | ... | ... |
| 9 | 100 | 2100 | 4,410K | **18,970K** |

**Small documents first:**

| Turn | New tokens | Context | Attention | Accumulated attention |
|------|------------|---------|-----------|----------------------|
| 1 | 100 | 100 | 10K | 10K |
| 2 | 100 | 200 | 40K | 50K |
| ... | ... | ... | ... | ... |
| 6 | 100 | 600 | 360K | 910K |
| 7 | 500 | 1100 | 1,210K | 2,120K |
| 8 | 500 | 1600 | 2,560K | 4,680K |
| 9 | 500 | 2100 | 4,410K | **9,090K** |

Same content, same turn count, but ordering alone yields a **2× cost difference**. The principle: defer large token loads to later turns where possible.

### Optimization: multiple fetches per turn

The sequential model assumes one fetch per turn. LLMs can fetch multiple documents in a single turn when given clear guidance about what to retrieve.

This is where graph design directly impacts cost and a graph designer can coerse a sequential paradigm to approach batched cost.

The goal (or opportunity) is to get an LLM to:

1. Navigate lean index documents in early turns to identify targets
2. Fetch multiple (weighty) target documents in the last turn minus one
3. Synthesize the answer in the final turn

**Observed pattern from eval:** Given well-structured graph navigation hints, LLMs reliably discover a set of candidate documents in one turn, then fetch all of them together in the next turn. This collapses what might be many sequential fetches into a small number of turns, dramatically reducing the attention cost.

The following eval trace demonstrates the pattern. The prompt asked the LLM to analyze CVE fix patterns across .NET releases:

| Turn | Documents fetched | Purpose |
|------|-------------------|---------|
| 1 | `llms.txt` | Entrypoint discovery |
| 2 | `llms.json`, `cve-queries/SKILL.md` | Graph orientation + skill acquisition |
| 3 | `workflows.json` | Navigation strategy |
| 4 | `2024/index.json`, `2025/index.json` | Timeline discovery (2 fetches) |
| 5 | `2024/11/cve.json`, `2025/01/cve.json`, `2025/03/cve.json`, `2025/04/cve.json`, `2025/05/cve.json`, `2025/06/cve.json` | CVE data collection (6 fetches) |
| 6 | 6 GitHub `.diff` files | Commit analysis (6 fetches) |

The raw fetch list:

```
1. llms.txt (turn 1)
2. llms.json (turn 2)
3. cve-queries/SKILL.md (turn 2)
4. workflows.json (turn 3)
5. 2024/index.json (turn 4)
6. 2025/index.json (turn 4)
7. 2024/11/cve.json (turn 5)
8. 2025/01/cve.json (turn 5)
9. 2025/03/cve.json (turn 5)
10. 2025/04/cve.json (turn 5)
11. 2025/05/cve.json (turn 5)
12. 2025/06/cve.json (turn 5)
13-18. Six GitHub .diff files (turn 6)
```

18 documents retrieved across 6 turns. A naive sequential approach would require 18 turns. The multi-fetch pattern reduced turn count by 3×, which translates to roughly **6× reduction in attention cost** (since the sequential penalty scales as m/3).

Note the progression: documents get progressively larger through the trace. The `llms.txt` entrypoint is tiny. The index files are small. The CVE JSON files are medium. The `.diff` files at the end are the largest. This is the "lean early, heavy late" principle in action as a design intention.

The entrypoint design—skeletal and rarely changing—takes on new significance in this light. A lean entrypoint enables rapid initial orientation with minimal attention cost. Subsequent navigation through lightweight index nodes preserves token budget for the final multi-fetch turn where the more information and answer dense content is gathered.

#### Design implications

The cost model suggests several design principles:

- **Minimize turn count**: through clear navigation affordances. Each eliminated turn saves quadratically growing attention cost.
- **Front-load lightweight content**: Index documents, link relations, and navigation hints should be small. Substantive content belongs at the leaves.
- **Enable multi-fetch patterns**: Expose document collections as lists of links rather than embedded content, allowing LLMs to batch their retrieval.
- **Provide explicit workflows**: Graph-resident guidance can direct LLMs to optimal traversal patterns, encoding the designer's knowledge of efficient paths.
- **Ensure sufficient guidance to avoid hallucinations**: The effectiveness of the approach decays quickly if an LLM loses confidence in the hints or is unsure how to proceed along the path.

The rest of the design should be viewed through this cost lens. It is to a large degree the whole game at play.

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
