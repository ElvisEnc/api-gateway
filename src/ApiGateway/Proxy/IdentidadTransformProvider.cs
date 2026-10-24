using System.Security.Claims;

using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace ApiGateway.Proxy;

internal sealed class IdentidadTransformProvider : ITransformProvider
{
    public const string HeaderUserId = "X-User-Id";
    public const string HeaderUserName = "X-User-Name";
    public const string HeaderUserRoles = "X-User-Roles";
    public const string MetadataPropagar = "PropagarIdentidad";

    public void ValidateCluster(TransformClusterValidationContext context)
    {
    }

    public void ValidateRoute(TransformRouteValidationContext context)
    {
    }

    public void Apply(TransformBuilderContext context)
    {
        var propagar = context.Route.Metadata is { } metadata
            && metadata.TryGetValue(MetadataPropagar, out var valor)
            && string.Equals(valor, "true", StringComparison.OrdinalIgnoreCase);

        context.AddRequestTransform(transform =>
        {
            var headers = transform.ProxyRequest.Headers;
            headers.Remove(HeaderUserId);
            headers.Remove(HeaderUserName);
            headers.Remove(HeaderUserRoles);

            var usuario = transform.HttpContext.User;
            if (propagar && usuario.Identity?.IsAuthenticated == true)
            {
                headers.TryAddWithoutValidation(HeaderUserId, usuario.FindFirstValue("sub"));
                headers.TryAddWithoutValidation(HeaderUserName, usuario.FindFirstValue("name"));
                headers.TryAddWithoutValidation(HeaderUserRoles,
                    string.Join(',', usuario.FindAll("role").Select(c => c.Value)));
            }

            return ValueTask.CompletedTask;
        });
    }
}
