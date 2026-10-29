using Scheduler.Modules.Security.Authentication;

namespace Scheduler.UnitTests;

public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void Verify_ReturnsTrue_ForTheOriginalPassword()
    {
        var stored = _hasher.Hash("CorrectHorse42");

        Assert.True(_hasher.Verify("CorrectHorse42", stored.Hash, stored.Salt));
    }

    [Fact]
    public void Verify_ReturnsFalse_ForAWrongPassword()
    {
        var stored = _hasher.Hash("CorrectHorse42");

        Assert.False(_hasher.Verify("WrongHorse42", stored.Hash, stored.Salt));
    }

    [Fact]
    public void Hash_NeverContainsThePlainTextPassword()
    {
        var stored = _hasher.Hash("CorrectHorse42");

        Assert.DoesNotContain("CorrectHorse42", stored.Hash + stored.Salt);
    }

    [Fact]
    public void Hash_UsesANewSaltEachTime()
    {
        var first = _hasher.Hash("CorrectHorse42");
        var second = _hasher.Hash("CorrectHorse42");

        Assert.NotEqual(first.Salt, second.Salt);
        Assert.NotEqual(first.Hash, second.Hash);
    }

    [Fact]
    public void Verify_ReturnsFalse_ForADamagedHash()
    {
        Assert.False(_hasher.Verify("CorrectHorse42", "not-a-hash", "not-a-salt"));
    }

    [Theory]
    [InlineData("short1", false)]
    [InlineData("onlyletterslong", false)]
    [InlineData("1234567890", false)]
    [InlineData("alice12345", false)] // same as the username
    [InlineData("CorrectHorse42", true)]
    public void PasswordPolicy_AcceptsOnlyStrongPasswords(string password, bool accepted)
    {
        Assert.Equal(accepted, PasswordPolicy.Validate(password, "alice12345") is null);
    }
}
