using FluentAssertions;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;

namespace Spanner.InMemoryEmulator.Tests.Integration;

[Collection(IntegrationCollection.Name)]
public class ConnectionTests
{
	private readonly ITestDatabaseFixture _fixture;

	public ConnectionTests(EmulatorSession session)
	{
		_fixture = TestFixtureFactory.Create(session);
	}

	[Fact]
	public async Task OpenAsync_Succeeds()
	{
		// Arrange
		using var connection = _fixture.CreateConnection();

		// Act
		await connection.OpenAsync();

		// Assert — no exception means success
		connection.State.Should().Be(System.Data.ConnectionState.Open);
	}
}
