# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
