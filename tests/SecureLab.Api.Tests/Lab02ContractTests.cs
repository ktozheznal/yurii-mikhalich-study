using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using SecureLab.Api.Scaffolding;

namespace SecureLab.Api.Tests;

public sealed class Lab02ContractTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task T01_ValidCreate_NormalizesDateAndIgnoresServerManagedFields()
    {
        var title = $"Create-{Guid.NewGuid():N}";
        var clientId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow.AddMinutes(-10).ToOffset(TimeSpan.FromHours(3));
        try
        {
            using var response = await _client.PostAsJsonAsync("/api/incidents", new
            {
                title = $"  {title}  ",
                description = "Штучний запис для перевірки контракту створення.",
                severity = "Low",
                occurredAtUtc = occurredAt,
                id = clientId,
                ownerUserId = DbSeeder.BobId,
                status = "Closed",
                createdAtUtc = DateTimeOffset.MinValue
            });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var created = await response.Content.ReadFromJsonAsync<CreatedIncidentResponse>();
            Assert.NotNull(created);
            Assert.NotEqual(clientId, created.Id);
            Assert.Equal(title, created.Title);
            Assert.Equal("New", created.Status);
            Assert.Equal(occurredAt.ToUniversalTime(), created.OccurredAtUtc);
            Assert.Equal(TimeSpan.Zero, created.OccurredAtUtc.Offset);
            Assert.Equal($"/api/incidents/{created.Id}", response.Headers.Location?.ToString());

            using var details = await _client.GetAsync(response.Headers.Location);
            Assert.Equal(HttpStatusCode.OK, details.StatusCode);
            using var document = JsonDocument.Parse(await details.Content.ReadAsStringAsync());
            Assert.Equal("Аліса Коваль",
                document.RootElement.GetProperty("ownerDisplayName").GetString());
        }
        finally
        {
            await DeleteByTitleAsync(title);
        }
    }

    [Theory]
    [InlineData("empty-title", "title")]
    [InlineData("long-title-before-trim", "title")]
    [InlineData("long-description-before-trim", "description")]
    [InlineData("numeric-severity", "severity")]
    [InlineData("missing-date", "occurredAtUtc")]
    [InlineData("future-date", "occurredAtUtc")]
    public async Task T02_InvalidDto_ReturnsSafeValidationProblem(string scenario, string field)
    {
        var title = $"Invalid-{Guid.NewGuid():N}";
        var body = new Dictionary<string, object?>
        {
            ["title"] = title,
            ["description"] = "Штучний запис для негативного тесту.",
            ["severity"] = "Low",
            ["occurredAtUtc"] = DateTimeOffset.UtcNow.AddMinutes(-10)
        };

        switch (scenario)
        {
            case "empty-title": body["title"] = "   "; break;
            case "long-title-before-trim":
                body["title"] = new string('x', 160) + " "; break;
            case "long-description-before-trim":
                body["description"] = new string('x', 4000) + " "; break;
            case "numeric-severity": body["severity"] = "7"; break;
            case "missing-date": body.Remove("occurredAtUtc"); break;
            case "future-date":
                body["occurredAtUtc"] = DateTimeOffset.UtcNow.AddDays(1); break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        try
        {
            using var response = await _client.PostAsJsonAsync("/api/incidents", body);
            await AssertProblemAsync(response, HttpStatusCode.BadRequest, field);
        }
        finally
        {
            await DeleteByTitleAsync(title);
            if (scenario == "long-title-before-trim")
            {
                await DeleteByTitleAsync(new string('x', 160));
            }
        }
    }

    [Fact]
    public async Task T03_DuplicateActiveTitleConflicts_ButClosedAndDifferentCaseDoNot()
    {
        var title = $"Conflict-{Guid.NewGuid():N}";
        try
        {
            using var first = await PostValidAsync(title);
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);

            using var duplicate = await PostValidAsync($"  {title}  ");
            await AssertProblemAsync(duplicate, HttpStatusCode.Conflict);

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
                await db.Incidents.Where(item => item.Title == title)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(item => item.Status, SecureLab.Api.Data.Entities.IncidentStatus.Closed));
            }

            using var afterClose = await PostValidAsync(title);
            Assert.Equal(HttpStatusCode.Created, afterClose.StatusCode);

            using var differentCase = await PostValidAsync(title.ToLowerInvariant());
            Assert.Equal(HttpStatusCode.Created, differentCase.StatusCode);
        }
        finally
        {
            await DeleteByTitleAsync(title);
            await DeleteByTitleAsync(title.ToLowerInvariant());
        }
    }

    [Fact]
    public async Task T09_HighDescription_Rejects39AndAccepts40AfterTrim()
    {
        var title = $"Boundary-{Guid.NewGuid():N}";
        try
        {
            using var tooShort = await PostValidAsync(title, "  " + new string('x', 39) + "  ", "High");
            await AssertProblemAsync(tooShort, HttpStatusCode.BadRequest, "description");

            using var valid = await PostValidAsync(title, "  " + new string('x', 40) + "  ", "High");
            Assert.Equal(HttpStatusCode.Created, valid.StatusCode);
        }
        finally
        {
            await DeleteByTitleAsync(title);
        }
    }

    [Fact]
    public async Task T06_UnknownIncident_ReturnsSafeProblemDetails()
    {
        using var response = await _client.GetAsync(
            "/api/incidents/99999999-9999-9999-9999-999999999999");
        await AssertProblemAsync(response, HttpStatusCode.NotFound);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Інцидент не знайдено",
            document.RootElement.GetProperty("title").GetString());
    }

    private async Task<HttpResponseMessage> PostValidAsync(
        string title, string description = "Штучний опис для перевірки предметного правила.",
        string severity = "Low") =>
        await _client.PostAsJsonAsync("/api/incidents", new
        {
            title,
            description,
            severity,
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
        });

    private async Task DeleteByTitleAsync(string title)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        await db.Incidents.Where(item => item.Title == title).ExecuteDeleteAsync();
    }

    private static async Task AssertProblemAsync(
        HttpResponseMessage response, HttpStatusCode expectedStatus, string? field = null)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        Assert.Equal((int)expectedStatus, document.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("title").GetString()));
        if (field is not null)
        {
            Assert.True(document.RootElement.GetProperty("errors").TryGetProperty(field, out _));
        }
        Assert.DoesNotContain("Exception", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Npgsql", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connection string", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SELECT ", json, StringComparison.OrdinalIgnoreCase);
    }
}
