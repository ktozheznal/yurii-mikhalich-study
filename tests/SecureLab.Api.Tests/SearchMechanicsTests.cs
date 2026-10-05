using System.Net;

namespace SecureLab.Api.Tests;

public sealed class SearchMechanicsTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    [Fact]
    public async Task Search_ReturnsJson()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/incidents/search?q=навчальн");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Search_AllowsApostropheInTerm_AndRejectsUnsafeSort()
    {
        using var client = factory.CreateClient();

        using var validResponse = await client.GetAsync("/api/incidents/search?q=O%27Brien&sortBy=createdAtUtc");
        Assert.Equal(HttpStatusCode.OK, validResponse.StatusCode);

        using var invalidResponse = await client.GetAsync("/api/incidents/search?q=test&sortBy=price");
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
    }
}
