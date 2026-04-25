using System.Net.WebSockets;
using Kgs.Game.Contracts;
using Kgs.Server.Transport.Authentication;
using Kgs.Server.Transport.Configuration;
using Kgs.Server.Transport.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kgs.Server.Transport.Sessions;

public sealed class SessionManager : ISessionManager
{
    private readonly SessionRegistry _registry;
    private readonly IPacketDispatcher _dispatcher;
    private readonly ISessionAuthenticator _authenticator;
    private readonly GlobalPacketRateLimiter _globalRateLimiter;
    private readonly IOptions<SessionOptions> _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<SessionManager> _logger;

    public SessionManager(
        SessionRegistry registry,
        IPacketDispatcher dispatcher,
        ISessionAuthenticator authenticator,
        GlobalPacketRateLimiter globalRateLimiter,
        IOptions<SessionOptions> options,
        ILoggerFactory loggerFactory,
        ILogger<SessionManager> logger)
    {
        _registry = registry;
        _dispatcher = dispatcher;
        _authenticator = authenticator;
        _globalRateLimiter = globalRateLimiter;
        _options = options;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public async Task AcceptAsync(
        HttpContext context,
        WebSocket webSocket,
        CancellationToken cancellationToken)
    {
        var session = new GameSession(
            Guid.NewGuid(),
            webSocket,
            context.Connection.RemoteIpAddress?.ToString(),
            _dispatcher,
            _authenticator,
            _globalRateLimiter,
            _options.Value,
            _loggerFactory.CreateLogger<GameSession>());

        _registry.Add(session);
        _logger.LogInformation("Accepted session {SessionId}.", session.SessionId);

        try
        {
            await session.RunAsync(cancellationToken);
        }
        finally
        {
            _registry.Remove(session.SessionId);
            _logger.LogInformation("Removed session {SessionId}.", session.SessionId);
        }
    }
}
