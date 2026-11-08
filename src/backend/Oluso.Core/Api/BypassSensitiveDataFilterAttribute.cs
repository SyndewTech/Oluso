namespace Oluso.Core.Api;

/// <summary>
/// When applied to a controller or action, the SensitiveDataFilter will not strip
/// [SensitiveData] properties from the response. Use this on self-service endpoints
/// where users are viewing their own data and should see all fields regardless of permissions.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public class BypassSensitiveDataFilterAttribute : Attribute
{
}
