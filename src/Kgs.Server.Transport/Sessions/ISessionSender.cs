using Kgs.Game.Contracts;

namespace Kgs.Server.Transport.Sessions;

public interface ISessionSender
{
    Guid SessionId { get; }

    ValueTask EnqueueAsync(GamePacket packet, CancellationToken cancellationToken);
}
