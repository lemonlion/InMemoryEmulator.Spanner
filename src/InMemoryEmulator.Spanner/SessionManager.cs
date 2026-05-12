using System.Collections.Concurrent;
using Google.Cloud.Spanner.V1;

namespace InMemoryEmulator.Spanner;

/// <summary>
/// Manages in-memory sessions for the fake Spanner service.
/// </summary>
internal class SessionManager
{
	private readonly ConcurrentDictionary<string, SessionState> _sessions = new();

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.CreateSession
	//   "Creates a new session."
	public Session CreateSession(string database, bool multiplexed = false)
	{
		var session = new Session
		{
			Name = $"{database}/sessions/{Guid.NewGuid()}",
			Multiplexed = multiplexed,
			CreateTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
			ApproximateLastUseTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow)
		};
		_sessions[session.Name] = new SessionState(session, multiplexed);
		return session;
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.BatchCreateSessions
	//   "Creates multiple new sessions."
	public List<Session> BatchCreateSessions(string database, int count, bool multiplexed = false)
	{
		var sessions = new List<Session>(count);
		for (var i = 0; i < count; i++)
		{
			sessions.Add(CreateSession(database, multiplexed));
		}
		return sessions;
	}

	public bool TryGetSession(string name, out SessionState? session)
	{
		return _sessions.TryGetValue(name, out session);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.GetSession
	//   "Gets a session. Returns NOT_FOUND if the session does not exist."
	public Session? GetSession(string name)
	{
		return _sessions.TryGetValue(name, out var state) ? state.Session : null;
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.v1#google.spanner.v1.Spanner.DeleteSession
	//   "Ends a session, releasing server resources associated with it."
	public void DeleteSession(string name)
	{
		_sessions.TryRemove(name, out _);
	}

	public IReadOnlyList<Session> ListSessions(string database)
	{
		return _sessions.Values
			.Where(s => s.Session.Name.StartsWith(database, StringComparison.Ordinal))
			.Select(s => s.Session)
			.ToList();
	}

	public void UpdateLastUsed(string name)
	{
		if (_sessions.TryGetValue(name, out var state))
		{
			state.LastUsedAt = DateTimeOffset.UtcNow;
			state.Session.ApproximateLastUseTime =
				Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);
		}
	}
}

internal class SessionState
{
	public Session Session { get; }
	public DateTimeOffset CreatedAt { get; }
	public DateTimeOffset LastUsedAt { get; set; }
	public bool IsMultiplexed { get; }

	public SessionState(Session session, bool isMultiplexed)
	{
		Session = session;
		IsMultiplexed = isMultiplexed;
		CreatedAt = DateTimeOffset.UtcNow;
		LastUsedAt = DateTimeOffset.UtcNow;
	}
}
