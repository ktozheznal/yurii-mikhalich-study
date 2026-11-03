using SecureLab.Api.Scaffolding;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Data;
using SecureLab.Api.Presentation.Endpoints;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = "Client"
});
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["traceId"] =
        Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(30);
    // Preserve the framework's loopback exclusions. A test host can verify the
    // header on a non-loopback Host, without claiming real TLS or browser state.
});

var connectionString = builder.Configuration.GetConnectionString("SecureLab")
    ?? throw new InvalidOperationException(
        "Connection string 'SecureLab' не налаштовано.");

builder.Services.AddDbContext<SecureLabDbContext>(options =>
    options.UseNpgsql(connectionString));
builder.Services.AddScoped<IncidentQueries>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IncidentAccess>();
builder.AddLab03Identity();
builder.AddLoginRateLimit();
builder.AddCookieAntiforgery();

// The local CORS probe is a Development-only browser experiment.
const string browserLabCors = "BrowserLabProbe";
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options => options.AddPolicy(browserLabCors, policy => policy
        .WithOrigins("http://localhost:4173")
        .WithMethods("GET")
        .WithHeaders("X-Lab-Cors-Probe")
        .AllowCredentials()));
}

var app = builder.Build();

var resetRequested = args.Contains("--reset-database", StringComparer.Ordinal);
await DatabaseBootstrap.InitializeAsync(app.Services, app.Configuration, app.Environment, resetRequested);

if (resetRequested)
{
    return;
}

app.Use(async (context, next) =>
{
    // ExceptionHandler clears pre-existing headers. Register outside it and write
    // immediately before the final response starts, including exception responses.
    context.Response.OnStarting(() =>
    {
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        // Scalar uses its own inline/CDN resources only in Development.
        if (!(app.Environment.IsDevelopment() && context.Request.Path.StartsWithSegments("/scalar")))
            context.Response.Headers["Content-Security-Policy"] =
                "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; " +
                "connect-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'";
        return Task.CompletedTask;
    });
    await next(context);
});
app.UseExceptionHandler();
app.UseStatusCodePages();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    // Enable after configuring a real HTTPS listener/proxy. Local Production HTTP
    // remains available for the controlled failure diagnostic only.
    if (app.Configuration.GetValue<bool>("Security:HttpsRedirectionEnabled"))
        app.UseHttpsRedirection();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseRouting();
if (app.Environment.IsDevelopment())
{
    app.UseCors(browserLabCors);
}
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", async (SecureLabDbContext dbContext, CancellationToken cancellationToken) =>
    await dbContext.Database.CanConnectAsync(cancellationToken)
        ? Results.Ok(new { status = "ready" })
        : Results.Problem(
            title: "База даних недоступна",
            statusCode: StatusCodes.Status503ServiceUnavailable))
    .WithName("GetHealth")
    .Produces(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapIncidentEndpoints();
app.MapIncidentMutationEndpoints();
app.MapLab03Identity();
app.MapLab02Endpoints();
app.MapAntiforgeryToken();
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
