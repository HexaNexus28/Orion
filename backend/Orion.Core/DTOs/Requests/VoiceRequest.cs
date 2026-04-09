using System.Text.Json.Serialization;

namespace Orion.Core.DTOs.Requests;

/// <summary>
/// Requête de transcription avec audio en base64
/// </summary>
public class VoiceRequest
{
    [JsonPropertyName("audioBase64")]
    public string AudioBase64 { get; set; } = string.Empty;

    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }
}
