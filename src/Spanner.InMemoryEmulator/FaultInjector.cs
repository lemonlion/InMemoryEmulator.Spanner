namespace Spanner.InMemoryEmulator;

/// <summary>
/// Placeholder for fault injection support.
/// Enables testing retry policies, abort handling, and timeout scenarios.
/// </summary>
public class FaultInjector
{
	// TODO: Implement structured fault injection (per-RPC targeting, rate-based, count-based)
	// Currently, fault injection is handled via the FaultInjector delegate on FakeSpannerService.
}
