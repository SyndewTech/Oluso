using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Core.Authentication;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Dto.PublicLinks;
using Oluso.PublicLinks;

namespace Oluso.Controllers;

/// <summary>
/// Generic, anonymous landing endpoint for action-token links. Drives the
/// "magic link" UX across the platform — approval decide, vendor invoice
/// confirm, candidate offer accept, etc. The token is the entire
/// authorisation; modules plug in by registering
/// <see cref="IActionTokenHandler"/> implementations for their action keys.
///
/// <para><b>URL-leak safety</b>: tokens are never in the URL path or query
/// string — both endpoints are POSTs that take the token in the JSON body.
/// The magic-link email points the recipient at a workspace page like
/// <c>/m#tk={token}</c> (fragment), which reads <c>window.location.hash</c>
/// client-side and POSTs the token here. Fragments never reach the server,
/// so the token doesn't appear in server access logs, browser history
/// auto-fill, or <c>Referer</c> headers.</para>
///
/// <para>The frontend renders one generic <c>/m</c> page that calls
/// <see cref="Preview"/> first and <see cref="Redeem"/> when the user picks
/// an option. Adding a new magic-link flow doesn't require any UI work —
/// only a new <see cref="IActionTokenHandler"/>.</para>
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/public/links")]
public class PublicLinksController : ControllerBase
{
    private readonly IActionTokenService _tokenService;
    private readonly IEnumerable<IActionTokenHandler> _handlers;
    private readonly ITenantStore _tenantStore;
    private readonly ITenantContextAccessor _tenantContext;
    private readonly ILogger<PublicLinksController> _logger;
    private readonly IPublicLinkUnlockService? _unlock;

    public PublicLinksController(
        IActionTokenService tokenService,
        IEnumerable<IActionTokenHandler> handlers,
        ITenantStore tenantStore,
        ITenantContextAccessor tenantContext,
        ILogger<PublicLinksController> logger,
        IPublicLinkUnlockService? unlock = null)
    {
        _tokenService = tokenService;
        _handlers = handlers;
        _tenantStore = tenantStore;
        _tenantContext = tenantContext;
        _logger = logger;
        _unlock = unlock;
    }

