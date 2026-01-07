# Exposing Release Notes as an Information graph

The .NET project has published [release notes in JSON and markdown](https://github.com/dotnet/core/tree/main/release-notes) for many years. The investment in quality release notes has been based on the cloud-era idea that many deployment and compliance workflows require detailed structured data to safely operate at scale. For the most part, a highly clustered set of consumers (like GitHub Actions and vulnerability scanners) have adapted _their tools_ to _our formats_ to offer higher-level value to their users. The LLM era is strikingly different where a much smaller set of information systems (LLM model companies) _consume and expose diverse data in standard ways_ according to _their (shifting) paradigms_ to a much larger set of users. The task at hand is to modernize release notes to make them more efficient to consume and to adapt them for LLM consumption.

Overall goals for release notes consumption:

- Graph schema encodes graph update frequency
- Satisfy reasonable expectations of performance (no 1MB JSON files), reliability, and consistency
- Enable aesthetically pleasing queries that are terse, ergonomic, and effective, both for their own goals and as a proxy for LLM consumption.
- Support queries with multiple key styles, temporal and version-based (runtime and SDK versions) queries.
- Expose queryable data beyond version numbers, such as CVE disclosures, breaking changes, and download links.
- Use the same data to generate most release note markdown files, like [releases.md](https://github.com/dotnet/core/blob/main/releases.md), and CVE announcements like [CVE-2025-55248](https://github.com/dotnet/announcements/issues/372), ensuring consistency from a single source of truth.
- Use this project as a real-world information graph pilot to inform other efforts that expose information to modern information consumers.

## Scenario

Release notes are mechanism, not scenario. It is likely difficult for users to keep up with and act on the constant stream of .NET updates, typically one or two times a month. Users often have more than one .NET major version deployed, further complicating this puzzle. Many users rely on update orchestrators like APT, Yum, and Visual Studio, however, it is unlikely that such tools cover all the end-points that users care about in a uniform way. It is important that users can reliably make good, straightforward, and timely decisions about their entire infrastructure, orchestrated across a variety of deployment tools. This is a key scenario that release notes serve.

Obvious questions release notes should answer:

- What has changed, since last month, since the last _.NET_ update, or since the last _user_ update.
- How many patches back is this machine?
- How/where can new builds be acquired
- Is a recent update more critical to deploy than "staying current"?
- How long until a given major release is EOL or has been EOL?
- What are known upgrade challenges?

CIOs, CTOs, and others are accountable for maintaining efficient and secure continuity for a set of endpoints, including end-user desktops and cloud servers. They are unlikely to read [long markdown release notes](https://github.com/dotnet/core/blob/main/release-notes/10.0/10.0.1/10.0.1.md) or perform DIY `curl` + `jq` hacking with [structured data](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json). They will increasingly expect to be able to get answers to arbitrarily detailed compliance and deployment questions using chat assistants like Copilot. They may ask Claude to compare treatment of an industry-wide CVE like [CVE-2023-44487](https://nvd.nist.gov/vuln/detail/cve-2023-44487) across multiple application stacks in their portfolio. This already works reasonably well, but fails when prompts demand greater levels of detail and with the expectation that the source data comes from authoritative sources. It is very common to see assistants glean insight from a semi-arbitrary set of web pages with diverse authority. This is particularly problematic for day-of prompts (same day as a security release).

Some users have told us that they enable Slack notifications for [dotnet/announcements](https://github.com/dotnet/announcements/issues), which is an existing "release notes beacon". That's great and intended. What if we could take that to a new level, thinking of release notes as queryable data used by notification systems and LLMs? There is a lesson here. Users (virtuously) complain when we [forget to lock issues](https://github.com/dotnet/announcements/issues/107#issuecomment-482166428). They value high signal to noise. Fortunately, we no longer forget for announcements, but we have not achieved this same disciplined model with GitHub release notes commits (as will be covered later). It should be just as safe and reliable to use release notes updates as a beacon as dotnet/announcements.

LLMs are a different kind of "user" than we've previously tried to support. LLMs are _much more_ fickle relative to purpose-built tools. They are more likely to give up if release notes are not to their liking, instead relying on model knowledge or comfortable web search with its equal share of benefits and challenges. Regular testing (by the spec writer) of release notes with chat assistants has demonstrated that LLMs are typically only satiated by a "Goldilocks meal". Obscure formats, large documents, and complicated workflows don't perform well (or outright fail). LLMs will happily jump to [`releases-index.json`](https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json) and choke on the 1MB+ [`releases.json`](https://builds.dotnet.microsoft.com/dotnet/release-metadata/6.0/releases.json) files we maintain if prompts are unable to keep their attention.

![.NET 6.0 releases.json file as tokens](./releases-json-tokens.png)

This image shows that the worst case for the `releases.json` format is 600k tokens using the [OpenAI Tokenzier](https://platform.openai.com/tokenizer). It is an understatement to say that a file of that size doesn't work well with LLMs. Context: memory budgets tend to max out at 200k tokens. Large JSON files can be made to work in some scenarios, but not in the general case.

A strong belief is that workflows that are bad for LLMS are typically not _uniquely_ bad for LLMs but are challenging for most consumers. It is easy to guess that most readers of `releases-index.json` would be better-served by referenced JSON significantly less than 1MB+. This means that we need start from scratch with structured release notes.

In the early revisions of this project, the design followed our existing schema playbook, modeling parent/child relationships, linking to more detailed sources of information, and describing information domains in custom schemas. That then progressed into wanting to expose summaries of high-value information from leaf nodes into the trunk. This approach didn't work well since the design was lacking a broader information architecture. A colleague noted that the design was [Hypermedia as the engine of application state (HATEOAS)](https://en.wikipedia.org/wiki/HATEOAS)-esque but not using one of the standard formats. The benefits of using standard formats is that they are free to use, have gone through extensive design review, can be navigated with standard patterns and tools, and (most importantly) LLMs already understand their vocabulary and access patterns. A new format will by definition not have those characteristics.

This proposal leans heavily on hypermedia, specifically [HAL+JSON](https://datatracker.ietf.org/doc/html/draft-kelly-json-hal). Hypermedia is very common, with [OpenAPI](https://www.openapis.org/what-is-openapi) likely being the most common. LLMs are quite comfortable with these standards. The proposed use of HAL is a bit novel (meaning niche). It is inspired by [llms.txt](https://llmstxt.org/) as an emerging standard and the idea that hypermedia is the most natural next step to express complex diverse data and relationships. It's also expected (in fact, pre-ordained) that older standards will perform better than newer ones due to higher density (or presence at all) in the LLM training data.

## Hypermedia graph design

This project has adopted the idea that a wide and deep information graph can expose significant information within the graph that satisfies user queries without loading other files. The graph doesn't need to be skeletal. It can have some shape on it. In fact our existing graph with [`release-index.json`](https://github.com/dotnet/core/blob/main/release-notes/releases-index.json) already does this, but without the benefit of a standard format or architectural principles.

The design intent is that a graph should be skeletal at its roots for performance and to avoid punishing queries that do not benefit from the curated shape. The deeper the node is in the graph, the more shape or weight it should take on since the data curation is much more likely to hit the mark.

Hypermedia formats have a long history of satisfying this methodology, long pre-dating, and actually inspiring the World Wide Web and its Hypertext Markup Language (HTML). This project uses [HAL+JSON](https://en.wikipedia.org/wiki/Hypertext_Application_Language) as the "graph format". HAL is a sort of a "hypermedia in a nutshell" schema, initially drafted in 2012. You can develop a basic understanding of HAL in about two minutes because it has a very limited syntax.

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

The `_embedded` property contains order resources. This is the resource payload. Each of those order items have `self` and other related link relations referencing other resources. As stated earlier, the `self` relation references the canonical copy of the resource. Embedded resources may be a full or partial copy of the resource. Again, domain-specific readers will understand this schema and know how to process it.

This design aspect is the true strength of HAL, of projecting partial views of resources to their reference. It's the mechanism that enables the overall approach of a skeletal root with weighted leaves. It's also what enables these two seemingly anemic properties to provide so much modeling value.

The `currentlyProcessing` and `shippedToday` properties provide additional information about ongoing operations.

Hat tip to [Mike Kelly](https://github.com/mikekelly) for sharing such a simple yet highly effective hypermedia design with the world.

We can now look at how the same vibe can be applied to .NET release notes.

## Releases-index schema

It's important to take a quick look at the [`releases-index.json`](https://raw.githubusercontent.com/dotnet/core/refs/heads/main/release-notes/releases-index.json) schema, to understand our current baseline. It is the schema that this new design is replacing.

```json
{
  "$schema": "https://json.schemastore.org/dotnet-releases-index.json",
  "releases-index": [
    {
      "channel-version": "10.0",
      "latest-release": "10.0.1",
      "latest-release-date": "2025-12-09",
      "security": false,
      "latest-runtime": "10.0.1",
      "latest-sdk": "10.0.101",
      "product": ".NET",
      "support-phase": "active",
      "eol-date": "2028-11-14",
      "release-type": "lts",
      "releases.json": "https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json",
      "supported-os.json": "https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/supported-os.json"
    },
    {
      "channel-version": "9.0",
      "latest-release": "9.0.11",
      "latest-release-date": "2025-11-19",
      "security": false,
      "latest-runtime": "9.0.11",
      "latest-sdk": "9.0.308",
      "product": ".NET",
      "support-phase": "active",
      "eol-date": "2026-11-10",
      "release-type": "sts",
      "releases.json": "https://builds.dotnet.microsoft.com/dotnet/release-metadata/9.0/releases.json",
      "supported-os.json": "https://builds.dotnet.microsoft.com/dotnet/release-metadata/9.0/supported-os.json"
    },
```

Note: `releases-index.json` will continue to be updated. However, it will be deprecated as the preferred solution.

The basis of this design is that `releases-index.json` is no longer sufficient for our needs. Keep that assumption in mind while considering the rest of the spec.

## Release Notes Graph

Release notes naturally describe two information dimensions: time and product version.

- Within time, we have years, months, and ship days.
- Within version, we have major and patch version. We also have runtime and SDK version.

These dimensional characteristics form the load-bearing structure of the graph to which everything else is attached. The new graph exposes both timeline and version indices. We've previously only had a version index.

The following table summarizes the overall shape of the graph, starting at `dotnet/core:release-notes/index.json`.

| File | Type | Size | Frequency | Updates When... |
| --- | --- | --- | --- | --- |
| `index.json` (root) | Release version index | 4.7 KB | 1-2x/year | New major version, support bool changes |
| `timeline/index.json` | Release timeline index | 4.7 KB | 2x/year | New year starts, new major version |
| `timeline/{year}/index.json` | Year index | 6.5 KB | 12x/year | New month with activity, phase changes |
| `timeline/{year}/{month}/index.json` | Month index | 5.0 KB | 0 | Never (immutable after creation) |
| `{version}/index.json` | Major version index | 25 KB | 12x/year | New patch release |
| `{version}/{patch}/index.json` | Patch version index | 5.3 KB | 0 | Never (immutable after creation) |
| `llms.json` | AI-optimized index | 8.0 KB | 12x/year | New patch release, support status changes |

**Summary:** Cold roots, warm branches, immutable leaves.

Notes:

- 2025 was used for `{year}`, 10 for `{month}`, 8.0 for `{version}`, and 8.0.21 for `{patch}`
- The bottom of the graph has the largest variance over time. `8.0.21/index.json` is `5.3` KB while `8.0.22/index.json` is `4.8` KB, a little smaller because it was a non-security release. Similarly, `2025/10/index.json` is `5.0` KB while `2025/11/index.json` is `3.0` KB, significantly smaller because there was no security release that month. `2025/12/index.json` was even smaller because there was only a .NET 10 patch, also non-security. Security releases were chosen to demonstrate more realistic sizes.
- `llms.json` will be covered more later.

We can contrast this approach with the existing release graph.

| File | Type | Size | Frequency | Updates When... |
| --- | --- | --- | --- | --- |
| `releases-index.json` (root) | Release version index | 6.3 KB | 18x/year | Every patch release |
| `{version}/releases.json` | Major Version Index | 1.2 MB | 18x/year | Every patch release |

Notes:

- 8.0 was used for `{version}`. 6.0 `releases.json` is 1.6 MB.
- Frequency is 18x a year to allow for SDK-only releases that occur after Patch Tuesday.

It is straightforward to see that the new graph design enables access to far more information before hitting a data wall. In fact, in experiments, LLMs reliably hit an HTTP `409` error code as soon as they try to read [`releases.json`](https://builds.dotnet.microsoft.com/dotnet/release-metadata/8.0/releases.json"). It hits the LLM context limit, or similar.

The last 12 months of commit data (as of Nov 2025) demonstrates a higher than expected update rate.

| File | Commits | Notes |
| ---- | ------- | ----- |
| `releases-index.json` | 29 | Root index (all versions) |
| `10.0/releases.json` | 22 | Includes previews/RCs and SDK-only releases |
| `9.0/releases.json` | 24 | Includes SDK-only releases, fixes, URL rewrites |
| `8.0/releases.json` | 18 | Includes SDK-only releases, fixes, URL rewrites |

**Summary:** Hot everything.

The commit counts are not good. The `releases-index.json` file is a mission-critical live-site resource. 29 updates is > 2x/month!

### Graph consistency

The graph has one rule:

> Every resource in the graph needs to be guaranteed consistent with every other part of the graph.

The underlying problem is Content Delivery Network (CDN) caching. Assume that the entire graph is consistent when uploaded to an origin server. A CDN server is guaranteed by construction to serve both old and new copies of the graph -- for existing files that have been updated -- leading to potential inconsistencies. The graph construction needs to be resilient to that.

Related examples:

- <https://github.com/dotnet/core/issues/9200>
- <https://github.com/dotnet/core/issues/10082>

Today, we publish `releases-index.json` as the root of our release notes graph. Some users read this JSON file to learn the latest patch version numbers, while others navigate deeper into the graph. Both are legitimate patterns. However, we've found that our approach has fundamental flaws.

Problems:

- Exposing patch versions in multiple files that need to agree is incompatible with using a CND that employs standard caching (expiration / TTL).
- The `releases-index.json` file is a critical live site resource driving 1000s of GBs of downloads a month, yet we update it multiple times a month, by virtue of the data it exposes.

It's hard to understate the impact of schema design on file update frequency. Files that expose properties with patch versions (scalars or links) inherently require updates on the patch schedule.

Solution:

- Fast changing currency (like patch version numbers) are exposed in (at most) a single resource in the graph, and never at the root.
- The root index file is updated once or twice a year (to add the presence of a new major release and change support status of newly EOL versions; releases come in and go out, typically not on the same day although that [assumption has changed](https://devblogs.microsoft.com/dotnet/dotnet-sts-releases-supported-for-24-months/)).

The point about the root index isn't a _solution_ but an _implication_ of the first point. If the root index isn't allowed to contain fast-moving currency, in part because it is present in another resource, then it is stripped of its reason to change.

There are videos on YouTube with these [crazy gear reductions](https://www.youtube.com/watch?v=QwXK4e4uqXY). You can watch them for a long time! Keen observers will realize our graph will look nothing like that. It's a metaphor. We can model years and months and major and patch versions as spinning gears with a differing number of teeth and revolution times. The remaining design question is whether we like the rate that our gears spin.

A stellar orbit analogy would have worked just as well. The root `index.json` is the star. It, too, is subject to an orbit. It's just a [lot slower](https://en.wikipedia.org/wiki/Galactic_year).

Release notes graph indexes are updated (gear reduce) per the following:

- Timeline index (list of years): one to two updates per year
- Year index (list of months): one update per month
- Month index (list of patches across versions): No updates (immutable)

The same progression for versions:

- Releases index (list of of major versions): one to two updates per year
- Major version index (list of patches): one update per month
- Patch version index (details about a patch): No updates (immutable)

It's the middle section changing constantly, but the roots and the leaves are either immutable or close enough to it.

Note: Some annoying details, like SDK-only releases, have been ignored. The intent is to reason about rough order of magnitude and the fundamental pressure being applied to each layer.

A key question about this scheme is when we should a add new major releases, like `12.0`. The most obvious answer to add new releases at Preview 1. They exist! The other end of the spectrum would be at GA. From a mission-critical standpoint, GA sounds better. Indeed, the root `index.json` file is intended as a mission-critical resource. So, we should add an entry for `12.0` when it is needed and can be acted on for production use. Case closed! Unintuitively, this approach is likely a priority inversion.

We should add vNext releases at Preview 1 for the following reasons:

- vNext is available (in preview form), we so we should advertise it.
- Special once-in-a-release tasks are more likely to fail when done on the very busy and critical GA day.
- Adding vNext early enables consumers to cache aggressively.

The last point clinches it. The intent is that root `index.json` can be cached aggressively. Adding vNext to `index.json` with Preview 1 is perfectly aligned with that. Adding vNext at GA is not. This practice supports caching at weeks-long scale (or longer).

### Graph wormholes

There are many design paradigms and tradeoffs one can consider with a graph like this. A major focus is the "skeletal root" vs "weighted leaves" design point, discussed earlier. This approach forces significant fetches and inefficiency to get to _any_ useful data, unlike `releases-index.json`. To mitigate that, the graph includes helpful workflow-specific wormhole links. These wormholes are somewhat emergent, somewhat obvious to expose due to the otherwise challenging nature of the design.

**Wormhole links** jump across the graph—from a patch version to its release month, skipping intermediate navigation. `latest-lts` teleports to the current LTS release. As the name suggests, these links enable jumping from one part of the graph to another, creating ergonomics for expected queries.

**Spear-fishing** is a wormhole variant targeting timely, high-value content deep in the graph. `latest-security-disclosures` points directly to CVE information with a short half-life, where freshness defines value.

The wormhole links take on a different character at each layer of the graph.

Cold roots:

- `latest` and `latest-lts` -- enables jumping to the matching major version index.
- `latest-year` -- enables jumping to the latest year index.

Warm branches:

- `latest` and `latest-security` -- enables jumping to the matching patch version index.
- `latest-month` and `latest-security-month` -- enables jumping to the latest matching month.

Immutable leaves:

- `prev` and `prev-security` -- enables jumping to an earlier matching patch version or month index.

For some scenarios, it can be efficient to jump to `latest-security-month` and then backwards in time via `prev-security`. `prev` and `prev-security` jump across year boundaries, reducing the logic required to use the scheme.

The wormhole links are what make the graph a graph and not just two related trees providing alternative views of the same data. They also enable efficient navigation. Testing has borne this out, demonstrating that wormhole links are one of the defining features of the graph.

Notes:

- There are other such wormhole links. These are the primary ones, which also best demonstrate the idea.
- The wormhole links cannot (per policy) force an update schedule beyond what the file would naturally allow.
- For the cold roots, a wormhole like `latest-lts` is the best we can do.
- There is no `next` or `next-security`. `prev` and `prev-security` link immutable leaves, establishing a linked-list based on past knowledge. It's not possible to provide `next` links given the immutability constraint.
- There is no `latest-sts` link because it's not really useful. `latest` covers the same need sufficiently.

## Version Index Modeling

The version index has three layers: releases, major version, patch version. Most nodes in the graph are named `index.json`. The examples should look similar to the HAL spec documents shared earlier.

### Releases index

The root `index.json` file represents all .NET versions. It is can be viewed as a stripped-down version of the existing `releases-index.json`.

Source: <https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/index.json>

```json
{
  "$schema": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/schemas/v1/dotnet-release-version-index.json",
  "kind": "root",
  "title": ".NET Release Index",
  "latest_major": "10.0",
  "latest_lts_major": "10.0",
  "_links": {
    "self": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/index.json"
    },
    "latest-lts-major": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/index.json",
      "title": "Latest LTS major release - .NET 10.0"
    },
    "latest-major": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/index.json",
      "title": "Latest major release - .NET 10.0"
    },
    "timeline": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/index.json",
      "title": ".NET Release Timeline Index"
    }
  },
```

Key points:

- Schema reference is included
- All links are raw content (will eventually transition to `builds.dotnet.microsoft.com`).
- `kind` and `title` describe the resource
- `latest_major` and `latest_lts_major` describe high-level resource metadata, often useful currency that helps contextualize the rest of the resource without the need to parse/split strings. For example, the `latest_lts_major` scalar describes the target of the `latest-lts-major` link relation. Notably, there are no three-part (patch) versions, like `latest_lts_patch`.
- `timeline-index` provides a wormhole link to another part of the graph, which provides a temporal view of .NET releases.
- Core schema syntax, like `latest_lts_major`, uses snake-case-lower for query ergonomics (using `jq` as the proxy for that).
- Link relations. like `latest-lts-major`, use kebab-case-lower since they can be names or brands.
- The difference in naming follows the approach used by [cve-schema](https://github.com/dotnet/designs/blob/main/accepted/2025/cve-schema/cve_schema.md#brand-names-vs-schema-fields-mixed-naming-strategy).

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

One layer lower, we have the major version index, using .NET 9 as the example.

Source: <https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/index.json>

```json
{
  "$schema": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/schemas/v1/dotnet-release-version-index.json",
  "kind": "major",
  "title": ".NET Major Release Index - 9.0",
  "target_framework": "net9.0",
  "latest_patch": "9.0.11",
  "latest_patch_date": "2025-11-19T00:00:00\u002B00:00",
  "latest_security_patch": "9.0.10",
  "latest_security_patch_date": "2025-10-14T00:00:00\u002B00:00",
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
      "title": "Downloads - .NET 9.0"
    },
    "latest-month": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/11/index.json",
      "title": "Latest month - November 2025"
    },
    "latest-patch": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/9.0.11/index.json",
      "title": "Latest patch - 9.0.11"
    },
    "latest-security-disclosures": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/index.json",
      "title": "Latest security disclosures - October 2025"
    },
    "latest-security-month": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/index.json",
      "title": "Latest security month - October 2025"
    },
    "latest-security-patch": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/9.0.10/index.json",
      "title": "Latest security patch - 9.0.10"
    },
    "manifest": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/manifest.json",
      "title": "Manifest - .NET 9.0"
    },
    "root": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/index.json",
      "title": ".NET Release Index"
    },
    "latest-cve-json": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/cve.json",
      "title": "Latest CVE records - October 2025",
      "type": "application/json"
    }
  },
```

This index includes much more useful and detailed information, both metadata/currency and patch-version links. It starts to answer the question of "what should I care about _now_?".

Much of the form is similar to the root index. Instead of `latest-lts-major`, there is `latest-security-month`. The `manifest` relation stores important but lower-tier content about a given major release. That will be covered shortly.

The `_embedded` property includes: `patches`.

```json
  "_embedded": {
    "patches": [
      {
        "version": "9.0.11",
        "date": "2025-11-19T00:00:00\u002B00:00",
        "year": "2025",
        "month": "11",
        "security": false,
        "support_phase": "active",
        "sdk_version": "9.0.308",
        "_links": {
          "self": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/9.0.11/index.json"
          },
          "month": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/11/index.json"
          }
        }
      },
      {
        "version": "9.0.10",
        "date": "2025-10-14T00:00:00\u002B00:00",
        "year": "2025",
        "month": "10",
        "security": true,
        "support_phase": "active",
        "sdk_version": "9.0.306",
        "_links": {
          "self": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/9.0.10/index.json"
          },
          "month": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/index.json"
          },
          "security-disclosures": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/index.json"
          },
          "cve-json": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/cve.json",
            "type": "application/json"
          }
        }
      },
```

The `patches` object contains detailed information that can drive deployment and compliance workflows. The first two link relations, `self` and `month` are HAL links while `cve-json` is a plain JSON link. Non-HAL links end in the given format, like `-json`, `-markdown`, or `-html`. The links are raw text by default, with rendered `-html` content being useful for content targeted for human consumption, for example as links in generated release notes.

Link relations titles were initially included for all links. The cost of the duplicated text within `_embedded` arrays and dictionaries adds up fast. A design decision was made to include titles only for root link relations and to rely on a trickle-down understanding (since many of the relation names are the same or similar) and skills guidance.

The `month` relation is another wormhole link to the month resource that includes this patch release. Direct access to high-relevance and graph-distant resources avoids awkward indirections, multiple network hops, and wasted bytes/tokens to acquire. The `security-disclosures` relation is an alias for `month`, included for security patches. It is provided for LLMs, which have been observed to find a semantic match more attractive when responding to security-related prompts.

There is a link to `cve.json`. Our [CVE schema](https://github.com/dotnet/designs/blob/main/accepted/2025/cve-schema/cve_schema.md) is a custom schema with no HAL vocabulary. It's an exit node of the graph. The point is that we're free to describe complex domains, like CVE disclosures, using a clean-slate design methodology. Much of the `cve.json` information has been projected into the `month` node, adding high-value shape over the skeleton.

As stated, there is a lot more useful detailed currency on offer. However (as mentioned earlier), there is a rule that currency needs to be guaranteed consistent. Let's consider if the rule is obeyed. The important characteristic is that listed versions and links _within_ the resource are consistent by virtue of being _captured_ in the same file.

The critical trick is with the link relations. In the case of the `month` link, the link origin is a fast moving resource (warm branch) while the link target is immutable. That combination works. It's easy to be consistent with something immutable. It will either exist or not, in the expected form. In contrast, there would be a problem if there was a link between two mutable resources that expose the same currency. This is the problem that `releases-index.json` has. This is similar to data races due to global mutable state in programming language discussions.

Next is `manifest.json`. It contains extra data that are helpful in second-tier scenarios.

Source: <https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/manifest.json>

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
    "compatibility-html": {
      "href": "https://learn.microsoft.com/dotnet/core/compatibility/9.0",
      "title": "Breaking changes in .NET 9",
      "type": "text/html"
    },
    "downloads-html": {
      "href": "https://dotnet.microsoft.com/download/dotnet/9.0",
      "title": ".NET 9 Downloads",
      "type": "text/html"
    },
    "os-packages-json": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/os-packages.json",
      "title": "OS Packages",
      "type": "application/json"
    },
    "release-blog-html": {
      "href": "https://devblogs.microsoft.com/dotnet/announcing-dotnet-9/",
      "title": "Announcing .NET 9",
      "type": "text/html"
    },
    "supported-os-html": {
      "href": "https://github.com/dotnet/core/blob/main/release-notes/9.0/supported-os.md",
      "title": "Supported OSes (HTML)",
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
    "target-frameworks": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/target-frameworks.json",
      "title": "Target Frameworks",
      "type": "application/json"
    },
    "usage-html": {
      "href": "https://github.com/dotnet/core/blob/main/release-notes/9.0/README.md",
      "title": "Release Notes (HTML)",
      "type": "text/html"
    },
    "whats-new": {
      "href": "https://raw.githubusercontent.com/dotnet/docs/main/docs/core/whats-new/dotnet-9/overview.md",
      "title": "What\u0027s new in .NET 9",
      "type": "application/markdown"
    },
    "whats-new-html": {
      "href": "https://learn.microsoft.com/dotnet/core/whats-new/dotnet-9/overview",
      "title": "What\u0027s new in .NET 9",
      "type": "text/html"
    },
    "whats-new-libraries": {
      "href": "https://raw.githubusercontent.com/dotnet/docs/main/docs/core/whats-new/dotnet-9/libraries.md",
      "title": "What\u0027s new in .NET libraries for .NET 9",
      "type": "application/markdown"
    },
    "whats-new-runtime": {
      "href": "https://raw.githubusercontent.com/dotnet/docs/main/docs/core/whats-new/dotnet-9/runtime.md",
      "title": "What\u0027s new in the .NET 9 runtime",
      "type": "application/markdown"
    },
    "whats-new-sdk": {
      "href": "https://raw.githubusercontent.com/dotnet/docs/main/docs/core/whats-new/dotnet-9/sdk.md",
      "title": "What\u0027s new in the SDK and tooling for .NET 9",
      "type": "application/markdown"
    }
  }
}
```

This is a dictionary of links with some useful metadata. The relations are in order, first `self`, then other HAL links, then other links, each in alphabetical order.

Some of the information in this file is sourced from a human-curated `_manifest.json`. This file is used by the graph generation tools, not the graph itself. It provides a path to seeding the graph with data not available elsewhere.

It's somewhat surprising, but LLMs can efficiently find a lot of this information, even tucked away.

.NET 9 `_manifest.json`:

Source: <https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/_manifest.json>

```json
{
  "kind": "manifest",
  "title": ".NET 9.0 Manifest",
  "version": "9.0",
  "label": ".NET 9.0",
  "target_framework": "net9.0",
  "release_type": "sts",
  "support_phase": "active",
  "ga_date": "2024-11-12T00:00:00Z",
  "eol_date": "2026-11-10T00:00:00Z",
  "_links": {
    "downloads-html": {
      "href": "https://dotnet.microsoft.com/download/dotnet/9.0",
      "title": ".NET 9 Downloads",
      "type": "text/html"
    },
    "whats-new-html": {
      "href": "https://learn.microsoft.com/dotnet/core/whats-new/dotnet-9/overview",
      "title": "What's new in .NET 9",
      "type": "text/html"
    },
    "whats-new": {
      "href": "https://raw.githubusercontent.com/dotnet/docs/main/docs/core/whats-new/dotnet-9/overview.md",
      "title": "What's new in .NET 9",
      "type": "application/markdown"
    },
    "whats-new-runtime": {
      "href": "https://raw.githubusercontent.com/dotnet/docs/main/docs/core/whats-new/dotnet-9/runtime.md",
      "title": "What's new in the .NET 9 runtime",
      "type": "application/markdown"
    },
    "whats-new-libraries": {
      "href": "https://raw.githubusercontent.com/dotnet/docs/main/docs/core/whats-new/dotnet-9/libraries.md",
      "title": "What's new in .NET libraries for .NET 9",
      "type": "application/markdown"
    },
    "whats-new-sdk": {
      "href": "https://raw.githubusercontent.com/dotnet/docs/main/docs/core/whats-new/dotnet-9/sdk.md",
      "title": "What's new in the SDK and tooling for .NET 9",
      "type": "application/markdown"
    },
    "compatibility-html": {
      "href": "https://learn.microsoft.com/dotnet/core/compatibility/9.0",
      "title": "Breaking changes in .NET 9",
      "type": "text/html"
    },
    "release-blog-html": {
      "href": "https://devblogs.microsoft.com/dotnet/announcing-dotnet-9/",
      "title": "Announcing .NET 9",
      "type": "text/html"
    }
  }
}
```

These links are free form and can be anything. They follow the same scheme as the links used elsewhere in the graph.

### Patch Version Index

Last, we have the patch version index. `9.0.10` is used, as the most recent security release (at the time of writing).

Source: <https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/9.0.10/index.json>

```json
{
  "$schema": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/schemas/v1/dotnet-patch-detail-index.json",
  "kind": "patch",
  "title": ".NET Patch Release Index - 9.0.10",
  "version": "9.0.10",
  "date": "2025-10-14T00:00:00\u002B00:00",
  "support_phase": "active",
  "security": true,
  "cve_records": [
    "CVE-2025-55247",
    "CVE-2025-55248",
    "CVE-2025-55315"
  ],
  "prev_patch_date": "2025-09-09T00:00:00\u002B00:00",
  "prev_security_patch_date": "2025-06-10T00:00:00\u002B00:00",
  "sdk_version": "9.0.306",
  "sdk_feature_bands": [
    "9.0.306",
    "9.0.111"
  ],
  "_links": {
    "self": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/9.0.10/index.json"
    },
    "prev-patch": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/9.0.9/index.json",
      "title": "Previous patch release - 9.0.9"
    },
    "prev-security-patch": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/9.0.6/index.json",
      "title": "Previous security patch release - 9.0.6"
    },
    "downloads": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/downloads/index.json",
      "title": "Downloads - .NET 9.0"
    },
    "major": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/index.json",
      "title": ".NET Major Release Index - 9.0"
    },
    "month": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/index.json",
      "title": ".NET Month Timeline Index - October 2025"
    },
    "root": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/index.json",
      "title": ".NET Release Index"
    },
    "security-disclosures": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/index.json",
      "title": "Security disclosures - October 2025"
    },
    "cve-json": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/cve.json",
      "title": "CVE records - October 2025",
      "type": "application/json"
    },
    "release-json": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/9.0.10/release.json",
      "title": "Release information",
      "type": "application/json"
    }
  },
```

This content looks much the same as we saw with the major version index, except that much of the content we saw in the patch object is now exposed at index root. That's a intentional and largely inherent aspect of the model.

The `prev` and `prev-security` link relations provide similar wormhole functionality, this time to less distant targets. A `next` relation isn't provided because it would break the immutability contract. In addition, the combination of a `latest*` properties (in the major version index) and `prev*` links satisfies many scenarios.

The `downloads` relation provides access to `aka.ms` evergreen download links.

The `_embedded` property contains: `runtime`, `sdk` and `sdk_feature_bands`.

```json
"_embedded": {
    "runtime": {
      "version": "9.0.10",
      "_links": {
        "release-notes": {
          "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/9.0.10/9.0.10.md",
          "type": "application/markdown"
        }
      }
    },
    "sdk": {
      "version": "9.0.306",
      "band": "9.0.3xx",
      "date": "2025-10-14T00:00:00\u002B00:00",
      "label": ".NET SDK 9.0.3xx",
      "_links": {
        "downloads": {
          "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/downloads/sdk-9.0.3xx.json"
        },
        "release-notes": {
          "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/9.0.10/9.0.10.md",
          "type": "application/markdown"
        }
      }
    },
    "sdk_feature_bands": [
      {
        "version": "9.0.306",
        "band": "9.0.3xx",
        "date": "2025-10-14T00:00:00\u002B00:00",
        "label": ".NET SDK 9.0.3xx",
        "_links": {
          "downloads": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/downloads/sdk-9.0.3xx.json"
          },
          "release-notes": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/9.0.10/9.0.10.md",
            "type": "application/markdown"
          }
        }
      },
      {
        "version": "9.0.111",
        "band": "9.0.1xx",
        "date": "2025-10-14T00:00:00\u002B00:00",
        "label": ".NET SDK 9.0.1xx",
        "_links": {
          "downloads": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/downloads/sdk-9.0.1xx.json"
          },
          "release-notes": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/9.0.10/9.0.111.md",
            "type": "application/markdown"
          }
        }
      }
    ]
  }
