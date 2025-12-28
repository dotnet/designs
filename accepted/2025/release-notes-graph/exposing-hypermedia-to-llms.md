# Exposing Hypermedia Information Graphs to LLMs

[Hypermedia](https://en.wikipedia.org/wiki/Hypermedia) and [hypertext](https://en.wikipedia.org/wiki/Hypertext) are decades-old ideas and formats that are perfectly-suited for LLM consumption by virtue of self-describing structure and relationships between resources. The premise is that there is enough meta-information in a hypermedia document that a semantic consumer can successfully traverse an information graph to find the information demanded by a user prompt. The prevailing narrative over the last few decades has been that _structured data_ > _unstructured documents_, in terms of the ability to derive meaningful insight with tools beyond basic text search. JSON and XML came out of that heritage, with structured query enabled by [JSONPath](https://en.wikipedia.org/wiki/JSONPath) and [XPath](https://en.wikipedia.org/wiki/XPath), both assuming a priori schema knowledge. [Hypermedia as the engine of application state (HATEOAS)](https://en.wikipedia.org/wiki/HATEOAS) contributes the idea that data labeling can be extended to relations across documents. [Hypertext Application Language](https://en.wikipedia.org/wiki/Hypertext_Application_Language) is a standard implementation of this concept, enabling applications to expose a semantic space to a broad range of consumers, enabling navigation patterns beyond what a schema could reasonably describe.

> A (nearly) century-old principle, articulated by [Korzybski](https://en.wikipedia.org/wiki/Alfred_Korzybski): [the map is not the territory](https://en.wikipedia.org/wiki/Map%E2%80%93territory_relation).

In trail races, there are frequent ribbons hanging from trees and painted arrows on the ground to ensure the correct path taken. It is often the case that there are races of multiple distances being run on an overlapping course. At key intersections, there are signs that say "5 KM -> left" and "10 KM -> continue straight". The ribbons and the painted arrows are the kind of map that a document schema provides, ensuring correct coloring within the lines. The signposting where the courses diverge is the extra descriptive insight that enables graph traversal. The signposting is a key-value function. You match a key you know with a value you don't. This signposting gets us closer to understanding and being able to explore the territory.

This approach is in sharp contrast to the typical HTML graph implementation: `For a deeper discussion on this topic, <a href="another-document.html">click here</a>.`. A semantic graph might expose a named link relation like `"gardening-deep-dive": "https://../diving-deeper-on-gardening.md"` or expose more descriptive complexity by separating link kind, the "deep-dive", from target kind, "gardening", more cleanly splitting the adjective and noun. The better the implementation, the less inference, flow analysis, or attention is required to derive the intended meaning.

Databases went through a "no-sql" transition. That wasn't a rejection of structure, but a realization that the source of structure is the documents. Semantic graphs extend this idea with "no-schema" consumption. Rather than requiring upfront schema knowledge, a fully realized semantic space—in both content and link relations—allows readers to discover structure through descriptive labels and traversal. A sort of "world schema" emerges from navigation.

Hypermedia information document graphs can be published pre-baked, making them suitable for direct consumption without being pre-loaded and exposed by a vector database. The semantic relationships and other meta-information are used as the basis of typical LLM mechanics like vector similarity, making hypermedia a kind of RAG scheme and suitable for static-webhost deployment.

The concept of a pre-baked static hypermedia graph has been applied to the .NET release notes. That project was initially approached as a modern revamp of a set of JSON files that are used to inform and direct cloud-infra deployment and compliance workflows at scale. Over time, it became obvious that LLMs could read the same content directly and self-reason about its content and navigation patterns. Early experimentation proved that out. The primary techniques used to improve applicability for LLMs are semantic naming and graph-resident guidance. These techniques can be quite subtle, where a small shift in semantic bias can result in a large shift in LLM performance.

Graph-resident guidance consists of skills and workflows. HATEOAS tells us that "customer" can be a relation of a sales order. Why not make "graph-instructions" a relation of a graph? Skills and workflows are first-class relations in the graph, enabling graph designers to express navigation intent. Skills follow the Anthropic skill format, while workflows are HAL documents that describe queries over graph link relations. This enables the graph designer to provide the reader with the "ten-km-route" workflow if that's a match for the intended outcome.

The .NET release notes information graph uses [Hypertext Application Language (HAL)](https://en.wikipedia.org/wiki/Hypertext_Application_Language) as its hypermedia foundation. It augments HAL with a homegrown HAL-native workflow convention that looks just as native as `_links` or `_embedded`. LLMs grasp the intent, in part because HAL predates LLMs by over a decade. This approach enables low-cost LLM enablement for scenarios where hosting a persistent "AI server" would be prohibitive.

## Graph entrypoint tension

The release notes information graph is based on the restrictive idea that the entrypoint of the graph should be skeletal and rarely changing. That's workable for LLMs but far from ideal. The restrictive idea of the core graph is that it should support n-9s levels of reliability and be subject to rigorous engineering practices (git workflows, peer review, merge gates). However, we're in the early days of AI and subject to waves of externally-driven change that requires significant rework to maintain high quality LLM enablement. These requirements are in firm opposition, needing a winner to pull ahead, a painful compromise, or some form of tie-break.

Instead, we can view the core graph as a well-defined data-layer that honors the reliability requirements, while exposing a separate application-layer entrypoint for LLMs that can evolve over time without a heavy compatibility burden.

We can compare the two entrypoints.

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

That's how the core graph exposes a major version. As suggested, it's skeletal. The graph entrypoint only needs to be updates once or twice a year. Even if the file is regenerated daily, git won't notice any changes.

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

This approach enables both principles from earlier:

- "match for the intended outcome": the designer provides multiple semantic pathways for different query types
- "match a key you know with a value you don't": the reader discovers the right pathway through semantic labels

## LLM entry point

Two strong design principles emerged from intuition and then observed behavior from eval:

- Consistently apply a semantic model throughout the graph. It's a comfort to find a concept where it is expected.
- Expose resources in terms of structual kind, like `major` aand `-month`, and desired output, like `-security-disclosures`.

This dual approach to semantic naming sometimes results in this double-mapping. Emperical observation suggests that LLMs prefer the outcome-based naming, while the more schema-correct and initial naming is the structual framing.
