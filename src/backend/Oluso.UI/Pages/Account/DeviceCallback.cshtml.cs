using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Oluso.Core.Protocols;

namespace Oluso.UI.Pages.Account;

/// <summary>
/// Where the sign-in started by the device verification page comes back to (<c>/device/callback</c>, the
/// coordinator's callback for the <c>device</c> protocol). It checks the sign-in completed and returns the
/// user to <c>/device</c> with the code they followed.
/// </summary>
public class DeviceCallbackModel : PageModel
{
    private readonly IAuthenticationCoordinator _coordinator;
    private readonly IProtocolStateStore _protocolStateStore;

    public DeviceCallbackModel(IAuthenticationCoordinator coordinator, IProtocolStateStore protocolStateStore)
    {
        _coordinator = coordinator;
        _protocolStateStore = protocolStateStore;
    }

    public async Task<IActionResult> OnGetAsync(
        [FromQuery(Name = "correlation_id")] string? correlationId,
        [FromQuery(Name = "journey_id")] string? journeyId)
    {
        if (string.IsNullOrEmpty(correlationId))
        {
            return Redirect("/device");
        }

        var state = await _protocolStateStore.GetAsync(correlationId, HttpContext.RequestAborted);
        if (state == null || state.ProtocolName != DeviceModel.ProtocolName)
        {
            return Redirect("/device");
        }

        var result = await _coordinator.HandleCallbackAsync(correlationId, journeyId, HttpContext.RequestAborted);
        await _protocolStateStore.RemoveAsync(correlationId, HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            return RedirectToPage("/Error", new { error = result.Error?.Code ?? "access_denied", message = result.Error?.Description });
        }

        var returnPath = state.Properties.TryGetValue(DeviceModel.ReturnPathProperty, out var path) ? path : null;
        return Redirect(IsDevicePath(returnPath) ? returnPath! : "/device");
    }

    /// <summary>Only this page's own path, so the callback can't be used to send the user elsewhere.</summary>
    private static bool IsDevicePath(string? path) =>
        path != null && (path == "/device" || path.StartsWith("/device?", StringComparison.Ordinal));
}
