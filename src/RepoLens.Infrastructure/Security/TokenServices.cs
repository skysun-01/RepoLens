using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using RepoLens.Application.Common.Abstractions.Security;

namespace RepoLens.Infrastructure.Security;

/// <summary>Encrypts GitHub tokens with ASP.NET Core Data Protection (AES-256-CBC + HMAC, keys rotated every 90 days).</summary>
internal sealed class DataProtectionTokenProtector(IDataProtectionProvider provider) : ITokenProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("RepoLens.GitHubAccessToken.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string protectedValue) => _protector.Unprotect(protectedValue);
}

/// <summary>256-bit random tokens, URL-safe; stored only as SHA-256 hashes.</summary>
internal sealed class SecretTokenGenerator : ISecretTokenGenerator
{
    public string Generate() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