    /// <summary>
    /// Inspect the token (body-carried) and return the preview view-model
    /// for the landing page. Does not consume the token.
    /// </summary>
    [HttpPost("preview")]
    public async Task<ActionResult<ActionTokenPreview>> Preview(
        [FromBody] PreviewActionTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Token))
            return BadRequest(new { error = "token is required" });

        var claim = await _tokenService.PeekAsync(request.Token, cancellationToken);
        if (claim == null)
            return Gone("This link is invalid, expired, or has already been used.");
        var (tenantFound, tokenTenant) = await FindTokenTenantAsync(claim, cancellationToken);
        if (!tenantFound)
            return Gone("This link's organisation no longer exists.");
        if (tokenTenant != null)
            _tenantContext.SetTenant(tokenTenant);

        var handler = FindHandler(claim.Action);
        if (handler == null)
        {
            _logger.LogWarning(
                "No IActionTokenHandler registered for action '{Action}'",
                claim.Action);
            return Gone("This link's action is no longer supported.");
        }

        var requirement = await LockedRequirementAsync(handler, claim, request.Token, request.Unlock, cancellationToken);
        if (requirement != null)
        {
            return Ok(new ActionTokenPreview
            {
                Title = requirement.Title,
                Description = requirement.Description,
                Data = requirement.Data,
                Locked = true,
                LockMode = requirement.Mode == ActionTokenLockMode.EmailCode ? "code" : "email",
            });
        }

        var preview = await handler.PreviewAsync(claim, cancellationToken);
        if (preview == null)
            return Gone("This link is no longer actionable.");

        return Ok(preview);
    }

    /// <summary>
    /// Atomically redeem the token (body-carried) and dispatch the chosen
    /// option to the matching handler.
    /// </summary>
    /// <summary>
    /// Stream the document behind a token, when its handler provides one.
    /// Peeks rather than redeems, so the link stays valid for re-downloads.
    /// </summary>
    [HttpPost("file")]
    public async Task<IActionResult> File(
        [FromBody] FileActionTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Token))
            return BadRequest(new { error = "token is required" });

        var claim = await _tokenService.PeekAsync(request.Token, cancellationToken);
        if (claim == null)
            return Gone("This link is invalid, expired, or has already been used.");
        var (tenantFound, tokenTenant) = await FindTokenTenantAsync(claim, cancellationToken);
        if (!tenantFound)
            return Gone("This link's organisation no longer exists.");
        if (tokenTenant != null)
            _tenantContext.SetTenant(tokenTenant);

        if (FindHandler(claim.Action) is not IActionTokenFileProvider provider)
            return NotFound(new { error = "This link has no document." });
        if (await LockedRequirementAsync((IActionTokenHandler)provider, claim, request.Token, request.Unlock, cancellationToken) != null)
            return LockedResponse();

        var file = await provider.GetFileAsync(claim, cancellationToken);
        if (file == null)
            return NotFound(new { error = "This link's document is no longer available." });

        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpPost("redeem")]
    public async Task<ActionResult<ActionTokenExecuteResult>> Redeem(
        [FromBody] RedeemActionTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Token))
            return BadRequest(new { error = "token is required" });
        if (string.IsNullOrWhiteSpace(request.Option))
            return BadRequest(new { error = "option is required" });

        var claim = await _tokenService.PeekAsync(request.Token, cancellationToken);
        if (claim == null)
            return Gone("This link is invalid, expired, or has already been used.");
        var (tenantFound, tokenTenant) = await FindTokenTenantAsync(claim, cancellationToken);
        if (!tenantFound)
            return Gone("This link's organisation no longer exists.");
        if (tokenTenant != null)
            _tenantContext.SetTenant(tokenTenant);

        var handler = FindHandler(claim.Action);
        if (handler == null)
        {
            _logger.LogWarning(
                "Token redeemed but no handler for action '{Action}'",
                claim.Action);
            return Gone("This link's action is no longer supported.");
        }

        if (await LockedRequirementAsync(handler, claim, request.Token, request.Unlock, cancellationToken) != null)
            return LockedResponse();

        // A single-use link is consumed before the action runs, so two clicks
        // can never both act; if the action then fails the link is given back.
        if (handler.SingleUse && await _tokenService.RedeemAsync(request.Token, cancellationToken) == null)
            return Gone("This link is invalid, expired, or has already been used.");

        try
        {
            var fields = (IReadOnlyDictionary<string, string>?)request.Fields ?? new Dictionary<string, string>();
            var result = await handler.ExecuteAsync(claim, request.Option, request.Comments, fields, cancellationToken);
            _logger.LogInformation(
                "Magic link redeemed: action={Action} subject={SubjectId} option={Option} user={UserId}",
                claim.Action, claim.SubjectId, request.Option, claim.UserId);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            await ReleaseAsync(handler, request.Token);
            return StatusCode(StatusCodes.Status403Forbidden, new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            await ReleaseAsync(handler, request.Token);
            return Conflict(new { Message = ex.Message });
        }
        catch
        {
            await ReleaseAsync(handler, request.Token);
            throw;
        }
    }

    /// <summary>
    /// Confirm the visitor is the recipient of a locked link: the address it was
    /// sent to, and when required a one-time code mailed there. Returns a proof
    /// for later requests on this link.
    /// </summary>
    [HttpPost("unlock")]
    public async Task<ActionResult<UnlockActionTokenResponse>> Unlock(
        [FromBody] UnlockActionTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Token))
            return BadRequest(new { error = "token is required" });
        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { Message = "Enter the email address this was sent to." });

        var claim = await _tokenService.PeekAsync(request.Token, cancellationToken);
        if (claim == null)
            return Gone("This link is invalid, expired, or has already been used.");
        var (tenantFound, tokenTenant) = await FindTokenTenantAsync(claim, cancellationToken);
        if (!tenantFound)
            return Gone("This link's organisation no longer exists.");
        if (tokenTenant != null)
            _tenantContext.SetTenant(tokenTenant);

        var handler = FindHandler(claim.Action);
        if (handler == null)
            return Gone("This link's action is no longer supported.");
        if (handler is not IActionTokenAccessGuard guard
            || await guard.GetAccessRequirementAsync(claim, cancellationToken) is not { } requirement)
        {
            return Ok(new UnlockActionTokenResponse());
        }
        if (_unlock == null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { Message = "This document cannot be opened online right now." });

        var result = await _unlock.UnlockAsync(request.Token, claim.TenantId, requirement, request.Email, request.Code, cancellationToken);
        return result.Outcome switch
        {
            UnlockOutcome.Unlocked => Ok(new UnlockActionTokenResponse { Unlock = result.Proof }),
            UnlockOutcome.CodeSent => Ok(new UnlockActionTokenResponse
            {
                CodeSent = true,
                Message = "If that is the address this was sent to, a 6-digit code is on its way. It expires in 10 minutes."
            }),
            UnlockOutcome.Mismatch => BadRequest(new { Message = "That is not the email address this was sent to." }),
            UnlockOutcome.WrongCode => BadRequest(new { Message = "That code is not right or has expired." }),
            UnlockOutcome.CodeCooldown => StatusCode(StatusCodes.Status429TooManyRequests, new { Message = "A code was sent less than a minute ago. Check your inbox." }),
            UnlockOutcome.LockedOut => StatusCode(StatusCodes.Status429TooManyRequests, new { Message = "Too many attempts. Try again in 15 minutes." }),
            UnlockOutcome.NoEmailOnFile => BadRequest(new { Message = "This document cannot be opened online. Please contact the sender." }),
            _ => StatusCode(StatusCodes.Status502BadGateway, new { Message = "The code could not be sent. Try again shortly." }),
        };
    }

    /// <summary>The handler's lock when this visitor has not unlocked it yet; null when the content may be shown.</summary>
    private async Task<ActionTokenAccessRequirement?> LockedRequirementAsync(
        IActionTokenHandler handler, ActionTokenClaim claim, string rawToken, string? proof, CancellationToken cancellationToken)
    {
        if (handler is not IActionTokenAccessGuard guard)
            return null;
        var requirement = await guard.GetAccessRequirementAsync(claim, cancellationToken);
        if (requirement == null)
            return null;
        return _unlock != null && _unlock.IsUnlocked(rawToken, proof) ? null : requirement;
    }

    private ObjectResult LockedResponse() =>
        StatusCode(StatusCodes.Status403Forbidden, new { Message = "Confirm your email address to open this document.", Locked = true });

    private async Task ReleaseAsync(IActionTokenHandler handler, string rawToken)
    {
        if (!handler.SingleUse) return;
        try
        {
            await _tokenService.ReleaseAsync(rawToken, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not release a link after its {Action} action failed", handler.Action);
        }
    }

    /// <summary>
    /// The tenant the token was issued for, when the request is not already in
    /// it. The token is the authority here: whatever tenant the host, an
    /// X-Tenant-ID header or a signed-in session implied would otherwise filter
    /// the handler's data to the wrong tenant and hide the document. The caller
    /// switches the context itself: the tenant is async-local, so a switch made
    /// inside this method would not survive its return.
    /// </summary>
    private async Task<(bool Found, Tenant? Switch)> FindTokenTenantAsync(ActionTokenClaim claim, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(claim.TenantId) || _tenantContext.TenantId == claim.TenantId)
            return (true, null);

        var tenant = await _tenantStore.GetByIdAsync(claim.TenantId, cancellationToken);
        if (tenant == null)
        {
            _logger.LogWarning("Action token for {Action} names tenant {TenantId}, which no longer exists", claim.Action, claim.TenantId);
            return (false, null);
        }
        return (true, tenant);
    }

    private IActionTokenHandler? FindHandler(string action)
        => _handlers.FirstOrDefault(h => string.Equals(h.Action, action, StringComparison.Ordinal));

    private ObjectResult Gone(string message) =>
        StatusCode(StatusCodes.Status410Gone, new { Message = message });
}

