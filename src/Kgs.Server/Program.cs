using Kgs.Game.Contracts;
using Kgs.Server.Transport.DependencyInjection;
using Kgs.Server.WebSockets;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) =>
    {
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext();
    });

    builder.Services.AddSingleton<IPacketDispatcher, NoOpPacketDispatcher>();
    builder.Services.AddGameTransport();
    builder.Services.AddSingleton<WebSocketGateway>();

    var app = builder.Build();

    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
        options.GetLevel = (_, elapsed, exception) =>
        {
            if (exception is not null)
            {
                return LogEventLevel.Error;
            }

            return elapsed > 1_000
                ? LogEventLevel.Warning
                : LogEventLevel.Information;
        };
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("RemoteAddress", httpContext.Connection.RemoteIpAddress?.ToString());
        };
    });

    app.UseWebSockets(new WebSocketOptions
    {
        KeepAliveInterval = TimeSpan.FromSeconds(30)
    });

    app.MapGet("/healthz", () => Results.Ok(new HealthResponse("Healthy")));
    app.Map("/ws", (HttpContext context, WebSocketGateway gateway) => gateway.HandleAsync(context));

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "KGS server terminated unexpectedly.");
}
finally
{
    Log.CloseAndFlush();
}

public sealed record HealthResponse(string Status);

public partial class Program;
