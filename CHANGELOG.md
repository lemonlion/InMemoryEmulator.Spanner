# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.68] - 2026-07-10

### Fixed
- **ARRAY_IS_DISTINCT incorrect comparison**: Previously compared elements using string representation, causing false duplicates with different types. Now uses proper value comparison semantics matching Spanner's DISTINCT behavior.
- **MOD NUMERIC precision loss**: `MOD` with NUMERIC arguments now preserves decimal precision instead of converting to FLOAT64.

## [1.0.67] - 2026-07-10

### Fixed
- **LPAD/RPAD(BYTES) corruption**: `LPAD` and `RPAD` now correctly pad byte arrays instead of converting them to "System.Byte[]" strings.
- **SPLIT(BYTES) corruption**: `SPLIT` now correctly splits BYTES values and returns `ARRAY<BYTES>` instead of converting to string.

## [1.0.66] - 2026-07-10

### Fixed
- **STRPOS(BYTES) crash**: `STRPOS` now correctly finds byte subsequences in BYTES values instead of throwing an InvalidCastException.
- **TRIM/LTRIM/RTRIM(BYTES) crash**: `TRIM`, `LTRIM`, and `RTRIM` now correctly trim bytes from BYTES values instead of throwing an InvalidCastException.

## [1.0.65] - 2026-07-10

### Fixed
- **REVERSE(BYTES) silent wrong result**: `REVERSE` on BYTES values now correctly returns the reversed byte sequence instead of incorrectly returning the byte length.
- **REPLACE(BYTES) crash**: `REPLACE` on BYTES values now correctly replaces byte subsequences instead of throwing an InvalidCastException.

### Added
- **LAST_DAY function**: Implements `LAST_DAY(date[, date_part])` which returns the last day of the period containing the date. Supports YEAR, QUARTER, MONTH, WEEK, and ISOWEEK date parts.

## [1.0.64] - 2026-07-10

### Fixed
- **LENGTH(BYTES) crash**: `LENGTH`, `CHAR_LENGTH`, and `CHARACTER_LENGTH` now correctly return the byte count for BYTES inputs instead of throwing an InvalidCastException.
- **CONCAT(BYTES) corruption**: `CONCAT` with BYTES arguments now properly concatenates byte arrays instead of converting to "System.Byte[]" strings.
- **SUBSTR(BYTES) crash**: `SUBSTR`/`SUBSTRING` now correctly extracts subsequences from BYTES values instead of throwing an InvalidCastException.
- **BYTES || BYTES operator**: The concatenation operator (`||`) now correctly concatenates BYTES values instead of producing "System.Byte[]System.Byte[]" strings.

## [1.0.63] - 2026-07-10

### Fixed
- **Partitioned DML INSERT rejection**: INSERT statements are now correctly rejected in Partitioned DML transactions with `InvalidArgument` status code. Only UPDATE and DELETE are supported per the Spanner specification.
- **FORMAT_TIMESTAMP %E9S crash**: The `%E9S` format specifier (nanosecond-precision seconds) no longer crashes with a FormatException. Limited to 7 fractional digits (maximum .NET precision).
- **FORMAT_TIMESTAMP %E4Y support**: The `%E4Y` format specifier (4-digit year) is now correctly handled.
- **TIMESTAMP_ADD overflow**: Adding extreme values to timestamps now returns a proper error instead of an unhandled exception.

## [1.0.62] - 2026-07-10

### Fixed
- **PARSE_TIMESTAMP timezone parameter**: The optional third timezone parameter is now respected. When parsing a timestamp string without timezone info, the 3rd parameter specifies which timezone to assume, and the result is converted to UTC.
- **CAST(NaN/Infinity AS INT64)**: Now correctly throws an error. Previously produced undefined C# behavior (garbage values). `SAFE_CAST` correctly returns NULL.
- **CAST(large FLOAT64 AS INT64)**: Values exceeding INT64 range (e.g., 1e19) now correctly throw an error instead of producing overflow garbage.
- **Uncaught exceptions in ExecuteSql/ExecuteStreamingSql**: FormatException, OverflowException, and other unexpected exceptions are now caught and returned as proper gRPC INVALID_ARGUMENT errors instead of raw "Unknown" server errors. This fixes `CAST('' AS INT64)`, `CAST('abc' AS FLOAT64)`, etc.

### Added
- 7 new integration tests: PARSE_TIMESTAMP timezone (1), CAST NaN/Inf/overflow to INT64 (4), uncaught exception handling (2).

## [1.0.61] - 2026-07-10

### Fixed
- **TIMESTAMP_ADD/TIMESTAMP_SUB reject WEEK and QUARTER**: These date parts are now correctly rejected with an error. Per docs, only NANOSECOND through DAY are supported for timestamp arithmetic.
- **DATE_ADD/DATE_SUB reject sub-day parts**: NANOSECOND, MICROSECOND, MILLISECOND, SECOND, MINUTE, HOUR are now correctly rejected. Per docs, only DAY, WEEK, MONTH, QUARTER, YEAR are supported for date arithmetic.
- **JSON_VALUE returns NULL for non-scalar**: `JSON_VALUE` now correctly returns NULL when the path resolves to a JSON object or array, instead of returning the raw JSON text. Per docs: "Extracts a JSON scalar value."
- **FORMAT_TIMESTAMP timezone parameter**: The optional third timezone parameter is now respected. `FORMAT_TIMESTAMP('%H:%M', ts, 'America/Los_Angeles')` correctly converts to the target timezone before formatting.
- **STARTS_WITH/ENDS_WITH BYTES support**: These functions now correctly handle `BYTES` type inputs with byte-level prefix/suffix comparison, instead of throwing an InvalidCastException.

