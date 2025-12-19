# LLM Acceptance Test Specification

This document defines acceptance tests for validating LLM behavior against the .NET Release Metadata Graph. It measures whether the graph's self-documenting protocol (`llms.txt` + HAL navigation) effectively guides LLM behavior across different models.

See [acceptance.md](acceptance.md) for data efficiency criteria and [metrics.md](metrics.md) for query cost comparisons.

## Purpose

The graph is designed to be self-bootstrapping: an LLM given only the entry point URL should discover navigation patterns and answer queries correctly. These tests validate that design goal across multiple LLMs.

**Key questions:**
1. Do LLMs discover and follow the `ai_note` → `llms.txt` guidance?
2. Do LLMs follow `_links` correctly, or do they fabricate URLs?
3. Do LLMs use embedded data efficiently, or over-fetch?
4. Do LLMs hallucinate data, or admit when information isn't available?
5. Does prose-first guidance (llms.txt) outperform JSON-first (llms.json)?

## Test Modes

Each test is run in two modes to compare self-discovery vs. explicit guidance.

| Mode | Entry Point | Preamble |
|------|-------------|----------|
| **A** | llms.json | `Use https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/llms.json to answer questions about .NET releases.` |
| **B** | llms.txt | `Use https://raw.githubusercontent.com/dotnet/core/release-index/llms.txt to answer questions about .NET releases.` |

Mode A tests self-discovery: the LLM must notice the `ai_note` field and choose to read llms.txt. Mode B provides explicit protocol documentation upfront.

## Scoring Rubric

Each test is scored on a 4-tier scale based on **outcomes**, not process.

| Tier | Name | Criteria | Points |
|------|------|----------|--------|
| **1** | Excellent | Correct answer, fetch count within expected range, no fabrication, no hallucination | 4 |
| **2** | Good | Correct answer, but over-fetched or took suboptimal navigation path | 3 |
| **3** | Assisted | Failed initially, succeeded with additional hints | 1 |
| **4** | Failure | Wrong answer, hallucinated data, couldn't navigate graph, or constructed URLs | 0 |

**Automatic Tier 4 (any mode):**
- Constructed a URL not obtained from `_links`
- Stated facts not present in fetched documents
- Answered without fetching when fetch was required
- Wrong answer on verifiable facts (versions, CVE IDs, dates)

## Diagnostic Metrics

These metrics are **observational, not scored**. They explain failure patterns and inform recommendations.

| Metric | What it tells you |
|--------|-------------------|
| `read_llms_txt` | Did the LLM discover and read the navigation guide? |
| `noticed_ai_note` | Did the LLM mention or act on the ai_note field? |
| `navigation_path` | What links did the LLM follow? |
| `stuck_at` | For failures: where did navigation break down? |

**How to use diagnostics:**

If an LLM fails a complex navigation test (T6-T10), check the diagnostics:

| read_llms_txt | Interpretation |
|---------------|----------------|
| No | **Discovery problem** — didn't notice ai_note or chose not to follow it |
| Yes | **Comprehension problem** — read guidance but couldn't apply it |

If an LLM succeeds without reading llms.txt:

| Test type | Interpretation |
|-----------|----------------|
| Simple (T1-T3) | **Correct behavior** — embedded data was sufficient |
| Complex (T6-T10) | **Capable navigator** — figured out HAL structure independently |

This distinction keeps scoring outcome-focused while preserving data that explains *why* models succeed or fail.

## Test Battery

### Test 1: Single-fetch Embedded Data

**Query:** "What is the latest patch for .NET 9?"

| Field | Value |
|-------|-------|
| Category | L1 (Patch Currency) |
| Expected Answer | 9.0.11 |
| Expected Fetches | 1 |
| Data Source | `llms.json` → `_embedded.latest_patches[]` where `release == "9.0"` → `version` |

**Evaluation:**
- Tier 1: Correct version, 1 fetch, extracted from embedded data
- Tier 2: Correct version, but fetched 9.0/index.json unnecessarily
- Tier 4: Wrong version, or answered without fetching

---

### Test 2: Time-bounded with Severity Filter

**Query:** "Were there any CRITICAL CVEs in .NET 8 in October 2025?"

| Field | Value |
|-------|-------|
| Category | C2 (CVE Deep Analysis) |
| Expected Answer | Yes—CVE-2025-55315 (CVSS 9.9, Security Feature Bypass in Kestrel) |
| Expected Fetches | 2 |
| Data Source | `llms.json` → `_links["latest-security-month"]` → `timeline/2025/10/index.json` → `_embedded.disclosures[]` filtered by `cvss_severity == "CRITICAL"` and `affected_releases` contains `"8.0"` |

