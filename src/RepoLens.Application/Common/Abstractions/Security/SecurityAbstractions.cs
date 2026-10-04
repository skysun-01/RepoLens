using RepoLens.Domain.Users;

namespace RepoLens.Application.Common.Abstractions.Security;

/// <summary>Encrypts secrets (GitHub tokens) before they are stored.</summary>
public interface ITokenProtector
{
    string Protect(string plaintext);

    string Unprotect(string protectedValue);
}

/// <summary>Issues the short-lived JWT access tokens the API accepts.</summary>
public interface IAccessTokenIssuer
{
    IssuedAccessToken Issue(User user);
}

public sealed record IssuedAccessToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>Creates random opaque tokens (auth codes, refresh tokens) and the hashes stored in their place.</summary>
public interface ISecretTokenGenerator
{
    string Generate();

    string Hash(string token);
}
