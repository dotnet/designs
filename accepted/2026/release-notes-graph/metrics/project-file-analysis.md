# Project File Analysis (Q5)

Query cost comparison for Q5-Project category tests from [release-graph-eval](https://github.com/dotnet/release-graph-eval).

See [overview.md](../overview.md) for design context, file characteristics, and link relation discovery.

## T13: TFM Support Check

**Query:** "Here's my project file with `<TargetFramework>net7.0</TargetFramework>`. Is my target framework still supported?"

| Schema | Files Required | Total Transfer |
|--------|----------------|----------------|
| llms-index | `llms.json` → `manifest.json` → `target-frameworks.json` per version | **~45 KB** |
| hal-index | `index.json` → `manifest.json` → `target-frameworks.json` per version | **~50 KB** |
| releases-index | `releases-index.json` (string parsing) | **6 KB** |

**llms-index:** Navigate via manifest to target-frameworks for each supported release:

```bash
# Check each supported release's target-frameworks.json for the TFM
# Get supported major releases first
curl -s "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/llms.json" | jq -r '.supported_major_releases[]'
# 10.0
# 9.0
# 8.0

# Then check if net7.0 is in any supported version's target-frameworks
curl -s "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/llms.json" | jq -r '._embedded.patches["10.0"]._links.manifest.href'
# https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/manifest.json

curl -s "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/manifest.json" | jq -r '._links["target-frameworks"].href'
# https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/target-frameworks.json

curl -s "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/target-frameworks.json" | jq -r '.frameworks[] | select(.tfm == "net7.0") | .tfm'
# (empty - net7.0 is NOT supported)
```

**Winner:** Tie (all equivalent with string parsing)

- All schemas can use string parsing for efficiency
- llms-index/hal-index shown here use authoritative TFM data for correctness

**Analysis:**

- **Completeness:** ✅ All schemas can determine support status.
- **Correctness vs efficiency:** llms-index uses authoritative TFM data via `target-frameworks` relation; releases-index uses string parsing.
- **Platform TFMs:** The `target-frameworks` approach correctly handles platform-specific TFMs (e.g., `net10.0-android`) that string parsing would miss.

---

## T14: Package CVE Check

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
# Start from the latest security month
curl -s "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/llms.json" | jq -r '._links["latest-security-month"].href'
# https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/index.json

# Get cve.json link and check for package vulnerabilities
curl -s "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/timeline/2025/10/index.json" | jq -r '._links["cve-json"].href'
# https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/10/cve.json

# Check packages in cve.json
curl -s "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/timeline/2025/10/cve.json" | jq -r '
  .packages | to_entries[] | select(.key | test("Microsoft.Build")) |
  "\(.key) | \(.value.cve_id) | vulnerable: \(.value.min_vulnerable)-\(.value.max_vulnerable) | fixed: \(.value.fixed)"'
# Microsoft.Build | CVE-2025-55247 | vulnerable: 17.10.0-17.10.29 | fixed: 17.10.46
# Microsoft.Build.Tasks.Core | CVE-2025-55247 | vulnerable: 17.8.0-17.8.29 | fixed: 17.8.43

# Get commit diff URLs for the CVE
curl -s "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/timeline/2025/10/cve.json" | jq -r '
  .cve_commits["CVE-2025-55247"][] as $ref | .commits[$ref].url'
# https://github.com/dotnet/msbuild/commit/aa888d3214e5adb503c48c3bad2bfc6c5aff638a.diff
# ...

# Walk to previous security month
curl -s "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/timeline/2025/10/index.json" | jq -r '._links["prev-security-month"].href'
# https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/2025/06/index.json
```

**Winner:** llms-index

- Direct `latest-security-month` link as starting point
- Package-level CVE data with commit diff URLs in cve.json
- Enables code review of security fixes without relying on nuget.org

**Analysis:**

- **Completeness:** ⚠️ releases-index does not provide package-level CVE data.
- **Version matching:** String comparison works for semver when patch versions have consistent digit counts.
- **Commit diffs:** The `cve_commits` and `commits` mappings provide direct links to fix diffs on GitHub.
- **Timeline navigation:** Uses `prev-security-month` links to efficiently walk security history.

---

## T15: Target Platform Versions

**Query:** "For a .NET MAUI app targeting `net10.0-android` and `net10.0-ios`, what platform SDK versions do these target?"

| Schema | Files Required | Total Transfer |
|--------|----------------|----------------|
| llms-index | `llms.json` → `10.0/manifest.json` → `target-frameworks.json` | **11 KB** |
| hal-index | `index.json` → `10.0/index.json` → `10.0/manifest.json` → `target-frameworks.json` | **20 KB** |
| releases-index | N/A | N/A (not available) |

**llms-index:** Navigate to target-frameworks.json via manifest link:

```bash
# Get target-frameworks.json for .NET 10 via manifest link
curl -s "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/llms.json" | jq -r '._embedded.patches["10.0"]._links.manifest.href'
# https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/manifest.json

curl -s "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/manifest.json" | jq -r '._links["target-frameworks"].href'
# https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/10.0/target-frameworks.json

# Get Android and iOS platform versions
curl -s "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/10.0/target-frameworks.json" | jq -r '.frameworks[] | select(.platform == "android" or .platform == "ios") | "\(.tfm) targets \(.platform_name) \(.platform_version)"'
# net10.0-android targets Android 36.0
# net10.0-ios targets iOS 18.7
```

**Winner:** llms-index

- Direct path via `manifest` link
- Platform-specific TFM data not available in releases-index

**Analysis:**

- **Completeness:** ❌ releases-index does not include target framework data.
- **MAUI context:** Understanding multi-platform project requirements.
- **Version mapping:** Each .NET version targets specific platform SDK versions (Android API level, iOS SDK version).
