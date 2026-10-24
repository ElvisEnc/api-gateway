using System.Text;
using System.Threading.RateLimiting;
using ApiGateway.Auth;
using ApiGateway.Errores;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.Seccion))
    .ValidateDataAnnotations()
    .Validate(o => Encoding.UTF8.GetByteCount(o.Key) >= 32, "Jwt:Key debe tener al menos 32 bytes (HS256).")
    .ValidateOnStart();

builder.Services.AddOptions<UsuariosOptions>()
    .Bind(builder.Configuration.GetSection(UsuariosOptions.Seccion))
    .ValidateDataAnnotations()
    .Validate(o => o.Usuarios.Count > 0, "Auth:Usuarios necesita al menos un usuario.")
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IUsuarioStore, ConfigUsuarioStore>();
builder.Services.AddSingleton<ITokenIssuer, LocalJwtTokenIssuer>();

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = async (contexto, ct) =>
    {
        contexto.HttpContext.Response.Headers.RetryAfter = "60";
        await RespuestasError.EscribirAsync(contexto.HttpContext, StatusCodes.Status429TooManyRequests,
            "DEMASIADOS_INTENTOS", "Demasiados intentos de login. Intenta de nuevo en un minuto.", ct);
    };
    o.AddPolicy(LoginEndpoints.PoliticaRateLimit, ctx =>
    {
        // Se lee en cada request (no al construir) para que los tests puedan sobrescribirlo.
        var limite = ctx.RequestServices.GetRequiredService<IConfiguration>().GetValue("Login:IntentosPorMinuto", 10);
        return RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = limite, Window = TimeSpan.FromMinutes(1) });
    });
});

var app = builder.Build();

app.UseRateLimiter();
app.MapLogin();

app.Run();

public partial class Program { }
