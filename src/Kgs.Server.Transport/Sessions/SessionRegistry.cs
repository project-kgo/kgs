using System.Collections.Concurrent;

namespace Kgs.Server.Transport.Sessions;

public sealed class SessionRegistry : ISessionRegistry
{
    private readonly ConcurrentDictionary<Guid, ISessionSender> _sessions = new();

    public int Count => _sessions.Count;

    public bool TryGet(Guid sessionId, out ISessionSender sender)
    {
        return _sessions.TryGetValue(sessionId, out sender!);
    }

    internal void Add(ISessionSender sender)
    {
        _sessions[sender.SessionId] = sender;
    }

    internal void Remove(Guid sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
    }
}
