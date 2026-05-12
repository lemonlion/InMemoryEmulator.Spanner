using FluentAssertions;
using Google.Cloud.Spanner.V1;
using Google.Protobuf;
using Grpc.Core;

namespace InMemoryEmulator.Spanner.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="FaultInjector"/> factory methods.
/// </summary>
public class FaultInjectorTests
{
	// ─── Always ───

	[Fact]
	public void Always_ReturnsFaultForEveryCall()
	{
		var injector = FaultInjector.Always(StatusCode.Unavailable);

		var result1 = injector("CreateSession", new CreateSessionRequest());
		var result2 = injector("ExecuteSql", new ExecuteSqlRequest());

		result1.Should().NotBeNull();
		result1!.StatusCode.Should().Be(StatusCode.Unavailable);
		result2.Should().NotBeNull();
		result2!.StatusCode.Should().Be(StatusCode.Unavailable);
	}

	[Fact]
	public void Always_UsesCustomMessage()
	{
		var injector = FaultInjector.Always(StatusCode.Aborted, "custom error");

		var result = injector("Commit", new CommitRequest());

		result.Should().NotBeNull();
		result!.Status.Detail.Should().Be("custom error");
	}

	[Fact]
	public void Always_SupportsDeadlineExceeded()
	{
		var injector = FaultInjector.Always(StatusCode.DeadlineExceeded);

		var result = injector("ExecuteSql", new ExecuteSqlRequest());

		result.Should().NotBeNull();
		result!.StatusCode.Should().Be(StatusCode.DeadlineExceeded);
	}

	// ─── ForMethod ───

	[Fact]
	public void ForMethod_ReturnsFaultOnlyForTargetedMethod()
	{
		var injector = FaultInjector.ForMethod("Commit", StatusCode.Aborted);

		var commitFault = injector("Commit", new CommitRequest());
		var queryFault = injector("ExecuteSql", new ExecuteSqlRequest());

		commitFault.Should().NotBeNull();
		commitFault!.StatusCode.Should().Be(StatusCode.Aborted);
		queryFault.Should().BeNull();
	}

	[Fact]
	public void ForMethod_IsCaseInsensitive()
	{
		var injector = FaultInjector.ForMethod("commit", StatusCode.Aborted);

		var result = injector("Commit", new CommitRequest());

		result.Should().NotBeNull();
	}

	[Fact]
	public void ForMethod_ReturnsNullForNonMatchingMethods()
	{
		var injector = FaultInjector.ForMethod("Read", StatusCode.Unavailable);

		var result1 = injector("CreateSession", new CreateSessionRequest());
		var result2 = injector("ExecuteSql", new ExecuteSqlRequest());
		var result3 = injector("Commit", new CommitRequest());

		result1.Should().BeNull();
		result2.Should().BeNull();
		result3.Should().BeNull();
	}

	// ─── NTimes ───

	[Fact]
	public void NTimes_ReturnsFaultForFirstNCalls_ThenNull()
	{
		var injector = FaultInjector.NTimes(2, StatusCode.Unavailable);

		var call1 = injector("Any", new CommitRequest());
		var call2 = injector("Any", new CommitRequest());
		var call3 = injector("Any", new CommitRequest());
		var call4 = injector("Any", new CommitRequest());

		call1.Should().NotBeNull();
		call2.Should().NotBeNull();
		call3.Should().BeNull();
		call4.Should().BeNull();
	}

	[Fact]
	public void NTimes_WithCountZero_NeverFaults()
	{
		var injector = FaultInjector.NTimes(0, StatusCode.Unavailable);

		var result = injector("Any", new CommitRequest());

		result.Should().BeNull();
	}

	[Fact]
	public void NTimes_WithCountOne_FaultsExactlyOnce()
	{
		var injector = FaultInjector.NTimes(1, StatusCode.Aborted);

		var first = injector("Commit", new CommitRequest());
		var second = injector("Commit", new CommitRequest());

		first.Should().NotBeNull();
		first!.StatusCode.Should().Be(StatusCode.Aborted);
		second.Should().BeNull();
	}

	// ─── ForMethodNTimes ───

	[Fact]
	public void ForMethodNTimes_FaultsTargetMethodNTimes_ThenSucceeds()
	{
		var injector = FaultInjector.ForMethodNTimes("Commit", 2, StatusCode.Aborted);

		var c1 = injector("Commit", new CommitRequest());
		var c2 = injector("Commit", new CommitRequest());
		var c3 = injector("Commit", new CommitRequest());

		c1.Should().NotBeNull();
		c2.Should().NotBeNull();
		c3.Should().BeNull();
	}

	[Fact]
	public void ForMethodNTimes_DoesNotFaultOtherMethods()
	{
		var injector = FaultInjector.ForMethodNTimes("Commit", 10, StatusCode.Aborted);

		var queryResult = injector("ExecuteSql", new ExecuteSqlRequest());

		queryResult.Should().BeNull();
	}

	[Fact]
	public void ForMethodNTimes_CountAppliesToTargetMethodOnly()
	{
		var injector = FaultInjector.ForMethodNTimes("Commit", 1, StatusCode.Aborted);

		// Non-matching calls don't consume the count
		injector("ExecuteSql", new ExecuteSqlRequest()).Should().BeNull();
		injector("Read", new ReadRequest()).Should().BeNull();

		// First matching call faults
		injector("Commit", new CommitRequest()).Should().NotBeNull();

		// Second matching call succeeds (count exhausted)
		injector("Commit", new CommitRequest()).Should().BeNull();
	}

	// ─── Thread safety ───

	[Fact]
	public void NTimes_IsThreadSafe()
	{
		const int faultCount = 100;
		var injector = FaultInjector.NTimes(faultCount, StatusCode.Unavailable);

		var results = new RpcException?[200];
		Parallel.For(0, 200, i =>
		{
			results[i] = injector("Any", new CommitRequest());
		});

		results.Count(r => r != null).Should().Be(faultCount);
		results.Count(r => r == null).Should().Be(200 - faultCount);
	}
}
