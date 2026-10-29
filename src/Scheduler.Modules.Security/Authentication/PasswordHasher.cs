using System.Globalization;
using System.Security.Cryptography;

namespace Scheduler.Modules.Security.Authentication;

/// <summary>A stored password: the hash with its iteration count, and the salt, both Base64 (app_user.password_hash and password_salt).</summary>
public sealed record PasswordHash(string Hash, string Salt);

/// <summary>
/// Salted PBKDF2-HMAC-SHA256 with a random 128-bit salt per user and 600 000 iterations, compared in constant
/// time (FR-46, Phase 4 Table 8.2). The iteration count is stored with the hash ("600000.base64") so it can be
/// raised later without invalidating existing passwords.
/// </summary>
public sealed class PasswordHasher
{
    public const int Iterations = 600_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    private static readonly Lazy<PasswordHash> Dummy = new(() => new PasswordHasher().Hash(Guid.NewGuid().ToString()));

    public PasswordHash Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return new PasswordHash($"{Iterations.ToString(CultureInfo.InvariantCulture)}.{Convert.ToBase64String(hash)}", Convert.ToBase64String(salt));
    }

    public bool Verify(string password, string storedHash, string storedSalt)
    {
        var parts = storedHash.Split('.');
        if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations) || iterations < 1)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(storedSalt);
            var expected = Convert.FromBase64String(parts[1]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Spends the same time as a real check, so a missing username cannot be detected by timing (Algorithm 6.13).</summary>
    public void VerifyDummy(string password) => Verify(password, Dummy.Value.Hash, Dummy.Value.Salt);
}
