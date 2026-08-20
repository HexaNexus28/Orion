namespace Orion.Core.Configuration;

public class AgentOptions
{
    public const string SectionName = "Agent";

    /// <summary>
    /// Budget d'allers-retours LLM par tour utilisateur. Chaque itération = 1 appel au modèle
    /// (+ l'exécution des outils qu'il demande). Borne le coût et empêche une boucle infinie
    /// si le modèle redemande sans fin le même outil.
    /// </summary>
    public int MaxToolIterations { get; set; } = 6;

    /// <summary>Longueur max d'un résultat d'outil renvoyé à l'UI (le LLM reçoit le résultat complet).</summary>
    public int ToolSummaryMaxChars { get; set; } = 500;
}
