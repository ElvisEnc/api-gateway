using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Microsoft.IdentityModel.JsonWebTokens;

namespace ApiGateway.Tests;

public sealed class LoginTests(GatewayFactory factory) : IClassFixture<GatewayFactory>
{
    private readonly HttpClient _cliente = factory.CreateClient();

    private Task<HttpResponseMessage> Login(object body) => _cliente.PostAsJsonAsync("/api/login", body);

    private static async Task<string?> Codigo(HttpResponseMessage respuesta)
    {
        using var json = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("codigo").GetString();
    }

    [Fact]
    public async Task Login_con_credenciales_validas_devuelve_200_y_token()
    {
        var respuesta = await Login(new { username = "admin", password = "admin123" });

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        using var json = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        Assert.Equal("Bearer", json.RootElement.GetProperty("token_type").GetString());
        Assert.Equal(3600, json.RootElement.GetProperty("expires_in").GetInt32());

        var token = new JsonWebToken(json.RootElement.GetProperty("access_token").GetString());
        Assert.Equal("admin", token.Subject);
        Assert.Contains(token.Claims, c => c.Type == "role" && c.Value == "integracion");
    }

    [Fact]
    public async Task Login_con_contrasena_incorrecta_devuelve_401()
    {
        var respuesta = await Login(new { username = "admin", password = "incorrecta" });

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.Equal("CREDENCIALES_INVALIDAS", await Codigo(respuesta));
    }

    [Fact]
    public async Task Login_con_usuario_inexistente_devuelve_401()
    {
        var respuesta = await Login(new { username = "nadie", password = "admin123" });

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.Equal("CREDENCIALES_INVALIDAS", await Codigo(respuesta));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"username":"admin"}""")]
    [InlineData("""{"password":"admin123"}""")]
    public async Task Login_sin_campos_obligatorios_devuelve_400(string body)
    {
        var respuesta = await _cliente.PostAsync("/api/login",
            new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal("SOLICITUD_INVALIDA", await Codigo(respuesta));
    }

    [Fact]
    public async Task Login_no_distingue_mayusculas_en_el_username()
    {
        var respuesta = await Login(new { username = "ADMIN", password = "admin123" });

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
    }
}

// Clase aparte: el límite de 3 intentos no debe afectar a los demás tests de login.
public sealed class LoginRateLimitTests(GatewayFactoryLimitado factory) : IClassFixture<GatewayFactoryLimitado>
{
    [Fact]
    public async Task Cuarto_login_en_un_minuto_devuelve_429_con_retry_after()
    {
        var cliente = factory.CreateClient();
        var body = new { username = "admin", password = "incorrecta" };

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.PostAsJsonAsync("/api/login", body)).StatusCode);

        var respuesta = await cliente.PostAsJsonAsync("/api/login", body);

        Assert.Equal(HttpStatusCode.TooManyRequests, respuesta.StatusCode);
        Assert.True(respuesta.Headers.Contains("Retry-After"));
        using var json = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        Assert.Equal("DEMASIADOS_INTENTOS", json.RootElement.GetProperty("codigo").GetString());
    }
}
