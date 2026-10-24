using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;

using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ApiGateway.Tests;

internal static class TokenDePrueba
{
    public static string Crear(
        string clave = GatewayFactory.ClaveDePrueba,
        string issuer = GatewayFactory.Issuer,
        string audience = GatewayFactory.Audience,
        DateTime? expira = null,
        params string[] roles)
    {
        var ahora = DateTime.UtcNow;
        var fin = expira ?? ahora.AddMinutes(10);
        var claims = new List<Claim> { new("sub", "tester"), new("name", "tester") };
        claims.AddRange(roles.Select(r => new Claim("role", r)));

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(claims),
            // nbf/iat anteriores a exp: así un token vencido da SecurityTokenExpiredException (TOKEN_EXPIRADO).
            IssuedAt = fin.AddMinutes(-20),
            NotBefore = fin.AddMinutes(-20),
            Expires = fin,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(clave)), SecurityAlgorithms.HmacSha256)
        });
    }
}
