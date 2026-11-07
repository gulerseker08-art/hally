using FluentValidation;
using Hally.Api.Auth;
using Hally.Api.Common;
using Hally.Domain.Users;
using Hally.Infrastructure.Analytics;
using Hally.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hally.Api.Features.Auth;

public record SignInRequest(string IdToken, string? DisplayName, string? Locale);
public record RefreshRequest(string RefreshToken);
public record AuthResponse(Guid UserId, bool IsNewUser, TokenPair Tokens);

public class SignInRequestValidator : AbstractValidator<SignInRequest>
{
    public SignInRequestValidator()
    {
        RuleFor(x => x.IdToken).NotEmpty().MaximumLength(8192);
        RuleFor(x => x.DisplayName).MaximumLength(80);
        RuleFor(x => x.Locale).MaximumLength(10);
    }
}

[ApiController]
[Route("api/v1/auth")]
[AllowAnonymous]
public class AuthController(
    IEnumerable<IExternalIdentityVerifier> verifiers,
    HallyDbContext db,
    TokenService tokens,
    IAnalytics analytics) : ControllerBase
{
    /// <summary>Exchanges a Google or Apple ID token for Hally access and refresh tokens.</summary>
    [HttpPost("{provider}")]
    [ProducesResponseType<AuthResponse>(200)]
    [ProducesResponseType<ProblemDetails>(401)]
    public async Task<IActionResult> SignIn(string provider, SignInRequest request, CancellationToken ct)
    {
        var verifier = verifiers.FirstOrDefault(v => v.Provider == provider.ToLowerInvariant());
        if (verifier is null)
        {
            return Problem(statusCode: 404, title: $"Unknown provider '{provider}'.");
        }

        var identity = await verifier.VerifyAsync(request.IdToken, ct);
        if (identity is null)
        {
            return Problem(statusCode: 401, title: "The identity token could not be verified.");
        }

        var login = await db.ExternalLogins.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Provider == identity.Provider && l.Subject == identity.Subject, ct);

        User user;
        var isNew = false;
        if (login is not null)
        {
            user = await db.Users.FirstAsync(u => u.Id == login.UserId, ct);
        }
        else
        {
            var name = request.DisplayName ?? identity.Name ?? identity.Email.Split('@')[0];
            user = new User(identity.Email, name, request.Locale ?? Request.Headers.AcceptLanguage.ToString());
            user.LinkLogin(identity.Provider, identity.Subject);
            db.Users.Add(user);
            isNew = true;
        }

        analytics.Track(isNew ? AnalyticsEvents.SignedUp : AnalyticsEvents.SignedIn, user.Id, null,
            new Dictionary<string, object?> { ["provider"] = identity.Provider });
        await db.SaveChangesAsync(ct);

        return Ok(new AuthResponse(user.Id, isNew, await tokens.IssueAsync(user, ct)));
    }

    [HttpPost("refresh")]
    [ProducesResponseType<TokenPair>(200)]
    public async Task<IActionResult> Refresh(RefreshRequest request, CancellationToken ct)
    {
        var pair = await tokens.RefreshAsync(request.RefreshToken, ct);
        return pair is null ? Problem(statusCode: 401, title: "Refresh token is invalid or expired.") : Ok(pair);
    }
}
