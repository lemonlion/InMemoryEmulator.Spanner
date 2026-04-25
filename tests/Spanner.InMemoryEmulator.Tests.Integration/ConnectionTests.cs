using FluentAssertions;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;

namespace Spanner.InMemoryEmulator.Tests.Integration;

[Collection(IntegrationCollection.Name)]
public class ConnectionTests : IntegrationTestBase
{
	public ConnectionTests(EmulatorSession session) : base(session) { }

	[Fact]
	public async Task OpenAsync_Succeeds()
	{
		using var connection = Fixture.CreateConnection();
		await connection.OpenAsync();
		connection.State.Should().Be(System.Data.ConnectionState.Open);
	}
}
