namespace ApiGateway.Auth;

public interface IUsuarioStore
{
    UsuarioAutenticado? Validar(string username, string password);
}

public sealed record UsuarioAutenticado(string Username, IReadOnlyList<string> Roles);