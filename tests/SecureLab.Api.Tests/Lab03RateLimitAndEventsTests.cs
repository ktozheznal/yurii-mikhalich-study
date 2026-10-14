using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using SecureLab.Api.Scaffolding;

namespace SecureLab.Api.Tests;

public sealed class Lab03RateLimitAndEventsTests : IDisposable
{
    private readonly SecureLabApiFactory _factory = new();

    [Fact]
    public async Task LoginQuota_IsSharedAcrossClients_RecoversAfterRealWindow_AndFreshHostIsIndependent()
    {
        using var host = _factory.WithWebHostBuilder(_ => { });
        using var first = host.CreateClient();
        using var second = host.CreateClient();
        var failedLogin = new { userName = "unknown", password = Guid.NewGuid().ToString("N") };
        for (var i = 0; i < 5; i++)
        {
            using var accepted = await first.PostAsJsonAsync("/api/auth/login", failedLogin);
            Assert.Equal(HttpStatusCode.Unauthorized, accepted.StatusCode);
        }
        // TestServer clients have null RemoteIpAddress, so both share "unknown".
        using var limited = await second.PostAsJsonAsync("/api/auth/login", failedLogin);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.False(limited.Headers.Contains("Set-Cookie"));
        using var publicRead = await second.GetAsync("/api/incidents");
        Assert.Equal(HttpStatusCode.OK, publicRead.StatusCode);
        using var newHost = _factory.WithWebHostBuilder(_ => { });
        using var independent = newHost.CreateClient();
        using var fresh = await independent.PostAsJsonAsync("/api/auth/login", failedLogin);
        Assert.Equal(HttpStatusCode.Unauthorized, fresh.StatusCode);
        await Task.Delay(TimeSpan.FromSeconds(61));
        using var recovered = await second.PostAsJsonAsync("/api/auth/login", failedLogin);
        Assert.Equal(HttpStatusCode.Unauthorized, recovered.StatusCode);
    }

    [Fact]
    public async Task AuthenticationEvents_RecordSuccessFailureLogoutAndLimit_WithoutCredentials()
    {
        var capture = new AuthenticationLogCapture();
        using var host = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureLogging(logging => logging.AddProvider(capture)));
        using var client = host.CreateClient();
        var password = CookieClient.SeedPassword;
        using var success = await client.PostAsJsonAsync("/api/auth/login", new { userName = "alice", password });
        Assert.Equal(HttpStatusCode.NoContent, success.StatusCode);
        using var logout = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        var unverifiedName = "unknown-" + Guid.NewGuid().ToString("N");
        var wrongPassword = Guid.NewGuid().ToString("N");
        for (var i = 0; i < 4; i++)
        {
            using var failure = await client.PostAsJsonAsync("/api/auth/login",
                new { userName = unverifiedName, password = wrongPassword });
            Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
        }
        using var limited = await client.PostAsJsonAsync("/api/auth/login",
            new { userName = unverifiedName, password = wrongPassword });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        var events = capture.Events.ToArray();
        Assert.Contains(events, item => item["EventType"]?.ToString() == "login" && item["Result"]?.ToString() == "success");
        Assert.Contains(events, item => item["Result"]?.ToString() == "failure");
        Assert.Contains(events, item => item["EventType"]?.ToString() == "logout");
        Assert.Contains(events, item => item["Result"]?.ToString() == "rate-limited");
        foreach (var item in events)
        {
            Assert.Equal(new[] { "EventType", "Result", "TimestampUtc", "TraceId", "UserId" },
                item.Keys.OrderBy(key => key));
            Assert.IsType<DateTimeOffset>(item["TimestampUtc"]);
            Assert.False(string.IsNullOrWhiteSpace(item["TraceId"]?.ToString()));
            var text = string.Join(" ", item.Values);
            Assert.True(!text.Contains(password, StringComparison.Ordinal));
            Assert.True(!text.Contains(wrongPassword, StringComparison.Ordinal));
            Assert.True(!text.Contains(unverifiedName, StringComparison.Ordinal));
            Assert.True(!text.Contains(".AspNetCore.Identity.Application", StringComparison.Ordinal));
        }
    }

    public void Dispose() => _factory.Dispose();

    private sealed class AuthenticationLogCapture : ILoggerProvider
    {
        public ConcurrentQueue<Dictionary<string, object?>> Events { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(this,
            categoryName == typeof(AuthenticationEvents).FullName);
        public void Dispose() { }

        private sealed class CaptureLogger(AuthenticationLogCapture capture, bool enabled) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => enabled;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (enabled && state is IEnumerable<KeyValuePair<string, object?>> fields)
                    capture.Events.Enqueue(fields.Where(pair => pair.Key != "{OriginalFormat}")
                        .ToDictionary(pair => pair.Key, pair => pair.Value));
            }
        }
    }
}
