using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;
using SecureLab.Api.Scaffolding;
using Xunit.Abstractions;

namespace SecureLab.Api.Tests;

public sealed class Lab06HardeningTests(ITestOutputHelper output) : IDisposable
{
    private readonly SecureLabApiFactory _factory = new();

    [Theory]
    [InlineData("create", false)]
    [InlineData("create", true)]
    [InlineData("update", false)]
    [InlineData("update", true)]
    [InlineData("delete", false)]
    [InlineData("delete", true)]
    [InlineData("triage", false)]
    [InlineData("triage", true)]
    [InlineData("logout", false)]
    [InlineData("logout", true)]
    public async Task REG_CSRF_AllCookieMutations_RejectWithoutValidToken_KeepState_ThenAcceptValidPair(
        string operation, bool invalidToken)
    {
        // Explicitly raw: the negative client must never receive a token from LoginAsync.
        using var client = await _factory.LoginAsync(operation == "triage" ? "morgan" : "alice",
            CookieClient.SeedPassword, antiforgery: false);
        var id = await AddIncidentAsync();
        var title = "Lab06-created-" + Guid.NewGuid();
        try
        {
            var before = await SnapshotAsync(id);
            var countBefore = await CountAsync(title);
            if (invalidToken) client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", "invalid-study-token");
            using var denied = await MutateAsync(client, operation, id, title);
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            using var problem = JsonDocument.Parse(await denied.Content.ReadAsStringAsync());
            Assert.Equal("Перевірку CSRF відхилено", problem.RootElement.GetProperty("title").GetString());
            Assert.False(problem.RootElement.TryGetProperty("errors", out _));
            Assert.Equal(before, await SnapshotAsync(id));
            Assert.Equal(countBefore, await CountAsync(title));
            using var stillAuthenticated = await client.GetAsync("/api/me");
            Assert.Equal(HttpStatusCode.OK, stillAuthenticated.StatusCode);

            await client.UseAntiforgeryTokenAsync();
            using var accepted = await MutateAsync(client, operation, id, title);
            Assert.Equal(operation == "create" ? HttpStatusCode.Created : operation == "triage"
                ? HttpStatusCode.OK : HttpStatusCode.NoContent, accepted.StatusCode);
            if (operation == "create") Assert.Equal(countBefore + 1, await CountAsync(title));
            if (operation == "update") Assert.Equal("Edited by valid token", (await SnapshotAsync(id))!.Description);
            if (operation == "delete") Assert.Null(await SnapshotAsync(id));
            if (operation == "triage")
            {
                var after = await SnapshotAsync(id);
                Assert.Equal(IncidentStatus.Triaged, after!.Status);
                Assert.Equal(before!.HistoryCount + 1, after.HistoryCount);
                Assert.Equal(before.Owner, after.Owner);
            }
            if (operation == "logout")
            {
                using var anonymous = await client.GetAsync("/api/me");
                Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            }
            output.WriteLine($"REG-CSRF {operation}; token={(invalidToken ? "invalid" : "missing")}: 400, state unchanged; valid pair={(int)accepted.StatusCode}, expected state verified.");
        }
        finally { await CleanupAsync(id, title); }
    }

    [Fact]
    public async Task REG_CSRF_TokenIsBoundToAuthenticatedIdentity_AndFreshPairWorksAfterNewLogin()
    {
        using var client = await _factory.LoginAsync("alice", CookieClient.SeedPassword);
        using var switchUser = await client.PostAsJsonAsync("/api/auth/login",
            new { userName = "bob", password = CookieClient.SeedPassword });
        Assert.Equal(HttpStatusCode.NoContent, switchUser.StatusCode);
        var title = "Lab06-user-switch-" + Guid.NewGuid();
        try
        {
            using var rejected = await client.PostAsJsonAsync("/api/incidents", CreateBody(title));
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Equal(0, await CountAsync(title));
            await client.UseAntiforgeryTokenAsync();
            using var accepted = await client.PostAsJsonAsync("/api/incidents", CreateBody(title));
            Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
            var created = await accepted.Content.ReadFromJsonAsync<CreatedIncidentResponse>();
            Assert.Equal(DbSeeder.BobId, (await SnapshotAsync(created!.Id))!.Owner);
        }
        finally { await CleanupAsync(Guid.Empty, title); }
    }

