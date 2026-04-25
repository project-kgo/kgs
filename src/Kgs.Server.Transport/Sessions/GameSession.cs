using System.Buffers;
using System.Net.WebSockets;
using System.Threading.Channels;
using Kgs.Game.Contracts;
using Kgs.Server.Transport.Authentication;
using Kgs.Server.Transport.Configuration;
using Kgs.Server.Transport.Packets;
using Kgs.Server.Transport.RateLimiting;
using Microsoft.Extensions.Logging;

namespace Kgs.Server.Transport.Sessions;

public sealed class GameSession : ISessionSender
{
    private const int ReceiveBufferSize = 64 * 1024;
    private readonly WebSocket _webSocket;
    private readonly string? _remoteAddress;
    private readonly IPacketDispatcher _dispatcher;
    private readonly ISessionAuthenticator _authenticator;
    private readonly GlobalPacketRateLimiter _globalRateLimiter;
    private readonly TokenBucketRateLimiter _sessionRateLimiter;
    private readonly SessionOptions _options;
    private readonly Channel<GamePacket> _outbound;
    private readonly ILogger<GameSession> _logger;
    private readonly DateTimeOffset _connectedAt = DateTimeOffset.UtcNow;
    private DateTimeOffset _lastHeartbeatAt = DateTimeOffset.UtcNow;
    private Guid? _playerId;
    private bool _isClosing;

