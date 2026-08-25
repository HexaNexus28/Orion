using Orion.Core.Enums;

namespace Orion.Core.DTOs.Responses;

/// <summary>
/// État du cerveau d'ORION, exposé au métier.
/// Le MODÈLE en fait partie, pas seulement le fournisseur : c'est précisément l'information
/// qui manquait quand ORION tournait des mois sur un modèle de repli sans que rien ne le dise
/// (docs/jarvis-gap-analysis.md §1.11).
/// </summary>
public class LLMStatusDto
{
    public LLMProvider Provider { get; set; }
    public string Model { get; set; } = string.Empty;
    public bool IsOnline { get; set; }
}
