using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using SecureLab.Api.Presentation.Contracts;
using SecureLab.Api.Scaffolding;

namespace SecureLab.Api.Tests;

// xUnit creates a new instance (and host/quota) for each scenario.
public sealed class Lab03AuthenticationTests : IDisposable
{
    private readonly SecureLabApiFactory _factory = new();

    [Fact]
    public async Task A01_AnonymousMeAndCreate_Return401WithoutRedirect_EvenWithForgedHeader()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Demo-UserId", DbSeeder.BobId.ToString());
        using var me = await client.GetAsync("/api/me");
        AssertUnauthorized(me);
        using var create = await client.PostAsJsonAsync("/api/incidents", IncidentBody());
        AssertUnauthorized(create);
    }

    [Fact]
    public async Task A02_LoginThenMe_ReturnsOnlyVerifiedSafeFields()
    {
        using var client = await _factory.LoginAsync("alice", CookieClient.SeedPassword);
        using var response = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var profile = await response.Content.ReadFromJsonAsync<CurrentUserResponse>();
        Assert.Equal(new CurrentUserResponse(DbSeeder.AliceId, "alice", "Reporter"), profile);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "role", "userId", "userName" },
            json.RootElement.EnumerateObject().Select(item => item.Name).OrderBy(name => name));
    }

    [Fact]
    public async Task A03_ValidAliceCookie_WithBobDemoHeader_DoesNotChangeMe()
    {
        using var client = await _factory.LoginAsync("alice", CookieClient.SeedPassword);
        var before = await client.GetFromJsonAsync<CurrentUserResponse>("/api/me");
        client.DefaultRequestHeaders.Add("X-Demo-UserId", DbSeeder.BobId.ToString());
        var after = await client.GetFromJsonAsync<CurrentUserResponse>("/api/me");
        Assert.NotNull(after);
        Assert.Equal(DbSeeder.AliceId, after.UserId);
        Assert.Equal(before, after);
    }

    [Fact]
    public Task A04_BobCreateWithoutOwner_PersistsBobFromPrincipal() => AssertBobOwnerAsync(false);

    [Fact]
    public Task A05_BobCreateWithForgedAliceOwner_PersistsBobFromPrincipal() => AssertBobOwnerAsync(true);

    [Fact]
    public async Task A06_Logout_SameCookieClientLosesAccessToMeAndCreate()
    {
        using var client = await _factory.LoginAsync("alice", CookieClient.SeedPassword);
        using var before = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        using var logout = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var after = await client.GetAsync("/api/me");
        AssertUnauthorized(after);
        using var create = await client.PostAsJsonAsync("/api/incidents", IncidentBody());
        AssertUnauthorized(create);
    }

    [Fact]
    public async Task A07_UnknownUserAndWrongPassword_HaveSameExternalFailure()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var password = "invalid-" + Guid.NewGuid().ToString("N");
        using var unknown = await client.PostAsJsonAsync("/api/auth/login",
            new { userName = "unknown-" + Guid.NewGuid().ToString("N"), password });
        using var wrong = await client.PostAsJsonAsync("/api/auth/login", new { userName = "alice", password });
        AssertUnauthorized(unknown);
        AssertUnauthorized(wrong);
        Assert.Equal(unknown.Content.Headers.ContentType?.MediaType, wrong.Content.Headers.ContentType?.MediaType);
        Assert.False(unknown.Headers.Contains("Set-Cookie"));
        Assert.False(wrong.Headers.Contains("Set-Cookie"));
        // Trace identifiers may differ; the public problem fields must match.
        using var first = JsonDocument.Parse(await unknown.Content.ReadAsStringAsync());
        using var second = JsonDocument.Parse(await wrong.Content.ReadAsStringAsync());
        foreach (var field in new[] { "type", "title", "status", "detail", "instance" })
        {
            var hasFirst = first.RootElement.TryGetProperty(field, out var value1);
            var hasSecond = second.RootElement.TryGetProperty(field, out var value2);
            Assert.Equal(hasFirst, hasSecond);
            if (hasFirst) Assert.Equal(value1.GetRawText(), value2.GetRawText());
        }
        Assert.True(!first.RootElement.GetRawText().Contains(password, StringComparison.Ordinal));
        Assert.True(!second.RootElement.GetRawText().Contains(password, StringComparison.Ordinal));
    }

    private async Task AssertBobOwnerAsync(bool forgedOwner)
    {
        using var client = await _factory.LoginAsync("bob", CookieClient.SeedPassword);
        var body = IncidentBody();
        var title = (string)body["title"]!;
        if (forgedOwner)
        {
            body["ownerUserId"] = DbSeeder.AliceId;
            body["id"] = DbSeeder.AliceIncidentId;
            body["status"] = "Closed";
            body["role"] = "Administrator";
        }
        try
        {
            using var response = await client.PostAsJsonAsync("/api/incidents", body);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var created = await response.Content.ReadFromJsonAsync<CreatedIncidentResponse>();
            Assert.NotNull(created);
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
            var saved = await db.Incidents.AsNoTracking().SingleAsync(item => item.Id == created.Id);
            Assert.Equal(DbSeeder.BobId, saved.OwnerUserId);
            Assert.Equal(SecureLab.Api.Data.Entities.IncidentStatus.New, saved.Status);
            Assert.NotEqual(DbSeeder.AliceIncidentId, saved.Id);
        }
        finally
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<SecureLabDbContext>()
                .Incidents.Where(item => item.Title == title).ExecuteDeleteAsync();
        }
    }

    internal static Dictionary<string, object?> IncidentBody() => new()
    {
        ["title"] = "Lab03-" + Guid.NewGuid().ToString("N"),
        ["description"] = "Синтетичний інцидент для перевірки автентифікації.",
        ["severity"] = "Low",
        ["occurredAtUtc"] = DateTimeOffset.UtcNow.AddMinutes(-10)
    };

    internal static void AssertUnauthorized(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    public void Dispose() => _factory.Dispose();
}
