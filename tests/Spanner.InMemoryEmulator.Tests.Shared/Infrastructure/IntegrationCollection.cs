using Xunit;

namespace Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;

/// <summary>
/// xUnit collection definition for integration tests that share an emulator session.
/// </summary>
[CollectionDefinition(Name)]
public class IntegrationCollection : ICollectionFixture<EmulatorSession>
{
	public const string Name = "Integration";
}
