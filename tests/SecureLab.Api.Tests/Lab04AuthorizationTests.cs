using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;
using SecureLab.Api.Scaffolding;

namespace SecureLab.Api.Tests;

public sealed class Lab04AuthorizationTests(
    Xunit.Abstractions.ITestOutputHelper output) : IDisposable{
    private readonly SecureLabApiFactory _factory = new();
    private const string InternalId = "30000000-0000-0000-0000-000000000003";
    private static string PathFor(Guid id) => $"/api/incidents/{id}";

    [Theory]
    [InlineData("GET", "/api/incidents")]
    [InlineData("GET", "/api/incidents/20000000-0000-0000-0000-000000000001")]
    [InlineData("PUT", "/api/incidents/20000000-0000-0000-0000-000000000001")]
    [InlineData("DELETE", "/api/incidents/20000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/incidents/20000000-0000-0000-0000-000000000003/triage")]
    [InlineData("GET", "/api/incident-comments/30000000-0000-0000-0000-000000000001")]
    [InlineData("GET", "/api/incidents/search")]
    [InlineData("GET", "/api/incidents/severity-summary")]
    public async Task A01_Anonymous_Is401WithoutRedirect(string method, string path)
    {
        using var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "PUT") request.Content = JsonContent.Create(new { title = "Fake", description = "Fake" });
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.DoesNotContain("text/html", response.Content.Headers.ContentType?.MediaType ?? "");
    }

    [Fact]
    public async Task A02_Owner_ReadsAndUpdatesOwnContent_AndDeletesOwnNewIncident()
    {
        using var alice = await _factory.LoginAsync("alice", CookieClient.SeedPassword);
        var id = await CreateAsync(alice);
        try
        {
            using var own = await alice.GetAsync(PathFor(id));
            Assert.Equal(HttpStatusCode.OK, own.StatusCode);
            var before = await StateAsync(id);
            using var update = await alice.PutAsJsonAsync(PathFor(id), new { title = "Updated " + id, description = "Owner updated content." });
            Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
            var after = await StateAsync(id);
            Assert.Equal("Owner updated content.", after.Description);
            Assert.Equal(before.Owner, after.Owner);
            Assert.Equal(before.Status, after.Status);
            Assert.Equal(before.Severity, after.Severity);
            using var delete = await alice.DeleteAsync(PathFor(id));
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
            using var missing = await alice.GetAsync(PathFor(id));
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
        finally { await CleanupAsync(id); }
    }

    [Fact]
    public async Task A03_NonOwner_DetailsDeny_DoesNotDiscloseBob()
    {
        using var alice = await _factory.LoginAsync("alice", CookieClient.SeedPassword);
        alice.DefaultRequestHeaders.Add("X-Demo-UserId", DbSeeder.BobId.ToString());
        using var response = await alice.GetAsync(PathFor(DbSeeder.BobIncidentId));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.TryGetProperty("description", out _));
        Assert.False(json.RootElement.TryGetProperty("comments", out _));
        Assert.False(json.RootElement.TryGetProperty("ownerDisplayName", out _));
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task A04_AllForeignMutationsDeny_EntireStateUnchanged(string method)
    {
        using var alice = await _factory.LoginAsync("alice", CookieClient.SeedPassword);
        var before = await StateAsync(DbSeeder.BobIncidentId);
        using var request = new HttpRequestMessage(new HttpMethod(method), PathFor(DbSeeder.BobIncidentId));
        if (method == "PUT") request.Content = JsonContent.Create(new { title = "Forged", description = "An unauthorized description that is sufficiently long for High severity." });
        using var response = await alice.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, await StateAsync(DbSeeder.BobIncidentId));
    }
    [Fact]
    public async Task Lab4def()
    {
        using var alice = await _factory.LoginAsync("alice", CookieClient.SeedPassword);
        using var bob = await _factory.LoginAsync("bob", CookieClient.SeedPassword);

        var i1 = await CreateAsync(alice);
        var i2 = Guid.Empty;

        try
        {
            i2 = await CreateAsync(bob);
            var before = await StateAsync(i1);

            const string title = "Probe";
            const string description = "Test description";

            output.WriteLine($"I1={i1}; I2={i2}; before={before}");

            using var first = await bob.PutAsJsonAsync(PathFor(i1), new {title, description});
            var afterFirst = await StateAsync(i1);

            output.WriteLine($"no id = {(int)first.StatusCode}; after={afterFirst}");

            using var second = await bob.PutAsJsonAsync(PathFor(i1), new {title, description, id = i2});
            var afterSecond = await StateAsync(i1);

            output.WriteLine($"third = {(int)second.StatusCode}; after={afterSecond}");

            Assert.Equal(HttpStatusCode.Forbidden, first.StatusCode);

            Assert.Equal(before, afterFirst);

            Assert.Equal(HttpStatusCode.Forbidden, second.StatusCode);

            Assert.Equal(before, afterSecond);
        }
        finally
        {
            if (i2 != Guid.Empty) await CleanupAsync(i2);

            await CleanupAsync(i1);
        }
    }
    [Fact]
    public async Task A03R_Analyst_ReadsForAnalysis_WithoutContentOrDeleteRights()
    {
        using var morgan = await _factory.LoginAsync("morgan", CookieClient.SeedPassword);
        using var read = await morgan.GetAsync(PathFor(DbSeeder.AliceIncidentId));
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var before = await StateAsync(DbSeeder.AliceIncidentId);
        using var update = await morgan.PutAsJsonAsync(PathFor(DbSeeder.AliceIncidentId), new { title = "Forged", description = "Forged" });
        using var delete = await morgan.DeleteAsync(PathFor(DbSeeder.AliceIncidentId));
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        Assert.Equal(before, await StateAsync(DbSeeder.AliceIncidentId));
    }

    [Fact]
    public async Task A05_ListAndExistingReadPaths_AreScopedInDatabase()
    {
        using var alice = await _factory.LoginAsync("alice", CookieClient.SeedPassword);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        var allowed = await db.Incidents.Where(item => item.OwnerUserId == DbSeeder.AliceId).Select(item => item.Id).ToListAsync();
        var list = await alice.GetFromJsonAsync<List<IncidentListItemResponse>>("/api/incidents");
        Assert.NotNull(list);
        Assert.Equal(allowed.OrderBy(id => id), list.Select(item => item.Id).OrderBy(id => id));
        var search = await alice.GetFromJsonAsync<List<SearchIncidentResponse>>("/api/incidents/search");
        Assert.NotNull(search);
        Assert.All(search, item => Assert.Contains(item.Id, allowed));
        var summary = await alice.GetFromJsonAsync<List<IncidentSeveritySummaryResponse>>("/api/incidents/severity-summary");
        Assert.NotNull(summary);
        Assert.Equal(allowed.Count, summary.Sum(item => item.Count));
    }

    [Fact]
    public async Task A06_ReporterCannotTriage_EvenWithForgedRoleAndState()
    {
        using var alice = await _factory.LoginAsync("alice", CookieClient.SeedPassword);
        var id = await CreateAsync(alice);
        try
        {
            var before = await StateAsync(id);
            using var response = await alice.PostAsJsonAsync(PathFor(id) + "/triage", new { role = "Analyst", status = "Triaged" });
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(before, await StateAsync(id));
        }
        finally { await CleanupAsync(id); }
    }

    [Fact]
    public async Task A07_AnalystOnlyNewToTriaged_ServerSelectsNextStatus()
    {
        using var alice = await _factory.LoginAsync("alice", CookieClient.SeedPassword);
        using var morgan = await _factory.LoginAsync("morgan", CookieClient.SeedPassword);
        var id = await CreateAsync(alice);
        try
        {
            var before = await StateAsync(id);
            using var response = await morgan.PostAsJsonAsync(PathFor(id) + "/triage", new { status = "Closed" });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var after = await StateAsync(id);
            Assert.Equal(IncidentStatus.Triaged, after.Status);
            Assert.Equal(before.Owner, after.Owner);
            Assert.Equal(before.Title, after.Title);
            Assert.Equal(before.HistoryCount + 1, after.HistoryCount);
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
            var history = await db.IncidentStatusHistory.SingleAsync(item => item.IncidentId == id);
            Assert.Equal(DbSeeder.MorganId, history.ChangedByUserId);
            Assert.Equal(IncidentStatus.New, history.OldStatus);
            using var repeat = await morgan.PostAsync(PathFor(id) + "/triage", null);
            Assert.Equal(HttpStatusCode.Forbidden, repeat.StatusCode);
            Assert.Equal(after, await StateAsync(id));
        }
        finally { await CleanupAsync(id); }
    }

    [Fact]
    public async Task A08a_UpdateIgnoresOwnerRoleStatusAndSeverity_RealIdentityUnchanged()
    {
        using var alice = await _factory.LoginAsync("alice", CookieClient.SeedPassword);
        var id = await CreateAsync(alice);
        try
        {
            var before = await StateAsync(id);
            using var response = await alice.PutAsJsonAsync(PathFor(id), new
            {
                title = "Allowed content " + id, description = "Allowed edited content.",
                ownerUserId = DbSeeder.BobId, role = "Administrator", status = "Closed", severity = "Critical"
            });
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            var after = await StateAsync(id);
            Assert.Equal("Allowed edited content.", after.Description);
            Assert.Equal(before.Owner, after.Owner);
            Assert.Equal(before.Status, after.Status);
            Assert.Equal(before.Severity, after.Severity);
            var me = await alice.GetFromJsonAsync<CurrentUserResponse>("/api/me");
            Assert.Equal("Reporter", me!.Role);
            await using var scope = _factory.Services.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByIdAsync(DbSeeder.AliceId.ToString());
            Assert.Equal(new[] { "Reporter" }, await users.GetRolesAsync(user!));
        }
        finally { await CleanupAsync(id); }
    }

    [Fact]
    public async Task A09_DeniedAudit_HasSameTraceAsResponse_AndOnlySafeFields()
    {
        var capture = new AuditCapture();
        using var host = _factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(capture)));
        using var alice = host.CreateClient();
        using var login = await alice.PostAsJsonAsync("/api/auth/login", new { userName = "alice", password = CookieClient.SeedPassword });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        using var response = await alice.PostAsJsonAsync(PathFor(DbSeeder.SafeTextIncidentId) + "/triage", new { token = "do-not-log-this-body" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var trace = json.RootElement.GetProperty("traceId").GetString();
        var evt = Assert.Single(capture.Events);
        Assert.Equal(new[] { "Action", "Resource", "Subject", "TraceId", "Utc" }, evt.Keys.OrderBy(key => key));
        Assert.Equal(trace, evt["TraceId"]);
        Assert.Equal(DbSeeder.AliceId, evt["Subject"]);
        Assert.Equal("triage", evt["Action"]);
        var text = string.Join(" ", evt.Values);
        Assert.DoesNotContain(CookieClient.SeedPassword, text);
        Assert.DoesNotContain("do-not-log-this-body", text);
        Assert.DoesNotContain(".AspNetCore.Identity.Application", text);
    }

    [Fact]
    public async Task A12_ChildChecksParent_AndInternalVisibilityMatchesDetails()
    {
        using var alice = await _factory.LoginAsync("alice", CookieClient.SeedPassword);
        using var morgan = await _factory.LoginAsync("morgan", CookieClient.SeedPassword);
        foreach (var id in new[] { "30000000-0000-0000-0000-000000000002", InternalId })
        {
            using var response = await alice.GetAsync("/api/incident-comments/" + id);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.False(json.RootElement.TryGetProperty("text", out _));
        }
        var own = await alice.GetFromJsonAsync<IncidentDetailsResponse>(PathFor(DbSeeder.AliceIncidentId));
        Assert.DoesNotContain(own!.Comments, item => item.Id.ToString() == InternalId);
        var analyst = await morgan.GetFromJsonAsync<IncidentDetailsResponse>(PathFor(DbSeeder.AliceIncidentId));
        Assert.Contains(analyst!.Comments, item => item.Id.ToString() == InternalId);
        using var allowed = await morgan.GetAsync("/api/incident-comments/" + InternalId);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        using var publicOwn = await alice.GetAsync("/api/incident-comments/30000000-0000-0000-0000-000000000001");
        Assert.Equal(HttpStatusCode.OK, publicOwn.StatusCode);
    }

    [Fact]
    public async Task AdministratorHasNoImplicitIncidentBypass_AndOwnerDeleteIsNewOnly()
    {
        using var admin = await _factory.LoginAsync("admin", CookieClient.SeedPassword);
        using var alice = await _factory.LoginAsync("alice", CookieClient.SeedPassword);
        using var read = await admin.GetAsync(PathFor(DbSeeder.AliceIncidentId));
        using var triage = await admin.PostAsync(PathFor(DbSeeder.SafeTextIncidentId) + "/triage", null);
        using var delete = await alice.DeleteAsync(PathFor(DbSeeder.AliceIncidentId));
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, triage.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }

    private static async Task<Guid> CreateAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/incidents", new
        { title = "Lab04-" + Guid.NewGuid(), description = "Synthetic authorization test.", severity = "Low", occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10) });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreatedIncidentResponse>())!.Id;
    }

    private async Task<IncidentState> StateAsync(Guid id)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        return await db.Incidents.AsNoTracking().Where(item => item.Id == id)
            .Select(item => new IncidentState(item.Title, item.Description, item.OwnerUserId, item.Status,
                item.Severity, item.UpdatedAtUtc, item.StatusHistory.Count)).SingleAsync();
    }
    private async Task CleanupAsync(Guid id)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SecureLabDbContext>().Incidents.Where(item => item.Id == id).ExecuteDeleteAsync();
    }
    public void Dispose() => _factory.Dispose();
    private sealed record IncidentState(string Title, string Description, Guid Owner, IncidentStatus Status,
        IncidentSeverity Severity, DateTimeOffset Updated, int HistoryCount);

    private sealed class AuditCapture : ILoggerProvider
    {
        public ConcurrentQueue<Dictionary<string, object?>> Events { get; } = new();
        public ILogger CreateLogger(string name) => new CaptureLogger(this, name == typeof(IncidentAccess).FullName);
        public void Dispose() { }
        private sealed class CaptureLogger(AuditCapture capture, bool enabled) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => enabled;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter)
            {
                if (enabled && state is IEnumerable<KeyValuePair<string, object?>> fields)
                    capture.Events.Enqueue(fields.Where(pair => pair.Key != "{OriginalFormat}").ToDictionary(pair => pair.Key, pair => pair.Value));
            }
        }
    }
}
