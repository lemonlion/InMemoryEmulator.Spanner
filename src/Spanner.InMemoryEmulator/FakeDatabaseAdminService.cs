using System.Collections.Concurrent;
using Google.Cloud.Spanner.Admin.Database.V1;
using Google.LongRunning;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Spanner.InMemoryEmulator;

/// <summary>
/// gRPC service implementation for DatabaseAdmin.DatabaseAdminBase.
/// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.admin.database.v1#databaseadmin
/// </summary>
public class FakeDatabaseAdminService : DatabaseAdmin.DatabaseAdminBase
{
	private readonly InMemorySpannerDatabase _database;
	private readonly FakeSpannerServerOptions _options;
	private readonly ConcurrentBag<string> _ddlStatements = new();
	private readonly string _databaseName;
	private readonly string _parentName;

	public FakeDatabaseAdminService(InMemorySpannerDatabase database, FakeSpannerServerOptions options)
	{
		_database = database ?? throw new ArgumentNullException(nameof(database));
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_databaseName = $"projects/{options.ProjectId}/instances/{options.InstanceId}/databases/{options.DatabaseId}";
		_parentName = $"projects/{options.ProjectId}/instances/{options.InstanceId}";
	}

	/// <summary>The DDL statements that have been applied to the database.</summary>
	internal IReadOnlyList<string> DdlStatements => _ddlStatements.Reverse().ToList();

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.admin.database.v1#google.spanner.admin.database.v1.DatabaseAdmin.CreateDatabase
	//   Creates a new Spanner database. Returns a long-running operation.
	public override Task<Operation> CreateDatabase(CreateDatabaseRequest request, ServerCallContext context)
	{
		// Execute extra DDL statements if provided
		// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.admin.database.v1#createdatabaserequest
		//   "extra_statements: Optional. A list of DDL statements to run inside the newly created database."
		foreach (var stmt in request.ExtraStatements)
		{
			_database.ExecuteDdl(stmt);
			_ddlStatements.Add(stmt);
		}

		var dbProto = new Database
		{
			Name = _databaseName,
			State = Database.Types.State.Ready,
			CreateTime = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
			DatabaseDialect = DatabaseDialect.GoogleStandardSql,
		};

		var metadata = new CreateDatabaseMetadata
		{
			Database = _databaseName
		};

		var operation = new Operation
		{
			Name = $"{_databaseName}/operations/create",
			Done = true,
			Response = Any.Pack(dbProto),
			Metadata = Any.Pack(metadata),
		};

		return Task.FromResult(operation);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.admin.database.v1#google.spanner.admin.database.v1.DatabaseAdmin.UpdateDatabaseDdl
	//   Updates the schema of a Cloud Spanner database. Returns a long-running operation.
	public override Task<Operation> UpdateDatabaseDdl(UpdateDatabaseDdlRequest request, ServerCallContext context)
	{
		foreach (var stmt in request.Statements)
		{
			_database.ExecuteDdl(stmt);
			_ddlStatements.Add(stmt);
		}

		var now = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);

		var metadata = new UpdateDatabaseDdlMetadata
		{
			Database = request.Database,
		};
		metadata.Statements.AddRange(request.Statements);
		metadata.CommitTimestamps.AddRange(request.Statements.Select(_ => now));

		var operation = new Operation
		{
			Name = $"{request.Database}/operations/{request.OperationId ?? "_auto_ddl_" + Guid.NewGuid():N}",
			Done = true,
			Metadata = Any.Pack(metadata),
			Response = Any.Pack(new Empty()),
		};

		return Task.FromResult(operation);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.admin.database.v1#google.spanner.admin.database.v1.DatabaseAdmin.GetDatabase
	//   Gets the state of a Cloud Spanner database.
	public override Task<Database> GetDatabase(GetDatabaseRequest request, ServerCallContext context)
	{
		if (request.Name != _databaseName)
		{
			throw new RpcException(new Status(StatusCode.NotFound, $"Database not found: {request.Name}"));
		}

		var db = new Database
		{
			Name = _databaseName,
			State = Database.Types.State.Ready,
			CreateTime = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
			DatabaseDialect = DatabaseDialect.GoogleStandardSql,
		};

		return Task.FromResult(db);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.admin.database.v1#google.spanner.admin.database.v1.DatabaseAdmin.ListDatabases
	//   Lists Cloud Spanner databases.
	public override Task<ListDatabasesResponse> ListDatabases(ListDatabasesRequest request, ServerCallContext context)
	{
		var response = new ListDatabasesResponse();

		if (request.Parent == _parentName)
		{
			response.Databases.Add(new Database
			{
				Name = _databaseName,
				State = Database.Types.State.Ready,
				DatabaseDialect = DatabaseDialect.GoogleStandardSql,
			});
		}

		return Task.FromResult(response);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.admin.database.v1#google.spanner.admin.database.v1.DatabaseAdmin.DropDatabase
	//   Drops (aka deletes) a Cloud Spanner database.
	public override Task<Empty> DropDatabase(DropDatabaseRequest request, ServerCallContext context)
	{
		// In the single-database emulator, we don't actually drop the database.
		// Just acknowledge the request.
		return Task.FromResult(new Empty());
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.admin.database.v1#google.spanner.admin.database.v1.DatabaseAdmin.GetDatabaseDdl
	//   Returns the schema of a Cloud Spanner database as a list of formatted DDL statements.
	public override Task<GetDatabaseDdlResponse> GetDatabaseDdl(GetDatabaseDdlRequest request, ServerCallContext context)
	{
		if (request.Database != _databaseName)
		{
			throw new RpcException(new Status(StatusCode.NotFound, $"Database not found: {request.Database}"));
		}

		var response = new GetDatabaseDdlResponse();
		response.Statements.AddRange(DdlStatements);
		return Task.FromResult(response);
	}
}
