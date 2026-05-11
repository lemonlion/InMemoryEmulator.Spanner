# Known Issues

## Behavioral Divergences from Real Cloud Spanner

This file tracks known behavioral differences between the in-memory emulator and real Google Cloud Spanner. Tests covering these divergences are marked with `[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]`.

| # | Feature | Expected (Real Spanner) | Actual (In-Memory) | Trait | Issue |
|---|---------|------------------------|---------------------|-------|-------|
| | | | | | |

*Updated as divergences are discovered during testing.*

## Cloud Spanner Free-Trial Instance Limitations

The CI Cloud Spanner tests run against a **free-trial instance** (`ci-test-instance` in project `spanner-emulator-ci`). The free-trial instance has undocumented limitations that cause certain tests to fail even though the features are documented as supported by production Cloud Spanner.

### Window/Analytic Functions

Window functions (ROW_NUMBER, RANK, DENSE_RANK, SUM OVER, AVG OVER, COUNT OVER, MIN OVER, MAX OVER, LAG, LEAD, FIRST_VALUE, LAST_VALUE, NTILE, etc.) return:

```
StatusCode="Unimplemented", Detail="Unsupported built-in function: <function_name>."
```

This contradicts the official documentation which lists these as fully supported:
- https://cloud.google.com/spanner/docs/reference/standard-sql/window-function-calls
- https://cloud.google.com/spanner/docs/reference/standard-sql/numbering_functions
- https://cloud.google.com/spanner/docs/reference/standard-sql/navigation_functions

The free-trial docs state: "A Spanner free trial instance supports Standard edition features, and Enterprise edition features" with no SQL function limitations listed.

**Workaround:** Tests are marked with `[Trait(TestTraits.Target, TestTraits.CloudSpannerUnsupported)]` and excluded from the Cloud Spanner CI workflow filter.

**Resolution:** Upgrade to a paid Cloud Spanner instance or create a new free-trial instance (if the 90-day period expired).

### Functions Not in Cloud Spanner (BigQuery-only)

The following functions exist in BigQuery's GoogleSQL but NOT in Cloud Spanner's GoogleSQL:
- `LEFT`, `RIGHT` — Use `SUBSTR()` instead
- `CHR`, `ASCII`, `UNICODE` — Use `CODE_POINTS_TO_STRING()` / `TO_CODE_POINTS()` instead
- `INITCAP` — No direct equivalent
- `APPROX_COUNT_DISTINCT` — Use `COUNT(DISTINCT ...)` instead
- `CONTAINS_SUBSTR` — Use `REGEXP_CONTAINS()` instead
- `ARRAY_SUM`, `ARRAY_AVG` — Use subquery with `UNNEST()` instead
- `GENERATE_TIMESTAMP_ARRAY` — No direct equivalent

The in-memory emulator correctly rejects these with an error matching real Cloud Spanner behavior.
Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/functions-all
