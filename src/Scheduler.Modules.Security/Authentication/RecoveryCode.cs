using System.Security.Cryptography;

namespace Scheduler.Modules.Security.Authentication;

/// <summary>
/// The one-time code a user keeps to set a new password when they forget it. Everything runs locally without email, so
/// the code takes the place of a reset link. It is shown once and stored only as a salted hash, like a password.
/// </summary>
public static class RecoveryCode
{
    // No 0/O or 1/I/L, so the code can be copied by hand without mistakes. 31 characters, 16 places: about 79 bits.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int Length = 16;

    /// <summary>A new code in four groups, for example "K7QM-2XNB-R9TD-HW4P".</summary>
    public static string Generate()
    {
        var chars = RandomNumberGenerator.GetItems<char>(Alphabet, Length);
        return string.Join('-', Enumerable.Range(0, Length / 4).Select(i => new string(chars, i * 4, 4)));
    }

    /// <summary>Upper case without spaces and dashes, so "k7qm 2xnb-r9td hw4p" matches the code it was copied from.</summary>
    public static string Normalize(string? code) =>
        new((code ?? string.Empty).Where(c => !char.IsWhiteSpace(c) && c != '-').Select(char.ToUpperInvariant).ToArray());
}
