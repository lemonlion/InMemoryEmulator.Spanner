# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.55] - 2026-07-10

### Fixed
- **NaN equality in BETWEEN/IN/CASE/NULLIF**: NaN values now correctly fail equality checks in BETWEEN bounds, IN list/subquery/UNNEST, simple CASE WHEN matching, and NULLIF comparison. Previously NaN matched itself in these constructs.
- **PARSE_TIMESTAMP/PARSE_DATE NULL format**: `PARSE_TIMESTAMP(NULL, '2023-01-01')` now correctly returns NULL instead of throwing a FormatException. Fixed `Convert.ToString(null)` returning empty string instead of null.
- **GENERATE_ARRAY with NaN arguments**: `GENERATE_ARRAY(NaN, 5, 1)` and similar now throw an error, matching Spanner's documented behavior.

### Added
- 7 new integration tests: NaN in BETWEEN/IN/CASE/NULLIF (4), PARSE_TIMESTAMP NULL format (1), GENERATE_ARRAY NaN (2).

## [1.0.54] - 2026-07-10

### Fixed
- **NaN comparison semantics**: All comparisons with NaN now return FALSE except `!=`/`<>` which return TRUE, per Spanner docs. Previously `NaN = NaN` returned TRUE and `NaN < x` returned TRUE.
- **BETWEEN three-valued logic**: `X BETWEEN NULL AND high` now correctly returns FALSE when `X > high` (previously returned NULL). Implements proper three-valued AND: `FALSE AND NULL = FALSE`.
- **FORMAT with NULL arguments**: `FORMAT('%d', NULL)` now produces `"NULL"` instead of `"0"`. All format specifiers now output the literal string "NULL" for NULL arguments.
- **MOD function FLOAT64 zero divisor**: `MOD(5.0, 0.0)` now throws an error instead of silently returning NaN.

### Added
- 10 new integration tests: NaN comparison (5), BETWEEN NULL three-valued logic (3), FORMAT NULL args (2).

## [1.0.53] - 2026-07-10

### Fixed
- **FLOAT64 division-by-zero now throws an error** instead of returning Infinity/NaN. Real Spanner errors on divide-by-zero; `IEEE_DIVIDE` is the function that returns Inf/NaN per IEEE 754 semantics.
- **MOD function with FLOAT64 zero divisor now throws an error** instead of returning NaN, matching the documented behavior ("An error is generated if Y is 0").
- Corrected 4 pre-existing tests that incorrectly expected Infinity/NaN for FLOAT64 division by zero.

### Added
- 6 new integration tests: division-by-zero error coverage for FLOAT64, INT64, and NUMERIC (both `/` and `%` operators).

## [1.0.52] - 2026-07-10

### Fixed
- **CASE WHEN with aggregates on empty table**: `SELECT CASE WHEN COUNT(*) > 0 THEN 'yes' ELSE 'no' END FROM empty_table` now correctly returns `'no'` instead of NULL. Non-aggregate expressions containing aggregates are now evaluated against the precomputed aggregate values even when the source table has zero rows.

### Added
- Integration test: 1 new test for CASE WHEN aggregate on empty table.

## [1.0.51] - 2026-07-10

### Fixed
- **GROUP BY ordinal resolution**: `GROUP BY 1` now correctly resolves integer ordinals to SELECT column positions, matching real Spanner behavior.
- **GROUP BY alias resolution**: `GROUP BY alias_name` now correctly resolves SELECT list aliases to the underlying expression.
- **RIGHT/FULL JOIN with empty left table**: When the left table has zero rows, unmatched right rows now correctly produce NULL values for left-side columns instead of missing columns.
- **ARRAY_AGG ORDER BY null handling**: `ARRAY_AGG(col ORDER BY col)` without explicit IGNORE/RESPECT NULLS now correctly includes NULL values (RESPECT NULLS is the default for ARRAY_AGG).
- **SAFE functions NUMERIC handling**: `SAFE_DIVIDE`, `SAFE_NEGATE`, `SAFE_ADD`, `SAFE_SUBTRACT`, and `SAFE_MULTIPLY` now correctly preserve NUMERIC (decimal) precision instead of converting to FLOAT64.
- **SAFE_DIVIDE type inference**: `SAFE_DIVIDE(NUMERIC, NUMERIC)` now correctly returns NUMERIC type instead of always returning FLOAT64.
- **SAFE_DIVIDE infinity detection**: Division producing infinity now returns NULL instead of the infinity value.

