# Schema Metrics Comparison

This document compares the data transfer costs between the new **hal-index** schema and the legacy **releases-index** schema for common query patterns.

## Design Context

The hal-index schema was designed to solve fundamental problems with the releases-index approach:

1. **Cache Coherency** - The releases-index.json references external files (e.g., `9.0/releases.json`) that may have different CDN cache TTLs, leading to inconsistent data when patch versions are updated.

2. **Data Efficiency** - The `releases.json` files contain download URLs and hashes for every binary artifact, making them 30-50x larger than necessary for most queries.

3. **Atomic Consistency** - The hal-index uses HAL `_embedded` to include all referenced data in a single document, ensuring a consistent snapshot per fetch.

See [dotnet/core#10143](https://github.com/dotnet/core/issues/10143) for full design rationale.

## File Characteristics

The tables below show theoretical update frequency based on practice and design, with file sizes measured from actual files.

### Hal-Graph Files

| File | Size | Updates/Year | Description |
|------|------|--------------|-------------|
| `index.json` | 8 KB | ~1 | Root index with all major versions |
| `10.0/index.json` | 15 KB | ~12 | All 10.0 patches (fewer releases so far) |
| `9.0/index.json` | 28 KB | ~12 | All 9.0 patches with CVE references |
| `8.0/index.json` | 34 KB | ~12 | All 8.0 patches with CVE references |
| `timeline/index.json` | 8 KB | ~1 | Timeline root (all years) |
| `timeline/2025/index.json` | 15 KB | ~12 | Year index (all months) |
| `timeline/2024/07/index.json` | 10 KB | ~1 | Month index with embedded CVE summaries |
| `timeline/2025/01/cve.json` | 14 KB | ~1 | Full CVE details for a month |

### Releases-Index Files

| File | Size | Updates/Year | Description |
|------|------|--------------|-------------|
| `releases-index.json` | 6 KB | ~12 | Root index (version list only) |
| `10.0/releases.json` | **440 KB** | ~12 | All 10.0 releases with full download metadata |
| `9.0/releases.json` | **769 KB** | ~12 | All 9.0 releases with full download metadata |
| `8.0/releases.json` | **1,233 KB** | ~12 | All 8.0 releases with full download metadata |

### Measurements

Actual git commits in the last 12 months (Nov 2024 - Nov 2025):

| File | Commits | Notes |
|------|---------|-------|
| `releases-index.json` | 29 | Root index (all versions) |
| `10.0/releases.json` | 22 | Includes previews/RCs and SDK-only releases |
| `9.0/releases.json` | 24 | Includes SDK-only releases, fixes, URL rewrites |
| `8.0/releases.json` | 18 | Includes SDK-only releases, fixes, URL rewrites |

The commit counts are significantly higher than the theoretical ~12/year due to:

- **SDK-only releases**: Additional releases between Patch Tuesdays (e.g., [9.0.308 SDK](https://github.com/dotnet/core/commit/24a83fcc189ecf3c514dc06963ce779dcbf64ad5) released 8 days after November Patch Tuesday)
- **Metadata corrections**: Simple changes like [updating .NET 9's EOL date](https://github.com/dotnet/core/commit/24ff22598de88e3c9681e579aab5fe344cdc21b0) require updating both `releases-index.json` and `9.0/releases.json`
- **Post-release corrections**: `fix hashes`, `sdk array mismatch`
- **Infrastructure changes**: `Update file URLs`, `Rewrite URLs to builds.dotnet`
- **Rollbacks**: `Revert "Switch links..."`

The EOL date example illustrates a key architectural tradeoff: with releases-index, metadata changes to any version require updating the root file. With hal-index, we give up rich information in the root file, but in exchange we can safely propagate useful information to many locations (authored in `9.0/_manifest.json`, generated into `9.0/manifest.json`, `9.0/index.json`, timeline indexes, etc.)—provided it doesn't violate the core rule: **the root `index.json` is never touched for version-specific changes**.

Note that Patch Tuesday releases are batched—a single commit like [November 2025](https://github.com/dotnet/core/commit/484b00682d598f8d11a81607c257c1f0a099b84c) updates `releases-index.json`, `8.0/releases.json`, `9.0/releases.json`, and `10.0/releases.json` together (6,802 lines changed across 30 files). Even with batching, the root file still requires ~30 updates/year.

These massive commits are difficult to review and analyze. Mixing markdown documentation with JSON data files in the same commit makes it hard to distinguish content changes from data updates. The hal-index design separates these concerns—JSON index files are generated automatically, while markdown content is authored separately.

**Operational Risk:** These files are effectively mission-critical live-site APIs, driving hundreds of GBs of downloads monthly. Yet they require ~20-30 manual updates per year each, carrying risk of human error (as the fix commits demonstrate), cache invalidation complexity, and CDN propagation delays.

**Key insight:** The hal-index root file (`index.json`) is updated ~1x/year when a new major version is added. The releases-index root file (`releases-index.json`) is updated ~30x/year. This makes the hal-index root file ideal for aggressive CDN caching, while the releases-index files are constantly-moving targets.

## Query Comparison

### Capability Summary

| Query Type | hal-index | releases-index | Winner |
|------------|-----------|----------------|--------|
| List versions | ✅ | ✅ | releases-index (1.3x smaller) |
| Version lifecycle (EOL, LTS) | ✅ | ✅ | releases-index (1.3x smaller) |
| Latest patch per version | ✅ | ✅ | hal-index (23x smaller) |
| CVEs per version | ✅ | ✅ | hal-index (23x smaller) |
| CVEs per month | ✅ | ❌ | hal-index only |
| CVE details (severity, fixes) | ✅ | ❌ | hal-index only |
| Timeline navigation | ✅ | ❌ | hal-index only |
| SDK-first navigation | ✅ | ❌ | hal-index only |
| Version diff (severity, products) | ✅ | ⚠️ Partial | hal-index (15x smaller) |
| Breaking changes by category | ✅ | ❌ | hal-index only |
| EOL exposure analysis | ✅ | ⚠️ Partial | hal-index (with severity) |

The sections below demonstrate each query pattern with working examples.

### Version-Based Queries

The following queries navigate the schema by major version (8.0, 9.0, 10.0), drilling down to specific patches and CVE details.

#### Query: "What .NET versions are currently supported?"

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

### High Severity CVEs with Details

#### Query: "What High+ severity CVEs were fixed in the latest .NET 8.0 security patch, with titles?"

| Schema | Files Required | Total Transfer |
|--------|----------------|----------------|
| hal-index | `index.json` → `8.0/index.json` → `8.0/8.0.21/index.json` | **52 KB** |
| releases-index | `releases-index.json` + `8.0/releases.json` | **1,239 KB** (cannot answer) |

**hal-index:**

```bash
ROOT="https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/index.json"

# Step 1: Get the 8.0 version href
VERSION_HREF=$(curl -s "$ROOT" | jq -r '._embedded.releases[] | select(.version == "8.0") | ._links.self.href')

# Step 2: Get the latest security patch href
PATCH_HREF=$(curl -s "$VERSION_HREF" | jq -r '._links["latest-security"].href')

# Step 3: Filter HIGH+ severity with titles
curl -s "$PATCH_HREF" | jq -r '._embedded.disclosures[] | select(.cvss_severity == "HIGH" or .cvss_severity == "CRITICAL") | "\(.id): \(.title) (\(.cvss_severity))"'
# CVE-2025-55247: .NET Denial of Service Vulnerability (HIGH)
# CVE-2025-55315: .NET Security Feature Bypass Vulnerability (CRITICAL)
```

**releases-index:**

```bash
ROOT="https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json"

# Step 1: Get the 8.0 releases.json URL
RELEASES_URL=$(curl -s "$ROOT" | jq -r '.["releases-index"][] | select(.["channel-version"] == "8.0") | .["releases.json"]')

# Step 2: Find latest security release and get CVE IDs (best effort—no severity/title available)
curl -s "$RELEASES_URL" | jq -r '[.releases[] | select(.security == true)] | .[0] | .["cve-list"][] | "\(.["cve-id"]): (severity unknown) (title unknown)"'
# CVE-2025-55247: (severity unknown) (title unknown)
# CVE-2025-55315: (severity unknown) (title unknown)
# CVE-2025-55248: (severity unknown) (title unknown)
```

**Analysis:**

- **Completeness:** ❌ The releases-index only provides CVE IDs and URLs to external CVE databases. It does not include severity scores, CVSS ratings, or vulnerability titles. To get this information, you would need to fetch each CVE URL individually from cve.mitre.org.
- **Ergonomics:** The hal-index embeds full CVE details (`cvss_severity`, `cvss_score`, `title`, `fixes`) directly in the patch index, enabling single-query filtering by severity.

**Winner:** hal-index (releases-index cannot answer this query—CVE severity and titles are not available)

### Servicing Version Diff

#### Query: "What changed between .NET 8.0.15 and 8.0.22?"

| Schema | Files Required | Total Transfer |
|--------|----------------|----------------|
| hal-index | `index.json` → `8.0/index.json` + 3 patch indexes | **~80 KB** |
| releases-index | `releases-index.json` + `8.0/releases.json` | **1,239 KB** (partial data) |

**hal-index:**

This query requires two passes: first to get the release summaries, then to fetch CVE details for severity filtering and affected products.

```bash
ROOT="https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/index.json"

# Configuration
MAJOR_VERSION="8.0"
FROM_PATCH=15
TO_PATCH=22
SEVERITY_FILTER="CRITICAL"  # Minimum severity: CRITICAL, HIGH, MEDIUM, or LOW (all)

# Step 1: Get the major version href
VERSION_HREF=$(curl -s "$ROOT" | jq -r --arg ver "$MAJOR_VERSION" '._embedded.releases[] | select(.version == $ver) | ._links.self.href')

# Step 2: Get release summaries and security release URLs
VERSION_DATA=$(curl -s "$VERSION_HREF")

# Extract security release URLs for CVE detail fetching
SECURITY_HREFS=$(echo "$VERSION_DATA" | jq -r --argjson from "$FROM_PATCH" --argjson to "$TO_PATCH" '
  [._embedded.releases[] |
   select((.version | split(".")[2] | tonumber) > $from and (.version | split(".")[2] | tonumber) <= $to) |
   select(.security) |
   ._links.self.href] | .[]
')

# Step 3: Fetch CVE details from each security release and aggregate
CVE_DETAILS=$(for HREF in $SECURITY_HREFS; do
  curl -s "$HREF" | jq -c '._embedded.disclosures[]? | {id, cvss_severity, affected_products, title}'
done | jq -s 'unique_by(.id)')

# Step 4: Generate the diff report with severity-filtered CVE IDs
echo "$VERSION_DATA" | jq -r --arg major "$MAJOR_VERSION" --argjson from "$FROM_PATCH" --argjson to "$TO_PATCH" \
  --arg severity "$SEVERITY_FILTER" --argjson cve_details "$CVE_DETAILS" '
  # Filter releases in range (excluding start, including end)
  [._embedded.releases[] |
   select((.version | split(".")[2] | tonumber) > $from and (.version | split(".")[2] | tonumber) <= $to)
  ] as $releases |

  # Filter CVEs by minimum severity
  [$cve_details[] | select(
    ($severity == "LOW") or
    ($severity == "MEDIUM" and (.cvss_severity == "MEDIUM" or .cvss_severity == "HIGH" or .cvss_severity == "CRITICAL")) or
    ($severity == "HIGH" and (.cvss_severity == "HIGH" or .cvss_severity == "CRITICAL")) or
    ($severity == "CRITICAL" and .cvss_severity == "CRITICAL")
  )] as $filtered_cves |

  # Aggregate affected products across all CVEs
  [$cve_details[].affected_products // [] | .[]] | unique | sort as $all_products |

  {
    from_version: "\($major).\($from)",
    to_version: "\($major).\($to)",
    from_date: (._embedded.releases[] | select(.version == "\($major).\($from)") | .date | split("T")[0]),
    to_date: (._embedded.releases[] | select(.version == "\($major).\($to)") | .date | split("T")[0]),
    total_releases: ($releases | length),
    security_releases: ([$releases[] | select(.security)] | length),
    non_security_releases: ([$releases[] | select(.security | not)] | length),
    total_cves: ([$releases[].cve_records? // [] | .[]] | unique | length),
    cve_ids: [$filtered_cves[] | .id],
    cve_severity_filter: $severity,
    affected_products: $all_products,
    releases: [$releases[] | {version, date: (.date | split("T")[0]), security, cve_count}]
  }
'
```

**Output:**

```json
{
  "from_version": "8.0.15",
  "to_version": "8.0.22",
  "from_date": "2025-04-08",
  "to_date": "2025-11-11",
  "total_releases": 7,
  "security_releases": 3,
  "non_security_releases": 4,
  "total_cves": 5,
  "cve_ids": [
    "CVE-2025-55315"
  ],
  "cve_severity_filter": "CRITICAL",
  "affected_products": [
    "aspnetcore-runtime",
    "dotnet-runtime",
    "windowsdesktop-runtime"
  ],
  "releases": [
    { "version": "8.0.22", "date": "2025-11-11", "security": false, "cve_count": 0 },
    { "version": "8.0.21", "date": "2025-10-14", "security": true, "cve_count": 3 },
    { "version": "8.0.20", "date": "2025-09-09", "security": false, "cve_count": 0 },
    { "version": "8.0.19", "date": "2025-08-05", "security": false, "cve_count": 0 },
    { "version": "8.0.18", "date": "2025-07-08", "security": false, "cve_count": 0 },
    { "version": "8.0.17", "date": "2025-06-10", "security": true, "cve_count": 1 },
    { "version": "8.0.16", "date": "2025-05-22", "security": true, "cve_count": 1 }
  ]
}
```

To include all CVEs regardless of severity:

```bash
SEVERITY_FILTER="LOW"  # LOW is the minimum, so this includes all CVEs
```

The script above outputs `from_date` and `to_date`. To calculate the time gap, pipe the output through an additional jq filter:

```bash
# Pipe the output to calculate days between versions
... | jq -r '
  # Parse ISO dates and calculate difference
  ((.to_date | strptime("%Y-%m-%d") | mktime) -
   (.from_date | strptime("%Y-%m-%d") | mktime)) / 86400 | floor as $days |
  . + {
    days_behind: $days,
    months_behind: (($days / 30) | floor)
  }
'
```

This adds `days_behind` and `months_behind` to the output:

```json
{
  "from_version": "8.0.15",
  "to_version": "8.0.22",
  "from_date": "2025-04-08",
  "to_date": "2025-11-11",
  "days_behind": 217,
  "months_behind": 7,
  ...
}
```

**releases-index:**

```bash
ROOT="https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json"

# Configuration
MAJOR_VERSION="8.0"
FROM_PATCH=15
TO_PATCH=22

# Step 1: Get the releases.json URL for the major version
RELEASES_URL=$(curl -s "$ROOT" | jq -r --arg ver "$MAJOR_VERSION" '.["releases-index"][] | select(.["channel-version"] == $ver) | .["releases.json"]')

# Step 2: Filter releases in range and aggregate
curl -s "$RELEASES_URL" | jq -r --arg major "$MAJOR_VERSION" --argjson from "$FROM_PATCH" --argjson to "$TO_PATCH" '
  # Filter releases in range (excluding start, including end)
  [.releases[] | select(
    (.["release-version"] | split(".")[2] | tonumber) > $from and
    (.["release-version"] | split(".")[2] | tonumber) <= $to
  )] as $releases |

  {
    from_version: "\($major).\($from)",
    to_version: "\($major).\($to)",
    from_date: ([.releases[] | select(.["release-version"] == "\($major).\($from)")] | .[0] | .["release-date"]),
    to_date: ([.releases[] | select(.["release-version"] == "\($major).\($to)")] | .[0] | .["release-date"]),
    total_releases: ($releases | length),
    security_releases: ([$releases[] | select(.security)] | length),
    non_security_releases: ([$releases[] | select(.security | not)] | length),
    total_cves: ([$releases[].["cve-list"]? // [] | .[]] | unique | length),
    cve_ids: ([$releases[].["cve-list"]? // [] | .[] | .["cve-id"]] | unique | sort),
    cve_severity_filter: "(not available)",
    affected_products: "(not available)",
    releases: [$releases[] | {
      version: .["release-version"],
      date: .["release-date"],
      security,
      cve_count: ([.["cve-list"]? // [] | .[]] | length)
    }]
  }
'
```

**Output:**

```json
{
  "from_version": "8.0.15",
  "to_version": "8.0.22",
  "from_date": "2025-04-08",
  "to_date": "2025-11-11",
  "total_releases": 7,
  "security_releases": 3,
  "non_security_releases": 4,
  "total_cves": 5,
  "cve_ids": [
    "CVE-2025-26646",
    "CVE-2025-30399",
    "CVE-2025-55247",
    "CVE-2025-55248",
    "CVE-2025-55315"
  ],
  "cve_severity_filter": "(not available)",
  "affected_products": "(not available)",
  "releases": [
    { "version": "8.0.22", "date": "2025-11-11", "security": false, "cve_count": 0 },
    { "version": "8.0.21", "date": "2025-10-14", "security": true, "cve_count": 3 },
    { "version": "8.0.20", "date": "2025-09-09", "security": false, "cve_count": 0 },
    { "version": "8.0.19", "date": "2025-08-05", "security": false, "cve_count": 0 },
    { "version": "8.0.18", "date": "2025-07-08", "security": false, "cve_count": 0 },
    { "version": "8.0.17", "date": "2025-06-10", "security": true, "cve_count": 1 },
    { "version": "8.0.16", "date": "2025-05-22", "security": true, "cve_count": 1 }
  ]
}
```

**Analysis:**

- **Completeness:** ⚠️ Partial—the releases-index can count releases and list CVE IDs, but cannot provide CVE severity, affected products, or detailed CVE information without fetching external CVE URLs.
- **Severity filtering:** The hal-index allows filtering `cve_ids` to specific severity levels (CRITICAL, HIGH, etc.) via the `SEVERITY_FILTER` variable, while `total_cves` always shows the complete count.
- **Affected products:** The hal-index aggregates all affected products across the version range (e.g., `dotnet-runtime`, `aspnetcore-runtime`), enabling teams to identify which components need patching.
- **Executive reporting:** For CIO/CTO reporting, the hal-index provides actionable data (severity-filtered CVEs, affected products) while the releases-index only provides CVE IDs that require manual lookup.

**Winner:** hal-index (**15x smaller**, with CVE severity filtering and affected products)

### Breaking Changes Summary

#### Query: "How many breaking changes are in .NET 10, by category?"

| Schema | Files Required | Total Transfer |
|--------|----------------|----------------|
| hal-index | `index.json` → `10.0/index.json` → `10.0/breaking-changes.json` | **~45 KB** |
| releases-index | N/A | N/A (not available) |

**hal-index:**

```bash
ROOT="https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/index.json"
VERSION="10.0"

# Step 1: Get the version index
VERSION_HREF=$(curl -s "$ROOT" | jq -r --arg ver "$VERSION" '._embedded.releases[] | select(.version == $ver) | ._links.self.href')

# Step 2: Get the breaking-changes.json link
BC_HREF=$(curl -s "$VERSION_HREF" | jq -r '._links["breaking-changes-json"].href')

# Step 3: Get counts by category
curl -s "$BC_HREF" | jq -r '
  "Total: \(.breaking_change_count)\n",
  ([.breaks[].category] | group_by(.) | map({category: .[0], count: length}) | sort_by(-.count) | .[] | "  \(.category): \(.count)")
'
```

**Output:**

```text
Total: 83

  sdk: 23
  core-libraries: 16
  aspnet-core: 9
  cryptography: 8
  extensions: 6
  windows-forms: 6
  interop: 3
  networking: 3
  reflection: 2
  serialization: 2
  wpf: 2
  containers: 1
  globalization: 1
  install-tool: 1
```

**releases-index:** Not available.

**Winner:** hal-index only

### Breaking Changes by Category

#### Query: "What are the core-libraries breaking changes in .NET 10?"

| Schema | Files Required | Total Transfer |
|--------|----------------|----------------|
| hal-index | `index.json` → `10.0/index.json` → `10.0/breaking-changes.json` | **~45 KB** |
| releases-index | N/A | N/A (not available) |

**hal-index:**

```bash
ROOT="https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/index.json"
VERSION="10.0"
CATEGORY="core-libraries"

# Step 1: Get the version index
VERSION_HREF=$(curl -s "$ROOT" | jq -r --arg ver "$VERSION" '._embedded.releases[] | select(.version == $ver) | ._links.self.href')

# Step 2: Get the breaking-changes.json link
BC_HREF=$(curl -s "$VERSION_HREF" | jq -r '._links["breaking-changes-json"].href')

# Step 3: Get breaking changes for category
curl -s "$BC_HREF" | jq -r --arg cat "$CATEGORY" '.breaks[] | select(.category == $cat) | "- \(.title)"'
```

**Output:**

```text
- ActivitySource.CreateActivity and ActivitySource.StartActivity behavior changes
- System.Linq.AsyncEnumerable in .NET 10
- BufferedStream.WriteByte no longer performs implicit flush
- C# 14 overload resolution with span parameters
- Default trace context propagator updated to W3C standard
- 'DynamicallyAccessedMembers' annotation removed from 'DefaultValueAttribute' ctor
- DriveInfo.DriveFormat returns Linux filesystem types
- FilePatternMatch.Stem changed to non-nullable
- Consistent shift behavior in generic math
- Specifying explicit struct Size disallowed with InlineArray
- LDAP DirectoryControl parsing is now more stringent
- MacCatalyst version normalization
- .NET 10 obsoletions with custom IDs
- .NET runtime no longer provides default termination signal handler
- Arm64 SVE nonfaulting loads require mask parameter
- GnuTarEntry and PaxTarEntry exclude atime and ctime by default
```

**releases-index:** Not available.

**Winner:** hal-index only

### Breaking Changes Documentation URLs

#### Query: "Get the raw markdown URLs for core-libraries breaking changes (for LLM context)"

| Schema | Files Required | Total Transfer |
|--------|----------------|----------------|
| hal-index | `index.json` → `10.0/index.json` → `10.0/breaking-changes.json` | **~45 KB** |
| releases-index | N/A | N/A (not available) |

**hal-index:**

```bash
ROOT="https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/index.json"
VERSION="10.0"
CATEGORY="core-libraries"

# Step 1: Get the version index
VERSION_HREF=$(curl -s "$ROOT" | jq -r --arg ver "$VERSION" '._embedded.releases[] | select(.version == $ver) | ._links.self.href')

# Step 2: Get the breaking-changes.json link
BC_HREF=$(curl -s "$VERSION_HREF" | jq -r '._links["breaking-changes-json"].href')

# Step 3: Get raw documentation URLs for category
curl -s "$BC_HREF" | jq -r --arg cat "$CATEGORY" '
  .breaks[] | select(.category == $cat) | .references[] | select(.type == "documentation-source") | .url
'
```

**Output:**

```text
https://raw.githubusercontent.com/dotnet/docs/main/docs/core/compatibility/core-libraries/10.0/activity-sampling.md
https://raw.githubusercontent.com/dotnet/docs/main/docs/core/compatibility/core-libraries/10.0/asyncenumerable.md
https://raw.githubusercontent.com/dotnet/docs/main/docs/core/compatibility/core-libraries/10.0/bufferedstream-writebyte-flush.md
https://raw.githubusercontent.com/dotnet/docs/main/docs/core/compatibility/core-libraries/10.0/csharp-overload-resolution.md
https://raw.githubusercontent.com/dotnet/docs/main/docs/core/compatibility/core-libraries/10.0/default-trace-context-propagator.md
https://raw.githubusercontent.com/dotnet/docs/main/docs/core/compatibility/core-libraries/10.0/defaultvalueattribute-dynamically-accessed-members.md
https://raw.githubusercontent.com/dotnet/docs/main/docs/core/compatibility/core-libraries/10.0/driveinfo-driveformat-linux.md
https://raw.githubusercontent.com/dotnet/docs/main/docs/core/compatibility/core-libraries/10.0/filepatternmatch-stem-nonnullable.md
https://raw.githubusercontent.com/dotnet/docs/main/docs/core/compatibility/core-libraries/10.0/generic-math.md
https://raw.githubusercontent.com/dotnet/docs/main/docs/core/compatibility/core-libraries/10.0/inlinearray-explicit-size-disallowed.md
https://raw.githubusercontent.com/dotnet/docs/main/docs/core/compatibility/core-libraries/10.0/ldap-directorycontrol-parsing.md
https://raw.githubusercontent.com/dotnet/docs/main/docs/core/compatibility/core-libraries/10.0/maccatalyst-version-normalization.md
https://raw.githubusercontent.com/dotnet/docs/main/docs/core/compatibility/core-libraries/10.0/obsolete-apis.md
https://raw.githubusercontent.com/dotnet/docs/main/docs/core/compatibility/core-libraries/10.0/sigterm-signal-handler.md
https://raw.githubusercontent.com/dotnet/docs/main/docs/core/compatibility/core-libraries/10.0/sve-nonfaulting-loads-mask-parameter.md
https://raw.githubusercontent.com/dotnet/docs/main/docs/core/compatibility/core-libraries/10.0/tar-atime-ctime-default.md
```

**releases-index:** Not available.

**Analysis:**

- **LLM context:** These raw markdown URLs can be fetched and fed directly to an LLM for analysis or migration assistance.
- **Reference types:** Each breaking change includes multiple reference types (`documentation`, `documentation-source`, `announcement`)—the `documentation-source` type provides the raw markdown.

**Winner:** hal-index only

### Timeline-Based Queries

The following queries demonstrate the hal-index timeline navigation model, which organizes releases chronologically rather than by version.

### Recent CVEs Across All Versions

#### Query: "What CVEs were fixed in the last 2 security releases?"

| Schema | Files Required | Total Transfer |
|--------|----------------|----------------|
| hal-index | `timeline/index.json` → `timeline/2025/index.json` | **23 KB** |
| releases-index | `releases-index.json` + 3 releases.json | **2,448 KB** |

**hal-index:**

```bash
TIMELINE="https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/timeline/index.json"

# Step 1: Get the latest year href
YEAR_HREF=$(curl -s "$TIMELINE" | jq -r '._embedded.years[0]._links.self.href')

# Step 2: Get the last 2 security months with CVEs
curl -s "$YEAR_HREF" | jq -r '[._embedded.months[] | select(.security)] | .[0:2] | .[] | "\(.month)/2025: \(.cve_records | join(", "))"'
# 10/2025: CVE-2025-55248, CVE-2025-55315, CVE-2025-55247
# 06/2025: CVE-2025-30399
```

**releases-index:**

```bash
ROOT="https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json"

# Get all supported version releases.json URLs
URLS=$(curl -s "$ROOT" | jq -r '.["releases-index"][] | select(.["support-phase"] == "active") | .["releases.json"]')

# For each version, find security releases and collect CVEs (requires multiple large fetches)
for URL in $URLS; do
  curl -s "$URL" | jq -r '.releases[] | select(.security == true) | "\(.["release-date"]): \([.["cve-list"][]? | .["cve-id"]] | join(", "))"'
done | sort -r | head -6
# 2025-10-14: CVE-2025-55247, CVE-2025-55315, CVE-2025-55248
# 2025-10-14: CVE-2025-55247, CVE-2025-55315, CVE-2025-55248
# 2025-10-14: CVE-2025-55247, CVE-2025-55315, CVE-2025-55248
# 2025-06-10: CVE-2025-30399
# 2025-06-10: CVE-2025-30399
# 2025-06-10: CVE-2025-30399
```

**Analysis:**

- **Completeness:** ⚠️ Partial—the releases-index can find CVEs by date, but produces duplicate entries (one per version) and cannot group by month without additional post-processing.
- **Ergonomics:** The hal-index timeline is purpose-built for chronological queries. The releases-index requires fetching all version files (2.4 MB) and manually correlating dates to find "last 2 security releases."
- **Data model:** The releases-index organizes by version; the hal-index timeline organizes by date. For "recent CVEs" queries, the timeline model is fundamentally better suited.

**Winner:** hal-index (**107x smaller**)

### CVE Details for a Month

#### Query: "What CVEs were disclosed in January 2025 with full details?"

| Schema | Files Required | Total Transfer |
|--------|----------------|----------------|
| hal-index | `timeline/index.json` → `timeline/2025/index.json` → `timeline/2025/01/cve.json` | **37 KB** |
| releases-index | All releases.json (13 versions) | **8.2 MB** (cannot answer) |

**hal-index:**

```bash
TIMELINE="https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/timeline/index.json"

# Step 1: Get 2025 year href
YEAR_HREF=$(curl -s "$TIMELINE" | jq -r '._embedded.years[] | select(.year == "2025") | ._links.self.href')
# https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/timeline/2025/index.json

# Step 2: Get January cve.json href
CVE_HREF=$(curl -s "$YEAR_HREF" | jq -r '._embedded.months[] | select(.month == "01") | ._links["cve-json"].href')
# https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/timeline/2025/01/cve.json

# Step 3: Get CVE details
curl -s "$CVE_HREF" | jq -r '.disclosures[] | "\(.id): \(.problem)"'
# CVE-2025-21171: .NET Remote Code Execution Vulnerability
# CVE-2025-21172: .NET and Visual Studio Remote Code Execution Vulnerability
# CVE-2025-21176: .NET and Visual Studio Remote Code Execution Vulnerability
# CVE-2025-21173: .NET Elevation of Privilege Vulnerability
```

**releases-index:**

```bash
ROOT="https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json"

# Must fetch ALL version releases.json files—cannot filter by "currently supported"
# because we need versions that were supported in January 2025, not today
URLS=$(curl -s "$ROOT" | jq -r '.["releases-index"][] | .["releases.json"]')

# Find January 2025 releases and get CVE IDs (no details available)
for URL in $URLS; do
  curl -s "$URL" | jq -r '.releases[] | select(.["release-date"] | startswith("2025-01")) | select(.security == true) | .["cve-list"][]? | "\(.["cve-id"]): (no description available)"'
done | sort -u
# CVE-2025-21171: (no description available)
# CVE-2025-21172: (no description available)
# CVE-2025-21173: (no description available)
# CVE-2025-21176: (no description available)
```

**Analysis:**

- **Completeness:** ❌ The releases-index provides only CVE IDs. The query asks for "full details" including problem descriptions, CVSS scores, affected products, and fix commits—none of which are available.
- **Historical queries:** The releases-index has no way to determine which versions were supported at a given point in time. To reliably find all CVEs for January 2025, you must fetch *every* version's releases.json file (not just currently supported versions), significantly increasing data transfer.
- **Ergonomics:** The hal-index provides a dedicated `cve.json` file per month with complete CVE records. The releases-index requires fetching all version files and provides only minimal data.

**Winner:** hal-index (**221x smaller**, and releases-index cannot answer this query—CVE details are not available)

### Security Patches in the Last 12 Months

#### Query: "List all CVEs fixed in the last 12 months"

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

### Critical CVE This Month

#### Query: "Is there a critical CVE in any supported release this month?" (November 2025)

| Schema | Files Required | Total Transfer |
|--------|----------------|----------------|
| hal-index | `timeline/index.json` → `timeline/2025/index.json` → `timeline/2025/11/index.json` | **28 KB** |
| releases-index | `releases-index.json` + all supported releases.json | **2.4+ MB** (incomplete—no severity data) |

**hal-index:**

```bash
TIMELINE="https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/timeline/index.json"

# Step 1: Get 2025 year href
YEAR_HREF=$(curl -s "$TIMELINE" | jq -r '._embedded.years[] | select(.year == "2025") | ._links.self.href')

# Step 2: Get November month href
MONTH_HREF=$(curl -s "$YEAR_HREF" | jq -r '._embedded.months[] | select(.month == "11") | ._links.self.href')

# Step 3: Check for CRITICAL CVEs
curl -s "$MONTH_HREF" | jq -r '._embedded.disclosures // [] | .[] | select(.cvss_severity == "CRITICAL") | "\(.id): \(.title)"'
# (no critical CVEs this month)
```

**releases-index:**

```bash
ROOT="https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json"

# Get all supported version releases.json URLs
URLS=$(curl -s "$ROOT" | jq -r '.["releases-index"][] | select(.["support-phase"] == "active") | .["releases.json"]')

# Find November 2025 releases and check for CVEs (cannot determine severity)
for URL in $URLS; do
  curl -s "$URL" | jq -r '
    .releases[] |
    select(.["release-date"] | startswith("2025-11")) |
    select(.security == true) |
    .["cve-list"][]? | "\(.["cve-id"]): (severity unknown)"'
done | sort -u
# (no output—November 2025 had no security releases)
```

**Analysis:**

- **Completeness:** ❌ The releases-index cannot answer this query. Even if there were CVEs in November, the schema only provides CVE IDs and URLs—no severity information. You would need to fetch each CVE URL from cve.mitre.org and parse the CVSS score.
- **Ergonomics:** The hal-index embeds `cvss_severity` directly in the disclosure records, enabling single-query filtering for CRITICAL vulnerabilities.
- **Use case:** This is a common security operations query ("Do I need to patch urgently?"). The hal-index answers it in 28 KB; the releases-index cannot answer it at all.

**Winner:** hal-index (**88x smaller**, and releases-index cannot answer this query—CVE severity is not available)

### Hybrid Queries

The following queries combine version and timeline navigation, demonstrating the full power of the hal-index design.

### CVE Exposure for EOL Version

#### Query: "If I'm still on .NET 6, which CVEs am I likely exposed to?"

This query finds all CVEs fixed in supported versions since .NET 6 went end-of-life. If you're running any .NET 6 version, you're potentially exposed to every vulnerability patched after .NET 6 stopped receiving updates.

| Schema | Files Required | Total Transfer |
|--------|----------------|----------------|
| hal-index | `index.json` → `6.0/index.json` → timeline months (via `release-month` + `next` links) | **~50-100 KB** |
| releases-index | `releases-index.json` + all supported releases.json | **2.4+ MB** (partial—no severity data) |

**hal-index:**

```bash
ROOT="https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/index.json"

# Configuration
EOL_VERSION="6.0"

# Step 1: Get the EOL version's last patch and its timeline month link
VERSION_HREF=$(curl -s "$ROOT" | jq -r --arg ver "$EOL_VERSION" '._embedded.releases[] | select(.version == $ver) | ._links.self.href')
VERSION_DATA=$(curl -s "$VERSION_HREF")
LAST_PATCH=$(echo "$VERSION_DATA" | jq -r '._embedded.releases[0]')
LAST_PATCH_DATE=$(echo "$LAST_PATCH" | jq -r '.date | split("T")[0]')
MONTH_HREF=$(echo "$LAST_PATCH" | jq -r '._links["release-month"].href')
echo "Last .NET $EOL_VERSION patch: $LAST_PATCH_DATE"
# Last .NET 6.0 patch: 2024-11-12

# Step 2: Walk forward from EOL month, collecting CVEs
echo ""
echo "CVEs fixed after .NET $EOL_VERSION EOL:"
echo "========================================"

CURRENT_HREF="$MONTH_HREF"
while [ -n "$CURRENT_HREF" ]; do
  DATA=$(curl -s "$CURRENT_HREF")

  # Move to next month first (we want CVEs AFTER the EOL month)
  CURRENT_HREF=$(echo "$DATA" | jq -r '._links.next.href // empty')
  [ -z "$CURRENT_HREF" ] && break

  DATA=$(curl -s "$CURRENT_HREF")
  YEAR_MONTH=$(echo "$DATA" | jq -r '"\(.year)-\(.month)"')
  SECURITY=$(echo "$DATA" | jq -r '.security')

  if [ "$SECURITY" = "true" ]; then
    echo ""
    echo "$YEAR_MONTH:"
    echo "$DATA" | jq -r '._embedded.disclosures[] | "  \(.id) [\(.cvss_severity)]: \(.title)"'
  fi

  CURRENT_HREF=$(echo "$DATA" | jq -r '._links.next.href // empty')
done
```

**Output:**

```text
Last .NET 6.0 patch: 2024-11-12

CVEs fixed after .NET 6.0 EOL:
========================================

2025-01:
  CVE-2025-21171 [HIGH]: .NET Remote Code Execution Vulnerability
  CVE-2025-21172 [HIGH]: .NET and Visual Studio Remote Code Execution Vulnerability
  CVE-2025-21173 [MEDIUM]: .NET Elevation of Privilege Vulnerability
  CVE-2025-21176 [HIGH]: .NET and Visual Studio Remote Code Execution Vulnerability

2025-03:
  CVE-2025-24070 [HIGH]: ASP.NET Core Elevation of Privilege Vulnerability

2025-04:
  CVE-2025-26682 [HIGH]: .NET Denial of Service Vulnerability

2025-05:
  CVE-2025-26646 [MEDIUM]: .NET Spoofing Vulnerability

2025-06:
  CVE-2025-30399 [HIGH]: .NET Remote Code Execution Vulnerability

2025-10:
  CVE-2025-55247 [HIGH]: .NET Denial of Service Vulnerability
  CVE-2025-55248 [MEDIUM]: .NET Denial of Service Vulnerability
  CVE-2025-55315 [CRITICAL]: .NET Security Feature Bypass Vulnerability
```

**releases-index:**

```bash
ROOT="https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json"

# Configuration
EOL_VERSION="6.0"

# Step 1: Get the EOL version's last patch date
EOL_RELEASES_URL=$(curl -s "$ROOT" | jq -r --arg ver "$EOL_VERSION" '.["releases-index"][] | select(.["channel-version"] == $ver) | .["releases.json"]')
LAST_PATCH_DATE=$(curl -s "$EOL_RELEASES_URL" | jq -r '.releases[0]["release-date"]')
echo "Last .NET $EOL_VERSION patch: $LAST_PATCH_DATE"

# Step 2: Get all supported version releases.json URLs
URLS=$(curl -s "$ROOT" | jq -r '.["releases-index"][] | select(.["support-phase"] == "active") | .["releases.json"]')

# Step 3: Find all security releases after the EOL date
echo ""
echo "CVEs fixed after .NET $EOL_VERSION EOL:"
echo "========================================"

for URL in $URLS; do
  curl -s "$URL" | jq -r --arg cutoff "$LAST_PATCH_DATE" '
    .releases[] |
    select(.security == true) |
    select(.["release-date"] > $cutoff) |
    . as $rel | .["cve-list"][]? | "\($rel["release-date"]): \(.["cve-id"]) (severity unknown)"'
done | sort -u
```

**Output:**

```text
Last .NET 6.0 patch: 2024-11-12

CVEs fixed after .NET 6.0 EOL:
========================================
2025-01-14: CVE-2025-21172 (severity unknown)
2025-01-14: CVE-2025-21173 (severity unknown)
2025-01-14: CVE-2025-21176 (severity unknown)
2025-03-11: CVE-2025-24070 (severity unknown)
2025-04-08: CVE-2025-26682 (severity unknown)
2025-05-13: CVE-2025-26646 (severity unknown)
2025-06-10: CVE-2025-30399 (severity unknown)
2025-10-14: CVE-2025-55247 (severity unknown)
2025-10-14: CVE-2025-55248 (severity unknown)
2025-10-14: CVE-2025-55315 (severity unknown)
```

**Analysis:**

- **Completeness:** ⚠️ Partial—the releases-index finds CVE IDs but misses CVE-2025-21171 (only affected .NET 9.0, not in "active" support phase query). More critically, it cannot provide severity or titles.
- **Actionability:** The hal-index output is immediately actionable—security teams can see that CVE-2025-55315 is CRITICAL and prioritize accordingly. The releases-index output requires manual lookup of each CVE.
- **Cross-index navigation:** The hal-index `release-month` link connects version and timeline indexes directly—no need to parse dates or search the timeline. Combined with `next` links, this enables natural forward traversal from any release.
- **Executive reporting:** "You're exposed to 1 CRITICAL, 6 HIGH, and 3 MEDIUM vulnerabilities" vs "You're exposed to 10 CVEs of unknown severity."

**Winner:** hal-index (releases-index provides incomplete data with no severity information)

## Cache Coherency

### releases-index Problem

```text
Client cache state:
├── releases-index.json (fetched now)
│   └── "latest-release": "9.0.12"  ← Just updated
└── 9.0/releases.json (fetched 1 hour ago)
    └── releases[0]: "9.0.11"  ← Stale!

Result: Client sees 9.0.12 as latest but can't find its data
```

### hal-index Solution

```text
Client cache state:
├── index.json (fetched now)
│   └── _embedded.releases[]: includes 9.0 summary
└── 9.0/index.json (fetched now)
    └── _embedded.releases[0]: "9.0.12" with full data

Result: Each file is a consistent snapshot
```

The HAL `_embedded` pattern ensures that any data referenced within a document is included in that document. There are no "dangling pointers" to data that might not exist in a cached copy of another file.

## Summary

| Metric | hal-index | releases-index |
|--------|-------------|----------------|
| Basic version queries | 8 KB | 6 KB |
| CVE queries (latest security patch) | 52 KB | 1,239 KB |
| Recent CVEs (last 2 security releases) | 23 KB | 2.4 MB |
| CVEs in last 12 months | ~90 KB | 2.4 MB |
| Version diff with severity + products | ~80 KB | 1,239 KB (partial—no severity/products) |
| Cache coherency | ✅ Atomic | ❌ TTL mismatch risk |
| Query syntax | snake_case (dot notation) | kebab-case (bracket notation) |
| Link traversal | `._links.self.href` | `.["releases.json"]` |
| Boolean filters | `supported`, `security` | `support-phase == "active"` |
| CVE details | ✅ Full | ❌ ID + URL only |
| Timeline navigation | ✅ | ❌ |

The hal-index schema is optimized for the queries that matter most to security operations, while maintaining cache coherency across CDN deployments. The use of boolean properties (`supported`) instead of enum comparisons (`support-phase == "active"`) reduces query complexity and eliminates the need to know the vocabulary of valid enum values. Counterintuitively, the deeper HAL link structure (`._links.self.href`) is more ergonomic than flat URL properties (`.["releases.json"]`) because consistent snake_case naming enables dot notation throughout the query path.
