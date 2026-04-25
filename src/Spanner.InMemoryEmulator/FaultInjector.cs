using Google.Protobuf;
using Grpc.Core;

namespace Spanner.InMemoryEmulator;

/// <summary>
/// Factory methods for building fault injection delegates for <see cref="FakeSpannerService"/>.
/// </summary>
public static class FaultInjector
{
	/// <summary>
	/// Creates a fault injector that always throws the given status code.
	/// </summary>
	public static Func<string, IMessage, RpcException?> Always(StatusCode code, string message = "Injected fault")
	{
		return (_, _) => new RpcException(new Status(code, message));
	}

	/// <summary>
	/// Creates a fault injector that throws only for a specific RPC method name.
	/// </summary>
	public static Func<string, IMessage, RpcException?> ForMethod(
		string methodName, StatusCode code, string message = "Injected fault")
	{
		return (method, _) =>
			string.Equals(method, methodName, StringComparison.OrdinalIgnoreCase)
				? new RpcException(new Status(code, message))
				: null;
	}

	/// <summary>
	/// Creates a fault injector that throws for the first N calls, then succeeds.
	/// </summary>
	public static Func<string, IMessage, RpcException?> NTimes(
		int count, StatusCode code, string message = "Injected fault")
	{
		var remaining = count;
		return (_, _) =>
		{
			if (Interlocked.Decrement(ref remaining) >= 0)
				return new RpcException(new Status(code, message));
			return null;
		};
	}

	/// <summary>
	/// Creates a fault injector that throws for the first N calls to a specific method.
	/// </summary>
	public static Func<string, IMessage, RpcException?> ForMethodNTimes(
		string methodName, int count, StatusCode code, string message = "Injected fault")
	{
		var remaining = count;
		return (method, _) =>
		{
			if (!string.Equals(method, methodName, StringComparison.OrdinalIgnoreCase))
				return null;
			if (Interlocked.Decrement(ref remaining) >= 0)
				return new RpcException(new Status(code, message));
			return null;
		};
	}
}
