using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oluso.Core.Services;

namespace Oluso.Messaging;

/// <summary>
/// Email sender implementation using SendGrid
/// </summary>
public class SendGridEmailSender : IEmailSender
{
    private readonly HttpClient _httpClient;
    private readonly EmailOptions _options;
    private readonly ILogger<SendGridEmailSender> _logger;

    private const string SendGridApiUrl = "https://api.sendgrid.com/v3/mail/send";

    public SendGridEmailSender(
        HttpClient httpClient,
        IOptions<EmailOptions> options,
        ILogger<SendGridEmailSender> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<EmailResult> SendAsync(
        string email,
        string subject,
        string htmlMessage,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_options.SendGrid.ApiKey))
        {
            _logger.LogError("SendGrid API key is not configured");
            return EmailResult.Failed("SendGrid API key is not configured");
        }

        try
        {
            var payload = new SendGridPayload
            {
                Personalizations = new[]
                {
                    new SendGridPersonalization
                    {
                        To = new[] { new SendGridEmail { Email = email } }
                    }
                },
                From = new SendGridEmail
                {
                    Email = _options.FromAddress ?? "noreply@example.com",
                    Name = _options.FromName
                },
                Subject = subject,
                Content = new[]
                {
                    new SendGridContent { Type = "text/html", Value = htmlMessage }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, SendGridApiUrl);
            request.Headers.Add("Authorization", $"Bearer {_options.SendGrid.ApiKey}");
            request.Content = JsonContent.Create(payload, options: new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                // SendGrid returns message ID in header
                var messageId = response.Headers.TryGetValues("X-Message-Id", out var values)
                    ? values.FirstOrDefault()
                    : null;

                _logger.LogInformation("Email sent successfully to {To} via SendGrid, MessageId: {MessageId}", email, messageId);
                return EmailResult.Succeeded(messageId);
            }

            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("SendGrid API error: {StatusCode} - {Error}", response.StatusCode, errorContent);

            return EmailResult.Failed($"SendGrid API error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending email via SendGrid to {To}", email);
            return EmailResult.Failed(ex.Message);
        }
    }

    public async Task<EmailResult> SendTemplateAsync(
        string email,
        string templateName,
        IDictionary<string, object> templateData,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_options.SendGrid.ApiKey))
        {
            _logger.LogError("SendGrid API key is not configured");
            return EmailResult.Failed("SendGrid API key is not configured");
        }

        try
        {
            var payload = new SendGridTemplatePayload
            {
                Personalizations = new[]
                {
                    new SendGridTemplatePersonalization
                    {
                        To = new[] { new SendGridEmail { Email = email } },
                        DynamicTemplateData = templateData
                    }
                },
                From = new SendGridEmail
                {
                    Email = _options.FromAddress ?? "noreply@example.com",
                    Name = _options.FromName
                },
                TemplateId = templateName
            };

            var request = new HttpRequestMessage(HttpMethod.Post, SendGridApiUrl);
            request.Headers.Add("Authorization", $"Bearer {_options.SendGrid.ApiKey}");
            request.Content = JsonContent.Create(payload, options: new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var messageId = response.Headers.TryGetValues("X-Message-Id", out var values)
                    ? values.FirstOrDefault()
                    : null;

                _logger.LogInformation("Templated email sent successfully to {To} via SendGrid, MessageId: {MessageId}", email, messageId);
                return EmailResult.Succeeded(messageId);
            }

            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("SendGrid API error: {StatusCode} - {Error}", response.StatusCode, errorContent);

            return EmailResult.Failed($"SendGrid API error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending templated email via SendGrid to {To}", email);
            return EmailResult.Failed(ex.Message);
        }
    }

    #region SendGrid DTOs

    private class SendGridPayload
    {
        public SendGridPersonalization[] Personalizations { get; set; } = Array.Empty<SendGridPersonalization>();
        public SendGridEmail From { get; set; } = null!;
        public string Subject { get; set; } = null!;
        public SendGridContent[] Content { get; set; } = Array.Empty<SendGridContent>();
    }

    private class SendGridTemplatePayload
    {
        public SendGridTemplatePersonalization[] Personalizations { get; set; } = Array.Empty<SendGridTemplatePersonalization>();
        public SendGridEmail From { get; set; } = null!;
        public string TemplateId { get; set; } = null!;
    }

    private class SendGridPersonalization
    {
        public SendGridEmail[] To { get; set; } = Array.Empty<SendGridEmail>();
    }

    private class SendGridTemplatePersonalization
    {
        public SendGridEmail[] To { get; set; } = Array.Empty<SendGridEmail>();
        public IDictionary<string, object>? DynamicTemplateData { get; set; }
    }

    private class SendGridEmail
    {
        public string Email { get; set; } = null!;
        public string? Name { get; set; }
    }

    private class SendGridContent
    {
        public string Type { get; set; } = null!;
        public string Value { get; set; } = null!;
    }

    #endregion
}
