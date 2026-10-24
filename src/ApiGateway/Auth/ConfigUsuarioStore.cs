using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Options;

namespace ApiGateway.Auth;

internal sealed class ConfigUsuarioStore(IOptionsMonitor<UsuariosOptions> opciones) : IUsuarioStore
{
    public UsuarioAutenticado? Validar(string username, string password)
    {
        var usuario = opciones.CurrentValue.Usuarios.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
        var esperado = SHA256.HashData(Encoding.UTF8.GetBytes(usuario?.Password ?? Guid.NewGuid().ToString()));
        var recibido = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        var coincide = CryptographicOperations.FixedTimeEquals(esperado, recibido);

        return usuario is not null && coincide
            ? new UsuarioAutenticado(usuario.Username, usuario.Roles)
            : null;
    }
}
