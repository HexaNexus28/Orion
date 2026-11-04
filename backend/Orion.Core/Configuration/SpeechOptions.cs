namespace Orion.Core.Configuration;

/// <summary>
/// Synthese vocale distante — Voxtral TTS (Mistral).
///
/// POURQUOI. Kokoro tourne sur le PC de l utilisateur, derriere un aller-retour par le daemon.
/// Mesure du 2026-11-04 : environ 11,5 s pour 15 caracteres, sur un portable a court de memoire.
/// Le texte s affichait, puis il fallait attendre la voix. Voxtral TTS, depuis le VPS :
/// 0,41 s pour 18 caracteres, 1,16 s pour 133. Et ORION ne devient plus muet PC eteint.
/// </summary>
public class SpeechOptions
{
    public const string SectionName = "Speech";

    public string BaseUrl { get; set; } = "https://api.mistral.ai/v1";

    /// <summary>Vide par defaut : retombe sur Embedding:ApiKey, comme la transcription.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Version figee : un alias « latest » changerait la voix sans deploiement.</summary>
    public string Model { get; set; } = "voxtral-mini-tts-3-0";

    /// <summary>Marie, voix francaise predefinie de Voxtral TTS 3.</summary>
    public string VoiceId { get; set; } = "01a1536e-a46c-7101-b6e9-1ebda46309fd";

    /// <summary>Au-dela, le repli Kokoro prend le relais : 133 caracteres se synthetisent en 1,2 s.</summary>
    public int TimeoutSeconds { get; set; } = 8;

    /// <summary>Permet de revenir au tout-local sans redeployer.</summary>
    public bool Enabled { get; set; } = true;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
