// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/information-schema
// Virtual INFORMATION_SCHEMA tables backed by SchemaRegistry metadata.

namespace Spanner.InMemoryEmulator;

internal class InformationSchemaProvider
{
	private readonly SchemaRegistry _schema;

	public InformationSchemaProvider(SchemaRegistry schema) => _schema = schema;

	/// <summary>
	/// Returns virtual rows for the given INFORMATION_SCHEMA table.
	/// Column names are upper-cased to match Spanner conventions.
	/// Returns virtual rows and a TableDefinition for type inference.
	/// </summary>
	public (TableDefinition VirtualTable, List<Dictionary<string, object?>> Rows) GetVirtualTable(string tableName)
	{
		var (columns, rows) = tableName.ToUpperInvariant() switch
		{
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/information-schema#tables
			"TABLES" => GetTables(),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/information-schema#columns
			"COLUMNS" => GetColumns(),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/information-schema#indexes
			"INDEXES" => GetIndexes(),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/information-schema#index_columns
			"INDEX_COLUMNS" => GetIndexColumns(),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/information-schema#table_constraints
			"TABLE_CONSTRAINTS" => GetTableConstraints(),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/information-schema#constraint_column_usage
			"CONSTRAINT_COLUMN_USAGE" => GetConstraintColumnUsage(),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/information-schema#referential_constraints
			"REFERENTIAL_CONSTRAINTS" => GetReferentialConstraints(),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/information-schema#check_constraints
			"CHECK_CONSTRAINTS" => GetCheckConstraints(),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/information-schema#schemata
			"SCHEMATA" => GetSchemata(),
			"VIEWS" => GetViews(),
			"SEQUENCES" => GetSequences(),
			"SEQUENCE_OPTIONS" => GetSequenceOptions(),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/information-schema#column_options
			"COLUMN_OPTIONS" => GetColumnOptions(),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/information-schema#key_column_usage
			"KEY_COLUMN_USAGE" => GetKeyColumnUsage(),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/information-schema#database_options
			"DATABASE_OPTIONS" => GetDatabaseOptions(),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/information-schema#constraint_table_usage
			"CONSTRAINT_TABLE_USAGE" => GetConstraintTableUsage(),
			// Ref: https://cloud.google.com/spanner/docs/information-schema#information_schemachange_streams
			"CHANGE_STREAMS" => GetChangeStreams(),
			// Ref: https://cloud.google.com/spanner/docs/information-schema#information_schemachange_stream_tables
			"CHANGE_STREAM_TABLES" => GetChangeStreamTables(),
			// Ref: https://cloud.google.com/spanner/docs/information-schema#information_schemachange_stream_columns
			"CHANGE_STREAM_COLUMNS" => GetChangeStreamColumns(),
			// Ref: https://cloud.google.com/spanner/docs/information-schema#information_schemachange_stream_options
			"CHANGE_STREAM_OPTIONS" => GetChangeStreamOptions(),
			// Ref: https://cloud.google.com/spanner/docs/information-schema#spanner_statistics
			"SPANNER_STATISTICS" => GetSpannerStatistics(),
			_ => throw new InvalidOperationException($"INFORMATION_SCHEMA.{tableName} is not supported.")
		};

		// Build a virtual TableDefinition for type inference
		var colDefs = columns.Select(c => new ColumnDef(c, InferInfoSchemaColumnType(c))).ToList();
		var virtualTable = new TableDefinition("INFORMATION_SCHEMA." + tableName.ToUpperInvariant(), colDefs, columns.Take(1).ToList());
		return (virtualTable, rows);
	}

	private static Google.Cloud.Spanner.V1.TypeCode InferInfoSchemaColumnType(string columnName)
	{
		// Most INFORMATION_SCHEMA columns are STRING. Only ORDINAL_POSITION and boolean columns differ.
		return columnName switch
		{
			"ORDINAL_POSITION" => Google.Cloud.Spanner.V1.TypeCode.Int64,
			"IS_UNIQUE" or "IS_NULL_FILTERED" or "ALL" or "ALL_COLUMNS" => Google.Cloud.Spanner.V1.TypeCode.Bool,
			_ => Google.Cloud.Spanner.V1.TypeCode.String
		};
	}

