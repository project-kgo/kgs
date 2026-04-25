using Kgs.Game.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Kgs.Game.Actors;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGameActors(this IServiceCollection services)
    {
        services.AddSingleton<GameActorSystemHostedService>();
        services.AddSingleton<IActorSystemHost>(sp => sp.GetRequiredService<GameActorSystemHostedService>());
        services.AddHostedService(sp => sp.GetRequiredService<GameActorSystemHostedService>());
        return services;
    }

    public static IServiceCollection AddActorPacketDispatcher(this IServiceCollection services)
    {
        services.AddSingleton<IPacketDispatcher, ActorPacketDispatcher>();
        return services;
    }
}
