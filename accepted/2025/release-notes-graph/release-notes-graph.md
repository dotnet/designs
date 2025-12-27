# Exposing Release Notes as an Information graph

Wormholes for navigation, spear fishing for direct data access.

The .NET project has published [release notes in JSON and markdown](https://github.com/dotnet/core/tree/main/release-notes) for many years. The investment in quality release notes has been based on the virtuous cloud-era idea that many deployment and compliance workflows require detailed structured data to safely operate at scale. For the most part, a highly clustered set of consumers (like GitHub Actions and vulnerability scanners) have adapted _their tools_ to _our formats_ to offer higher-level value to their users. The LLM era is strikingly different where an even more narrow set of information systems (LLM model companies) _consume and expose diverse data in standard ways_ according to _their (shifting) paradigms_ to a much larger set of users. The task at hand is to modernize release notes to make them more efficient to consume generally and to adapt them for LLM consumption.

Overall goals for release notes consumption:

- Alignment between graph schema and intended graph update frequency.
- Satisfy expectations of performance (no 1MB JSON files), reliability, and consistency.
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

- Within time, we have years, months, and (ship) days.
- Within version, we have major and patch version. We also have runtime vs SDK version.

These dimensional characteristics form the load-bearing structure of the graph to which everything else is attached. The new graph exposes both timeline and version indices. We've previously only had a version index.

The following table summarizes the overall shape of the graph, starting at `dotnet/core:release-notes/index.json`.

| File | Type | Size | Frequency | Updates When... |
| --- | --- | --- | --- | --- |
| `index.json` (root) | Release version index | 4.6 KB | 1-2x/year | New major version, support bool changes |
| `timeline/index.json` | Release timeline index | 4.4 KB | 2x/year | New year starts, new major version |
| `timeline/{year}/index.json` | Year index | 5.5 KB | 12x/year | New month with activity, phase changes |
| `timeline/{year}/{month}/index.json` | Month index | 9.7 KB | 0 | Never (immutable after creation) |
| `{version}/index.json` | Major version index | 20 KB | 12x/year | New patch release |
| `{version}/{patch}/index.json` | Patch version index | 7.7 KB | 0 | Never (immutable after creation) |
| `llms.json` | AI-optimized index | 5 KB | 12x/year | New patch release, support status changes |

**Summary:** Cold roots, warm branches, immutable leaves.

Notes:

- 2025 was used for `{year}`, 10 for `{month`}, 8.0 for `{version}`, and 8.0.21 for `{patch}`
- The bottom of the graph has the largest variance over time. `8.0.21/index.json` is `11.5` KB while `8.0.22/index.json` is `4.8` KB, significantly smaller because it was a non-security release. Similarly, `2025/10/index.json` is `9.7` KB while `2025/11/index.json` is `3.5` KB, significantly smaller because there was no security release that month. `2025/12/index.json` was even smaller because there was only a .NET 10 patch, also non-security. Security releases were chosen to demonstrate more realistic sizes.
- `llms.json` will be covered more later.

We can contrast this approach with the existing release graph.

| File | Type | Size | Frequency | Updates When... |
| --- | --- | --- | --- | --- |
| `releases-index.json` (root) | Release version index | 6.3 KB | 18x/year | Every patch release |
| `{version}/releases.json` | Major Version Index | 1.3 MB | 18x/year | Every patch release |

Notes:

- 8.0 was used for `{version}`. 6.0 `releases.json` is 1.6 MB.
- Frequency is 18x a year to allow for SDK-only releases that occur after Patch Tuesday.

It is straightforward to see that the new graph design enables access to far more information before hitting a data wall. In fact, in experiments, LLMs reliably hit an HTTP `409` error code as soon as they try to read `release.json`. It hits the LLM context limit, or similar.

The last 12 months of commit data (Nov 2024-Nov 2025) demonstrates a higher than expected update rate.

| File | Commits | Notes |
| ---- | ------- | ----- |
| `releases-index.json` | 29 | Root index (all versions) |
| `10.0/releases.json` | 22 | Includes previews/RCs and SDK-only releases |
| `9.0/releases.json` | 24 | Includes SDK-only releases, fixes, URL rewrites |
| `8.0/releases.json` | 1Please8 | Includes SDK-only releases, fixes, URL rewrites |

**Summary:** Hot everything.

Conservatively, the existing commit counts are not good. The `releases-index.json` file is a mission-critical live-site resource. 29 updates is > 2x/month!

## Attached data

> These dimensional characteristics form the load-bearing structure of the graph to which everything else is attached.

This leaves the question of which data we could attach.

The following are all in scope to include:

- Breaking changes (included)
- What's new links (included)
- CVE disclosures (included)
- Supported OSes (included)
- Linux package dependencies (included)
- Download links + hashes (included)
- Servicing fixes and commits (beyond CVEs)
- Known issues

### Graph consistency

The graph has one rule:

> Every resource in the graph needs to be guaranteed consistent with every other part of the graph.

The unstated problem is CDN caching. Assume that the entire graph is consistent when uploaded to an origin server. A CDN server is guaranteed by construction to serve both old and new copies of the graph -- for existing files that have been updated -- leading to potential inconsistencies. The graph construction needs to be resilient to that.

Related examples:

- <https://github.com/dotnet/core/issues/9200>
- <https://github.com/dotnet/core/issues/10082>

Today, we publish `releases-index.json` as the root of our release notes graph. Some users read this JSON file to learn the latest patch version numbers, while others navigate deeper into the graph. Both are legitimate patterns. However, we've found that our approach has fundamental flaws.

Problems:

- Exposing patch versions in multiple files that need to agree is incompatible with using a Content Delivery Network (CDN) that employs standard caching (expiration / TTL).
- The `releases-index.json` file is a critical live site resource driving 1000s of GBs of downloads a month, yet we update it multiple times a month, by virue of the data it exposes.

It's hard to understate the impact of schema design on file update frequency. Files that expose properties with patch versions (scalars or links) inherently require updates on the patch schedule. If so, `git status` will put that file on a stage. Otherwise, `git status` will not give that file a second look.

