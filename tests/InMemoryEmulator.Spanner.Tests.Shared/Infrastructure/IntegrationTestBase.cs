using System.Text.RegularExpressions;
using Google.Cloud.Spanner.Data;
using Google.Cloud.Spanner.V1;
using InMemoryEmulator.Spanner.Tests.Shared.Traits;

namespace InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;

/// <summary>
/// Base class for integration tests that need to work against both the in-memory
/// emulator and the official Google Cloud Spanner Go emulator.
/// Provides helper methods for DDL, data seeding, and DML that abstract away
/// the different setup paths required by each target.
/// </summary>
public abstract class IntegrationTestBase
{
	protected readonly ITestDatabaseFixture Fixture;
	private readonly EmulatorSession _session;

	protected IntegrationTestBase(EmulatorSession session)
	{
		_session = session;
		Fixture = TestFixtureFactory.Create(session);
	}

	// ─── DDL ───

	// Matches: CREATE TABLE tableName or CREATE TABLE IF NOT EXISTS tableName
	private static readonly Regex CreateTableRegex = new(
		@"^\s*CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?(?:`?(\w+)`?|(\w+))",
		RegexOptions.IgnoreCase | RegexOptions.Compiled);

	// Matches: CREATE (?:UNIQUE|NULL_FILTERED)? INDEX indexName
	private static readonly Regex CreateIndexRegex = new(
		@"^\s*CREATE\s+(?:UNIQUE\s+|NULL_FILTERED\s+)?INDEX\s+(?:IF\s+NOT\s+EXISTS\s+)?(?:`?(\w+)`?|(\w+))",
		RegexOptions.IgnoreCase | RegexOptions.Compiled);

	/// <summary>
	/// Executes DDL statements (CREATE TABLE, CREATE INDEX, etc.).
	/// InMemory: uses direct database API.
	/// Emulator/Cloud: uses SpannerConnection.CreateDdlCommand (Database Admin API).
	/// Deduplicates CREATE TABLE/INDEX calls to avoid hitting DDL rate limits.
	/// </summary>
	protected async Task ExecuteDdlAsync(params string[] statements)
	{
		if (Fixture.Database != null)
		{
			foreach (var stmt in statements)
				Fixture.Database.ExecuteDdl(stmt);
			return;
		}

		// Go emulator / Cloud: DDL goes through the Database Admin gRPC API.
		// DDL is rate-limited (~5 ops/min on Cloud Spanner), so skip duplicates.
		var statementsToExecute = new List<string>();
		foreach (var stmt in statements)
		{
			var key = GetDdlDeduplicationKey(stmt);
			if (key != null && !_session.ExecutedDdl.TryAdd(key, 0))
			{
				// Already executed this DDL statement in this session — skip it.
				continue;
			}
			statementsToExecute.Add(stmt);
		}

		if (statementsToExecute.Count == 0)
			return;

		// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.admin.database.v1#updatedatabaseddlrequest
		//   Batch all DDL statements into a single UpdateDatabaseDdl RPC call.
		//   This is significantly faster than individual calls, especially on Cloud Spanner
		//   where each UpdateDatabaseDdl is a long-running operation.
		using var connection = Fixture.CreateConnection();
		await connection.OpenAsync();
		var cmd = connection.CreateDdlCommand(
			statementsToExecute[0],
			statementsToExecute.Skip(1).ToArray());
		await cmd.ExecuteNonQueryAsync();
	}

	/// <summary>
	/// Extracts a deduplication key from a DDL statement.
	/// Returns null for statements that should never be deduplicated (ALTER, DROP).
	/// </summary>
	private static string? GetDdlDeduplicationKey(string statement)
	{
		var tableMatch = CreateTableRegex.Match(statement);
		if (tableMatch.Success)
		{
			var name = tableMatch.Groups[1].Success ? tableMatch.Groups[1].Value : tableMatch.Groups[2].Value;
			return $"TABLE:{name}";
		}

		var indexMatch = CreateIndexRegex.Match(statement);
		if (indexMatch.Success)
		{
			var name = indexMatch.Groups[1].Success ? indexMatch.Groups[1].Value : indexMatch.Groups[2].Value;
			return $"INDEX:{name}";
		}

		// ALTER TABLE, DROP TABLE, etc. — always execute
		return null;
	}

	// ─── Data Seeding ───

	/// <summary>
	/// Inserts a single row into a table.
	/// InMemory: uses direct database API.
	/// Emulator/Cloud: uses SpannerConnection.CreateInsertCommand.
	/// </summary>
	protected async Task InsertAsync(string table, Dictionary<string, object?> columns)
	{
		if (Fixture.Database != null)
		{
			Fixture.Database.Insert(table, columns);
			return;
		}

		using var connection = Fixture.CreateConnection();
		await connection.OpenAsync();
		using var cmd = connection.CreateInsertCommand(table);
		foreach (var col in columns)
		{
			if (col.Value == null || col.Value is DBNull)
			{
				// For null values, we skip the column — it defaults to NULL for nullable columns.
				continue;
			}
			cmd.Parameters.Add(col.Key, InferSpannerDbType(col.Value), col.Value);
		}
		await cmd.ExecuteNonQueryAsync();
	}

