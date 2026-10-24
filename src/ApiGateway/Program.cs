using System.Text;
using System.Threading.RateLimiting;

using ApiGateway.Auth;
using ApiGateway.Errores;
using ApiGateway.Proxy;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

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
        var limite = ctx.RequestServices.GetRequiredService<IConfiguration>().GetValue("Login:IntentosPorMinuto", 10);
        return RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = limite, Window = TimeSpan.FromMinutes(1) });
    });
});


builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
    {
        var jwt = jwtOptions.Value;
        bearer.MapInboundClaims = false;
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = ClaveDeFirma.Crear(jwt.Key),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "name",
            RoleClaimType = "role"
        };

        bearer.Events = new JwtBearerEvents
        {
            OnChallenge = async ctx =>
            {
                ctx.HandleResponse(); 
                var expirado = ctx.AuthenticateFailure is SecurityTokenExpiredException;
                ctx.Response.Headers.WWWAuthenticate = expirado
                    ? "Bearer error=\"invalid_token\", error_description=\"The token expired\""
                    : "Bearer";
                await RespuestasError.EscribirAsync(ctx.HttpContext, StatusCodes.Status401Unauthorized,
                    expirado ? "TOKEN_EXPIRADO" : "NO_AUTENTICADO",
                    expirado ? "El token expiró. Vuelve a hacer login." : "Falta el token Bearer o no es válido.");
            },
            OnForbidden = ctx => RespuestasError.EscribirAsync(ctx.HttpContext, StatusCodes.Status403Forbidden,
                "SIN_PERMISO", "No tienes permiso para este recurso.")
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Politicas.Autenticado, p => p.RequireAuthenticatedUser())
    .AddPolicy(Politicas.Integracion, p => p.RequireAuthenticatedUser().RequireRole(Politicas.RolIntegracion))
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddHealthChecks();
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms<IdentidadTransformProvider>();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapLogin();
app.MapReverseProxy();

app.Run();

public partial class Program { }
