using System.Net.WebSockets;
using Microsoft.AspNetCore.Http;

namespace Kgs.Server.Transport.Sessions;

public interface ISessionManager
{
    Task AcceptAsync(
        HttpContext context,
        WebSocket webSocket,
        CancellationToken cancellationToken);
}