```

These properties uniformly describe each layer of the stack and to demystify SDK feature bands. The `downloads` relation has been skipped for `runtime` because there isn't a single one to offer. The root `downloads` relation can be used to get download URLs specific to the .NET Runtime, ASP.NET Core, or Windows Desktop runtimes.

The `sdk` property is a scalar and `sdk_feature_bands` is a vector. For queries oriented on latest patches, the scalar is greatly preferred. Also, if SDK feature bands ever go away, this scheme is prepared for that.

First-class treatment is provided for SDK releases, both at root and in `_embedded`. That said, it is obvious that we don't quite do the right thing with release notes. It is very odd that the "best" SDK has to share release notes with the runtime.

Previews are a special beast. An `_embedded.documentation` property is added to account for all content that the team makes available about new features.

Source: <https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/preview/preview1/index.json>

```json
    "documentation": {
      "aspnetcore": {
        "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/preview/preview1/aspnetcore.md",
        "type": "application/markdown"
      },
      "containers": {
        "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/preview/preview1/containers.md",
        "type": "application/markdown"
      },
      "csharp": {
        "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/preview/preview1/csharp.md",
        "type": "application/markdown"
      },
      "dotnetmaui": {
        "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/preview/preview1/dotnetmaui.md",
        "type": "application/markdown"
      },
      "efcore": {
        "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/preview/preview1/efcore.md",
        "type": "application/markdown"
      },
      "fsharp": {
        "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/preview/preview1/fsharp.md",
        "type": "application/markdown"
      },
      "libraries": {
        "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/preview/preview1/libraries.md",
        "type": "application/markdown"
      },
      "runtime": {
        "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/preview/preview1/runtime.md",
        "type": "application/markdown"
      },
      "sdk": {
        "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/preview/preview1/sdk.md",
        "type": "application/markdown"
      },
      "visualbasic": {
        "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/preview/preview1/visualbasic.md",
        "type": "application/markdown"
      },
      "winforms": {
        "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/preview/preview1/winforms.md",
        "type": "application/markdown"
      },
      "wpf": {
        "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/preview/preview1/wpf.md",
        "type": "application/markdown"
      }
    }
