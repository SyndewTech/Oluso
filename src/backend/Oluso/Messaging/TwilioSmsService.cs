using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oluso.Core.Services;

namespace Oluso.Messaging;

/// <summary>
/// SMS sender implementation using Twilio
/// </summary>
public class TwilioSmsSender : ISmsSender
{
    private readonly HttpClient _httpClient;
    private readonly SmsOptions _options;
    private readonly ILogger<TwilioSmsSender> _logger;

    private const string TwilioApiBaseUrl = "https://api.twilio.com/2010-04-01";

    public TwilioSmsSender(
        HttpClient httpClient,
        IOptions<SmsOptions> options,
        ILogger<TwilioSmsSender> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SmsResult> SendAsync(
        string phoneNumber,
        string message,
        CancellationToken cancellationToken = default)
    {
        var twilio = _options.Twilio;

        if (string.IsNullOrEmpty(twilio.AccountSid) || string.IsNullOrEmpty(twilio.AuthToken))
        {
            _logger.LogError("Twilio credentials are not configured");
            return SmsResult.Failed("Twilio credentials are not configured");
        }

        if (string.IsNullOrEmpty(twilio.FromNumber) && string.IsNullOrEmpty(_options.FromNumber))
        {
            _logger.LogError("Twilio from number is not configured");
            return SmsResult.Failed("Twilio from number is not configured");
        }

        try
        {
            var normalizedPhone = NormalizePhoneNumber(phoneNumber);
            var fromNumber = twilio.FromNumber ?? _options.FromNumber!;

            var apiUrl = $"{TwilioApiBaseUrl}/Accounts/{twilio.AccountSid}/Messages.json";

            var formContent = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("To", normalizedPhone),
                new KeyValuePair<string, string>("From", fromNumber),
                new KeyValuePair<string, string>("Body", message)
            });

            var request = new HttpRequestMessage(HttpMethod.Post, apiUrl);
            var authBytes = Encoding.UTF8.GetBytes($"{twilio.AccountSid}:{twilio.AuthToken}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
            request.Content = formContent;

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<TwilioMessageResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                });

                _logger.LogInformation(
                    "SMS sent successfully to {Phone} via Twilio, Sid: {Sid}, Status: {Status}",
                    MaskPhone(normalizedPhone),
                    result?.Sid,
                    result?.Status);

                return SmsResult.Succeeded(result?.Sid);
            }

            var error = JsonSerializer.Deserialize<TwilioErrorResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            });

            _logger.LogError(
                "Twilio API error: {StatusCode} - {Code}: {Message}",
                response.StatusCode,
                error?.Code,
                error?.Message);

            return SmsResult.Failed(error?.Message ?? $"Twilio API error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending SMS via Twilio to {Phone}", MaskPhone(phoneNumber));
            return SmsResult.Failed(ex.Message);
        }
    }

    private static string NormalizePhoneNumber(string phoneNumber)
    {
        // Remove common formatting characters
        var normalized = new string(phoneNumber.Where(c => char.IsDigit(c) || c == '+').ToArray());

        // Ensure it starts with +
        if (!normalized.StartsWith('+'))
        {
            normalized = "+" + normalized;
        }

        return normalized;
    }

    private static string MaskPhone(string phone)
    {
        if (string.IsNullOrEmpty(phone) || phone.Length < 6)
            return "***";

        return phone[..4] + "****" + phone[^2..];
    }

    #region Twilio DTOs

    private class TwilioMessageResponse
    {
        public string? Sid { get; set; }
        public string? Status { get; set; }
        public string? To { get; set; }
        public string? From { get; set; }
        public string? Body { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
    }

    private class TwilioErrorResponse
    {
        public int? Code { get; set; }
        public string? Message { get; set; }
        public string? MoreInfo { get; set; }
        public int? Status { get; set; }
    }

    #endregion
}
