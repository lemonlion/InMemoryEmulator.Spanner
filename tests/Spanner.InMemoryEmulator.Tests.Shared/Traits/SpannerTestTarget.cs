namespace Spanner.InMemoryEmulator.Tests.Shared.Traits;

/// <summary>
/// Identifies the Spanner backend to test against.
/// Set via the <c>SPANNER_TEST_TARGET</c> environment variable.
/// </summary>
public enum SpannerTestTarget
{
	/// <summary>In-process fake (default).</summary>
	InMemory,

	/// <summary>Docker-hosted Go Spanner emulator.</summary>
	Emulator,

	/// <summary>Real Google Cloud Spanner instance.</summary>
	CloudSpanner
}

public static class SpannerTestTargetHelper
{
	public static SpannerTestTarget Current
	{
		get
		{
			var env = Environment.GetEnvironmentVariable("SPANNER_TEST_TARGET");
			return env?.ToLowerInvariant() switch
			{
				"emulator" => SpannerTestTarget.Emulator,
				"cloudspanner" or "cloud" => SpannerTestTarget.CloudSpanner,
				_ => SpannerTestTarget.InMemory
			};
		}
	}
}
