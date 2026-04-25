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
		// or standard Spanner connection settings
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
