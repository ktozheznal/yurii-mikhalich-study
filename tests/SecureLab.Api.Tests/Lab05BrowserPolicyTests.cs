using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace SecureLab.Api.Tests;

public sealed class Lab05BrowserPolicyTests : IDisposable
{
    private const string AllowedOrigin = "http://localhost:4173";
    private readonly SecureLabApiFactory _factory = new();

    [Fact]
    public async Task AllowedSimpleRequest_DeclaresExactOriginAndCredentials()
    {
        using var client = _factory.CreateClient();
        using var request = WithOrigin(HttpMethod.Get, "/health", AllowedOrigin);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(AllowedOrigin, Header(response, "Access-Control-Allow-Origin"));
        Assert.Equal("true", Header(response, "Access-Control-Allow-Credentials"));
    }

    [Theory]
    [InlineData("http://localhost:4174")]
    [InlineData("http://127.0.0.1:4173")]
    [InlineData("https://localhost:4173")]
    [InlineData("null")]
    public async Task OtherOrigins_GetActualResponseWithoutCorsReadPermission(string origin)
    {
        using var client = _factory.CreateClient();
        using var request = WithOrigin(HttpMethod.Get, "/health", origin);
        using var response = await client.SendAsync(request);

        // Direct HTTP receives the response. A browser enforces the missing CORS permission.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(Header(response, "Access-Control-Allow-Origin"));
        Assert.Null(Header(response, "Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task AllowedPreflight_DeclaresOnlyGetAndProbeHeader()
    {
        using var client = _factory.CreateClient();
        using var request = Preflight(AllowedOrigin);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(AllowedOrigin, Header(response, "Access-Control-Allow-Origin"));
        Assert.Equal("true", Header(response, "Access-Control-Allow-Credentials"));
        Assert.Equal("GET", Header(response, "Access-Control-Allow-Methods"));
        Assert.Equal("x-lab-cors-probe", Header(response, "Access-Control-Allow-Headers")?.ToLowerInvariant());
    }

    [Fact]
    public async Task DisallowedPreflight_HasNoCorsPermission()
    {
        using var client = _factory.CreateClient();
        using var request = Preflight("http://localhost:4174");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(Header(response, "Access-Control-Allow-Origin"));
        Assert.Null(Header(response, "Access-Control-Allow-Credentials"));
        Assert.Null(Header(response, "Access-Control-Allow-Methods"));
        Assert.Null(Header(response, "Access-Control-Allow-Headers"));
    }

    [Fact]
    public async Task AllowedAnonymousProtectedRequest_IsReadable401WithoutRedirect()
    {
        using var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        using var request = WithOrigin(HttpMethod.Get, "/api/me", AllowedOrigin);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal(AllowedOrigin, Header(response, "Access-Control-Allow-Origin"));
        Assert.Equal("true", Header(response, "Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task AllowedAuthenticatedProtectedRequest_KeepsIdentityAndAuthorization()
    {
        using var alice = await _factory.LoginAsync("alice", CookieClient.SeedPassword);
        using var profile = WithOrigin(HttpMethod.Get, "/api/me", AllowedOrigin);
        using var response = await alice.SendAsync(profile);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(AllowedOrigin, Header(response, "Access-Control-Allow-Origin"));
        Assert.Equal("true", Header(response, "Access-Control-Allow-Credentials"));

        using var foreign = WithOrigin(HttpMethod.Get,
            "/api/incidents/20000000-0000-0000-0000-000000000002", AllowedOrigin);
        using var forbidden = await alice.SendAsync(foreign);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(AllowedOrigin, Header(forbidden, "Access-Control-Allow-Origin"));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/auth-lab.html")]
    [InlineData("/browser-lab.html")]
    public async Task HtmlDocuments_HaveEnforcedSecurityHeaders(string path)
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
        var csp = Header(response, "Content-Security-Policy");
        Assert.NotNull(csp);
        Assert.Contains("default-src 'self'", csp);
        Assert.Contains("script-src 'self'", csp);
        Assert.Contains("object-src 'none'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.DoesNotContain("unsafe-inline", csp);
        Assert.Null(Header(response, "Content-Security-Policy-Report-Only"));
    }

    [Fact]
    public async Task Production_DoesNotEnableLocalProbeCorsPolicy()
    {
        using var host = _factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = host.CreateClient();
        using var request = WithOrigin(HttpMethod.Get, "/health", AllowedOrigin);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(Header(response, "Access-Control-Allow-Origin"));
        Assert.Null(Header(response, "Access-Control-Allow-Credentials"));
        Assert.Contains("frame-ancestors 'none'", Header(response, "Content-Security-Policy") ?? "");
    }

    private static HttpRequestMessage WithOrigin(HttpMethod method, string path, string origin)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Origin", origin);
        return request;
    }

    private static HttpRequestMessage Preflight(string origin)
    {
        var request = WithOrigin(HttpMethod.Options, "/health", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "x-lab-cors-probe");
        return request;
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(", ", values) : null;

    public void Dispose() => _factory.Dispose();
}
