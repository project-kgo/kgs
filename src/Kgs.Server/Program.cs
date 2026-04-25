using Kgs.Game.Contracts;
using Kgs.Server.Transport.DependencyInjection;
using Kgs.Server.WebSockets;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IPacketDispatcher, NoOpPacketDispatcher>();
builder.Services.AddGameTransport();
builder.Services.AddSingleton<WebSocketGateway>();

var app = builder.Build();

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
});

app.MapGet("/healthz", () => Results.Ok(new HealthResponse("Healthy")));
app.Map("/ws", (HttpContext context, WebSocketGateway gateway) => gateway.HandleAsync(context));

app.Run();

public sealed record HealthResponse(string Status);

public partial class Program;
