# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
