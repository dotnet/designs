# Exposing Release Notes as an Information graph

Spec for LLMs: <https://raw.githubusercontent.com/dotnet/core/release-index/llms.txt>

The rest is for you, friendly human.

The .NET project has published release notes in JSON and markdown for many years. Our production of release notes has been based on the virtuous cloud-era idea that many deployment and compliance workflows require detailed and structured data to operate safely at scale. For the most part, a highly clustered set of consumers (like GitHub Actions and malware scanners) have adapted _their tools_ to _our formats_ to offer higher-level value to their users. That's all good. The LLM era is strikingly different where a much smaller set of information distributors (LLM model companies) _consume and expose diverse data in standard ways_ according to _their (shifting) paradigms_ to a much larger set of users. The task at hand is to adapt release notes for LLM consumption while continuing to serve and improve workflows for cloud users.

Release notes are mechanism, not scenario. It likely difficult for users to keep up with and act on the constant stream of .NET updates, typically one or two times a month. Users often have more than one .NET major version deployed, further complicating this puzzle. Many users rely on update orchestrators like APT, Yum, and Visual Studio, however, it is unlikely that such tools cover all the end-points that users care about in a uniform way. It is important that users can reliably make good, straightforward, and timely decisions about their entire infrastructure, orchestrated across a variety of deployment tools. This is a key scenario that release notes serve.

Obvious questions release notes should answer:

- What has changed, since last month, since the last _.NET_ update, or since the last _user_ update.
- How many patches back is this machine?
- How/where can new builds be acquired
- Is a recent update more critical to deploy than "staying current"?
- How long until a given major release is EOL or has been EOL?
- What are known upgrade challenges?

