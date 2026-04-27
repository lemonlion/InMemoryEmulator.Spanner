# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
