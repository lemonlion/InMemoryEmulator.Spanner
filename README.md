# Spanner.InMemoryEmulator

[![NuGet](https://img.shields.io/nuget/v/Spanner.InMemoryEmulator.svg)](https://www.nuget.org/packages/Spanner.InMemoryEmulator)
[![Build](https://github.com/lemonlion/Spanner.InMemoryEmulator/actions/workflows/build.yml/badge.svg)](https://github.com/lemonlion/Spanner.InMemoryEmulator/actions)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

An in-process fake for the Google Cloud Spanner .NET SDK (`Google.Cloud.Spanner.Data`). Zero Docker, instant startup, full SDK fidelity.

## Quick Start

### Install

```bash
dotnet add package Spanner.InMemoryEmulator
```

### Layer 1: Direct API (simplest)

```csharp
var db = new InMemorySpannerDatabase();
db.ExecuteDdl("CREATE TABLE Users (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
db.Insert("Users", new Dictionary<string, object?> { ["Id"] = 1L, ["Name"] = "Alice" });
var results = db.ExecuteQuery("SELECT * FROM Users WHERE Id = @id",
    new Dictionary<string, object?> { ["id"] = 1L });
```

### Layer 2: Fake gRPC Server (highest fidelity)

```csharp
using var server = new FakeSpannerServer();
server.Start();

// Seed schema via direct API
server.Database.ExecuteDdl("CREATE TABLE Users (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");

// Use the real SDK — everything goes through the fake gRPC service
using var connection = server.CreateConnection();
await connection.OpenAsync();

await connection.RunWithRetriableTransactionAsync(async tx =>
{
    var cmd = connection.CreateInsertCommand("Users");
    cmd.Parameters.Add("Id", SpannerDbType.Int64, 1L);
    cmd.Parameters.Add("Name", SpannerDbType.String, "Alice");
    cmd.Transaction = tx;
    await cmd.ExecuteNonQueryAsync();
});
```

### Layer 3: DI Integration (drop-in replacement)

```csharp
// In test ConfigureServices
services.AddInMemorySpanner(options =>
{
    options.OnDatabaseCreated = db =>
    {
        db.ExecuteDdl("CREATE TABLE Users (Id INT64 NOT NULL, Name STRING(MAX)) PRIMARY KEY (Id)");
    };
});
```

## Features

- **Session Management** — `CreateSession`, `BatchCreateSessions`, `DeleteSession`, multiplexed sessions
- **DDL** — `CREATE TABLE`, `DROP TABLE`, `ALTER TABLE`, `CREATE INDEX` (GoogleSQL)
- **Mutations** — Insert, Update, Delete, InsertOrUpdate, Replace via SDK mutation API
- **SQL Queries** — `SELECT` with WHERE, ORDER BY, LIMIT/OFFSET, JOINs, GROUP BY, aggregates
- **DML** — `INSERT`, `UPDATE`, `DELETE` SQL statements
- **Transactions** — Read-write, read-only, partitioned DML, retry-on-abort
- **Typed Columns** — INT64, FLOAT64, BOOL, STRING, BYTES, TIMESTAMP, DATE, NUMERIC, JSON, ARRAY
- **Indexes** — Secondary indexes with UNIQUE, NULL_FILTERED, STORING
- **Interleaved Tables** — Parent-child relationships with CASCADE/NO ACTION
- **DI Integration** — `services.AddInMemorySpanner()` drop-in replacement
- **Fault Injection** — Simulate ABORTED, UNAVAILABLE, DEADLINE_EXCEEDED for retry testing
- **Request Logging** — Record all gRPC requests and SQL statements for test assertions
- **State Persistence** — Export/import schema + data as JSON

## Layers Comparison

| Feature | InMemorySpannerDatabase | FakeSpannerServer | DI (`AddInMemorySpanner`) |
|---------|------------------------|-------------------|--------------------------|
| Complexity | Simplest | Medium | Simplest (for DI apps) |
| SDK fidelity | Direct API only | Full gRPC pipeline | Full gRPC pipeline |
| Production code changes | Different API | None | None |
| Fault injection | No | Yes | Yes |
| Request logging | No | Yes | Yes |

## Comparison with Docker Emulator

| | In-Memory Emulator | Docker Emulator |
|-|-------------------|-----------------|
| **Startup time** | Instant (~1ms) | 5-15 seconds |
| **Dependencies** | None | Docker Desktop |
| **Reliability** | Deterministic | Occasional crashes, port conflicts |
| **CI suitability** | Excellent | Requires Docker service |
| **Feature coverage** | Growing (see wiki) | Comprehensive |
| **Thread safety** | Full | N/A (separate process) |

## Supported SDK Versions

- `Google.Cloud.Spanner.Data` >= 5.0.0
- .NET 8.0+

## Documentation

See the [wiki](https://github.com/lemonlion/Spanner.InMemoryEmulator/wiki) for full documentation including:

- [Getting Started](https://github.com/lemonlion/Spanner.InMemoryEmulator/wiki/Getting-Started)
- [Choosing Your Approach](https://github.com/lemonlion/Spanner.InMemoryEmulator/wiki/Choosing-Your-Approach)
- [SQL Queries](https://github.com/lemonlion/Spanner.InMemoryEmulator/wiki/SQL-Queries)
- [Transactions](https://github.com/lemonlion/Spanner.InMemoryEmulator/wiki/Transactions)
- [Known Limitations](https://github.com/lemonlion/Spanner.InMemoryEmulator/wiki/Known-Limitations)

## License

[MIT](LICENSE)