### Added
- Integration tests: 11 new tests (2 GROUP BY, 2 RIGHT/FULL JOIN, 1 ARRAY_AGG, 6 SAFE NUMERIC).

## [1.0.40] - 2026-05-19

### Fixed
- **NUMERIC arithmetic precision**: Operations between NUMERIC values (addition, subtraction, multiplication, division, modulo) now correctly preserve decimal precision instead of converting to FLOAT64.
- **DATE_ADD/DATE_SUB WEEK interval**: `INTERVAL n WEEK` now correctly adds/subtracts `n * 7` days instead of throwing "Unsupported INTERVAL part".
- **DATE_ADD/DATE_SUB QUARTER interval**: `INTERVAL n QUARTER` now correctly adds/subtracts `n * 3` months instead of throwing "Unsupported INTERVAL part".

### Added
- Integration tests: 7 new tests (3 NUMERIC arithmetic precision, 4 DATE_ADD/SUB WEEK/QUARTER).

## [1.0.39] - 2026-05-19

### Fixed
- **INFORMATION_SCHEMA.COLUMNS metadata**: `IS_GENERATED`, `GENERATION_EXPRESSION`, `IS_STORED`, and `COLUMN_DEFAULT` now correctly reflect generated columns and default expressions instead of returning hardcoded NULL/NEVER values.
- **GENERATE_ARRAY step=0 error**: `GENERATE_ARRAY(start, end, 0)` now throws an error instead of silently returning an empty array, matching real Spanner behavior.

### Added
- **GENERATE_ARRAY FLOAT64/NUMERIC support**: `GENERATE_ARRAY` now supports FLOAT64 and NUMERIC types in addition to INT64 (e.g., `GENERATE_ARRAY(0.0, 5.0, 2.5)`).
- Integration tests: 5 new tests (3 INFORMATION_SCHEMA metadata, 2 GENERATE_ARRAY edge cases).

## [1.0.38] - 2026-05-19

### Added
- **HAVING MAX/MIN clause**: Aggregate functions now support `HAVING MAX expr` and `HAVING MIN expr` to restrict computation to rows where the HAVING expression achieves its extremum. Works with all aggregates (ANY_VALUE, MAX, MIN, SUM, etc.).
- **LIKE ANY/ALL/SOME**: Quantified LIKE operator — `value LIKE ANY (patterns)` matches if any pattern matches, `LIKE ALL (patterns)` requires all patterns to match. `SOME` is a synonym for `ANY`. `NOT LIKE ANY/ALL` also supported.
- Integration tests: 10 new tests (4 HAVING MAX/MIN, 6 LIKE ANY/ALL/SOME).

## [1.0.37] - 2026-05-19

### Added
- **APPROX_COUNT_DISTINCT**: Aggregate function that returns the count of distinct non-null values (exact computation in-memory), matching Spanner's approximate semantics.
- **STRUCT/ARRAY equality comparisons**: STRUCT and ARRAY values can now be compared with `=` and `!=` operators using element-wise comparison.
- **FOR UPDATE clause**: Queries using `SELECT ... FOR UPDATE` now parse successfully (no-op in emulator — exclusive locks are not applicable in-memory).
- Integration tests: 12 new tests (3 APPROX_COUNT_DISTINCT, 6 STRUCT/ARRAY comparison, 3 FOR UPDATE).

## [1.0.36] - 2026-05-18

