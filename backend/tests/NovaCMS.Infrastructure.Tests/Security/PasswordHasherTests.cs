using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NovaCMS.Application.Security;
using NovaCMS.Infrastructure;
using Xunit;

namespace NovaCMS.Infrastructure.Tests.Security;

public sealed class PasswordHasherTests
{
    private const string TestPassword = "TestPassword123!";
    private readonly IPasswordHasher _sut = CreateHasher();

    [Fact]
    public void Hash_WhenCalledTwice_ProducesDifferentHashes()
    {
        var firstHash = _sut.Hash(TestPassword);
        var secondHash = _sut.Hash(TestPassword);

        Assert.NotEqual(firstHash, secondHash);
    }

    [Fact]
    public void Hash_ReturnsNonEmptyHashWithoutPlaintextPassword()
    {
        var hash = _sut.Hash(TestPassword);

        Assert.NotEmpty(hash);
        Assert.DoesNotContain(TestPassword, hash, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Hash_WithInvalidPassword_ThrowsArgumentException(string? password)
    {
        Assert.ThrowsAny<ArgumentException>(() => _sut.Hash(password!));
    }

    [Fact]
    public void Verify_WithCorrectPassword_ReturnsSuccess()
    {
        var hash = _sut.Hash(TestPassword);

        var result = _sut.Verify(TestPassword, hash);

        Assert.Equal(PasswordVerificationResult.Success, result);
    }

    [Fact]
    public void Verify_WithWrongPassword_ReturnsFailed()
    {
        var hash = _sut.Hash(TestPassword);

        var result = _sut.Verify("WrongTestPassword123!", hash);

        Assert.Equal(PasswordVerificationResult.Failed, result);
    }

    [Theory]
    [InlineData(null, "not-a-hash")]
    [InlineData("", "not-a-hash")]
    [InlineData("   ", "not-a-hash")]
    [InlineData(TestPassword, null)]
    [InlineData(TestPassword, "")]
    [InlineData(TestPassword, "   ")]
    [InlineData(TestPassword, "not-a-valid-password-hash")]
    public void Verify_WithInvalidInput_ReturnsFailed(string? password, string? hash)
    {
        var result = _sut.Verify(password!, hash!);

        Assert.Equal(PasswordVerificationResult.Failed, result);
    }

    [Fact]
    public void Verify_WithLegacyHash_ReturnsSuccessRehashNeeded()
    {
        var legacyHasher = new Microsoft.AspNetCore.Identity.PasswordHasher<object>(
            Options.Create(new Microsoft.AspNetCore.Identity.PasswordHasherOptions
            {
                CompatibilityMode = Microsoft.AspNetCore.Identity.PasswordHasherCompatibilityMode.IdentityV2
            }));
        var legacyHash = legacyHasher.HashPassword(new object(), TestPassword);

        var result = _sut.Verify(TestPassword, legacyHash);

        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded, result);
    }

    private static IPasswordHasher CreateHasher()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=test;Database=test"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);

        return services.BuildServiceProvider().GetRequiredService<IPasswordHasher>();
    }
}
