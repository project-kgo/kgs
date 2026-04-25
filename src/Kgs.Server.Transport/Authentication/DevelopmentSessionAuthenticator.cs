using System.Text;
using Kgs.Game.Contracts;

namespace Kgs.Server.Transport.Authentication;

public sealed class DevelopmentSessionAuthenticator : ISessionAuthenticator
{
    public ValueTask<SessionAuthenticationResult> AuthenticateAsync(
        GameSessionContext context,
        GamePacket packet,
        CancellationToken cancellationToken)
    {
        var token = Encoding.UTF8.GetString(packet.Payload.Span);
        if (string.IsNullOrWhiteSpace(token))
        {
            return ValueTask.FromResult(SessionAuthenticationResult.Fail("Empty auth token."));
        }

        return ValueTask.FromResult(SessionAuthenticationResult.Success(context.SessionId));
    }
}
