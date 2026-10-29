namespace Scheduler.Modules.Security.Authentication;

/// <summary>At least ten characters with a letter and a digit, and not the username (FR-52).</summary>
public static class PasswordPolicy
{
    public const int MinimumLength = 10;

    public const string Description = "At least 10 characters, with at least one letter and one digit, and not the same as the username.";

    /// <summary>Returns the problem with the password, or null when it is acceptable.</summary>
    public static string? Validate(string? password, string? username)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinimumLength)
        {
            return $"The password must be at least {MinimumLength} characters long.";
        }

        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
        {
            return "The password must contain at least one letter and one digit.";
        }

        return username is not null && string.Equals(password.Trim(), username.Trim(), StringComparison.OrdinalIgnoreCase)
            ? "The password must not be the same as the username."
            : null;
    }
}