Solution:

- Fast changing currency (like patch version numbers) are exposed in (at most) a single resource in the graph, and never at the root.
- The root index file is updated once or twice a year (to add the presence of a new major release and change support status; releases come in and go out, typically not on the same day).

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

There are many design paradigms and tradeoffs one can consider with a graph like this. A major focus is the "skeletal root" vs "weighted bottom" design point, discussed earlier. This approach forces significant fetches and inefficiency to get to _any_ useful data, unlike `releases-index.json`. To mitigate that, the graph includes helpful workflow-specific wormhole links. As the name suggests, these links enable jumping from one part of the graph to another, intended to create a kind of ergonomics for expected queries. The wormholes are somewhat emergent, somewhat obvious to expose due to otherwise challenging nature of the design.

The wormholes links take on a different character at each layer of the graph.

Cold roots:

- `latest` and `latest-lts` -- enables jumping to the matching major version index.
- `latest-year` -- enables jumping to the latest year index.

Warm branches:

- `latest` and `latest-security` -- enables jumping to the matching patch version index.
- `latest-month` and `latest-security-month` -- enables jumping to the latest matching month.

Immutable leaves:

- `prev` and `prev-security` -- enables jumping to an earlier matching patch version or month index.

Notes:

- The wormhole links cannot force an update schedule beyond what the file would naturally allow. For the cold roots, a workhole like `latest-lts` is the best we can do.
- There are other such wormhole links. These are the the primary ones, which also best demonstrate the idea.

The wormhole links are what make the graph a graph and not just two related trees providing alternative views of the same data. They also enable efficient navigation.

For some scenarios, it can be efficient to jump to `latest-security-month` and then backwards in time via `prev-security`. `prev` and `prev-security` jump across year boundaries, reducing the logic required to use the scheme. There are other algoriths that [work best when counting backwards](https://www.benjoffe.com/fast-date-64).

There is no `next` or `next-security`. `prev` and `prev-security` link immutable leaves, establishing a linked-list based on past knowledge. It's not possible to provide `next` links given the immutability constraint.

There is no `latest-sts` link because it's not really useful. `latest` covers the same need sufficiently.

Testing has demonstrated that these wormhole links are one of the defining features of the graph.

## Version Index Modeling

The version index has three layers: releases, major version, patch version. Most nodes in the graph are named `index.json`. The examples should look similar to the HAL spec documents shared earlier.

### Releases index

The root `index.json` file represents all .NET versions. It is a stripped-down version of the existing `releases-index.json`.

Source: <https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/index.json>

```json
{
  "$schema": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/schemas/v1/dotnet-release-version-index.json",
  "kind": "releases-index",
  "title": ".NET Release Index",
  "latest": "10.0",
  "latest_lts": "10.0",
  "_links": {
    "self": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/index.json"
    },
    "latest": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/index.json",
      "title": "Latest release - .NET 10.0"
    },
    "latest-lts": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/index.json",
      "title": "Latest LTS release - .NET 10.0"
    },
    "timeline-index": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/index.json",
      "title": ".NET Release Timeline Index"
    }
  },
```

Key points:

- Schema reference is included
- All links are raw content (will eventually transition to `builds.dotnet.microsoft.com).
- `kind` and `title` describe the resource
- `latest` and `latest_lts` describe high-level resource metadata, often useful currency that helps contextualize the rest of the resource without the need to parse/split strings. For example, the `latest_lts` scalar describes the target of the `latest-lts` link relation. Notably, there are no three-part (patch) versions, like `latest_lts_patch`.
- `timeline-index` provides a wormhole link to another part of the graph, which provides a temporal view of .NET releases.
- Core schema syntax, like `latest_lts`, uses snake-case-lower for query ergonomics (using `jq` as the proxy for that).
- Link relations. like `latest-lts`, use kebab-case-lower since they can be names or brands. This follows the approach used by [cve-schema](https://github.com/dotnet/designs/blob/main/accepted/2025/cve-schema/cve_schema.md#brand-names-vs-schema-fields-mixed-naming-strategy).

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
  "kind": "major-version-index",
  "title": ".NET Major Release Index - 9.0",
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
      "title": "Downloads - .NET 9.0"
    },
    "latest": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/9.0.11/index.json",
      "title": "Latest patch - 9.0.11"
    },
    "latest-sdk": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/sdk/index.json",
      "title": "Latest SDK - .NET 9.0"
    },
    "latest-security": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/9.0.10/index.json",
      "title": "Latest security patch - 9.0.10"
    },
    "manifest": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/manifest.json",
      "title": "Manifest - .NET 9.0"
    },
    "releases-index": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/index.json",
      "title": ".NET Release Index"
    }
  },
```

This index includes much more useful and detailed information, both metadata/currency and patch-version links. It starts to answer the question of "what should I care about _now_?".

Much of the form is similar to the root index. Instead of `latest_lts`, there is `latest_security`. The `manifest` relation stores important but lower-tier content about a given major release. That will be covered shortly.

The `_embedded` property (solely) includes `releases`.

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
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/11/index.json"
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
          "cve-json": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/cve.json"
          },
          "release-month": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/index.json"
          }
        }
      },