CIOs, CTOs, and others are accountable for maintaining efficient and secure continuity for a set of endpoints, including end-user desktops and cloud servers. They are unlikely to read markdown release notes or perform DIY `curl` + `jq` hacking with structured data, however, they will increasingly expect to be able to answer .NET-related compliance and deployment questions using chat assistants like Claude or Copilot. They may ask ChatGPT to compare treatment of an industry-wide CVE like [CVE-2023-44487](https://nvd.nist.gov/vuln/detail/cve-2023-44487) across multiple application stacks in their portfolio. This already works reasonably well, but fails when queries/prompts demand greater levels of detail with the expectation that they come from an authoritative source. It is very common to see assistants glean insight from a semi-arbitrary set of web pages with matching content.

LLMs are _much more_ fickle relative to purpose-built tools. They are more likely to give up if release notes are not to their liking, instead relying on comfortable web search with its equal share of benefits and challenges. Regular testing (by the spec writer) of release notes with these chat assistants has demonstrated that LLMs are typically only satiated by a "Goldilocks meal". Obscure formats, large documents, and complicated workflows are unlikely to work well. The task at hand is to adapt our release notes publishing so that it works equally well for LLMs and purpose-built tools, exposes more scenario-targeted information, and avoids reliability and performance challenges of our current solution.

In the early revisions of this project, the design followed much the same playbook as past schemas, modeling parent/child relationships, linking to more detailed sources of information, and describing information domains in custom schemas. That then progressed into wanting to expose summaries of high-value information from leaf nodes into the trunk or as far as root nodes. This approach didn't work well since the design was lacking a broader information architecure. A colleague noted that the design was [Hypermedia as the engine of application state (HATEOAS)](https://en.wikipedia.org/wiki/HATEOAS)-esque but not using one of the standard formats. The benefits of using standard formats is that they are free to use, have gone through extensive design review, can be navigated with standard patterns and tools, and (most importantly) LLMs already understand their vocabulary and access patterns. A new format will be definition not have those characteristics.

This proposal leans heavily on hypermedia, specifically [HAL+JSON](https://datatracker.ietf.org/doc/html/draft-kelly-json-hal). Hypermedia is very common, with [OpenAPI](https://www.openapis.org/what-is-openapi) and [JSON:API](https://jsonapi.org/) likely being the most common. LLMs are quite comfortable with these standards. The proposed use of HAL is a bit novel (not intended as a positive description). It is inspired by [llms.txt](https://llmstxt.org/) as an emerging standard and the idea that hypermedia is be the most natural next step to express complex diverse data and relationships. It's also expected (in fact, pre-ordained) that older standards will perform better than newer ones due to higher density (or presence at all) in the LLM training data.

Overall goals:

- Enable queries with multiple key styles, temporal and version-based queries.
- Describe runtime and SDK versions (as much as appropriate) at parity.
- Intergrate high value data, such as CVE disclosures, breaking changes, and download links.
- Ensure high-performance (low kb cost) and high reliability (TTL resilience).
- Enable aestheticly pleasing queries that are terse, ergonomic, and effective.
- Generate most release note files, like [releases.md](https://github.com/dotnet/core/blob/main/releases.md), and CVE announcements like [CVE-2025-55248](https://github.com/dotnet/announcements/issues/372).
- Use this project as a real-world pilot for exposing an information graph equally to LLMs, client libraries, and DIY `curl` + `jq` hacking.

## Hypermedia graph design

This project has adopted the idea that a wide and deep information graph can expose significant information within the graph that satisfies user queries without loading other files. The graph doesn't need to be skeletal. It can have some shape on it. In fact our existing graph with [`release-index.json`](https://github.com/dotnet/core/blob/main/release-notes/releases-index.json) already does this but without the benefit of a standard format or architectural principles.

The design intent is that a graph should be skeletal at its roots for performance and to avoid punishing queries that do not benefit from the curated shape. The deeper the node is in the graph, the more shape (or weight) it should take on since the data curation is much more likely to hit the mark.

Hypermedia formats have a long history of satisfying this methodology, long pre-dating, and actually inspiring the World Wide Web and its Hypertext Markup Language (HTML). This project uses [HAL+JSON](https://en.wikipedia.org/wiki/Hypertext_Application_Language) as the "graph format". HAL is a sort of a "hypermedia in a nutshell" schema, initally drafted in 2012. You can develop a basic understanding of HAL in about two minutes because it has a very limited syntax.

For the most part, HAL defines just two properties:

- `_links` -- links to resources.
- `_embedded` -- embedded resources, which may include more HAL-style links.

It seems like this is hardly enough to support the ambitious design approach that has been described. It turns out that the design is more clever than first blush would suggest.

There is an excellent Australian movie that comes to mind, [The Castle](https://www.imdb.com/title/tt0118826).

> Judge: “What section of the constitution has been breached?”
> Dennis Denuto: "It’s the constitution. It’s Mabo. It’s justice. It’s law. It’s the vibe … no, that’s it, it’s the vibe. I rest my case"

HAL is much the same. It defines an overall approach that a schema designer can hang off of these two seemingly understated properties. You just have to follow the vibe of it.

Here is a simple example from the HAL spec:

```json
{
  "_links": {
    "self": { "href": "/orders/523" },
    "warehouse": { "href": "/warehouse/56" },
    "invoice": { "href": "/invoices/873" }
  },
  "currency": "USD",
  "status": "shipped",
  "total": 10.20
}
```

The `_links` property is a dictionary of link objects with specific named relations. Most links dictionaries start with the standard `self` relation. The `self` relation describes the canonical URL of the given resource. The `warehouse` and `invoice` relations are examples of domain-specific relations. Together, they establish a navigation protocol for this resource domain. One can also imagine `next`, `previous`, `buy-again` as being equally applicable relations for e-commerce. Domain-specific HAL readers will understand these relations and know how or when to act on them.

The `currency`, `status`, and `total` properties provide additional domain-specific resource metadata. The package should arrive at your door soon!

The following example is similar, with the addition of the `_embedded` property.

```json
{
  "_links": {
    "self": { "href": "/orders" },
    "next": { "href": "/orders?page=2" },
    "find": { "href": "/orders{?id}", "templated": true }
  },
  "_embedded": {
    "orders": [{
        "_links": {
          "self": { "href": "/orders/123" },
          "basket": { "href": "/baskets/98712" },
          "customer": { "href": "/customers/7809" }
        },
        "total": 30.00,
        "currency": "USD",
        "status": "shipped",
      },{
        "_links": {
          "self": { "href": "/orders/124" },
          "basket": { "href": "/baskets/97213" },
          "customer": { "href": "/customers/12369" }
        },
        "total": 20.00,
        "currency": "USD",
        "status": "processing"
    }]
  },
  "currentlyProcessing": 14,
  "shippedToday": 20
}
```

The `_embedded` property contains order resources. This is the document payload. Each of those order items have `self` and other related link relations referencing other resources. As stated earlier, the `self` relation references the canonical copy of the resource. Embedded resources may be a full or partial copy of the resource. A domain-specific reader will have a deeper understanding of the resource rules and associated schema.

This design aspect is the true strength of HAL. It's the mechanism that enables the overall approach of a skeletal root with weighted bottom nodes. It's also what enables these two seemingly aenemic properties to provide so much modeling value.

The `currentlyProcessing` and `shippedToday` properties provide additional information about ongoing operations.

We've now seen representative examples of both properties in use. You are now a HAL expert! We can now look at how the same vibe can be applied to .NET release notes.

## Release Notes Graph

Release notes naturally describe two information dimensions: time and product version.

- Within time, we have years, months, and (ship) days.
- Within version, we have major and patch version. We also have runtime vs SDK version.

These dimensional characteristics form the load-bearing structure of the graph to which everything else is attached. We will have both a timeline and version indices. We've previously only had a version index.

The following table summarizes the overall shape of the graph, starting at `dotnet/core:release-notes/index.json`.

| File | Type | Updates when... | Frequency |
|------|------|-----------------|-----------|
| `index.json` (root) | Release version index | New major version, version phase changes | 1x/year |
| `timeline/index.json` | Release timeline index | New year starts, new major version | 1x/year |
| `timeline/{year}/index.json` | Year index | New month with activity, phase changes | 12x/year |
| `timeline/{year}/{month}/index.json` | Month index | Never (immutable after creation) | 0 |
| `{version}/index.json` | Major version index | New patch release | 12x/year |
| `{version}/{patch}/index.json` | Patch version index | Never (immutable after creation) | 0 |

**Summary:** Cold roots, warm branches, immutable leaves.

Note: SDK-only releases break the immutable claim a little, but not much.

We can contrast this approach with the existing release graph, using the last 12 months of commit data (Nov 2024-Nov 2025).

| File | Commits | Notes |
|------|---------|-------|
| `releases-index.json` | 29 | Root index (all versions) |
| `10.0/releases.json` | 22 | Includes previews/RCs and SDK-only releases |
| `9.0/releases.json` | 24 | Includes SDK-only releases, fixes, URL rewrites |
| `8.0/releases.json` | 18 | Includes SDK-only releases, fixes, URL rewrites |

**Summary:** Hot everything.

Conservatively, the existing commit counts are not good. The `releases-index.json` file is a mission-critical live-site resource. 29 updates is > 2x/month!

### Graph Rules

The graph has one rule:

> Every resource in the graph needs to be guaranteed consistent with every other part of the graph.

The unstated problem is CDN caching. Assume that the entire graph is consistent when uploaded to an origin server. A CDN server is guaranteed by construction to serve both old and new copies of the graph leading to potential inconsistencies. The graph construction needs to be resilient to that.

Related examples:

- <https://github.com/dotnet/core/issues/9200>
- <https://github.com/dotnet/core/issues/10082>

Today, we publish [releases-index.json](https://github.com/dotnet/core/blob/main/release-notes/releases-index.json) as the root of our release notes information graph. Some users read this JSON file to learn the latest patch version numbers, while others navigate deeper into the graph. Both are legitimate patterns. However, we've found that our approach has fundamental flaws.

Problems:

- Exposing patch versions in multiple files that need to agree is incompatible with using a Content Delivery Network (CDN) that employs standard caching (expiration / TTL).
- The `releases-index.json` file is a critical live site resource driving 1000s of GBs of downloads a month, yet we manually update it multiple times a month, including for previews.

Solution:

- Fast changing currency (like patch version numbers) are exposed in (at most) a single resource in the graph.
- The root index file is updated once a year (to add the presence of a new major release).

The point about the root index isn't a "solution" but an implication of the first point. If the root index isn't allowed to contain fast-moving currency, because the canonical location is another resource, then it is stripped of its reason to change. That will be addressed later in the document.

There are videos on YouTube with these [crazy gear reductions](https://www.youtube.com/watch?v=QwXK4e4uqXY). You can watch them for a long time! Keen observers will realize our graph will be nothing like that. Well, kindof. One can model years and months and major and patch versions as spinning gears with a differing number of teeth and revolution times. It just won't look the same as those lego videos.

A celestial orbit analogy would have worked just as well.

Release notes graph indexes operate like the following (ignoring some annoying details):

- Timeline index (list of years): one update per year
- Year index (list of months): one update per month
- Month index (list of patches across versions): one update (immutable)

The same progression for versions:

- Releases index (list of of major versions): one update per year
- Major version index (list of patches): one update per month
- Patch version index (details about a patch): one update (immutable)

It's the middle section changing constantly, but the roots and the leaves are either immutable or close enough to it.

### Resource modeling

The following example demonstrates what HAL JSON looks like generally. Each node in the graph is named `index.json`. This is the root [index.json](https://github.com/dotnet/core/blob/release-index/release-notes/index.json) file that represents all .NET versions. It exposes the same general information as the existing `releases-index.json`.

```json
{
  "$schema": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/schemas/dotnet-release-version-index.json",
  "kind": "releases-index",
  "title": ".NET Release Index",
  "description": ".NET Release Index (latest: 10.0)",
  "latest": "10.0",
  "latest_lts": "10.0",
  "_links": {
    "self": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/index.json",
      "path": "/index.json",
      "title": ".NET Release Index",
      "type": "application/hal\u002Bjson"
    },
    "latest": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/index.json",
      "path": "/10.0/index.json",
      "title": "Latest .NET release (.NET 10.0)",
      "type": "application/hal\u002Bjson"
    },
    "latest-lts": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/index.json",
      "path": "/10.0/index.json",
      "title": "Latest LTS release (.NET 10.0)",
      "type": "application/hal\u002Bjson"
    },
```

Key points:

- Schema reference is included
- `kind`, `title`, and `description` describe the resource
- Additional kind-specific properties, like `latest`, describe high-level resource metadata, often useful currency that helps contextualize the rest of the resource without the need to parse/split strings. For example, the `latest_lts` scalar describes the target of the `latest-lts` link relation.
- `_links` and `_embedded` as appropriate.
- Core schema syntax like `latest_lts` uses snake-case-lower for query ergonomics (using `jq` as the proxy for that), while relations like `latest-lts` use kebab-case-lower since they can be names or brands. This follows the approach used by [cve-schema](https://github.com/dotnet/designs/blob/main/accepted/2025/cve-schema/cve_schema.md#brand-names-vs-schema-fields-mixed-naming-strategy).

Here is the first couple objects within the `_embedded` property, in the same root index:

```json
  "_embedded": {
    "releases": [
      {
        "version": "10.0",
        "release_type": "lts",
        "supported": true,
        "eol_date": "2028-11-14T00:00:00\u002B00:00",
        "_links": {
          "self": {
            "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/index.json",
            "title": ".NET 10.0",
            "type": "application/hal\u002Bjson"
          }
        }
      },
      {
        "version": "9.0",
        "release_type": "sts",
        "supported": true,
        "eol_date": "2026-11-10T00:00:00\u002B00:00",
        "_links": {
          "self": {
            "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/9.0/index.json",
            "title": ".NET 9.0",
            "type": "application/hal\u002Bjson"
          }
        }
      },
```

This is where we see the design diverge significantly from `releases-index.json`. There are no patch versions, no statement about security releases. It's the most minimal data to determine the release type, if/when it is supported until, and how to access the canonical resource that exposes richer information. This approach removes the need to update the root index monthly.

Let's look at another section of the graph, the [major version index for .NET 9](https://github.com/dotnet/core/blob/release-index/release-notes/9.0/index.json).

```json
{
  "$schema": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/schemas/dotnet-release-version-index.json",
  "kind": "major-version-index",
  "title": ".NET 9.0 Patch Release Index",
  "description": ".NET 9.0 (latest: 9.0.11)",
  "latest": "9.0.11",
  "latest_security": "9.0.10",
  "release_type": "sts",
  "phase": "active",
  "supported": true,
  "ga_date": "2024-11-12T00:00:00\u002B00:00",
  "eol_date": "2026-11-10T00:00:00\u002B00:00",
  "_links": {
    "self": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/9.0/index.json",
      "path": "/9.0/index.json",
      "title": ".NET 9.0",
      "type": "application/hal\u002Bjson"
    },
    "latest": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/9.0/9.0.11/index.json",
      "path": "/9.0/9.0.11/index.json",
      "title": "Latest patch release (9.0.11)",
      "type": "application/hal\u002Bjson"
    },
    "latest-sdk": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/9.0/sdk/index.json",
      "path": "/9.0/sdk/index.json",
      "title": ".NET SDK 9.0 Release Information",
      "type": "application/hal\u002Bjson"
    },
    "latest-security": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/9.0/9.0.10/index.json",
      "path": "/9.0/9.0.10/index.json",
      "title": "Latest security patch (9.0.10)",
      "type": "application/hal\u002Bjson"
    },
```

This index includes much more useful and detailed information, both metadata/currency and patch-version links. It starts to answer the question of "what should I care about _now_?".

Let's also look at one of the objects from the `_embedded` section as well.

```json
      {
        "version": "9.0.10",
        "date": "2025-10-14T00:00:00\u002B00:00",
        "year": "2025",
        "month": "10",
        "security": true,
        "cve_count": 3,
        "cve_records": [
          "CVE-2025-55247",
          "CVE-2025-55248",
          "CVE-2025-55315"
        ],
        "support_phase": "active",
        "sdk_patches": [
          "9.0.306",
          "9.0.111"
        ],
        "_links": {
          "self": {
            "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/9.0/9.0.10/index.json",
            "path": "/9.0/9.0.10/index.json",
            "title": "9.0.10 Patch Index",
            "type": "application/hal\u002Bjson"
          },
          "release-month": {
            "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/timeline/2025/10/index.json",
            "path": "/timeline/2025/10/index.json",
            "title": "Release timeline index for 2025-10",
            "type": "application/hal\u002Bjson"
          },
          "cve-json": {
            "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/timeline/2025/10/cve.json",
            "path": "/timeline/2025/10/cve.json",
            "title": "CVE Information",
            "type": "application/json"
          }
        }
      },
```

This patch-version object contains even more high-level information that can drive deployment and compliance workflows. The first two link relations are HAL links. The last is a plain JSON link. Non-HAL links end in the format, like `json` or `markdown` or `markdown-rendered`. The links are raw text by default, with `-rendered` HTML content being useful for content targeted for human consumption, for example in generated release notes.

The design has a concept of "wormhole links". That's what we see with `release-month`. It provides direct access to a high-relevance (potentially graph-distant) resource that would otherwise require awkward indirections, multiple network hops, and wasted bytes/tokens to acquire. These wormhole links massively improve query ergonomics for sophisticated queries. There are multiple of these wormhole links, not just `release-month` that are sprinkled throughout the graph for this purpose. They also provide hints on how the graph is intended to be traversed.

There is a link  `cve.json` file. Our [CVE schema](https://github.com/dotnet/designs/blob/main/accepted/2025/cve-schema/cve_schema.md) is a custom schema with no HAL vocabulary. It's an exit node of the graph. The point is that we're free to describe complex domains, like CVE disclosures, using a clean-slate design methodology. One can also see that some of the `cve.json` information has been projected into the graph, adding high-value shape over the skeleton.

This one-level-lower index is more nuanced to what we saw earlier with the root version index. As stated, there is a lot more useful detailed currency on offer. However, there is a rule that currency needs to be guaranteed consistent. Let's consider if the rule is obeyed. The important characteristic is that listed versions and links _within_ the resource are consistent by virtue of being _captured_ in the same file. The critical trick is with the links. The link origin is a fast moving resource and target resources are immutable. That combination works. It's easy to be consistent with something immutable, that either exists or doesn't. In contrast, there would be a problem if there was a link between two mutable resources that expose the same currency. This is the problem that `releases-index.json` has.

The following example is a patch version index, for [9.0.10](https://github.com/dotnet/core/blob/release-index/release-notes/9.0/9.0.10/index.json).

```json
{
  "$schema": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/schemas/dotnet-patch-detail-index.json",
  "kind": "patch-version-index",
  "title": ".NET 9.0.10 Patch Index",
  "description": "Patch information for .NET 9.0.10",
  "version": "9.0.10",
  "date": "2025-10-14T00:00:00\u002B00:00",
  "support_phase": "active",
  "security": true,
  "cve_count": 3,
  "cve_records": [
    "CVE-2025-55247",
    "CVE-2025-55248",
    "CVE-2025-55315"
  ],
  "sdk_patches": [
    "9.0.306",
    "9.0.111"
  ],
  "_links": {
    "self": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/9.0/9.0.10/index.json",
      "path": "/9.0/9.0.10/index.json",
      "title": "9.0.10 Patch Index",
      "type": "application/hal\u002Bjson"
    },
    "next": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/9.0/9.0.11/index.json",
      "path": "/9.0/9.0.11/index.json",
      "title": "9.0.11 Patch Index",
      "type": "application/hal\u002Bjson"
    },
    "prev": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/9.0/9.0.9/index.json",
      "path": "/9.0/9.0.9/index.json",
      "title": "9.0.9 Patch Index",
      "type": "application/hal\u002Bjson"
    },
    "latest-sdk": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/9.0/sdk/index.json",
      "path": "/9.0/sdk/index.json",
      "title": ".NET SDK 9.0 Release Information",
      "type": "application/hal\u002Bjson"
    },
```

This content looks much the same as we saw earlier, except that much of the content we saw in the patch object is now exposed at index root. That's not coincidental, but a key aspect of the model.

The `next` and `prev` link relations provide some more wormholes, this time to less distant targets. The `latest-sdk` target provides access to `aka.ms` evergreen SDK links and other SDK-related information. The `release-month` and `cve-json` links are still there, but a bit further down the dictionary definition as to what's copied above.

The `_embedded` property contains a description of all the SDKs released at the same time as the runtime.

```json
 "_embedded": {
    "sdk": [
      {
        "version": "9.0.306",
        "_links": {
          "feature-band": {
            "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/9.0/sdk/sdk-9.0.3xx.json",
            "path": "/9.0/sdk/sdk-9.0.3xx.json",
            "title": ".NET SDK 9.0.3xx",
            "type": "application/json"
          },
          "release-notes-markdown": {
            "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/9.0/9.0.10/9.0.10.md",
            "path": "/9.0/9.0.10/9.0.10.md",
            "title": "9.0.10 Release Notes",
            "type": "application/markdown"
          },
          "release-notes-markdown-rendered": {
            "href": "https://github.com/dotnet/core/blob/main/release-notes/9.0/9.0.10/9.0.10.md",
            "path": "/9.0/9.0.10/9.0.10.md",
            "title": "9.0.10 Release Notes (Rendered)",
            "type": "application/markdown"
          }
        }
      },
      {
        "version": "9.0.111",
        "_links": {
          "feature-band": {
            "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/9.0/sdk/sdk-9.0.1xx.json",
            "path": "/9.0/sdk/sdk-9.0.1xx.json",
            "title": ".NET SDK 9.0.1xx",
            "type": "application/json"
          },
          "release-notes-markdown": {
            "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/9.0/9.0.10/9.0.111.md",
            "path": "/9.0/9.0.10/9.0.111.md",
            "title": "SDK 9.0.111 Release Notes",
            "type": "application/markdown"
          },
          "release-notes-markdown-rendered": {
            "href": "https://github.com/dotnet/core/blob/main/release-notes/9.0/9.0.10/9.0.111.md",
            "path": "/9.0/9.0.10/9.0.111.md",
            "title": "SDK 9.0.111 Release Notes (Rendered)",
            "type": "application/markdown"
          }
        }
      }
    ],
```

There is an `sdk*.json` that matches each feature band. It is largely the same as `sdk/index.json` but more specific. The rest of the links are for markdown release notes.

Any CVEs for the month are described in `disclosures`.

```json
    "disclosures": [
      {
        "id": "CVE-2025-55247",
        "title": ".NET Denial of Service Vulnerability",
        "_links": {
          "self": {
            "href": "https://github.com/dotnet/announcements/issues/370",
            "title": "CVE-2025-55247"
          }
        },
        "cvss_score": 7.3,
        "cvss_severity": "HIGH",
        "disclosure_date": "2025-10-14",
        "affected_releases": [
          "8.0",
          "9.0"
        ],
        "affected_products": [
          "dotnet-sdk"
        ],
        "platforms": [
          "linux"
        ]
      },
      {
        "id": "CVE-2025-55248",
        "title": ".NET Information Disclosure Vulnerability",
        "_links": {
          "self": {
            "href": "https://github.com/dotnet/announcements/issues/372",
            "title": "CVE-2025-55248"
          }
        },
        "fixes": [
          {
            "href": "https://github.com/dotnet/runtime/commit/18e28d767acf44208afa6c4e2e67a10c65e9647e.diff",
            "repo": "dotnet/runtime",
            "branch": "release/9.0",
            "title": "Fix commit in runtime (release/9.0)",
            "release": "9.0"
          }
        ],
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
```

It's possible to make detail-oriented compliance and deployment decisions based on this information. There's even a commit for the CVE fix with an LLM friendly link style. This is the bottom part of the hypermedia graph. It's far more shapely and weightier than the root. If a consumer gets this far, it is likely because they need access to the exposed information. If they only wanted access to the `cve.json` file, they could have accessed it in the major version index, where it is also made available.

The timeline index is different, but follows much the same approach.

## Design tradeoffs

There are lots of design tradeoffs within the graph design. Ergonomics vs update velocity were perhaps the fiercest of foes in the design.

As mentioned multiple times, graph consistency is a major design requirement. The primary consideration is avoiding exposing currency that can be misused. If that's avoided, then there are no concerns with CDN consistency.

If we cleverly apply these rules, we can actually expand these major version objects in our root releases index with convenience links, like the following:

```json
  "_embedded": {
    "releases": [
      {
        "version": "10.0",
        "release_type": "lts",
        "supported": true,
        "eol_date": "2028-11-14T00:00:00\u002B00:00",
        "_links": {
          "self": {
            "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/index.json",
            "title": ".NET 10.0",
            "type": "application/hal\u002Bjson"
          },
          "latest-patch": {
            "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/10.0.1/index.json",
            "title": ".NET 10.0.1",
            "type": "application/hal\u002Bjson"
          },
          "release-month": {
            "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/timeline/2025/11/index.json",
            "path": "/timeline/2025/11/index.json",
            "title": "Release timeline index for 2025-11",
            "type": "application/hal\u002Bjson"
          }          
        }
      },
      {
        "version": "9.0",
        "release_type": "sts",
        "supported": true,
        "eol_date": "2026-11-10T00:00:00\u002B00:00",
        "_links": {
          "self": {
            "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/9.0/index.json",
            "title": ".NET 9.0",
            "type": "application/hal\u002Bjson"
          },
          "latest-patch": {
            "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/9.0/9.0.9/index.json",
            "title": ".NET 9.0.9",
            "type": "application/hal\u002Bjson"
          },
          "release-month": {
            "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/timeline/2025/11/index.json",
            "path": "/timeline/2025/11/index.json",
            "title": "Release timeline index for 2025-11",
            "type": "application/hal\u002Bjson"
          }
        }
      },
```

That would be _so nice_. Consumers could wormhole to high value content without needing to go through the major version index or the year to get to the intended target. From a query ergonomics standpoint, this structure would be superior.

This approach doesn't violate the consistency rules. There is no badly-behaved currency that can be mis-used. The links are opague and notably target immutable resources. So, why not? Why can't we have nice things?

The issue is that these high-value links would require updating the root index once a month. Regular updates of a high-value resources signficantly increase the likelihood of an outage and reduces the time that the root index can be cached. Cache aggressiveness is part of the performance equation. It's much better to keep the root lean (skeletal) and highly cacheable.

There has been significant discussion on consistency. We might as well complete the lesson.

The following example, with `latest_patch`, is what would cause the worst pain.

```json
  "_embedded": {
    "releases": [
      {
        "version": "10.0",
        "release_type": "lts",
        "supported": true,
        "eol_date": "2028-11-14T00:00:00\u002B00:00",
        "latest_patch": "10.0.1",
        "_links": {
          "self": {
            "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/index.json",
            "title": ".NET 10.0",
            "type": "application/hal\u002Bjson"
          },
          "latest-patch": {
            "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/10.0.1/index.json",
            "title": ".NET 10.0.1",
            "type": "application/hal\u002Bjson"
          },
          "release-month": {
            "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/timeline/2025/11/index.json",
            "path": "/timeline/2025/11/index.json",
            "title": "Release timeline index for 2025-11",
            "type": "application/hal\u002Bjson"
          }
        }
      },
      {
        "version": "9.0",
        "release_type": "sts",
        "supported": true,
        "eol_date": "2026-11-10T00:00:00\u002B00:00",
        "latest_patch": "9.0.9",
        "_links": {
          "self": {
            "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/9.0/index.json",
            "title": ".NET 9.0",
            "type": "application/hal\u002Bjson"
          },
          "latest-patch": {
            "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/9.0/9.0.9/index.json",
            "title": ".NET 9.0.9",
            "type": "application/hal\u002Bjson"
          },
          "release-month": {
            "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/timeline/2025/11/index.json",
            "path": "/timeline/2025/11/index.json",
            "title": "Release timeline index for 2025-11",
            "type": "application/hal\u002Bjson"
          }
        }
      },
```

The `latest_patch` property is the "fast-moving currency" that the design attempts to avoid. A consumer can now take the `10.0.1` value and try it on for size in `10.0/index.json`. It might not fit. That's the exact problem we have in `release-index.json` today. Let's not recreate it.

Another classic movie -- A Few Good Men (1992):

> Judge Randolph: You don't have to answer that question!
> Jessup: I'll answer the question. You want answers?
> Kaffee: I think I'm entitled to it!
> Jessup: You want answers?!
> Kaffee: I WANT THE TRUTH!
> Jessup: You can't handle the truth!

This provides perfect clarity on why we cannot include the `latest_patch` propery, even if we might feel entitled to it.

> "I don't give a DAMN what you think you are entitled to!"

Source: A Few Good Men (1992; 10s later)

That would seems to close the book on convenience.

## Attached data

> These dimensional characteristics form the load-bearing structure of the graph to which everything else is attached.

This leaves the question of which data we could attach.

The following are all in scope to include:

- Breaking changes (already included)
- CVE disclosures (already included)
- Servicing fixes and commits (beyond CVEs)
- Known issues
- Supported OSes
- Linux package dependencies
- Download links + hashes (partially included)

Non goals:

- Preview release details at the same fidelity as GA
- Performance benchmark data

### Modeling as validation

As the final graph took shape, distinct relationships inherent to the resource modeling started to emerge.

- Parent <-> child -- Represents a shift in information depth, scalar <-> vector. The object data (within `_embedded`) in the parent becomes the root metadata in the child, and more complex children appear than were in the parent.
- Version <-> timeline -- Represents an inversion of information, using a different key--temporal or version--to access the same information. The version index converges to a point while the timeline index converges to a slice or row of points.

This reflection on resource modeling is similar to the mathematic concept of [duals](https://en.wikipedia.org/wiki/Duality_(mathematics)). The elements of the graph are not duals, however, the time and version keys likely are.

We can also reason about shape in terms of a storage analogy.

Storage for a 3-level hypermedia design:

- Root/outer nodes: flat scalars or tuples -- filter operation
- Middle nodes: nested documents -- traverse operation
- Exit nodes: indexed documents -- query operation

This analogy is attempting to demonstrate the kind of data exposed at each level and the most sophisticated operation that the node can satisfy. The term "document" is intended to align with "document" in "document database".

These formal descriptions may not help everyone, however, it was used as part of the design process. It can helpful to consider the inherent nature of the data to validate the shape once it is concretely modeled. One can consider the release notes data from one point in the graph to another and pre-conceptualize what it should be accroding to these transformation rules. If it matches, it is likely correct. That's an important approach that was used for graph validation.

## Quality metrics

Query metrics were assessed with the earlier CVE schema project. This time, we can do that an also reason about network cost.

See [metrics.md](./metrics.md) for an in-depth analysis. A few representative tests are included in this document.

### Query: "What .NET versions are currently supported?"

| Schema | Files Required | Total Transfer |
|--------|----------------|----------------|
| hal-index | `index.json` | **8 KB** |
| releases-index | `releases-index.json` | **6 KB** |

**hal-index:**

```bash
ROOT="https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/index.json"

curl -s "$ROOT" | jq -r '._embedded.releases[] | select(.supported) | .version'
# 10.0
# 9.0
# 8.0
```

**releases-index:**

```bash
ROOT="https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json"

curl -s "$ROOT" | jq -r '.["releases-index"][] | select(.["support-phase"] == "active") | .["channel-version"]'
# 10.0
# 9.0
# 8.0
```

**Analysis:**

- **Completeness:** ✅ Equal—both return the same list of supported versions.
- **Boolean vs enum:** The hal-index uses `supported: true`, a simple boolean. The releases-index uses `support-phase: "active"`, requiring knowledge of the enum vocabulary (active, maintenance, eol, preview, go-live).
- **Property naming:** The hal-index uses `select(.supported)` with dot notation. The releases-index requires `select(.["support-phase"] == "active")` with bracket notation and string comparison.
- **Query complexity:** The hal-index query is 30% shorter and more intuitive for someone unfamiliar with the schema.

**Winner:** releases-index (**1.3x smaller** for basic version queries, but hal-index has better query ergonomics)

### CVE Queries for Latest Security Patch

#### Query: "What CVEs were fixed in the latest .NET 8.0 security patch?"

| Schema | Files Required | Total Transfer |
|--------|----------------|----------------|
| hal-index | `index.json` → `8.0/index.json` → `8.0/8.0.21/index.json` | **52 KB** |
| releases-index | `releases-index.json` + `8.0/releases.json` | **1,239 KB** |

**hal-index:**

```bash
ROOT="https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/index.json"

# Step 1: Get the 8.0 version href
VERSION_HREF=$(curl -s "$ROOT" | jq -r '._embedded.releases[] | select(.version == "8.0") | ._links.self.href')
# https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/8.0/index.json

# Step 2: Get the latest security patch href
PATCH_HREF=$(curl -s "$VERSION_HREF" | jq -r '._links["latest-security"].href')
# https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/8.0/8.0.21/index.json

# Step 3: Get the CVE records
curl -s "$PATCH_HREF" | jq -r '.cve_records[]'
# CVE-2025-55247
# CVE-2025-55248
# CVE-2025-55315
```

**releases-index:**

```bash
ROOT="https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json"

# Step 1: Get the 8.0 releases.json URL
RELEASES_URL=$(curl -s "$ROOT" | jq -r '.["releases-index"][] | select(.["channel-version"] == "8.0") | .["releases.json"]')
# https://builds.dotnet.microsoft.com/dotnet/release-metadata/8.0/releases.json

# Step 2: Find latest security release and get CVE IDs
curl -s "$RELEASES_URL" | jq -r '[.releases[] | select(.security == true)] | .[0] | .["cve-list"][] | .["cve-id"]'
# CVE-2025-55247
# CVE-2025-55315
# CVE-2025-55248
```

**Analysis:** Both schemas produce the same CVE IDs. However:

- **Completeness:** ✅ Equal—both return the CVE identifiers
- **Ergonomics:** The releases-index requires downloading a 1.2 MB file to extract 3 CVE IDs. The hal-index uses a dedicated `latest-security` link, avoiding iteration through all releases.
- **Link syntax:** Counterintuitively, the deeper HAL structure `._links.self.href` is more ergonomic than `.["releases.json"]` because snake_case enables dot notation throughout. The releases-index embeds URLs directly in properties, but kebab-case naming forces bracket notation.
- **Data efficiency:** hal-index is 23x smaller

**Winner:** hal-index (**23x smaller**)

### Query: "List all CVEs fixed in the last 12 months"

| Schema | Files Required | Total Transfer |
|--------|----------------|----------------|
| hal-index | `timeline/index.json` → up to 12 month indexes (via `prev` links) | **~90 KB** |
| releases-index | All version releases.json files | **2.4+ MB** |

**hal-index:**

```bash
TIMELINE="https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/timeline/index.json"

# Step 1: Get the latest month href
MONTH_HREF=$(curl -s "$TIMELINE" | jq -r '._embedded.years[0]._links["latest-month"].href')

# Step 2: Walk back 12 months using prev links, collecting security CVEs
for i in {1..12}; do
  DATA=$(curl -s "$MONTH_HREF")
  YEAR_MONTH=$(echo "$DATA" | jq -r '"\(.year)-\(.month)"')
  SECURITY=$(echo "$DATA" | jq -r '.security')
  if [ "$SECURITY" = "true" ]; then
    CVES=$(echo "$DATA" | jq -r '[._embedded.disclosures[].id] | join(", ")')
    echo "$YEAR_MONTH: $CVES"
  fi
  MONTH_HREF=$(echo "$DATA" | jq -r '._links.prev.href // empty')
  [ -z "$MONTH_HREF" ] && break
done
# 2025-10: CVE-2025-55248, CVE-2025-55315, CVE-2025-55247
# 2025-06: CVE-2025-30399
# 2025-05: CVE-2025-26646
# 2025-04: CVE-2025-26682
# 2025-03: CVE-2025-24070
# 2025-01: CVE-2025-21171, CVE-2025-21172, CVE-2025-21176, CVE-2025-21173
```

**releases-index:**

```bash
ROOT="https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json"

# Get all supported version releases.json URLs
URLS=$(curl -s "$ROOT" | jq -r '.["releases-index"][] | select(.["support-phase"] == "active") | .["releases.json"]')

# For each version, find security releases in the last 12 months
CUTOFF="2024-12-01"
for URL in $URLS; do
  curl -s "$URL" | jq -r --arg cutoff "$CUTOFF" '
    .releases[] |
    select(.security == true) |
    select(.["release-date"] >= $cutoff) |
    "\(.["release-date"]): \([.["cve-list"][]? | .["cve-id"]] | join(", "))"'
done | sort -u | sort -r
# 2025-10-14: CVE-2025-55247, CVE-2025-55315, CVE-2025-55248
# 2025-06-10: CVE-2025-30399
# 2025-05-22: CVE-2025-26646
# 2025-04-08: CVE-2025-26682
# 2025-03-11: CVE-2025-24070
# 2025-01-14: CVE-2025-21172, CVE-2025-21173, CVE-2025-21176
```

**Analysis:**

- **Completeness:** ⚠️ Partial—the releases-index can list CVEs by date, but notice CVE-2025-21171 is missing (it only affected .NET 9.0 which was still in its first patch cycle). The output also shows exact dates rather than grouped by month.
- **Ergonomics:** The hal-index uses `prev` links for natural backward navigation. The releases-index requires downloading all version files (2.4+ MB), filtering by date, and deduplicating results.
- **Navigation model:** The hal-index timeline is designed for chronological traversal. The releases-index has no concept of time-based navigation.

**Winner:** hal-index (**27x smaller**)
