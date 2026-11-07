namespace Hally.Api.Common;

/// <summary>Brand and public URLs live here so the product name is configured in one place.</summary>
public class AppOptions
{
    public const string Section = "App";
    public string Name { get; set; } = "Hally";
    /// <summary>Base for Universal Links / App Links, e.g. https://hally.app</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:8080";
}

public class JwtOptions
{
    public const string Section = "Jwt";
    public string Issuer { get; set; } = "hally-api";
    public string Audience { get; set; } = "hally-app";
    /// <summary>HMAC key, at least 32 bytes. Supplied via environment/secret store, never committed.</summary>
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 30;
    public int RefreshTokenDays { get; set; } = 60;
}

public class ExternalAuthOptions
{
    public const string Section = "ExternalAuth";
    public string[] GoogleClientIds { get; set; } = [];
    /// <summary>Apple Services ID / bundle ids accepted as the identity token audience.</summary>
    public string[] AppleAudiences { get; set; } = [];
    /// <summary>Accepts "dev" tokens. Only honoured when the host environment is Development or Testing.</summary>
    public bool EnableDevProvider { get; set; }
}
