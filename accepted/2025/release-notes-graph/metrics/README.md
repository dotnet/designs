# Metrics

Query cost comparison tests from [richlander/release-graph-eval-results](https://github.com/richlander/release-graph-eval-results).

## Schema Comparison Summary

Three schema approaches are compared across 15 real-world query scenarios.

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
| T12 | Docker setup with SDK | **36 KB** | 45 KB | 25 KB P |
| T13 | TFM support check | 45 KB | 50 KB | **6 KB** |
| T14 | Package CVE check | **60 KB** | 65 KB | ✗ |
| T15 | Target platform versions | **11 KB** | 20 KB | ✗ |
| | **Winner** | **13** | **2** | **1** |

**Legend:** Smaller is better. **Bold** = winner. **P** = partial answer. **✗** = cannot answer.

### Key Findings

- **`llms.json`** optimizes for AI agent workflows with `_embedded.patches` providing direct links to manifests, security patches, and downloads without intermediate navigation.
- **`index.json`** provides complete graph traversal and works for all queries, but requires more fetches for common operations.
- **`releases-index.json`** lacks CVE severity, code fixes, breaking changes, what's new content, libc requirements, and target frameworks—making it unsuitable for upgrade planning and security analysis.

## Contents

| Document | Description |
|----------|-------------|
| [Index Discovery](index-discovery.md) | HAL discovery patterns—exploring `_links` and `_embedded` |
| [Easy Questions (Q1)](easy-questions.md) | Version queries answerable from embedded data |
| [CVE Stress Tests (Q2)](cve-stress-test.md) | Timeline navigation, severity filtering, code fixes |
| [Upgrade and What's New (Q3)](upgrade-whats-new.md) | Breaking changes and migration guidance |
| [Interacting with Environment (Q4)](interacting-with-environment.md) | Shell output parsing, libc checks, Docker setup |
| [Project File Analysis (Q5)](project-file-analysis.md) | TFM support, package CVEs, target platforms |
