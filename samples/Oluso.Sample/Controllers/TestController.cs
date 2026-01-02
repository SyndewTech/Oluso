using System.DirectoryServices.Protocols;
using System.Net;
using System.Text;
using System.Xml;
using Microsoft.AspNetCore.Mvc;

namespace Oluso.Sample.Controllers;

/// <summary>
/// Test API endpoints for LDAP and SAML functionality.
/// Static HTML/JS test pages are served from wwwroot/test/
/// </summary>
[ApiController]
[Route("test")]
public class TestController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<TestController> _logger;

    public TestController(IConfiguration configuration, ILogger<TestController> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    #region LDAP API Endpoints

    /// <summary>
    /// Get LDAP configuration for the test page
    /// </summary>
    [HttpGet("ldap/config")]
    public IActionResult GetLdapConfig()
    {
        var baseDn = _configuration.GetValue<string>("Oluso:LdapServer:BaseDn", "dc=oluso,dc=local");
        return Ok(new
        {
            host = _configuration.GetValue<string>("Oluso:LdapServer:Host", "localhost"),
            port = _configuration.GetValue<int>("Oluso:LdapServer:Port", 10389),
            baseDn = baseDn,
            adminDn = _configuration.GetValue<string>("Oluso:LdapServer:AdminDn", $"cn=admin,{baseDn}")
        });
    }

    /// <summary>
    /// Test LDAP bind (authentication)
    /// </summary>
    [HttpPost("ldap/bind")]
    public IActionResult TestLdapBind([FromBody] LdapBindRequest request)
    {
        var port = _configuration.GetValue<int>("Oluso:LdapServer:Port", 10389);
        var baseDn = _configuration.GetValue<string>("Oluso:LdapServer:BaseDn", "dc=oluso,dc=local");
        var userOu = _configuration.GetValue<string>("Oluso:LdapServer:UserOu", "users");

        try
        {
            // Construct user DN
            var userDn = $"uid={request.Username},ou={userOu},{baseDn}";

            var ldapHost = _configuration.GetValue<string>("Oluso:LdapServer:Host", "localhost")!;
            using var connection = new LdapConnection(new LdapDirectoryIdentifier(ldapHost, port));
            connection.SessionOptions.ProtocolVersion = 3;
            connection.AuthType = AuthType.Basic;
            connection.Credential = new NetworkCredential(userDn, request.Password);

            connection.Bind();

            _logger.LogInformation("LDAP bind successful for user {Username}", request.Username);

            return Ok(new { message = "Bind successful!", dn = userDn });
        }
        catch (LdapException ex)
        {
            _logger.LogWarning("LDAP bind failed for user {Username}: {Error}", request.Username, ex.Message);
            return BadRequest(new { error = $"Bind failed: {ex.Message}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LDAP test error");
            return StatusCode(500, new { error = $"Error: {ex.Message}" });
        }
    }

    /// <summary>
    /// Test LDAP search
    /// </summary>
    [HttpPost("ldap/search")]
    public IActionResult TestLdapSearch([FromBody] LdapSearchRequest request)
    {
        var port = _configuration.GetValue<int>("Oluso:LdapServer:Port", 10389);
        var baseDn = _configuration.GetValue<string>("Oluso:LdapServer:BaseDn", "dc=oluso,dc=local");
        var adminDn = _configuration.GetValue<string>("Oluso:LdapServer:AdminDn", $"cn=admin,{baseDn}");
        var adminPassword = _configuration.GetValue<string>("Oluso:LdapServer:AdminPassword", "admin123");

        try
        {
            var ldapHost = _configuration.GetValue<string>("Oluso:LdapServer:Host", "localhost")!;
            using var connection = new LdapConnection(new LdapDirectoryIdentifier(ldapHost, port));
            connection.SessionOptions.ProtocolVersion = 3;
            connection.AuthType = AuthType.Basic;
            connection.Credential = new NetworkCredential(adminDn, adminPassword);
            connection.Bind();

            var scope = request.Scope?.ToLower() switch
            {
                "base" => SearchScope.Base,
                "one" => SearchScope.OneLevel,
                _ => SearchScope.Subtree
            };

            var searchRequest = new SearchRequest(baseDn, request.Filter ?? "(objectClass=*)", scope);
            var response = (SearchResponse)connection.SendRequest(searchRequest);

            var entries = new List<object>();
            foreach (SearchResultEntry entry in response.Entries)
            {
                var attrs = new Dictionary<string, object?>();
                foreach (DirectoryAttribute attr in entry.Attributes.Values)
                {
                    attrs[attr.Name] = attr.Count == 1
                        ? attr[0]?.ToString()
                        : attr.GetValues(typeof(string));
                }
                entries.Add(new { dn = entry.DistinguishedName, attributes = attrs });
            }

            return Ok(new { count = entries.Count, entries });
        }
        catch (LdapException ex)
        {
            return BadRequest(new { error = $"Search failed: {ex.Message}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LDAP search error");
            return StatusCode(500, new { error = $"Error: {ex.Message}" });
        }
    }

    #endregion

    #region SAML API Endpoints

    /// <summary>
    /// Get SAML configuration for the test page
    /// </summary>
    [HttpGet("saml/config")]
    public IActionResult GetSamlConfig()
    {
        var baseUrl = _configuration.GetValue<string>("Oluso:Urls:BaseUrl", "http://localhost:5050")!;
        return Ok(new
        {
            idpEntityId = _configuration.GetValue<string>("Oluso:Saml:IdentityProvider:EntityId") ?? baseUrl,
            ssoEndpoint = $"{baseUrl}/saml/idp/sso",
            acsUrl = $"{baseUrl}/test/saml/acs",
            spEntityId = $"{baseUrl}/test/saml"
        });
    }

    /// <summary>
    /// SAML Assertion Consumer Service (receives SAML Response from IdP)
    /// </summary>
    [HttpPost("saml/acs")]
    [Produces("text/html")]
    public IActionResult SamlAcs([FromForm] string SAMLResponse)
    {
        try
        {
            if (string.IsNullOrEmpty(SAMLResponse))
            {
                return Content("<html><body><h1>Error</h1><p>No SAMLResponse received</p></body></html>", "text/html");
            }

            _logger.LogDebug("Received SAMLResponse, length: {Length}, first 100 chars: {Preview}",
                SAMLResponse.Length,
                SAMLResponse.Length > 100 ? SAMLResponse.Substring(0, 100) : SAMLResponse);

            // The SAMLResponse should be Base64 encoded
            // Clean up any whitespace that might have been introduced
            var cleanedResponse = SAMLResponse.Replace(" ", "+").Replace("\n", "").Replace("\r", "");

            // Decode the SAML Response
            var decodedResponse = Encoding.UTF8.GetString(Convert.FromBase64String(cleanedResponse));

            // Parse XML to extract assertion details
            var doc = new XmlDocument();
            doc.LoadXml(decodedResponse);

            var nsManager = new XmlNamespaceManager(doc.NameTable);
            nsManager.AddNamespace("saml", "urn:oasis:names:tc:SAML:2.0:assertion");
            nsManager.AddNamespace("samlp", "urn:oasis:names:tc:SAML:2.0:protocol");

            var nameId = doc.SelectSingleNode("//saml:NameID", nsManager)?.InnerText ?? "N/A";
            var issuer = doc.SelectSingleNode("//saml:Issuer", nsManager)?.InnerText ?? "N/A";
            var statusCode = doc.SelectSingleNode("//samlp:StatusCode/@Value", nsManager)?.Value ?? "N/A";

            // Extract attributes
            var attributes = new Dictionary<string, string>();
            var attrNodes = doc.SelectNodes("//saml:Attribute", nsManager);
            if (attrNodes != null)
            {
                foreach (XmlNode attr in attrNodes)
                {
                    var name = attr.Attributes?["Name"]?.Value ?? "unknown";
                    var value = attr.SelectSingleNode("saml:AttributeValue", nsManager)?.InnerText ?? "";
                    attributes[name] = value;
                }
            }

            // Redirect to the static results page with data in query params
            // For simplicity, we return a simple HTML page showing success
            var attributeRows = string.Join("", attributes.Select(a =>
                $"<tr><td>{Escape(a.Key)}</td><td>{Escape(a.Value)}</td></tr>"));

            var formattedXml = Escape(FormatXml(decodedResponse));

            var html = $@"<!DOCTYPE html>
<html>
<head>
    <title>SAML Response Received</title>
    <style>
        body {{ font-family: system-ui, -apple-system, sans-serif; max-width: 900px; margin: 40px auto; padding: 20px; }}
        .success {{ background: #d4edda; border: 1px solid #c3e6cb; padding: 20px; border-radius: 8px; margin: 20px 0; }}
        pre {{ background: #1e1e1e; color: #d4d4d4; padding: 15px; border-radius: 4px; overflow-x: auto; white-space: pre-wrap; max-height: 400px; }}
        table {{ width: 100%; border-collapse: collapse; margin: 10px 0; }}
        th, td {{ text-align: left; padding: 10px; border-bottom: 1px solid #ddd; }}
        th {{ background: #f8f9fa; }}
        code {{ background: #e9ecef; padding: 2px 6px; border-radius: 3px; }}
        button {{ background: #0066cc; color: white; padding: 10px 20px; border: none; border-radius: 4px; cursor: pointer; }}
    </style>
</head>
<body>
    <h1>SAML Response Received</h1>

    <div class=""success"">
        <h3>Authentication Successful!</h3>
        <p><strong>NameID:</strong> <code>{Escape(nameId)}</code></p>
        <p><strong>Issuer:</strong> <code>{Escape(issuer)}</code></p>
        <p><strong>Status:</strong> <code>{Escape(statusCode)}</code></p>
    </div>

    <h3>User Attributes</h3>
    <table>
        <tr><th>Attribute</th><th>Value</th></tr>
        {attributeRows}
    </table>

    <h3>Raw SAML Response (decoded)</h3>
    <pre>{formattedXml}</pre>

    <p><button onclick=""window.location.href='/test/saml.html'"">Try Again</button></p>
</body>
</html>";

            return Content(html, "text/html");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing SAML response");

            var html = $@"<!DOCTYPE html>
<html>
<head>
    <title>SAML Error</title>
    <style>
        body {{ font-family: system-ui; max-width: 800px; margin: 40px auto; padding: 20px; }}
        .error {{ background: #f8d7da; border: 1px solid #f5c6cb; padding: 20px; border-radius: 8px; }}
    </style>
</head>
<body>
    <h1>SAML Error</h1>
    <div class=""error"">
        <p><strong>Error processing SAML response:</strong></p>
        <pre>{Escape(ex.Message)}</pre>
    </div>
    <p><a href=""/test/saml.html"">Try Again</a></p>
</body>
</html>";

            return Content(html, "text/html");
        }
    }

    #endregion

    #region OIDC API Endpoints

    /// <summary>
    /// Get OIDC configuration for the test page
    /// </summary>
    [HttpGet("oidc/config")]
    public IActionResult GetOidcConfig()
    {
        var baseUrl = _configuration.GetValue<string>("Oluso:Urls:BaseUrl", "http://localhost:5050")!;
        var testClientUrl = _configuration.GetValue<string>("Oluso:Urls:TestClientUrl", "http://localhost:5100")!;
        return Ok(new
        {
            issuer = _configuration.GetValue<string>("Oluso:IssuerUri") ?? baseUrl,
            clientId = "test-client",
            redirectUri = $"{testClientUrl}/signin-oidc"
        });
    }

    #endregion

    #region Helpers

    private static string Escape(string? value)
    {
        return System.Security.SecurityElement.Escape(value) ?? "";
    }

    private static string FormatXml(string xml)
    {
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            using var sw = new StringWriter();
            using var writer = new XmlTextWriter(sw) { Formatting = Formatting.Indented };
            doc.WriteTo(writer);
            return sw.ToString();
        }
        catch
        {
            return xml;
        }
    }

    #endregion
}

#region Request Models

public class LdapBindRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public class LdapSearchRequest
{
    public string? Filter { get; set; }
    public string? Scope { get; set; }
}

#endregion
