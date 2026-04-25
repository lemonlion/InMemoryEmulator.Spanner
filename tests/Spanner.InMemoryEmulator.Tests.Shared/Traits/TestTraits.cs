namespace Spanner.InMemoryEmulator.Tests.Shared.Traits;

/// <summary>
/// Constants for xUnit test trait names and values.
/// </summary>
public static class TestTraits
{
	/// <summary>Trait key for test target (InMemory, Emulator, CloudSpanner).</summary>
	public const string Target = "Target";

	/// <summary>Trait key for feature category (DDL, DML, Query, etc.).</summary>
	public const string Category = "Category";

	/// <summary>Trait value: test only runs against in-memory emulator (not Docker/Cloud).</summary>
	public const string InMemoryOnly = "InMemoryOnly";

	/// <summary>Trait value: test only runs against Docker emulator or Cloud Spanner.</summary>
	public const string EmulatorOnly = "EmulatorOnly";
}
