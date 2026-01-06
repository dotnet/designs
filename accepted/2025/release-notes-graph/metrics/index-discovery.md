# Index Discovery

Query patterns for discovering and navigating the .NET release metadata graph. These patterns demonstrate how HAL's self-describing structure enables exploration without prior documentation.

## 1: List Available Link Relations

**Query:** "What operations are available from this index?"

The `_links` object in any HAL document describes all available navigation paths. This is the fundamental discovery pattern.

**Root index:**

```bash
curl -s "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/index.json" | jq -r '._links | keys[]'
# latest-lts-major
# latest-major
# self
# timeline
```

**Version index:**

```bash
curl -s "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/index.json" | jq -r '._links | keys[]'
# downloads
# latest-cve-json
# latest-month
# latest-patch
# latest-security-disclosures
# latest-security-month
# latest-security-patch
# manifest
# root
# self
```

**Patch index:**

```bash
curl -s "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/8.0/8.0.21/index.json" | jq -r '._links | keys[]'
# cve-json
# downloads
# major
# month
# prev-patch
# prev-security-patch
# release-json
# root
# security-disclosures
# self
```

**Analysis:**

- HAL's `_links` pattern makes APIs self-documenting
- No need to know the schema in advance—just inspect available links
- Naming conventions reveal link types: `-json`, `-markdown`, `-html`, `prev-`, `latest-`

---

## 2: Follow Self Links

**Query:** "How do I get the canonical URL for any resource?"

Every HAL resource includes a `self` link with its canonical URL. This enables reliable caching and reference.

```bash
# Get self link from root
curl -s "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/index.json" | jq -r '._links.self.href'
# https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/index.json

# Get self link from embedded resource
curl -s "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/index.json" | jq -r '._embedded.releases[] | select(.version == "10.0") | ._links.self.href'
# https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/index.json
```

**Analysis:**

- `self` links provide canonical URLs for caching
- Embedded resources include their own `self` links for navigation
- Never construct URLs manually—always follow links

---

## 3: Discover Embedded Data

**Query:** "What data is available without additional fetches?"

The `_embedded` object contains data that would otherwise require additional fetches. Inspect it to understand what's available inline.

```bash
# What's embedded in the root index?
curl -s "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/index.json" | jq -r '._embedded | keys[]'
# releases

# What fields are available in each embedded release?
curl -s "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/index.json" | jq -r '._embedded.releases[0] | keys[]'
# _links
# release_type
# supported
# version

# What's embedded in a patch index?
curl -s "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/8.0/8.0.21/index.json" | jq -r '._embedded | keys[]'
# runtime
# sdk
# sdk_feature_bands
```

**Analysis:**

- `_embedded` provides complete data inline—no dangling references
- Root embeds version summaries; patch indexes embed runtime info, SDK info, and feature bands
- Check `_embedded` first before following links to avoid unnecessary fetches

---

## 4: Navigate Version Hierarchy

**Query:** "How do I traverse from root to patch to CVE details?"

HAL links create a navigable hierarchy. Follow links to drill down or up.

```bash
# Root -> Version
curl -s "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/index.json" | jq -r '._embedded.releases[] | select(.version == "8.0") | ._links.self.href'
# https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/8.0/index.json

# Version -> Latest Security Patch
curl -s "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/8.0/index.json" | jq -r '._links["latest-security-patch"].href'
# https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/8.0/8.0.21/index.json

# Patch -> CVE Details
curl -s "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/8.0/8.0.21/index.json" | jq -r '._links["cve-json"].href'
# https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/8.0/8.0.21/cve.json

# Navigate back up: Patch -> Version
curl -s "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/8.0/8.0.21/index.json" | jq -r '._links["major"].href'
# https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/8.0/index.json
```

**Analysis:**

- `latest-security-patch` jumps directly to the most recent security patch
- `major` navigates back up to the version index
- Bidirectional links enable traversal in any direction

---

## 5: Discover Timeline Navigation

**Query:** "How do I explore CVEs by date rather than version?"

The timeline index provides date-based navigation, complementing version-based navigation.

```bash
# Root -> Timeline
curl -s "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/index.json" | jq -r '._links["timeline"].href'
# https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/index.json

curl -s "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/timeline/index.json" | jq -r '._links | keys[]'
# latest-lts-major
# latest-major
# latest-year
# root
# self

# Timeline -> Year
curl -s "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/timeline/index.json" | jq -r '._links["latest-year"].href'
# https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/index.json

curl -s "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/timeline/2025/index.json" | jq -r '._links | keys[]'
# latest-cve-json
# latest-major
# latest-month
# latest-security-disclosures
# latest-security-month
# prev-year
# self
# timeline

# Year -> Month
curl -s "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/timeline/2025/index.json" | jq -r '._links["latest-security-month"].href'
# https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/index.json

curl -s "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/timeline/2025/10/index.json" | jq -r '._links | keys[]'
# cve-json
# prev-month
# prev-security-month
# self
# timeline
# year
```

**Analysis:**

- Timeline provides an alternative entry point for date-based queries
- `prev-security-month` links skip non-security months for efficient traversal
- `latest-security-month` jumps directly to the most recent security content

---

## 6: Discover Documentation Links

**Query:** "What human-readable documentation is available?"

HAL indexes include links to rendered documentation alongside machine-readable data.

```bash
# What documentation links are available?
curl -s "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/manifest.json" | jq -r '._links | to_entries[] | select(.key | endswith("-html")) | .key'
# compatibility-html
# downloads-html
# release-blog-html
# supported-os-html
# usage-html
# whats-new-html

# Get the what's new documentation URL
curl -s "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/manifest.json" | jq -r '._links["whats-new-html"].href'
# https://learn.microsoft.com/dotnet/core/whats-new/dotnet-10/overview
```

**Analysis:**

- `-html` suffix indicates rendered documentation (GitHub blob or Microsoft Learn)
- `-markdown` suffix indicates raw markdown source
- Documentation links enable rich context without leaving the graph

---

## 7: Discover Link Naming Conventions

**Query:** "What patterns do link names follow?"

Link relation names follow consistent conventions that reveal their purpose.

| Suffix/Prefix | Meaning | Example |
|---------------|---------|---------|
| `-json` | Machine-readable JSON data | `cve-json`, `os-packages-json` |
| `-markdown` | Raw markdown source | `supported-os-markdown` |
| `-html` | HTML view (GitHub blob or Learn) | `whats-new-html`, `downloads-html` |
| `latest-` | Jump to most recent | `latest-security-patch`, `latest-month` |
| `prev-` | Backward navigation | `prev-security-month`, `prev-year` |

```bash
# Find all "latest-" links in a version index
curl -s "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/index.json" | jq -r '._links | keys[] | select(startswith("latest-"))'
# latest-cve-json
# latest-month
# latest-patch
# latest-security-disclosures
# latest-security-month
# latest-security-patch

# Find all "-json" links in a manifest
curl -s "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/manifest.json" | jq -r '._links | keys[] | select(endswith("-json"))'
# os-packages-json
# supported-os-json
```

**Analysis:**

- Consistent naming makes links predictable without documentation
- Prefixes indicate navigation direction or recency
- Suffixes indicate content format
