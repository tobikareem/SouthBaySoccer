namespace SouthBaySoccer.Infrastructure.Authentication;

/// <summary>
/// Options for SouthBaySoccer JWT access-token issuance and validation.
/// </summary>
public sealed class JwtTokenOptions
{
    /// <summary>
    /// Gets or sets the expected JWT issuer.
    /// </summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the expected JWT audience.
    /// </summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the access-token lifetime.
    /// </summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Gets or sets the active signing key id used for new tokens.
    /// </summary>
    public string ActiveKeyId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets all signing keys accepted during validation, including retired keys in overlap windows.
    /// </summary>
    public List<JwtSigningKeyOptions> SigningKeys { get; set; } = [];
}

/// <summary>
/// Options for one JWT signing key.
/// </summary>
public sealed class JwtSigningKeyOptions
{
    /// <summary>
    /// Gets or sets the stable key identifier emitted in the JWT header.
    /// </summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the HMAC secret material. This value must come from user secrets or Key Vault.
    /// </summary>
    public string Secret { get; set; } = string.Empty;
}