	/// <summary>
	/// Inserts multiple rows into a table.
	/// </summary>
	protected async Task InsertAsync(string table, params Dictionary<string, object?>[] rows)
	{
		foreach (var row in rows)
		{
			await InsertAsync(table, row);
		}
	}

	/// <summary>
	/// Upserts a row (insert or update if exists).
	/// InMemory: uses direct database API (InsertOrUpdate).
	/// Emulator/Cloud: uses SpannerConnection.CreateInsertOrUpdateCommand.
	/// </summary>
	protected async Task InsertOrUpdateAsync(string table, Dictionary<string, object?> columns)
	{
		if (Fixture.Database != null)
		{
			Fixture.Database.InsertOrUpdate(table, columns);
			return;
		}

		using var connection = Fixture.CreateConnection();
		await connection.OpenAsync();
		using var cmd = connection.CreateInsertOrUpdateCommand(table);
		foreach (var col in columns)
		{
			if (col.Value == null) continue;
			cmd.Parameters.Add(col.Key, InferSpannerDbType(col.Value), col.Value);
		}
		await cmd.ExecuteNonQueryAsync();
	}

	// ─── DML ───

	/// <summary>
	/// Executes a DML statement (INSERT, UPDATE, DELETE) and returns the affected row count.
	/// Always goes through the SDK/gRPC pipeline.
	/// </summary>
	protected async Task<long> ExecuteDmlAsync(string sql,
		params (string name, SpannerDbType type, object? value)[] parameters)
	{
		using var connection = Fixture.CreateConnection();
		await connection.OpenAsync();
		using var txn = await connection.BeginTransactionAsync();
		using var cmd = connection.CreateDmlCommand(sql);
		cmd.Transaction = txn;
		foreach (var (name, type, value) in parameters)
		{
			cmd.Parameters.Add(name, type, value ?? DBNull.Value);
		}
		var count = await cmd.ExecuteNonQueryAsync();
		await txn.CommitAsync();
		return count;
	}

	// ─── Query Helpers ───

	/// <summary>
	/// Executes a SELECT query and returns all rows as a list of dictionaries.
	/// </summary>
	protected async Task<List<Dictionary<string, object?>>> QueryAsync(string sql,
		params (string name, SpannerDbType type, object? value)[] parameters)
	{
		using var connection = Fixture.CreateConnection();
		var paramCollection = new SpannerParameterCollection();
		foreach (var (name, type, value) in parameters)
		{
			paramCollection.Add(name, type, value ?? DBNull.Value);
		}
		using var cmd = connection.CreateSelectCommand(sql, paramCollection);
		using var reader = await cmd.ExecuteReaderAsync();

		var results = new List<Dictionary<string, object?>>();
		while (await reader.ReadAsync())
		{
			var row = new Dictionary<string, object?>();
			for (int i = 0; i < reader.FieldCount; i++)
			{
				row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
			}
			results.Add(row);
		}
		return results;
	}

	/// <summary>
	/// Executes a SELECT and returns a single scalar value.
	/// </summary>
	protected async Task<object?> QueryScalarAsync(string sql,
		params (string name, SpannerDbType type, object? value)[] parameters)
	{
		using var connection = Fixture.CreateConnection();
		var paramCollection = new SpannerParameterCollection();
		foreach (var (name, type, value) in parameters)
		{
			paramCollection.Add(name, type, value ?? DBNull.Value);
		}
		using var cmd = connection.CreateSelectCommand(sql, paramCollection);
		return await cmd.ExecuteScalarAsync();
	}

	// ─── Type Inference ───

	/// <summary>
	/// Infers SpannerDbType from a CLR value's runtime type.
	/// </summary>
	protected static SpannerDbType InferSpannerDbType(object value)
	{
		return value switch
		{
			long or int => SpannerDbType.Int64,
			string => SpannerDbType.String,
			bool => SpannerDbType.Bool,
			double or float => SpannerDbType.Float64,
			SpannerNumeric => SpannerDbType.Numeric,
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-types#date_type
			//   DATE columns store calendar dates. DateTime values with no time component
			//   (midnight, Unspecified kind) map to Date; all others to Timestamp.
			DateTime dt when dt.TimeOfDay == TimeSpan.Zero && dt.Kind != DateTimeKind.Utc
				=> SpannerDbType.Date,
			DateTime => SpannerDbType.Timestamp,
			DateTimeOffset => SpannerDbType.Timestamp,
			byte[] => SpannerDbType.Bytes,
			string[] => SpannerDbType.ArrayOf(SpannerDbType.String),
			long[] => SpannerDbType.ArrayOf(SpannerDbType.Int64),
			double[] => SpannerDbType.ArrayOf(SpannerDbType.Float64),
			bool[] => SpannerDbType.ArrayOf(SpannerDbType.Bool),
			_ => throw new NotSupportedException(
				$"Cannot infer SpannerDbType for CLR type {value.GetType().Name}. " +
				$"Use an explicit SpannerDbType parameter instead.")
		};
	}
}