```

The `patches` object contains detailed information that can drive basic deployment and compliance workflows. The first two link relations, `self` and `release-month` are HAL links while `cve-json` is a plain JSON link. Non-HAL links end in the given format, like `json` or `markdown` or `markdown-rendered`. The links are raw text by default, with `-rendered` HTML content being useful for content targeted for human consumption, for example as links in generated release notes.

MAJOR NOTE: Preview releases are removed (no longer included when the file is generated) to reduce size. These files are intended for missions critical scenarios. After GA, only supported releases are of interest to well over 99% of users. RC releases are included because they are supported. This means on GA day a bunch of previews will dissapear, but the last two RCs releases will remain. That's likely OK.

This behavior is made more clear with a `jq` query for patch versions.

```json
$ jq ._embedded.patches[].version release-notes/9.0/index.json
"9.0.11"
"9.0.10"
"9.0.9"
"9.0.8"
"9.0.7"
"9.0.6"
"9.0.5"
"9.0.4"
"9.0.3"
"9.0.2"
"9.0.1"
"9.0.0"
"9.0.0-rc.2"
"9.0.0-rc.1"
```

Note: Preview releases are not included for 8.0 and earlier releases because we didn't yet have the practice of creating preview directories to place an `index.json` file. This isn't much of a concern since non-GA releases are not really important for historical releases.

The `release-month` relation is another wormhole link. It provides direct access to a high-relevance and  graph-distant resource that would otherwise require awkward indirections, multiple network hops, and wasted bytes/tokens to acquire. These wormhole links massively improve query ergonomics for sophisticated queries.

There is a link to a `cve.json` file. Our [CVE schema](https://github.com/dotnet/designs/blob/main/accepted/2025/cve-schema/cve_schema.md) is a custom schema with no HAL vocabulary. It's an exit node of the graph. The point is that we're free to describe complex domains, like CVE disclosures, using a clean-slate design methodology. One can also see that some of the `cve.json` information has been projected into the graph, adding high-value shape over the skeleton.

As stated, there is a lot more useful detailed currency on offer. However (as mentioned earlier), there is a rule that currency needs to be guaranteed consistent. Let's consider if the rule is obeyed. The important characteristic is that listed versions and links _within_ the resource are consistent by virtue of being _captured_ in the same file.

The critical trick is with the links. In the case of the `release-month` link, the link origin is a fast moving resource (warm branch) while the link target is immutable. That combination works. It's easy to be consistent with something immutable. It will either exist or not, only in the expected form. In contrast, there would be a problem if there was a link between two mutable resources that expose the same currency. This is the problem that `releases-index.json` has. This is not unlike data races due to global mutable state in programming language discussions.

Back to `manifest.json`. It contains extra data that are helpful in second-tier scenarios.

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
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/9.0/manifest.json"
    },
    "compatibility": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/9.0/compatibility.json",
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
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/9.0/os-packages.json",
      "title": "OS Packages",
      "type": "application/json"
    },
    "release-blog-rendered": {
      "href": "https://devblogs.microsoft.com/dotnet/announcing-dotnet-9/",
      "title": "Announcing .NET 9",
      "type": "text/html"
    },
    "supported-os-json": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/9.0/supported-os.json",
      "title": "Supported OSes",
      "type": "application/json"
    },
    "supported-os-markdown": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/9.0/supported-os.md",
      "title": "Supported OSes",
      "type": "application/markdown"
    },
    "supported-os-markdown-rendered": {
      "href": "https://github.com/dotnet/core/blob/main/release-notes/9.0/supported-os.md",
      "title": "Supported OSes (Rendered)",
      "type": "text/html"
    },
    "target-frameworks": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/9.0/target-frameworks.json",
      "title": "Target Frameworks",
      "type": "application/json"
    },
    "usage-markdown-rendered": {
      "href": "https://github.com/dotnet/core/blob/main/release-notes/9.0/README.md",
      "title": "Release Notes (Rendered)",
      "type": "text/html"
    },
    "whats-new": {
      "href": "https://raw.githubusercontent.com/dotnet/docs/main/docs/core/whats-new/dotnet-9/overview.md",
      "title": "What\u0027s new in .NET 9",
      "type": "application/markdown"
    },
    "whats-new-libraries": {
      "href": "https://raw.githubusercontent.com/dotnet/docs/main/docs/core/whats-new/dotnet-9/libraries.md",
      "title": "What\u0027s new in .NET libraries for .NET 9",
      "type": "application/markdown"
    },
    "whats-new-rendered": {
      "href": "https://learn.microsoft.com/dotnet/core/whats-new/dotnet-9/overview",
      "title": "What\u0027s new in .NET 9",
      "type": "text/html"
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

This is a dictionary of links with some useful metadata. The relations are in alphabetical order, after `self`.

Some of the information in this file is sourced from a human-curated `_manifest.json`. This file is used by the graph generation tools, not the graph itself. It provides a path to seeding the graph with data not available elsewhere.

It's somewhat surprising, but LLMs can efficiently find a lot of this information, even as tucked away as it is.

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

Last, we have the patch version index. `9.0.10` is used, as the most recent security release (at the time of writing).

Source: <https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/9.0.10/index.json>

```json
{
  "$schema": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/schemas/v1/dotnet-patch-detail-index.json",
  "kind": "patch-version-index",
  "title": ".NET Patch Release Index - 9.0.10",
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
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/9.0/9.0.10/index.json"
    },
    "prev": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/9.0/9.0.9/index.json",
      "title": "Previous patch release - 9.0.9"
    },
    "prev-security": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/9.0/9.0.6/index.json",
      "title": "Previous security patch release - 9.0.6"
    },
    "latest-sdk": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/9.0/sdk/index.json",
      "title": "Latest SDK - .NET 9.0"
    },
    "manifest": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/9.0/9.0.10/manifest.json",
      "title": "Manifest - .NET 9.0"
    },
    "release-major": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/9.0/index.json",
      "title": ".NET Major Release Index - 9.0"
    },
    "release-month": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/timeline/2025/10/index.json",
      "title": ".NET Month Timeline Index - October 2025"
    },
    "releases-index": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/index.json",
      "title": ".NET Release Index"
    },
    "cve-json": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/timeline/2025/10/cve.json",
      "title": "CVE records - October 2025",
      "type": "application/json"
    }
  },
