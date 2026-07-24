using AzerothCore_UI.Api.Security;
using Xunit;

namespace AzerothCore_UI.Api.Tests.Security;

public sealed class AdministrationPasswordHasherTests
{
    private readonly AdministrationPasswordHasher hasher = new();

    [Fact]
    public void Hash_ProducesSaltedVerifiableHashes()
    {
        const string password = "This is a strong test password!";
        var first = hasher.Hash(password);
        var second = hasher.Hash(password);

        Assert.NotEqual(first, second);
        Assert.True(hasher.Verify(password, first));
        Assert.True(hasher.Verify(password, second));
        Assert.False(hasher.Verify("incorrect password", first));
    }

    [Theory]
    [InlineData("")]
    [InlineData("too-short")]
    public void Hash_RejectsShortPasswords(string password) =>
        Assert.Throws<ArgumentException>(() => hasher.Hash(password));

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("pbkdf2-sha256$bad$salt$hash")]
    public void Verify_RejectsMalformedHashes(string encoded) =>
        Assert.False(hasher.Verify("irrelevant password", encoded));
}
