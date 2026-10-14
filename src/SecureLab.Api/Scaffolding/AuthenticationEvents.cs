namespace SecureLab.Api.Scaffolding;

public sealed class AuthenticationEvents
{
    public static void Write(ILogger<AuthenticationEvents> logger, HttpContext context,
        string eventType, string result, string? verifiedUserId = null) =>
        logger.LogInformation("Authentication {EventType} at {TimestampUtc}: result {Result}, user {UserId}, trace {TraceId}",
            eventType, DateTimeOffset.UtcNow, result, verifiedUserId ?? "unverified", context.TraceIdentifier);
}
