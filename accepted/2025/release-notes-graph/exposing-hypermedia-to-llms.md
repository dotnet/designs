# Exposing Hypermedia Information Graphs to LLMs

[Hypermedia](https://en.wikipedia.org/wiki/Hypermedia) and [hypertext](https://en.wikipedia.org/wiki/Hypertext) are self-describing formats well-suited for LLM consumption. A hypermedia document graph contains sufficient meta-information for a semantic consumer to traverse it and find information demanded by a prompt—without requiring a pre-loaded vector database or a priori schema knowledge.

In a traditional system, a schema is the pre-requisite to traversal; in a hypermedia system, traversal reveals the schema. In a vector system, every query matches against the full corpus; in a hypermedia system, each node reveals the candidates.

## Background

The prevailing narrative has been that _structured data_ > _unstructured documents_ for deriving insight. JSON and XML came out of that heritage, with [JSONPath](https://en.wikipedia.org/wiki/JSONPath) and [XPath](https://en.wikipedia.org/wiki/XPath) providing structured query that relies on a priori schema knowledge. [Hypermedia as the engine of application state (HATEOAS)](https://en.wikipedia.org/wiki/HATEOAS) extended this by labeling relations across resources, not just data within them.

Databases went through a "no-SQL" transition—not a rejection of structure, but a recognition that structure lives in the documents themselves. Hypermedia graphs extend this to "no-schema" consumption: readers discover structure through descriptive labels and traversal rather than requiring it upfront.

## Why hypermedia over vector search?

Vector databases establish relationships via embedding similarity, refined through techniques like [Metadata Extraction Usage Pattern](https://developers.llamaindex.ai/python/framework/module_guides/loading/documents_and_nodes/usage_metadata_extractor/) and [Maximum Marginal Relevance Retrieval](https://developers.llamaindex.ai/python/examples/vector_stores/simpleindexdemommr/). Hypermedia relations are both semantic and structural—equal parts [Retrieval-Augmented Generation (RAG)](https://en.wikipedia.org/wiki/Retrieval-augmented_generation) and [PageRank](https://en.wikipedia.org/wiki/PageRank)—which reduces the need for those techniques.

Hypermedia graphs can be published pre-baked and consumed directly. This enables low-cost LLM enablement for scenarios where hosting a persistent AI server would be prohibitive. An AI server must also manage time-to-live semantics for volatile data—complexity hypermedia graphs avoid.

## Approach

The primary techniques for LLM applicability are:

- Semantic naming: Link relations like "latest-security-disclosure" reduce the inference required to derive meaning.
- Graph-resident guidance: Skills and workflows as first-class relations in the graph. Skills follow the Anthropic skills format; workflows are HAL documents describing queries over link relations.

This approach has been applied to the .NET release notes. The project began as a modernization of JSON files used for cloud-infra deployment and compliance workflows. It became clear that LLMs could read the same content directly and self-reason about navigation. The graph uses [Hypertext Application Language (HAL)](https://en.wikipedia.org/wiki/Hypertext_Application_Language) as its hypermedia foundation, augmented HAL-native with a workflow convention.

## Graph design point

The release notes graph is built on a restrictive premise: the entrypoint should be skeletal and rarely changing, supporting n-9s reliability with rigorous engineering practices (git workflows, peer review, merge gates). But we're in the early days of AI—externally-driven change may require rapid iteration on the entrypoint to maintain LLM enablement quality. These goals are in tension.

The resolution: treat the core graph as a **well-defined data layer** honoring reliability requirements, while exposing a separate **adaptable application layer** entrypoint for LLMs that can evolve without the compatibility burden.

### Design and evaluation

The graph as a whole is based on a somewhat traditional schema design, utilizing both normalized and denormalized approaches in service of consumer queries. After the graph was realized, it was tested with `jq` as a passive and syntactic consumer and with LLMs as an active and semantic consumer. The graph was successively adapted to improve performance for both consumption styles. Performance is primarily measured in terms of terseness of query and quickness (fetches and data cost) of response. Much of the feedback could be considered fundamental in nature. The overall character of the graph remains a pure information-oriented data design, but with a significant tilt towards semantic consumers.

Hypermedia long predates LLMs, but it has always treated semantic consumers (humans) as a key design cohort. This made it easy to adapt the graph based on LLM feedback.

### Patterns from LLM eval

- **Consistency breeds comfort.** It is rewarding and calming to find a concept exposed where it is expected.
- **Trust must be earned for shortcuts.** Links that jump across the graph (wormholes) require LLMs to develop both comprehension and trust. The more valuable the shortcut, the more skeptical the LLM. We observed this with `latest-security-disclosures`—LLMs understood the relation perfectly but repeatedly double-checked its correctness.
- **Dual-map by structure and intent.** A resource can be exposed as `latest-security-month` (structural) and `latest-security-disclosures` (intent). Different prompts bias toward different framings.
- **LLMs batch when strategy is evident.** They will acquire multiple resources in a single turn if the path is clear.
- **LLMs operate on scarcity.** Smaller nodes encourage exploration by signaling that comprehension is outpacing token cost.
- **Differentiate node weight.** The `month` node is heavier than others, making it cheaper to explore the graph before committing to fetch one.

## Performance considerations

Navigating a hypermedia graph requires multiple turns. At each turn, new content is fetched, reasoned about, and used to direct the next fetch or select an answer. The [transformer architecture](https://en.wikipedia.org/wiki/Transformer_(deep_learning)) imposes costs that make multi-turn navigation expensive—much faster than intuition suggests.

[API pricing](https://openai.com/api/pricing/) is listed in terms of 1M tokens. One million tokens may sound like a lot, but doesn't require the complete works of Shakespeare. Straightforward formulas can predict how quickly token counts grow and what that will cost dollar-wise. They demonstrate how little it takes to hit the million token milestone.

It was watching our API balance decay that led us to understand these mechanics—and to design for both right answers and cheap answers. They are not the same thing.

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

- API pricing is in terms of tokens. For multi-turn conversations, the cost is the accumulated token cost not the final context size.
- The cubic growth in attention is the dominant computational cost, the primary contributor to latency and throughput. It emerges from summing quadratic costs across turns. Each turn pays attention on everything accumulated so far. This cost is likely the gating function on context size and expected to be persistent even if GPU memory doubles.
- These costs provide clues on why conversation compacting exists and why there is scrutiny on token economics.

### Batched vs sequential

What if all content could be fetched in a single turn?

| Approach | Total attention cost | Multiplier |
|----------|----------------------|------------|
| Batched (1 turn) | (nm)² = n²m² | 1 |
| Sequential (m turns) | n²m³/3 | m/3 |

Ten turns ≈ 3× batched cost. Thirty turns ≈ 10×. This ratio scales linearly with turn count, the `m` term.

Many problems inherently require multiple turns. The LLM must reason about intermediate results before knowing what to fetch next. The goal is not to eliminate turns but to minimize them and optimize their structure.

### Optimization: lean early, heavy late

> Defer large token loads to later turns to reduce the number of turns that must pay the cost of large token loads.

The uniform model above assumes equal token counts per turn. In practice, token distribution across turns is a design choice with significant cost implications. The tokens in the first turns are by far the most costly.

This is roughly similar to credit card debt: early charges compound. If the initial purchase was large, you're in trouble.

### Optimization: multiple fetches per turn

> Prefer larger token loads per turn to reduce the number of turns overall.

The sequential model assumes one fetch per turn. LLMs can fetch multiple documents in a single turn when aided by intuition or given clear guidance about what to retrieve. This technique tames the rate at which token cost and attention cost accumulates, enabling a cost profile that approaches _batched_ while maintaining a _sequential_ model.

This approach can (to a degree) amortize network costs across multiple async requests.

This optimization may seem in conflict with the earlier optimization, but it isn't. The earlier optimization is about the order of fetches across turns, whereas this optimization is about collapsing turns. They are complementary ideas with no tension.

### Applicability to release notes graph

The strict n-9s reliability design model is perfectly aligned with the LLM cost model. Skeletal roots with heavy leaves and differentiated weight per node enable an LLM to navigate most of the graph at low cost. This mirrors how LLMs naturally separate planning from execution—cheaper exploration, then targeted retrieval.

## LLM entrypoints

We'll now look at the LLM enablement design and start to see how the cost functions influence the approach.

There are two entrypoints provided for LLMs:

- [llms.txt](https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/llms.txt) -- Prose explanation of how to use the graph, including a link to `llms.json`.
- [llms.json](https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/llms.json) -- The LLM index (AKA "application-layer entrypoint for LLMs"). It also includes guidance inline.

### Design process

These formats are the result of many iterations and experiments. Early revisions of `llms.txt` attempted to explain everything in great detail, nearing 500 lines. The current lean approach was arrived at via iterative LLM feedback and observation.

Sidebar: A key design principle emerged from this process: _curiosity-driven evaluation_ > _intuition reliance_. Once you have a good test harness and methodology, it's quite liberating to not trust your intuition but to test all the ideas that occur as interesting. Any bias or distinction between "intuitive and informed direction" and "bat-crazy" drops away. Test both.

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

As stated earlier, `llms.txt` was initially a large document covering multiple skills. That turned out to be very difficult to maintain and adapt. It also forced a minimum token burden on every reader. Skills files provided a solution. Each skill describes a domain, including contextual descriptions, guidance, rules, and workflows. The problem then was how to advertise the skills.

As demonstrated above, about half of `llms.txt` is dedicated to skills routing. It's just a markdown table that provides a key/value structure for skills.

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

This approach was chosen for two reasons:

- Fully Orient guidance on skills and to encourage skill use.
- Enable specializing guidance for the `llms.json` path as compared to starting from `llms.txt`.
- Enable syndicating just the `release-notes` directory to a CDN and not `llms.txt`.

The `SKILL.md` files optionally reference a homegrown `workflows.json` file. These could equally have been exposed from `llms.txt` to enable collapsing a turn. There is a tradeoff between the complexity of the general `llms.txt` and each of the domain-specific skills. Every reader consumes `llms.txt`, while some don't fetch any skills at all, some fetch multiple, and only a subset fetch workflows. It is possible the current design is optimal. This design point hasn't been sufficiently explored to draw a strong conclusion.

### Skills

The skills follow a [skills template](https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/skills/template/SKILL.md) to ensure uniformity and are intended to be as simple as possible to achieve the desired performance as measured by the tests.

The following markdown document is the complete `cve-queries` skill.

```markdown
---
name: cve-queries
description: CVE queries needing severity, CVSS, affected versions, or security history
workflows: https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/skills/cve-queries/workflows.json
---

# CVE Queries

All CVE queries use timeline. Fetch workflows.json for navigation paths with `next_workflow` transitions.

## Stop Criteria

| Need | Stop At | Has |
|------|---------|-----|
| Severity, CVSS, affected versions | month index | `_embedded.disclosures[]` |
| Code diffs, CWE, version ranges | cve.json | different schema—see `next_workflow` |

## Rules

1. Follow `_links` only. Never construct URLs.
2. Year indexes must be fetched sequentially via `prev-year`.
3. Code diffs: `$.commits[key].url` already ends in `.diff` — use as-is. Never fetch commit HTML pages.
```

The other skills are similar.

The `next_workflow` transition leads us to `workflows.json`.

### Workflows

Workflows are a homegrown HAL-inspired and -compatible format. The graph -- as has been sufficiently clarified to this point -- uses HAL as its hypermedia format. HAL is focused on describing resources and relations between them. What if HAL also included a query system? What would it look like? That's what workflows attempt to answer.

The premise behind them is:

- Query as document data
- HAL relations (and other document data) as query targets

The initial design started as `_workflows` within `llms.json`. Similar to using `llms.txt` as the sole source of guidance, this approach got bulky fast. It was then realized that the skill registry/router idea could equally be applied to workflows.

The current design offers workflows as auxiliary skill content. The primary router is the skill router. The skills then offer a workflow, both in terms of listing them and referring to them as part of the skill.

Workflows hang most of their query expressivity on the `follow_path` property. This can be seen in the `cve-latest` workflow in [`cve-queries/workflows.json`](https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/skills/cve-queries/workflows.json).

```json
      "cve-latest": {
        "description": "CVEs from the most recent security release",
        "follow_path": ["kind:llms", "latest-security-disclosures"],
        "destination_kind": "month",
        "yields": {
          "data": "_embedded.disclosures[]",
          "fields": ["id", "title", "cvss_severity", "cvss_score", "affected_releases"]
        },
        "next_workflow": {
          "condition": "code diffs, CWE, or package versions needed",
          "workflow": "cve-extraction",
          "via": "follow _links.cve-json from current month index"
        }
      },
```

As stated, the initial design included workflows inline in core HAL docs. In that case, it's not much of a leap to connect that `latest-security-disclosures` is a reference to the link relation of the same name. It's a much bigger leap when the workflow is in a separate file in a separate location and indirected via the skills registry. This connection is re-gained by making the node kind as the first part of the `follow_path` array.

The intent of the initial compound term -- `kind:llms` -- is to indicate that it is special and of a different kind than the link relations. That seems to work.

The `next_workflow` property enables chaining workflows to develop a sense of [equivalence classes](https://en.wikipedia.org/wiki/Equivalence_class) and to conform with [DRY principle](https://en.wikipedia.org/wiki/Don%27t_repeat_yourself).

Here's an excerpt from the [feedback turn of one of the tests](https://github.com/richlander/release-graph-eval-results/blob/main/anthropic_claude-haiku-4.5/B/T6.md).

> ## 4. Workflow Verification
>
>**Workflow used:** `cve-history` (for 6-month range)
>
>**Specified `follow_path`:**
>```json
>"4+_months_or_cross_year": {
>  "follow_path": ["kind:llms", "latest-year"],
>  "then": "follow _links.prev-year from each year index",
>  "select": "_embedded.months[?security].cve-json links"
>}
>```
>
>**What I actually did:**
>1. ✅ Fetched `llms.json` (kind:llms)
>2. ✅ Fetched `timeline/2025/index.json` (latest-year)
>3. ✅ Fetched `timeline/2024/index.json` (via prev-year link)
>4. ✅ Selected only `_embedded.months[?security=true].cve-json` links
>5. ✅ Fetched 6 cve.json files directly (skipped month indexes)
>
>**Did I deviate?** No, I followed the path exactly. The workflow was precise enough that I didn't need to guess.

The interesting aspect is that the feedback is basically an unpacking of the workflow query. It demonstrates a high level of comprehension of the query and of the overall scheme.

The workflow syntax itself is a bit loose. The core idea is HAL-native query over link relations. Much of the syntax is using "programmy structure" to describe logical operations. It may be useful to standardize them into an actual vocabulary at some point. Given the constantly changing LLM landscape, it likely makes the most sense to validate the strength of the syntax across a year of usage and LLM changes before feeling sufficiently confident about the model.

### Applying workflows for generically

Workflows were developed for LLMs. However, it seems like they are sufficiently descriptive that they could be used by non-semantic consumers. The question was whether a C# program could be written to generate bash scripts from the workflows.

```bash
$ dotnet run -- list ~/git/core/release-notes/skills/cve-queries/workflows.json
  cve-by-version  CVEs affecting a specific .NET version
  cve-details     Go directly to cve.json for full details
  cve-extraction  Extract data from cve.json (different schema than month index)
  cve-history     CVEs over a time range
  cve-latest      CVEs from the most recent security release

Total: 5 workflows
$ dotnet run -- show ~/git/core/release-notes/skills/cve-queries/workflows.json cve-latest
Workflow: cve-latest
Description: CVEs from the most recent security release
Follow path: kind:llms -> latest-security-disclosures
Destination: month
Yields: WorkflowYields { Data = _embedded.disclosures[], Fields = System.Collections.Generic.List`1[System.String], Filter =  }
$ dotnet run -- script ~/git/core/release-notes/ills/cve-queries/workflows.json cve-latest > get-latest-cves.sh
$ chmod +x get-latest-cves.sh
$ ./get-latest-cves.sh
Fetching: https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/llms.json
Fetching: https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/index.json
{
  "$schema": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/schemas/v1/dotnet-release-timeline-index.json",
  "kind": "month",
  "title": ".NET Month Timeline Index - October 2025",
  "year": "2025",
  "month": "10",
  "date": "2025-10-14T00:00:00+00:00",
  "security": true,
  "prev_month_date": "2025-09-09T00:00:00+00:00",
  "prev_security_month_date": "2025-06-10T00:00:00+00:00",
  "cve_records": [
    "CVE-2025-55248",
    "CVE-2025-55315",
    "CVE-2025-55247"
  ],
  "_links": {
    "self": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/index.json"
    },
    "prev-month": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/09/index.json",
      "title": "Previous month - September 2025"
    },
    "prev-security-month": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/06/index.json",
      "title": "Previous security month - June 2025"
    },
    "manifest": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/manifest.json",
      "title": "Manifest - October 2025"
    },
    "timeline": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/index.json",
      "title": ".NET Release Timeline Index"
    },
    "year": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/index.json",
      "title": ".NET Year Timeline Index - 2025"
    },
    "cve-json": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/cve.json",
      "title": "CVE records - October 2025",
      "type": "application/json"
    }
  },
  "_embedded": {
    "patches": {
      "10.0": {
        "version": "10.0.0-rc.2.25502.107",
        "date": "2025-10-14T00:00:00+00:00",
        "year": "2025",
        "month": "10",
        "security": true,
        "support_phase": "go-live",
        "_links": {
          "self": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/10.0.0-rc.2.25502.107/index.json"
          }
        },
        "sdk_version": "10.0.100-rc.2.25502.107"
      },
      "9.0": {
        "version": "9.0.10",
        "date": "2025-10-14T00:00:00+00:00",
        "year": "2025",
        "month": "10",
        "security": true,
        "support_phase": "active",
        "_links": {
          "self": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/9.0.10/index.json"
          }
        },
        "sdk_version": "9.0.306"
      },
      "8.0": {
        "version": "8.0.21",
        "date": "2025-10-14T00:00:00+00:00",
        "year": "2025",
        "month": "10",
        "security": true,
        "support_phase": "active",
        "_links": {
          "self": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/8.0/8.0.21/index.json"
          }
        },
        "sdk_version": "8.0.415"
      }
    },
    "disclosures": [
      {
        "id": "CVE-2025-55248",
        "title": ".NET Information Disclosure Vulnerability",
        "_links": {
          "self": {
            "href": "https://github.com/dotnet/announcements/issues/372"
          }
        },
        "cvss_score": 4.8,
        "cvss_severity": "MEDIUM",
        "disclosure_date": "2025-10-14",
        "affected_releases": [
          "8.0",
          "9.0"
        ],
        "affected_products": [
          "dotnet-runtime"
        ],
        "platforms": [
          "all"
        ]
      },
      {
        "id": "CVE-2025-55315",
        "title": ".NET Security Feature Bypass Vulnerability",
        "_links": {
          "self": {
            "href": "https://github.com/dotnet/announcements/issues/371"
          }
        },
        "cvss_score": 9.9,
        "cvss_severity": "CRITICAL",
        "disclosure_date": "2025-10-14",
        "affected_releases": [
          "8.0",
          "9.0",
          "10.0"
        ],
        "affected_products": [
          "dotnet-aspnetcore"
        ],
        "platforms": [
          "all"
        ]
      },
      {
        "id": "CVE-2025-55247",
        "title": ".NET Denial of Service Vulnerability",
        "_links": {
          "self": {
            "href": "https://github.com/dotnet/announcements/issues/370"
          }
        },
        "cvss_score": 7.3,
        "cvss_severity": "HIGH",
        "disclosure_date": "2025-10-14",
        "affected_releases": [
          "8.0",
          "9.0",
          "10.0"
        ],
        "affected_products": [
          "dotnet-sdk"
        ],
        "affected_packages": [
          "Microsoft.Build",
          "Microsoft.Build.Tasks.Core",
          "Microsoft.Build.Utilities.Core"
        ],
        "platforms": [
          "linux"
        ]
      }
    ]
  }
}
$ ./get-latest-cves.sh | jq ._embedded.disclosures.[].id
Fetching: https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/llms.json
Fetching: https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/index.json
"CVE-2025-55248"
"CVE-2025-55315"
"CVE-2025-55247"
```

This is replaying the same approach as an LLM. Workflows lower the cost of navigating the graph for any semantic consumer. The prototype tool fills in the boiler-plate and could equally be applied to PowerShell or C# output.

There is also replaying the overall approach of proving out formats with semantic and non-semantic consumers. This document led with the idea of proving out the efficacy of the graph with `jq` and LLMs. In this case, `jq` has been replaced with a C# app, but it's the same basic approach. The irony is that `jq` managed to sneak into this use case as well. The tool can be blamed for its remarkable utility.

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

Vector databases take this a step further by making it easier for semantic consumers to find the content they want, for example with [Metadata Extraction Usage Pattern](https://developers.llamaindex.ai/python/framework/module_guides/loading/documents_and_nodes/usage_metadata_extractor/) and [Maximum Marginal Relevance Retrieval](https://developers.llamaindex.ai/python/examples/vector_stores/simpleindexdemommr/).