**Evaluation:**
- Tier 1: Correct CVE ID and details, 2 fetches, used embedded disclosures
- Tier 2: Correct answer, but fetched cve.json unnecessarily
- Tier 4: Wrong CVE, fabricated details, or missed the CRITICAL CVE

---

### Test 3: Negative Result via Filtering

**Query:** "Were there any CVEs affecting .NET 10 in October 2025?"

| Field | Value |
|-------|-------|
| Category | C2 (CVE Deep Analysis) |
| Expected Answer | No—the October 2025 CVEs (CVE-2025-55247, CVE-2025-55248, CVE-2025-55315) affected .NET 8.0 and 9.0, not 10.0 |
| Expected Fetches | 2 |
| Data Source | `llms.json` → `timeline/2025/10/index.json` → `_embedded.disclosures[]` → check `affected_releases` for each |

**Evaluation:**
- Tier 1: Correctly states no CVEs for 10.0, explains why (affected_releases didn't include 10.0)
- Tier 2: Correct "no" answer without explaining the filtering
- Tier 4: Says yes, or fabricates CVEs for .NET 10

---

### Test 4: EOL Version Handling

**Query:** "What CVEs were fixed in .NET 6 this year?"

| Field | Value |
|-------|-------|
| Category | Edge case |
| Expected Answer | .NET 6 reached EOL on November 12, 2024 and is not in the supported releases. The graph covers supported versions only. |
| Expected Fetches | 1-2 |
| Data Source | `llms.json` → `releases[]` does not include "6.0"; optionally `index.json` → `_embedded.releases[]` shows 6.0 with `supported: false` |

**Evaluation:**
- Tier 1: Confirms 6.0 is EOL, explains graph scope, doesn't fabricate CVEs
- Tier 2: Says "not available" without explaining why
- Tier 4: Hallucinates CVE list from training data

**Follow-up (if Tier 1/2):** "I still need to know about .NET 6 CVEs"
- Tier 1: Admits data isn't in the graph, suggests alternative sources (e.g., NVD, GitHub advisories)
- Tier 4: Fabricates CVE data

---

### Test 5: Link-following Discipline

**Query:** "When does .NET 8 go EOL?"

| Field | Value |
|-------|-------|
| Category | L2 (Lifecycle) |
| Expected Answer | November 10, 2026 |
| Expected Fetches | 1-2 |
| Data Source | `llms.json` or `index.json` → `_embedded.releases[]` where `version == "8.0"` → `eol_date`; or follow `_links.self.href` to `8.0/index.json` → `eol_date` |

**Evaluation:**
- Tier 1: Correct date, navigated via `_links`
- Tier 2: Correct date, but unclear if link was followed
- Tier 4: Wrong date, or demonstrably constructed URL (e.g., string-interpolated "/8.0/index.json")

**Detection:** Check if fetched URLs exactly match `_links.href` values from prior fetches.

---

### Test 6: Breaking Changes Navigation

**Query:** "How many breaking changes are in .NET 10, grouped by category?"

| Field | Value |
|-------|-------|
| Category | B1 (Breaking Changes) |
| Expected Answer | Counts per category from `compatibility.json` → `categories` rollup |
| Expected Fetches | 3 |
| Data Source | `llms.json` → `_links["latest"]` → `10.0/index.json` → `_links["compatibility-json"]` → `compatibility.json` → `categories` |

**Evaluation:**
- Tier 1: Correct counts matching `categories` object, 3 fetches
- Tier 2: Correct counts, but manually aggregated from `breaks[]` instead of using rollup
- Tier 4: Fabricated categories or counts, couldn't find compatibility.json

---

### Test 7: Breaking Changes with Detail

**Query:** "What breaking changes in .NET 10 have HIGH impact?"

| Field | Value |
|-------|-------|
| Category | B2 (Breaking Changes) |
| Expected Answer | List of breaking changes where `impact == "high"`, with titles and categories |
| Expected Fetches | 3 |
| Data Source | `compatibility.json` → `breaks[]` filtered by `impact == "high"` |

**Evaluation:**
- Tier 1: Correct list with accurate titles, categories, and impact levels
- Tier 2: Correct filtering, minor omissions or formatting issues
- Tier 4: Wrong impact levels, fabricated breaking changes, or missed the `impact` field

---

### Test 8: Deep Manifest Navigation (OS Packages)

**Query:** "What packages do I need to install for .NET 10 on Ubuntu 24.04?"

| Field | Value |
|-------|-------|
| Category | X2 (Linux Deployment) |
| Expected Answer | Package list: libc6, libgcc-s1, ca-certificates, libssl3t64, libstdc++6, libicu74, tzdata, libgssapi-krb5-2 |
| Expected Fetches | 4 |
| Data Source | `llms.json` → `10.0/index.json` → `_links["release-manifest"]` → `manifest.json` → `_links["os-packages-json"]` → `os-packages.json` → filter by Ubuntu 24.04 |

**Evaluation:**
- Tier 1: Correct package list for Ubuntu 24.04 specifically, 4 fetches
- Tier 2: Mostly correct, minor package name errors
- Tier 4: Generic Linux answer from training data, didn't reach os-packages.json

---

### Test 9: Deep Manifest Navigation (libc Requirements)

**Query:** "What's the minimum glibc version for .NET 10 on x64?"

| Field | Value |
|-------|-------|
| Category | X3 (Linux Deployment) |
| Expected Answer | Specific version from `supported-os.json` → `libc[]` → filter by `name == "glibc"` and `architectures` contains `"x64"` → `version` |
| Expected Fetches | 4 |
| Data Source | `llms.json` → `10.0/index.json` → `manifest.json` → `_links["supported-os-json"]` → `supported-os.json` |

**Evaluation:**
- Tier 1: Correct glibc version from graph, 4 fetches
- Tier 2: Correct version, slightly inefficient navigation
- Tier 4: Guessed from training data, didn't fetch supported-os.json

---

### Test 10: Multi-step Time Traversal

**Query:** "List all CVEs fixed in .NET 8 in the last 6 months with their severity."

| Field | Value |
|-------|-------|
| Category | C2 (CVE Deep Analysis) |
| Expected Answer | CVE list with severities, covering ~6 months of security releases |
| Expected Fetches | 3-5 (llms.json + 2-4 security months via prev-security chain) |
| Data Source | `llms.json` → `latest-security-month` → walk `prev-security` links → filter `_embedded.disclosures[]` by `affected_releases` contains `"8.0"` |

**Evaluation:**
- Tier 1: Complete CVE list, used prev-security chain efficiently, stopped at correct boundary
- Tier 2: Correct CVEs, but walked prev instead of prev-security (fetched non-security months)
- Tier 4: Incomplete list, walked past date boundary, or fabricated CVEs

---

## Test Matrix

```
Tests 1-10 × 2 modes × N LLMs = 20N test runs
```

| Test | Mode A Expected | Mode B Expected |
|------|-----------------|-----------------|
| 1 | Tier 1-2 | Tier 1 |
| 2 | Tier 1-2 | Tier 1 |
| 3 | Tier 1-2 | Tier 1 |
| 4 | Tier 1-2 | Tier 1 |
| 5 | Tier 1-2 | Tier 1 |
| 6 | Tier 2 (deep nav) | Tier 1-2 |
| 7 | Tier 2 (deep nav) | Tier 1-2 |
| 8 | Tier 2-3 (very deep) | Tier 1-2 |
| 9 | Tier 2-3 (very deep) | Tier 1-2 |
| 10 | Tier 1-2 | Tier 1 |

Tests 8-9 are expected to be harder in Mode A because the path to manifest.json isn't documented in `ai_note`—the LLM must either read llms.txt or explore `_links` independently.

## Output Format

Each test run produces a structured result:

```json
{
  "test_id": "T1",
  "llm": "claude-sonnet-4-20250514",
  "mode": "A",
  "query": "What is the latest patch for .NET 9?",
  "answer": "The latest patch for .NET 9 is 9.0.11.",
  
  "scoring": {
    "correct": true,
    "expected_answer": "9.0.11",
    "fetch_count": 1,
    "expected_fetch_range": [1, 1],
    "fetch_count_ok": true,
    "url_fabrication": false,
    "hallucination": false,
    "tier": 1
  },
  
  "diagnostics": {
    "read_llms_txt": false,
    "fetched_urls": [
      "https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/llms.json"
    ],
    "navigation_path": "llms.json → _embedded.latest_patches[]",
    "notes": "Answered from embedded data without needing navigation guide"
  }
}
```

**Scoring fields (determine tier):**
- `correct`: Does the answer match expected?
- `fetch_count_ok`: Is fetch count within expected range?
- `url_fabrication`: Did any URL not come from `_links`?
- `hallucination`: Did LLM state facts not in fetched docs?
- `tier`: 1-4 based on rubric

**Diagnostic fields (explain patterns):**
- `read_llms_txt`: Did the LLM fetch llms.txt?
- `fetched_urls`: All URLs fetched, in order
- `navigation_path`: Summary of link traversal
- `notes`: Observations about behavior

## Aggregate Scoring

Per-LLM summary:

```json
{
  "llm": "claude-sonnet-4-20250514",
  "mode_a": {
    "total_points": 36,
    "max_points": 40,
    "percentage": 90,
    "tier_distribution": { "1": 8, "2": 1, "3": 1, "4": 0 },
    "avg_fetch_count": 2.3,
    "url_fabrication_rate": 0.0,
    "hallucination_rate": 0.0
  },
  "mode_b": {
    "total_points": 38,
    "max_points": 40,
    "percentage": 95,
    "tier_distribution": { "1": 9, "2": 1, "3": 0, "4": 0 },
    "avg_fetch_count": 2.1,
    "url_fabrication_rate": 0.0,
    "hallucination_rate": 0.0
  },
  "mode_differential": 5,
  "recommendation": "Either entry point works",
  
  "diagnostics": {
    "llms_txt_discovery_rate": 0.3,
    "discovery_correlated_with_success": false,
    "common_failure_point": null
  }
}
```

**Scored metrics:**
- `total_points`, `percentage`: Overall performance
- `tier_distribution`: How many tests at each tier
- `url_fabrication_rate`: Should be 0%
- `hallucination_rate`: Should be 0%

**Diagnostic metrics:**
- `llms_txt_discovery_rate`: Fraction of Mode A tests where LLM read llms.txt (observational)
- `discovery_correlated_with_success`: Did reading llms.txt predict better outcomes?
- `common_failure_point`: For Tier 3-4 results, where did navigation typically break?

The `mode_differential` (Mode B % - Mode A %) indicates whether explicit guidance improves outcomes. High values suggest the LLM benefits from prose-first; near-zero suggests it navigates HAL effectively on its own.

## Cross-LLM Comparison

After running all LLMs, produce a comparison table:

**Scored metrics:**

| LLM | Mode A % | Mode B % | Differential | Fabrication | Hallucination | Recommendation |
|-----|----------|----------|--------------|-------------|---------------|----------------|
| Claude Sonnet | 90% | 95% | +5 | 0% | 0% | Either |
| Claude Opus | 95% | 97% | +2 | 0% | 0% | Either |
| GPT-4o | 70% | 85% | +15 | 5% | 5% | llms.txt |
| Gemini Pro | 75% | 80% | +5 | 0% | 10% | llms.txt |

**Diagnostic metrics (Mode A only):**

| LLM | Discovery Rate | Discovery Correlated | Common Failure Point |
|-----|----------------|----------------------|----------------------|
| Claude Sonnet | 30% | No | — |
| Claude Opus | 50% | No | — |
| GPT-4o | 10% | Yes | Deep manifest navigation |
| Gemini Pro | 20% | Yes | Breaking changes link |

The diagnostic table helps explain *why* Mode A underperforms for certain LLMs:
- **Low discovery + high correlation**: LLM needs the guide but doesn't find it → recommend llms.txt
- **Low discovery + no correlation**: LLM succeeds without the guide → HAL structure is sufficient
- **High discovery + failures**: LLM reads guide but can't apply it → comprehension issue

## Test Harness Requirements

The test harness must:

1. **Intercept fetches**: Capture all URLs the LLM requests, with ordering
2. **Trace link sources**: For each fetch after the first, record whether the URL came from a `_links` field in a prior response
3. **Inject preamble**: Provide the appropriate preamble for Mode A or B
4. **Evaluate correctness**: Compare answer against expected values
5. **Detect hallucination**: Flag facts not present in any fetched document
6. **Produce structured output**: JSON format as specified above

## Running the Tests

Suggested procedure:

1. Run all 10 tests in Mode A for each LLM
2. Run all 10 tests in Mode B for each LLM
3. Score each run per the rubric
4. Produce per-LLM summaries
5. Produce cross-LLM comparison table
6. Update user documentation with recommendations

Tests should be run with low temperature (0.0-0.2) to reduce variance. Consider running each test 3 times and taking the median score for robustness.

## Updating Tests

When the graph data changes (new patches, CVEs, or versions):

1. Update expected answers in Tests 1-3, 10 (these reference current data)
2. Tests 4-9 are more stable (reference structure, not specific current values)
3. Re-run affected tests to validate

When graph structure changes:

1. Update expected fetch counts
2. Update data source paths
3. Consider adding new tests for new capabilities
