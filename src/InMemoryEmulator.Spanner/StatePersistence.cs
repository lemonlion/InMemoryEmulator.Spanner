using System.Text.Json;
using System.Text.Json.Serialization;
using TypeCode = Google.Cloud.Spanner.V1.TypeCode;

namespace InMemoryEmulator.Spanner;

/// <summary>
/// JSON-based state export/import for InMemorySpannerDatabase.
/// Serializes both schema (tables, indexes, views, sequences) and data.
/// </summary>
internal static class StatePersistence
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	/// <summary>
	/// Exports the entire database state (schema + data) as a JSON string.
	/// </summary>
	public static string Export(SchemaRegistry schema)
	{
		var state = new DatabaseState
		{
			Tables = ExportTables(schema),
			Indexes = ExportIndexes(schema),
			Views = ExportViews(schema),
			Sequences = ExportSequences(schema),
			ProtoBundleTypes = schema.HasProtoBundle ? schema.GetProtoBundleTypes().ToList() : null
		};
		return JsonSerializer.Serialize(state, JsonOptions);
	}

	/// <summary>
	/// Imports database state from JSON, replacing all existing state.
	/// </summary>
	public static void Import(string json, SchemaRegistry schema)
	{
		var state = JsonSerializer.Deserialize<DatabaseState>(json, JsonOptions)
			?? throw new ArgumentException("Invalid state JSON.");

		schema.ClearAll();

		// Restore proto bundle types
		if (state.ProtoBundleTypes is { Count: > 0 })
			schema.SetProtoBundleTypes(state.ProtoBundleTypes);

		// 1. Recreate tables
		foreach (var ts in state.Tables ?? [])
		{
			var columns = ts.Columns?.Select(c => new ColumnDef(
				c.Name ?? "",
				Enum.TryParse<TypeCode>(c.Type, true, out var tc) ? tc : TypeCode.String,
				c.IsNullable,
				c.MaxLength,
				c.AllowCommitTimestamp,
				protoTypeFqn: c.ProtoTypeFqn
			)).ToList() ?? [];

			var table = new TableDefinition(
				ts.Name ?? "",
				columns,
				ts.PrimaryKeyColumns ?? [],
				ts.ParentTable,
				Enum.TryParse<OnDeleteAction>(ts.OnDeleteAction, true, out var oda) ? oda : OnDeleteAction.NoAction
			);

			// Restore row deletion policy
			if (ts.RowDeletionPolicyColumn != null && ts.RowDeletionPolicyIntervalDays != null)
			{
				table.RowDeletionPolicy = new RowDeletionPolicy(
					ts.RowDeletionPolicyColumn, ts.RowDeletionPolicyIntervalDays.Value);
			}

			schema.AddTable(table);

			// Import rows
			foreach (var rowState in ts.Rows ?? [])
			{
				var rowValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
				foreach (var kvp in rowState.Columns ?? new Dictionary<string, object?>())
				{
					rowValues[kvp.Key] = ConvertJsonElement(kvp.Value);
				}

				var pkValues = (ts.PrimaryKeyColumns ?? [])
					.Select(pk => rowValues.TryGetValue(pk, out var v) ? v : null)
					.ToArray();

				var rowKey = new RowKey(pkValues);
				var commitTs = rowState.CommitTimestamp != null
					? DateTimeOffset.Parse(rowState.CommitTimestamp)
					: DateTimeOffset.UtcNow;

				table.Rows[rowKey] = new RowData(rowValues, commitTs);
			}
		}

		// 2. Recreate indexes
		foreach (var ix in state.Indexes ?? [])
		{
			var columns = ix.Columns?.Select(c => new IndexColumn(
				c.Name ?? "",
				Enum.TryParse<SortOrder>(c.Order, true, out var so) ? so : SortOrder.Asc
			)).ToList() ?? [];

			schema.AddIndex(new IndexDefinition(
				ix.Name ?? "",
				ix.TableName ?? "",
				columns,
				ix.StoringColumns,
				ix.IsUnique,
				ix.IsNullFiltered
			));
		}

		// 3. Recreate views
		foreach (var v in state.Views ?? [])
		{
			schema.AddView(new ViewDefinition(v.Name ?? "", v.SqlBody ?? ""));
		}

		// 4. Recreate sequences
		foreach (var s in state.Sequences ?? [])
		{
			schema.AddSequence(new SequenceDefinition(
				s.Name ?? "",
				s.SequenceKind ?? "bit_reversed_positive",
				s.Counter
			));
		}
	}

	private static object? ConvertJsonElement(object? value)
	{
		if (value is JsonElement elem)
		{
			return elem.ValueKind switch
			{
				JsonValueKind.String => elem.GetString(),
				JsonValueKind.Number when elem.TryGetInt64(out var l) => l,
				JsonValueKind.Number => elem.GetDouble(),
				JsonValueKind.True => true,
				JsonValueKind.False => false,
				JsonValueKind.Null => null,
				_ => elem.GetRawText()
			};
		}
		return value;
	}

	private static List<TableState> ExportTables(SchemaRegistry schema)
	{
		var result = new List<TableState>();
		foreach (var tableName in schema.GetTableNames())
		{
			var table = schema.GetTableDefinition(tableName);
			var ts = new TableState
			{
				Name = table.Name,
				Columns = table.Columns.Select(c => new ColumnState
				{
					Name = c.Name,
					Type = c.SpannerType.ToString(),
					IsNullable = c.IsNullable,
					MaxLength = c.MaxLength,
					AllowCommitTimestamp = c.AllowCommitTimestamp,
					ProtoTypeFqn = c.ProtoTypeFqn
				}).ToList(),
				PrimaryKeyColumns = table.PrimaryKeyColumns.ToList(),
				ParentTable = table.ParentTable,
				OnDeleteAction = table.OnDeleteAction.ToString(),
				RowDeletionPolicyColumn = table.RowDeletionPolicy?.Column,
				RowDeletionPolicyIntervalDays = table.RowDeletionPolicy?.IntervalDays,
				Rows = table.Rows.Values.Select(r => new RowState
				{
					Columns = r.Columns.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
					CommitTimestamp = r.CommitTimestamp.ToString("O")
				}).ToList()
			};
			result.Add(ts);
		}
		return result;
	}

	private static List<IndexState> ExportIndexes(SchemaRegistry schema)
	{
		var result = new List<IndexState>();
		foreach (var tableName in schema.GetTableNames())
		{
			foreach (var ix in schema.GetIndexesForTable(tableName))
			{
				result.Add(new IndexState
				{
					Name = ix.Name,
					TableName = ix.TableName,
					Columns = ix.Columns.Select(c => new IndexColumnState
					{
						Name = c.Name,
						Order = c.Order.ToString()
					}).ToList(),
					StoringColumns = ix.StoringColumns.ToList(),
					IsUnique = ix.IsUnique,
					IsNullFiltered = ix.IsNullFiltered
				});
			}
		}
		return result;
	}

	private static List<ViewState> ExportViews(SchemaRegistry schema)
	{
		return schema.GetViews().Values.Select(v => new ViewState
		{
			Name = v.Name,
			SqlBody = v.SqlBody
		}).ToList();
	}

	private static List<SequenceState> ExportSequences(SchemaRegistry schema)
	{
		return schema.GetSequences().Values.Select(s => new SequenceState
		{
			Name = s.Name,
			SequenceKind = s.SequenceKind,
			Counter = s.GetInternalState()
		}).ToList();
	}

	// ── JSON DTOs ──

	internal class DatabaseState
	{
		public List<TableState>? Tables { get; set; }
		public List<IndexState>? Indexes { get; set; }
		public List<ViewState>? Views { get; set; }
		public List<SequenceState>? Sequences { get; set; }
		public List<string>? ProtoBundleTypes { get; set; }
	}

	internal class TableState
	{
		public string? Name { get; set; }
		public List<ColumnState>? Columns { get; set; }
		public List<string>? PrimaryKeyColumns { get; set; }
		public string? ParentTable { get; set; }
		public string? OnDeleteAction { get; set; }
		public string? RowDeletionPolicyColumn { get; set; }
		public int? RowDeletionPolicyIntervalDays { get; set; }
		public List<RowState>? Rows { get; set; }
	}

	internal class ColumnState
	{
		public string? Name { get; set; }
		public string? Type { get; set; }
		public bool IsNullable { get; set; }
		public long? MaxLength { get; set; }
		public bool AllowCommitTimestamp { get; set; }
		public string? ProtoTypeFqn { get; set; }
	}

	internal class RowState
	{
		public Dictionary<string, object?>? Columns { get; set; }
		public string? CommitTimestamp { get; set; }
	}

	internal class IndexState
	{
		public string? Name { get; set; }
		public string? TableName { get; set; }
		public List<IndexColumnState>? Columns { get; set; }
		public List<string>? StoringColumns { get; set; }
		public bool IsUnique { get; set; }
		public bool IsNullFiltered { get; set; }
	}

	internal class IndexColumnState
	{
		public string? Name { get; set; }
		public string? Order { get; set; }
	}

	internal class ViewState
	{
		public string? Name { get; set; }
		public string? SqlBody { get; set; }
	}

	internal class SequenceState
	{
		public string? Name { get; set; }
		public string? SequenceKind { get; set; }
		public long Counter { get; set; }
	}
}
