using System.Text.Json.Serialization;

using ApiGateway.Errores;

namespace ApiGateway.Auth;

public sealed record LoginRequest(string? Username, string? Password);

public sealed record TokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn);

public static class LoginEndpoints
{
    public const string PoliticaRateLimit = "login";

    public static IEndpointRouteBuilder MapLogin(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/login", async (LoginRequest? request, ITokenIssuer issuer, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request?.Username) || string.IsNullOrWhiteSpace(request.Password))
                return Results.BadRequest(new ErrorResponse(
                    "SOLICITUD_INVALIDA", "Los campos username y password son obligatorios."));

            var token = await issuer.EmitirAsync(request.Username, request.Password, ct);

            return token is null
                ? Results.Json(
                    new ErrorResponse("CREDENCIALES_INVALIDAS", "Usuario o contraseña incorrectos."),
                    statusCode: StatusCodes.Status401Unauthorized)
                : Results.Ok(token);
        })
            .AllowAnonymous()
            .RequireRateLimiting(PoliticaRateLimit)
            .WithName("Login");

        return app;
    }
}