```

## Timeline Modeling

The timeline is much the same as the version index. The key difference is that the version index converges to a point while the timeline index converges to a slice or row of points.

### Timeline Index

The root timeline `index.json` file represents all release years.

Source: <https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/index.json>

```json
{
  "$schema": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/schemas/v1/dotnet-release-timeline-index.json",
  "kind": "timeline",
  "title": ".NET Release Timeline Index",
  "latest_major": "10.0",
  "latest_lts_major": "10.0",
  "latest_year": "2025",
  "_links": {
    "self": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/index.json"
    },
    "latest-lts-major": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/index.json",
      "title": "Latest LTS major release - .NET 10.0"
    },
    "latest-major": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/index.json",
      "title": "Latest major release - .NET 10.0"
    },
    "latest-year": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/index.json",
      "title": "Latest year - 2025"
    },
    "root": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/index.json",
      "title": ".NET Release Index"
    }
  },
```

The `_embedded` section naturally contains `years`.

```json
  "_embedded": {
    "years": [
      {
        "year": "2025",
        "major_releases": [
          "10.0",
          "9.0",
          "8.0"
        ],
        "_links": {
          "self": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/index.json"
          }
        }
      },
```

It also provides a helpful join with the active (not necessarily supported throughout) releases for that year. This baked-in query may help some workflows. The timeline index doesn't try to be quite as lean as the root version index, so is happy to take on a bit more shape.

This index file similarly avoids fast-moving currency as the root releases index.

### Year Index

The year index represents all the release months for a given year.

Source: <https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/index.json>

```json
{
  "$schema": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/schemas/v1/dotnet-release-timeline-index.json",
  "kind": "year",
  "title": ".NET Year Timeline Index - 2025",
  "year": "2025",
  "latest_month": "12",
  "latest_security_month": "10",
  "latest_major": "10.0",
  "major_releases": [
    "10.0",
    "9.0",
    "8.0"
  ],
  "_links": {
    "self": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/index.json"
    },
    "prev-year": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2024/index.json",
      "title": "Previous year - 2024"
    },
    "latest-major": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/index.json",
      "title": "Latest major - .NET 10.0"
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
    "timeline": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/index.json",
      "title": ".NET Release Timeline Index"
    },
    "latest-cve-json": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/cve.json",
      "title": "Latest CVE records - October 2025",
      "type": "application/json"
    }
  },
