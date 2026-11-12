using FluentValidation;
using Hally.Api.Auth;
using Hally.Api.Common;
using Hally.Api.Features.Proposals;
using Hally.Domain.Assistant;
using Hally.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Hally.Api.Features.Assistant;

public record SendMessageRequest(Guid? ConversationId, string Text);
public record ChatMessageDto(Guid Id, string Role, string Content, DateTimeOffset CreatedAt, IReadOnlyList<Guid> ProposalIds, bool Fallback);
public record AssistantReplyDto(Guid ConversationId, ChatMessageDto Reply, IReadOnlyList<ProposalDto> Proposals);
public record ConversationDto(Guid Id, string? Title, DateTimeOffset UpdatedAt);

public class SendMessageRequestValidator : AbstractValidator<SendMessageRequest>
{
    public SendMessageRequestValidator() => RuleFor(x => x.Text).NotEmpty().MaximumLength(2000);
}

[ApiController]
[Authorize(Policy = Policies.FamilyMember)]
[Route("api/v1/families/{familyId:guid}/assistant")]
public class AssistantController(HallyDbContext db, AssistantService assistant, TimeProvider clock) : ControllerBase
{
    public const string RateLimitPolicy = "assistant";

    /// <summary>Sends a message to Hally and returns the reply plus any suggestions (pending proposals) it made.</summary>
    [HttpPost("messages")]
    [Idempotent]
    [EnableRateLimiting(RateLimitPolicy)]
    public async Task<ActionResult<AssistantReplyDto>> Send(Guid familyId, SendMessageRequest request, CancellationToken ct)
    {
        var reply = await assistant.ReplyAsync(familyId, User.UserId(), request.ConversationId, request.Text.Trim(), ct);
        var now = clock.GetUtcNow();
        return new AssistantReplyDto(reply.ConversationId, ToDto(reply.Message), reply.Proposals.Select(p => ProposalsController.ToDto(p, now)).ToList());
    }

    /// <summary>The caller's own conversations in this family (other adults' chats are private).</summary>
    [HttpGet("conversations")]
    public async Task<ActionResult<IReadOnlyList<ConversationDto>>> Conversations(Guid familyId, CancellationToken ct)
    {
        var userId = User.UserId();
        return await db.Conversations.AsNoTracking().Where(c => c.FamilyId == familyId && c.UserId == userId)
            .OrderByDescending(c => c.UpdatedAt).Take(20).Select(c => new ConversationDto(c.Id, c.Title, c.UpdatedAt)).ToListAsync(ct);
    }

    [HttpGet("conversations/{conversationId:guid}/messages")]
    public async Task<ActionResult<IReadOnlyList<ChatMessageDto>>> Messages(Guid familyId, Guid conversationId, CancellationToken ct)
    {
        var userId = User.UserId();
        if (!await db.Conversations.AnyAsync(c => c.Id == conversationId && c.FamilyId == familyId && c.UserId == userId, ct))
        {
            throw new NotFoundException("conversation.not_found", "Conversation not found.");
        }

        var messages = await db.ChatMessages.AsNoTracking().Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt).Take(200).ToListAsync(ct);
        return messages.Select(ToDto).ToList();
    }

    private static ChatMessageDto ToDto(ChatMessage m) => new(m.Id, m.Role.ToString(), m.Content, m.CreatedAt,
        m.ProposalIds is null ? [] : m.ProposalIds.Split(',').Select(Guid.Parse).ToList(), m.Fallback);
}
