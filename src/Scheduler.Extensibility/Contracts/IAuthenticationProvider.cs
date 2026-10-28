using Scheduler.Domain.Enums;

namespace Scheduler.Extensibility.Contracts;

/// <summary>
/// Defines how Admin application users are authenticated. The Login and Security module supplies
/// a local implementation; a developer can replace it, for example with a directory service (FR-44).
/// </summary>
public interface IAuthenticationProvider
{
    Task<AuthenticationResult> SignInAsync(string username, string password, CancellationToken cancellationToken = default);

    Task SignOutAsync(CancellationToken cancellationToken = default);
}

/// <summary>Result of a sign-in attempt. <see cref="Message"/> is always generic on failure (FR-47).</summary>
public sealed record AuthenticationResult(bool Succeeded, string? Username, UserRole? Role, string? Message);
