# Exposing Release Notes as an Information graph

The .NET project has published release notes in JSON and markdown for many years. The investment in quality release notes has been based on the virtuous cloud-era idea that many deployment and compliance workflows require detailed and structured data to safely operate at scale. For the most part, a highly clustered set of consumers (like GitHub Actions and vulnerability scanners) have adapted _their tools_ to _our formats_ to offer higher-level value to their users. That's all good. The LLM era is strikingly different where a much smaller set of information systems (LLM model companies) _consume and expose diverse data in standard ways_ according to _their (shifting) paradigms_ to a much larger set of users. The task at hand is to modernize release notes to make them more efficient to consume generally and to adapt them for LLM consumption.

Overall goals for release notes consumption:

- Graph schema encodes graph update frequency
- Satisfy reasonable expectations of performance (no 1MB JSON files), reliability, and consistency
- Enable aestheticly-pleasing queries that are terse, ergonomic, and effective, both for their own goals and as a proxy for LLM consumption.
- Support queries with multiple key styles, temporal and version-based (runtime and SDK versions) queries.
- Expose queryable data beyond version numbers, such as CVE disclosures, breaking changes, and download links.
- Use the same data to generate most release note markdown files, like [releases.md](https://github.com/dotnet/core/blob/main/releases.md), and CVE announcements like [CVE-2025-55248](https://github.com/dotnet/announcements/issues/372), guaranteeing ensuring consistency from a single source of truth.
- Use this project as a real-world information graph pilot to inform other efforts that expose information to modern information consumers.

## Scenario

Release notes are mechanism, not scenario. It likely difficult for users to keep up with and act on the constant stream of .NET updates, typically one or two times a month. Users often have more than one .NET major version deployed, further complicating this puzzle. Many users rely on update orchestrators like APT, Yum, and Visual Studio, however, it is unlikely that such tools cover all the end-points that users care about in a uniform way. It is important that users can reliably make good, straightforward, and timely decisions about their entire infrastructure, orchestrated across a variety of deployment tools. This is a key scenario that release notes serve.

Obvious questions release notes should answer:

- What has changed, since last month, since the last _.NET_ update, or since the last _user_ update.
- How many patches back is this machine?
- How/where can new builds be acquired
- Is a recent update more critical to deploy than "staying current"?
- How long until a given major release is EOL or has been EOL?
- What are known upgrade challenges?

CIOs, CTOs, and others are accountable for maintaining efficient and secure continuity for a set of endpoints, including end-user desktops and cloud servers. They are unlikely to read long markdown release notes or perform DIY `curl` + `jq` hacking with structured data. They will increasingly expect to be able to get answers to arbitrarily detailed compliance and deployment questions using chat assistants like Copilot. They may ask Claude to compare treatment of an industry-wide CVE like [CVE-2023-44487](https://nvd.nist.gov/vuln/detail/cve-2023-44487) across multiple application stacks in their portfolio. This already works reasonably well, but fails when prompts demand greater levels of detail and with the expectation that the source data comes from authoritative sources. It is very common to see assistants glean insight from a semi-arbitrary set of web pages with matching content. This is particularly problematic for day-of prompts (same day as a security release).

Some users have told us that they enable Slack notifications for [dotnet/announcements](https://github.com/dotnet/announcements/issues), which is an existing "release notes beacon". That's great and intended. What if we could take that to a new level, thinking of release notes as queryable data used by notification systems and LLMs? There is a lesson here. Users (virtuously) complain when we [forget to lock issues](https://github.com/dotnet/announcements/issues/107#issuecomment-482166428). They value high signal to noise. Fortunately, we no longer forget for announcements, but we have not achieved this same disciplined model with GitHub release notes commits (as will be covered later). It should just just as safe and reliable to use release notes updates as a beacon as dotnet/announcements.

LLMs are a different kind of "user" than we've previously tried to enable. LLMs are _much more_ fickle relative to purpose-built tools. They are more likely to give up if release notes are not to their liking, instead relying on model knowledge or comfortable web search with its equal share of benefits and challenges. Regular testing (by the spec writer) of release notes with chat assistants has demonstrated that LLMs are typically only satiated by a "Goldilocks meal". Obscure formats, large documents, and complicated workflows don't perform well (or outright fail). LLMs will happily jump to `releases-index.json` and choke on the 1MB+ [`releases.json`](https://github.com/dotnet/core/blob/main/release-notes/releases-index.json) files we maintain if prompts are unable to keep their attention.

![.NET 6.0 releases.json file as tokens](./releases-json-tokens.png)

This image shows that the worst case for the `releases.json` format is 600k tokens using the [OpenAI Tokenzier](https://platform.openai.com/tokenizer). It is an understatement to say that a file of that size doesn't work well with LLMs. Context: memory budgets tend to max out at 200k tokens. Large JSON files can be made to work in some scenarios, but not in the general case.

A strong belief is that workflows that are bad for LLMS are typically not _uniquely_ bad for LLMs but are challenging for other consumers. It is easy to guess that most readers of `releases-index.json` would be better-served by referenced JSON significantly less than 1MB+. This means that we need start from scratch with structured release notes.

In the early revisions of this project, the design followed our existing schema playbook, modeling parent/child relationships, linking to more detailed sources of information, and describing information domains in custom schemas. That then progressed into wanting to expose summaries of high-value information from leaf nodes into the trunk. This approach didn't work well since the design was lacking a broader information architecure. A colleague noted that the design was [Hypermedia as the engine of application state (HATEOAS)](https://en.wikipedia.org/wiki/HATEOAS)-esque but not using one of the standard formats. The benefits of using standard formats is that they are free to use, have gone through extensive design review, can be navigated with standard patterns and tools, and (most importantly) LLMs already understand their vocabulary and access patterns. A new format will by definition not have those characteristics.

This proposal leans heavily on hypermedia, specifically [HAL+JSON](https://datatracker.ietf.org/doc/html/draft-kelly-json-hal). Hypermedia is very common, with [OpenAPI](https://www.openapis.org/what-is-openapi) and [JSON:API](https://jsonapi.org/) likely being the most common. LLMs are quite comfortable with these standards. The proposed use of HAL is a bit novel (not intended as a positive descriptor). It is inspired by [llms.txt](https://llmstxt.org/) as an emerging standard and the idea that hypermedia is the most natural next step to express complex diverse data and relationships. It's also expected (in fact, pre-ordained) that older standards will perform better than newer ones due to higher density (or presence at all) in the LLM training data.

## Hypermedia graph design

This project has adopted the idea that a wide and deep information graph can expose significant information within the graph that satisfies user queries without loading other files. The graph doesn't need to be skeletal. It can have some shape on it. In fact our existing graph with [`release-index.json`](https://github.com/dotnet/core/blob/main/release-notes/releases-index.json) already does this, but without the benefit of a standard format or architectural principles.

The design intent is that a graph should be skeletal at its roots for performance and to avoid punishing queries that do not benefit from the curated shape. The deeper the node is in the graph, the more shape (or weight) it should take on since the data curation is much more likely to hit the mark.

Hypermedia formats have a long history of satisfying this methodology, long pre-dating, and actually inspiring the World Wide Web and its Hypertext Markup Language (HTML). This project uses [HAL+JSON](https://en.wikipedia.org/wiki/Hypertext_Application_Language) as the "graph format". HAL is a sort of a "hypermedia in a nutshell" schema, initally drafted in 2012. You can develop a basic understanding of HAL in about two minutes because it has a very limited syntax.

For the most part, HAL defines just two properties:

- `_links` -- links to resources.
- `_embedded` -- embedded resources, which will often include `_links`.

It seems like this is hardly enough to support the ambitious design approach that has been described. It turns out that the design is more clever than first blush would suggest.

There is an excellent Australian movie that comes to mind, [The Castle](https://www.imdb.com/title/tt0118826).

> Judge: “What section of the constitution has been breached?”
> Dennis Denuto: "It’s the constitution. It’s Mabo. It’s justice. It’s law. It’s the vibe ... no, that’s it, it’s the vibe. I rest my case"

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

The `_links` property is a dictionary of link objects with specific named relations. Most link dictionaries start with the standard `self` relation. The `self` relation describes the canonical URL of the given resource. The `warehouse` and `invoice` relations are examples of domain-specific relations. Together, they establish a navigation protocol for this resource domain. One can also imagine `next`, `previous`, `buy-again`, or `i-am-feeling-lucky` as relations for e-commerce. Domain-specific HAL readers will understand these relations and know how or when to act on them.

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

The `_embedded` property contains order resources. This is the resource payload. Each of those order items have `self` and other related link relations referencing other resources. As stated earlier, the `self` relation references the canonical copy of the resource. Embedded resources may be a full or partial copy of the resource. Again, domain-specific reader will understand this schema and know how to process it.

This design aspect is the true strength of HAL, of projecting partial views of resources to their reference. It's the mechanism that enables the overall approach of a skeletal root with weighted bottom nodes. It's also what enables these two seemingly anemic properties to provide so much modeling value.

The `currentlyProcessing` and `shippedToday` properties provide additional information about ongoing operations.

We can now look at how the same vibe can be applied to .NET release notes.

## Release Notes Graph

Release notes naturally describe two information dimensions: time and product version.

- Within time, we have years, months, and (ship) days.
- Within version, we have major and patch version. We also have runtime vs SDK version.

These dimensional characteristics form the load-bearing structure of the graph to which everything else is attached. The new graph exposes both timeline and version indices. We've previously only had a version index.

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

### Graph consistency

The graph has one rule:

> Every resource in the graph needs to be guaranteed consistent with every other part of the graph.

The unstated problem is CDN caching. Assume that the entire graph is consistent when uploaded to an origin server. A CDN server is guaranteed by construction to serve both old and new copies of the graph -- for existing files that have been updated -- leading to potential inconsistencies. The graph construction needs to be resilient to that.

Related examples:

- <https://github.com/dotnet/core/issues/9200>
- <https://github.com/dotnet/core/issues/10082>

Today, we publish [releases-index.json](https://github.com/dotnet/core/blob/main/release-notes/releases-index.json) as the root of our release notes information graph. Some users read this JSON file to learn the latest patch version numbers, while others navigate deeper into the graph. Both are legitimate patterns. However, we've found that our approach has fundamental flaws.

Problems:

- Exposing patch versions in multiple files that need to agree is incompatible with using a Content Delivery Network (CDN) that employs standard caching (expiration / TTL).
- The `releases-index.json` file is a critical live site resource driving 1000s of GBs of downloads a month, yet we update it multiple times a month, including for previews.

Solution:

- Fast changing currency (like patch version numbers) are exposed in (at most) a single resource in the graph.
- The root index file is updated once a year (to add the presence of a new major release).

The point about the root index isn't a "solution" but an implication of the first point. If the root index isn't allowed to contain fast-moving currency, because it is present in another resource, then it is stripped of its reason to change.

There are videos on YouTube with these [crazy gear reductions](https://www.youtube.com/watch?v=QwXK4e4uqXY). You can watch them for a long time! Keen observers will realize our graph will be nothing like that. Well, kindof. One can model years and months and major and patch versions as spinning gears with a differing number of teeth and revolution times. It just won't look the same as those lego videos.

A stellar orbit analogy would have worked just as well.

Release notes graph indexes are updatd (gear reduce) like the following:

- Timeline index (list of years): one update per year
- Year index (list of months): one update per month
- Month index (list of patches across versions): one update (immutable)

The same progression for versions:

- Releases index (list of of major versions): one update per year
- Major version index (list of patches): one update per month
- Patch version index (details about a patch): one update (immutable)

It's the middle section changing constantly, but the roots and the leaves are either immutable or close enough to it.

Note: Some annoying details, like SDK-only releases, have been ignored. The intent is to reason about rough order of magnitude and the fundamental pressure being applied to each layer.

A key question about this scheme is when we add new releases. The most obvious answer to add new releases at Preview 1. The other end of the spectrum would be at GA. From a mission-critical standpoint, GA sounds better. Add it when it is needed and can be acted on for mission critical use. Unintuitively, this approach is likely a priority inversion.

We should add vNext releases at Preview 1 for the following reasons:

- vNext is available (in preview form), we so we should advertise it.
- Special once-in-a-release tasks are more likely to fail when done on GA day.
- Adding vNext early enables consumers to cache aggressively.

The intent is that root `index.json` can be cached aggressively. Adding vNext to `index.json` with Preview 1 is perfectly aligned with that. Adding vNext at GA is not.

## Graph wormholes

There are many design paradigms and tradeoffs one can consider with a graph like this. A major design point is the skeletal roots and weighted bottom. That was already been covered. This approach forces significant fetches and inefficiency to get anywhere. To mitigate that, the graph includes a significant number of helpful workflow-specific wormhole links. As the name suggests, these links enable jumps from one part of the graph to another, aligned with expected queries. The wormholes are the most elaborate in the warm branches since they are regularly updated.

The following are the primary wormhole links.

Cold roots:

- `latest` and `latest-lts` -- enables jumping to the matching major version index.
- `latest-year` -- enables jumping to the latest year index.

Warm branches:

- `latest` and `latest-security` -- enables jumping to the matching patch version index.
- `latest-month` and `latest-security-month` -- enables jumping to the latest matching month.
- `release-month` -- enables jumping to the month index for a patch version.

Immutable leaves:

- `prev` and `prev-security` -- enables jumping to an earlier matching patch version or month index.

The wormhole links are what make the graph a graph and not just two trees provided alternative views of the same data. They also enable efficient navigation.

In some cases, it may be better to look at `timeline/2025/index.json` and consider all the security months. In other cases, it may be more efficient to jump to `latest-security-month` and then backwards in time via `prev-security`. Both are possible and legitimate. Note that `prev-security` jumps across year boundaries.

There are lots of algoriths that [work best when counting backwards](https://www.benjoffe.com/fast-date-64).

There is no `next` or `next-security`. `prev` and `prev-security` link immutable leaves. It is easy to create a linked-list based on past knowledge. It's not possible to provide `next` links an immutability constraint.

There is no `latest-sts` link because it's not really useful. `latest` covers it.

Testing has demonstrated that these wormhole links are one of the defining features of the graph.

## Version Index Modeling

The resource modeling witin the graph has to satisfy the intended "gear reduction" mentioned earlier. The key technique is noticing when a design choice forces a faster update schedule than desired or exposes currency that could be misused. This includes the wormhole links, just discussed.

### Releases index

Most nodes in the graph are named `index.json`. This is the root [index.json](https://github.com/dotnet/core/blob/release-index/release-notes/index.json) file that represents all .NET versions. It exposes the same general information as the existing `releases-index.json`. It should look similar to the examples shared earlier from the HAL spec.

```json
{
  "$schema": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/schemas/v1/dotnet-release-version-index.json",
  "kind": "releases-index",
  "title": ".NET Release Index",
  "latest": "10.0",
  "latest_lts": "10.0",
  "_links": {
    "self": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/index.json",
      "title": ".NET Release Index"
    },
    "latest": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/index.json",
      "title": "Major version index"
    },
    "latest-lts": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/index.json",
      "title": "Latest LTS"
    },
    "timeline-index": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/index.json",
      "title": ".NET Release Timeline Index"
    },
  },
```

Key points:

- Schema reference is included
- All links are raw content (eventually will transition to `builds.dotnet.microsoft.com).
- `kind` and `title` describe the resource
- `latest` and `latest_lts` describe high-level resource metadata, often useful currency that helps contextualize the rest of the resource without the need to parse/split strings. For example, the `latest_lts` scalar describes the target of the `latest-lts` link relation.
- `timeline-index` provides a wormhole link to another part of the graph
- Core schema syntax like `latest_lts` uses snake-case-lower for query ergonomics (using `jq` as the proxy for that), while relations like `latest-lts` use kebab-case-lower since they can be names or brands. This follows the approach used by [cve-schema](https://github.com/dotnet/designs/blob/main/accepted/2025/cve-schema/cve_schema.md#brand-names-vs-schema-fields-mixed-naming-strategy).

The `_embedded` section has one child, `releases`:

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
      {
        "version": "9.0",
        "release_type": "sts",
        "supported": true,
        "_links": {
          "self": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/index.json"
          }
        }
      },
```

This is where we see the design diverge significantly from `releases-index.json`. There are no patch versions, no statement about security releases. It's the most minimal data to determine the release type, if it is supported, and how to access the canonical resource that exposes richer information. This approach removes the need to update the root index monthly. It's fine for tools to regenerate this file monthly. `git` should not see any diffs.

### Major version index

One layer lower, we have the major version idex. The following example is the [major version index for .NET 9](https://github.com/dotnet/core/blob/release-index/release-notes/9.0/index.json).

```json
{
  "$schema": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/schemas/v1/dotnet-release-version-index.json",
  "kind": "major-version-index",
  "title": ".NET 9.0 Release Index",
  "target_framework": "net9.0",
  "latest": "9.0.11",
  "latest_security": "9.0.10",
  "release_type": "sts",
  "support_phase": "active",
  "supported": true,
  "ga_date": "2024-11-12T00:00:00\u002B00:00",
  "eol_date": "2026-11-10T00:00:00\u002B00:00",
  "_links": {
    "self": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/index.json"
    },
    "downloads": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/downloads/index.json",
      "title": ".NET 9.0 Downloads"
    },
    "latest": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/9.0.11/index.json",
      "title": "Latest patch"
    },
    "latest-sdk": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/sdk/index.json",
      "title": ".NET SDK 9.0 Release Information"
    },
    "latest-security": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/9.0.10/index.json",
      "title": "Latest security patch"
    },
    "release-manifest": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/manifest.json",
      "title": "Release manifest"
    },
    "releases-index": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/index.json",
      "title": "Release index"
    }
  },
```

This index includes much more useful and detailed information, both metadata/currency and patch-version links. It starts to answer the question of "what should I care about _now_?".

Much of the form is similar to the root index. Instead of `latest_lts`, there is `latest_security`. A new addition is `release-manifest`. That relation stores important but lower value content about a given major release. That will be covered shortly.

The `_embeeded` section has two children: `releases` and `years`.

```json
  "_embedded": {
    "patches": [
      {
        "version": "9.0.11",
        "release": "9.0",
        "date": "2025-11-19T00:00:00\u002B00:00",
        "year": "2025",
        "month": "11",
        "security": false,
        "cve_count": 0,
        "support_phase": "active",
        "sdk_version": "9.0.308",
        "_links": {
          "self": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/9.0.11/index.json"
          },
          "release-month": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/11/index.json",
            "title": "Release month index"
          }
        }
      },
      {
        "version": "9.0.10",
        "release": "9.0",
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
        "sdk_version": "9.0.306",
        "_links": {
          "self": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/9.0.10/index.json"
          },
          "release-month": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/index.json",
            "title": "Release month index"
          },
          "cve-json": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/cve.json",
            "title": "CVE records (JSON)",
            "type": "application/json"
          }
        }
      }, 
```

and years:

```json
    "years": [
      {
        "year": "2025",
        "_links": {
          "self": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/index.json"
          }
        }
      },
      {
        "year": "2024",
        "_links": {
          "self": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2024/index.json"
          }
        }
      }
    ]
```

The `patches` object contains detailed information that can drive deployment and compliance workflows. The first two link relations, `self` and `release-month` are HAL links while `cve-json` is a plain JSON link. Most non-HAL links end in the given format, like `json` or `markdown` or `markdown-rendered`. The links are raw text by default, with `-rendered` HTML content being useful for content targeted for human consumption, for example in generated release notes.

As mentioned earlier, the design has a concept of "wormhole links". That's what we see with `release-month`. It provides direct access to a high-relevance (potentially graph-distant) resource that would otherwise require awkward indirections, multiple network hops, and wasted bytes/tokens to acquire. These wormhole links massively improve query ergonomics for sophisticated queries.

There is a link  `cve.json` file. Our [CVE schema](https://github.com/dotnet/designs/blob/main/accepted/2025/cve-schema/cve_schema.md) is a custom schema with no HAL vocabulary. It's an exit node of the graph. The point is that we're free to describe complex domains, like CVE disclosures, using a clean-slate design methodology. One can also see that some of the `cve.json` information has been projected into the graph, adding high-value shape over the skeleton.

The `year` property is effectively a baked in pre-query that directs further exploration if the timeline is of interest for this major releases. The `cve_records` property lists all the CVEs for the month, another pre-query baked in.

As stated, there is a lot more useful detailed currency on offer. However, there is a rule that currency needs to be guaranteed consistent. Let's consider if the rule is obeyed. The important characteristic is that listed versions and links _within_ the resource are consistent by virtue of being _captured_ in the same file.

The critical trick is with the links. In the case of the `release-month` link, the link origin is a fast moving resource (warm branch) while the link target is immutable. That combination works. It's easy to be consistent with something immutable. It will either exist or not. In contrast, there would be a problem if there was a link between two mutable resources that expose the same currency. This is the problem that `releases-index.json` has.

Back to `manifest.json`. It contains extra data that tools in particular might find useful. The following example is the `manifest.json` file for .NET 9.

```json
{
  "kind": "manifest",
  "title": ".NET 9.0 Manifest",
  "version": "9.0",
  "label": ".NET 9.0",
  "target_framework": "net9.0",
  "release_type": "sts",
  "support_phase": "active",
  "supported": true,
  "ga_date": "2024-11-12T00:00:00+00:00",
  "eol_date": "2026-11-10T00:00:00+00:00",
  "_links": {
    "self": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/manifest.json"
    },
    "compatibility": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/compatibility.json",
      "title": "Compatibility",
      "type": "application/json"
    },
    "compatibility-rendered": {
      "href": "https://learn.microsoft.com/dotnet/core/compatibility/9.0",
      "title": "Breaking changes in .NET 9",
      "type": "text/html"
    },
    "downloads-rendered": {
      "href": "https://dotnet.microsoft.com/download/dotnet/9.0",
      "title": ".NET 9 Downloads",
      "type": "text/html"
    },
    "os-packages-json": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/os-packages.json",
      "title": "OS Packages",
      "type": "application/json"
    },
    "release-blog-rendered": {
      "href": "https://devblogs.microsoft.com/dotnet/announcing-dotnet-9/",
      "title": "Announcing .NET 9",
      "type": "text/html"
    },
    "supported-os-json": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/supported-os.json",
      "title": "Supported OSes",
      "type": "application/json"
    },
    "supported-os-markdown": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/supported-os.md",
      "title": "Supported OSes",
      "type": "application/markdown"
    },
    "supported-os-markdown-rendered": {
      "href": "https://github.com/dotnet/core/blob/main/release-notes/9.0/supported-os.md",
      "title": "Supported OSes (Rendered)",
      "type": "text/html"
    },
    "target-frameworks": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/target-frameworks.json",
      "title": "Target Frameworks",
      "type": "application/json"
    },
    "usage-markdown-rendered": {
      "href": "https://github.com/dotnet/core/blob/main/release-notes/9.0/README.md",
      "title": "Release Notes (Rendered)",
      "type": "text/html"
    },
    "whats-new-rendered": {
      "href": "https://learn.microsoft.com/dotnet/core/whats-new/dotnet-9/overview",
      "title": "What\u0027s new in .NET 9",
      "type": "text/html"
    }
  }
}
```

This is a dictionary of links with some useful metadata. The relations are in alphabetical order, after `self`.

Some of the information in this file is sourced from a human-curated `_manifest.json`. This file is used by the graph generation tools, not the graph itself. It provides a path to seeding the graph with data not available elsewhere.

.NET 9 `_manifest.json`:

```json
{
  "kind": "manifest",
  "title": ".NET 9.0 Manifest",
  "version": "9.0",
  "label": ".NET 9.0",
  "target_framework": "net9.0",
  "release_type": "sts",
  "phase": "active",
  "ga_date": "2024-11-12T00:00:00Z",
  "eol_date": "2026-11-10T00:00:00Z",
  "_links": {
    "downloads-rendered": {
      "href": "https://dotnet.microsoft.com/download/dotnet/9.0",
      "title": ".NET 9 Downloads",
      "type": "text/html"
    },
    "whats-new-rendered": {
      "href": "https://learn.microsoft.com/dotnet/core/whats-new/dotnet-9/overview",
      "title": "What's new in .NET 9",
      "type": "text/html"
    },
    "compatibility-rendered": {
      "href": "https://learn.microsoft.com/dotnet/core/compatibility/9.0",
      "title": "Breaking changes in .NET 9",
      "type": "text/html"
    },
    "release-blog-rendered": {
      "href": "https://devblogs.microsoft.com/dotnet/announcing-dotnet-9/",
      "title": "Announcing .NET 9",
      "type": "text/html"
    }
  }
}
```

These links are free form and can be anything. They follow the same scheme as the links used elsewhere in the graph.

### Patch Version Index

The following example is a patch version index, for [9.0.10](https://github.com/dotnet/core/blob/release-index/release-notes/9.0/9.0.10/index.json).

```json
{
  "$schema": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/schemas/v1/dotnet-patch-detail-index.json",
  "kind": "patch-version-index",
  "title": ".NET 9.0.10 Patch Index",
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
  "sdk_version": "9.0.306",
  "sdk_feature_bands": [
    "9.0.306",
    "9.0.111"
  ],
  "_links": {
    "self": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/9.0.10/index.json"
    },
    "prev": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/9.0.9/index.json",
      "title": "Patch index"
    },
    "prev-security": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/9.0.6/index.json",
      "title": "Latest security patch"
    },
    "latest-sdk": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/sdk/index.json",
      "title": "SDK index"
    },
    "release-major": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/index.json",
      "title": "Major version index"
    },
    "release-month": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/index.json",
      "title": "Release month index"
    },
    "releases-index": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/index.json",
      "title": "Release index"
    },
    "cve-json": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/cve.json",
      "title": "CVE records (JSON)",
      "type": "application/json"
    },
    "cve-markdown": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/cve.md",
      "title": "CVE records (JSON)",
      "type": "application/markdown"
    },
    "cve-markdown-rendered": {
      "href": "https://github.com/dotnet/core/blob/main/release-notes/timeline/2025/10/cve.md",
      "title": "CVE records (JSON) (Rendered)",
      "type": "application/markdown"
    },
    "release-json": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/9.0.10/release.json",
      "title": "9.0.10 Release Information",
      "type": "application/json"
    },
    "release-notes-markdown": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/9.0.10/9.0.10.md",
      "title": "Release Notes",
      "type": "application/markdown"
    },
    "release-notes-markdown-rendered": {
      "href": "https://github.com/dotnet/core/blob/main/release-notes/9.0/9.0.10/9.0.10.md",
      "title": "Release Notes (Rendered)",
      "type": "application/markdown"
    }
  },
```

This content looks much the same as we saw earlier, except that much of the content we saw in the patch object is now exposed at index root. That's not coincidental, but a key aspect of the model.

The `prev` link relation provides another wormhole, this time to a less distant target. A `next` relation isn't provided because it would break the immutability goal. In addition, the combination of a `latest*` property and `prev` links satisfies many scenarios.

The `latest-sdk` target provides access to `aka.ms` evergreen SDK links and other SDK-related information. The `release-month` and `cve-json` links are still there, but a bit further down the dictionary definition as to what's copied above.

The `_embedded` property contains two children: `sdk` and `disclosures`.

```json
  "_embedded": {
    "sdk": [
      {
        "version": "9.0.306",
        "_links": {
          "feature-band": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/sdk/sdk-9.0.3xx.json",
            "path": "/9.0/sdk/sdk-9.0.3xx.json",
            "title": ".NET SDK 9.0.3xx",
            "type": "application/json"
          },
          "release-notes-markdown": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/9.0.10/9.0.10.md",
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
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/sdk/sdk-9.0.1xx.json",
            "path": "/9.0/sdk/sdk-9.0.1xx.json",
            "title": ".NET SDK 9.0.1xx",
            "type": "application/json"
          },
          "release-notes-markdown": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/9.0.10/9.0.111.md",
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

Note: `sdks` should probably be plural.

First-class treatment is provided for SDK releaes, both a root and in `_embedded`. That said, it is obvious that we don't quite do the right thing with release notes. It is very odd that the "best" SDK has to share release notes with the runtime.

There is an `sdk*.json` that matches each feature band. It is largely the same as `sdk/index.json` but more specific. The rest of the links are for markdown release notes.

Note: There is currently no runtime variant of `sdk/index.json`. This needs to be resolved and may inspire changes in the SDK design. The SDK design is still a bit shaky.

Any CVEs for the month are described in `disclosures`. This data provides a useful pre-baked view on data that from `cve.json`.

It's possible to make detail-oriented compliance and deployment decisions based on this information. There's even a commit for the CVE fix with an LLM friendly link style. This is the bottom part of the hypermedia graph. It's far more shapely and weighty than the root. If a consumer gets this far, it is likely because they need access to the exposed information. If they only want access to the `cve.json` file, it is exposed in the major version index.

## Timeline Modeling

The timeline is much the same. The key difference is that the version index converges to a point while the timeline index converges to a slice or row of points.

### Timeline Index

The root of the [timeline index](https://github.com/dotnet/core/blob/release-index/release-notes/timeline/index.json) is almost identical to the releases index, with `timeline-index` being inverted into `releases-index`.

```json
{
  "$schema": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/schemas/dotnet-release-timeline-index.json",
  "kind": "timeline-index",
  "title": ".NET Release Timeline Index",
  "description": ".NET Release Timeline (latest: 10.0)",
  "latest": "10.0",
  "latest_lts": "10.0",
  "latest_year": "2025",
  "_links": {
    "self": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/index.json",
      "path": "/timeline/index.json",
      "title": ".NET Release Timeline Index",
      "type": "application/hal\u002Bjson"
    },
    "latest": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/index.json",
      "path": "/10.0/index.json",
      "title": "Latest .NET release (.NET 10.0)",
      "type": "application/hal\u002Bjson"
    },
    "latest-lts": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/index.json",
      "path": "/10.0/index.json",
      "title": "Latest LTS release (.NET 10.0)",
      "type": "application/hal\u002Bjson"
    },
    "latest-year": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/index.json",
      "path": "/timeline/2025/index.json",
      "title": "Latest year (2025)",
      "type": "application/hal\u002Bjson"
    },
    "releases-index": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/index.json",
      "path": "/index.json",
      "title": ".NET Release Index",
      "type": "application/hal\u002Bjson"
    }
  },
```

The `_embedded` section naturally contains `years`.

```json
  "_embedded": {
    "years": [
      {
        "year": "2025",
        "description": ".NET release timeline for 2025",
        "releases": [
          "10.0",
          "9.0",
          "8.0"
        ],
        "_links": {
          "self": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/index.json",
            "path": "/timeline/2025/index.json",
            "title": "Release timeline index for 2025",
            "type": "application/hal\u002Bjson"
          }
        }
      },
      {
        "year": "2024",
        "description": ".NET release timeline for 2024",
        "releases": [
          "9.0",
          "8.0",
          "7.0",
          "6.0"
        ],
        "_links": {
          "self": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2024/index.json",
            "path": "/timeline/2024/index.json",
            "title": "Release timeline index for 2024",
            "type": "application/hal\u002Bjson"
          }
        }
      },
```

It also provides a helpful join with the active (not neccessarily supported) releases for that year. This baked-in query helps some workflows.

This index file similarly avoid fast-moving currency as the root releases index.

### Year Index

The year index follows much the same pattern as the major version index. The year objects you see above become the root of the index. This is [year index for 2025](https://github.com/dotnet/core/blob/release-index/release-notes/timeline/2025/index.json).

```json
{
  "$schema": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/schemas/dotnet-release-timeline-index.json",
  "kind": "year-index",
  "title": ".NET Release Timeline Index - 2025",
  "description": "Release timeline for 2025 (latest: 10.0)",
  "year": "2025",
  "latest_month": "11",
  "latest_security_month": "10",
  "latest_release": "10.0",
  "releases": [
    "10.0",
    "9.0",
    "8.0"
  ],
  "_links": {
    "self": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/index.json",
      "path": "/timeline/2025/index.json",
      "title": "Release timeline index for 2025",
      "type": "application/hal\u002Bjson"
    },
    "prev": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2024/index.json",
      "path": "/timeline/2024/index.json",
      "title": "Release timeline index for 2024",
      "type": "application/hal\u002Bjson"
    },
    "latest-month": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/11/index.json",
      "path": "/timeline/2025/11/index.json",
      "title": "Latest month (Release timeline index for 2025-11)",
      "type": "application/hal\u002Bjson"
    },
    "latest-release": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/index.json",
      "path": "/10.0/index.json",
      "title": "Latest release (.NET 10.0)",
      "type": "application/hal\u002Bjson"
    },
    "latest-security-month": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/index.json",
      "path": "/timeline/2025/10/index.json",
      "title": "Latest security month (Release timeline index for 2025-10)",
      "type": "application/hal\u002Bjson"
    },
    "timeline-index": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/index.json",
      "path": "/timeline/index.json",
      "title": ".NET Release Timeline Index",
      "type": "application/hal\u002Bjson"
    }
  },
```

Very similar approach as other indices.

The `_emdedded` section contains: `months` and `releases`.

```json
  "_embedded": {
    "months": [
      {
        "month": "11",
        "security": false,
        "cve_count": 0,
        "latest_release": "10.0",
        "releases": [
          "10.0",
          "9.0",
          "8.0"
        ],
        "runtime_patches": [
          "10.0.0",
          "9.0.11",
          "8.0.22"
        ],
        "_links": {
          "self": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/11/index.json",
            "path": "/timeline/2025/11/index.json",
            "title": "Release timeline index for 2025-11",
            "type": "application/hal\u002Bjson"
          }
        }
      },
      {
        "month": "10",
        "security": true,
        "cve_count": 3,
        "cve_records": [
          "CVE-2025-55248",
          "CVE-2025-55315",
          "CVE-2025-55247"
        ],
        "latest_release": "9.0",
        "releases": [
          "10.0",
          "9.0",
          "8.0"
        ],
        "runtime_patches": [
          "10.0.0-rc.2.25502.107",
          "9.0.10",
          "8.0.21"
        ],
        "_links": {
          "self": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/index.json",
            "path": "/timeline/2025/10/index.json",
            "title": "Release timeline index for 2025-10",
            "type": "application/hal\u002Bjson"
          },
          "cve-json": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/cve.json",
            "path": "/timeline/2025/10/cve.json",
            "title": "CVE Information",
            "type": "application/json"
          }
        }
      },
```

`releases` looks like the following:

```json
    "releases": [
      {
        "version": "10.0",
        "release_type": "lts",
        "support_phase": "active",
        "supported": true,
        "ga_date": "2025-11-11T00:00:00\u002B00:00",
        "eol_date": "2028-11-14T00:00:00\u002B00:00",
        "_links": {
          "self": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/index.json",
            "path": "/10.0/index.json",
            "title": ".NET 10.0",
            "type": "application/hal\u002Bjson"
          },
          "latest-month": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/11/index.json",
            "path": "/timeline/2025/11/index.json",
            "title": "Latest month (2025-11)",
            "type": "application/hal\u002Bjson"
          },
          "latest-patch": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/10.0.0/index.json",
            "path": "/10.0/10.0.0/index.json",
            "title": "Latest patch (10.0.0)",
            "type": "application/hal\u002Bjson"
          }
        }
      },
```

This is just an inversion on the major version index.

For light-duty compliance tools, the year index likely provides sufficient information.

### Month index

The last index to consider is the month index. This is the month index for [January 2025](https://github.com/dotnet/core/blob/release-index/release-notes/timeline/2025/01/index.json).

```json
{
  "$schema": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/schemas/dotnet-release-timeline-index.json",
  "kind": "month-index",
  "title": ".NET Release Timeline Index - 2025-01",
  "description": "Release timeline for 2025-01 (latest: 9.0)",
  "year": "2025",
  "month": "01",
  "security": true,
  "cve_count": 4,
  "cve_records": [
    "CVE-2025-21171",
    "CVE-2025-21172",
    "CVE-2025-21176",
    "CVE-2025-21173"
  ],
  "latest_release": "9.0",
  "releases": [
    "9.0",
    "8.0"
  ],
  "_links": {
    "self": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/01/index.json",
      "path": "/timeline/2025/01/index.json",
      "title": "Release timeline index for 2025-01",
      "type": "application/hal\u002Bjson"
    },
    "prev": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2024/12/index.json",
      "path": "/timeline/2024/12/index.json",
      "title": "Release timeline index for 2024-12",
      "type": "application/hal\u002Bjson"
    },
    "timeline-index": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/index.json",
      "path": "/timeline/index.json",
      "title": ".NET Release Timeline Index",
      "type": "application/hal\u002Bjson"
    },
    "year-index": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/index.json",
      "path": "/timeline/2025/index.json",
      "title": ".NET Release Timeline Index - 2025",
      "type": "application/hal\u002Bjson"
    },
    "cve-json": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/01/cve.json",
      "path": "/timeline/2025/01/cve.json",
      "title": "CVE Information",
      "type": "application/json"
    },
    "cve-markdown": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/01/cve.md",
      "path": "/timeline/2025/01/cve.md",
      "title": "CVE Information",
      "type": "application/markdown"
    },
    "cve-markdown-rendered": {
      "href": "https://github.com/dotnet/core/blob/main/release-notes/timeline/2025/01/cve.md",
      "path": "/timeline/2025/01/cve.md",
      "title": "CVE Information (Rendered)",
      "type": "application/markdown"
    }
  },
