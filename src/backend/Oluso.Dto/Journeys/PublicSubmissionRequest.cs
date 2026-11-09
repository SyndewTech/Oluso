namespace Oluso.Dto.Journeys;

public class PublicSubmissionRequest
{
    public Dictionary<string, object?> Data { get; set; } = new();
    public Dictionary<string, string>? Metadata { get; set; }
    public string? Honeypot { get; set; }
}
