using Scheduler.Modules.Security.Authentication;

namespace Scheduler.UnitTests;

public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void Verify_ReturnsTrue_ForTheOriginalPassword()
    {
        var stored = _hasher.Hash("CorrectHorse42");

        Assert.True(_hasher.Verify("CorrectHorse42", stored));
    }

    [Fact]
    public void Verify_ReturnsFalse_ForAWrongPassword()
    {
        var stored = _hasher.Hash("CorrectHorse42");

        Assert.False(_hasher.Verify("WrongHorse42", stored));
    }

    [Fact]
    public void Hash_NeverContainsThePlainTextPassword()
    {
        var stored = _hasher.Hash("CorrectHorse42");

        Assert.DoesNotContain("CorrectHorse42", stored);
    }
}
