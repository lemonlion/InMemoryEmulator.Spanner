# Known Issues

## Behavioral Divergences from Real Cloud Spanner

This file tracks known behavioral differences between the in-memory emulator and real Google Cloud Spanner. Tests covering these divergences are marked with `[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]`.

| # | Feature | Expected (Real Spanner) | Actual (In-Memory) | Trait | Issue |
|---|---------|------------------------|---------------------|-------|-------|
| | | | | | |

*Updated as divergences are discovered during testing.*

## Cloud Spanner CI Test Failures

The CI Cloud Spanner tests run against a **free-trial instance** (`ci-test-instance` in project `spanner-emulator-ci`, created with `--instance-type=free-instance`). Certain tests fail with `"Unsupported built-in function: X"` (gRPC status `UNIMPLEMENTED`).

### Root Cause Analysis

**The failing functions do NOT exist in Cloud Spanner's GoogleSQL dialect.** They are BigQuery-only functions that our in-memory emulator incorrectly implements. The Cloud Spanner errors are CORRECT behavior.

Evidence:
1. The Spanner all-functions reference does NOT list ROW_NUMBER, RANK, DENSE_RANK, NTILE, CUME_DIST, PERCENT_RANK, LAG, LEAD, FIRST_VALUE, LAST_VALUE — Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/functions-all
2. BigQuery's equivalent page DOES list all of these — Ref: https://docs.cloud.google.com/bigquery/docs/reference/standard-sql/numbering_functions
3. ALL Spanner documentation URLs for window/analytic functions return 404 Not Found:
   - `/spanner/docs/reference/standard-sql/numbering_functions` → 404
   - `/spanner/docs/reference/standard-sql/navigation_functions` → 404
   - `/spanner/docs/reference/standard-sql/window-function-calls` → 404
   - `/spanner/docs/reference/standard-sql/analytic-functions` → 404
4. Spanner's query syntax page has NO WINDOW clause and NO OVER keyword documentation — Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/query-syntax
5. Spanner's "Function calls" page has NO mention of window/analytic function call syntax — Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/functions-reference
6. Spanner's "Aggregate function calls" page shows NO OVER clause syntax — Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate-function-calls
7. The SQL reference sidebar navigation lists: Function calls, Aggregate function calls, Operators, Conditional expressions, Subqueries, Query syntax — but NO "Window function calls" or "Analytic function calls" section
8. The string functions reference does NOT list LEFT, RIGHT, or CHR — Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions

**Conclusion:** This is NOT a free-trial limitation or feature-gating issue. The free-trial instance correctly reports that these functions are unsupported because they genuinely do not exist in Cloud Spanner. The in-memory emulator over-implements by supporting BigQuery-only functions.

> **Note:** Spanner does have the `IS_FIRST` window function (listed in all-functions), suggesting limited/newer window function support is being added to Spanner, but the traditional window functions from BigQuery are not (yet) available.

### Functions Not in Cloud Spanner (BigQuery-only)

The following functions exist in BigQuery's GoogleSQL but NOT in Cloud Spanner's GoogleSQL dialect. Our in-memory emulator supports some of these for convenience, but they are not real Spanner features.

#### Window/Analytic Functions (BigQuery-only)

Window functions and the `OVER` clause do not exist in Cloud Spanner's GoogleSQL (with the exception of the newer `IS_FIRST` function):
- `ROW_NUMBER() OVER(...)` — No equivalent in Spanner
- `RANK() OVER(...)` — No equivalent in Spanner
- `DENSE_RANK() OVER(...)` — No equivalent in Spanner
- `NTILE() OVER(...)` — No equivalent in Spanner
- `CUME_DIST() OVER(...)` — No equivalent in Spanner
- `PERCENT_RANK() OVER(...)` — No equivalent in Spanner
- `LAG() OVER(...)` — No equivalent in Spanner
- `LEAD() OVER(...)` — No equivalent in Spanner
- `FIRST_VALUE() OVER(...)` — No equivalent in Spanner
- `LAST_VALUE() OVER(...)` — No equivalent in Spanner
- `SUM() OVER(...)`, `AVG() OVER(...)`, `COUNT() OVER(...)`, `MIN() OVER(...)`, `MAX() OVER(...)` — Aggregate-as-window not supported

Ref: https://docs.cloud.google.com/bigquery/docs/reference/standard-sql/numbering_functions (BigQuery)

#### String Functions (Not in Spanner's GoogleSQL)

- `LEFT`, `RIGHT` — Use `SUBSTR()` instead
- `CHR` — Use `CODE_POINTS_TO_STRING()` instead
- `ASCII`, `UNICODE` — Use `TO_CODE_POINTS()` instead
- `INITCAP` — No direct equivalent

Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions

#### Other Functions (BigQuery-only)

- `APPROX_COUNT_DISTINCT` — Use `COUNT(DISTINCT ...)` instead
- `CONTAINS_SUBSTR` — Use `REGEXP_CONTAINS()` instead
- `ARRAY_SUM`, `ARRAY_AVG` — Use subquery with `UNNEST()` instead
- `GENERATE_TIMESTAMP_ARRAY` — No direct equivalent

Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/functions-all

### Test Marking Strategy

Tests that use BigQuery-only functions are marked with `[Trait(TestTraits.Target, TestTraits.CloudSpannerUnsupported)]` and excluded from the Cloud Spanner CI workflow filter. This is because our emulator intentionally supports these functions for DX convenience even though real Spanner does not.