```

Very similar approach as other indices.

Here, we see just `prev-year` to enable queries that chain across years. `latest-security-month` enables a jump to the latest month that included security fixes. From there, it's possible to enable queries that chain across `prev-security` months.

The `_embedded` section contains: `months`.

```json
  "_embedded": {
    "months": [
      {
        "month": "12",
        "security": false,
        "_links": {
          "self": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/12/index.json"
          }
        }
      },
      {
        "month": "11",
        "security": false,
        "_links": {
          "self": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/11/index.json"
          }
        }
      },
      {
        "month": "10",
        "security": true,
        "_links": {
          "self": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/index.json"
          },
          "cve-json": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/cve.json",
            "type": "application/json"
          }
        }
      },
```

### Month index

Last, we have the month index, using January 2025 as the example.

Source: <https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/01/index.json>

```json
{
  "$schema": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/schemas/v1/dotnet-release-timeline-index.json",
  "kind": "month",
  "title": ".NET Month Timeline Index - January 2025",
  "year": "2025",
  "month": "01",
  "date": "2025-01-14T00:00:00\u002B00:00",
  "security": true,
  "prev_month_date": "2024-12-03T00:00:00\u002B00:00",
  "prev_security_month_date": "2024-11-12T00:00:00\u002B00:00",
  "cve_records": [
    "CVE-2025-21171",
    "CVE-2025-21172",
    "CVE-2025-21176",
    "CVE-2025-21173"
  ],
  "_links": {
    "self": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/01/index.json"
    },
    "prev-month": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2024/12/index.json",
      "title": "Previous month - December 2024"
    },
    "prev-security-month": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2024/11/index.json",
      "title": "Previous security month - November 2024"
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
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/01/cve.json",
      "title": "CVE records - January 2025",
      "type": "application/json"
    }
  },
