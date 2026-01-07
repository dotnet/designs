# Exposing Hypermedia Information Graphs to LLMs

[Hypermedia](https://en.wikipedia.org/wiki/Hypermedia) and [hypertext](https://en.wikipedia.org/wiki/Hypertext) are decades-old formats perfectly suited for LLM consumption by virtue of self-describing structure and labeled relationships between resources. A hypermedia document graph contains sufficient meta-information for a semantic consumer to traverse it and find information demanded by a prompt—without requiring a pre-loaded vector database or a priori schema knowledge. This makes hypermedia a lightweight alternative to vector databases: pre-baked static publishing with no specialized infrastructure, using semantic naming, graph-resident guidance, and cost-aware node weighting to achieve comparable LLM enablement. These techniques generalize to any domain where document relationships are meaningful and navigable.

> A (nearly) century-old principle, articulated by [Korzybski](https://en.wikipedia.org/wiki/Alfred_Korzybski): [the map is not the territory](https://en.wikipedia.org/wiki/Map%E2%80%93territory_relation).

[HTML](https://en.wikipedia.org/wiki/HTML) is perhaps the least sophisticated hypertext implementation in common use. A typical example: `For more on this vulnerability, <a href="cve-2025-1234.html">click here</a>`. "click here" [doesn't provide much of a map](https://developers.google.com/search/docs/crawling-indexing/links-crawlable#anchor-text-placement) for a semantic consumer.

In trail races, ribbons hang from trees and arrows mark the ground to keep runners on course. Where routes diverge, signs read "5 km → left" and "10 km → straight". The ribbons are the map—schema-driven correctness. The signposts are [Hypermedia as the engine of application state (HATEOAS)](https://en.wikipedia.org/wiki/HATEOAS)-like descriptive navigation. Signposting is a key-value function: you match a key you recognize with a value you need to stay on course.

A semantic graph exposes named relations like `{ "link-relation": "security-disclosure", "href": "..." }`. Greater sophistication can be achieved by describing the target kind: `"link-relation": "disclosure"` and `"target-kind": "cve-record"`. A strong semantic implementation shines a light on the path to follow and describes what it will reveal.

In a traditional system, a schema is the pre-requisite to traversal; in a hypermedia system, traversal reveals the schema. The map and the territory can be thought to coincide.

## Background

The prevailing narrative has been that _structured data_ > _unstructured documents_ for deriving insight. JSON and XML came out of that heritage, with [JSONPath](https://en.wikipedia.org/wiki/JSONPath) and [XPath](https://en.wikipedia.org/wiki/XPath) providing structured query that relies on a priori schema knowledge. This paradigm relates to data within documents.

Databases went through a "no-SQL" transition—not a rejection of structure, but a recognition that structure lives in the documents themselves. The same structured document query layers on top. This paradigm relates to grouping documents with extra metadata, like sales orders from a customer in Seattle or Toronto.

Vector databases establish relationships via embedding similarity, refined through techniques like [Metadata Extraction Usage Pattern](https://developers.llamaindex.ai/python/framework/module_guides/loading/documents_and_nodes/usage_metadata_extractor/) and [Maximum Marginal Relevance Retrieval](https://developers.llamaindex.ai/python/examples/vector_stores/simpleindexdemommr/). This paradigm changes the narrative to _meaning_ > _semi-arbitrary text strings_.

Hypermedia advocates for self-describing document structures. HATEOAS contributes the idea that labeled relations across resources enable semantic navigation. This idea can be stretched to being thought of as "no-schema" consumption: readers discover structure through descriptive labels and follow matching paths without requiring domain knowledge upfront. This paradigm is both semantic and structural, equal parts [Retrieval-Augmented Generation (RAG)](https://en.wikipedia.org/wiki/Retrieval-augmented_generation) and [PageRank](https://en.wikipedia.org/wiki/PageRank). It's this mixture that enables direct consumption without the need for vector techniques, enabling low-cost LLM enablement for scenarios where hosting a persistent AI server would be prohibitive.

## User experience

This is the intended user experience:

>I last updated .NET 8 in September 2025. Were there any critical CVEs since then? Tell me what they fixed so I can decide if I have an issue. Look at code diffs.
>
>Start here: https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/llms.txt

## Approach

The primary techniques for LLM applicability are:

- Semantic naming: Link relations like "latest-security-disclosure" reduce the inference required to derive meaning.
- Graph-resident guidance: Skills and workflows as first-class relations in the graph. Skills follow the Anthropic skills format; workflows are HAL documents describing queries over link relations.

This approach has been applied to the .NET release notes. The project began as a modernization of JSON files used for cloud-infra deployment and compliance workflows. It became clear that LLMs could read the same content directly and self-reason about navigation. The graph uses [Hypertext Application Language (HAL)](https://en.wikipedia.org/wiki/Hypertext_Application_Language) as its hypermedia foundation, augmented with a workflow convention.

## Graph design point

The release notes graph is built on a restrictive premise: the entrypoint should be skeletal and rarely changing, supporting n-9s reliability with rigorous engineering practices (git workflows, peer review, merge gates). But we're in the early days of AI; externally-driven change may require rapid iteration on the entrypoint to maintain LLM enablement quality. These goals are in tension.

The resolution: treat the core graph as a **well-defined data layer** honoring reliability requirements, while exposing a separate **adaptable application layer** entrypoint for LLMs that can evolve without the compatibility burden. We will rely on documentation and file naming to articulate this difference with an explicitly communicated contract (or lack thereof).

### Design and evaluation

The graph as a whole is based on a somewhat traditional schema design, utilizing both normalized and denormalized approaches in service of ergonomic consumer queries. After the graph was realized, it was tested with `jq` as a passive and syntactic consumer and with LLMs as an active and semantic consumer. The graph was successively adapted to improve performance for both consumption styles. Performance is primarily measured in terms of terseness of query and quickness (fetches and data cost) of response. Much of the feedback could be considered fundamental in nature. The overall character of the graph remains a pure information-oriented data design, but with a significant tilt towards semantic consumers.

Hypermedia long predates LLMs, but it has always treated semantic consumers (humans) as a key design cohort. This made it easy to adapt the graph based on LLM feedback.

### Patterns from LLM eval

- **Consistency breeds comfort.** It is rewarding and calming to find a concept exposed where it is expected.
- **Trust must be earned for shortcuts.** Links that jump across the graph (wormholes) require LLMs to develop both comprehension and trust. The more valuable the shortcut, the more skeptical the LLM. We observed this with `latest-security-disclosures`—LLMs understood the relation perfectly but had a tendency to double-check correctness.
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
- **Context**: Sum of tokens fetched so far (window size this turn)
- **Cumulative Tokens**: Accumulation of context per turn (your API bill—each turn charges for full context)
- **Attention**: Computational cost this turn, proportional to Context²
- **Cumulative Attention**: Accumulation of attention cost per turn (your computational cost / latency)

The formulas simplify for large m:

| Measure | Formula | Growth class |
|---------|---------|--------------|
| Final context | mn | Linear in turns |
| Accumulated tokens | nm²/2 | Quadratic in turns |
| Accumulated attention | n²m³/3 | Cubic in turns |

- API pricing is in terms of tokens. For multi-turn conversations, the cost is the accumulated token cost not the final context size.
- The cubic growth in attention is the dominant computational cost, the primary contributor to latency. It emerges from summing quadratic costs across turns. Each turn pays attention on everything accumulated so far. This cost is likely the gating function on context size and expected to be persistent even if GPU memory doubles.
- These costs provide clues on why conversation compacting exists and why there is scrutiny on token economics.

### Batched vs sequential

What if all content could be fetched in a single turn?

| Approach | Total attention cost | Multiplier |
|----------|----------------------|------------|
| Batched (1 turn) | (nm)² = n²m² | 1 |
| Sequential (m turns) | n²m³/3 | m/3 |

- Ten turns ≈ 3× batched cost. Thirty turns ≈ 10×.
- This ratio scales linearly with turn count, the `m` term.

Many problems inherently require multiple turns. The LLM must reason about intermediate results before knowing what to fetch next. The goal is not to eliminate turns but to minimize them and optimize their structure.

### Optimization: lean early, heavy late

> Defer large token loads to later turns to reduce the number of turns that must pay the cost of large token loads.

The uniform model above assumes equal token counts per turn. In practice, token distribution across turns is a design choice with significant cost implications. The tokens in the first turns are by far the most costly.

This is roughly similar to credit card debt: early charges compound. If the initial purchase was large and months ago, you're in trouble.

### Optimization: multiple fetches per turn

> Prefer larger token loads per turn to reduce the number of turns overall.

The sequential model assumes one fetch per turn. LLMs can fetch multiple documents in a single turn when aided by intuition or given clear guidance about what to retrieve. This technique tames the rate at which token cost and attention cost accumulates, enabling a cost profile that approaches _batched_ while maintaining a _sequential_ model.

This approach can (to a degree) amortize network costs across multiple async requests.

This optimization may seem in conflict with the earlier optimization, but it isn't. The earlier optimization is about the order of fetches across turns, whereas this optimization is about collapsing turns. They are complementary ideas with no tension.

### Applicability to release notes graph

The strict n-9s reliability design model is perfectly aligned with the LLM cost model. Skeletal roots with heavy leaves and differentiated weight per node enable an LLM to navigate most of the graph at low cost. This mirrors how LLMs naturally separate planning from execution—cheaper exploration, then targeted retrieval.

## Implementation

The release notes graph has two LLM entrypoints, a guidance system built on skills and workflows, and an overall graph design shaped by iterative evaluation. This section covers the artifacts, the methodology that produced them, and the patterns that emerged.

### Entrypoints

- [`llms.txt`](https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/llms.txt) — A markdown file (~600 tokens) that contextualizes the graph and routes to the graph and skills. Markdown is a natural fit: LLMs readily treat it as instructional content, and it offers native syntax for links, tables, and code fences.
- [`llms.json`](https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/llms.json) — A JSON file (~2k tokens) that serves both as instructional entrypoint and data. It embeds enough information to answer common queries directly while offering links into the graph.

### Why both?

[`llms.txt`](https://llmstxt.org/) is an emerging standard with awareness in recently trained LLMs. It can serve as meta-information (as here) or as a comprehensive index (as with [Stripe docs](https://docs.stripe.com/llms.txt) at 18.5k tokens or [Claude Code docs](https://code.claude.com/docs/llms.txt) at ~2.5k tokens). Our `llms.txt` clocks in at 609 tokens—a router, not an index.

Markdown offers natural syntax for links, guidance, and code fences. JSON can carry the same information but lacks markdown's instructional connotations. `llms.json` emerged from experimentation: could JSON achieve the same LLM enablement without a markdown on-ramp?

The answer: yes. Guidance must be more explicitly signaled:

```json
{
  "ai_note": "ALWAYS read required_pre_read first...",
  "required_pre_read": "https://...SKILL.md"
}
```

The imperative "ALWAYS" and the self-describing property name `required_pre_read` compensate for JSON's weaker association with guidance.

In evaluation, both entrypoints achieved similar accuracy. `llms.txt` biased toward over-fetching skills; `llms.json` biased toward under-fetching. The tradeoffs:

| Entrypoint | Tokens | Strength | Weakness |
|------------|--------|----------|----------|
| llms.txt | ~600 | Natural guidance format, aggressive skill routing | May over-fetch skills |
| llms.json | ~2k | Embedded data answers queries directly | Guidance less salient |

Maintaining both also enables syndication: the `release-notes` directory can be served from a CDN without `llms.txt`.

### Link design patterns

**Wormhole links** jump across the graph—from a patch version to its release month, skipping intermediate navigation. `latest-lts-major` teleports to the current LTS release.

**Spear-fishing links** target timely, high-value content deep in the graph. `latest-cve-json` points directly to CVE records with a short half-life, where freshness defines value.

Half the link relations in `llms.json` are `latest-*`, reflecting the belief that most queries start from current state.

**Semantic aliasing**: The same resource can have multiple relations:

- `latest-security-disclosures` — for "what are the latest CVEs?"
- `latest-security-month` — for "what happened in October?"

Same destination, different semantic intent. This implements the principle: match a key you know with a value you don't.

**Focal lengths**: The core index (`index.json`) is zoomed-out and normalized—all .NET versions over ten years. The LLM index (`llms.json`) is zoomed-in and denormalized—current supported state with enough data to answer queries directly. The graph applies multiple focal lengths throughout, separating skeletal navigation nodes from weighted content nodes.

### Guidance architecture

Guidance was the hardest part of the graph to develop. It was relatively easy to generate a graph intuitive for LLMs to navigate without guidance. The remaining problem: augmenting that intuition to aid long-tail navigation that tended to underperform including hallucination. The process of developing this guidance was unintuitive for the graph designer. There are aspects of LLM behavior that do not match human expectation—this needs to be understood.

#### Preamble prompt

The test harness uses this system prompt:

> You have access to a 'fetch' tool that retrieves content from URLs. Use it to navigate the .NET release metadata graph.
> Today's date is December 26, 2025. Use this to calculate time windows like "last 3 months".
> Your first order of business should be to look for skill files or documentation in the graph. Reading these first prevents wrong turns—they contain navigation patterns and query shortcuts built up through trial and error. It's worth the extra fetch.
> Start by fetching: `https://.../llms.json`

This mirrors Claude.ai's actual system prompt, which emphasizes reading skill files before acting:

> We've found that Claude's efforts are greatly aided by reading the documentation available in the skill BEFORE writing any code... Please invest the extra effort to read the appropriate SKILL.md file before jumping in—it's worth it!

The alignment is intentional. Testing without a preamble produced worse results than observed in claude.ai. All production apps have system prompts; testing without one isn't a useful baseline.

#### Skills

Early revisions of `llms.txt` attempted comprehensive guidance in a single document, approaching 500 lines. This was hard to maintain and imposed a minimum token burden on every reader.

Skills provide the solution: domain-specific documents covering context, rules, and workflows. The entrypoint becomes a router:

```markdown
| Query About | Skill |
|-------------|-------|
| CVEs, security patches, CVSS | [cve-queries](...) |
| Breaking changes, compatibility | [breaking-changes](...) |
| Version lifecycle, EOL dates | [version-eol](...) |
| General queries, unsure | [dotnet-releases](...) |
```

Skills follow a [template](https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/skills/template/SKILL.md) for uniformity. Here's the complete `cve-queries` skill:

```markdown
---
name: cve-queries
description: CVE queries needing severity, CVSS, affected versions, or security history
workflows: https://.../skills/cve-queries/workflows.json
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
3. Code diffs: `$.commits[key].url` already ends in `.diff`—use as-is.
```

The skills bias to terse, with observed performance in testing earning expansion.

#### Workflows

Workflows extend HAL with a query system. The premise: queries as document data, with HAL relations as query targets.

The `follow_path` property carries most of the expressivity:

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
    "workflow": "cve-extraction"
  }
}
```

The `kind:llms` prefix anchors the path to a node type, reconnecting the workflow to the graph even though it lives in a separate file. The `next_workflow` property enables chaining, supporting [equivalence class](https://en.wikipedia.org/wiki/Equivalence_class) identification and [Don't Repeat Yourself (DRY)](https://en.wikipedia.org/wiki/Don%27t_repeat_yourself) benefits.

### Evaluation

A key principle emerged: _curiosity-driven evaluation_ > _intuition reliance_. Once you have a good test harness, it's liberating to not trust your intuition but to test any idea that seems interesting. The distinction between "informed direction" and "crazy idea" drops away. Test both.

#### Test modes

| Mode | Entrypoint | Guidance | Purpose |
|------|------------|----------|---------|
| A | llms.json | Yes | JSON with AI hints |
| B | llms.txt | Yes | Markdown on-ramp |
| D | index.json | No | Data-layer baseline |
| D2 | llms2.json | No | JSON without hints |

Modes without guidance (D, D2) hallucinated on roughly 1/3 of questions.

#### Model targeting

Testing used Claude Haiku 3.5 as the primary target, spot-checked against Sonnet, Opus, and OpenAI models. Haiku-targeted guidance didn't degrade sophisticated models—it just added unnecessary tokens. Haiku was also cheaper, a reward for using it as lowest-common-denominator.

#### Observed dynamics

**Complaints vs errors.** Wrong answers are errors; complaints are warnings. We addressed complaints even without errors, treating them as directional signals.

**Guidance scope.** Performance increased as guidance became smaller and more focused. But LLMs complained when guidance targeted only edge cases. Resolution: broad coverage, variable depth. LLMs don't read everything, so they don't notice the imbalance.

> **LLM feedback:**
> "By providing comprehensive guidance that covers both the 75% (where I'd mostly succeed anyway) and the 15-25% (where I'd fail), you've eliminated that anxiety. The guidance isn't overhead on common cases; it's consistency assurance."

**Tilting vs scripting.** Strict guidance can straight-jacket LLMs into bad behavior. The strategy that emerged: lightly tilt natural LLM behavior rather than scripting every step. This performs well and fights hallucination—likely because it requires less "thinking."

#### Evidence: JSON-resident guidance

[Claude.ai's thinking log](https://claude.ai/share/96775d4b-76a9-4ba2-b5f3-dd3badb20cdb) when given `llms.json`:

> The file I fetched shows the current state of supported .NET releases. Looking at the data, I can see that .NET 6.0 is not in the list of supported_major_releases (which shows only 10.0, 9.0, and 8.0). This means .NET 6 is EOL.
>
> The file mentions there's a SKILL.md file that I should read first per the AI note. Let me fetch that to get more complete information...

When asked what motivated it to notice and follow these AI hints:

> **1. System Prompt Priming.** My instructions emphasize using skills before solving tasks. So when I saw the JSON had a `required_pre_read` field, it activated that pattern recognition—_this is a skill pointer, pay attention._
>
> **2. Signal of Intentional Design.** Fields like `ai_note`, `required_pre_read`, and `follow_path` aren't random. They signal that someone engineered this system _specifically for AI consumption_. That makes me trust the guidance. The note didn't just suggest following links—it said "Trust _embedded data—it's authoritative and current," which is basically the architect saying "I've done the hard work, use my structure."
>
> **3. The "ALWAYS" Flag.** The `ai_note` wasn't subtle: "ALWAYS read required_pre_read first." All caps. That's a red flag (or rather, a red instruction) that elevated it above background text.
>
> **4. It Actually Worked.** Following the hints gave me a clean, logical path. The skill routing sent me to the right specialist knowledge. The stop criteria told me when to stop fetching.
>
> **But here's the honest part:** Without those hints embedded in the data, I might have flailed around. I might have assumed .NET 6.0 was in the supported list since the index showed it. I could have tried to construct URLs. The hints prevented those mistakes.
>
> So the motivation wasn't "AI hints are always right"—it was more pragmatic: **follow structured guidance when it's clearly intentional and makes logical sense.**

#### Evidence: Workflow comprehension

From a feedback turn on the `cve-history` workflow:

> **Workflow used:** `cve-history` (for 6-month range)
>
> **What I actually did:**
>
> 1. ✅ Fetched `llms.json` (kind:llms)
> 2. ✅ Fetched `timeline/2025/index.json` (latest-year)
> 3. ✅ Fetched `timeline/2024/index.json` (via prev-year link)
> 4. ✅ Selected only `_embedded.months[?security=true].cve-json` links
> 5. ✅ Fetched 6 cve.json files directly (skipped month indexes)
>
> **Did I deviate?** No, I followed the path exactly.

### Cross-consumer applicability

Workflows were designed for LLMs, but they're formal enough for mechanical translation. A C# tool parses workflows and generates bash scripts:

```bash
$ dotnet run -- list .../workflows.json
  cve-by-version  CVEs affecting a specific .NET version
  cve-details     Go directly to cve.json for full details
  cve-extraction  Extract data from cve.json (different schema than month index)
  cve-history     CVEs over a time range
  cve-latest      CVEs from the most recent security release

Total: 5 workflows
```

```bash
$ dotnet run -- script .../workflows.json cve-latest
```

The generated script:

```bash
#!/bin/bash
# Workflow: cve-latest
# Description: CVEs from the most recent security release

set -euo pipefail

# Step 1: Start at llms.json
URL="https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/llms.json"
echo "Fetching: $URL" >&2
DOC=$(curl -sf "$URL")

# Step 2: Follow link "latest-security-disclosures"
URL=$(echo "$DOC" | jq -r '._links["latest-security-disclosures"].href // empty')
if [ -z "$URL" ]; then
  echo "Error: Link 'latest-security-disclosures' not found" >&2
  exit 1
fi
echo "Fetching: $URL" >&2
DOC=$(curl -sf "$URL")

echo "$DOC" | jq '.'
```

Running the script:

```bash
$ ./get-latest-cves.sh | jq ._embedded.disclosures.[].id
Fetching: https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/llms.json
Fetching: https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/index.json
"CVE-2025-55248"
"CVE-2025-55315"
"CVE-2025-55247"
```

This replays the theme from earlier: formats that work for both semantic and syntactic consumers. The graph was validated with `jq` and LLMs; workflows are validated with C# and LLMs (with `jq` offering a supporting role).

## Cost model validation

The cost model isn't theoretical—here's what it looks like in practice.

Final test results: <https://github.com/richlander/release-graph-eval-results>

### Trace: 6-month CVE analysis

A [test using Claude Haiku 3.5](https://github.com/richlander/release-graph-eval-results/blob/main/2025-12-27/anthropic_claude-haiku-4.5/B/T6.md) demonstrates the ideal navigation pattern.

**Prompt:** Analyze .NET Runtime and ASP.NET Core CVEs from November 2024 through April 2025. Fetch code diffs and assess whether fixes adequately protect mission-critical apps. Include repo and commit links.

| Turn | Docs | Tokens | Cumulative | Purpose |
|------|------|--------|------------|---------|
| 1 | 1 | 609 | 609 | Entrypoint |
| 2 | 2 | 2,323 | 2,932 | Orientation + skill |
| 3 | 1 | 1,146 | 4,078 | Navigation strategy |
| 4 | 2 | 3,374 | 7,452 | Timeline discovery |
| 5 | 6 | 12,131 | 19,583 | CVE data collection |
| 6 | 6 | 59,832 | 79,415 | Commit analysis |

**75% of all tokens arrive in the final turn.** This is "lean early, heavy late" in action—not by accident, but by design. The pattern:

1. Navigate lean index documents in early turns to identify paths
2. Fetch multiple documents in middle turns to parallelize navigation
3. Fetch information-dense documents in later turns to inform the answer
4. Synthesize in the final turn

<details>
<summary>Raw fetch list with token counts</summary>

```
Turn 1 (609 tokens):
  llms.txt                                    609

Turn 2 (2,323 tokens):
  llms.json                                 2,126
  cve-queries/SKILL.md                        197

Turn 3 (1,146 tokens):
  cve-queries/workflows.json                1,146

Turn 4 (3,374 tokens):
  2024/index.json                           1,765
  2025/index.json                           1,609

Turn 5 (12,131 tokens):
  2024/11/cve.json                          1,656
  2025/01/cve.json                          4,020
  2025/03/cve.json                          1,155
  2025/04/cve.json                          1,034
  2025/05/cve.json                          3,081
  2025/06/cve.json                          1,185

Turn 6 (59,832 tokens):
  dotnet/runtime:d16f41a.diff              37,425
  dotnet/runtime:9da8c6a.diff               1,781
  dotnet/runtime:89ef51c.diff                 260
  dotnet/aspnetcore:67f3b04.diff            1,669
  dotnet/aspnetcore:d6605eb.diff           15,388
  dotnet/runtime:b33d4e3.diff               3,309
```

Note: The eval harness truncated `.diff` files to 50 lines to ensure test completion. Token counts above reflect actual document sizes.

</details>

### Cost analysis

| Turn | Docs | Tokens | Context | Processed | Attention | Cum. Attention |
|------|------|--------|---------|-----------|-----------|----------------|
| 1 | 1 | 609 | 609 | 609 | 0.37M | 0.37M |
| 2 | 2 | 2,323 | 2,932 | 3,541 | 8.60M | 8.97M |
| 3 | 1 | 1,146 | 4,078 | 7,619 | 16.63M | 25.60M |
| 4 | 2 | 3,374 | 7,452 | 15,071 | 55.53M | 81.13M |
| 5 | 6 | 12,131 | 19,583 | 34,654 | 383.49M | 464.62M |
| 6 | 6 | 59,832 | 79,415 | 114,069 | 6,306.74M | 6,771.36M |
| **Total** | **18** | **79,415** | — | **114,069** | — | **6,771M** |

#### Sequential baseline

A sequential approach would process the same 18 documents across 18 turns, one per turn:

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

Both approaches fetch identical tokens. The difference is how they're batched across turns.

#### Comparison

| Metric | Multi-fetch (6 turns) | Sequential (18 turns) | Multiplier |
|--------|----------------------|----------------------|------------|
| Turns | 6 | 18 | 3.0× |
| Tokens processed | 114,069 | 504,551 | **4.4×** |
| Attention cost | 6,771M | 27,517M | **4.1×** |

#### Isolating the optimizations

The 4× improvement comes from two optimizations working together. To isolate their contributions:

|  | 6 turns | 18 turns |
|--|---------|----------|
| **Actual (lean→heavy)** | 114k (1.0×) | 505k (4.4×) |
| **Uniform distribution** | 278k (2.4×) | 754k (6.6×) |

The table reveals how the optimizations combine:

- **Turn collapsing alone** (uniform 18 → uniform 6): 2.7× reduction
- **Load ordering alone** (uniform 6 → actual 6): 2.4× reduction
- **Both together** (uniform 18 → actual 6): 6.6× reduction

The optimizations multiply. The actual 18-turn scenario already benefits from implicit load ordering—the graph naturally places heavy content (diffs) late in navigation. Without that, uniform 18-turn would cost 6.6× rather than 4.4×.

The "lean early, heavy late" pattern is load-bearing architecture: 75% of tokens arrive in the final turn, but they're processed exactly once rather than accumulating across subsequent turns.

### Design implications

- **Minimize turn count** through clear navigation affordances. Each eliminated turn saves quadratically growing attention cost.
- **Front-load lightweight content.** Index documents, link relations, and navigation hints should be small. Substantive content belongs at the leaves.
- **Enable multi-fetch patterns.** Expose document collections as lists of links rather than embedded content, encouraging LLMs to batch retrieval.
- **Provide explicit workflows.** Graph-resident guidance encodes the designer's knowledge of efficient paths.

The cost model constrains the design space. These principles work within it—we cannot change LLM fundamentals, but we can optimize around them. To a large degree, reducing turns is similar to loop variable hoisting—old-school performance strategies remain effective.

## Conclusion

Hypermedia graphs offer a lightweight alternative to vector databases for LLM enablement: self-describing structure, semantic navigation, and pre-baked publishing with no persistent infrastructure. The techniques described here—semantic naming, graph-resident guidance, cost-aware node weighting—are not specific to release notes. They should generalize to any domain where document relationships are meaningful and navigable. The design also remains equally useful for core compliance workflow scenarios.

The current design reflects an 80/20 tradeoff: the remaining 20% would be expensive to achieve and risk overfitting to today's LLMs. Testing prioritized validating that the core graph—which carries a compatibility promise—delivers sufficient performance. It does, for both LLMs and standalone tools.
