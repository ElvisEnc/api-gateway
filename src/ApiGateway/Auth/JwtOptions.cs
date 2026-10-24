using System.ComponentModel.DataAnnotations;

namespace ApiGateway.Auth;

public sealed class JwtOptions
{
    public const string Seccion = "Jwt";

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    // HS256 exige 256 bits como mínimo; además se valida en bytes en Program.cs.
    [Required, MinLength(32)]
    public string Key { get; set; } = string.Empty;

    [Range(1, 1440)]
    public int ExpiresMinutes { get; set; } = 60;
}