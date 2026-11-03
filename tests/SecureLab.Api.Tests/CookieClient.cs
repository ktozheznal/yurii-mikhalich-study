using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SecureLab.Api.Tests;

public static class CookieClient
{
    public static string SeedPassword => Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    public static async Task<HttpClient> LoginAsync(this WebApplicationFactory<Program> factory, string userName, string password,
        bool antiforgery = true)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
        });
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { userName, password });
               if (!response.IsSuccessStatusCode)
        {
            client.Dispose();
            throw new InvalidOperationException(
                $"Login HTTP {(int)response.StatusCode} ({response.StatusCode})");
        }
        if (antiforgery) await client.UseAntiforgeryTokenAsync();
        return client;
    }

    public static async Task UseAntiforgeryTokenAsync(this HttpClient client)
    {
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        using var response = await client.GetAsync("/api/antiforgery/token");
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Token HTTP {(int)response.StatusCode}");
        var body = await response.Content.ReadFromJsonAsync<TokenResponse>();
        if (string.IsNullOrWhiteSpace(body?.Token)) throw new InvalidOperationException("Missing request token.");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", body.Token);
    }

    private sealed record TokenResponse(string Token);
}
