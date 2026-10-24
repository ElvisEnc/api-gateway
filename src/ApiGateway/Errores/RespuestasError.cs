namespace ApiGateway.Errores;

public sealed record ErrorResponse(string Codigo, string Mensaje);

public static class RespuestasError
{
    public static Task EscribirAsync(
       HttpContext http, int status, string codigo, string mensaje, CancellationToken ct = default)
    {
        http.Response.StatusCode = status;
        return http.Response.WriteAsJsonAsync(new ErrorResponse(codigo, mensaje), ct);
    }
}
