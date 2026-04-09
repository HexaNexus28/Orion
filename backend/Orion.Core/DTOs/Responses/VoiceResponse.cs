using System.Text.Json.Serialization;

namespace Orion.Core.DTOs.Responses;

/// <summary>
/// Réponse de transcription vocale
/// </summary>
public class VoiceResponse
{
    [JsonPropertyName("transcript")]
    public string Transcript { get; set; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("language")]
    public string Language { get; set; } = "auto";
}

/// <summary>
/// Statut du service vocal
/// </summary>
public class VoiceStatusResponse
{
    [JsonPropertyName("isReady")]
    public bool IsReady { get; set; }

    [JsonPropertyName("supportedLanguages")]
    public List<string> SupportedLanguages { get; set; } = new();
}
