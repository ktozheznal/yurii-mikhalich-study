using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace SecureLab.Api.Scaffolding;

public static class LoginRateLimit
{
    public const string Policy = "login";
    public const string SearchPolicy = "incident-search";

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
            options.AddPolicy(SearchPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                // Authenticated users get independent budgets, even on one NAT IP.
                // Forwarded headers have no authority without a configured trusted proxy.
                context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } userId
                    ? "user:" + userId
                    : "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = builder.Configuration.GetValue<int>("SearchRateLimit:PermitLimit", 10),
                    Window = TimeSpan.FromSeconds(builder.Configuration.GetValue<int>(
                        "SearchRateLimit:WindowSeconds", 60)),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));
            options.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter =
                        Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                var policy = context.HttpContext.GetEndpoint()?.Metadata
                    .GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
                AuthenticationEvents.Write(context.HttpContext.RequestServices
                    .GetRequiredService<ILogger<AuthenticationEvents>>(), context.HttpContext,
                    policy == SearchPolicy ? "search" : "login", "rate-limited",
                    context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier));
                return ValueTask.CompletedTask;
            };
        });
    }
}