    public GameSession(
        Guid sessionId,
        WebSocket webSocket,
        string? remoteAddress,
        IPacketDispatcher dispatcher,
        ISessionAuthenticator authenticator,
        GlobalPacketRateLimiter globalRateLimiter,
        SessionOptions options,
        ILogger<GameSession> logger)
    {
        SessionId = sessionId;
        _webSocket = webSocket;
        _remoteAddress = remoteAddress;
        _dispatcher = dispatcher;
        _authenticator = authenticator;
        _globalRateLimiter = globalRateLimiter;
        _options = options;
        _logger = logger;
        _sessionRateLimiter = new TokenBucketRateLimiter(
            options.SessionPacketsPerSecond,
            options.SessionPacketBurst);
        _outbound = Channel.CreateBounded<GamePacket>(new BoundedChannelOptions(options.SendQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public Guid SessionId { get; }

    public bool IsAuthenticated => _playerId.HasValue;

    public ValueTask EnqueueAsync(GamePacket packet, CancellationToken cancellationToken)
    {
        return _outbound.Writer.WriteAsync(packet, cancellationToken);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var sendLoop = SendLoopAsync(cancellationToken);

        try
        {
            await ReceiveLoopAsync(cancellationToken);
        }
        finally
        {
            _outbound.Writer.TryComplete();
            await WaitForSendLoopAsync(sendLoop);
            await CloseIfNeededAsync();
            _logger.LogInformation("Session {SessionId} closed.", SessionId);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!_isClosing && (_webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived))
        {
            WebSocketFrame? frame;
            try
            {
                frame = await ReceiveFrameAsync(GetReceiveDeadline(), cancellationToken);
            }
            catch (TimeoutException)
            {
                var errorCode = IsAuthenticated
                    ? ProtocolErrorCode.HeartbeatTimeout
                    : ProtocolErrorCode.Unauthorized;
                var message = IsAuthenticated ? "Heartbeat timeout." : "Authentication timeout.";
                await CloseWithErrorAsync(errorCode, 0, message);
                return;
            }

            if (frame is null)
            {
                return;
            }

            if (frame.MessageType is not WebSocketMessageType.Binary)
            {
                await CloseWithErrorAsync(ProtocolErrorCode.InvalidPacket, 0, "Only binary packets are supported.");
                return;
            }

            var decode = PacketCodec.Decode(frame.Payload, _options.MaxPayloadLength);
            if (!decode.Succeeded || decode.Packet is null)
            {
                await CloseWithErrorAsync(ProtocolErrorCode.InvalidPacket, 0, decode.ErrorMessage ?? "Invalid packet.");
                return;
            }

            var packet = decode.Packet;
            if (!_globalRateLimiter.TryConsume())
            {
                await CloseWithErrorAsync(ProtocolErrorCode.ServerBusy, packet.RequestId, "Global packet limit exceeded.");
                return;
            }

            if (!_sessionRateLimiter.TryConsume(DateTimeOffset.UtcNow))
            {
                await CloseWithErrorAsync(ProtocolErrorCode.RateLimited, packet.RequestId, "Session packet limit exceeded.");
                return;
            }

            if (packet.OpCode is SystemOpCodes.Ping)
            {
                _lastHeartbeatAt = DateTimeOffset.UtcNow;
                await EnqueueAsync(new GamePacket(SystemOpCodes.Pong, packet.RequestId, 0, ReadOnlyMemory<byte>.Empty), cancellationToken);
                continue;
            }

            if (!IsAuthenticated)
            {
                await HandleUnauthenticatedPacketAsync(packet, cancellationToken);
                if (!IsAuthenticated)
                {
                    return;
                }

                continue;
            }

            await DispatchPacketAsync(packet, cancellationToken);
        }
    }

    private async Task HandleUnauthenticatedPacketAsync(GamePacket packet, CancellationToken cancellationToken)
    {
        if (packet.OpCode is not SystemOpCodes.AuthRequest)
        {
            await CloseWithErrorAsync(ProtocolErrorCode.Unauthorized, packet.RequestId, "Authentication is required.");
            return;
        }

        var result = await _authenticator.AuthenticateAsync(CreateContext(), packet, cancellationToken);
        if (!result.IsAuthenticated || result.PlayerId is null)
        {
            await CloseWithErrorAsync(
                ProtocolErrorCode.Unauthorized,
                packet.RequestId,
                result.FailureReason ?? "Authentication failed.");
            return;
        }

        _playerId = result.PlayerId;
        await EnqueueAsync(new GamePacket(SystemOpCodes.AuthResponse, packet.RequestId, 0, "OK"u8.ToArray()), cancellationToken);
        _logger.LogInformation("Session {SessionId} authenticated as player {PlayerId}.", SessionId, _playerId);
    }

    private async Task DispatchPacketAsync(GamePacket packet, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _dispatcher.DispatchAsync(
                new PacketDispatchRequest(CreateContext(), packet),
                cancellationToken);

            foreach (var outboundPacket in result.OutboundPackets)
            {
                await EnqueueAsync(outboundPacket, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispatch packet {OpCode} for session {SessionId}.", packet.OpCode, SessionId);
            await CloseWithErrorAsync(ProtocolErrorCode.DispatchFailed, packet.RequestId, "Dispatch failed.");
        }
    }

    private async Task SendLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var packet in _outbound.Reader.ReadAllAsync(cancellationToken))
            {
                if (_webSocket.State is not WebSocketState.Open)
                {
                    return;
                }

                var payload = PacketCodec.Encode(packet, _options.MaxPayloadLength);
                await _webSocket.SendAsync(
                    payload,
                    WebSocketMessageType.Binary,
                    endOfMessage: true,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (WebSocketException ex)
        {
            _logger.LogDebug(ex, "Send loop failed for session {SessionId}.", SessionId);
        }
    }

    private async Task<WebSocketFrame?> ReceiveFrameAsync(DateTimeOffset deadline, CancellationToken cancellationToken)
    {
        var remaining = deadline - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            throw new TimeoutException();
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(remaining);

        try
        {
            return await ReceiveFrameCoreAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException();
        }
    }

    private async Task<WebSocketFrame?> ReceiveFrameCoreAsync(CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
        MemoryStream? fragmentedMessage = null;
        WebSocketMessageType? messageType = null;

        try
        {
            while (true)
            {
                var result = await _webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    cancellationToken);

                if (result.MessageType is WebSocketMessageType.Close)
                {
                    return null;
                }

                messageType ??= result.MessageType;
                fragmentedMessage ??= new MemoryStream();
                fragmentedMessage.Write(buffer, 0, result.Count);

                if (!result.EndOfMessage)
                {
                    continue;
                }

                return new WebSocketFrame(messageType.Value, fragmentedMessage.ToArray());
            }
        }
        finally
        {
            fragmentedMessage?.Dispose();
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private DateTimeOffset GetReceiveDeadline()
    {
        return IsAuthenticated
            ? _lastHeartbeatAt + _options.HeartbeatTimeout
            : _connectedAt + _options.AuthTimeout;
    }

    private GameSessionContext CreateContext()
    {
        return new GameSessionContext(SessionId, _playerId, _remoteAddress);
    }

    private async Task CloseWithErrorAsync(ProtocolErrorCode errorCode, uint requestId, string message)
    {
        try
        {
            await EnqueueAsync(ProtocolErrorPackets.Create(errorCode, requestId, message), CancellationToken.None);
        }
        catch (ChannelClosedException)
        {
        }
        finally
        {
            _isClosing = true;
            _outbound.Writer.TryComplete();
        }
    }

    private static async Task WaitForSendLoopAsync(Task sendLoop)
    {
        try
        {
            await sendLoop;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task CloseIfNeededAsync()
    {
        if (_webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            await _webSocket.CloseOutputAsync(
                WebSocketCloseStatus.NormalClosure,
                "Session closed.",
                CancellationToken.None);
        }
    }

    private sealed record WebSocketFrame(
        WebSocketMessageType MessageType,
        ReadOnlyMemory<byte> Payload);
}
