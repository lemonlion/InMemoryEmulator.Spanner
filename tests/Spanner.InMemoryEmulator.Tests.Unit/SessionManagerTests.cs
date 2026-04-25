using FluentAssertions;

namespace Spanner.InMemoryEmulator.Tests.Unit;

public class SessionManagerTests
{
	[Fact]
	public void CreateSession_ReturnsSessionWithValidName()
	{
		// Arrange
		var manager = new SessionManager();
		var database = "projects/test-project/instances/test-instance/databases/test-db";

		// Act
		var session = manager.CreateSession(database);

		// Assert
		session.Name.Should().StartWith($"{database}/sessions/");
	}

	[Fact]
	public void BatchCreateSessions_ReturnsRequestedCount()
	{
		// Arrange
		var manager = new SessionManager();
		var database = "projects/test-project/instances/test-instance/databases/test-db";

		// Act
		var sessions = manager.BatchCreateSessions(database, 5);

		// Assert
		sessions.Should().HaveCount(5);
		sessions.Select(s => s.Name).Should().OnlyHaveUniqueItems();
	}

	[Fact]
	public void DeleteSession_RemovesSession()
	{
		// Arrange
		var manager = new SessionManager();
		var database = "projects/test-project/instances/test-instance/databases/test-db";
		var session = manager.CreateSession(database);

		// Act
		manager.DeleteSession(session.Name);

		// Assert
		manager.GetSession(session.Name).Should().BeNull();
	}
}
