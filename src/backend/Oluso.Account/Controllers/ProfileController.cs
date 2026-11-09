using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Account.Dto.Profile;
using Oluso.Core.Api;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Core.Storage;

namespace Oluso.Account.Controllers;

/// <summary>
/// Account API for managing user's own profile
/// </summary>
[Route("api/account/profile")]
public class ProfileController : AccountBaseController
{
    private readonly IOlusoUserService _userService;
    private readonly ILogger<ProfileController> _logger;

    public ProfileController(
        ITenantContext tenantContext,
        IOlusoUserService userService,
        ILogger<ProfileController> logger) : base(tenantContext)
    {
        _userService = userService;
        _logger = logger;
    }

    /// <summary>
    /// Get the current user's profile
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<UserProfileDto>> GetProfile(CancellationToken cancellationToken)
    {
        var user = await _userService.FindByIdAsync(UserId, cancellationToken);

        if (user == null)
        {
            return NotFound(new { error = "User not found" });
        }

        return Ok(MapToProfileDto(user));
    }

    /// <summary>
    /// Update the current user's profile
    /// </summary>
    [HttpPut]
    public async Task<ActionResult<UserProfileDto>> UpdateProfile(
        [FromBody] UpdateProfileRequest request,
        CancellationToken cancellationToken)
    {
        var updateRequest = new UpdateUserRequest
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            PhoneNumber = request.PhoneNumber,
            Picture = request.Picture
        };

        var result = await _userService.UpdateUserAsync(UserId, updateRequest, cancellationToken);

        if (!result.Succeeded)
        {
            return BadRequest(new { errors = result.MessagesOr("Update failed") });
        }

        _logger.LogInformation("User {UserId} updated their profile", UserId);

        var user = result.User ?? await _userService.FindByIdAsync(UserId, cancellationToken);

