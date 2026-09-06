using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using NovaCMS.Application.Security;
using NovaCMS.Domain.Authentication;

namespace NovaCMS.Infrastructure.Security;

// Shared creation primitive. The caller owns persistence and must not release the result before commit.
internal sealed class RefreshTokenFactory(IOptions<JwtOptions> options)
{
    private readonly JwtOptions _options = options.Value;

    public (RefreshToken Entity, RefreshTokenResult Result) Generate(Guid userId, DateTimeOffset now)
    {
        // 32 random bytes = 256 bits of entropy. Encoding is transport formatting, not encryption.
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var expiresAt = now.Add(_options.RefreshTokenLifetime);
        return (new RefreshToken(Guid.NewGuid(), userId, Hash(token), expiresAt, now),
            new RefreshTokenResult(token, expiresAt));
    }

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
