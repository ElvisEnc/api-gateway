using System.Net;
using System.Net.Http.Headers;

using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace ApiGateway.Tests;

public sealed class IdentidadTransformTests : IClassFixture<GatewayFactory>
{
    private readonly GatewayFactory _factory;

    public IdentidadTransformTests(GatewayFactory factory)
    {
        _factory = factory;
        factory.Upstream.Reset();
        factory.Upstream
            .Given(Request.Create().WithPath("/users").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("[]"));
    }

    [Fact]
    public async Task Un_X_User_Id_falso_no_llega_a_jsonplaceholder()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TokenDePrueba.Crear(roles: "admin"));
        request.Headers.Add("X-User-Id", "hacker");

        var respuesta = await _factory.CreateClient().SendAsync(request);

        // A un tercero no se le propaga identidad: el header falso se descarta y no se añade ninguno.
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var headers = Assert.Single(_factory.Upstream.LogEntries).RequestMessage!.Headers!;
        Assert.False(headers.ContainsKey("X-User-Id"));
        Assert.False(headers.ContainsKey("X-User-Name"));
        Assert.False(headers.ContainsKey("X-User-Roles"));
    }
}
