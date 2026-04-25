namespace Spanner.InMemoryEmulator;

/// <summary>
/// Options for configuring a <see cref="FakeSpannerServer"/>.
/// </summary>
public class FakeSpannerServerOptions
{
	public string ProjectId { get; init; } = "test-project";
	public string InstanceId { get; init; } = "test-instance";
	public string DatabaseId { get; init; } = "test-db";
}