```

This content looks much the same as we saw earlier, except that much of the content we saw in the patch object is now exposed at index root. That's a key aspect of the model. There is a `manifest` for patch releases as well, to expose lower-tier content.

The `prev` and `prev-security` link relations provide similar wormhole functionality, this time to less distant targets. A `next` relation isn't provided because it would break the immutability goal. In addition, the combination of a `latest*` properties (in the major version index) and `prev*` links satisfies many scenarios.

The `latest-sdk` target provides access to `aka.ms` evergreen SDK links and other SDK-related information.

The `_embedded` property contains three children: `sdk`, `sdk_feature_bands`, and `disclosures`.

```json
  "_embedded": {
    "sdk": {
      "version": "9.0.306",
      "band": "9.0.3xx",
      "date": "2025-10-14T00:00:00\u002B00:00",
      "label": ".NET SDK 9.0.3xx",
      "support_phase": "active",
      "_links": {
        "downloads": {
          "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/downloads/sdk-9.0.3xx.json"
        },
        "release-month": {
          "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/index.json"
        },
        "release-patch": {
          "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/9.0.10/index.json"
        }
      }
    },
    "sdk_feature_bands": [
      {
        "version": "9.0.306",
        "band": "9.0.3xx",
        "date": "2025-10-14T00:00:00\u002B00:00",
        "label": ".NET SDK 9.0.3xx",
        "support_phase": "active",
        "_links": {
          "downloads": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/downloads/sdk-9.0.3xx.json"
          },
          "release-month": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/index.json"
          },
          "release-patch": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/9.0.10/index.json"
          }
        }
      },
      {
        "version": "9.0.111",
        "band": "9.0.1xx",
        "date": "2025-10-14T00:00:00\u002B00:00",
        "label": ".NET SDK 9.0.1xx",
        "support_phase": "active",
        "_links": {
          "downloads": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/downloads/sdk-9.0.1xx.json"
          },
          "release-month": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/index.json"
          },
          "release-patch": {
            "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/9.0.10/index.json"
          }
        }
      }
    ],
```

and

```json
    "disclosures": [
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
          "10.0",
          "9.0",
          "8.0"
        ],
        "affected_products": [
          "dotnet-sdk"
        ],
        "platforms": [
          "linux"
        ]
      },
```

Likely obvious, `sdk` is a scalar and `sdk_feature_bands` is a vector. For queries that only care about latest, the scalar is greatly preferred. Also, if SDK feature bands ever go away, this scheme is prepared for that.

First-class treatment is provided for SDK releases, both at root and in `_embedded`. That said, it is obvious that we don't quite do the right thing with release notes. It is very odd that the "best" SDK has to share release notes with the runtime.

Any CVEs for the month are described in `disclosures`. This data provides a useful denormalized view on data sourced from `cve.json`.

Here's the `manifest.json` content.

Source: <https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/9.0/9.0.10/manifest.json>

```json
{
  "$schema": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/schemas/v1/dotnet-release-manifest.json",
  "kind": "manifest",
  "title": "Manifest - .NET 9.0.10",
  "_links": {
    "self": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/9.0/9.0.10/manifest.json"
    },
    "cve-markdown": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/timeline/2025/10/cve.md",
      "title": "CVE records - October 2025",
      "type": "application/markdown"
    },
    "cve-markdown-rendered": {
      "href": "https://github.com/dotnet/core/blob/main/release-notes/timeline/2025/10/cve.md",
      "title": "CVE records (Rendered) - October 2025",
      "type": "application/markdown"
    },
    "release-json": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/9.0/9.0.10/release.json",
      "title": "Release information",
      "type": "application/json"
    },
    "release-notes-9.0.111-markdown": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/9.0/9.0.10/9.0.111.md",
      "title": ".NET 9.0.111 - October 14, 2025",
      "type": "application/markdown"
    },
    "release-notes-markdown": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/9.0/9.0.10/9.0.10.md",
      "title": "Release notes",
      "type": "application/markdown"
    },
    "release-notes-markdown-rendered": {
      "href": "https://github.com/dotnet/core/blob/main/release-notes/9.0/9.0.10/9.0.10.md",
      "title": "Release notes (Rendered)",
      "type": "application/markdown"
    }
  }
}
```

It's possible to make detail-oriented compliance and deployment decisions based on this information. There's even a commit for the CVE fix with an LLM friendly link style. This is the bottom part of the hypermedia graph. It's far more shapely and weighty than the root. If a consumer gets this far, it is likely because they need access to the exposed information. If they only want access to the `cve.json` file, it is exposed in the major version index.

Previews are a special beast. Preview patch releases get a more extensive `manifest.json` treatment to account for all content that the team makes available about new features.

Source: <https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/preview/preview1/manifest.json>

```json
{
  "$schema": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/schemas/v1/dotnet-release-manifest.json",
  "kind": "manifest",
  "title": "Manifest - .NET 10.0.0-preview.1",
  "_links": {
    "self": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/preview/preview1/manifest.json"
    },
    "release-json": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/preview/preview1/release.json",
      "title": "Release information",
      "type": "application/json"
    },
    "release-notes-markdown": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/preview/preview1/10.0.0-preview.1.md",
      "title": "Release notes",
      "type": "application/markdown"
    },
    "release-notes-markdown-rendered": {
      "href": "https://github.com/dotnet/core/blob/main/release-notes/10.0/preview/preview1/10.0.0-preview.1.md",
      "title": "Release notes (Rendered)",
      "type": "application/markdown"
    },
    "whats-new-aspnetcore": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/preview/preview1/aspnetcore.md",
      "title": "ASP.NET Core in .NET 10 Preview 1 - Release Notes",
      "type": "application/markdown"
    },
    "whats-new-containers": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/preview/preview1/containers.md",
      "title": "Container image updates in .NET 10 Preview 1 - Release Notes",
      "type": "application/markdown"
    },
    "whats-new-csharp": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/preview/preview1/csharp.md",
      "title": "C# 14 updates in .NET 10 Preview 1 - Release Notes",
      "type": "application/markdown"
    },
    "whats-new-dotnetmaui": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/preview/preview1/dotnetmaui.md",
      "title": ".NET MAUI in .NET 10 Preview 1 - Release Notes",
      "type": "application/markdown"
    },
    "whats-new-efcore": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/preview/preview1/efcore.md",
      "title": "Entity Framework Core 10 Preview 1 - Release Notes",
      "type": "application/markdown"
    },
    "whats-new-fsharp": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/preview/preview1/fsharp.md",
      "title": "F# updates in .NET 10 Preview 1 - Release Notes",
      "type": "application/markdown"
    },
    "whats-new-libraries": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/preview/preview1/libraries.md",
      "title": ".NET Libraries in .NET 10 Preview 1 - Release Notes",
      "type": "application/markdown"
    },
    "whats-new-runtime": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/preview/preview1/runtime.md",
      "title": ".NET Runtime in .NET 10 Preview 1 - Release Notes",
      "type": "application/markdown"
    },
    "whats-new-sdk": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/preview/preview1/sdk.md",
      "title": ".NET SDK in .NET 10 Preview 1 - Release Notes",
      "type": "application/markdown"
    },
    "whats-new-visualbasic": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/preview/preview1/visualbasic.md",
      "title": "Visual Basic updates in .NET 10 Preview 1 - Release Notes",
      "type": "application/markdown"
    },
    "whats-new-winforms": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/preview/preview1/winforms.md",
      "title": "Windows Forms in .NET 10 Preview 1 - Release Notes",
      "type": "application/markdown"
    },
    "whats-new-wpf": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/preview/preview1/wpf.md",
      "title": "WPF in .NET 10 Preview 1 - Release Notes",
      "type": "application/markdown"
    }
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
  "kind": "timeline-index",
  "title": ".NET Release Timeline Index",
  "latest": "10.0",
  "latest_lts": "10.0",
  "latest_year": "2025",
  "_links": {
    "self": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/index.json"
    },
    "latest": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/index.json",
      "title": "Latest release - .NET 10.0"
    },
    "latest-lts": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/index.json",
      "title": "Latest LTS release - .NET 10.0"
    },
    "latest-year": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/index.json",
      "title": "Latest year - 2025"
    },
    "releases-index": {
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
        "releases": [
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

It also provides a helpful join with the active (not neccessarily supported) releases for that year. This baked-in query helps some workflows. The timeline index doesn't try to be quite as lean as the root version index, so is happy to take on a bit more shape.

This index file similarly avoid fast-moving currency as the root releases index.

### Year Index

The year index represents all the release months for a given year.

Source: <https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/index.json>

```json
{
  "$schema": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/schemas/v1/dotnet-release-timeline-index.json",
  "kind": "year-index",
  "title": ".NET Year Timeline Index - 2025",
  "year": "2025",
  "latest_month": "12",
  "latest_security_month": "10",
  "latest_release": "10.0",
  "releases": [
    "10.0",
    "9.0",
    "8.0"
  ],
  "_links": {
    "self": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/index.json"
    },
    "prev": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2024/index.json",
      "title": "Previous year - 2024"
    },
    "latest-month": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/12/index.json",
      "title": "Latest month - December 2025"
    },
    "latest-release": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/index.json",
      "title": "Latest release - .NET 10.0"
    },
    "latest-security-month": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/index.json",
      "title": "Latest security month - October 2025"
    },
    "timeline-index": {
      "href": "https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/index.json",
      "title": ".NET Release Timeline Index"
    }
  },
```

Very similar approach as other indices.

Here, we see just `prev` with no `prev-security`. It doesn't make much sense to think of a year having a security designation. However, it does make sense to think of `latest-security-month` and `latest-release` within the scope of that year.

The `_emdedded` section (solely) contains: `months`.

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
```

### Month index

Last, we have the month index, using January 2025 as the example.

Source: <https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/01/index.json>

```json
{
  "$schema": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/schemas/v1/dotnet-release-timeline-index.json",
  "kind": "month-index",
  "title": ".NET Month Timeline Index - January 2025",
  "year": "2025",
  "month": "01",
  "date": "2025-01-14T00:00:00\u002B00:00",
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
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/timeline/2025/01/index.json"
    },
    "prev": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/timeline/2024/12/index.json",
      "title": "Previous month - December 2024"
    },
    "prev-security": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/timeline/2024/11/index.json",
      "title": "Previous security month - November 2024"
    },
    "manifest": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/timeline/2025/01/manifest.json",
      "title": "Manifest - January 2025"
    },
    "timeline-index": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/timeline/index.json",
      "title": ".NET Release Timeline Index"
    },
    "year-index": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/timeline/2025/index.json",
      "title": ".NET Year Timeline Index - 2025"
    },
    "cve-json": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/timeline/2025/01/cve.json",
      "title": "CVE records - January 2025",
      "type": "application/json"
    }
  },
