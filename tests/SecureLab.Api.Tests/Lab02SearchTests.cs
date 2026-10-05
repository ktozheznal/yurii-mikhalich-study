using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Scaffolding;

namespace SecureLab.Api.Tests;

public sealed class Lab02SearchTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task T04_OrdinaryAndApostropheTerms_FindSeedRecords()
    {
        var usb = await _client.GetFromJsonAsync<List<SearchIncidentResponse>>(
            "/api/incidents/search?q=USB");
        Assert.NotNull(usb);
        Assert.Contains(usb, row => row.Id == Guid.Parse("20000000-0000-0000-0000-000000000005"));

        var apostrophe = await _client.GetFromJsonAsync<List<SearchIncidentResponse>>(
            "/api/incidents/search?q=O%27Brien");
        Assert.NotNull(apostrophe);
        Assert.Contains(apostrophe, row => row.Id == Guid.Parse("20000000-0000-0000-0000-000000000004"));
    }

    [Fact]
    public async Task S02_ControlInput_DoesNotExpandResults()
    {
        var normal = await _client.GetFromJsonAsync<List<SearchIncidentResponse>>(
            "/api/incidents/search?q=USB");
        Assert.NotNull(normal);
        Assert.Contains(normal, row => row.Id == Guid.Parse("20000000-0000-0000-0000-000000000005"));

        using var response = await _client.GetAsync(
            "/api/incidents/search?q=zz-no-match%27%20OR%20TRUE%20--%20");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<List<SearchIncidentResponse>>();
        Assert.NotNull(results);
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("\\")]
    public async Task Search_TreatsLikeMetacharactersAsLiteralText(string character)
    {
        var prefix = $"Literal-{Guid.NewGuid():N}-";
        var matchingTitle = prefix + character + "End";
        var controlTitle = prefix + "XEnd";
        try
        {
            var expectedId = await CreateAsync(matchingTitle);
            await CreateAsync(controlTitle);

            var term = Uri.EscapeDataString(matchingTitle);
            var rows = await _client.GetFromJsonAsync<List<SearchIncidentResponse>>(
                $"/api/incidents/search?q={term}");
            Assert.NotNull(rows);
            Assert.Equal([expectedId], rows.Select(row => row.Id));
        }
        finally
        {
            await DeleteByTitlesAsync(matchingTitle, controlTitle);
        }
    }

    [Fact]
    public async Task Search_UsesContractRanksAndIdAsTieBreaker()
    {
        var prefix = $"Ranks-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var cases = new[]
        {
            NewIncident(prefix + "-LowA", IncidentSeverity.Low, IncidentStatus.New, now),
            NewIncident(prefix + "-LowB", IncidentSeverity.Low, IncidentStatus.New, now),
            NewIncident(prefix + "-Medium", IncidentSeverity.Medium, IncidentStatus.Triaged, now),
            NewIncident(prefix + "-High", IncidentSeverity.High, IncidentStatus.InProgress, now),
            NewIncident(prefix + "-Critical", IncidentSeverity.Critical, IncidentStatus.Closed, now)
        };
        var ids = cases.Select(item => item.Id).ToArray();

        try
        {
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
                db.Incidents.AddRange(cases);
                await db.SaveChangesAsync();
            }

            var term = Uri.EscapeDataString(prefix);
            var severityRows = await _client.GetFromJsonAsync<List<SearchIncidentResponse>>(
                $"/api/incidents/search?q={term}&sortBy=severity");
            Assert.NotNull(severityRows);
            Assert.Equal(["Critical", "High", "Medium", "Low", "Low"],
                severityRows.Select(row => row.Severity));
            Assert.Equal(ids.Where(id => id == cases[0].Id || id == cases[1].Id).OrderBy(id => id),
                severityRows.Where(row => row.Severity == "Low").Select(row => row.Id));

            var statusRows = await _client.GetFromJsonAsync<List<SearchIncidentResponse>>(
                $"/api/incidents/search?q={term}&sortBy=status");
            Assert.NotNull(statusRows);
            Assert.Equal(["New", "New", "Triaged", "InProgress", "Closed"],
                statusRows.Select(row => row.Status));
            Assert.Equal(ids.Where(id => id == cases[0].Id || id == cases[1].Id).OrderBy(id => id),
                statusRows.Where(row => row.Status == "New").Select(row => row.Id));
        }
        finally
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
            await db.Incidents.Where(item => ids.Contains(item.Id)).ExecuteDeleteAsync();
        }
    }

    [Theory]
    [InlineData("price")]
    [InlineData("created_at_utc")]
    public async Task T05_UnknownSort_ReturnsSafeValidationProblem(string sortBy)
    {
        using var response = await _client.GetAsync($"/api/incidents/search?sortBy={sortBy}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("sortBy", out _));
        Assert.DoesNotContain("Exception", json, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Guid> CreateAsync(string title)
    {
        using var response = await _client.PostAsJsonAsync("/api/incidents", new
        {
            title,
            description = "Штучний запис для перевірки буквального пошуку.",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CreatedIncidentResponse>();
        Assert.NotNull(created);
        return created.Id;
    }

    private async Task DeleteByTitlesAsync(params string[] titles)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        await db.Incidents.Where(item => titles.Contains(item.Title)).ExecuteDeleteAsync();
    }

    private static Incident NewIncident(
        string title, IncidentSeverity severity, IncidentStatus status, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        OwnerUserId = DbSeeder.AliceId,
        Title = title,
        Description = "Штучний опис для перевірки порядку пошуку.",
        Severity = severity,
        Status = status,
        OccurredAtUtc = now,
        CreatedAtUtc = now,
        UpdatedAtUtc = now
    };
}
