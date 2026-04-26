# KGS

KGS is a .NET 10 game server framework skeleton built on ASP.NET Core Kestrel WebSockets, with optional Proto.Actor integration.

## Projects

- `src/Kgs.Server` - Kestrel host and WebSocket gateway endpoint.
- `src/Kgs.Server.Transport` - session management, receive/send loops, packet codec, auth, heartbeat, and rate limiting.
- `src/Kgs.Game.Actors` - optional Proto.Actor runtime integration and packet dispatcher adapter.
- `src/Kgs.Game.Contracts` - shared packet, session, and dispatcher contracts.
- `tests/Kgs.Server.Transport.Tests` - packet codec and transport utility tests.
- `tests/Kgs.Server.Tests` - server endpoint tests.
- `tests/Kgs.Game.Actors.Tests` - actor behavior tests.

## Getting started

Install the .NET 10 SDK, then run:

```bash
dotnet restore
dotnet build
dotnet test
dotnet run --project src/Kgs.Server
```

The server exposes:

- `GET /healthz`
- `GET /ws` for binary WebSocket game clients.

The initial packet format uses a 12-byte big-endian header:

- `ushort OpCode`
- `uint RequestId`
- `ushort Flags`
- `uint PayloadLength`

Default protocol opcodes:

- `1` - auth request
- `2` - auth response
- `3` - ping
- `4` - pong
- `5` - error

Business opcodes and routing rules are intentionally application-defined. Replace the default no-op `IPacketDispatcher` in `Kgs.Server` with your own dispatcher, or use `Kgs.Game.Actors` and provide an `IActorPacketRouteResolver` to route packets to Proto.Actor actors.

## Logging

`Kgs.Server` uses Serilog as the host logging pipeline. The default configuration is in `src/Kgs.Server/appsettings.json`, with development overrides in `src/Kgs.Server/appsettings.Development.json`.

Library projects should continue to depend on `Microsoft.Extensions.Logging` abstractions only. Add structured properties such as `SessionId`, `PlayerId`, `OpCode`, and `RequestId` through message templates so host applications can decide how to route and store logs.
