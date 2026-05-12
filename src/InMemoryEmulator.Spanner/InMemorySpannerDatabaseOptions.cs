namespace InMemoryEmulator.Spanner;

/// <summary>
/// Options for configuring an <see cref="InMemorySpannerDatabase"/>.
/// </summary>
public class InMemorySpannerDatabaseOptions
{
	/// <summary>Auto-save state to this directory on Dispose, auto-load on create.</summary>
	public string? StatePersistenceDirectory { get; set; }

	/// <summary>File path for explicit state persistence.</summary>
	public string? StateFilePath { get; set; }
}
