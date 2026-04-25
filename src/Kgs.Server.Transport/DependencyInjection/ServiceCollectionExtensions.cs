using Kgs.Server.Transport.Authentication;
using Kgs.Server.Transport.Configuration;
using Kgs.Server.Transport.RateLimiting;
using Kgs.Server.Transport.Sessions;
using Microsoft.Extensions.DependencyInjection;

namespace Kgs.Server.Transport.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGameTransport(
        this IServiceCollection services,
        Action<SessionOptions>? configure = null)
    {
        services.AddOptions<SessionOptions>();

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.AddSingleton<ISessionAuthenticator, DevelopmentSessionAuthenticator>();
        services.AddSingleton<GlobalPacketRateLimiter>();
        services.AddSingleton<SessionRegistry>();
        services.AddSingleton<ISessionRegistry>(sp => sp.GetRequiredService<SessionRegistry>());
        services.AddSingleton<ISessionManager, SessionManager>();
        return services;
    }
}
