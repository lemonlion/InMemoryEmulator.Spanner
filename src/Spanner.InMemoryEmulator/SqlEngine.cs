using Google.Cloud.Spanner.V1;
using Spanner.InMemoryEmulator.Parsing;
using Superpower;
using TypeCode = Google.Cloud.Spanner.V1.TypeCode;

namespace Spanner.InMemoryEmulator;

/// <summary>
/// Central SQL dispatcher: parses SQL and routes to the appropriate executor.
/// </summary>
internal class SqlEngine
{
	private readonly InMemorySpannerDatabase _database;
	private readonly QueryExecutor _queryExecutor;
	private readonly DmlExecutor _dmlExecutor;

	public SqlEngine(InMemorySpannerDatabase database)
	{
		_database = database ?? throw new ArgumentNullException(nameof(database));
		_queryExecutor = new QueryExecutor(database);
		_dmlExecutor = new DmlExecutor(database);
	}

	/// <summary>
	/// Executes a SQL statement and returns the result.
	/// For SELECT: returns a ResultSet with rows.
	/// For DML: returns a ResultSet with stats.row_count_exact.
	/// </summary>
	public ResultSet ExecuteSql(string sql, IDictionary<string, object?>? parameters)
	{
		var trimmed = sql.Trim().TrimEnd(';').Trim();
		if (string.IsNullOrWhiteSpace(trimmed))
			throw new InvalidOperationException("Empty SQL statement.");

		// Simple heuristic detection for DDL (handled separately by the admin service)
		var upper = trimmed.ToUpperInvariant();
		if (upper.StartsWith("CREATE ") || upper.StartsWith("DROP ") || upper.StartsWith("ALTER "))
		{
			DdlParser.ExecuteDdl(trimmed, _database.Schema);
			return new ResultSet();
		}

		// Tokenize
		var tokens = GoogleSqlTokenizer.Tokenize(trimmed);

		// Try to parse as a SQL statement
		var result = SqlParsers.SqlStatement.AtEnd().TryParse(tokens);
		if (!result.HasValue)
		{
			throw new InvalidOperationException(
				$"Failed to parse SQL: {result.ErrorMessage} at position {result.ErrorPosition}. SQL: {sql}");
		}

		return result.Value switch
		{
			FullQuery fullQuery => _queryExecutor.Execute(fullQuery, parameters),
			SelectStatement select => _queryExecutor.Execute(select, parameters),
			InsertStatement insert => ExecuteDml(() => _dmlExecutor.ExecuteInsert(insert, parameters)),
			UpdateStatement update => ExecuteDml(() => _dmlExecutor.ExecuteUpdate(update, parameters)),
			DeleteStatement delete => ExecuteDml(() => _dmlExecutor.ExecuteDelete(delete, parameters)),
			_ => throw new InvalidOperationException($"Unknown SQL statement type: {result.Value.GetType().Name}")
		};
	}

	private static ResultSet ExecuteDml(Func<int> executor)
	{
		var rowCount = executor();
		// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.ResultSet
		//   "For DML statements, stats.row_count_exact contains the number of rows modified."
		var resultSet = new ResultSet
		{
			Stats = new ResultSetStats
			{
				RowCountExact = rowCount
			}
		};
		return resultSet;
	}

	/// <summary>
	/// Extracts parameters from an ExecuteSqlRequest into a .NET dictionary.
	/// </summary>
	public static IDictionary<string, object?>? ExtractParameters(ExecuteSqlRequest request)
	{
		return ExtractParameters(request.Sql, request.Params, request.ParamTypes);
	}

	/// <summary>
	/// Extracts parameters from a SQL statement's params and param types.
	/// </summary>
	public static IDictionary<string, object?>? ExtractParameters(
		string sql,
		Google.Protobuf.WellKnownTypes.Struct? paramStruct,
		Google.Protobuf.Collections.MapField<string, Google.Cloud.Spanner.V1.Type>? paramTypes)
	{
		if (paramStruct == null || paramStruct.Fields.Count == 0)
			return null;

		var parameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

		foreach (var field in paramStruct.Fields)
		{
			var paramName = field.Key;
			var paramValue = field.Value;

			// Determine the type from ParamTypes if available
			TypeCode typeCode = TypeCode.String;
			if (paramTypes != null && paramTypes.TryGetValue(paramName, out var paramType))
			{
				typeCode = paramType.Code;
			}
			else
			{
				// Infer from the value kind
				typeCode = paramValue.KindCase switch
				{
					Google.Protobuf.WellKnownTypes.Value.KindOneofCase.BoolValue => TypeCode.Bool,
					Google.Protobuf.WellKnownTypes.Value.KindOneofCase.NumberValue => TypeCode.Float64,
					Google.Protobuf.WellKnownTypes.Value.KindOneofCase.StringValue => TypeCode.String,
					Google.Protobuf.WellKnownTypes.Value.KindOneofCase.NullValue => TypeCode.String,
					_ => TypeCode.String
				};
			}

			parameters[paramName] = TypeConverter.FromProtobufValue(paramValue, typeCode);
		}

		return parameters;
	}
}
