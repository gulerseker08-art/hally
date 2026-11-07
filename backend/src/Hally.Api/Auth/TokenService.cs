using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Hally.Api.Common;
using Hally.Domain.Users;
using Hally.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Hally.Api.Auth;

public record TokenPair(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);

public class TokenService(IOptions<JwtOptions> options, HallyDbContext db, TimeProvider clock)
{
    private readonly JwtOptions _jwt = options.Value;

    public static SymmetricSecurityKey KeyFrom(JwtOptions jwt) => new(Encoding.UTF8.GetBytes(jwt.SigningKey));

    public async Task<TokenPair> IssueAsync(User user, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var accessExpires = now.AddMinutes(_jwt.AccessTokenMinutes);
        var jwt = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim("locale", user.Locale),
            ],
            notBefore: now.UtcDateTime,
            expires: accessExpires.UtcDateTime,
            signingCredentials: new SigningCredentials(KeyFrom(_jwt), SecurityAlgorithms.HmacSha256));

        var refresh = Crypto.NewToken();
        var refreshExpires = now.AddDays(_jwt.RefreshTokenDays);
        db.RefreshTokens.Add(new RefreshToken(user.Id, Crypto.Sha256(refresh), refreshExpires));
        await db.SaveChangesAsync(ct);

        return new TokenPair(new JwtSecurityTokenHandler().WriteToken(jwt), accessExpires, refresh, refreshExpires);
    }

    /// <summary>Rotates a refresh token. Reusing a revoked token revokes every token of that user.</summary>
    public async Task<TokenPair?> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var hash = Crypto.Sha256(refreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null)
        {
            return null;
        }

        if (!stored.IsActive(now))
        {
            if (stored.RevokedAt is not null)
            {
                await db.RefreshTokens.Where(t => t.UserId == stored.UserId && t.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
            }

            return null;
        }

        stored.Revoke(now);
        var user = await db.Users.FirstAsync(u => u.Id == stored.UserId, ct);
        return await IssueAsync(user, ct);
    }
}