```

This schema also follows the same approach we've seen elsewhere. We also see `prev` wormhold links show up. They cross years, as can be seen in this example. This wormhole links makes backwards `foreach` from `latest-month` trivial.

The `_embedded` property contains: `releases` and `disclosures`.

```json
  "_embedded": {
    "releases": [
      {
        "version": "9.0.1",
        "date": "2025-01-14T00:00:00\u002B00:00",
        "year": "2025",
        "month": "01",
        "security": true,
        "cve_count": 4,
        "cve_records": [
          "CVE-2025-21171",
          "CVE-2025-21172",
          "CVE-2025-21176",
          "CVE-2025-21173"
        ],
        "support_phase": "active",
        "sdk_patches": [
          "9.0.102"
        ],
        "_links": {
          "self": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/9.0.1/index.json",
            "path": "/9.0/9.0.1/index.json",
            "title": ".NET 9.0.1",
            "type": "application/hal\u002Bjson"
          },
          "latest-sdk": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/sdk/index.json",
            "path": "/9.0/sdk/index.json",
            "title": ".NET SDK 9.0 Release Information",
            "type": "application/hal\u002Bjson"
          }
        }
      },
      {
        "version": "8.0.12",
        "date": "2025-01-14T00:00:00\u002B00:00",
        "year": "2025",
        "month": "01",
        "security": true,
        "cve_count": 3,
        "cve_records": [
          "CVE-2025-21172",
          "CVE-2025-21176",
          "CVE-2025-21173"
        ],
        "support_phase": "active",
        "sdk_patches": [
          "8.0.405",
          "8.0.308",
          "8.0.112"
        ],
        "_links": {
          "self": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/8.0/8.0.12/index.json",
            "path": "/8.0/8.0.12/index.json",
            "title": ".NET 8.0.12",
            "type": "application/hal\u002Bjson"
          },
          "latest-sdk": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/8.0/sdk/index.json",
            "path": "/8.0/sdk/index.json",
            "title": ".NET SDK 8.0 Release Information",
            "type": "application/hal\u002Bjson"
          }
        }
      }
    ],
    "disclosures": [
      {
        "id": "CVE-2025-21171",
        "title": ".NET Remote Code Execution Vulnerability",
        "_links": {
          "self": {
            "href": "https://github.com/dotnet/announcements/issues/340",
            "title": "CVE-2025-21171"
          }
        },
        "fixes": [
          {
            "href": "https://github.com/dotnet/runtime/commit/9da8c6a4a6ea03054e776275d3fd5c752897842e.diff",
            "repo": "dotnet/runtime",
            "branch": "release/9.0",
            "title": "Fix commit in runtime (release/9.0)",
            "release": "9.0"
          }
        ],
        "cvss_score": 7.5,
        "cvss_severity": "HIGH",
        "disclosure_date": "2025-01-14",
        "affected_releases": [
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

It was stated earlier that the version indexes converges to a point while the timeline index coverges to a row of points. We see that on display here. Otherwise, this is a variation of what we saw in the patch version index.

## Design tradeoffs

There are lots of design tradeoffs within the graph design. Ergonomics vs update velocity were perhaps the most challenging constraints to balance.

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

That would be _so nice_. Consumers could wormhole to high value content without needing to go through the major version index or or year to get to the intended target. From a query ergonomics standpoint, this structure would be superior. From a file-size standpoint, it would be acceptable.

This approach doesn't violate the consistency rules. There is no badly-behaved currency that can be mis-used. The links are opague and notably target immutable resources. So, why not? Why can't we have nice things?

The issue is that these high-value links would require updating the root index once a month. Regular updates of a high-value resource signficantly increase the likelihood of an outage and reduces the time that the root index can be cached. Cache aggressiveness is part of the performance equation. It's much better to keep the root lean (skeletal) and highly cacheable.

On aspect that has (somewhat) haunted the design is deciding if it is appropriate to add a preview version to the high-value index for something as NOT mission critical as .NET 11 preview 1 (for example). On one hand, the answer is "definitely not". On another, by adding an `11.0` link in February, it is much more likely that all caches will have a root `index.json` with an  11.0 link, added in February, by November. We have to add the new major version sometime. Might as well add it at first availability, avoid a "has to be right" change on GA day, and ensure that all caches have the data they need when the special day comes.

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

## Modeling as validation

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
- **Boolean vs enum:** The hal-index uses `supported: true`, a simple boolean. The releases-index pnly exposes `support-phase: "active"` (with hal-index also has), requiring knowledge of the enum vocabulary (active, maintenance, eol, preview, go-live).
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

## Cache-Control TTL Recommendations

Time to Live (TTL) values are calibrated for "safe but maximally aggressive" caching based on update frequency and consistency requirements.

### TTL Summary

| Resource | Example Path | Update Frequency | Recommended TTL | Cache-Control Header |
|----------|--------------|------------------|-----------------|---------------------|
| Root index | `/index.json` | ~1×/year | 7 days | `public, max-age=604800` |
| Timeline root | `/timeline/index.json` | ~1×/year | 7 days | `public, max-age=604800` |
| Year index | `/timeline/2025/index.json` | ~12×/year | 4 hours | `public, max-age=14400` |
| Month index | `/timeline/2025/10/index.json` | Never (immutable) | 1 year | `public, max-age=31536000, immutable` |
| Major version index | `/9.0/index.json` | ~12×/year | 4 hours | `public, max-age=14400` |
| Patch version index | `/9.0/9.0.10/index.json` | Never (immutable) | 1 year | `public, max-age=31536000, immutable` |
| SDK index | `/9.0/sdk/index.json` | ~12×/year | 4 hours | `public, max-age=14400` |
| Exit nodes (CVE, markdown) | `/timeline/2025/10/cve.json` | Never (immutable) | 1 year | `public, max-age=31536000, immutable` |

This doesn't take SDK-only releases or mistakes into account.

## Rationale

**Root and timeline root (7 days):** These change approximately once per year. A 7-day TTL provides strong caching while ensuring that when a new major version is added (even as a preview in February), propagation completes well before it matters. Worst case: a consumer sees stale data for a week after a yearly update.

**Year and major version indexes (4 hours):** These update monthly, typically on Patch Tuesday. A 4-hour TTL balances freshness against CDN load. On release day, caches will converge within a business day. Between releases, these are effectively immutable and the 4-hour TTL is conservative.

**Immutable resources (1 year + immutable directive):** Patch indexes, month indexes, and exit nodes are never modified after creation. The `immutable` directive tells browsers and CDNs to never revalidate—the URL is the version. One year is the practical maximum; longer provides no benefit.

## LLM Consumption

A major focus of 

Spec for LLMs: <https://raw.githubusercontent.com/dotnet/core/release-index/llms.txt>
