using System.IdentityModel.Tokens.Jwt;
using Google.Apis.Auth;
using Hally.Api.Common;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Hally.Api.Auth;

public record ExternalIdentity(string Provider, string Subject, string Email, string? Name);

/// <summary>Verifies an ID token issued by an external identity provider.</summary>
public interface IExternalIdentityVerifier
{
    string Provider { get; }
    Task<ExternalIdentity?> VerifyAsync(string idToken, CancellationToken ct);
}

public class GoogleIdentityVerifier(IOptions<ExternalAuthOptions> options, ILogger<GoogleIdentityVerifier> logger) : IExternalIdentityVerifier
{
    public string Provider => "google";

    public async Task<ExternalIdentity?> VerifyAsync(string idToken, CancellationToken ct)
    {
        if (options.Value.GoogleClientIds.Length == 0)
        {
            logger.LogWarning("Google sign-in attempted but ExternalAuth:GoogleClientIds is not configured");
            return null;
        }

        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken,
                new GoogleJsonWebSignature.ValidationSettings { Audience = options.Value.GoogleClientIds });
            return payload.EmailVerified ? new ExternalIdentity(Provider, payload.Subject, payload.Email, payload.Name) : null;
        }
        catch (InvalidJwtException ex)
        {
            logger.LogInformation("Rejected Google token: {Reason}", ex.Message);
            return null;
        }
    }
}

public class AppleIdentityVerifier : IExternalIdentityVerifier
{
    private const string AppleIssuer = "https://appleid.apple.com";
    private readonly ConfigurationManager<OpenIdConnectConfiguration> _config = new(
        $"{AppleIssuer}/.well-known/openid-configuration", new OpenIdConnectConfigurationRetriever());
    private readonly IOptions<ExternalAuthOptions> _options;
    private readonly ILogger<AppleIdentityVerifier> _logger;

    public AppleIdentityVerifier(IOptions<ExternalAuthOptions> options, ILogger<AppleIdentityVerifier> logger)
    {
        _options = options;
        _logger = logger;
    }

    public string Provider => "apple";

    public async Task<ExternalIdentity?> VerifyAsync(string idToken, CancellationToken ct)
    {
        if (_options.Value.AppleAudiences.Length == 0)
        {
            _logger.LogWarning("Apple sign-in attempted but ExternalAuth:AppleAudiences is not configured");
            return null;
        }

        var oidc = await _config.GetConfigurationAsync(ct);
        var parameters = new TokenValidationParameters
        {
            ValidIssuer = AppleIssuer,
            ValidAudiences = _options.Value.AppleAudiences,
            IssuerSigningKeys = oidc.SigningKeys,
        };

        try
        {
            var principal = new JwtSecurityTokenHandler { MapInboundClaims = false }.ValidateToken(idToken, parameters, out _);
            var sub = principal.FindFirst("sub")?.Value;
            var email = principal.FindFirst("email")?.Value;
            // Apple only sends the user's name to the app on first sign-in; the app passes it separately.
            return sub is null || email is null ? null : new ExternalIdentity(Provider, sub, email, null);
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogInformation("Rejected Apple token: {Reason}", ex.Message);
            return null;
        }
    }
}

/// <summary>
/// Development/test only. Token format: <c>dev:{email}:{display name}</c>. Never enabled in Production.
/// </summary>
public class DevIdentityVerifier(IOptions<ExternalAuthOptions> options, IHostEnvironment env) : IExternalIdentityVerifier
{
    public string Provider => "dev";

    public Task<ExternalIdentity?> VerifyAsync(string idToken, CancellationToken ct)
    {
        var allowed = options.Value.EnableDevProvider && (env.IsDevelopment() || env.IsEnvironment("Testing"));
        var parts = idToken.Split(':', 3);
        if (!allowed || parts.Length < 2 || parts[0] != "dev" || !parts[1].Contains('@'))
        {
            return Task.FromResult<ExternalIdentity?>(null);
        }

        var email = parts[1].ToLowerInvariant();
        return Task.FromResult<ExternalIdentity?>(new ExternalIdentity(Provider, email, email, parts.Length == 3 ? parts[2] : null));
    }
}
