using Hally.Domain.Common;

namespace Hally.Domain.Users;

public class User : Entity
{
    private readonly List<ExternalLogin> _logins = [];

    private User() { }

    public User(string email, string displayName, string locale)
    {
        Email = email.Trim().ToLowerInvariant();
        DisplayName = displayName.Trim();
        Locale = Locales.Normalize(locale);
    }

    public string Email { get; private set; } = default!;
    public string DisplayName { get; private set; } = default!;
    public string Locale { get; private set; } = Locales.Turkish;
    public IReadOnlyCollection<ExternalLogin> Logins => _logins;

    public void LinkLogin(string provider, string subject)
    {
        if (_logins.Any(l => l.Provider == provider && l.Subject == subject))
        {
            return;
        }

        _logins.Add(new ExternalLogin(Id, provider, subject));
    }

    public void UpdateProfile(string displayName, string locale)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new DomainException("user.display_name_required", "Display name is required.");
        }

        DisplayName = displayName.Trim();
        Locale = Locales.Normalize(locale);
    }
}

public class ExternalLogin : Entity
{
    private ExternalLogin() { }

    public ExternalLogin(Guid userId, string provider, string subject)
    {
        UserId = userId;
        Provider = provider;
        Subject = subject;
    }

    public Guid UserId { get; private set; }
    public string Provider { get; private set; } = default!;
    public string Subject { get; private set; } = default!;
}

public static class Locales
{
    public const string Turkish = "tr";
    public const string English = "en";

    public static string Normalize(string? locale)
    {
        var two = (locale ?? string.Empty).Trim().ToLowerInvariant();
        two = two.Length >= 2 ? two[..2] : two;
        return two == English ? English : Turkish;
    }
}
