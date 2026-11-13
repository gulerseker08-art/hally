using Hally.Domain.Families;
using Hally.Domain.Notifications;
using Hally.Domain.Planning;
using Hally.Infrastructure.Persistence;
using Hally.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Hally.Api.Features.Privacy;

/// <summary>Leaving a family and deleting an account (KVKK/GDPR right to erasure).
/// A family the user is alone in is deleted with everything in it, including stored photos.
/// In a shared family the family's plan stays (it belongs to the other adults too), but the user's
/// personal traces go: assignments, personal reminders, pending invitations and open helper links.</summary>
public class AccountService(HallyDbContext db, IDocumentStore store, TimeProvider clock, ILogger<AccountService> logger)
{
    public async Task LeaveFamilyAsync(Guid userId, Guid familyId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var keys = await LeaveAsync(userId, familyId, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await DeleteFilesAsync(keys, ct);
    }

    public async Task DeleteAccountAsync(Guid userId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var keys = new List<string>();
        var familyIds = await db.FamilyMembers.Where(m => m.UserId == userId).Select(m => m.FamilyId).ToListAsync(ct);
        foreach (var familyId in familyIds)
        {
            keys.AddRange(await LeaveAsync(userId, familyId, ct));
        }

        await db.SaveChangesAsync(ct);

        // Analytics keep counts but lose the person.
        await db.AnalyticsEvents.Where(e => e.UserId == userId).ExecuteUpdateAsync(s => s.SetProperty(e => e.UserId, (Guid?)null), ct);
        // Logins, sessions, devices, inbox and own chats are removed by cascade.
        await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync(ct);
        await tx.CommitAsync(ct);
        await DeleteFilesAsync(keys, ct);
        logger.LogInformation("Account deleted ({Families} families left)", familyIds.Count);
    }

    /// <summary>Returns stored document keys to delete once the transaction commits.</summary>
    private async Task<List<string>> LeaveAsync(Guid userId, Guid familyId, CancellationToken ct)
    {
        var family = await db.Families.Include(f => f.Members).FirstAsync(f => f.Id == familyId, ct);
        if (family.Members.Count <= 1)
        {
            var keys = await db.Captures.Where(c => c.FamilyId == familyId && c.StorageKey != null).Select(c => c.StorageKey!).ToListAsync(ct);
            await db.AuditEntries.Where(a => a.FamilyId == familyId).ExecuteDeleteAsync(ct);
            await db.AnalyticsEvents.Where(e => e.FamilyId == familyId).ExecuteUpdateAsync(s => s.SetProperty(e => e.FamilyId, (Guid?)null), ct);
            db.Families.Remove(family); // cascades to tasks, events, captures, proposals, reminders, links, chats, subscription
            return keys;
        }

        var now = clock.GetUtcNow();
        family.RemoveMember(userId);
        foreach (var task in await db.Tasks.Where(t => t.FamilyId == familyId && t.AssigneeUserId == userId && t.State == TaskState.Open).ToListAsync(ct))
        {
            task.Assign(null);
        }

        await db.Reminders.Where(r => r.FamilyId == familyId && r.RecipientUserId == userId && r.Status == ReminderStatus.Scheduled).ExecuteDeleteAsync(ct);
        foreach (var invitation in await db.Invitations.Where(i => i.FamilyId == familyId && i.InvitedByUserId == userId && i.AcceptedAt == null).ToListAsync(ct))
        {
            invitation.Revoke(now);
        }

        foreach (var link in await db.ShareLinks.Where(l => l.FamilyId == familyId && l.CreatedByUserId == userId && l.RevokedAt == null).ToListAsync(ct))
        {
            link.Revoke(now);
        }

        await db.Conversations.Where(c => c.FamilyId == familyId && c.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Notifications.Where(n => n.FamilyId == familyId && n.UserId == userId).ExecuteDeleteAsync(ct);
        return [];
    }

    private async Task DeleteFilesAsync(IEnumerable<string> keys, CancellationToken ct)
    {
        foreach (var key in keys)
        {
            try
            {
                await store.DeleteAsync(key, ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The rows are already gone. Log it so an operator can remove the leftover encrypted file.
                logger.LogWarning(ex, "Could not delete a stored document after account deletion");
            }
        }
    }
}
