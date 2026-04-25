namespace Kgs.Game.Contracts;

public sealed record GameSessionContext(
    Guid SessionId,
    Guid? PlayerId,
    string? RemoteAddress);
