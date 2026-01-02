using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oluso.Core.Services;

namespace Oluso.Messaging;

/// <summary>
/// SMS sender implementation using Infobip
/// </summary>
public class InfobipSmsSender : ISmsSender
{
    private readonly HttpClient _httpClient;
    private readonly SmsOptions _options;
    private readonly ILogger<InfobipSmsSender> _logger;

    public InfobipSmsSender(
        HttpClient httpClient,
        IOptions<SmsOptions> options,
        ILogger<InfobipSmsSender> logger)
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
        var infobip = _options.Infobip;

        if (string.IsNullOrEmpty(infobip.ApiKey))
        {
            _logger.LogError("Infobip API key is not configured");
            return SmsResult.Failed("Infobip API key is not configured");
        }

        try
        {
            var normalizedPhone = NormalizePhoneNumber(phoneNumber);
            var senderId = infobip.SenderId ?? _options.FromNumber ?? "Oluso";

            var payload = new InfobipSmsPayload
            {
                Messages = new[]
                {
                    new InfobipSmsMessage
                    {
                        From = senderId,
                        Destinations = new[]
                        {
                            new InfobipDestination { To = normalizedPhone }
                        },
                        Text = message
                    }
                }
            };

            var apiUrl = $"{infobip.BaseUrl.TrimEnd('/')}/sms/2/text/advanced";
            var request = new HttpRequestMessage(HttpMethod.Post, apiUrl);
            request.Headers.Add("Authorization", $"App {infobip.ApiKey}");
            request.Headers.Add("Accept", "application/json");
            request.Content = JsonContent.Create(payload, options: new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<InfobipSmsResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                var messageResult = result?.Messages?.FirstOrDefault();
                if (messageResult != null)
                {
                    var status = messageResult.Status;
                    if (status.GroupId <= 2) // Pending or Undeliverable
                    {
                        _logger.LogInformation(
                            "SMS sent successfully to {Phone} via Infobip, MessageId: {MessageId}, Status: {Status}",
                            MaskPhone(normalizedPhone),
                            messageResult.MessageId,
                            status.Name);

                        return SmsResult.Succeeded(messageResult.MessageId);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "SMS delivery issue to {Phone}: {Status} - {Description}",
                            MaskPhone(normalizedPhone),
                            status.Name,
                            status.Description);

                        return SmsResult.Failed(status.Description ?? status.Name);
                    }
                }

                return SmsResult.Succeeded();
            }

            _logger.LogError("Infobip API error: {StatusCode} - {Error}", response.StatusCode, responseContent);
            return SmsResult.Failed($"Infobip API error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending SMS via Infobip to {Phone}", MaskPhone(phoneNumber));
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

    #region Infobip DTOs

    private class InfobipSmsPayload
    {
        public InfobipSmsMessage[] Messages { get; set; } = Array.Empty<InfobipSmsMessage>();
    }

    private class InfobipSmsMessage
    {
        public string From { get; set; } = null!;
        public InfobipDestination[] Destinations { get; set; } = Array.Empty<InfobipDestination>();
        public string Text { get; set; } = null!;
    }

    private class InfobipDestination
    {
        public string To { get; set; } = null!;
    }

    private class InfobipSmsResponse
    {
        public string? BulkId { get; set; }
        public InfobipMessageResult[]? Messages { get; set; }
    }

    private class InfobipMessageResult
    {
        public string? MessageId { get; set; }
        public InfobipStatus Status { get; set; } = null!;
        public string? To { get; set; }
    }

    private class InfobipStatus
    {
        public int GroupId { get; set; }
        public string GroupName { get; set; } = null!;
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
    }

    #endregion
}
