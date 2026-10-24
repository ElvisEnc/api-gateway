using System.Security.Claims;

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ApiGateway.Auth;

internal sealed class LocalJwtTokenIssuer(IUsuarioStore usuarios, IOptions<JwtOptions> opciones, TimeProvider reloj) : ITokenIssuer
{
    private readonly JsonWebTokenHandler _handler = new();
    public Task<TokenResponse?> EmitirAsync(string username, string password, CancellationToken ct)
    {
        var usuario = usuarios.Validar(username, password);
        if (usuario is null)
            return Task.FromResult<TokenResponse?>(null);

        var jwt = opciones.Value;
        var ahora = reloj.GetUtcNow().UtcDateTime;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, usuario.Username),
            new(JwtRegisteredClaimNames.Name, usuario.Username),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        // Varios claims "role" se serializan como arreglo JSON: "role": ["admin","integracion"].
        claims.AddRange(usuario.Roles.Select(rol => new Claim("role", rol)));

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = ahora,
            NotBefore = ahora,
            Expires = ahora.AddMinutes(jwt.ExpiresMinutes),
            SigningCredentials = new SigningCredentials(ClaveDeFirma.Crear(jwt.Key), SecurityAlgorithms.HmacSha256)
        });

        return Task.FromResult<TokenResponse?>(new TokenResponse(token, "Bearer", jwt.ExpiresMinutes * 60));
    }
}
