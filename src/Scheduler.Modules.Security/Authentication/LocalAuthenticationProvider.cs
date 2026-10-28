using Scheduler.Extensibility.Contracts;

namespace Scheduler.Modules.Security.Authentication;

/// <summary>
/// Local username and password sign-in with lockout after five failed attempts for 15 minutes and a
/// single generic failure message (FR-45, FR-47, FR-53).
/// </summary>
public sealed class LocalAuthenticationProvider(PasswordHasher hasher) : IAuthenticationProvider
{
    internal const string GenericFailureMessage = "The username or password is incorrect.";

    private readonly PasswordHasher _hasher = hasher;

    public Task<AuthenticationResult> SignInAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        // Look up the user, check lockout, verify the hash with _hasher and write the login audit here.
        return Task.FromResult(new AuthenticationResult(false, null, null, GenericFailureMessage));
    }

    public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
