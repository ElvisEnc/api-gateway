using System.Text;

using Microsoft.IdentityModel.Tokens;

namespace ApiGateway.Auth;

public static class ClaveDeFirma
{
    public static SymmetricSecurityKey Crear(string clave) => new(Encoding.UTF8.GetBytes(clave));
}
