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
        return await problems.TryWriteAsync(new() { HttpContext = ctx, ProblemDetails = details, Exception = ex });
    }
}

public class NotFoundException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
