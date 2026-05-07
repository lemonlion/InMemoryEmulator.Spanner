using FluentAssertions;
using Google.Api.Gax;
using Google.Cloud.Spanner.Admin.Database.V1;
using Google.Cloud.Spanner.Admin.Instance.V1;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

// ╔══════════════════════════════════════════════════════════════════════════╗
// ║  Admin API Integration Tests                                           ║
// ║  Validates the Database Admin and Instance Admin gRPC services via     ║
// ║  the official Google Cloud SDK clients.                                ║
// ╚══════════════════════════════════════════════════════════════════════════╝

[Collection(IntegrationCollection.Name)]
[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
public class AdminApiIntegrationTests : IntegrationTestBase
{
	private readonly EmulatorSession _session;

	public AdminApiIntegrationTests(EmulatorSession session) : base(session)
	{
		_session = session;
	}

	// ─── Database Admin ───

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.admin.database.v1#google.spanner.admin.database.v1.DatabaseAdmin.GetDatabase
	[Fact]
	public async Task GetDatabase_ReturnsReadyState()
	{
		var client = await BuildDatabaseAdminClientAsync();
		var dbName = $"projects/{_session.ProjectId}/instances/{_session.InstanceId}/databases/{_session.DatabaseId}";

		var db = await client.GetDatabaseAsync(dbName);

		db.Should().NotBeNull();
		db.Name.Should().Be(dbName);
		db.State.Should().Be(Database.Types.State.Ready);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.admin.database.v1#google.spanner.admin.database.v1.DatabaseAdmin.ListDatabases
	[Fact]
	public async Task ListDatabases_ReturnsSingleDatabase()
	{
		var client = await BuildDatabaseAdminClientAsync();
		var parent = $"projects/{_session.ProjectId}/instances/{_session.InstanceId}";

		var response = client.ListDatabases(parent);
		var databases = response.ToList();

		databases.Should().ContainSingle();
		databases[0].State.Should().Be(Database.Types.State.Ready);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.admin.database.v1#google.spanner.admin.database.v1.DatabaseAdmin.CreateDatabase
	[Fact]
	public async Task CreateDatabase_WithExtraStatements_ExecutesDdl()
	{
		var client = await BuildDatabaseAdminClientAsync();
		var parent = $"projects/{_session.ProjectId}/instances/{_session.InstanceId}";

		var operation = await client.CreateDatabaseAsync(new CreateDatabaseRequest
		{
			Parent = parent,
			CreateStatement = $"CREATE DATABASE `{_session.DatabaseId}`",
			ExtraStatements = { "CREATE TABLE AdminTest (Id INT64 NOT NULL) PRIMARY KEY (Id)" }
		});

		// The operation should complete immediately in the emulator.
		operation.IsCompleted.Should().BeTrue();
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.admin.database.v1#google.spanner.admin.database.v1.DatabaseAdmin.UpdateDatabaseDdl
	[Fact]
	public async Task UpdateDatabaseDdl_ExecutesStatements()
	{
		var client = await BuildDatabaseAdminClientAsync();
		var dbName = $"projects/{_session.ProjectId}/instances/{_session.InstanceId}/databases/{_session.DatabaseId}";

		var operation = await client.UpdateDatabaseDdlAsync(new UpdateDatabaseDdlRequest
		{
			Database = dbName,
			Statements = { "CREATE TABLE DdlTest (K INT64 NOT NULL) PRIMARY KEY (K)" }
		});

		operation.IsCompleted.Should().BeTrue();
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.admin.database.v1#google.spanner.admin.database.v1.DatabaseAdmin.GetDatabaseDdl
	[Fact]
	public async Task GetDatabaseDdl_ReturnsTrackedStatements()
	{
		var client = await BuildDatabaseAdminClientAsync();
		var dbName = $"projects/{_session.ProjectId}/instances/{_session.InstanceId}/databases/{_session.DatabaseId}";

		// Execute some DDL first
		await client.UpdateDatabaseDdlAsync(new UpdateDatabaseDdlRequest
		{
			Database = dbName,
			Statements = { "CREATE TABLE DdlTrack (V INT64 NOT NULL) PRIMARY KEY (V)" }
		});

		var ddlResponse = await client.GetDatabaseDdlAsync(dbName);

		ddlResponse.Statements.Should().Contain("CREATE TABLE DdlTrack (V INT64 NOT NULL) PRIMARY KEY (V)");
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.admin.database.v1#google.spanner.admin.database.v1.DatabaseAdmin.DropDatabase
	[Fact]
	public async Task DropDatabase_DoesNotThrow()
	{
		var client = await BuildDatabaseAdminClientAsync();
		var dbName = $"projects/{_session.ProjectId}/instances/{_session.InstanceId}/databases/{_session.DatabaseId}";

		var act = async () => await client.DropDatabaseAsync(dbName);

		await act.Should().NotThrowAsync();
	}

	// ─── Instance Admin ───

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.admin.instance.v1#google.spanner.admin.instance.v1.InstanceAdmin.GetInstance
	[Fact]
	public async Task GetInstance_ReturnsReadyState()
	{
		var client = await BuildInstanceAdminClientAsync();
		var instanceName = $"projects/{_session.ProjectId}/instances/{_session.InstanceId}";

		var instance = await client.GetInstanceAsync(instanceName);

		instance.Should().NotBeNull();
		instance.InstanceName.Should().NotBeNull();
		instance.State.Should().Be(Instance.Types.State.Ready);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.admin.instance.v1#google.spanner.admin.instance.v1.InstanceAdmin.ListInstances
	[Fact]
	public async Task ListInstances_ReturnsSingleInstance()
	{
		var client = await BuildInstanceAdminClientAsync();
		var parent = $"projects/{_session.ProjectId}";

		var response = client.ListInstances(parent);
		var instances = response.ToList();

		instances.Should().ContainSingle();
		instances[0].State.Should().Be(Instance.Types.State.Ready);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.admin.instance.v1#google.spanner.admin.instance.v1.InstanceAdmin.CreateInstance
	[Fact]
	public async Task CreateInstance_ReturnsCompletedOperation()
	{
		var client = await BuildInstanceAdminClientAsync();
		var parent = $"projects/{_session.ProjectId}";

		var operation = await client.CreateInstanceAsync(new CreateInstanceRequest
		{
			Parent = parent,
			InstanceId = _session.InstanceId,
			Instance = new Instance
			{
				DisplayName = "Test Instance",
				NodeCount = 1,
				Config = $"projects/{_session.ProjectId}/instanceConfigs/regional-us-central1"
			}
		});

		operation.IsCompleted.Should().BeTrue();
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.admin.instance.v1#google.spanner.admin.instance.v1.InstanceAdmin.DeleteInstance
	[Fact]
	public async Task DeleteInstance_DoesNotThrow()
	{
		var client = await BuildInstanceAdminClientAsync();
		var instanceName = $"projects/{_session.ProjectId}/instances/{_session.InstanceId}";

		var act = async () => await client.DeleteInstanceAsync(instanceName);

		await act.Should().NotThrowAsync();
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.admin.instance.v1#google.spanner.admin.instance.v1.InstanceAdmin.DeleteInstance
	//   "Returns NOT_FOUND if the instance does not exist."
	[Fact]
	public async Task DeleteInstance_NotFound_ThrowsNotFound()
	{
		var client = await BuildInstanceAdminClientAsync();
		var instanceName = $"projects/{_session.ProjectId}/instances/nonexistent-instance";

		var act = async () => await client.DeleteInstanceAsync(instanceName);

		var ex = await act.Should().ThrowAsync<Grpc.Core.RpcException>();
		ex.Which.StatusCode.Should().Be(Grpc.Core.StatusCode.NotFound);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.admin.instance.v1#google.spanner.admin.instance.v1.InstanceAdmin.UpdateInstance
	//   "Returns NOT_FOUND if the instance does not exist."
	[Fact]
	public async Task UpdateInstance_NotFound_ThrowsNotFound()
	{
		var client = await BuildInstanceAdminClientAsync();

		var act = async () => await client.UpdateInstanceAsync(new UpdateInstanceRequest
		{
			Instance = new Instance
			{
				Name = $"projects/{_session.ProjectId}/instances/nonexistent-instance",
				DisplayName = "Updated",
			},
			FieldMask = new Google.Protobuf.WellKnownTypes.FieldMask { Paths = { "display_name" } }
		});

		var ex = await act.Should().ThrowAsync<Grpc.Core.RpcException>();
		ex.Which.StatusCode.Should().Be(Grpc.Core.StatusCode.NotFound);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.admin.instance.v1#google.spanner.admin.instance.v1.InstanceAdmin.GetInstanceConfig
	//   "Gets information about a particular instance configuration."
	[Fact]
	public async Task GetInstanceConfig_ReturnsConfig()
	{
		var client = await BuildInstanceAdminClientAsync();
		var configName = $"projects/{_session.ProjectId}/instanceConfigs/emulator-config";

		var config = await client.GetInstanceConfigAsync(configName);

		config.Should().NotBeNull();
		config.Name.Should().Be(configName);
		config.DisplayName.Should().NotBeNullOrEmpty();
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.admin.instance.v1#google.spanner.admin.instance.v1.InstanceAdmin.ListInstanceConfigs
	//   "Lists the supported instance configurations for a given project."
	[Fact]
	public async Task ListInstanceConfigs_ReturnsAtLeastOneConfig()
	{
		var client = await BuildInstanceAdminClientAsync();
		var parent = $"projects/{_session.ProjectId}";

		var response = client.ListInstanceConfigs(parent);
		var configs = response.ToList();

		configs.Should().NotBeEmpty();
		configs[0].Name.Should().StartWith($"projects/{_session.ProjectId}/instanceConfigs/");
	}

	// ─── Helpers ───

	private async Task<DatabaseAdminClient> BuildDatabaseAdminClientAsync()
	{
		var builder = new DatabaseAdminClientBuilder();
		if (_session.Target != SpannerTestTarget.CloudSpanner)
			builder.EmulatorDetection = EmulatorDetection.EmulatorOnly;
		return await builder.BuildAsync();
	}

	private async Task<InstanceAdminClient> BuildInstanceAdminClientAsync()
	{
		var builder = new InstanceAdminClientBuilder();
		if (_session.Target != SpannerTestTarget.CloudSpanner)
			builder.EmulatorDetection = EmulatorDetection.EmulatorOnly;
		return await builder.BuildAsync();
	}
}
