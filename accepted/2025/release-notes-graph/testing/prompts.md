# LLM Chat Test Prompts

This file contains prompts for manually running the LLM acceptance tests via chat interfaces. Each test produces structured JSON output for batch processing.

## Instructions

1. Start a new conversation with the target LLM
2. Paste the appropriate preamble (Mode A or Mode B)
3. Paste the test query
4. Save the JSON output to a file named `{llm}-{mode}-{test}.json`
5. After all tests, run the batch processor to generate the comparison table

## Preambles

### Mode A (JSON-first, self-discovery)

```
Use https://raw.githubusercontent.com/dotnet/core/release-index/release-notes/llms.json to answer questions about .NET releases.

After answering, provide:

1. A summary table:

| Field | Value |
|-------|-------|
| Answer | <your answer> |
| Fetch count | <number of URLs fetched> |

2. A JSON block with details:

{
  "fetched_urls": ["<url1>", "<url2>", ...],
  "data_sources": {
    "<fact>": "<url where you found it>"
  },
  "navigation_notes": "<how you navigated the graph>"
}
```

### Mode B (Prose-first, explicit guidance)

```
Use https://raw.githubusercontent.com/dotnet/core/release-index/llms.txt to answer questions about .NET releases.

After answering, provide:

1. A summary table:

| Field | Value |
|-------|-------|
| Answer | <your answer> |
| Fetch count | <number of URLs fetched> |

2. A JSON block with details:

{
  "fetched_urls": ["<url1>", "<url2>", ...],
  "data_sources": {
    "<fact>": "<url where you found it>"
  },
  "navigation_notes": "<how you navigated the graph>"
}
```

---

## Test Queries

### Test 1: Single-fetch Embedded Data

**Query:**
```
What is the latest patch for .NET 9?
```

**Expected Answer:** 9.0.11  
**Expected Fetches:** 1  
**Evaluation Notes:** Check if answer came from `_embedded.latest_patches[]` without additional fetches.

---

### Test 2: Time-bounded with Severity Filter

**Query:**
```
Were there any CRITICAL CVEs in .NET 8 in October 2025?
```

**Expected Answer:** Yes—CVE-2025-55315 (CVSS 9.9, Security Feature Bypass in Kestrel)  
**Expected Fetches:** 2  
**Evaluation Notes:** Should fetch llms.json then timeline/2025/10/index.json. Should NOT need cve.json.

---

### Test 3: Negative Result via Filtering

**Query:**
```
Were there any CVEs affecting .NET 10 in October 2025?
```

**Expected Answer:** No—the October 2025 CVEs affected .NET 8.0 and 9.0, not 10.0  
**Expected Fetches:** 2  
**Evaluation Notes:** Must correctly read `affected_releases` field. Common failure: assuming 10.0 was affected because it's listed in `releases`.

---

### Test 4: EOL Version Handling

**Query:**
```
What CVEs were fixed in .NET 6 this year?
```

**Expected Answer:** .NET 6 is EOL (November 12, 2024) and not in the supported releases graph.  
**Expected Fetches:** 1-2  
**Evaluation Notes:** Should check graph and report 6.0 not present. Tier 4 if it hallucinates CVEs.

**Follow-up (optional, if LLM gives Tier 1-2 response):**
```
I still need to know about .NET 6 CVEs.
```

**Expected:** Admits data isn't available, suggests alternatives (NVD, GitHub advisories). Tier 4 if it fabricates data.

---

### Test 5: Link-following Discipline

**Query:**
```
When does .NET 8 go EOL?
```

**Expected Answer:** November 10, 2026  
**Expected Fetches:** 1-2  
**Evaluation Notes:** Check `fetched_urls`—did URLs come from `_links.href` or were they constructed? Look for suspicious patterns like string interpolation.

---

### Test 6: Breaking Changes Navigation

**Query:**
```
How many breaking changes are in .NET 10, grouped by category?
```

**Expected Answer:** Counts per category from compatibility.json `categories` rollup  
**Expected Fetches:** 3  
**Evaluation Notes:** Path should be llms.json → 10.0/index.json → compatibility.json. Check if it used the pre-computed `categories` object.

