using System.Collections.Concurrent;
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

	/// <summary>
	/// Tracks DDL statements that have already been executed in this session.
	/// Used to skip duplicate CREATE TABLE/INDEX calls on Cloud Spanner/Emulator
	/// where DDL is rate-limited and expensive.
	/// </summary>
	internal ConcurrentDictionary<string, byte> ExecutedDdl { get; } = new(StringComparer.OrdinalIgnoreCase);

	public SpannerTestTarget Target { get; private set; }

	/// <summary>The fake server (only set when Target == InMemory).</summary>
	public FakeSpannerServer? Server => _server;

	/// <summary>The backing database (only set when Target == InMemory).</summary>
	public InMemorySpannerDatabase? Database => _server?.Database;

	/// <summary>Project ID for connection strings.</summary>
	public string ProjectId { get; private set; } = "test-project";

	/// <summary>Instance ID for connection strings.</summary>
	public string InstanceId { get; private set; } = "test-instance";

	/// <summary>Database ID for connection strings.</summary>
	public string DatabaseId { get; private set; } = "test-db";

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

		if (Target == SpannerTestTarget.CloudSpanner)
		{
			// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.admin.database.v1
			//   CreateDatabase / DropDatabase on real Cloud Spanner via Application Default Credentials.
			ProjectId = Environment.GetEnvironmentVariable("GCP_PROJECT_ID") ?? ProjectId;
			InstanceId = Environment.GetEnvironmentVariable("SPANNER_INSTANCE_ID") ?? InstanceId;
			DatabaseId = Environment.GetEnvironmentVariable("SPANNER_DATABASE_ID") ?? DatabaseId;

			var adminClient = await new DatabaseAdminClientBuilder().BuildAsync();
			var parent = $"projects/{ProjectId}/instances/{InstanceId}";

			await adminClient.CreateDatabaseAsync(new CreateDatabaseRequest
			{
				Parent = parent,
				CreateStatement = $"CREATE DATABASE `{DatabaseId}`"
			});
		}
	}

	public async Task DisposeAsync()
	{
		if (Target == SpannerTestTarget.CloudSpanner)
		{
			// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.admin.database.v1
			//   Drop the ephemeral database to avoid leaving resources behind.
			try
			{
				var adminClient = await new DatabaseAdminClientBuilder().BuildAsync();
				var databaseName = $"projects/{ProjectId}/instances/{InstanceId}/databases/{DatabaseId}";
				await adminClient.DropDatabaseAsync(databaseName);
			}
			catch
			{
				// Best-effort cleanup — the workflow also has a cleanup step.
			}
		}

		if (_server != null)
		{
			Environment.SetEnvironmentVariable("SPANNER_EMULATOR_HOST", null);
			await _server.DisposeAsync();
		}
	}
}
