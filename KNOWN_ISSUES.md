# Known Issues

## Behavioral Divergences from Real Cloud Spanner

This file tracks known behavioral differences between the in-memory emulator and real Google Cloud Spanner. Tests covering these divergences are marked with `[Trait(TestTraits.Target, TestTraits.InMemoryOnly)]`.

| # | Feature | Expected (Real Spanner) | Actual (In-Memory) | Trait | Issue |
|---|---------|------------------------|---------------------|-------|-------|
| | | | | | |

*Updated as divergences are discovered during testing.*

## Cloud Spanner Free-Trial Instance Limitations

The CI Cloud Spanner tests run against a **free-trial instance** (`ci-test-instance` in project `spanner-emulator-ci`, created with `--instance-type=free-instance`). The free-trial instance has undocumented limitations that cause certain tests to fail even though the features are documented as supported by production Cloud Spanner.

### Root Cause Analysis

**The free-trial instance appears to be backed by the Go Spanner Emulator internally.**

Evidence:
1. The error message `"Unsupported built-in function: X"` with gRPC status `UNIMPLEMENTED` matches **exactly** the Go emulator's `error::UnsupportedFunction()` in [`common/errors.cc`](https://github.com/GoogleCloudPlatform/cloud-spanner-emulator/blob/master/common/errors.cc)
2. Real Cloud Spanner uses different error formats (e.g., `"Function not found: X [at line:col]"` with `INVALID_ARGUMENT`)
3. The failing functions (analytic/window functions, LEFT, RIGHT, CHR) are known Go emulator gaps
4. Regular aggregates (SUM, AVG, COUNT without OVER) work — consistent with Go emulator capabilities
5. 4591/5002 tests pass — consistent with Go emulator being highly capable for standard operations
6. Free-trial docs state it's "for evaluation purposes" and "not meant for ongoing testing and development"

Google likely uses the Go emulator infrastructure for free-trial instances to avoid the cost of provisioning real Spanner resources for free 90-day trials. This is undocumented behavior.

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

**Resolution:** Upgrade to a paid Cloud Spanner instance (minimum 100 processing units) to get access to the full production Spanner query engine.

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
