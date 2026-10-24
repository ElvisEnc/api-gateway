using System.ComponentModel.DataAnnotations;

namespace ApiGateway.Auth;

public sealed class UsuariosOptions
{
    public const string Seccion = "Auth";

    [MinLength(1)]
    public List<UsuarioEstatico> Usuarios { get; set; } = [];
}

public sealed class UsuarioEstatico
{
    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    public List<string> Roles { get; set; } = [];
}
