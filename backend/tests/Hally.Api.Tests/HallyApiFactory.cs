using System.Net.Http.Headers;
using System.Net.Http.Json;
using Hally.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace Hally.Api.Tests;

/// <summary>
/// Boots the real API against a real PostgreSQL. Uses Testcontainers by default; set
/// HALLY_TEST_CONNECTION (a server connection string) to reuse an existing server instead.
/// </summary>
public sealed class HallyApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private string _connectionString = default!;

    public TestClock Clock { get; } = new();

    public async Task InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable("HALLY_TEST_CONNECTION");
        if (!string.IsNullOrWhiteSpace(external))
        {
            _connectionString = $"{external.TrimEnd(';')};Database=hally_test_{Guid.NewGuid():N}";
        }
        else
        {
            _container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
            await _container.StartAsync();
            _connectionString = _container.GetConnectionString();
        }

        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<HallyDbContext>().Database.MigrateAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Hally", _connectionString);
        builder.UseSetting("Jwt:SigningKey", "test-signing-key-0123456789-abcdefghijkl");
        builder.UseSetting("ExternalAuth:EnableDevProvider", "true");
        builder.UseSetting("App:PublicBaseUrl", "https://hally.test");
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(Clock);
        });
    }

    public new async Task DisposeAsync()
    {
        if (_container is null)
        {
            using var scope = Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<HallyDbContext>().Database.EnsureDeletedAsync();
        }

        await base.DisposeAsync();
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>Signs a user in through the dev provider and returns an authenticated client.</summary>
    public async Task<HttpClient> SignInAsync(string email, string name, string locale = "tr")
    {
        var client = CreateClient();
        var res = await client.PostAsJsonAsync("/api/v1/auth/dev", new { idToken = $"dev:{email}:{name}", locale });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<AuthBody>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Tokens.AccessToken);
        return client;
    }
}

public record TokensBody(string AccessToken, string RefreshToken);
public record AuthBody(Guid UserId, bool IsNewUser, TokensBody Tokens);

[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<HallyApiFactory>
{
    public const string Name = "api";
}

public static class HttpExtensions
{
    public static Task<HttpResponseMessage> PostIdempotentAsync(this HttpClient client, string url, object? body, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body ?? new { }) };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        return client.SendAsync(request);
    }
}

/// <summary>System time plus an adjustable offset. Starts at real time because JWT lifetime
/// validation in the bearer handler uses the system clock.</summary>
public sealed class TestClock : TimeProvider
{
    public TimeSpan Offset { get; private set; }

    public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + Offset;

    public void Advance(TimeSpan by) => Offset += by;
}
