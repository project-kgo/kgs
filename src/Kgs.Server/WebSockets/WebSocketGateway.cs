using System.Net.WebSockets;
using Kgs.Server.Transport.Sessions;

namespace Kgs.Server.WebSockets;

public sealed class WebSocketGateway
{
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<WebSocketGateway> _logger;

    public WebSocketGateway(
        ISessionManager sessionManager,
        ILogger<WebSocketGateway> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
    }

    public async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();

        try
        {
            await _sessionManager.AcceptAsync(context, webSocket, context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            _logger.LogDebug("WebSocket request aborted.");
        }
        catch (WebSocketException ex)
        {
            _logger.LogWarning(ex, "WebSocket transport failed.");
        }
    }
}
