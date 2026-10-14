using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SecureLab.Api.Tests;

public sealed class Lab03CookieProfileTests : IDisposable
{
    private readonly SecureLabApiFactory _factory = new();

    [Theory]
    [InlineData("Development", "http://localhost", false)]
    [InlineData("Development", "https://localhost", true)]
    [InlineData("Production", "https://localhost", true)]
    [InlineData("Production", "http://localhost", true)]
    public async Task Login_ActualCookieFlagsMatchEnvironmentAndRequestScheme(
        string environment, string origin, bool secure)
    {
        using var host = _factory.WithWebHostBuilder(builder => builder.UseEnvironment(environment));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(origin), AllowAutoRedirect = false, HandleCookies = true
        });
        using var anonymous = await client.GetAsync("/api/me");
        Lab03AuthenticationTests.AssertUnauthorized(anonymous);
        using var login = await client.PostAsJsonAsync("/api/auth/login",
            new { userName = "alice", password = CookieClient.SeedPassword });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var matching = login.Headers.GetValues("Set-Cookie")
            .Where(value => value.StartsWith(".AspNetCore.Identity.Application=", StringComparison.Ordinal)).ToArray();
        // Never include the cookie value in an assertion failure.
        Assert.True(matching.Length == 1);
        var attributes = matching[0].Split(';').Skip(1)
            .Select(part => part.Trim().ToLowerInvariant()).ToArray();
        Assert.Contains("httponly", attributes);
        Assert.Contains("samesite=lax", attributes);
        Assert.Contains("path=/", attributes);
        Assert.Equal(secure, attributes.Contains("secure"));
        Assert.DoesNotContain(attributes, value => value.StartsWith("expires=", StringComparison.Ordinal));
        Assert.DoesNotContain(attributes, value => value.StartsWith("max-age=", StringComparison.Ordinal));
        if (origin.StartsWith("https:", StringComparison.Ordinal))
        {
            using var profile = await client.GetAsync("/api/me");
            Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        }
    }

    public void Dispose() => _factory.Dispose();
}
