using System.Net.WebSockets;
using System.Net;
using FluentAssertions;
using Kgs.Game.Contracts;
using Kgs.Server.Transport.Packets;
using Kgs.Server.Transport.Sessions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Kgs.Server.Tests;

public sealed class ServerEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ServerEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Healthz_returns_healthy_response()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/healthz");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("Healthy");
    }

    [Fact]
    public async Task Ws_rejects_plain_http_requests()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/ws");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Ws_requires_authentication_before_business_packets()
    {
        using var socket = await _factory.Server.CreateWebSocketClient()
            .ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);

        await SendPacketAsync(socket, new GamePacket(
            1000,
            10,
            0,
            ReadOnlyMemory<byte>.Empty));
        var response = await ReceivePacketAsync(socket);

        response.OpCode.Should().Be(SystemOpCodes.Error);
        response.RequestId.Should().Be(10);
        response.Flags.Should().Be((ushort)ProtocolErrorCode.Unauthorized);
    }

    [Fact]
    public async Task Ws_authenticates_and_responds_to_protocol_ping()
    {
        using var socket = await _factory.Server.CreateWebSocketClient()
            .ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);

        await SendPacketAsync(socket, new GamePacket(SystemOpCodes.AuthRequest, 1, 0, "dev-token"u8.ToArray()));
        var auth = await ReceivePacketAsync(socket);

        await SendPacketAsync(socket, new GamePacket(SystemOpCodes.Ping, 2, 0, ReadOnlyMemory<byte>.Empty));
        var pong = await ReceivePacketAsync(socket);

        auth.OpCode.Should().Be(SystemOpCodes.AuthResponse);
        auth.RequestId.Should().Be(1);
        pong.OpCode.Should().Be(SystemOpCodes.Pong);
        pong.RequestId.Should().Be(2);

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test complete", CancellationToken.None);
        await WaitUntilAsync(() => _factory.Services.GetRequiredService<ISessionRegistry>().Count == 0);
    }

    [Fact]
    public async Task Ws_dispatches_authenticated_business_packets()
    {
        var dispatcher = new RecordingGameMessageDispatcher();
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPacketDispatcher>();
                services.AddSingleton<IPacketDispatcher>(dispatcher);
            });
        });
        using var socket = await factory.Server.CreateWebSocketClient()
            .ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);

        await SendPacketAsync(socket, new GamePacket(SystemOpCodes.AuthRequest, 1, 0, "dev-token"u8.ToArray()));
        _ = await ReceivePacketAsync(socket);
        await SendPacketAsync(socket, new GamePacket(1000, 20, 0, "move"u8.ToArray()));

        await WaitUntilAsync(() => dispatcher.Requests.Count == 1);
        dispatcher.Requests[0].Packet.OpCode.Should().Be(1000);
        dispatcher.Requests[0].Context.PlayerId.Should().NotBeNull();

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test complete", CancellationToken.None);
    }

    private static async Task SendPacketAsync(WebSocket socket, GamePacket packet)
    {
        var encoded = PacketCodec.Encode(packet, maxPayloadLength: 64 * 1024);
        await socket.SendAsync(encoded, WebSocketMessageType.Binary, endOfMessage: true, CancellationToken.None);
    }

    private static async Task<GamePacket> ReceivePacketAsync(WebSocket socket)
    {
        var buffer = new byte[64 * 1024];
        var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
        result.MessageType.Should().Be(WebSocketMessageType.Binary);
        result.EndOfMessage.Should().BeTrue();

        var decoded = PacketCodec.Decode(buffer.AsMemory(0, result.Count), maxPayloadLength: 64 * 1024);
        decoded.Succeeded.Should().BeTrue(decoded.ErrorMessage);
        return decoded.Packet!;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        while (!condition())
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
    }

    private sealed class RecordingGameMessageDispatcher : IPacketDispatcher
    {
        public List<PacketDispatchRequest> Requests { get; } = [];

        public ValueTask<PacketDispatchResult> DispatchAsync(
            PacketDispatchRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return ValueTask.FromResult(PacketDispatchResult.Empty);
        }
    }
}
