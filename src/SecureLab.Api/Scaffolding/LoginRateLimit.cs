using System.Globalization;
using System.Threading.RateLimiting;

namespace SecureLab.Api.Scaffolding;

public static class LoginRateLimit
{
    public const string Policy = "login";

    public static void AddLoginRateLimit(this WebApplicationBuilder builder)
    {
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(Policy, context => RateLimitPartition.GetFixedWindowLimiter(
                // No client header is trusted as an IP; null addresses in TestServer share one quota.
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                  PermitLimit = builder.Configuration.GetValue<int>(
                    "LoginRateLimit:PermitLimit", 5),
                    Window = TimeSpan.FromMinutes(builder.Configuration.GetValue<int>(
                        "LoginRateLimit:WindowMinutes", 1)),
                        QueueLimit = 0,
                        AutoReplenishment = true
                }));
            options.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter =
                        Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                AuthenticationEvents.Write(context.HttpContext.RequestServices
                    .GetRequiredService<ILogger<AuthenticationEvents>>(), context.HttpContext, "login", "rate-limited");
                return ValueTask.CompletedTask;
            };
        });
    }
}
