using System.ComponentModel.DataAnnotations;
using System.Text;

namespace RepoLens.Infrastructure.Security;

public sealed class JwtOptions : IValidatableObject
{
    public const string SectionName = "Auth:Jwt";

    public const int MinimumKeyBytes = 32;

    [Required]
    public string Issuer { get; set; } = "repolens";

    [Required]
    public string Audience { get; set; } = "repolens-api";

    /// <summary>HMAC-SHA256 signing key, at least 32 bytes. Keep it in user secrets or Key Vault, never in appsettings.</summary>
    [Required(ErrorMessage = "Auth:Jwt:SigningKey is required. Set it with 'dotnet user-secrets' locally or Key Vault in Azure.")]
    public string SigningKey { get; set; } = string.Empty;

    [Range(typeof(TimeSpan), "00:01:00", "1.00:00:00")]
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(60);

    public byte[] SigningKeyBytes() => Encoding.UTF8.GetBytes(SigningKey);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrEmpty(SigningKey) && SigningKeyBytes().Length < MinimumKeyBytes)
        {
            yield return new ValidationResult($"Auth:Jwt:SigningKey must be at least {MinimumKeyBytes} bytes.", [nameof(SigningKey)]);
        }
    }
}
