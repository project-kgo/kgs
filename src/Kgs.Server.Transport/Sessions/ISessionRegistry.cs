namespace Kgs.Server.Transport.Sessions;

public interface ISessionRegistry
{
    int Count { get; }

    bool TryGet(Guid sessionId, out ISessionSender sender);
}
