using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Tests;

public sealed class Lab03RegistrationTests : IDisposable
{
    private readonly SecureLabApiFactory _factory = new();

    [Fact]
    public async Task T01_Register_UsesStandardVerifierAndServerManagedIdAndRole()
    {
        using var client = _factory.CreateClient();
        var name = "reg-" + Guid.NewGuid().ToString("N");
        var password = "Aa1!" + Guid.NewGuid().ToString("N");
        try
        {
            using var response = await client.PostAsJsonAsync("/api/auth/register", new
            {
                userName = name, email = name + "@example.test", displayName = "Тестова особа", password,
                id = DbSeeder.AdminId, role = "Administrator", passwordHash = "forged"
            });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(new[] { "displayName", "id", "userName" },
                json.RootElement.EnumerateObject().Select(item => item.Name).OrderBy(item => item));
            var registered = await response.Content.ReadFromJsonAsync<RegisteredUserResponse>();
            Assert.NotNull(registered);
            Assert.NotEqual(DbSeeder.AdminId, registered.Id);
            await using var scope = _factory.Services.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByNameAsync(name);
            Assert.NotNull(user);
            Assert.True(await users.HasPasswordAsync(user));
            Assert.True(await users.CheckPasswordAsync(user, password));
            Assert.True(user.PasswordHash != password);
            Assert.Equal(new[] { DbSeeder.ReporterRole }, await users.GetRolesAsync(user));
        }
        finally { await DeleteUserAsync(name); }
    }

    [Theory]
    [InlineData("same-name")]
    [InlineData("same-email")]
    public async Task DuplicateRegistration_ReturnsConsistent409_WithoutPartialAccount(string scenario)
    {
        using var client = _factory.CreateClient();
        var name = "dup-" + Guid.NewGuid().ToString("N");
        var otherName = name + "-other";
        var password = "Aa1!" + Guid.NewGuid().ToString("N");
        var email = name + "@example.test";
        try
        {
            using var first = await client.PostAsJsonAsync("/api/auth/register",
                new { userName = name, email, displayName = "Тест", password });
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var duplicate = await client.PostAsJsonAsync("/api/auth/register", new
                {
                    userName = scenario == "same-name" ? name.ToUpperInvariant() : otherName,
                    email = scenario == "same-email" ? email.ToUpperInvariant() : otherName + "@example.test",
                    displayName = "Тест", password
                });
                Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
                using var problem = JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync());
                Assert.Equal("Обліковий запис уже існує", problem.RootElement.GetProperty("title").GetString());
                Assert.True(!problem.RootElement.GetRawText().Contains(password, StringComparison.Ordinal));
            }
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
            Assert.Equal(1, await db.Users.CountAsync(user => user.UserName == name || user.UserName == otherName));
        }
        finally { await DeleteUserAsync(name); await DeleteUserAsync(otherName); }
    }

    [Theory]
    [InlineData("missing-name")]
    [InlineData("long-name")]
    [InlineData("bad-email")]
    [InlineData("long-email")]
    [InlineData("long-display-name")]
    [InlineData("weak-password")]
    [InlineData("long-password")]
    [InlineData("missing-password")]
    public async Task T02_InvalidRegistration_ReturnsSafe400_AndDoesNotSaveUser(string scenario)
    {
        using var client = _factory.CreateClient();
        var name = "invalid-" + Guid.NewGuid().ToString("N");
        var body = new Dictionary<string, object?>
        {
            ["userName"] = name, ["email"] = name + "@example.test",
            ["displayName"] = "Тест", ["password"] = "Aa1!" + Guid.NewGuid().ToString("N")
        };
        switch (scenario)
        {
            case "missing-name": body["userName"] = "  "; break;
            case "long-name": body["userName"] = new string('x', 65); break;
            case "bad-email": body["email"] = "invalid"; break;
            case "long-email": body["email"] = new string('x', 255) + "@example.test"; break;
            case "long-display-name": body["displayName"] = new string('x', 121); break;
            case "weak-password": body["password"] = new string('x', 12); break;
            case "long-password": body["password"] = new string('x', 129); break;
            case "missing-password": body.Remove("password"); break;
        }
        using var response = await client.PostAsJsonAsync("/api/auth/register", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Реєстрацію відхилено", json.RootElement.GetProperty("title").GetString());
        await using var scope = _factory.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<SecureLabDbContext>().Users
            .AnyAsync(user => user.UserName == (string?)body["userName"]));
    }

    private async Task DeleteUserAsync(string name)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByNameAsync(name);
        if (user is not null) Assert.True((await users.DeleteAsync(user)).Succeeded);
    }

    public void Dispose() => _factory.Dispose();
}
