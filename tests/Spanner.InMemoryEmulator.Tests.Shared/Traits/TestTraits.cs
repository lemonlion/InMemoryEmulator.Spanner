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

	/// <summary>Trait value: test only runs against in-memory emulator (not Docker/Cloud).
	/// Used for tests that require internal APIs (e.g. FakeSpannerService, Database direct access).</summary>
	public const string InMemoryOnly = "InMemoryOnly";

	/// <summary>Trait value: test only runs against Docker emulator or Cloud Spanner.</summary>
	public const string EmulatorOnly = "EmulatorOnly";

	/// <summary>Trait value: test only runs against real GCP Spanner (not Docker emulator).
	/// Used for features the Go emulator doesn't support correctly.</summary>
	public const string GcpOnly = "GcpOnly";

	/// <summary>Trait value: test is skipped against the Go Spanner emulator due to known gaps
	/// (e.g. unsupported functions, missing analytic support). These features work on both
	/// the in-memory emulator and real GCP Spanner.</summary>
	public const string GoEmulatorUnsupported = "GoEmulatorUnsupported";
}
