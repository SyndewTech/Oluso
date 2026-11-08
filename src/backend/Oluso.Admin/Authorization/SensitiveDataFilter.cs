using System.Collections;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using Oluso.Core.Api;

namespace Oluso.Admin.Authorization;

/// <summary>
/// ASP.NET Core result filter that inspects response objects and nulls out properties
/// marked with [SensitiveData] if the current user lacks the required permission.
/// This provides field-level security at the API boundary, preventing data leakage
/// even when controller actions return full entity objects.
/// </summary>
public class SensitiveDataFilter : IAsyncResultFilter
{
    private readonly ILogger<SensitiveDataFilter> _logger;

    public SensitiveDataFilter(ILogger<SensitiveDataFilter> logger)
    {
        _logger = logger;
    }

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        // Skip filtering for actions/controllers marked with [BypassSensitiveDataFilter]
        // (e.g., self-service endpoints where users view their own data)
        var endpoint = context.HttpContext.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<BypassSensitiveDataFilterAttribute>() != null)
        {
            await next();
            return;
        }

        if (context.Result is ObjectResult objectResult && objectResult.Value != null)
        {
            var userPermissions = GetUserPermissions(context);
            if (userPermissions != null)
            {
                StripSensitiveData(objectResult.Value, userPermissions, new HashSet<object>(ReferenceEqualityComparer.Instance));
            }
        }

        await next();
    }

    /// <summary>
    /// Extracts the user's permissions from the token's "permissions" claim.
    /// Returns null only for unauthenticated requests.
    /// Note: SuperAdmin does NOT bypass field-level sensitive data checks.
    /// Sensitive data requires explicit permission grants regardless of role.
    /// </summary>
    private HashSet<string>? GetUserPermissions(ResultExecutingContext context)
    {
        var user = context.HttpContext.User;

        if (user?.Identity?.IsAuthenticated != true)
            return null;

        // Get permissions from token — handle both JSON array and individual claims
        var permissionsClaim = user.FindFirst("permissions")?.Value;
        if (string.IsNullOrEmpty(permissionsClaim))
        {
            // No permissions in token — treat as having no permissions (most restrictive)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        // Try JSON array format first (single claim with serialized array)
        if (permissionsClaim.StartsWith('['))
        {
            try
            {
                var permissions = JsonSerializer.Deserialize<string[]>(permissionsClaim);
                if (permissions != null)
                {
                    return new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (JsonException)
            {
                _logger.LogWarning("Failed to parse permissions claim as JSON array in SensitiveDataFilter");
            }
        }

        // JWT library split the JSON array into individual claims — collect them all
        var allClaims = user.FindAll("permissions").Select(c => c.Value).ToList();
        if (allClaims.Count > 0)
        {
            return new HashSet<string>(allClaims, StringComparer.OrdinalIgnoreCase);
        }

        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Recursively walks the object graph and nulls out properties
    /// marked with [SensitiveData] if the user lacks the required permission.
    /// </summary>
    private void StripSensitiveData(object obj, HashSet<string> userPermissions, HashSet<object> visited)
    {
        if (obj == null || !visited.Add(obj))
            return;

        var type = obj.GetType();

        // Handle collections (lists, arrays)
        if (obj is IEnumerable enumerable && type != typeof(string))
        {
            foreach (var item in enumerable)
            {
                if (item != null && !item.GetType().IsPrimitive && item.GetType() != typeof(string))
                {
                    StripSensitiveData(item, userPermissions, visited);
                }
            }
            return;
        }

        // Skip primitive types and strings
        if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal) ||
            type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(Guid))
            return;

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var property in properties)
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
                continue;

            var sensitiveAttr = property.GetCustomAttribute<SensitiveDataAttribute>();
            if (sensitiveAttr != null)
            {
                // Check if user has any of the required permissions (primary or alternates)
                if (!HasAnyPermission(userPermissions, sensitiveAttr))
                {
                    // Null out the property if writable
                    if (property.CanWrite)
                    {
                        var defaultValue = property.PropertyType.IsValueType && Nullable.GetUnderlyingType(property.PropertyType) == null
                            ? Activator.CreateInstance(property.PropertyType)
                            : null;
                        property.SetValue(obj, defaultValue);
                        _logger.LogDebug(
                            "Stripped sensitive property {Type}.{Property} (requires {Permission})",
                            type.Name, property.Name, sensitiveAttr.Permission);
                    }
                }
            }
            else
            {
                // Recurse into nested objects (only non-primitive, non-string types)
                var value = property.GetValue(obj);
                if (value != null && !property.PropertyType.IsPrimitive &&
                    property.PropertyType != typeof(string) &&
                    property.PropertyType != typeof(decimal) &&
                    property.PropertyType != typeof(DateTime))
                {
                    StripSensitiveData(value, userPermissions, visited);
                }
            }
        }
    }

    /// <summary>
    /// Checks if the user has any of the permissions specified by the attribute
    /// (primary permission or any alternate permissions).
    /// </summary>
    private static bool HasAnyPermission(HashSet<string> permissions, SensitiveDataAttribute attr)
    {
        if (HasPermission(permissions, attr.Permission))
            return true;

        foreach (var alt in attr.AlternatePermissions)
        {
            if (HasPermission(permissions, alt))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if the user has a specific permission, respecting wildcards
    /// and RequiresExplicitGrant rules.
    /// </summary>
    private static bool HasPermission(HashSet<string> permissions, string requiredPermission)
    {
        // Direct match
        if (permissions.Contains(requiredPermission))
            return true;

        // Sensitive permissions marked as RequiresExplicitGrant cannot use wildcards
        if (PermissionRegistry.Instance.RequiresExplicitGrant(requiredPermission))
            return false;

        // Check wildcards at each level
        if (permissions.Contains("*"))
            return true;

        var segments = requiredPermission.Split('.');
        var prefix = "";
        for (int i = 0; i < segments.Length - 1; i++)
        {
            prefix = i == 0 ? segments[i] : $"{prefix}.{segments[i]}";
            if (permissions.Contains($"{prefix}.*"))
                return true;
        }

        return false;
    }
}
