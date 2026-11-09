using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Oluso.Keys;

/// <summary>
/// Reads the <c>kid</c> header of the access token (Bearer or DPoP) on an incoming request
/// without validating it, so the validation key set can be refreshed before
/// the handler rejects a token signed by a key this instance has not seen yet.
/// </summary>
public static class TokenKeyIdReader
{
    private static readonly JsonWebTokenHandler Handler = new();

    public static string? Read(MessageReceivedContext context)
    {
        var token = context.Token;
        if (string.IsNullOrEmpty(token))
        {
            token = Protocols.DPoP.PresentedAccessToken.FromAuthorizationHeader(context.Request.Headers.Authorization.ToString())?.Token;
        }

        if (string.IsNullOrEmpty(token) || !Handler.CanReadToken(token))
        {
            return null;
        }

        try
        {
            var kid = Handler.ReadJsonWebToken(token).Kid;
            return string.IsNullOrEmpty(kid) ? null : kid;
        }
        catch
        {
            return null;
        }
    }
}
