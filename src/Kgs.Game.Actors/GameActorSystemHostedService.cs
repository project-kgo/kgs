using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proto;

namespace Kgs.Game.Actors;

public sealed class GameActorSystemHostedService : IActorSystemHost, IHostedService
{
    private readonly ILogger<GameActorSystemHostedService> _logger;

    public GameActorSystemHostedService(ILogger<GameActorSystemHostedService> logger)
    {
        _logger = logger;
        ActorSystem = new ActorSystem();
    }

    public ActorSystem ActorSystem { get; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("KGS actor system started.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("KGS actor system stopped.");
        return Task.CompletedTask;
    }
}