### Added
- **INSERT ... ON CONFLICT DO NOTHING**: Silently skip inserts that conflict with existing primary key rows instead of raising an error.
- **INSERT ... ON CONFLICT DO UPDATE SET ...**: Upsert semantics — update existing rows on primary key conflict using `EXCLUDED.column` to reference the attempted insert values.
- **ON CONFLICT ... WHERE**: Conditional upsert — only apply the DO UPDATE when an additional WHERE predicate (referencing both existing and EXCLUDED columns) evaluates to true.
- Integration tests: 5 new ON CONFLICT tests covering DO NOTHING (skip, insert new row, without conflict target), DO UPDATE, and DO UPDATE with WHERE clause.

## [1.0.35] - 2026-05-18

### Added
- **FK ON DELETE CASCADE**: Deleting a parent row now cascades deletes to child rows in tables with `FOREIGN KEY ... ON DELETE CASCADE` constraints (both DML DELETE and mutations).
- **FK ON DELETE NO ACTION enforcement**: Deleting a parent row with existing referencing child rows now returns gRPC `FAILED_PRECONDITION` when the FK uses `ON DELETE NO ACTION` (the default).
- **TIMESTAMP_TRUNC WEEK/ISOWEEK/QUARTER/ISOYEAR**: Added support for truncating timestamps to week (Sunday start), ISO week (Monday start), quarter, and ISO year boundaries.
- Integration tests: 6 new (FK cascade deletion, FK NO ACTION blocking, TIMESTAMP_TRUNC WEEK/ISOWEEK/QUARTER/ISOYEAR)

## [1.0.34] - 2026-05-18

### Fixed
- **SUBSTR position 0 / very negative**: `SUBSTR('apple', 0, 3)` now correctly returns `"app"` instead of `"ap"`. Position 0 and positions less than `-LENGTH(value)` are clamped to 1 per Spanner docs.
- **Bare UNION/INTERSECT/EXCEPT parsing**: `SELECT 1 UNION SELECT 2` (without `ALL` or `DISTINCT`) now parses correctly, defaulting to `DISTINCT` per Spanner specification.
- **LPAD/RPAD negative return_length**: `LPAD('abc', -1)` and `RPAD('abc', -1)` now throw an error instead of returning an empty string, matching Spanner behavior.
- **SPLIT NULL delimiter**: `SPLIT('a,b', CAST(NULL AS STRING))` now returns `NULL` instead of splitting on comma, following standard SQL NULL propagation.
- **DATE_ADD/DATE_SUB NULL amount**: `DATE_ADD(d, INTERVAL CAST(NULL AS INT64) DAY)` now returns `NULL` instead of throwing, following standard SQL NULL propagation.
- **Duplicate key → ALREADY_EXISTS**: DML INSERT and mutation INSERT of a duplicate primary key now return gRPC status `ALREADY_EXISTS` (6) instead of `INVALID_ARGUMENT` or `FAILED_PRECONDITION`.
- **Read-only transaction DML enforcement**: DML statements on read-only transactions now return `FAILED_PRECONDITION` on `ExecuteSql`, `ExecuteStreamingSql`, and `ExecuteBatchDml`.
- **ExecuteBatchDml duplicate key status**: Batch DML now returns `ALREADY_EXISTS` for duplicate key errors in the response status.

### Added
- Integration tests: 28 new (SUBSTR edge cases, LPAD/RPAD negative length, SPLIT NULL delimiter, DATE_ADD/SUB NULL amount, bare UNION/INTERSECT/EXCEPT, duplicate key status codes, read-only transaction DML enforcement)

## [1.0.22] - 2026-05-10

### Added
- **Observer callbacks**: `OnRequestReceived` and `OnResponseSent` real-time observer callbacks on `FakeSpannerService` and `FakeSpannerServer`. Fires for all 16 gRPC method overrides (13 unary + 3 streaming). Includes method name, protobuf request/response, duration, gRPC status code. Error-safe — observer exceptions are silently swallowed. Works alongside `FaultInjector` and `RequestLog`/`SqlLog`.
- Unit tests: 13 new (observer callback unit tests)
- Integration tests: 6 new (observer callback integration tests)

