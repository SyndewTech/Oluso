using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Oluso.Controllers;

/// <summary>
/// The shared secret standalone apps (Functions, marketplace, careers) send to the
/// platform's relay endpoints. Fails closed: with no key configured every call is
/// refused, so a missing setting never leaves a relay open.
/// </summary>
internal static class RelayKey
{
    public const string ConfigKey = "EventRelay:ApiKey";
    public const string Header = "X-Event-Relay-Key";

    /// <returns>null when the caller sent the configured key, otherwise the response to return.</returns>
    public static IActionResult? Check(ControllerBase controller, IConfiguration configuration, ILogger logger)
    {
        var expected = configuration[ConfigKey];
        if (string.IsNullOrWhiteSpace(expected))
        {
            logger.LogError("Relay call refused: {ConfigKey} is not configured", ConfigKey);
            return controller.StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Relay key is not configured" });
        }

        var provided = controller.Request.Headers[Header].FirstOrDefault();
        if (string.IsNullOrEmpty(provided)
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected)))
        {
            logger.LogWarning("Relay call refused: missing or wrong {Header}", Header);
            return controller.Unauthorized();
        }

        return null;
    }
}