        return Ok(MapToProfileDto(user!));
    }

    private static UserProfileDto MapToProfileDto(OlusoUserInfo user) => new()
    {
        Id = user.Id,
        Email = user.Email ?? "",
        UserName = user.Username,
        FirstName = user.FirstName,
        LastName = user.LastName,
        DisplayName = user.DisplayName ?? $"{user.FirstName} {user.LastName}".Trim(),
        Picture = user.Picture,
        PhoneNumber = user.PhoneNumber,
        EmailVerified = user.EmailVerified,
        PhoneNumberVerified = user.PhoneNumberVerified
    };

    /// <summary>
    /// Upload a profile picture
    /// </summary>
    [HttpPost("picture")]
    public async Task<ActionResult<AvatarUploadResult>> UploadPicture(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { error = "No file provided" });
        }

        // Validate file type
        var allowedTypes = new[] { "image/jpeg", "image/png", "image/gif", "image/webp" };
        if (!allowedTypes.Contains(file.ContentType.ToLowerInvariant()))
        {
            return BadRequest(new { error = "Invalid file type. Allowed: JPEG, PNG, GIF, WebP" });
        }

        // Validate file size (max 5MB)
        const long maxSize = 5 * 1024 * 1024;
        if (file.Length > maxSize)
        {
            return BadRequest(new { error = "File too large. Maximum size is 5MB" });
        }

        var fileUploader = HttpContext.RequestServices.GetService<IFileUploader>();
        if (fileUploader == null)
        {
            return StatusCode(503, new { error = "File storage not configured" });
        }

        try
        {
            // Profile pictures are PUBLIC assets (shown anonymously in user lists,
            // comments, etc.) — store under the public prefix with a stable,
            // non-expiring URL, served via CDN/blob directly or the anonymous
            // public endpoint in dev.
            var extension = Path.GetExtension(file.FileName);
            var fileName = $"profile-pictures/{UserId}/{Guid.NewGuid()}{extension}";

            // Buffer once so we can upload the original and pre-generate variants.
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, cancellationToken);
            var bytes = ms.ToArray();

            var result = await fileUploader.UploadAsync(
                fileName,
                bytes,
                FileAccessLevel.Public,
                file.ContentType,
                cancellationToken: cancellationToken);

            if (!result.Success)
            {
                _logger.LogError("Failed to upload avatar: {Error}", result.Error);
                return StatusCode(500, new { error = "Failed to upload file" });
            }

            // Pre-generate size variants (served directly from blob/CDN on read).
            var variantGenerator = HttpContext.RequestServices.GetService<IPublicImageVariantGenerator>();
            if (variantGenerator != null)
                await variantGenerator.GenerateAsync(fileName, bytes, cancellationToken);

            // Stable public URL: the provider's direct/CDN URL when available,
            // otherwise the anonymous public serve endpoint.
            var avatarUrl = result.PublicUrl ?? $"{Request.Scheme}://{Request.Host}/api/files/raw/{fileName}";

            // Update user's picture with the proper URL
            var updateResult = await _userService.UpdateUserAsync(UserId, new UpdateUserRequest
            {
                Picture = avatarUrl
            }, cancellationToken);

            if (!updateResult.Succeeded)
            {
                // Try to clean up uploaded file
                await fileUploader.DeleteAsync(fileName, cancellationToken);
                return BadRequest(new { error = updateResult.MessageOr("Failed to update profile") });
            }

            _logger.LogInformation("User {UserId} uploaded new avatar", UserId);

            return Ok(new AvatarUploadResult
            {
                Url = avatarUrl,
                Success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading avatar for user {UserId}", UserId);
            return StatusCode(500, new { error = "An error occurred while uploading the file" });
        }
    }

    /// <summary>
    /// Get a signed URL for direct upload to blob storage
    /// </summary>
    [HttpPost("picture/upload-url")]
    public async Task<ActionResult<UploadUrlResult>> GetPictureUploadUrl(
        [FromBody] GetUploadUrlRequest request,
        CancellationToken cancellationToken)
    {
        var allowedTypes = new[] { "image/jpeg", "image/png", "image/gif", "image/webp" };
        if (!allowedTypes.Contains(request.ContentType?.ToLowerInvariant() ?? ""))
        {
            return BadRequest(new { error = "Invalid content type. Allowed: JPEG, PNG, GIF, WebP" });
        }

        var fileUploader = HttpContext.RequestServices.GetService<IFileUploader>();
        if (fileUploader == null)
        {
            return StatusCode(503, new { error = "File storage not configured" });
        }

        try
        {
            var extension = request.ContentType?.ToLowerInvariant() switch
            {
                "image/jpeg" => ".jpg",
                "image/png" => ".png",
                "image/gif" => ".gif",
                "image/webp" => ".webp",
                _ => ".jpg"
            };

            var fileName = $"avatars/{UserId}/{Guid.NewGuid()}{extension}";

            var signedUrl = await fileUploader.GetSignedUrlAsync(
                fileName,
                TimeSpan.FromMinutes(15),
                SignedUrlPermissions.Write,
                cancellationToken);

            if (string.IsNullOrEmpty(signedUrl))
            {
                return StatusCode(503, new { error = "Unable to generate upload URL" });
            }

            return Ok(new UploadUrlResult
            {
                UploadUrl = signedUrl,
                FileName = fileName,
                ExpiresIn = 900 // 15 minutes in seconds
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating upload URL for user {UserId}", UserId);
            return StatusCode(500, new { error = "An error occurred" });
        }
    }

    /// <summary>
    /// Confirm picture upload (after direct upload to blob storage)
    /// </summary>
    [HttpPost("picture/confirm")]
    public async Task<ActionResult<AvatarUploadResult>> ConfirmPictureUpload(
        [FromBody] ConfirmUploadRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.FileName))
        {
            return BadRequest(new { error = "File name is required" });
        }

        // Verify the file belongs to this user
        if (!request.FileName.StartsWith($"avatars/{UserId}/"))
        {
            return BadRequest(new { error = "Invalid file" });
        }

        var fileUploader = HttpContext.RequestServices.GetService<IFileUploader>();
        if (fileUploader == null)
        {
            return StatusCode(503, new { error = "File storage not configured" });
        }

        // The client uploaded to a private staging path via SAS. Pull it down,
        // re-store it as a PUBLIC profile picture (stable, non-expiring URL) and
        // generate size variants — matching the direct-upload path — then drop
        // the staging blob. Avatars are small, so the single download is cheap.
        var staged = await fileUploader.DownloadAsync(request.FileName, cancellationToken);
        if (staged == null)
        {
            return BadRequest(new { error = "File not found" });
        }

        var extension = Path.GetExtension(request.FileName);
        var publicName = $"profile-pictures/{UserId}/{Guid.NewGuid()}{extension}";
        var contentType = extension.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "image/jpeg",
        };

        var result = await fileUploader.UploadAsync(publicName, staged, FileAccessLevel.Public, contentType, cancellationToken: cancellationToken);
        if (!result.Success)
        {
            _logger.LogError("Failed to store confirmed avatar: {Error}", result.Error);
            return StatusCode(500, new { error = "Failed to store picture" });
        }

        var variantGenerator = HttpContext.RequestServices.GetService<IPublicImageVariantGenerator>();
        if (variantGenerator != null)
            await variantGenerator.GenerateAsync(publicName, staged, cancellationToken);

        // Remove the private staging upload now it's been promoted to public.
        await fileUploader.DeleteAsync(request.FileName, cancellationToken);

        var avatarUrl = result.PublicUrl ?? $"{Request.Scheme}://{Request.Host}/api/files/raw/{publicName}";

        // Update user's picture with the proper URL
        var updateResult = await _userService.UpdateUserAsync(UserId, new UpdateUserRequest
        {
            Picture = avatarUrl
        }, cancellationToken);

        if (!updateResult.Succeeded)
        {
            return BadRequest(new { error = updateResult.MessageOr("Failed to update profile") });
        }

        _logger.LogInformation("User {UserId} confirmed avatar upload: {FileName}", UserId, request.FileName);

        return Ok(new AvatarUploadResult
        {
            Url = avatarUrl,
            Success = true
        });
    }

    /// <summary>
    /// Serve a file from storage (for local file storage)
    /// </summary>
    [HttpGet("files/{*path}")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public async Task<IActionResult> ServeFile(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(path))
        {
            return NotFound();
        }

        var fileUploader = HttpContext.RequestServices.GetService<IFileUploader>();
        if (fileUploader == null)
        {
            return StatusCode(503, new { error = "File storage not configured" });
        }

        // Only allow serving files from avatars directory
        if (!path.StartsWith("avatars/"))
        {
            return Forbid();
        }

        var content = await fileUploader.DownloadAsync(path, cancellationToken);
        if (content == null)
        {
            return NotFound();
        }

        // Determine content type from extension
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var contentType = extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };

        return File(content, contentType);
    }

    /// <summary>
    /// Get the URL for serving a file through our endpoint
    /// </summary>
    private string GetFileServeUrl(string fileName)
    {
        var request = HttpContext.Request;
        return $"{request.Scheme}://{request.Host}/api/account/profile/files/{fileName}";
    }

    /// <summary>
    /// Get tenants the user belongs to (for multi-tenant users)
    /// </summary>
    [HttpGet("tenants")]
    public async Task<ActionResult<UserTenantsDto>> GetTenants(CancellationToken cancellationToken)
    {
        var tenantIds = GetUserTenantIds();
        var tenantStore = HttpContext.RequestServices.GetService(typeof(ITenantStore)) as ITenantStore;
        var settingsProvider = HttpContext.RequestServices.GetService(typeof(ITenantSettingsProvider)) as ITenantSettingsProvider;

        var tenants = new List<TenantInfoDto>();

        if (tenantStore != null)
        {
            foreach (var tenantId in tenantIds)
            {
                var tenant = await tenantStore.GetByIdAsync(tenantId, cancellationToken);
                if (tenant != null && tenant.Enabled)
                {
                    var logoUrl = settingsProvider != null
                        ? (await settingsProvider.GetBrandingSettingsAsync(tenant.Id, cancellationToken)).LogoUrl
                        : null;
                    tenants.Add(new TenantInfoDto
                    {
                        Id = tenant.Id,
                        Name = tenant.Name,
                        DisplayName = tenant.DisplayName,
                        LogoUrl = logoUrl
                    });
                }
            }
        }

        return Ok(new UserTenantsDto
        {
            CurrentTenantId = TenantId,
            Tenants = tenants,
            IsMultiTenant = tenants.Count > 1
        });
    }
}