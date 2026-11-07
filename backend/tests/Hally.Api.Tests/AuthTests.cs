using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Hally.Api.Tests;

[Collection(ApiCollection.Name)]
public class AuthTests(HallyApiFactory api)
{
    [Fact]
    public async Task Sign_in_twice_returns_the_same_user()
    {
        var client = api.CreateClient();
        var email = $"{Guid.NewGuid():N}@example.com";
        var first = await (await client.PostAsJsonAsync("/api/v1/auth/dev", new { idToken = $"dev:{email}:Zeynep" })).Content.ReadFromJsonAsync<AuthBody>();
        var second = await (await client.PostAsJsonAsync("/api/v1/auth/dev", new { idToken = $"dev:{email}:Zeynep" })).Content.ReadFromJsonAsync<AuthBody>();

        first!.IsNewUser.Should().BeTrue();
        second!.IsNewUser.Should().BeFalse();
        second.UserId.Should().Be(first.UserId);
    }

    [Fact]
    public async Task Invalid_token_is_rejected()
    {
        var res = await api.CreateClient().PostAsJsonAsync("/api/v1/auth/dev", new { idToken = "garbage" });
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Unconfigured_google_provider_rejects_tokens()
    {
        var res = await api.CreateClient().PostAsJsonAsync("/api/v1/auth/google", new { idToken = "eyJhbGciOi.fake.token" });
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_token_rotates_and_reuse_revokes_the_family_of_tokens()
    {
        var client = api.CreateClient();
        var auth = await (await client.PostAsJsonAsync("/api/v1/auth/dev", new { idToken = $"dev:{Guid.NewGuid():N}@example.com:R" })).Content.ReadFromJsonAsync<AuthBody>();

        var rotated = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = auth!.Tokens.RefreshToken });
        rotated.StatusCode.Should().Be(HttpStatusCode.OK);
        var next = await rotated.Content.ReadFromJsonAsync<TokensBody>();

        // Replaying the old token is treated as theft: it fails and the new token is revoked too.
        (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = auth.Tokens.RefreshToken }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = next!.RefreshToken }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Locale_is_normalised_to_supported_languages()
    {
        var client = await api.SignInAsync($"{Guid.NewGuid():N}@example.com", "Jane", "en-GB");
        var me = await client.GetFromJsonAsync<MeBody>("/api/v1/me");
        me!.Locale.Should().Be("en");
    }

    [Fact]
    public async Task Health_endpoints_respond()
    {
        var client = api.CreateClient();
        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
