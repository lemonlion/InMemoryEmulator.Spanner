using InMemoryEmulator.Spanner.Tests.Shared.Infrastructure;
using Xunit;

namespace InMemoryEmulator.Spanner.Tests.Integration;

/// <summary>
/// xUnit collection definition for integration tests that share an emulator session.
/// Must be in the same assembly as the test classes that use it.
/// </summary>
[CollectionDefinition(Name)]
public class IntegrationCollection : ICollectionFixture<EmulatorSession>
{
	public const string Name = Shared.Infrastructure.IntegrationCollection.Name;
}