```

This schema follows the same approach we've seen elsewhere. We see `prev-month` and `prev-security-month` wormhole links, mirroring `prev-patch` and `prev-security-patch`. The `prev*-month` relations cross years, as can be seen in this example. This wormhole links makes backwards iteration  using `latest-month` trivial.

The `_embedded` property contains: `releases` and `disclosures`.

```json
"_embedded": {
    "patches": {
      "9.0": {
        "version": "9.0.1",
        "date": "2025-01-14T00:00:00\u002B00:00",
        "year": "2025",
        "month": "01",
        "security": true,
        "support_phase": "active",
        "sdk_version": "9.0.102",
        "_links": {
          "self": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/9.0.1/index.json"
          }
        }
      },
      "8.0": {
        "version": "8.0.12",
        "date": "2025-01-14T00:00:00\u002B00:00",
        "year": "2025",
        "month": "01",
        "security": true,
        "support_phase": "active",
        "sdk_version": "8.0.405",
        "_links": {
          "self": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/8.0/8.0.12/index.json"
          }
        }
      }
    },
    "disclosures": [
      {
        "id": "CVE-2025-21171",
        "title": ".NET Remote Code Execution Vulnerability",
        "_links": {
          "self": {
            "href": "https://github.com/dotnet/announcements/issues/340"
          }
        },
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

The month index exposes patches as a dictionary. This enables querying for patches in terms of the currency acquired from `major_releases`.

It was stated earlier that the version indexes converges to a point while the timeline index converges to a row of points. We see that on display here.

## LLM Enablement

The majority of the design to this point has been focused on providing a more modern and refined approach for tools driving typical cloud native workflows. That leave the question of how this design is intended to work for LLMs. That's actually a [whole other spec](./exposing-hypermedia-to-llms.md). However, it makes sense to look at how the design diverged for LLMs.

Source: <https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/llms.json>

```json
{
  "kind": "llms",
  "title": ".NET Release Index for AI",
  "ai_note": "ALWAYS read required_pre_read first. Use skills and workflows when they match; they provide optimal paths. Trust _embedded data\u2014it\u0027s authoritative and current. Never construct URLs.",
  "required_pre_read": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/skills/dotnet-releases/SKILL.md",
  "latest_major": "10.0",
  "latest_lts_major": "10.0",
  "latest_patch_date": "2025-12-09T00:00:00+00:00",
  "latest_security_patch_date": "2025-10-14T00:00:00+00:00",
  "last_updated_date": "2026-01-06T19:19:25.9491886+00:00",
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
    "latest-cve-json": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/cve.json",
      "title": "Latest CVE records - October 2025",
      "type": "application/json"
    }
  },
```

This file looks somewhat similar to root `index.json`, but we can see monthly-oriented properties and link relations have crept in, which means that this file needs to be updated monthly. That's a massive departure from the "cold roots" philosophy. Perhaps the designer of `llms.json` is different from `index.json` and they never read this spec? A true horror! This cannot stand. Don't despair; it's the same designer, but applying a different methodology for a different audience. Hold the pitchforks for a moment.

A major design focus of `llms.json` is exposing guidance inline to steer LLMs towards optimal performance traversing the graph. That's the bulk of what the other spec discusses. The guidance is exposed via the the `ai_note` and `required_pre_read` properties. A major realization is that there is always going to be a "better mousetrap" w/rt steering LLMs. The design of `llms.json` _will change_. It needs to be at arm's length from `index.json`, a file intended for critical workloads. `llms.json` is effectively an abstraction layer for LLMs, taking advantage of, but not integrated into the cloud workflows. This realization opens the door to adopting a different design point.

The `_embedded` property contains `latest_patches`:

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
          "manifest": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/manifest.json"
          }
        }
      },