```

This schema follows the same approach we've seen elsewhere. We see `prev` and `prev-security` wormhole links again. They cross years, as can be seen in this example. This wormhole links makes backwards `foreach` using `latest-month` trivial.

The `_embedded` property contains: `releases` and `disclosures`.

```json
  "_embedded": {
    "patches": [
      {
        "version": "9.0.1",
        "release": "9.0",
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
        "sdk_version": "9.0.102",
        "_links": {
          "self": {
            "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/9.0/9.0.1/index.json"
          }
        }
      },
      {
        "version": "8.0.12",
        "release": "8.0",
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
        "sdk_version": "8.0.405",
        "_links": {
          "self": {
            "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/8.0/8.0.12/index.json"
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
            "href": "https://github.com/dotnet/announcements/issues/340"
          }
        },
        "fixes": [
          {
            "href": "https://github.com/dotnet/runtime/commit/9da8c6a4a6ea03054e776275d3fd5c752897842e.diff",
            "repo": "dotnet/runtime",
            "branch": "release/9.0",
            "title": "Fix commit in runtime (release/9.0)",
            "release": "9.0",
            "min_vulnerable": "9.0.0",
            "max_vulnerable": "9.0.0",
            "fixed": "9.0.1"
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

The month manifest is quite small.

Source: <https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/01/manifest.json>

```json
{
  "$schema": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/schemas/v1/dotnet-release-manifest.json",
  "kind": "manifest",
  "title": "Manifest - January 2025",
  "_links": {
    "self": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/timeline/2025/01/manifest.json"
    },
    "cve-markdown": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/timeline/2025/01/cve.md",
      "title": "CVE records - January 2025",
      "type": "application/markdown"
    },
    "cve-markdown-rendered": {
      "href": "https://github.com/dotnet/core/blob/main/release-notes/timeline/2025/01/cve.md",
      "title": "CVE records (Rendered) - January 2025",
      "type": "text/html"
    }
  }
}
```

## LLM Enablement

The majority of the design to this point has been focused on providing a better more modern approach for tools driving typical cloud native workflows. That leave the question of how this design is intended to work for LLMs. That's actually a [whole other spec](./release-notes-graph-llms.md). However, it makes sense to look at how the design for LLMs both diverges and remains consistent.

Source: <https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/llms.json>

```json
{
  "kind": "llms-index",
  "title": ".NET Release Index for AI",
  "ai_note": "ALWAYS read required_pre_read first. HAL graph\u2014follow _links only, never construct URLs.",
  "required_pre_read": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/skills/dotnet-releases/SKILL.md",
  "latest": "10.0",
  "latest_lts": "10.0",
  "supported_releases": [
    "10.0",
    "9.0",
    "8.0"
  ],
  "_links": {
    "self": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/llms.json"
    },
    "latest": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/index.json",
      "title": "Latest release - .NET 10.0"
    },
    "latest-lts": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/index.json",
      "title": "Latest LTS release - .NET 10.0"
    },
    "latest-security-month": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/timeline/2025/10/index.json",
      "title": "Latest security month - October 2025"
    },
    "latest-year": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/timeline/2025/index.json",
      "title": "Latest year - 2025"
    },
    "releases-index": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/index.json",
      "title": ".NET Release Index"
    },
    "timeline-index": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/timeline/index.json",
      "title": ".NET Release Timeline Index"
    }
  },
```

For far, this looks largely similar to root `index.json`.

There are two differences:

- `supported_releases` -- Describes the set of releases supported at any point
- `latest-security-month` -- Wormhole link to the latest security month

This link relation appears on first glance to violate the "cold root" goals of index.json. Indeed, it does. This file isn't intended to support the n-9s demanded by cloud scenarios. It's intended to make LLMs efficient. Based on extensive testing, LLMs love these wormhole links. They actually halluciante less when given a quick and obvious path towards a goal.

The `ai_note` and `required_pre_read` are LLM-specific properties that are covered in the other spec. These properties are a best guess take at steering LLMs, at the time of writing. A major realization is that there is always going to be a "better mousetrap" w/rt context engineering. The design of `llms.json` _will change_. It needs to be at arm's length from `index.json` and to avoid the burden of responsibility for mission critical workloads. `llms.json` is effectively an abstraction layer for LLMs, taking advantage of, but not integrated into the cloud workflows.

The `_embedded` property (solely) contains: `latest_patches`.

```json
  "_embedded": {
    "latest_patches": [
      {
        "version": "10.0.1",
        "release": "10.0",
        "release_type": "lts",
        "security": false,
        "support_phase": "active",
        "supported": true,
        "sdk_version": "10.0.101",
        "latest_security": "10.0.0-rc.2",
        "latest_security_date": "2025-10-14",
        "_links": {
          "self": {
            "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/10.0.1/index.json"
          },
          "latest-security": {
            "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/preview/rc2/index.json"
          },
          "release-major": {
            "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/index.json"
          },
          "latest-sdk": {
            "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/sdk/index.json"
          },
          "manifest": {
            "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/manifest.json"
          }
        }
      },
```

These design choices equally violate the "cold roots" design. Once its been broken once, you might as well break it thoroughly to take full advantage. This file also a bit more budget to play with since it only ever has to accomodate (in general) three patch releases, not the growing set of major versions in root index.json. This opens up space for more richness in the link relations. The four listed are the highest priority and enable the majority of scenarios. This is also where we see `manifest` providing more value, making it possible to skip a jump through `release-major` to get to breaking change, what's new, and other similar information.

There is a little more going on than a first look might reveal. In particular, this design _does_ violate the "cold roots" design, but it _largely_ keeps true to the critical consistency rule.

Here's the major distinction, looking at the three root schemas:

```bash
$ jq ._embedded.releases[0] index.json
{
  "version": "10.0",
  "release_type": "lts",
  "supported": true,
  "_links": {
    "self": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/index.json"
    }
  }
}
$ jq '.["releases-index"][0]' releases-index.json
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
}
$ jq ._embedded.latest_patches[0] llms.json
{
  "version": "10.0.1",
  "release": "10.0",
  "release_type": "lts",
  "security": false,
  "support_phase": "active",
  "supported": true,
  "sdk_version": "10.0.101",
  "latest_security": "10.0.0-rc.2",
  "latest_security_date": "2025-10-14",
  "_links": {
    "self": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/10.0.1/index.json"
    },
    "latest-security": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/preview/rc2/index.json"
    },
    "release-major": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/index.json"
    },
    "latest-sdk": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/sdk/index.json"
    },
    "manifest": {
      "href": "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/manifest.json"
    }
  }
}
```

These files don't expose the same type of data as their primary payload. `index.json` deals exclusively in terms of major releases. `releases-index.json` is the same but spinkles in patch metdata. That's what gets it into trouble. On first glance, `llms.json` seems to do the same thing, but doesn't. It deals in _patches_. On one hand, it offers patch versions and on the other hand, it offers up patch links, with (most importantly) `self`, and `latest-security`. That's consistent. This is the same "fast moving" -> "immutable" type of structure where a consistent set of links and data are captured in a single file, discussed earlier.

Close readers will notice that `release-major` is the antogonist in this narrative, the fly in the ointment. This is true. It's our [Faustian bargain](https://en.wikipedia.org/wiki/Deal_with_the_Devil).

This tradeoff was considered acceptable for three reasons:

- It's extremely useful to have this link relation to efficiently enable important scenarios.
- The canonical link -- `self` -- follows all the rules. This is the primary distinction with `releases-index.json`.
- The other relations, which we've brought in with the higher budget, enable skipping over `releases-major` and its potential related consistency problems in many scenarios.

Another key aspect of this design is that it is fully bounded. It's basically a denormalized viewport on index.json. Over time, `index.json` will grow and `llms.json` will slide its window (hence it being a viewport).

This viewport idea could also be useful for the cloud scenario, as a means of making `index.json` smaller. This idea was considered, but it cannot be done compatibily. Imagine `index-supported.json`, effectively a `.supported` quert over `index.json`. The problem with that is that one day a version is there another day it is not. The only rational approach is for the tools to drop (for example) .NET 8 and .NET 9 on their EOL day (same day). That's the day that they will get their last patch. It is guaranteed that this approach will break and annoy people. That's precisely the plan for `llms.json` and also why it's called that and not `index2.json`. Perhaps `yolo.json` would be better name.

There is a real risk that people will take a dependency on `llms.json` for mission critical scenarios because it has adopted an attractive design point. It's great that you like the design! We will rely on documentation to tell people what the support policy (none) and compatibility bar (none) are. In the ancient days of the 1990s, there was a lot of talk about [fuzzy logic](https://en.wikipedia.org/wiki/Fuzzy_logic). `llms.json` is intended for consumers that can apply fuzzy logic in the presence of change.

This is a sample of the support statement we'll share for `llms.json`:

> The `llms.json` file is intended as an LLM-specific entrypoint into the release notes information graph. All other users are intended to use `index.json`. `index.json` has been designed to satisfy mission-critical workloads and uses a streamlined schema that is unlikely to ever change. `llms.json` is likely to be changed over time to accomodate the changing nature of LLMs and our understanding of how to best target their capabilities. There may or may not be before-the-fact notifications when `llms.json` is changed.

## Validation

This approach can be seen the opposite-end-of-the-spectrum solution compared to past practice. Such a radical design departure begs for some sort of evidence.

The [Release Graph Metrics](./metrics/README.md) suite includes several diverse queries over `index.json`, `llms.json`, and `releases-index.json`. The analysis considers correct results, query complexity, aesthetics, and data cost. The results speak for themselves.

The point-of-view of this spec is that this validation process proves the efficacy of the approach.

### Schema discovery

Before discussing the queries themselves, an interesting aspect of using an existing hypermedia scheme is schema discovery. You can quite reasonably walk up the hypermedia API and start asking it questions.

The following pattern enables asking which link relations are available for a given HAL resource.

```bash
ROOT="https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/index.json"

curl -s "$ROOT" | jq -r '._links | keys[]'
# latest
# latest-lts
# self
# timeline-index
```

The `_embedded` object can similarly be inspected.

```bash
ROOT="https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/index.json"

# What's embedded in the root index?
curl -s "$ROOT" | jq -r '._embedded | keys[]'
# releases
```

Other similar questions be asked by inspected by intereraction with `_links` and `_embedded`. Additional patterns are discussed in [Index Discovery](./metrics/index-discovery.md).

### Metrics

Several queries were developed to evalulate the effectiveness of the graph. The same queries were used for LLM eval testing. A couple of those tests are shared here to provide a quick understanding of graph performance. They represent a range of queries, capturing much of the spectrum of capability and complexity. In many cases, the existing `releases-index.json` format is incapable of providing the information required by a query.

#### Supported .NET Versions

**Query:** "Which .NET versions are currently supported?"

| Schema | Files Required | Total Transfer |
|--------|----------------|----------------|
| llms-index | `llms.json` | **5 KB** |
| hal-index | `index.json` | **5 KB** |
| releases-index | `releases-index.json` | **6 KB** |

**llms-index:** The `supported_releases` property provides a direct array—no filtering required:

```bash
LLMS="https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/llms.json"

curl -s "$LLMS" | jq -r '.supported_releases[]'
# 10.0
# 9.0
# 8.0
```

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

**Winner:** llms-index

- Direct array access, no filtering required
- Equivalent size to hal-index (5 KB)
- 17% smaller than releases-index

**Analysis:**

- **Completeness:** ✅ Equal—all three return the same list of supported versions.
- **Zero-fetch for LLMs:** The llms-index `supported_releases` array can be answered directly from embedded data without any jq filtering—ideal for AI assistants that have already fetched llms.json as their entry point.
- **Query complexity:** llms-index requires no `select()` filter; hal-index uses boolean filter; releases-index requires enum comparison with bracket notation.

---

#### .NET 6 EOL Details

**Query:** "When did .NET 6 go EOL, when was the last .NET 6 security patch and what CVEs did it fix?"

| Schema | Files Required | Total Transfer |
|--------|----------------|----------------|
| llms-index | `llms.json` → `index.json` → `6.0/index.json` → `6.0/6.0.35/index.json` | **45 KB** |
| hal-index | `index.json` → `6.0/index.json` → `6.0/6.0.35/index.json` | **40 KB** |
| releases-index | `releases-index.json` → `6.0/releases.json` | **1,612 KB** |

**llms-index:** EOL versions are not in `latest_patches[]`, so navigation through the release index is required:

```bash
LLMS="https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/llms.json"

# Step 1: Get releases-index link (6.0 not in latest_patches since it's EOL)
ROOT=$(curl -s "$LLMS" | jq -r '._links["releases-index"].href')

# Step 2: Get 6.0 version href
VERSION_HREF=$(curl -s "$ROOT" | jq -r '._embedded.releases[] | select(.version == "6.0") | ._links.self.href')

# Step 3: Get EOL date and latest-security link
VERSION_DATA=$(curl -s "$VERSION_HREF")
echo "$VERSION_DATA" | jq -r '"EOL: \(.eol_date | split("T")[0])"'
# EOL: 2024-11-12

# Step 4: Get last security patch details
SECURITY_HREF=$(echo "$VERSION_DATA" | jq -r '._links["latest-security"].href')
curl -s "$SECURITY_HREF" | jq -r '"Last security: \(.version) (\(.date | split("T")[0])) | CVEs: \(.cve_records | join(", "))"'
# Last security: 6.0.35 (2024-10-08) | CVEs: CVE-2024-43483, CVE-2024-43484, CVE-2024-43485
```

**hal-index:**

```bash
ROOT="https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/index.json"

# Step 1: Get 6.0 version href
VERSION_HREF=$(curl -s "$ROOT" | jq -r '._embedded.releases[] | select(.version == "6.0") | ._links.self.href')

# Step 2: Get EOL date and latest-security link
VERSION_DATA=$(curl -s "$VERSION_HREF")
echo "$VERSION_DATA" | jq -r '"EOL: \(.eol_date | split("T")[0])"'
# EOL: 2024-11-12

# Step 3: Get last security patch details
SECURITY_HREF=$(echo "$VERSION_DATA" | jq -r '._links["latest-security"].href')
curl -s "$SECURITY_HREF" | jq -r '"Last security: \(.version) (\(.date | split("T")[0])) | CVEs: \(.cve_records | join(", "))"'
# Last security: 6.0.35 (2024-10-08) | CVEs: CVE-2024-43483, CVE-2024-43484, CVE-2024-43485
```

**releases-index:**

```bash
ROOT="https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json"

# Step 1: Get EOL date from root (available inline)
curl -s "$ROOT" | jq -r '.["releases-index"][] | select(.["channel-version"] == "6.0") | "EOL: \(.["eol-date"])"'
# EOL: 2024-11-12

# Step 2: Get 6.0 releases.json URL and find last security patch
RELEASES_URL=$(curl -s "$ROOT" | jq -r '.["releases-index"][] | select(.["channel-version"] == "6.0") | .["releases.json"]')
curl -s "$RELEASES_URL" | jq -r '
  [.releases[] | select(.security == true)][0] |
  "Last security: \(.["release-version"]) (\(.["release-date"])) | CVEs: \([.["cve-list"][]?["cve-id"]] | join(", "))"'
# Last security: 6.0.35 (2024-10-08) | CVEs: CVE-2024-43483, CVE-2024-43484, CVE-2024-43485
```

**Winner:** hal-index (**40x smaller** than releases-index)

- llms-index requires extra hop through `releases-index` link since EOL versions aren't embedded
- hal-index starts directly at the release index
- releases-index EOL date is in root, but CVE query requires 1.6 MB file

**Analysis:**

- **Completeness:** ✅ Equal—all three return the same EOL date, patch version, and CVE IDs.
- **EOL version handling:** The llms-index optimizes for supported versions (`latest_patches[]`), requiring navigation for EOL queries. This is a reasonable tradeoff since EOL queries are less frequent.
- **CVE details:** To get CVE severity/titles (not just IDs), hal-index and llms-index can navigate to `timeline/2024/10/cve.json`; releases-index cannot provide this data.

---

#### Package CVE Check

**Query:** "Here's my project file with package references. Have any of my packages had CVEs in the last 6 months?"

```xml
<PackageReference Include="Microsoft.Build" Version="17.10.10" />
<PackageReference Include="Microsoft.Build.Tasks.Core" Version="17.8.10" />
```

| Schema | Files Required | Total Transfer |
|--------|----------------|----------------|
| llms-index | `llms.json` → 6 month cve.json files (via `prev-security`) | **~60 KB** |
| hal-index | `timeline/index.json` → `timeline/2025/index.json` → 6 month cve.json files | **~65 KB** |
| releases-index | N/A | N/A |

**llms-index:** Walk security timeline and check packages in cve.json with version comparison:

```bash
LLMS="https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/llms.json"

# Package references from project file (name and version)
declare -A PKGS
PKGS["Microsoft.Build"]="17.10.10"
PKGS["Microsoft.Build.Tasks.Core"]="17.8.10"

# Start from the latest security month
MONTH_HREF=$(curl -s "$LLMS" | jq -r '._links["latest-security-month"].href')

# Walk back 6 security months
for i in {1..6}; do
  DATA=$(curl -s "$MONTH_HREF")
  YEAR=$(echo "$DATA" | jq -r '.year')
  MONTH=$(echo "$DATA" | jq -r '.month')
  CVE_HREF=$(echo "$DATA" | jq -r '._links["cve-json"].href // empty')
  
  if [ -n "$CVE_HREF" ]; then
    CVE_DATA=$(curl -s "$CVE_HREF")
    
    for pkg in "${!PKGS[@]}"; do
      ver="${PKGS[$pkg]}"
      # Check vulnerability and get commit diff URLs
      echo "$CVE_DATA" | jq -r --arg pkg "$pkg" --arg ver "$ver" --arg ym "$YEAR-$MONTH" '
        .commits as $all_commits |
        .packages[]? | select(.name == $pkg) |
        if ($ver >= .min_vulnerable and $ver <= .max_vulnerable) then
          "\($ym) | \($pkg)@\($ver) | \(.cve_id) | vulnerable: \(.min_vulnerable)-\(.max_vulnerable) | fixed: \(.fixed)",
          (.commits[]? | "  diff: " + ($all_commits[.].url // "unknown"))
        else
          empty
        end
      '
    done
  fi
  
  MONTH_HREF=$(echo "$DATA" | jq -r '._links["prev-security"].href // empty')
  [ -z "$MONTH_HREF" ] && break
done
# 2025-10 | Microsoft.Build@17.10.10 | CVE-2025-55247 | vulnerable: 17.10.0-17.10.29 | fixed: 17.10.46
#   diff: https://github.com/dotnet/msbuild/commit/aa888d3214e5adb503c48c3bad2bfc6c5aff638a.diff
# 2025-10 | Microsoft.Build.Tasks.Core@17.8.10 | CVE-2025-55247 | vulnerable: 17.8.0-17.8.29 | fixed: 17.8.43
#   diff: https://github.com/dotnet/msbuild/commit/f0cbb13971c30ad15a3f252a8d0171898a01ec11.diff
```

**Winner:** llms-index

- Direct `latest-security-month` link as starting point
- Package-level CVE data with commit diff URLs
- Enables code review of security fixes without relying on nuget.org

**Analysis:**

- **Completeness:** ⚠️ releases-index does not provide package-level CVE data.
- **Version matching:** String comparison works for semver when patch versions have consistent digit counts.
- **Commit diffs:** The `.commits` lookup provides direct links to fix diffs on GitHub.
- **Timeline navigation:** Uses `prev-security` links to efficiently walk security history.

---