	private (List<string>, List<Dictionary<string, object?>>) GetTables()
	{
		// Ref: https://cloud.google.com/spanner/docs/information-schema#tables
		var cols = new List<string> { "TABLE_CATALOG", "TABLE_SCHEMA", "TABLE_NAME", "TABLE_TYPE", "PARENT_TABLE_NAME", "ON_DELETE_ACTION", "SPANNER_STATE" };
		var rows = new List<Dictionary<string, object?>>();

		foreach (var name in _schema.GetTableNames())
		{
			var t = _schema.GetTableDefinition(name);
			rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
			{
				["TABLE_CATALOG"] = "",
				["TABLE_SCHEMA"] = "",
				["TABLE_NAME"] = t.Name,
				["TABLE_TYPE"] = "BASE TABLE",
				["PARENT_TABLE_NAME"] = t.ParentTable,
				["ON_DELETE_ACTION"] = t.ParentTable != null ? t.OnDeleteAction.ToString().ToUpperInvariant() : null,
				["SPANNER_STATE"] = "COMMITTED"
			});
		}

		return (cols, rows);
	}

	private (List<string>, List<Dictionary<string, object?>>) GetColumns()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/information-schema#columns
		var cols = new List<string>
		{
			"TABLE_CATALOG", "TABLE_SCHEMA", "TABLE_NAME", "COLUMN_NAME",
			"ORDINAL_POSITION", "IS_NULLABLE", "SPANNER_TYPE",
			"COLUMN_DEFAULT", "GENERATION_EXPRESSION", "IS_GENERATED", "IS_STORED", "SPANNER_STATE"
		};
		var rows = new List<Dictionary<string, object?>>();

		foreach (var name in _schema.GetTableNames())
		{
			var t = _schema.GetTableDefinition(name);
			for (int i = 0; i < t.Columns.Count; i++)
			{
				var c = t.Columns[i];
				rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
				{
					["TABLE_CATALOG"] = "",
					["TABLE_SCHEMA"] = "",
					["TABLE_NAME"] = t.Name,
					["COLUMN_NAME"] = c.Name,
					["ORDINAL_POSITION"] = (long)(i + 1),
					["IS_NULLABLE"] = c.IsNullable ? "YES" : "NO",
					["SPANNER_TYPE"] = FormatSpannerType(c),
					// Ref: https://cloud.google.com/spanner/docs/information-schema#columns
					["COLUMN_DEFAULT"] = c.DefaultExpression,
					["GENERATION_EXPRESSION"] = c.GeneratedExpression,
					["IS_GENERATED"] = c.GeneratedExpression != null ? "ALWAYS" : "NEVER",
					["IS_STORED"] = c.GeneratedExpression != null ? (c.IsStored ? "YES" : "NO") : null,
					["SPANNER_STATE"] = "COMMITTED"
				});
			}
		}

		return (cols, rows);
	}

	private (List<string>, List<Dictionary<string, object?>>) GetIndexes()
	{
		// Ref: https://cloud.google.com/spanner/docs/information-schema#indexes
		var cols = new List<string>
		{
			"TABLE_CATALOG", "TABLE_SCHEMA", "TABLE_NAME", "INDEX_NAME",
			"INDEX_TYPE", "PARENT_TABLE_NAME", "IS_UNIQUE", "IS_NULL_FILTERED",
			"INDEX_STATE", "SPANNER_IS_MANAGED"
		};
		var rows = new List<Dictionary<string, object?>>();

		// Primary key indexes
		foreach (var name in _schema.GetTableNames())
		{
			var t = _schema.GetTableDefinition(name);
			rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
			{
				["TABLE_CATALOG"] = "",
				["TABLE_SCHEMA"] = "",
				["TABLE_NAME"] = t.Name,
				["INDEX_NAME"] = "PRIMARY_KEY",
				["INDEX_TYPE"] = "PRIMARY_KEY",
				["PARENT_TABLE_NAME"] = "",
				["IS_UNIQUE"] = true,
				["IS_NULL_FILTERED"] = false,
				["INDEX_STATE"] = "READ_WRITE",
				["SPANNER_IS_MANAGED"] = false
			});
		}

		// Secondary indexes
		foreach (var name in _schema.GetTableNames())
		{
			foreach (var idx in _schema.GetIndexesForTable(name))
			{
				rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
				{
					["TABLE_CATALOG"] = "",
					["TABLE_SCHEMA"] = "",
					["TABLE_NAME"] = idx.TableName,
					["INDEX_NAME"] = idx.Name,
					["INDEX_TYPE"] = "INDEX",
					["PARENT_TABLE_NAME"] = "",
					["IS_UNIQUE"] = idx.IsUnique,
					["IS_NULL_FILTERED"] = idx.IsNullFiltered,
					["INDEX_STATE"] = "READ_WRITE",
					["SPANNER_IS_MANAGED"] = false
				});
			}
		}

		return (cols, rows);
	}

	private (List<string>, List<Dictionary<string, object?>>) GetIndexColumns()
	{
		var cols = new List<string>
		// Ref: https://cloud.google.com/spanner/docs/information-schema#index_columns
		{
			"TABLE_CATALOG", "TABLE_SCHEMA", "TABLE_NAME", "INDEX_NAME",
			"COLUMN_NAME", "ORDINAL_POSITION", "COLUMN_ORDERING", "IS_NULLABLE", "SPANNER_TYPE"
		};
		var rows = new List<Dictionary<string, object?>>();

		// PK columns
		foreach (var name in _schema.GetTableNames())
		{
			var t = _schema.GetTableDefinition(name);
			for (int i = 0; i < t.PrimaryKeyColumns.Count; i++)
			{
				var pkCol = t.PrimaryKeyColumns[i];
				var colDef = t.Columns.FirstOrDefault(c =>
					string.Equals(c.Name, pkCol, StringComparison.OrdinalIgnoreCase));
				rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
				{
					["TABLE_CATALOG"] = "",
					["TABLE_SCHEMA"] = "",
					["TABLE_NAME"] = t.Name,
					["INDEX_NAME"] = "PRIMARY_KEY",
					["COLUMN_NAME"] = pkCol,
					["ORDINAL_POSITION"] = (long)(i + 1),
					["COLUMN_ORDERING"] = "ASC",
					["IS_NULLABLE"] = colDef?.IsNullable == true ? "YES" : "NO",
					["SPANNER_TYPE"] = colDef != null ? FormatSpannerType(colDef) : null
				});
			}
		}

		// Secondary index columns
		foreach (var name in _schema.GetTableNames())
		{
			foreach (var idx in _schema.GetIndexesForTable(name))
			{
				var t = _schema.GetTableDefinition(idx.TableName);
				for (int i = 0; i < idx.Columns.Count; i++)
				{
					var ic = idx.Columns[i];
					var colDef = t?.Columns.FirstOrDefault(c =>
						string.Equals(c.Name, ic.Name, StringComparison.OrdinalIgnoreCase));
					rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
					{
						["TABLE_CATALOG"] = "",
						["TABLE_SCHEMA"] = "",
						["TABLE_NAME"] = idx.TableName,
						["INDEX_NAME"] = idx.Name,
						["COLUMN_NAME"] = ic.Name,
						["ORDINAL_POSITION"] = (long)(i + 1),
						["COLUMN_ORDERING"] = ic.Order == SortOrder.Desc ? "DESC" : "ASC",
						["IS_NULLABLE"] = "YES",
						["SPANNER_TYPE"] = colDef != null ? FormatSpannerType(colDef) : null
					});
				}
			}
		}

		return (cols, rows);
	}

	private (List<string>, List<Dictionary<string, object?>>) GetTableConstraints()
	{
		var cols = new List<string>
		{
			"CONSTRAINT_CATALOG", "CONSTRAINT_SCHEMA", "CONSTRAINT_NAME",
			"TABLE_CATALOG", "TABLE_SCHEMA", "TABLE_NAME",
			"CONSTRAINT_TYPE", "IS_DEFERRABLE", "INITIALLY_DEFERRED", "ENFORCED"
		};
		var rows = new List<Dictionary<string, object?>>();

		foreach (var name in _schema.GetTableNames())
		{
			var t = _schema.GetTableDefinition(name);

			// PK constraint
			rows.Add(MakeConstraintRow(t.Name, "PK_" + t.Name, "PRIMARY KEY", "YES"));

			// CHECK constraints
			foreach (var ck in t.CheckConstraints)
			{
				rows.Add(MakeConstraintRow(t.Name, ck.Name ?? "CK_" + t.Name, "CHECK", "YES"));
			}

			// FK constraints
			foreach (var fk in t.ForeignKeys)
			{
				rows.Add(MakeConstraintRow(t.Name, fk.Name ?? "FK_" + t.Name, "FOREIGN KEY",
					fk.IsEnforced ? "YES" : "NO"));
			}
		}

		// Unique index constraints
		foreach (var tname in _schema.GetTableNames())
		{
			foreach (var idx in _schema.GetIndexesForTable(tname))
			{
				if (idx.IsUnique)
				{
					rows.Add(MakeConstraintRow(tname, idx.Name, "UNIQUE", "YES"));
				}
			}
		}

		return (cols, rows);
	}

	private static Dictionary<string, object?> MakeConstraintRow(string tableName, string constraintName,
		string constraintType, string enforced)
	{
		return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
		{
			["CONSTRAINT_CATALOG"] = "",
			["CONSTRAINT_SCHEMA"] = "",
			["CONSTRAINT_NAME"] = constraintName,
			["TABLE_CATALOG"] = "",
			["TABLE_SCHEMA"] = "",
			["TABLE_NAME"] = tableName,
			["CONSTRAINT_TYPE"] = constraintType,
			["IS_DEFERRABLE"] = "NO",
			["INITIALLY_DEFERRED"] = "NO",
			["ENFORCED"] = enforced
		};
	}

	private (List<string>, List<Dictionary<string, object?>>) GetConstraintColumnUsage()
	{
		var cols = new List<string>
		{
			"TABLE_CATALOG", "TABLE_SCHEMA", "TABLE_NAME",
			"COLUMN_NAME", "CONSTRAINT_CATALOG", "CONSTRAINT_SCHEMA", "CONSTRAINT_NAME"
		};
		var rows = new List<Dictionary<string, object?>>();

		foreach (var name in _schema.GetTableNames())
		{
			var t = _schema.GetTableDefinition(name);
			// PK columns
			foreach (var pk in t.PrimaryKeyColumns)
			{
				rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
				{
					["TABLE_CATALOG"] = "", ["TABLE_SCHEMA"] = "", ["TABLE_NAME"] = t.Name,
					["COLUMN_NAME"] = pk,
					["CONSTRAINT_CATALOG"] = "", ["CONSTRAINT_SCHEMA"] = "",
					["CONSTRAINT_NAME"] = "PK_" + t.Name
				});
			}
		}

		return (cols, rows);
	}

	private (List<string>, List<Dictionary<string, object?>>) GetReferentialConstraints()
	{
		var cols = new List<string>
		{
			"CONSTRAINT_CATALOG", "CONSTRAINT_SCHEMA", "CONSTRAINT_NAME",
			"UNIQUE_CONSTRAINT_CATALOG", "UNIQUE_CONSTRAINT_SCHEMA", "UNIQUE_CONSTRAINT_NAME",
			"MATCH_OPTION", "UPDATE_RULE", "DELETE_RULE", "SPANNER_STATE"
		};
		var rows = new List<Dictionary<string, object?>>();

		foreach (var name in _schema.GetTableNames())
		{
			var t = _schema.GetTableDefinition(name);
			foreach (var fk in t.ForeignKeys)
			{
				rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
				{
					["CONSTRAINT_CATALOG"] = "", ["CONSTRAINT_SCHEMA"] = "",
					["CONSTRAINT_NAME"] = fk.Name ?? "FK_" + t.Name,
					["UNIQUE_CONSTRAINT_CATALOG"] = "", ["UNIQUE_CONSTRAINT_SCHEMA"] = "",
					["UNIQUE_CONSTRAINT_NAME"] = "PK_" + fk.ReferencedTable,
					["MATCH_OPTION"] = "SIMPLE",
					["UPDATE_RULE"] = "NO ACTION",
					["DELETE_RULE"] = fk.OnDelete == ForeignKeyDeleteAction.Cascade ? "CASCADE" : "NO ACTION",
					["SPANNER_STATE"] = "COMMITTED"
				});
			}
		}

		return (cols, rows);
	}

	private (List<string>, List<Dictionary<string, object?>>) GetCheckConstraints()
	{
		var cols = new List<string>
		{
			"CONSTRAINT_CATALOG", "CONSTRAINT_SCHEMA", "CONSTRAINT_NAME",
			"CHECK_CLAUSE", "SPANNER_STATE"
		};
		var rows = new List<Dictionary<string, object?>>();

		foreach (var name in _schema.GetTableNames())
		{
			var t = _schema.GetTableDefinition(name);
			foreach (var ck in t.CheckConstraints)
			{
				rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
				{
					["CONSTRAINT_CATALOG"] = "", ["CONSTRAINT_SCHEMA"] = "",
					["CONSTRAINT_NAME"] = ck.Name ?? "CK_" + t.Name,
					["CHECK_CLAUSE"] = ck.Expression,
					["SPANNER_STATE"] = "COMMITTED"
				});
			}
		}

		return (cols, rows);
	}

	private (List<string>, List<Dictionary<string, object?>>) GetSchemata()
	{
		var cols = new List<string> { "CATALOG_NAME", "SCHEMA_NAME" };
		var rows = new List<Dictionary<string, object?>>
		{
			new(StringComparer.OrdinalIgnoreCase) { ["CATALOG_NAME"] = "", ["SCHEMA_NAME"] = "" },
			new(StringComparer.OrdinalIgnoreCase) { ["CATALOG_NAME"] = "", ["SCHEMA_NAME"] = "INFORMATION_SCHEMA" }
		};
		return (cols, rows);
	}

	private static string FormatSpannerType(ColumnDef col)
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/information-schema#columns
		var baseType = col.SpannerType switch
		{
			Google.Cloud.Spanner.V1.TypeCode.Int64 => "INT64",
			Google.Cloud.Spanner.V1.TypeCode.Float64 => "FLOAT64",
			Google.Cloud.Spanner.V1.TypeCode.Float32 => "FLOAT32",
			Google.Cloud.Spanner.V1.TypeCode.Bool => "BOOL",
			Google.Cloud.Spanner.V1.TypeCode.Timestamp => "TIMESTAMP",
			Google.Cloud.Spanner.V1.TypeCode.Date => "DATE",
			Google.Cloud.Spanner.V1.TypeCode.Numeric => "NUMERIC",
			Google.Cloud.Spanner.V1.TypeCode.Json => "JSON",
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-types#uuid_type
			(Google.Cloud.Spanner.V1.TypeCode)17 => "UUID",
			// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#typecode
			(Google.Cloud.Spanner.V1.TypeCode)13 => col.ProtoTypeFqn ?? "PROTO",
			(Google.Cloud.Spanner.V1.TypeCode)14 => col.ProtoTypeFqn ?? "ENUM",
			Google.Cloud.Spanner.V1.TypeCode.Bytes => col.MaxLength > 0 ? $"BYTES({col.MaxLength})" : "BYTES(MAX)",
			Google.Cloud.Spanner.V1.TypeCode.String => col.MaxLength > 0 ? $"STRING({col.MaxLength})" : "STRING(MAX)",
			Google.Cloud.Spanner.V1.TypeCode.Array => $"ARRAY<{col.ArrayElementType}>",
			_ => col.SpannerType.ToString().ToUpperInvariant()
		};

		return baseType;
	}

	private (List<string>, List<Dictionary<string, object?>>) GetViews()
	{
		var cols = new List<string> { "TABLE_CATALOG", "TABLE_SCHEMA", "TABLE_NAME", "VIEW_DEFINITION" };
		var rows = new List<Dictionary<string, object?>>();
		foreach (var (name, view) in _schema.GetViews())
		{
			rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
			{
				["TABLE_CATALOG"] = "", ["TABLE_SCHEMA"] = "",
				["TABLE_NAME"] = view.Name,
				["VIEW_DEFINITION"] = view.SqlBody
			});
		}
		return (cols, rows);
	}

	private (List<string>, List<Dictionary<string, object?>>) GetSequences()
	{
		var cols = new List<string> { "CATALOG", "SCHEMA", "NAME", "DATA_TYPE" };
		var rows = new List<Dictionary<string, object?>>();
		foreach (var (name, seq) in _schema.GetSequences())
		{
			rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
			{
				["CATALOG"] = "", ["SCHEMA"] = "",
				["NAME"] = seq.Name,
				["DATA_TYPE"] = "INT64"
			});
		}
		return (cols, rows);
	}

	private (List<string>, List<Dictionary<string, object?>>) GetSequenceOptions()
	{
		var cols = new List<string> { "CATALOG", "SCHEMA", "NAME", "OPTION_NAME", "OPTION_TYPE", "OPTION_VALUE" };
		var rows = new List<Dictionary<string, object?>>();
		foreach (var (name, seq) in _schema.GetSequences())
		{
			rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
			{
				["CATALOG"] = "", ["SCHEMA"] = "", ["NAME"] = seq.Name,
				["OPTION_NAME"] = "sequence_kind",
				["OPTION_TYPE"] = "STRING",
				["OPTION_VALUE"] = $"'{seq.SequenceKind}'"
			});
		}
		return (cols, rows);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/information-schema#column_options
	//   "Contains the options set on columns via ALTER COLUMN ... SET OPTIONS."
	private (List<string>, List<Dictionary<string, object?>>) GetColumnOptions()
	{
		var cols = new List<string> { "TABLE_CATALOG", "TABLE_SCHEMA", "TABLE_NAME", "COLUMN_NAME", "OPTION_NAME", "OPTION_TYPE", "OPTION_VALUE" };
		var rows = new List<Dictionary<string, object?>>();
		foreach (var name in _schema.GetTableNames())
		{
			var table = _schema.GetTableDefinition(name);
			if (table == null) continue;
			foreach (var col in table.Columns)
			{
				if (col.AllowCommitTimestamp)
				{
					rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
					{
						["TABLE_CATALOG"] = "", ["TABLE_SCHEMA"] = "",
						["TABLE_NAME"] = table.Name,
						["COLUMN_NAME"] = col.Name,
						["OPTION_NAME"] = "allow_commit_timestamp",
						["OPTION_TYPE"] = "BOOL",
						["OPTION_VALUE"] = "TRUE"
					});
				}
			}
		}
		return (cols, rows);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/information-schema#key_column_usage
	//   "Contains the columns that make up the primary key and foreign key constraints."
	private (List<string>, List<Dictionary<string, object?>>) GetKeyColumnUsage()
	{
		var cols = new List<string> { "CONSTRAINT_CATALOG", "CONSTRAINT_SCHEMA", "CONSTRAINT_NAME", "TABLE_CATALOG", "TABLE_SCHEMA", "TABLE_NAME", "COLUMN_NAME", "ORDINAL_POSITION", "POSITION_IN_UNIQUE_CONSTRAINT" };
		var rows = new List<Dictionary<string, object?>>();
		foreach (var name in _schema.GetTableNames())
		{
			var table = _schema.GetTableDefinition(name);
			if (table == null) continue;
			// Primary key columns
			for (int i = 0; i < table.PrimaryKeyColumns.Count; i++)
			{
				rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
				{
					["CONSTRAINT_CATALOG"] = "", ["CONSTRAINT_SCHEMA"] = "",
					["CONSTRAINT_NAME"] = $"PK_{table.Name}",
					["TABLE_CATALOG"] = "", ["TABLE_SCHEMA"] = "",
					["TABLE_NAME"] = table.Name,
					["COLUMN_NAME"] = table.PrimaryKeyColumns[i],
					["ORDINAL_POSITION"] = (long)(i + 1),
					["POSITION_IN_UNIQUE_CONSTRAINT"] = null
				});
			}
		}
		return (cols, rows);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/information-schema#database_options
	//   "Contains the database options set via ALTER DATABASE."
	private (List<string>, List<Dictionary<string, object?>>) GetDatabaseOptions()
	{
		var cols = new List<string> { "CATALOG_NAME", "SCHEMA_NAME", "OPTION_NAME", "OPTION_TYPE", "OPTION_VALUE" };
		var rows = new List<Dictionary<string, object?>>
		{
			new(StringComparer.OrdinalIgnoreCase)
			{
				["CATALOG_NAME"] = "", ["SCHEMA_NAME"] = "",
				["OPTION_NAME"] = "version_retention_period",
				["OPTION_TYPE"] = "STRING",
				["OPTION_VALUE"] = "'1h'"
			}
		};
		return (cols, rows);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/information-schema#constraint_table_usage
	//   "Contains the tables that constraints are defined on."
	private (List<string>, List<Dictionary<string, object?>>) GetConstraintTableUsage()
	{
		var cols = new List<string> { "TABLE_CATALOG", "TABLE_SCHEMA", "TABLE_NAME", "CONSTRAINT_CATALOG", "CONSTRAINT_SCHEMA", "CONSTRAINT_NAME" };
		var rows = new List<Dictionary<string, object?>>();
		foreach (var tname in _schema.GetTableNames())
		{
			// Primary key constraint
			rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
			{
				["TABLE_CATALOG"] = "", ["TABLE_SCHEMA"] = "",
				["TABLE_NAME"] = tname,
				["CONSTRAINT_CATALOG"] = "", ["CONSTRAINT_SCHEMA"] = "",
				["CONSTRAINT_NAME"] = $"PK_{tname}"
			});
		}
		return (cols, rows);
	}

	// Ref: https://cloud.google.com/spanner/docs/information-schema#information_schemachange_streams
	//   "Lists all of a database's change streams."
	private (List<string>, List<Dictionary<string, object?>>) GetChangeStreams()
	{
		var cols = new List<string> { "CHANGE_STREAM_CATALOG", "CHANGE_STREAM_SCHEMA", "CHANGE_STREAM_NAME", "ALL" };
		var rows = new List<Dictionary<string, object?>>();
		foreach (var (_, cs) in _schema.GetChangeStreams())
		{
			rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
			{
				["CHANGE_STREAM_CATALOG"] = "",
				["CHANGE_STREAM_SCHEMA"] = "",
				["CHANGE_STREAM_NAME"] = cs.Name,
				["ALL"] = cs.WatchesAll
			});
		}
		return (cols, rows);
	}

	// Ref: https://cloud.google.com/spanner/docs/information-schema#information_schemachange_stream_tables
	//   "Contains information about tables and the change streams that watch them."
	private (List<string>, List<Dictionary<string, object?>>) GetChangeStreamTables()
	{
		var cols = new List<string> { "CHANGE_STREAM_CATALOG", "CHANGE_STREAM_SCHEMA", "CHANGE_STREAM_NAME", "TABLE_CATALOG", "TABLE_SCHEMA", "TABLE_NAME", "ALL_COLUMNS" };
		var rows = new List<Dictionary<string, object?>>();
		foreach (var (_, cs) in _schema.GetChangeStreams())
		{
			foreach (var (table, columns) in cs.WatchedTables)
			{
				rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
				{
					["CHANGE_STREAM_CATALOG"] = "",
					["CHANGE_STREAM_SCHEMA"] = "",
					["CHANGE_STREAM_NAME"] = cs.Name,
					["TABLE_CATALOG"] = "",
					["TABLE_SCHEMA"] = "",
					["TABLE_NAME"] = table,
					["ALL_COLUMNS"] = columns == null
				});
			}
		}
		return (cols, rows);
	}

	// Ref: https://cloud.google.com/spanner/docs/information-schema#information_schemachange_stream_columns
	//   "Contains information about table columns and the change streams that watch them."
	private (List<string>, List<Dictionary<string, object?>>) GetChangeStreamColumns()
	{
		var cols = new List<string> { "CHANGE_STREAM_CATALOG", "CHANGE_STREAM_SCHEMA", "CHANGE_STREAM_NAME", "TABLE_CATALOG", "TABLE_SCHEMA", "TABLE_NAME", "COLUMN_NAME" };
		var rows = new List<Dictionary<string, object?>>();
		foreach (var (_, cs) in _schema.GetChangeStreams())
		{
			foreach (var (table, columns) in cs.WatchedTables)
			{
				if (columns == null) continue; // whole-table watch — columns not listed here
				foreach (var col in columns)
				{
					rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
					{
						["CHANGE_STREAM_CATALOG"] = "",
						["CHANGE_STREAM_SCHEMA"] = "",
						["CHANGE_STREAM_NAME"] = cs.Name,
						["TABLE_CATALOG"] = "",
						["TABLE_SCHEMA"] = "",
						["TABLE_NAME"] = table,
						["COLUMN_NAME"] = col
					});
				}
			}
		}
		return (cols, rows);
	}

	// Ref: https://cloud.google.com/spanner/docs/information-schema#information_schemachange_stream_options
	//   "Contains the configuration options for change streams."
	private (List<string>, List<Dictionary<string, object?>>) GetChangeStreamOptions()
	{
		var cols = new List<string> { "CHANGE_STREAM_CATALOG", "CHANGE_STREAM_SCHEMA", "CHANGE_STREAM_NAME", "OPTION_NAME", "OPTION_TYPE", "OPTION_VALUE" };
		var rows = new List<Dictionary<string, object?>>();
		foreach (var (_, cs) in _schema.GetChangeStreams())
		{
			foreach (var (optName, optValue) in cs.Options)
			{
				rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
				{
					["CHANGE_STREAM_CATALOG"] = "",
					["CHANGE_STREAM_SCHEMA"] = "",
					["CHANGE_STREAM_NAME"] = cs.Name,
					["OPTION_NAME"] = optName,
					["OPTION_TYPE"] = "STRING",
					["OPTION_VALUE"] = optValue
				});
			}
		}
		return (cols, rows);
	}

	// Ref: https://cloud.google.com/spanner/docs/information-schema#spanner_statistics
	//   "This table lists the available query optimizer statistics packages."
	//   The in-memory emulator has no statistics packages, so this returns an empty result set.
	private (List<string>, List<Dictionary<string, object?>>) GetSpannerStatistics()
	{
		var cols = new List<string> { "CATALOG_NAME", "SCHEMA_NAME", "PACKAGE_NAME", "ALLOW_GC" };
		var rows = new List<Dictionary<string, object?>>();
		return (cols, rows);
	}
}
