using DnsClient;
using DnsClient.Protocol;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Services;

namespace Oluso.Core.Services;

public class DnsVerificationService : IDnsVerificationService
{
    private readonly ILookupClient _dnsClient;
    private readonly ILogger<DnsVerificationService> _logger;

    public DnsVerificationService(ILookupClient dnsClient, ILogger<DnsVerificationService> logger)
    {
        _dnsClient = dnsClient;
        _logger = logger;
    }

    public Task<DnsVerificationResult> VerifyDomainAsync(CustomDomainVerification verification, CancellationToken ct = default)
        => VerifyTxtRecordAsync(verification.TxtRecordName, verification.TxtRecordValue, ct);

    public async Task<DnsVerificationResult> VerifyTxtRecordAsync(string recordName, string expectedValue, CancellationToken ct = default)
    {
        try
        {
            // e.g. _oluso-verify.login.customer.com
            var result = await _dnsClient.QueryAsync(recordName, QueryType.TXT, cancellationToken: ct);

            if (result.HasError)
            {
                _logger.LogDebug("DNS query error for {Host}: {Error}", recordName, result.ErrorMessage);
                return DnsVerificationResult.TxtNotFound();
            }

            var txtRecords = result.Answers
                .OfType<TxtRecord>()
                .SelectMany(r => r.Text)
                .ToList();

            if (txtRecords.Count == 0)
            {
                _logger.LogDebug("No TXT records found for {Host}", recordName);
                return DnsVerificationResult.TxtNotFound();
            }

            var match = txtRecords.Any(r => r.Equals(expectedValue, StringComparison.OrdinalIgnoreCase));

            if (!match)
            {
                _logger.LogDebug("TXT records found for {Host} but none match. Found: [{Records}], Expected: {Expected}",
                    recordName, string.Join(", ", txtRecords), expectedValue);
                return DnsVerificationResult.Failed(
                    $"TXT record found but value doesn't match. Expected: {expectedValue}");
            }

            _logger.LogInformation("TXT record at {Host} verified", recordName);
            return DnsVerificationResult.Verified();
        }
        catch (DnsResponseException ex)
        {
            _logger.LogDebug(ex, "DNS response error for {Host}", recordName);
            return DnsVerificationResult.Failed($"DNS error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DNS lookup failed for {Host}", recordName);
            return DnsVerificationResult.Failed($"DNS lookup failed: {ex.Message}");
        }
    }
}
