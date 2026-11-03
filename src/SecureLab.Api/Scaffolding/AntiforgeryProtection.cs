using Microsoft.AspNetCore.Antiforgery;

namespace SecureLab.Api.Scaffolding;

public static class AntiforgeryProtection
{
    public const string HeaderName = "X-CSRF-TOKEN";

    public static void AddCookieAntiforgery(this WebApplicationBuilder builder) =>
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = HeaderName;
            options.Cookie.Name = "SecureLab.Antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });

    public static void MapAntiforgeryToken(this WebApplication app) =>
        app.MapGet("/api/antiforgery/token", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { token = tokens.RequestToken, headerName = HeaderName });
        }).RequireAuthorization().WithName("GetAntiforgeryToken");

    // JSON minimal APIs need an explicit check, including DELETE and logout.
    // Validation happens before handlers can change the resource or session.
    public static RouteHandlerBuilder RequireCookieAntiforgery(this RouteHandlerBuilder endpoint) =>
        endpoint.AddEndpointFilter<CookieAntiforgeryFilter>().ProducesProblem(400);
}

public sealed class CookieAntiforgeryFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            // Never echo token, cookie, exception details or the submitted body.
            return Results.Problem(title: "Перевірку CSRF відхилено", statusCode: 400);
        }
        return await next(context);
    }
}
