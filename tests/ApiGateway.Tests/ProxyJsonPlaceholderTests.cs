using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace ApiGateway.Tests;

public sealed class ProxyJsonPlaceholderTests : IClassFixture<GatewayFactory>
{
    private readonly GatewayFactory _factory;
    private readonly HttpClient _cliente;

    public ProxyJsonPlaceholderTests(GatewayFactory factory)
    {
        _factory = factory;
        _cliente = factory.CreateClient();

        // El WireMock se comparte entre los tests de la clase: se limpia antes de cada uno.
        factory.Upstream.Reset();
        Stub("/users", """[{"id":1,"name":"Leanne Graham"}]""");
        Stub("/posts", """[{"userId":1,"id":1,"title":"sunt aut facere"}]""");
    }

    private void Stub(string ruta, string body) =>
        _factory.Upstream
            .Given(Request.Create().WithPath(ruta).UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(body));

    private Task<HttpResponseMessage> Get(string ruta, string? token = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, ruta);
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _cliente.SendAsync(request);
    }

    private static async Task<string?> Codigo(HttpResponseMessage respuesta)
    {
        using var json = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("codigo").GetString();
    }

    [Fact]
    public async Task Users_sin_token_devuelve_401_y_no_llama_al_upstream()
    {
        var respuesta = await Get("/api/users");

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.Equal("NO_AUTENTICADO", await Codigo(respuesta));
        Assert.Empty(_factory.Upstream.LogEntries);
    }

    [Fact]
    public async Task Token_firmado_con_otra_clave_devuelve_401()
    {
        var token = TokenDePrueba.Crear(clave: "otra-clave-distinta-de-al-menos-32-bytes-xyz");

        var respuesta = await Get("/api/users", token);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.Empty(_factory.Upstream.LogEntries);
    }

    [Fact]
    public async Task Token_expirado_hace_5_minutos_devuelve_401_token_expirado()
    {
        var token = TokenDePrueba.Crear(expira: DateTime.UtcNow.AddMinutes(-5));

        var respuesta = await Get("/api/users", token);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.Equal("TOKEN_EXPIRADO", await Codigo(respuesta));
    }

    [Theory]
    [InlineData("otro-emisor", GatewayFactory.Audience)]
    [InlineData(GatewayFactory.Issuer, "otra-audiencia")]
    public async Task Token_con_otro_iss_o_aud_devuelve_401(string issuer, string audience)
    {
        var token = TokenDePrueba.Crear(issuer: issuer, audience: audience);

        var respuesta = await Get("/api/users", token);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.Empty(_factory.Upstream.LogEntries);
    }

    [Fact]
    public async Task Users_con_token_valido_reenvia_a_users_sin_prefijo_api()
    {
        var respuesta = await Get("/api/users", TokenDePrueba.Crear());

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("Leanne Graham", await respuesta.Content.ReadAsStringAsync());
        var llamada = Assert.Single(_factory.Upstream.LogEntries);
        Assert.Equal("/users", llamada.RequestMessage!.Path);
    }

    [Fact]
    public async Task Users_con_token_valido_no_reenvia_authorization_al_tercero()
    {
        await Get("/api/users", TokenDePrueba.Crear());

        var llamada = Assert.Single(_factory.Upstream.LogEntries);
        Assert.False(llamada.RequestMessage!.Headers?.ContainsKey("Authorization") ?? false);
    }

    [Fact]
    public async Task Posts_con_token_valido_reenvia_a_posts()
    {
        var respuesta = await Get("/api/posts", TokenDePrueba.Crear());

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var llamada = Assert.Single(_factory.Upstream.LogEntries);
        Assert.Equal("/posts", llamada.RequestMessage!.Path);
    }

    [Fact]
    public async Task Post_a_users_no_esta_enrutado_y_no_llega_al_upstream()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TokenDePrueba.Crear());

        var respuesta = await _cliente.SendAsync(request);

        Assert.Contains(respuesta.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        Assert.Empty(_factory.Upstream.LogEntries);
    }

    [Fact]
    public async Task Health_sin_token_devuelve_200()
    {
        var respuesta = await Get("/health");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
    }

    [Fact]
    public void Todas_las_rutas_del_proxy_declaran_authorization_policy()
    {
        var configuracion = _factory.Services.GetRequiredService<IConfiguration>();
        var rutas = configuracion.GetSection("ReverseProxy:Routes").GetChildren().ToList();

        Assert.NotEmpty(rutas);
        Assert.All(rutas, ruta =>
            Assert.False(string.IsNullOrWhiteSpace(ruta["AuthorizationPolicy"]),
                $"La ruta '{ruta.Key}' no declara AuthorizationPolicy."));
    }
}
