namespace ApiGateway.Auth;

public interface ITokenIssuer
{
    Task<TokenResponse?> EmitirAsync(string username, string password, CancellationToken ct);
}
