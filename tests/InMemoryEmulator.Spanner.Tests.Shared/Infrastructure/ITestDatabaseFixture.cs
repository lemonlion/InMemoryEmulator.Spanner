namespace InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;

/// <summary>
/// Interface for a test fixture that provides access to a Spanner database.
/// </summary>
public interface ITestDatabaseFixture
{
	/// <summary>The backing in-memory database (for InMemory target).</summary>
	InMemorySpannerDatabase? Database { get; }

	/// <summary>The fake server (for InMemory target).</summary>
	FakeSpannerServer? Server { get; }

	/// <summary>Creates a SpannerConnection suitable for the current test target.</summary>
	Google.Cloud.Spanner.Data.SpannerConnection CreateConnection();
}