## [1.0.19] - 2025-07-04

### Added
- **QueryMode support**: PLAN, PROFILE, WITH_STATS, WITH_PLAN_AND_STATS modes return empty QueryPlan and/or QueryStats in ResultSetStats
- **PartitionQuery/PartitionRead transaction echoing**: PartitionQuery and PartitionRead now resolve transactions and echo them in the response
- **Row deletion policy runtime enforcement**: Expired rows (based on timestamp column + INTERVAL days) are automatically filtered from queries, reads, and DML operations
- **Proto/Enum type system**: CREATE/ALTER/DROP PROTO BUNDLE DDL, proto/enum column types with FQN (`examples.music.SingerInfo`), INFORMATION_SCHEMA shows FQN, proto_type_fqn propagated in gRPC Type metadata, base64 PROTO and string ENUM wire encoding, ARRAY<proto_type> support
- **State persistence**: RowDeletionPolicy and ProtoBundleTypes now exported/imported; ProtoTypeFqn preserved on columns
- Unit tests: 12 new (QueryMode + PartitionQuery/Read)
- Integration tests: 6 row deletion policy runtime, 14 proto/enum type system

## [1.0.18] - 2025-07-03

### Added
- **Compression functions**: ZSTD_COMPRESS, ZSTD_DECOMPRESS_TO_BYTES, ZSTD_DECOMPRESS_TO_STRING via ZstdSharp.Port (pure managed C#)
- **Change stream DDL**: CREATE/ALTER/DROP CHANGE STREAM with FOR ALL, FOR table(columns), OPTIONS support
- **Change stream INFORMATION_SCHEMA**: CHANGE_STREAMS, CHANGE_STREAM_TABLES, CHANGE_STREAM_COLUMNS, CHANGE_STREAM_OPTIONS virtual tables
- **Property graph DDL stubs**: CREATE [OR REPLACE] PROPERTY GRAPH and DROP PROPERTY GRAPH accepted as no-ops
- Unit tests: 6 compression, 12 change stream, 5 property graph
- Integration tests: 11 compression, 9 change stream, 4 property graph (all marked GoEmulatorUnsupported)

## [1.0.17] - 2025-07-03

### Fixed
- Mark CONTAINS_SUBSTR tests with `GoEmulatorUnsupported` trait (Go emulator does not support this function)
- Mark AdminApiIntegrationTests with `GoEmulatorUnsupported` trait (destructive operations cause ordering failures against Go emulator)
- Mark UuidAndTypeIntegrationTests with `GoEmulatorUnsupported` trait (Go emulator does not support UUID type)

## [1.0.16] - 2025-07-03

### Added
- **UUID type support**: DDL (`CREATE TABLE ... Id UUID NOT NULL`), CAST, TypeConverter, INFORMATION_SCHEMA
- **NEW_UUID function**: Returns a UUID value (alongside existing GENERATE_UUID)
- **Stale reads**: Proper handling of SingleUse transactions with staleness parameters (exact_staleness, max_staleness, min_read_timestamp, read_timestamp, strong). Creates real transaction state with ReadTimestamp metadata.
- **PROTO/ENUM type stubs**: TypeConverter handles TypeCode 13 (Proto) and 14 (Enum) without crashing; INFORMATION_SCHEMA correctly formats them
- Integration tests for stale reads (5 tests) and UUID/NEW_UUID (5 tests)
- Unit tests for UUID type and NEW_UUID (7 tests)

### Changed
- `SetTransactionMetadata` now also populates transaction metadata for SingleUse transactions (previously only Begin)
- `ResolveTransactionState` for SingleUse now creates a proper transaction instead of returning null

### Added
- Initial repository scaffolding: solution structure, projects, CI/CD, scripts
- AGENTS.md with TDD workflow, behavioral source requirements, test classification rules
- Wiki documentation: Home, Getting Started, Beta pages
