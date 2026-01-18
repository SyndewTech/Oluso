using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Oluso.Admin.Authorization;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Authentication controller for Admin UI.
/// Admin login is NOT tenant-scoped - admins can log in from any domain.
/// After login, the JWT contains the admin's TenantId which determines data access.
///
/// Tenant resolution priority for login:
/// 1. Explicit tenant qualifier in username (e.g., "john@acme")
/// 2. Tenant context from subdomain/domain (e.g., acme.example.com)
/// 3. Ask user to specify if multiple accounts found
/// </summary>
[ApiController]
[Route("api/admin/auth")]
public class AuthController : ControllerBase
{
    private readonly IOlusoUserService _userService;
    private readonly ITenantStore _tenantStore;
    private readonly ITenantContext _tenantContext;
    private readonly IRoleStore _roleStore;
    private readonly IConfiguration _configuration;
    private readonly IOlusoEventService _eventService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IOlusoUserService userService,
        ITenantStore tenantStore,
        ITenantContext tenantContext,
        IRoleStore roleStore,
        IConfiguration configuration,
        IOlusoEventService eventService,
        ILogger<AuthController> logger)
    {
        _userService = userService;
        _tenantStore = tenantStore;
        _tenantContext = tenantContext;
        _roleStore = roleStore;
        _configuration = configuration;
        _eventService = eventService;
        _logger = logger;
    }

    /// <summary>
    /// Authenticate admin user and return JWT token.
    /// This endpoint does NOT use tenant-scoped lookup - admins can log in from any domain.
    ///
    /// Login formats supported:
    /// - "admin" or "admin@localhost" - unique username/email across all tenants
    /// - "john@acme" - username "john" in tenant with identifier "acme"
    ///
    /// If multiple users exist with the same username across tenants, use the tenant-qualified format.
    /// </summary>
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { message = "Username and password are required" });
        }

        // Parse username - check for tenant qualifier (username@tenantIdentifier)
        var (username, tenantIdentifier) = ParseUsernameWithTenant(request.Username);

        string? tenantId = null;

        // Priority 1: Explicit tenant qualifier in username (e.g., "john@acme")
        if (!string.IsNullOrEmpty(tenantIdentifier))
        {
            var tenant = await _tenantStore.GetByIdentifierAsync(tenantIdentifier, cancellationToken);
            if (tenant == null)
            {
                _logger.LogWarning("Admin login failed: tenant not found for identifier {TenantIdentifier}", tenantIdentifier);
                return Unauthorized(new { message = "Invalid username or password" });
            }
            tenantId = tenant.Id;
            _logger.LogDebug("Using tenant from username qualifier: {TenantId}", tenantId);
        }
        // Priority 2: Tenant context from subdomain/domain resolution
        else if (_tenantContext.HasTenant)
        {
            tenantId = _tenantContext.TenantId;
            _logger.LogDebug("Using tenant from request context (subdomain/domain): {TenantId}", tenantId);
        }

        // Validate credentials
        var result = await _userService.ValidateCredentialsAsync(username, request.Password, tenantId, cancellationToken);

        if (!result.Success)
        {
            if (result.RequiresTenantQualifier)
            {
                _logger.LogWarning(
                    "Admin login failed: multiple users found for {Username} across tenants",
                    request.Username);

                return BadRequest(new
                {
                    message = "Multiple accounts found with this username. Either: (1) access via tenant subdomain, or (2) specify tenant in username: username@tenant",
                    code = "MULTIPLE_ACCOUNTS",
                    tenants = result.AvailableTenants
                });
            }

            // Raise login failed event
            await _eventService.RaiseAsync(new UserSignInFailedEvent
            {
                TenantId = tenantId,
                Username = username,
                ClientId = "admin-ui",
                FailureReason = result.Error ?? "Invalid credentials",
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
            }, cancellationToken);

            _logger.LogWarning("Admin login failed: {Error}", result.Error);
            return Unauthorized(new { message = result.Error ?? "Invalid username or password" });
        }

        var user = result.User!;

        // Check if user has admin dashboard access
        // User needs either:
        // 1. One of the admin roles (SuperAdmin, SystemAdmin, TenantAdmin, Admin), OR
        // 2. A role with the admin_dashboard_access claim set to "true"
        var roles = await _userService.GetUserRolesAsync(user.Id, cancellationToken);

        // Get role claims and permissions
        var roleClaims = new List<(string Type, string Value)>();
        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var roleName in roles)
        {
            // Try tenant-specific role first, then global
            var role = await _roleStore.GetByNameAsync(roleName, user.TenantId, cancellationToken)
                ?? await _roleStore.GetByNameAsync(roleName, null, cancellationToken);
            if (role != null)
            {
                var claims = await _roleStore.GetRoleClaimsAsync(role.Id, cancellationToken);
                roleClaims.AddRange(claims.Select(c => (c.Type, c.Value)));

                // Collect permissions from role
                foreach (var permission in role.GetPermissions())
                {
                    permissions.Add(permission);
                }
            }
        }

        var hasAdminAccess = ReservedClaimTypes.HasAdminDashboardAccess(roles, roleClaims);

        if (!hasAdminAccess)
        {
            _logger.LogWarning("Admin login failed: user {UserId} does not have admin dashboard access", user.Id);
            return Forbid();
        }

        // Update last login time
        await _userService.UpdateLastLoginAsync(user.Id, cancellationToken);

        // Generate JWT token with permissions
        var accessToken = GenerateJwtToken(user, roles, permissions);

        // Raise login success event
        await _eventService.RaiseAsync(new UserSignedInEvent
        {
            TenantId = user.TenantId,
            SubjectId = user.Id,
            Username = user.UserName ?? username,
            ClientId = "admin-ui",
            AuthenticationMethod = "pwd",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
        }, cancellationToken);

        _logger.LogInformation(
            "Admin user {UserId} logged in successfully (TenantId: {TenantId})",
            user.Id, user.TenantId ?? "system");

        return Ok(new LoginResponse
        {
            User = new UserInfo
            {
                Id = user.Id,
                UserName = user.UserName,
                Email = user.Email,
                DisplayName = user.DisplayName ?? $"{user.FirstName} {user.LastName}".Trim(),
                Roles = roles.ToList(),
                Permissions = permissions.ToList(),
                TenantId = user.TenantId
            },
            AccessToken = accessToken
        });
    }

    /// <summary>
    /// Parse username that may include tenant qualifier.
    /// Formats: "username", "user@tenant", "email@domain.com", "email@domain.com@tenant"
    /// </summary>
    private static (string username, string? tenantIdentifier) ParseUsernameWithTenant(string input)
    {
        if (string.IsNullOrEmpty(input))
            return (input, null);

        // Check if it looks like an email (contains @ followed by a domain with a dot)
        var atIndex = input.LastIndexOf('@');
        if (atIndex <= 0)
            return (input, null); // No @ or @ at start

        var afterAt = input[(atIndex + 1)..];

        // If what's after @ contains a dot, it's likely a domain (email)
        // Check if there's another @ before this one for tenant qualifier
        if (afterAt.Contains('.'))
        {
            // This looks like an email - check for tenant suffix after the email
            // e.g., "john@example.com@acme" -> email="john@example.com", tenant="acme"
            // For simplicity, we don't support this format - emails are globally unique
            return (input, null);
        }

        // No dot after @ - this is username@tenant format
        var username = input[..atIndex];
        var tenant = afterAt;

        return (username, tenant);
    }

    /// <summary>
    /// Get current user info from JWT token
    /// </summary>
    [HttpGet("me")]
    public async Task<ActionResult<UserInfo>> GetCurrentUser(CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var user = await _userService.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            return NotFound();
        }

        var roles = await _userService.GetUserRolesAsync(userId, cancellationToken);

        // Collect permissions from roles
        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var roleName in roles)
        {
            var role = await _roleStore.GetByNameAsync(roleName, user.TenantId, cancellationToken)
                ?? await _roleStore.GetByNameAsync(roleName, null, cancellationToken);
            if (role != null)
            {
                foreach (var permission in role.GetPermissions())
                {
                    permissions.Add(permission);
                }
            }
        }

        return Ok(new UserInfo
        {
            Id = user.Id,
            UserName = user.UserName,
            Email = user.Email,
            DisplayName = user.DisplayName ?? $"{user.FirstName} {user.LastName}".Trim(),
            Roles = roles.ToList(),
            Permissions = permissions.ToList(),
            TenantId = user.TenantId
        });
    }

    private string GenerateJwtToken(ValidatedUser user, IEnumerable<string> roles, IEnumerable<string> permissions)
    {
        var jwtKey = _configuration["Oluso:Jwt:Key"]
            ?? _configuration["Oluso:AdminJwtKey"]
            ?? throw new InvalidOperationException("JWT key not configured. Set 'Jwt:Key' in configuration.");

        var jwtIssuer = _configuration["Jwt:Issuer"]
            ?? _configuration["Oluso:IssuerUri"];

        var jwtAudience = _configuration["Jwt:Audience"] ?? "admin-ui";
        var expirationMinutes = _configuration.GetValue<int>("Jwt:ExpirationMinutes", 60);

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email ?? ""),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName ?? ""),
            new("name", user.DisplayName ?? user.UserName ?? "")
        };

        // Add roles
        var rolesList = roles.ToList();
        foreach (var role in rolesList)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
            claims.Add(new Claim("role", role));
        }

        // Add super_admin claim for SuperAdmin/SystemAdmin users
        if (rolesList.Any(r => r.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase) ||
                               r.Equals("SystemAdmin", StringComparison.OrdinalIgnoreCase)))
        {
            claims.Add(new Claim("super_admin", "true"));
        }

        // Add tenant if present
        if (!string.IsNullOrEmpty(user.TenantId))
        {
            claims.Add(new Claim("tenant_id", user.TenantId));
        }

        // Add permissions as JSON array (ensure distinct)
        var permissionsList = permissions.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (permissionsList.Count > 0)
        {
            var permissionsJson = JsonSerializer.Serialize(permissionsList);
            claims.Add(new Claim("permissions", permissionsJson, JsonClaimValueTypes.JsonArray));
        }

        var token = new JwtSecurityToken(
            issuer: jwtIssuer,
            audience: jwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expirationMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

#region DTOs

public class LoginRequest
{
    public string Username { get; set; } = null!;
    public string Password { get; set; } = null!;
}

public class LoginResponse
{
    public UserInfo User { get; set; } = null!;
    public string AccessToken { get; set; } = null!;
}

public class UserInfo
{
    public string Id { get; set; } = null!;
    public string UserName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? DisplayName { get; set; }
    public List<string> Roles { get; set; } = new();
    /// <summary>
    /// Permissions derived from the user's roles.
    /// </summary>
    public List<string> Permissions { get; set; } = new();
    /// <summary>
    /// The tenant this admin belongs to. Null for system-level admins (SuperAdmin).
    /// </summary>
    public string? TenantId { get; set; }
}

#endregion