```

These design choices equally violate the "cold roots" design, as already mentioned. Once its been broken once, you might as well break it thoroughly. This file also has a bit more budget to play with since it only ever has to accommodate (in general) three patch releases, not the growing set of major versions in root index.json. This opens up more richness in the link relations to drive a broader set of efficient queries.

Testing demonstrates that this design works well. LLMs notice the `ai_note` and `required_pre_read` properties and use those for guidance. They love the wormhole links and grasp their intent. Logging suggests that they are following them in optimal ways to answer user queries (synthetically tests via LLM eval).

Testing also demonstrates that `index.json` can be effectively used as a root data file, but that it must be a secondary fetch after acquiring guidance via [`llms.txt`](https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/llms.txt) and is less efficient for many queries compared to `llms.json`.

## Validate

> These dimensional characteristics form the load-bearing structure of the graph to which everything else is attached.

The following data is attached within the graph and therefore queryable.

- Breaking changes
- What's new links
- CVE disclosures
- Supported OSes
- Linux package dependencies
- Download links + hashes

We can query this data to test out the efficacy for the design inspired.

Three schema approaches are compared across 15 real-world query scenarios, measuring file transfer in bytes.

| Test | Query | `llms.json` | `index.json` | `releases-index.json` |
|------|-------|----------:|-----------:|--------------------:|
| T1 | Supported .NET versions | **5 KB** | **5 KB** | 6 KB |
| T2 | Latest patch details | **5 KB** | 14 KB | 6 KB |
| T3 | Security patches since date | **14 KB** | 34 KB | 1,269 KB |
| T4 | EOL version CVE details | 45 KB | **40 KB** | 1,612 KB |
| T5 | CVE timeline with code fixes | **25 KB** | 30 KB | ✗ |
| T6 | Security process analysis | **450 KB** | 455 KB | ✗ |
| T7 | High-impact breaking changes | **117 KB** | 126 KB | ✗ |
| T8 | Code migration guidance | **122 KB** | 131 KB | ✗ |
| T9 | What's new in runtime | **24 KB** | 33 KB | ✗ |
| T10 | Security audit (version check) | **14 KB** | 34 KB | 1,269 KB |
| T11 | Minimum libc version | **17 KB** | 26 KB | ✗ |
| T12 | Docker setup with SDK | **36 KB** | 45 KB | 25 KB |
| T13 | TFM support check | 45 KB | 50 KB | **6 KB** |
| T14 | Package CVE check | **60 KB** | 65 KB | ✗ |
| T15 | Target platform versions | **11 KB** | 20 KB | ✗ |
| | **Winner** | **13** | **2** | **1** |

**Legend:** Smaller is better. **Bold** = winner. **P** = partial answer. **✗** = cannot answer.

Raw results are recorded at [metrics/README.md](./metrics/README.md). The results prove out the premise that the new graph can efficiently answer a lot of domain questions and that `llms.json` has a small advantage over `index.json` when testing with `jq`.

## Conclusion

The intent of this project is to publish structured release notes using a more efficient schema architecture, both to reduce update cadence and to enable smaller fetch costs. The belief was that an existing standard would push our schema efforts towards a strong design paradigm that ultimately enabled us to achieve our goals faster and better. The results seem to prove this out. The chosen solution is an opposite-end-of-the-spectrum approach. It generates a huge number of files compared to the existing approach, many of which will never be updated and rarely visited once the associated major version is out of support. The existing `releases.json` files are already much like that, just monolithic (and therein lies the challenge).

Note: Prototype graph generation tools were written to generate all the JSON files. They are not part of the spec.

A case in point is the addition of breaking changes. Someone suggested that breaking changes should be added to the graph. It took < 60 mins to do that. The HAL design paradigm and the aligned philosophy of cold roots and weighted immutable leaves naturally allotted specific space for breaking changes to be attached on the load-bearing structure. There's no need to consult a design committee since (as a virtue) there isn't much design freedom once the overall approach is established. It's likely that we'll find a need to attach more information to the graph. We can just repeat the process.

The restrictive nature of this design ends up being well aligned with the performance and cost consideration of LLMs. That's a bit of foreshadowing of the other spec. It boils down to being very intentional about the design being [breadth-](https://en.wikipedia.org/wiki/Breadth-first_search) or [depth-first](https://en.wikipedia.org/wiki/Depth-first_search). The core design is strongly breadth-first oriented, which enables learning about a layer at a time. The reason that `llms.json` is able to pull ahead of the purist `index.json` breadth-first implementation is that it offers a depth-first approach for specific queries. The wormholes and more so the spear-fishing are essentially saying "I already know the graph structure; let me skip to the leaf I need" which is the essence of depth-first targeting. And that in a nutshell describes the design and why it is effective.
