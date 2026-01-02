using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Oluso.Core.Protocols;

namespace Oluso.Protocols.Routing;

/// <summary>
/// MVC convention that applies configurable routes to protocol controllers
/// </summary>
public class ProtocolEndpointRouteConvention : IControllerModelConvention
{
    private readonly IReadOnlyDictionary<string, ProtocolRouteInfo> _routes;
    private readonly string _protocolName;

    public ProtocolEndpointRouteConvention(
        string protocolName,
        IReadOnlyDictionary<string, ProtocolRouteInfo> routes)
    {
        _protocolName = protocolName;
        _routes = routes;
    }

    public void Apply(ControllerModel controller)
    {
        var controllerName = controller.ControllerType.Name;

        if (!_routes.TryGetValue(controllerName, out var routeInfo))
            return;

        // Clear existing selectors to avoid duplicates
        controller.Selectors.Clear();

        // Add single route (policy comes from query param, not path)
        controller.Selectors.Add(new SelectorModel
        {
            AttributeRouteModel = new AttributeRouteModel
            {
                Template = routeInfo.Path.TrimStart('/')
            }
        });
    }
}

/// <summary>
/// OIDC-specific route convention
/// </summary>
public class OidcEndpointRouteConvention : ProtocolEndpointRouteConvention
{
    public OidcEndpointRouteConvention(OidcEndpointConfiguration config)
        : base("oidc", BuildRouteMap(config))
    {
    }

    private static Dictionary<string, ProtocolRouteInfo> BuildRouteMap(OidcEndpointConfiguration config)
    {
        return new Dictionary<string, ProtocolRouteInfo>(StringComparer.OrdinalIgnoreCase)
        {
            // User-facing endpoints (support policy param)
            ["OidcAuthorizeController"] = ProtocolRouteInfo.Create(
                config.AuthorizeEndpoint,
                EndpointType.Authorize,
                supportsPolicyParam: true,
                "GET", "POST"),

            ["OidcEndSessionController"] = ProtocolRouteInfo.Create(
                config.EndSessionEndpoint,
                EndpointType.Logout,
                supportsPolicyParam: true,
                "GET", "POST"),

            ["OidcDeviceAuthorizationController"] = ProtocolRouteInfo.Create(
                config.DeviceAuthorizationEndpoint,
                EndpointType.DeviceAuthorization,
                supportsPolicyParam: true,
                "POST"),

            // Machine-to-machine endpoints (no policy param)
            ["OidcTokenController"] = ProtocolRouteInfo.Create(
                config.TokenEndpoint,
                EndpointType.Token,
                supportsPolicyParam: false,
                "POST"),

            ["OidcUserInfoController"] = ProtocolRouteInfo.Create(
                config.UserInfoEndpoint,
                EndpointType.UserInfo,
                supportsPolicyParam: false,
                "GET", "POST"),

            ["OidcRevocationController"] = ProtocolRouteInfo.Create(
                config.RevocationEndpoint,
                EndpointType.Revocation,
                supportsPolicyParam: false,
                "POST"),

            ["OidcIntrospectionController"] = ProtocolRouteInfo.Create(
                config.IntrospectionEndpoint,
                EndpointType.Introspection,
                supportsPolicyParam: false,
                "POST"),

            ["OidcPushedAuthorizationController"] = ProtocolRouteInfo.Create(
                config.PushedAuthorizationEndpoint,
                EndpointType.PushedAuthorization,
                supportsPolicyParam: false,
                "POST"),

            ["OidcCibaController"] = ProtocolRouteInfo.Create(
                config.BackchannelAuthenticationEndpoint,
                EndpointType.BackchannelAuthentication,
                supportsPolicyParam: false,
                "POST"),

            // Metadata endpoints
            ["OidcDiscoveryController"] = ProtocolRouteInfo.Create(
                config.DiscoveryEndpoint,
                EndpointType.Metadata,
                supportsPolicyParam: false,
                "GET"),

            ["OidcJwksController"] = ProtocolRouteInfo.Create(
                config.JwksEndpoint,
                EndpointType.Metadata,
                supportsPolicyParam: false,
                "GET"),
        };
    }
}
