using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

var builder = WebApplication.CreateBuilder(args);

// Configure OIDC authentication against Oluso.Sample
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.Cookie.Name = "OlusoTestClient";
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
})
.AddOpenIdConnect(options =>
{
    // Oluso.Sample server (adjust port if needed)
    options.Authority = "http://localhost:5050";
    options.RequireHttpsMetadata = false; // Dev only

    // Nonce cookie settings for development (important for cross-origin flows)
    options.NonceCookie.SameSite = SameSiteMode.Lax;
    options.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.CorrelationCookie.SameSite = SameSiteMode.Lax;
    options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

    // Token validation parameters
    options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = "http://localhost:5050", // Default tenant uses base URL (no /default path)
        ValidateAudience = true,
        ValidAudience = "test-client",
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true
    };

    // Client credentials (must match what's seeded in Oluso.Sample)
    options.ClientId = "test-client";
    options.ClientSecret = "test-secret";

    // OIDC settings
    options.ResponseType = OpenIdConnectResponseType.Code;
    options.SaveTokens = true;
    // Enable UserInfo endpoint call - server now supports bearer token auth
    options.GetClaimsFromUserInfoEndpoint = true;

    // Scopes
    options.Scope.Clear();
    options.Scope.Add("openid");
    options.Scope.Add("profile");
    options.Scope.Add("email");

    // Callback paths
    options.CallbackPath = "/signin-oidc";
    options.SignedOutCallbackPath = "/signout-callback-oidc";

    // Events for debugging
    options.Events = new OpenIdConnectEvents
    {
        OnAuthenticationFailed = context =>
        {
            Console.WriteLine($"Authentication failed: {context.Exception.Message}");
            Console.WriteLine($"Exception type: {context.Exception.GetType().FullName}");
            Console.WriteLine($"Stack trace:\n{context.Exception.StackTrace}");
            if (context.Exception.InnerException != null)
            {
                Console.WriteLine($"Inner exception: {context.Exception.InnerException.Message}");
                Console.WriteLine($"Inner stack trace:\n{context.Exception.InnerException.StackTrace}");
            }
            return Task.CompletedTask;
        },
        OnTokenValidated = context =>
        {
            Console.WriteLine($"Token validated for: {context.Principal?.Identity?.Name}");
            return Task.CompletedTask;
        },
        OnRedirectToIdentityProvider = context =>
        {
            Console.WriteLine($"Redirecting to: {context.ProtocolMessage.CreateAuthenticationRequestUrl()}");
            return Task.CompletedTask;
        },
        OnTokenResponseReceived = context =>
        {
            Console.WriteLine($"Token response received:");
            Console.WriteLine($"  AccessToken length: {context.TokenEndpointResponse?.AccessToken?.Length ?? 0}");
            Console.WriteLine($"  IdToken length: {context.TokenEndpointResponse?.IdToken?.Length ?? 0}");
            var idToken = context.TokenEndpointResponse?.IdToken;
            var dotCount = idToken?.Count(c => c == '.') ?? 0;
            Console.WriteLine($"  IdToken dots: {dotCount}");
            Console.WriteLine($"  IdToken first 200 chars: {(idToken != null ? idToken.Substring(0, Math.Min(200, idToken.Length)) : "null")}");
            if (idToken != null && idToken.Length > 200)
            {
                Console.WriteLine($"  IdToken last 100 chars: ...{idToken.Substring(idToken.Length - 100)}");
            }
            Console.WriteLine($"  Error: {context.TokenEndpointResponse?.Error ?? "none"}");

            // Also check ProtocolMessage.IdToken - this is what the handler uses for validation
            var pmIdToken = context.ProtocolMessage?.IdToken;
            Console.WriteLine($"  ProtocolMessage.IdToken: {(pmIdToken != null ? $"length={pmIdToken.Length}, dots={pmIdToken.Count(c => c == '.')}" : "null")}");

            // The OpenIdConnect handler expects the id_token to be in ProtocolMessage for validation
            // In authorization code flow, we need to copy it from the token response
            if (context.TokenEndpointResponse?.IdToken != null && context.ProtocolMessage != null)
            {
                if (string.IsNullOrEmpty(context.ProtocolMessage.IdToken))
                {
                    Console.WriteLine("  Copying IdToken from TokenEndpointResponse to ProtocolMessage");
                    context.ProtocolMessage.IdToken = context.TokenEndpointResponse.IdToken;
                }
            }

            return Task.CompletedTask;
        },
        OnMessageReceived = context =>
        {
            if (context.ProtocolMessage != null)
            {
                var msg = context.ProtocolMessage;
                Console.WriteLine($"Message received:");
                Console.WriteLine($"  Code: {(msg.Code != null ? msg.Code.Substring(0, Math.Min(10, msg.Code.Length)) : "null")}...");
                Console.WriteLine($"  IdToken in message: {(msg.IdToken != null ? $"length={msg.IdToken.Length}, first50={msg.IdToken.Substring(0, Math.Min(50, msg.IdToken.Length))}" : "null")}");
                Console.WriteLine($"  AccessToken in message: {(msg.AccessToken != null ? $"length={msg.AccessToken.Length}" : "null")}");
                Console.WriteLine($"  Error: {msg.Error ?? "none"}");
            }
            return Task.CompletedTask;
        },
        OnAuthorizationCodeReceived = context =>
        {
            Console.WriteLine($"Authorization code received: {context.TokenEndpointRequest?.Code?[..Math.Min(10, context.TokenEndpointRequest?.Code?.Length ?? 0)] ?? "null"}...");
            return Task.CompletedTask;
        },
        OnUserInformationReceived = context =>
        {
            Console.WriteLine($"User info received: {context.User?.RootElement.ToString()?[..Math.Min(200, context.User?.RootElement.ToString()?.Length ?? 0)] ?? "null"}");
            return Task.CompletedTask;
        },
        OnRemoteFailure = context =>
        {
            Console.WriteLine($"Remote failure: {context.Failure?.Message}");
            Console.WriteLine($"Remote failure type: {context.Failure?.GetType().FullName}");
            Console.WriteLine($"Remote failure stack:\n{context.Failure?.StackTrace}");
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Public home page
app.MapGet("/", () => Results.Content(@"
<!DOCTYPE html>
<html>
<head>
    <title>Oluso Test Client</title>
    <style>
        body { font-family: system-ui, sans-serif; max-width: 600px; margin: 50px auto; padding: 20px; }
        a { display: inline-block; margin: 10px 0; padding: 10px 20px; background: #0066cc; color: white; text-decoration: none; border-radius: 5px; }
        a:hover { background: #0055aa; }
        pre { background: #f5f5f5; padding: 15px; border-radius: 5px; overflow-x: auto; }
    </style>
</head>
<body>
    <h1>Oluso Test Client</h1>
    <p>This is a simple OIDC client to test authentication with Oluso.Sample.</p>
    <a href=""/login"">Login with Oluso</a>
    <a href=""/protected"">Protected Page</a>
    <a href=""/claims"">View Claims</a>
</body>
</html>", "text/html"));

// Login endpoint
app.MapGet("/login", async (HttpContext context) =>
{
    await context.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme, new AuthenticationProperties
    {
        RedirectUri = "/claims"
    });
});

// Logout endpoint
app.MapGet("/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    await context.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme);
});

// Protected page - requires authentication
app.MapGet("/protected", (HttpContext context) =>
{
    if (!context.User.Identity?.IsAuthenticated ?? true)
    {
        return Results.Content(@"
<!DOCTYPE html>
<html>
<head><title>Not Authenticated</title></head>
<body>
    <h1>Not Authenticated</h1>
    <p>You need to <a href=""/login"">login</a> first.</p>
</body>
</html>", "text/html");
    }

    var userName = context.User.Identity?.Name ?? "Unknown";
    return Results.Content($@"
<!DOCTYPE html>
<html>
<head>
    <title>Protected Page</title>
    <style>
        body {{ font-family: system-ui, sans-serif; max-width: 600px; margin: 50px auto; padding: 20px; }}
        a {{ color: #0066cc; }}
    </style>
</head>
<body>
    <h1>Protected Page</h1>
    <p>Welcome, <strong>{userName}</strong>!</p>
    <p>You are authenticated.</p>
    <p><a href=""/claims"">View all claims</a> | <a href=""/logout"">Logout</a></p>
</body>
</html>", "text/html");
});

// Claims page - shows all user claims
app.MapGet("/claims", async (HttpContext context) =>
{
    if (!context.User.Identity?.IsAuthenticated ?? true)
    {
        return Results.Content(@"
<!DOCTYPE html>
<html>
<head><title>Not Authenticated</title></head>
<body>
    <h1>Not Authenticated</h1>
    <p>You need to <a href=""/login"">login</a> first.</p>
</body>
</html>", "text/html");
    }

    var claims = context.User.Claims.Select(c => $"<tr><td><strong>{c.Type}</strong></td><td>{c.Value}</td></tr>");
    var claimsTable = string.Join("\n", claims);

    // Get tokens
    var accessToken = await context.GetTokenAsync("access_token") ?? "Not available";
    var idToken = await context.GetTokenAsync("id_token") ?? "Not available";
    var refreshToken = await context.GetTokenAsync("refresh_token") ?? "Not available";

    return Results.Content($@"
<!DOCTYPE html>
<html>
<head>
    <title>User Claims</title>
    <style>
        body {{ font-family: system-ui, sans-serif; max-width: 800px; margin: 50px auto; padding: 20px; }}
        table {{ border-collapse: collapse; width: 100%; margin: 20px 0; }}
        th, td {{ border: 1px solid #ddd; padding: 10px; text-align: left; }}
        th {{ background: #f5f5f5; }}
        pre {{ background: #f5f5f5; padding: 10px; border-radius: 5px; overflow-x: auto; font-size: 12px; word-break: break-all; }}
        a {{ color: #0066cc; }}
        h2 {{ margin-top: 30px; }}
    </style>
</head>
<body>
    <h1>User Claims</h1>
    <p><a href=""/"">Home</a> | <a href=""/logout"">Logout</a></p>

    <h2>Claims</h2>
    <table>
        <tr><th>Type</th><th>Value</th></tr>
        {claimsTable}
    </table>

    <h2>Tokens</h2>
    <h3>Access Token</h3>
    <pre>{accessToken}</pre>

    <h3>ID Token</h3>
    <pre>{idToken}</pre>

    <h3>Refresh Token</h3>
    <pre>{refreshToken}</pre>
</body>
</html>", "text/html");
});

Console.WriteLine("Test Client running at http://localhost:5100");
Console.WriteLine("Oluso.Sample should be running at http://localhost:5050");
Console.WriteLine();
Console.WriteLine("1. Start Oluso.Sample: cd samples/Oluso.Sample && dotnet run --urls=http://localhost:5050");
Console.WriteLine("2. Start this client: cd samples/Oluso.TestClient && dotnet run --urls=http://localhost:5100");
Console.WriteLine("3. Open http://localhost:5100 and click 'Login with Oluso'");
Console.WriteLine();
Console.WriteLine("Test credentials: testuser@example.com / Password123!");

app.Run();
