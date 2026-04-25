namespace Kgs.Server.Transport.Authentication;

public sealed record SessionAuthenticationResult(
    bool IsAuthenticated,
    Guid? PlayerId,
    string? FailureReason)
{
    public static SessionAuthenticationResult Success(Guid playerId)
    {
        return new SessionAuthenticationResult(true, playerId, null);
    }

    public static SessionAuthenticationResult Fail(string failureReason)
    {
        return new SessionAuthenticationResult(false, null, failureReason);
    }
}