---

### Test 7: Breaking Changes with Detail

**Query:**
```
What breaking changes in .NET 10 have HIGH impact?
```

**Expected Answer:** List filtered from compatibility.json `breaks[]` where `impact == "high"`  
**Expected Fetches:** 3  
**Evaluation Notes:** Verify the listed breaking changes actually have `impact: "high"` in the source.

---

### Test 8: Deep Manifest Navigation (OS Packages)

**Query:**
```
What packages do I need to install for .NET 10 on Ubuntu 24.04?
```

**Expected Answer:** libc6, libgcc-s1, ca-certificates, libssl3t64, libstdc++6, libicu74, tzdata, libgssapi-krb5-2  
**Expected Fetches:** 4  
**Evaluation Notes:** Must reach os-packages.json via manifest.json. Tier 4 if it gives generic Linux packages from training data.

---

### Test 9: Deep Manifest Navigation (libc Requirements)

**Query:**
```
What's the minimum glibc version for .NET 10 on x64?
```

**Expected Answer:** Version from supported-os.json `libc[]` array  
**Expected Fetches:** 4  
**Evaluation Notes:** Must reach supported-os.json. Tier 4 if it guesses from training data.

---

### Test 10: Multi-step Time Traversal

**Query:**
```
List all CVEs fixed in .NET 8 in the last 6 months with their severity.
```

**Expected Answer:** CVE list covering June-December 2025, filtered by `affected_releases` containing "8.0"  
**Expected Fetches:** 3-5 (entry point + 2-4 security months)  
**Evaluation Notes:** Should use `prev-security` links, not `prev`. Should stop at correct date boundary.

---

## Scoring Template

After running a test, fill in this template:

```json
{
  "test_id": "T1",
  "llm": "<model name>",
  "mode": "A",
  "timestamp": "<ISO timestamp>",
  
  "llm_output": {
    "answer": "<from summary table>",
    "fetch_count": 1,
    "fetched_urls": [],
    "data_sources": {},
    "navigation_notes": ""
  },
  
  "scoring": {
    "answer_correct": true,
    "expected_answer": "9.0.11",
    "expected_fetch_range": [1, 1],
    "fetch_count_ok": true,
    "url_fabrication_detected": false,
    "hallucination_detected": false,
    "tier": 1
  },
  
  "diagnostics": {
    "read_llms_txt": false,
    "navigation_path": "llms.json → _embedded.latest_patches[]",
    "notes": ""
  }
}
```

**Scoring fields (determine tier):**
- `answer_correct`: Does the answer match expected?
- `fetch_count_ok`: Is fetch count within expected range?
- `url_fabrication_detected`: Did any URL not come from `_links`?
- `hallucination_detected`: Did LLM state facts not in fetched docs?
- `tier`: 1-4 based on rubric

**Diagnostic fields (explain patterns):**
- `read_llms_txt`: Infer from `fetched_urls`—set to `true` if llms.txt appears
- `navigation_path`: Summary of how the LLM traversed the graph
- `notes`: Any observations about behavior

**Evaluation fields you fill in:**
- `answer_correct`: Does the answer match expected?
- `fetch_count_ok`: Is fetch count within expected range?
- `url_fabrication_detected`: Did any URL not come from `_links`?
- `hallucination_detected`: Did LLM state facts not in fetched docs?
- `tier`: 1-4 based on rubric
- `notes`: Any observations

---

## File Naming Convention

Save results as:
```
results/{llm}/{mode}/T{n}.json
```

Examples:
```
results/claude-sonnet/A/T1.json
results/claude-sonnet/B/T1.json
results/gpt-4o/A/T1.json
results/gpt-4o/B/T1.json
```

---

## Batch Processing

After collecting all results, the processor reads all JSON files and produces:

1. Per-LLM summary (points, tier distribution, rates)
2. Cross-LLM comparison table
3. Recommendations per LLM

Processor input: `results/` directory  
Processor output: `summary.json`, `comparison.md`
