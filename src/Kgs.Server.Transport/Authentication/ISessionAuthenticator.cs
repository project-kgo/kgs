using Kgs.Game.Contracts;

namespace Kgs.Server.Transport.Authentication;

public interface ISessionAuthenticator
{
    ValueTask<SessionAuthenticationResult> AuthenticateAsync(
        GameSessionContext context,
        GamePacket packet,
        CancellationToken cancellationToken);
}
