using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;

/// <summary>
/// Creates test fixtures appropriate for the current test target.
/// </summary>
public static class TestFixtureFactory
{
	public static ITestDatabaseFixture Create(EmulatorSession session)
	{
		return session.Target switch
		{
			SpannerTestTarget.InMemory => new InMemoryTestFixture(session),
			SpannerTestTarget.Emulator => new EmulatorTestFixture(session),
			SpannerTestTarget.CloudSpanner => new CloudSpannerTestFixture(session),
			_ => throw new NotSupportedException($"Test target '{session.Target}' is not supported by TestFixtureFactory.")
		};
	}
}

internal class InMemoryTestFixture : ITestDatabaseFixture
{
	private readonly EmulatorSession _session;

	public InMemoryTestFixture(EmulatorSession session)
	{
		_session = session;
	}

	public InMemorySpannerDatabase? Database => _session.Database;
	public FakeSpannerServer? Server => _session.Server;

	public SpannerConnection CreateConnection()
	{
		return _session.Server!.CreateConnection();
	}
}

internal class EmulatorTestFixture : ITestDatabaseFixture
{
	private readonly EmulatorSession _session;

	public EmulatorTestFixture(EmulatorSession session)
	{
		_session = session;
	}

	public InMemorySpannerDatabase? Database => null;
	public FakeSpannerServer? Server => null;

	public SpannerConnection CreateConnection()
	{
		// Ref: https://cloud.google.com/spanner/docs/emulator#using_the_emulator
		//   "Set the SPANNER_EMULATOR_HOST environment variable."
		var connectionStringBuilder = new SpannerConnectionStringBuilder
		{
			DataSource = $"projects/{_session.ProjectId}/instances/{_session.InstanceId}/databases/{_session.DatabaseId}",
			EmulatorDetection = Google.Api.Gax.EmulatorDetection.EmulatorOnly
		};

		return new SpannerConnection(connectionStringBuilder);
	}
}

internal class CloudSpannerTestFixture : ITestDatabaseFixture
{
	private readonly EmulatorSession _session;

	public CloudSpannerTestFixture(EmulatorSession session)
	{
		_session = session;
	}

	public InMemorySpannerDatabase? Database => null;
	public FakeSpannerServer? Server => null;

	public SpannerConnection CreateConnection()
	{
		// Ref: https://cloud.google.com/spanner/docs/getting-started/dotnet
		//   Real Cloud Spanner uses Application Default Credentials (no EmulatorDetection).
		var connectionStringBuilder = new SpannerConnectionStringBuilder
		{
			DataSource = $"projects/{_session.ProjectId}/instances/{_session.InstanceId}/databases/{_session.DatabaseId}"
		};

		return new SpannerConnection(connectionStringBuilder);
	}
}
