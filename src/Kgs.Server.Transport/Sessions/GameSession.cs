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
            PooledWebSocketFrame? frameResult;
            try
            {
                frameResult = await ReceiveFrameAsync(GetReceiveDeadline(), cancellationToken);
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
            catch (FrameTooLargeException ex)
            {
                await CloseWithErrorAsync(ProtocolErrorCode.PayloadTooLarge, 0, ex.Message);
                return;
            }
            catch (UnsupportedFrameTypeException ex)
            {
                await CloseWithErrorAsync(ProtocolErrorCode.InvalidPacket, 0, ex.Message);
                return;
            }

            if (frameResult is null)
            {
                return;
            }

            using var frame = frameResult.Value;

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

                var encodedLength = PacketCodec.GetEncodedLength(packet, _options.MaxPayloadLength);
                var buffer = ArrayPool<byte>.Shared.Rent(encodedLength);
                try
                {
                    var written = PacketCodec.EncodeTo(packet, buffer, _options.MaxPayloadLength);
                    await _webSocket.SendAsync(
                        buffer.AsMemory(0, written),
                        WebSocketMessageType.Binary,
                        endOfMessage: true,
                        cancellationToken);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (WebSocketException ex)
        {
            _logger.LogDebug(ex, "Send loop failed for session {SessionId}.", SessionId);
        }
        catch (Exception ex)
        {
            _isClosing = true;
            _logger.LogError(ex, "Send loop failed unexpectedly for session {SessionId}.", SessionId);
        }
    }

    private async Task<PooledWebSocketFrame?> ReceiveFrameAsync(DateTimeOffset deadline, CancellationToken cancellationToken)
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

    private async Task<PooledWebSocketFrame?> ReceiveFrameCoreAsync(CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
        var returnBuffer = true;
        PooledFrameBuffer? fragmentedMessage = null;
        WebSocketMessageType? messageType = null;
        var maxFrameLength = GetMaxFrameLength();

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

                if (messageType.Value is not WebSocketMessageType.Binary)
                {
                    throw new UnsupportedFrameTypeException(messageType.Value);
                }

                if (result.Count > maxFrameLength)
                {
                    throw new FrameTooLargeException(maxFrameLength);
                }

                if (fragmentedMessage is null && result.EndOfMessage)
                {
                    returnBuffer = false;
                    return new PooledWebSocketFrame(
                        messageType.Value,
                        buffer,
                        result.Count,
                        returnToPool: true);
                }

                fragmentedMessage ??= new PooledFrameBuffer(
                    Math.Min(ReceiveBufferSize, maxFrameLength),
                    maxFrameLength);

                if (fragmentedMessage.Count > maxFrameLength - result.Count)
                {
                    throw new FrameTooLargeException(maxFrameLength);
                }

                fragmentedMessage.Append(buffer.AsSpan(0, result.Count));

                if (!result.EndOfMessage)
                {
                    continue;
                }

                return fragmentedMessage.Detach(messageType.Value);
            }
        }
        finally
        {
            fragmentedMessage?.Dispose();
            if (returnBuffer)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    private int GetMaxFrameLength()
    {
        return checked(PacketCodec.HeaderSize + _options.MaxPayloadLength);
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
        var errorPacket = ProtocolErrorPackets.Create(errorCode, requestId, message, _options.MaxPayloadLength);
        if (!_outbound.Writer.TryWrite(errorPacket))
        {
            _logger.LogDebug(
                "Could not enqueue error packet {ErrorCode} for session {SessionId}; closing without error response.",
                errorCode,
                SessionId);
        }

        _isClosing = true;
        _outbound.Writer.TryComplete();
        await Task.CompletedTask;
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

    private readonly struct PooledWebSocketFrame(
        WebSocketMessageType messageType,
        byte[] buffer,
        int count,
        bool returnToPool) : IDisposable
    {
        private readonly byte[] _buffer = buffer;
        private readonly int _count = count;
        private readonly bool _returnToPool = returnToPool;

        public WebSocketMessageType MessageType { get; } = messageType;

        public ReadOnlyMemory<byte> Payload => _buffer.AsMemory(0, _count);

        public void Dispose()
        {
            if (_returnToPool)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
            }
        }
    }

    private sealed class PooledFrameBuffer(int initialCapacity, int maxCapacity) : IDisposable
    {
        private byte[]? _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
        private int _count;

        public int Count => _count;

        public void Append(ReadOnlySpan<byte> source)
        {
            EnsureCapacity(_count + source.Length);
            source.CopyTo(_buffer.AsSpan(_count));
            _count += source.Length;
        }

        public PooledWebSocketFrame Detach(WebSocketMessageType messageType)
        {
            var buffer = _buffer ?? throw new ObjectDisposedException(nameof(PooledFrameBuffer));
            _buffer = null;
            return new PooledWebSocketFrame(
                messageType,
                buffer,
                _count,
                returnToPool: true);
        }

        public void Dispose()
        {
            if (_buffer is not null)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = null;
            }
        }

        private void EnsureCapacity(int requiredLength)
        {
            if (requiredLength > maxCapacity)
            {
                throw new FrameTooLargeException(maxCapacity);
            }

            var buffer = _buffer ?? throw new ObjectDisposedException(nameof(PooledFrameBuffer));
            if (buffer.Length >= requiredLength)
            {
                return;
            }

            var newLength = buffer.Length;
            while (newLength < requiredLength)
            {
                newLength = newLength > maxCapacity / 2
                    ? maxCapacity
                    : Math.Min(newLength * 2, maxCapacity);
            }

            var newBuffer = ArrayPool<byte>.Shared.Rent(newLength);
            buffer.AsSpan(0, _count).CopyTo(newBuffer);
            ArrayPool<byte>.Shared.Return(buffer);
            _buffer = newBuffer;
        }
    }

    private sealed class FrameTooLargeException(int maxFrameLength)
        : Exception($"WebSocket frame exceeds the maximum allowed length of {maxFrameLength} bytes.");

    private sealed class UnsupportedFrameTypeException(WebSocketMessageType messageType)
        : Exception($"Only binary WebSocket frames are supported. Received: {messageType}.");
}