### Added
- 12 new integration tests: TIMESTAMP_ADD/SUB part validation (3), DATE_ADD/SUB part validation (3), JSON_VALUE non-scalar (2), FORMAT_TIMESTAMP timezone (2), STARTS_WITH/ENDS_WITH BYTES (3).

## [1.0.60] - 2026-07-10

### Fixed
- **EXP overflow**: `EXP(x)` now correctly throws an error when the result overflows to Infinity (e.g., `EXP(710)`). Previously returned Infinity silently. `EXP(-Infinity)` continues to correctly return 0.
- **REGEXP_REPLACE backreferences**: Backreference syntax (`\1`, `\2`, etc.) in replacement strings now works correctly. Previously, Spanner-style `\1` backreferences were not converted to .NET's `$1` format, causing replacements to be treated as literal text.

### Added
- 3 new integration tests: EXP overflow (2), REGEXP_REPLACE backreferences (1).

## [1.0.59] - 2026-07-10

### Fixed
- **SAFE_ADD/SAFE_SUBTRACT/SAFE_MULTIPLY NaN passthrough**: These functions now correctly return NaN when any input is NaN, instead of NULL. Per docs: "All mathematical functions return NaN if any of the arguments is NaN."
- **SAFE_ADD/SAFE_SUBTRACT/SAFE_MULTIPLY Infinity handling**: `SAFE_ADD(Inf, 1)` now correctly returns Infinity (passthrough, not overflow). Only finite+finite→Inf is treated as overflow (returns NULL). Indeterminate forms like `Inf+(-Inf)` correctly return NULL.
- **SIGN(NaN)**: Now correctly returns NaN instead of throwing ArithmeticException.
- **GREATEST/LEAST with NaN**: Now correctly returns NaN if any floating-point argument is NaN, per docs.
- **POW error cases**: `POW(negative, non-integer)` and `POW(0, negative)` now correctly throw errors instead of returning NaN/Infinity silently.

### Added
- 13 new integration tests: SAFE_ADD/SUBTRACT/MULTIPLY NaN and Infinity (8), SIGN NaN (1), GREATEST/LEAST NaN (2), POW error cases (2).

## [1.0.58] - 2026-07-10

### Fixed
- **SAFE_DIVIDE NaN passthrough**: `SAFE_DIVIDE(NaN, x)` and `SAFE_DIVIDE(x, NaN)` now correctly return NaN instead of NULL. Per docs: "All mathematical functions return NaN if any of the arguments is NaN."
- **SAFE_DIVIDE Infinity handling**: `SAFE_DIVIDE(Inf, finite)` now correctly returns Infinity (input was already Inf, not an overflow). `SAFE_DIVIDE(Inf, Inf)` correctly returns NULL (indeterminate form).
- **REPEAT negative count**: `REPEAT('abc', -1)` now correctly throws an error. Per docs: "This function returns an error if the repetitions value is negative." Previously returned empty string.
- **IGNORE NULLS for window navigation functions**: `FIRST_VALUE`, `LAST_VALUE`, `LAG`, `LEAD`, and `NTH_VALUE` now properly respect the `IGNORE NULLS` clause, skipping NULL values when counting offsets or searching frames.

### Added
- 9 new integration tests: SAFE_DIVIDE NaN/Infinity (4), REPEAT negative/zero (2), IGNORE NULLS for FIRST_VALUE/LAST_VALUE (2), ARRAY_INCLUDES type coercion (1).

## [1.0.57] - 2026-07-10

### Added
- **ORDER BY NULLS FIRST/LAST**: Support for explicit null ordering in ORDER BY clauses (`ORDER BY col ASC NULLS LAST`, `ORDER BY col DESC NULLS FIRST`). Overrides Spanner's default behavior where NULLs are the minimum value.
- **SELECT * EXCEPT**: Support for `SELECT * EXCEPT (col1, col2)` to exclude specific columns from a star expansion.
- **SELECT * REPLACE**: Support for `SELECT * REPLACE (expr AS col)` to substitute expressions for specific columns in a star expansion.
- 4 new integration tests: ORDER BY NULLS FIRST/LAST (2), SELECT * EXCEPT (1), SELECT * REPLACE (1).

## [1.0.56] - 2026-07-10

### Fixed
- **TIMESTAMP_DIFF rejects MONTH/YEAR**: `TIMESTAMP_DIFF` now correctly throws an error for MONTH and YEAR date parts, which are only valid for `DATE_DIFF`. Per docs, TIMESTAMP_DIFF only supports NANOSECOND through DAY.
- **DATE_DIFF proper boundary counting**: `DATE_DIFF` now has its own implementation with correct boundary-counting semantics for WEEK (Sunday boundaries), ISOWEEK (Monday boundaries), QUARTER, and ISOYEAR date parts. Previously delegated to TIMESTAMP_DIFF which lacked these parts.
- **CAST hex strings to INT64**: `CAST('0x1A' AS INT64)` now correctly parses hexadecimal string literals to integers.

### Added
- 11 new integration tests: TIMESTAMP_DIFF MONTH/YEAR rejection (2), DATE_DIFF boundary counting for WEEK/ISOWEEK/QUARTER/ISOYEAR (6), CAST hex to INT64 (3).

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
