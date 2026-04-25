namespace Spanner.InMemoryEmulator;

/// <summary>
/// Options for configuring the in-memory Spanner emulator via DI.
/// </summary>
public class InMemorySpannerOptions
{
	public string ProjectId { get; set; } = "test-project";
	public string InstanceId { get; set; } = "test-instance";
	public string DatabaseId { get; set; } = "test-db";

	/// <summary>Called after the server is created but before it starts.</summary>
	public Action<FakeSpannerServer>? OnServerCreated { get; set; }

	/// <summary>Called after the database is created. Use for schema + seed data.</summary>
	public Action<InMemorySpannerDatabase>? OnDatabaseCreated { get; set; }

	/// <summary>Auto-persist state to this directory.</summary>
	public string? StatePersistenceDirectory { get; set; }

	/// <summary>Additional databases to create (multi-database scenarios).</summary>
	public List<AdditionalDatabase> AdditionalDatabases { get; } = new();
}

/// <summary>
/// Defines an additional database to be created in the fake Spanner server.
/// </summary>
public class AdditionalDatabase
{
	public string DatabaseId { get; set; } = null!;
	public Action<InMemorySpannerDatabase>? OnDatabaseCreated { get; set; }
}
