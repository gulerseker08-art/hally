using Hally.Domain.Common;

namespace Hally.Domain.Assistant;

/// <summary>A chat between one adult and Hally, inside one family. Other adults do not see it.</summary>
public class Conversation : Entity
{
    private Conversation() { }

    public Conversation(Guid familyId, Guid userId, DateTimeOffset now)
    {
        FamilyId = familyId;
        UserId = userId;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid FamilyId { get; private set; }
    public Guid UserId { get; private set; }
    public string? Title { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Touch(string firstUserText, DateTimeOffset now)
    {
        Title ??= firstUserText.Length > 80 ? firstUserText[..80] : firstUserText;
        UpdatedAt = now;
    }
}

public enum ChatRole
{
    User,
    Assistant,
}

/// <summary>A visible chat message. Tool traffic is not stored; token usage is kept for per-family cost tracking.</summary>
public class ChatMessage : Entity
{
    public const int MaxLength = 4000;

    private ChatMessage() { }

    public ChatMessage(Guid conversationId, Guid familyId, ChatRole role, string content, DateTimeOffset now, string? proposalIds = null,
        int promptTokens = 0, int completionTokens = 0, bool fallback = false)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new DomainException("chat.empty", "Message cannot be empty.");
        }

        ConversationId = conversationId;
        FamilyId = familyId;
        Role = role;
        Content = content.Length > MaxLength ? content[..MaxLength] : content;
        CreatedAt = now;
        ProposalIds = proposalIds;
        PromptTokens = promptTokens;
        CompletionTokens = completionTokens;
        Fallback = fallback;
    }

    public Guid ConversationId { get; private set; }
    public Guid FamilyId { get; private set; }
    public ChatRole Role { get; private set; }
    public string Content { get; private set; } = default!;

    /// <summary>Comma-separated ids of proposals suggested in this reply.</summary>
    public string? ProposalIds { get; private set; }

    public int PromptTokens { get; private set; }
    public int CompletionTokens { get; private set; }

    /// <summary>True when the reply came from the built-in answers because the AI provider was unavailable.</summary>
    public bool Fallback { get; private set; }
}
