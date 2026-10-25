using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;
using Xunit.Abstractions;

namespace SecureLab.Api.Tests;

public sealed class D7ForwardedForTests
{
    private readonly ITestOutputHelper _output;

    public D7ForwardedForTests(ITestOutputHelper output)
    {
        _output = output;
    }


    [Fact]
    public async Task Login_RawXff_DoesNotSplitQuota()
    {
        using var factory = new SecureLabApiFactory();

        using var host = factory.WithWebHostBuilder(builder =>{
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["LoginRateLimit:PermitLimit"] = "3",
                        ["LoginRateLimit:WindowMinutes"] = "2"
                    });
            });
        });
    

    using var handler = host.Server.CreateHandler(context => 
    {
        var remoteIp = context.Connection.RemoteIpAddress;
        var key = remoteIp?.ToString() ?? "unknown";
        var xff = context.Request.Headers["X-Forwarded-For"];

        _output.WriteLine(
        $"XFF={xff}" +
         $"RemoteIpAddress={remoteIp?.ToString() ?? "<null>"}" + 
         $"partitionKey={key}");

        Assert.Null(remoteIp);
        Assert.Equal("unknown", key);
    });

    using var client = new HttpClient(handler){
        BaseAddress = new Uri("http://localhost"),
        Timeout = TimeSpan.FromSeconds(20)
    };
    var statuses = new List<int>();
    var elapsed = Stopwatch.StartNew();

    for(var i = 1; i <= 4; i++){
        var xff = $"203.0.113.{i}";

    using var request = new HttpRequestMessage(
            HttpMethod.Post, "/api/auth/login")
        {
        Content = JsonContent.Create(new
        {
            username = "ghost-xff",
            password = "Synthetic-Wrong-1!"
        }),

    };

        request.Headers.Add("X-Forwarded-For", xff);

        using var response = await client.SendAsync(request);
        var status = (int)response.StatusCode;
        statuses.Add(status);

        _output.WriteLine($"XFF={xff}; stauts={status}");
    }
        elapsed.Stop();
    _output.WriteLine($"Elapsed={elapsed.Elapsed}");

    Assert.Equal(new[] {401, 401, 401, 429}, statuses.ToArray());
    Assert.True(
        elapsed.Elapsed < TimeSpan.FromMinutes(2),
        "All 4 requests must be sent in one window");

        }
        }