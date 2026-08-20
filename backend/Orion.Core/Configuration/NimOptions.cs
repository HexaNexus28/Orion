namespace Orion.Core.Configuration;

/// <summary>
/// NVIDIA NIM — API hébergée, compatible OpenAI (https://integrate.api.nvidia.com/v1).
/// C'est le cerveau distant d'ORION : le modèle local ne sait pas s'abstenir d'appeler un outil
/// (mesuré le 2026-08-20) et ne tient pas la latence.
/// </summary>
public class NimOptions
{
    public const string SectionName = "Nim";

    public string BaseUrl { get; set; } = "https://integrate.api.nvidia.com/v1";

    /// <summary>Clé `nvapi-...`. JAMAIS dans un fichier tracké — appsettings.Development.json uniquement.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Modèle principal. Mesuré le 2026-08-20 : latence la plus RÉGULIÈRE des candidats
    /// (954/1038/2480 ms) et réussit le test d'abstention que le modèle local rate.
    /// </summary>
    public string Model { get; set; } = "nvidia/nemotron-3-super-120b-a12b";

    /// <summary>Repli plus rapide en plancher (801 ms mesurées) si le principal est indisponible.</summary>
    public string FallbackModel { get; set; } = "nvidia/nemotron-3.5-lightning-30b-a3b";

    public int TimeoutSeconds { get; set; } = 120;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
