using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
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
/// </summary>
[ApiController]
[Route("api/admin/auth")]
public class AuthController : ControllerBase
{
    private readonly IOlusoUserService _userService;
    private readonly ITenantStore _tenantStore;
    private readonly IRoleStore _roleStore;
    private readonly IConfiguration _configuration;
    private readonly IOlusoEventService _eventService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IOlusoUserService userService,
        ITenantStore tenantStore,
        IRoleStore roleStore,
        IConfiguration configuration,
        IOlusoEventService eventService,
        ILogger<AuthController> logger)
    {
        _userService = userService;
        _tenantStore = tenantStore;
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
        if (!string.IsNullOrEmpty(tenantIdentifier))
        {
            var tenant = await _tenantStore.GetByIdentifierAsync(tenantIdentifier, cancellationToken);
            if (tenant == null)
            {
                _logger.LogWarning("Admin login failed: tenant not found for identifier {TenantIdentifier}", tenantIdentifier);
                return Unauthorized(new { message = "Invalid username or password" });
            }
            tenantId = tenant.Id;
        }

        // Validate credentials
        var result = await _userService.ValidateCredentialsAsync(username, request.Password, tenantId, cancellationToken);

        if (!result.Success)
        {
            if (result.RequiresTenantQualifier)
            {
                _logger.LogWarning(
                    "Admin login failed: multiple users found for {Username}",
                    request.Username);

                return BadRequest(new
                {
                    message = "Multiple accounts found with this username. Please specify tenant: username@tenant",
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

        // Get role claims to check for admin_dashboard_access claim
        var roleClaims = new List<(string Type, string Value)>();
        foreach (var roleName in roles)
        {
            var role = await _roleStore.GetByNameAsync(roleName, user.TenantId, cancellationToken);
            if (role != null)
            {
                var claims = await _roleStore.GetRoleClaimsAsync(role.Id, cancellationToken);
                roleClaims.AddRange(claims.Select(c => (c.Type, c.Value)));
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

        // Generate JWT token
        var accessToken = GenerateJwtToken(user, roles);

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

        return Ok(new UserInfo
        {
            Id = user.Id,
            UserName = user.UserName,
            Email = user.Email,
            DisplayName = user.DisplayName ?? $"{user.FirstName} {user.LastName}".Trim(),
            Roles = roles.ToList(),
            TenantId = user.TenantId
        });
    }

    private string GenerateJwtToken(ValidatedUser user, IEnumerable<string> roles)
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
        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
            claims.Add(new Claim("role", role));
        }

        // Add tenant if present
        if (!string.IsNullOrEmpty(user.TenantId))
        {
            claims.Add(new Claim("tenant_id", user.TenantId));
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
    /// The tenant this admin belongs to. Null for system-level admins (SuperAdmin).
    /// </summary>
    public string? TenantId { get; set; }
}

#endregion
