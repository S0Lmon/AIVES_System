namespace AIVES.WebRazor.Realtime;

/// <summary>
/// Who is connected and who has which question open for editing. Kept in memory, so it is
/// correct for one web instance; several instances would need a shared store and a SignalR
/// backplane (for example Redis).
/// </summary>
public sealed class PresenceTracker
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, Connection> connections = new();

    private sealed record Connection(string UserId, string Name)
    {
        public HashSet<int> EditingQuestions { get; } = [];
    }

    public void Connect(string connectionId, string userId, string name)
    {
        lock (gate)
            connections[connectionId] = new Connection(userId, name);
    }

    /// <summary>Forgets the connection and returns the questions it was editing.</summary>
    public IReadOnlyList<int> Disconnect(string connectionId)
    {
        lock (gate)
        {
            if (!connections.Remove(connectionId, out var connection))
                return [];
            return connection.EditingQuestions.ToList();
        }
    }

    public void StartEditing(string connectionId, int questionId)
    {
        lock (gate)
        {
            if (connections.TryGetValue(connectionId, out var connection))
                connection.EditingQuestions.Add(questionId);
        }
    }

    public bool StopEditing(string connectionId, int questionId)
    {
        lock (gate)
            return connections.TryGetValue(connectionId, out var connection) && connection.EditingQuestions.Remove(questionId);
    }

    /// <summary>One entry per user however many tabs they have open.</summary>
    public IReadOnlyList<OnlineUser> OnlineUsers()
    {
        lock (gate)
        {
            return connections.Values
                .GroupBy(connection => connection.UserId)
                .Select(group => new OnlineUser(group.Key, group.First().Name, group.Count()))
                .OrderBy(user => user.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    /// <summary>Distinct names of the users who have this question's edit page open.</summary>
    public IReadOnlyList<string> Editors(int questionId)
    {
        lock (gate)
        {
            return connections.Values
                .Where(connection => connection.EditingQuestions.Contains(questionId))
                .Select(connection => connection.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
