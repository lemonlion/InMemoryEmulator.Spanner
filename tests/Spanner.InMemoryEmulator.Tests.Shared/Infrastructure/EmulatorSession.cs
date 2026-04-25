using Google.Api.Gax;
using Google.Cloud.Spanner.Admin.Database.V1;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;
using Xunit;

namespace Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;

/// <summary>
/// xUnit collection fixture that manages the lifecycle of a Spanner emulator session.
/// Starts a <see cref="FakeSpannerServer"/> for in-memory tests,
/// or connects to a Docker emulator / Cloud Spanner based on <c>SPANNER_TEST_TARGET</c>.
/// </summary>
public class EmulatorSession : IAsyncLifetime
{
	private FakeSpannerServer? _server;

	public SpannerTestTarget Target { get; private set; }

	/// <summary>The fake server (only set when Target == InMemory).</summary>
	public FakeSpannerServer? Server => _server;

	/// <summary>The backing database (only set when Target == InMemory).</summary>
	public InMemorySpannerDatabase? Database => _server?.Database;

	/// <summary>Project ID for connection strings.</summary>
	public string ProjectId { get; } = "test-project";

	/// <summary>Instance ID for connection strings.</summary>
	public string InstanceId { get; } = "test-instance";

	/// <summary>Database ID for connection strings.</summary>
	public string DatabaseId { get; } = "test-db";

	public async Task InitializeAsync()
	{
		Target = SpannerTestTargetHelper.Current;

		if (Target == SpannerTestTarget.InMemory)
		{
			var options = new FakeSpannerServerOptions
			{
				ProjectId = ProjectId,
				InstanceId = InstanceId,
				DatabaseId = DatabaseId
			};
			_server = new FakeSpannerServer(options);
			await _server.StartAsync();

			// Ref: https://cloud.google.com/spanner/docs/emulator#using_the_emulator
			//   The Spanner SDK uses SPANNER_EMULATOR_HOST to connect to the emulator.
			Environment.SetEnvironmentVariable("SPANNER_EMULATOR_HOST", $"localhost:{_server.Port}");
		}
		// For Emulator / CloudSpanner, connection is established via SPANNER_EMULATOR_HOST
		// or standard Spanner connection settings.
		// Reset the database to ensure a clean slate (tables persist across test runs).
		if (Target == SpannerTestTarget.Emulator)
		{
			// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.admin.database.v1
			//   DropDatabase + CreateDatabase ensures no stale tables from previous runs.
			var adminClientBuilder = new DatabaseAdminClientBuilder
			{
				EmulatorDetection = EmulatorDetection.EmulatorOnly
			};
			var adminClient = await adminClientBuilder.BuildAsync();
			var databaseName = $"projects/{ProjectId}/instances/{InstanceId}/databases/{DatabaseId}";

			try { await adminClient.DropDatabaseAsync(databaseName); } catch { }

			await adminClient.CreateDatabaseAsync(new CreateDatabaseRequest
			{
				Parent = $"projects/{ProjectId}/instances/{InstanceId}",
				CreateStatement = $"CREATE DATABASE `{DatabaseId}`"
			});
		}
	}

	public async Task DisposeAsync()
	{
		if (_server != null)
		{
			Environment.SetEnvironmentVariable("SPANNER_EMULATOR_HOST", null);
			await _server.DisposeAsync();
		}
	}
}
