using System.Text;
using System.Text.Json;
using Hally.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace Hally.Api.Common;

/// <summary>
/// Requires an <c>Idempotency-Key</c> header on a state-changing endpoint. A repeated request with the
/// same key and body returns the stored response without executing again; the same key with a
/// different body is rejected with 422.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentAttribute : Attribute, IFilterFactory
{
    public const string Header = "Idempotency-Key";
    public bool IsReusable => false;

    public IFilterMetadata CreateInstance(IServiceProvider services) =>
        new IdempotencyFilter(services.GetRequiredService<HallyDbContext>());
}

internal sealed class IdempotencyFilter(HallyDbContext db) : IAsyncActionFilter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var key = http.Request.Headers[IdempotentAttribute.Header].ToString();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100)
        {
            context.Result = Problem(400, "idempotency.key_required", $"A {IdempotentAttribute.Header} header (max 100 chars) is required.");
            return;
        }

        var caller = http.User.Identity?.IsAuthenticated == true ? http.User.UserId().ToString() : "anon";
        var scope = Crypto.Sha256($"{caller}|{http.Request.Method}|{http.Request.Path}")[..64];
        var arguments = context.ActionArguments
            .Where(a => a.Value is not CancellationToken)
            .OrderBy(a => a.Key, StringComparer.Ordinal)
            .ToDictionary(a => a.Key, a => a.Value is IFormFile f ? $"file:{f.FileName}:{f.Length}" : a.Value);
        var requestHash = Crypto.Sha256(JsonSerializer.Serialize(arguments, Json));

        var existing = await db.IdempotencyRecords.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Scope == scope && r.Key == key, http.RequestAborted);
        if (existing is not null)
        {
            if (existing.RequestHash != requestHash)
            {
                context.Result = Problem(422, "idempotency.key_reused", "This Idempotency-Key was already used with a different request.");
                return;
            }

            http.Response.Headers["Idempotent-Replayed"] = "true";
            context.Result = new ContentResult
            {
                StatusCode = existing.StatusCode,
                Content = existing.ResponseBody,
                ContentType = "application/json; charset=utf-8",
            };
            return;
        }

        var executed = await next();
        if (executed.Exception is null && executed.Result is ObjectResult { StatusCode: >= 200 and < 300 } ok)
        {
            db.ChangeTracker.Clear();
            db.IdempotencyRecords.Add(new IdempotencyRecord
            {
                Scope = scope,
                Key = key,
                RequestHash = requestHash,
                StatusCode = ok.StatusCode ?? 200,
                ResponseBody = ok.Value is null ? null : JsonSerializer.Serialize(ok.Value, Json),
            });
            await db.SaveChangesAsync(http.RequestAborted);
        }
    }

    private static ObjectResult Problem(int status, string code, string title)
    {
        var p = new ProblemDetails { Status = status, Title = title, Type = $"https://hally.app/problems/{code}" };
        p.Extensions["code"] = code;
        return new ObjectResult(p) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }
}
