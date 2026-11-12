using FluentValidation;
using Hally.Api.Common;
using Hally.Domain.Notifications;
using Hally.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hally.Api.Features.Notifications;

public record RegisterDeviceRequest(string Platform, string Token);
public record NotificationDto(Guid Id, Guid FamilyId, string Kind, string Title, string Body, string? TargetKind, Guid? TargetId, DateTimeOffset CreatedAt, bool Read);
public record InboxDto(int Unread, IReadOnlyList<NotificationDto> Items);
public record NotificationSettingsDto(bool EveningPrepEnabled, int EveningPrepHour);

public class RegisterDeviceRequestValidator : AbstractValidator<RegisterDeviceRequest>
{
    public RegisterDeviceRequestValidator()
    {
        RuleFor(x => x.Platform).Must(p => p is "android" or "ios").WithMessage("Platform must be 'android' or 'ios'.");
        RuleFor(x => x.Token).NotEmpty().MaximumLength(512);
    }
}

public class NotificationSettingsValidator : AbstractValidator<NotificationSettingsDto>
{
    public NotificationSettingsValidator() => RuleFor(x => x.EveningPrepHour).InclusiveBetween(16, 22);
}

[ApiController]
[Authorize]
[Route("api/v1/me")]
public class MeNotificationsController(HallyDbContext db, TimeProvider clock) : ControllerBase
{
    /// <summary>Registers (or re-assigns) this device's push token to the signed-in user. Safe to call on every app start.</summary>
    [HttpPut("devices")]
    public async Task<IActionResult> RegisterDevice(RegisterDeviceRequest request, CancellationToken ct)
    {
        var userId = User.UserId();
        var now = clock.GetUtcNow();
        var device = await db.Devices.FirstOrDefaultAsync(d => d.Token == request.Token.Trim(), ct);
        if (device is null)
        {
            db.Devices.Add(new Device(userId, request.Platform, request.Token, now));
        }
        else
        {
            device.Touch(userId, now);
        }

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Called on sign-out so the device stops receiving this user's pushes.</summary>
    [HttpDelete("devices")]
    public async Task<IActionResult> UnregisterDevice([FromQuery] string token, CancellationToken ct)
    {
        var userId = User.UserId();
        await db.Devices.Where(d => d.Token == token && d.UserId == userId).ExecuteDeleteAsync(ct);
        return NoContent();
    }

    [HttpGet("notifications")]
    public async Task<ActionResult<InboxDto>> Inbox([FromQuery] int take = 50, CancellationToken ct = default)
    {
        var userId = User.UserId();
        var items = await db.Notifications.AsNoTracking().Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt).Take(Math.Clamp(take, 1, 100)).ToListAsync(ct);
        var unread = await db.Notifications.CountAsync(n => n.UserId == userId && n.ReadAt == null, ct);
        return new InboxDto(unread, items.Select(n => new NotificationDto(n.Id, n.FamilyId, n.Kind.ToString(), n.Title, n.Body,
            n.TargetKind?.ToString(), n.TargetId, n.CreatedAt, n.ReadAt != null)).ToList());
    }

    [HttpPost("notifications/read-all")]
    public async Task<IActionResult> ReadAll(CancellationToken ct)
    {
        var userId = User.UserId();
        var now = clock.GetUtcNow();
        await db.Notifications.Where(n => n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
        return NoContent();
    }

    [HttpGet("notification-settings")]
    public async Task<ActionResult<NotificationSettingsDto>> GetSettings(CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == User.UserId(), ct);
        return new NotificationSettingsDto(user.EveningPrepEnabled, user.EveningPrepHour);
    }

    [HttpPut("notification-settings")]
    public async Task<ActionResult<NotificationSettingsDto>> PutSettings(NotificationSettingsDto request, CancellationToken ct)
    {
        var user = await db.Users.FirstAsync(u => u.Id == User.UserId(), ct);
        user.UpdateNotificationSettings(request.EveningPrepEnabled, request.EveningPrepHour);
        await db.SaveChangesAsync(ct);
        return new NotificationSettingsDto(user.EveningPrepEnabled, user.EveningPrepHour);
    }
}
