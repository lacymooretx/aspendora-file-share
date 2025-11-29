using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace AspendoraFileShare.Services;

public class EmailService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailService> _logger;
    private readonly string _apiKey;
    private readonly string _apiUrl;

    public EmailService(HttpClient httpClient, IConfiguration configuration, ILogger<EmailService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        _apiKey = configuration["Smtp2Go:ApiKey"]!;
        _apiUrl = configuration["Smtp2Go:ApiUrl"]!;
    }

    public async Task SendShareEmailAsync(
        string recipientEmail,
        string recipientName,
        string senderName,
        string senderEmail,
        string shareUrl,
        string? customMessage,
        List<(string FileName, long FileSize)> files)
    {
        _logger.LogInformation("SendShareEmailAsync called: to={RecipientEmail}, from={SenderEmail}, url={ShareUrl}, files={FileCount}",
            recipientEmail, senderEmail, shareUrl, files.Count);
        var fileListHtml = string.Join("", files.Select(f =>
            $"<li style='padding: 8px 0; border-bottom: 1px solid #e5e7eb;'><strong>{f.FileName}</strong> - {FormatBytes(f.FileSize)}</li>"));

        var messageHtml = "";
        if (!string.IsNullOrWhiteSpace(customMessage))
        {
            messageHtml = $@"
                <div style='margin: 24px 0; padding: 16px; border-left: 4px solid #660000; background-color: #f9fafb;'>
                    <p style='margin: 0; color: #374151; font-style: italic;'>{System.Net.WebUtility.HtmlEncode(customMessage)}</p>
                </div>";
        }

        // Use hosted logo URL instead of inline attachment (avoids showing as attachment in email clients)
        var logoUrl = "https://share.aspendora.com/aspendora-logo.png";

        var htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
</head>
<body style='font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, ""Helvetica Neue"", Arial, sans-serif; line-height: 1.6; color: #374151;'>
    <div style='max-width: 600px; margin: 0 auto; padding: 20px;'>
        <div style='text-align: center; margin-bottom: 32px;'>
            <img src='{logoUrl}' alt='Aspendora Technologies' style='height: 64px; width: auto;' />
            <h1 style='color: #660000; margin-top: 16px;'>Aspendora File Share</h1>
        </div>

        <div style='background-color: #ffffff; border: 2px solid #660000; border-radius: 8px; padding: 32px;'>
            <h2 style='color: #111827; margin-top: 0;'>Howdy,</h2>
            <p style='font-size: 16px; color: #374151;'>
                <strong>{System.Net.WebUtility.HtmlEncode(senderName)}</strong> has shared {files.Count} file(s) with you via Aspendora File Share.
            </p>
            {messageHtml}
            <h3 style='color: #111827; margin-top: 24px;'>Files ({files.Count}):</h3>
            <ul style='list-style: none; padding: 0; margin: 16px 0;'>
                {fileListHtml}
            </ul>
            <div style='text-align: center; margin: 32px 0;'>
                <a href='{shareUrl}' style='display: inline-block; background-color: #660000; color: #ffffff; padding: 14px 32px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 16px;'>
                    Download Files
                </a>
            </div>
            <p style='font-size: 14px; color: #6b7280; margin-top: 24px; padding-top: 24px; border-top: 1px solid #e5e7eb;'>
                <strong>Note:</strong> This link will expire in 30 days.
                {(files.Count > 1 ? " Multiple files will be automatically zipped for download." : "")}
            </p>
        </div>

        <div style='text-align: center; margin-top: 32px; font-size: 14px; color: #9ca3af;'>
            <p>© {DateTime.UtcNow.Year} Aspendora Technologies</p>
        </div>
    </div>
</body>
</html>";

        var textBody = $@"Howdy,

{senderName} has shared {files.Count} file(s) with you via Aspendora File Share.

{(string.IsNullOrWhiteSpace(customMessage) ? "" : $"Message: {customMessage}\n\n")}Files:
{string.Join("\n", files.Select(f => $"- {f.FileName} ({FormatBytes(f.FileSize)})"))}

Download your files: {shareUrl}

Note: This link will expire in 30 days.

© {DateTime.UtcNow.Year} Aspendora Technologies";

        var request = new Smtp2GoRequest
        {
            ApiKey = _apiKey,
            To = new[] { recipientEmail },
            Sender = $"{senderName} <{senderEmail}>",
            Subject = $"{senderName} shared files with you via Aspendora",
            HtmlBody = htmlBody,
            TextBody = textBody
        };

        _logger.LogInformation("Sending email via SMTP2GO API: url={ApiUrl}, apiKeyLength={ApiKeyLength}",
            _apiUrl, _apiKey?.Length ?? 0);

        var response = await _httpClient.PostAsJsonAsync(_apiUrl, request);
        var responseContent = await response.Content.ReadAsStringAsync();

        _logger.LogInformation("SMTP2GO response: status={StatusCode}, body={ResponseBody}",
            response.StatusCode, responseContent);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("SMTP2GO API error: {StatusCode} - {Response}", response.StatusCode, responseContent);
            throw new HttpRequestException($"SMTP2GO API returned {response.StatusCode}: {responseContent}");
        }

        _logger.LogInformation("Email sent successfully to {Email}", recipientEmail);
    }

    /// <summary>
    /// Send weekly report email
    /// </summary>
    public async Task SendReportEmailAsync(string recipientEmail, string subject, string htmlBody)
    {
        var request = new Smtp2GoRequest
        {
            ApiKey = _apiKey,
            To = new[] { recipientEmail },
            Sender = "Aspendora File Share <notifications@aspendora.com>",
            Subject = subject,
            HtmlBody = htmlBody,
            TextBody = "Please view this email in an HTML-capable email client."
        };

        var response = await _httpClient.PostAsJsonAsync(_apiUrl, request);
        response.EnsureSuccessStatusCode();

        _logger.LogInformation("Report email sent to {Email}", recipientEmail);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes == 0) return "0 Bytes";
        var k = 1024.0;
        var sizes = new[] { "Bytes", "KB", "MB", "GB" };
        var i = (int)Math.Floor(Math.Log(bytes) / Math.Log(k));
        return $"{Math.Round(bytes / Math.Pow(k, i), 2)} {sizes[i]}";
    }

    private class Smtp2GoRequest
    {
        [JsonPropertyName("api_key")]
        public string ApiKey { get; set; } = null!;

        [JsonPropertyName("to")]
        public string[] To { get; set; } = null!;

        [JsonPropertyName("sender")]
        public string Sender { get; set; } = null!;

        [JsonPropertyName("subject")]
        public string Subject { get; set; } = null!;

        [JsonPropertyName("html_body")]
        public string HtmlBody { get; set; } = null!;

        [JsonPropertyName("text_body")]
        public string TextBody { get; set; } = null!;

        [JsonPropertyName("inlines")]
        public Smtp2GoInline[]? Inlines { get; set; }
    }

    private class Smtp2GoInline
    {
        [JsonPropertyName("filename")]
        public string Filename { get; set; } = null!;

        [JsonPropertyName("fileblob")]
        public string Fileblob { get; set; } = null!;

        [JsonPropertyName("mimetype")]
        public string Mimetype { get; set; } = null!;

        [JsonPropertyName("headers")]
        public Dictionary<string, string>? Headers { get; set; }
    }
}