    [Theory]
    [InlineData("Development", "http://localhost", false)]
    [InlineData("Development", "https://localhost", true)]
    [InlineData("Production", "https://localhost", true)]
    public async Task AntiforgeryCookieAndCachePolicy_MatchActualProfile(string environment, string origin, bool secure)
    {
        using var host = _factory.WithWebHostBuilder(builder => builder.UseEnvironment(environment));
        Assert.Equal(environment, host.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri(origin), AllowAutoRedirect = false, HandleCookies = true });
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { userName = "alice", password = CookieClient.SeedPassword });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        // HTTPS TestServer verifies application flags, not a real TLS connection.
        using var response = await client.GetAsync("/api/antiforgery/token");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? "");
        var cookie = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("SecureLab.Antiforgery=", StringComparison.Ordinal));
        var attributes = cookie.Split(';').Skip(1).Select(part => part.Trim().ToLowerInvariant()).ToArray();
        Assert.Contains("httponly", attributes);
        Assert.Contains("samesite=strict", attributes);
        Assert.Equal(secure, attributes.Contains("secure"));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(!string.IsNullOrWhiteSpace(body.RootElement.GetProperty("token").GetString()));
        Assert.Equal("X-CSRF-TOKEN", body.RootElement.GetProperty("headerName").GetString());
        output.WriteLine($"{environment} {origin} TestServer: token200; HttpOnly=True, SameSite=Strict, Secure={secure}, no-store=True.");
    }

    [Fact]
    public async Task ProductionHsts_UsesHttpsNonLoopbackHost_AndPreservesLocalhostExclusion()
    {
        using var host = _factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        Assert.Equal("Production", host.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);
        foreach (var (origin, expected) in new[] { ("https://securelab.test", true), ("https://localhost", false), ("http://securelab.test", false) })
        {
            using var client = host.CreateClient(new WebApplicationFactoryClientOptions
                { BaseAddress = new Uri(origin), AllowAutoRedirect = false });
            using var response = await client.GetAsync("/");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(expected, response.Headers.Contains("Strict-Transport-Security"));
            if (expected) Assert.Contains("max-age=2592000", Header(response, "Strict-Transport-Security") ?? "");
        }
        output.WriteLine("Production TestServer application policy: HTTPS securelab.test HSTS max-age2592000; HTTPS localhost excluded; HTTP no HSTS. Real TLS/browser HSTS not claimed.");
    }

    [Fact]
    public async Task StatusAudit_RecordsAllowedAnalystAndDeniedReporter_WithoutBodyOrCredentials()
    {
        var capture = new DiagnosticCapture();
        using var host = _factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(capture)));
        using var reporter = await host.LoginAsync("alice", CookieClient.SeedPassword);
        using var analyst = await host.LoginAsync("morgan", CookieClient.SeedPassword);
        var id = await AddIncidentAsync();
        try
        {
            var before = await SnapshotAsync(id);
            using var denied = await reporter.PostAsJsonAsync("/api/incidents/" + id + "/triage", new { token = "never-log-request-body" });
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Equal(before, await SnapshotAsync(id));
            using var allowed = await analyst.PostAsJsonAsync("/api/incidents/" + id + "/triage", new { token = "never-log-request-body" });
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
            var after = await SnapshotAsync(id);
            Assert.Equal(IncidentStatus.Triaged, after!.Status);
            Assert.Equal(before!.HistoryCount + 1, after.HistoryCount);
            var audit = capture.Events.Where(entry => entry.Category.EndsWith("IncidentAccess", StringComparison.Ordinal)).ToArray();
            Assert.Contains(audit, entry => entry.Text.Contains("authorization.denied", StringComparison.Ordinal) && entry.Text.Contains(id.ToString(), StringComparison.Ordinal));
            Assert.Contains(audit, entry => entry.Text.Contains("incident.status.changed", StringComparison.Ordinal)
                && entry.Text.Contains("old=New new=Triaged", StringComparison.Ordinal) && entry.Text.Contains(DbSeeder.MorganId.ToString(), StringComparison.Ordinal));
            Assert.All(audit, entry =>
            {
                Assert.True(!entry.Text.Contains("never-log-request-body", StringComparison.Ordinal));
                Assert.True(!entry.Text.Contains(CookieClient.SeedPassword, StringComparison.Ordinal));
                Assert.Contains("traceId=", entry.Text);
            });
            output.WriteLine("Reporter triage403, snapshot unchanged; Analyst triage200, New->Triaged and history+1; safe denied+allowed audit includes subject/action/resource/trace without request body or credentials.");
        }
        finally { await CleanupAsync(id, ""); }
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Headers_PersistOnStaticApiValidationAndMissingResource(string environment)
    {
        using var host = _factory.WithWebHostBuilder(builder => builder.UseEnvironment(environment));
        Assert.Equal(environment, host.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);
        using var client = await host.LoginAsync("alice", CookieClient.SeedPassword);
        foreach (var (path, status) in new[]
        {
            ("/", HttpStatusCode.OK), ("/api/incidents/" + DbSeeder.AliceIncidentId, HttpStatusCode.OK),
            ("/api/incidents?status=Unknown", HttpStatusCode.BadRequest),
            ("/api/incidents/99999999-9999-9999-9999-999999999999", HttpStatusCode.NotFound)
        })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(status, response.StatusCode);
            AssertSecurityHeaders(response);
            if (status == HttpStatusCode.BadRequest)
            {
                using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("status", out _));
            }
            output.WriteLine($"{environment}; HTTPS TestServer; GET {path}: {(int)status}; nosniff; no-referrer; CSP enforced frame-ancestors none.");
        }
    }

    [Fact]
    public async Task ProductionUnexpectedError_IsSafeProblemDetailsWithTraceAndHeaders()
    {
        var capture = new DiagnosticCapture();
        using var host = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:SecureLab", "Host=127.0.0.1;Port=54399;Database=securelab;Username=securelab;Password=unreachable-study-value;Timeout=1;Command Timeout=1;Include Error Detail=false");
            builder.ConfigureLogging(logging => logging.AddProvider(capture));
        });
        Assert.Equal("Production", host.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        const string sentinel = "NotARealPassword1!";
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { userName = "alice", password = sentinel });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        AssertSecurityHeaders(response);
        var text = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(text);
        var trace = body.RootElement.GetProperty("traceId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(trace));
        foreach (var forbidden in new[] { sentinel, "Exception", "Npgsql", "54399", "unreachable-study-value", "Password=", "C:\\", "SELECT " })
            Assert.False(text.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        var entries = capture.Events.ToArray();
        Assert.Contains(entries, entry => entry.Category.Contains("ExceptionHandler", StringComparison.Ordinal));
        foreach (var forbidden in new[] { sentinel, "unreachable-study-value", ".AspNetCore.Identity.Application=" })
            Assert.All(entries, entry => Assert.True(!entry.Text.Contains(forbidden, StringComparison.Ordinal)));
        output.WriteLine($"Production HTTPS TestServer, unreachable local DB: 500 application/problem+json; traceId={trace}; security headers present; safe client response; exception diagnostics captured without credentials.");
    }

    [Fact]
    public async Task REG_RATE_Search_DefaultQuotaUsesTwoIdentityPartitions_SharedClientsAndRealWindowRecovery()
    {
        using var alice = await _factory.LoginAsync("alice", CookieClient.SeedPassword);
        using var aliceAgain = await _factory.LoginAsync("alice", CookieClient.SeedPassword);
        using var bob = await _factory.LoginAsync("bob", CookieClient.SeedPassword);
        for (var index = 0; index < 10; index++)
        {
            using var accepted = await alice.GetAsync("/api/incidents/search?q=USB");
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        }
        using var limited = await aliceAgain.GetAsync("/api/incidents/search?q=USB");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        for (var index = 0; index < 10; index++)
        {
            using var independent = await bob.GetAsync("/api/incidents/search?q=USB");
            Assert.Equal(HttpStatusCode.OK, independent.StatusCode);
        }
        using var bobLimited = await bob.GetAsync("/api/incidents/search?q=USB");
        Assert.Equal(HttpStatusCode.TooManyRequests, bobLimited.StatusCode);
        using var unrelated = await alice.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, unrelated.StatusCode);
        await Task.Delay(TimeSpan.FromSeconds(61));
        foreach (var client in new[] { aliceAgain, bob })
        {
            using var recovered = await client.GetAsync("/api/incidents/search?q=USB");
            Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        }
        output.WriteLine("REG-RATE search: Alice 10x200, second Alice client429 + Retry-After; Bob independent10x200 then429; after real61s both200; GET /api/me200 while limited.");
    }

    private static void AssertSecurityHeaders(HttpResponseMessage response)
    {
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
        Assert.Contains("frame-ancestors 'none'", Header(response, "Content-Security-Policy") ?? "");
        Assert.DoesNotContain("unsafe-inline", Header(response, "Content-Security-Policy") ?? "");
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(", ", values) : null;

    private static object CreateBody(string title) => new
    { title, description = "Synthetic CSRF regression", severity = "Low", occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5) };

    private static Task<HttpResponseMessage> MutateAsync(HttpClient client, string operation, Guid id, string title) => operation switch
    {
        "create" => client.PostAsJsonAsync("/api/incidents", CreateBody(title)),
        "update" => client.PutAsJsonAsync("/api/incidents/" + id, new { title = "Updated " + id, description = "Edited by valid token" }),
        "delete" => client.DeleteAsync("/api/incidents/" + id),
        "triage" => client.PostAsJsonAsync("/api/incidents/" + id + "/triage", new { }),
        "logout" => client.PostAsync("/api/auth/logout", null),
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    private async Task<Guid> AddIncidentAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Incidents.Add(new Incident
        {
            Id = id, OwnerUserId = DbSeeder.AliceId, Title = "Lab06-resource-" + id,
            Description = "Original synthetic content", Severity = IncidentSeverity.Low, Status = IncidentStatus.New,
            OccurredAtUtc = now, CreatedAtUtc = now, UpdatedAtUtc = now
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<IncidentState?> SnapshotAsync(Guid id)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SecureLabDbContext>().Incidents.AsNoTracking()
            .Where(item => item.Id == id).Select(item => new IncidentState(item.Title, item.Description, item.OwnerUserId,
                item.Status, item.UpdatedAtUtc, item.StatusHistory.Count)).SingleOrDefaultAsync();
    }

    private async Task<int> CountAsync(string title)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SecureLabDbContext>().Incidents.CountAsync(item => item.Title == title);
    }

    private async Task CleanupAsync(Guid id, string title)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SecureLabDbContext>().Incidents
            .Where(item => item.Id == id || item.Title == title).ExecuteDeleteAsync();
    }

    private sealed record IncidentState(string Title, string Description, Guid Owner, IncidentStatus Status,
        DateTimeOffset Updated, int HistoryCount);

    private sealed class DiagnosticCapture : ILoggerProvider
    {
        public ConcurrentQueue<(string Category, string Text)> Events { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(this, categoryName);
        public void Dispose() { }
        private sealed class CaptureLogger(DiagnosticCapture capture, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => capture.Events.Enqueue((category, formatter(state, exception)));
        }
    }

    public void Dispose() => _factory.Dispose();
}
