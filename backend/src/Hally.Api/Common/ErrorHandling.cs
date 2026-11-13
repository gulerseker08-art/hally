using Hally.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Hally.Api.Common;

/// <summary>Maps exceptions to RFC 9457 problem details.</summary>
public class ProblemExceptionHandler(IProblemDetailsService problems, ILogger<ProblemExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var (status, code, title) = ex switch
        {
            DomainException d => (StatusCodes.Status422UnprocessableEntity, d.Code, d.Message),
            NotFoundException n => (StatusCodes.Status404NotFound, n.Code, n.Message),
            Features.Proposals.ConflictException c => (StatusCodes.Status409Conflict, c.Code, c.Message),
            Features.Billing.PlanLimitException l => (StatusCodes.Status402PaymentRequired, l.Code, l.Message),
            Features.Billing.BillingUnavailableException b => (StatusCodes.Status503ServiceUnavailable, "billing.store_unavailable", b.Message),
            Infrastructure.Ai.AiUnavailableException => (StatusCodes.Status503ServiceUnavailable, "ai.unavailable", "The AI assistant is temporarily unavailable."),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "auth.unauthorized", "Unauthorized."),
            _ => (StatusCodes.Status500InternalServerError, "server.error", "An unexpected error occurred."),
        };

        if (status == 500)
        {
            logger.LogError(ex, "Unhandled exception");
        }

        ctx.Response.StatusCode = status;
        var details = new ProblemDetails { Status = status, Title = title, Type = $"https://hally.app/problems/{code}" };
        details.Extensions["code"] = code;
        if (ex is Features.Billing.PlanLimitException limit)
        {
            // The app opens the paywall with this trigger; the limit is shown in the copy.
            details.Extensions["trigger"] = limit.Trigger;
            details.Extensions["limit"] = limit.Limit;
        }

        return await problems.TryWriteAsync(new() { HttpContext = ctx, ProblemDetails = details, Exception = ex });
    }
}

public class NotFoundException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
