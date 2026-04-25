using FluentAssertions;
using Kgs.Game.Contracts;
using Proto;
using Xunit;

namespace Kgs.Game.Actors.Tests;

public sealed class ActorPacketDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_sends_packet_envelope_to_resolved_actor()
    {
        var actorSystem = new ActorSystem();
        var received = new TaskCompletionSource<PacketEnvelope>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var target = actorSystem.Root.Spawn(Props.FromProducer(() => new RecordingActor(received)));
        var dispatcher = new ActorPacketDispatcher(
            new TestActorSystemHost(actorSystem),
            new FixedRouteResolver(target));
        var request = CreateRequest(opCode: 1000);

        var result = await dispatcher.DispatchAsync(request, CancellationToken.None);
        var envelope = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));

        result.OutboundPackets.Should().BeEmpty();
        envelope.Request.Should().Be(request);

        await actorSystem.Root.StopAsync(target);
    }

    [Fact]
    public async Task DispatchAsync_returns_error_packet_when_route_is_not_found()
    {
        var actorSystem = new ActorSystem();
        var dispatcher = new ActorPacketDispatcher(
            new TestActorSystemHost(actorSystem),
            new FixedRouteResolver(null));

        var result = await dispatcher.DispatchAsync(CreateRequest(opCode: 9000), CancellationToken.None);

        result.OutboundPackets.Should().ContainSingle();
        result.OutboundPackets[0].OpCode.Should().Be(SystemOpCodes.Error);
        result.OutboundPackets[0].Flags.Should().Be((ushort)ProtocolErrorCode.InvalidPacket);
    }

    private static PacketDispatchRequest CreateRequest(ushort opCode)
    {
        return new PacketDispatchRequest(
            new GameSessionContext(Guid.NewGuid(), Guid.NewGuid(), "127.0.0.1"),
            new GamePacket(opCode, 42, 0, ReadOnlyMemory<byte>.Empty));
    }

    private sealed class RecordingActor : IActor
    {
        private readonly TaskCompletionSource<PacketEnvelope> _received;

        public RecordingActor(TaskCompletionSource<PacketEnvelope> received)
        {
            _received = received;
        }

        public Task ReceiveAsync(IContext context)
        {
            if (context.Message is PacketEnvelope envelope)
            {
                _received.TrySetResult(envelope);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FixedRouteResolver : IActorPacketRouteResolver
    {
        private readonly PID? _target;

        public FixedRouteResolver(PID? target)
        {
            _target = target;
        }

        public ValueTask<PID?> ResolveAsync(
            PacketDispatchRequest request,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(_target);
        }
    }

    private sealed class TestActorSystemHost : IActorSystemHost
    {
        public TestActorSystemHost(ActorSystem actorSystem)
        {
            ActorSystem = actorSystem;
        }

        public ActorSystem ActorSystem { get; }
    }
}
