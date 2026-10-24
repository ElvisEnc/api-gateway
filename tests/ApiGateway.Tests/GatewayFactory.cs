using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

using WireMock.Server;

namespace ApiGateway.Tests;

public class GatewayFactory: WebApplicationFactory<Program>
{
    public const string ClaveDePrueba = "clave-de-tests-de-al-menos-32-bytes-0123456789";
    public const string Issuer = "nur-tricenter-gateway";
    public const string Audience = "nur-tricenter-api";
    public WireMockServer Upstream { get; } = WireMockServer.Start();
    protected virtual string IntentosPorMinuto => "1000";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // UseSetting se aplica antes de que Program.cs lea la configuración.
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:Key", ClaveDePrueba);
        builder.UseSetting("Login:IntentosPorMinuto", IntentosPorMinuto);
        builder.UseSetting("ReverseProxy:Clusters:jsonplaceholder:Destinations:d1:Address", Upstream.Url!);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Upstream.Stop();
        base.Dispose(disposing);
    }
}

public sealed class GatewayFactoryLimitado : GatewayFactory
{
    protected override string IntentosPorMinuto => "3";
}
