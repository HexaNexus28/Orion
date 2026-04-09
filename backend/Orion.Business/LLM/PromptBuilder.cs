using System.Text;
using Orion.Core.DTOs.Responses;
using Orion.Core.Enums;
using Orion.Core.Entities;

namespace Orion.Business.LLM;

public class PromptBuilder
{
    public string BuildSystemPrompt(
        Dictionary<string, string> userProfile,
        List<MemoryVector> relevantMemories,
        List<ToolCallDto> availableTools,
        bool daemonConnected,
        LLMProvider activeProvider)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("Tu es ORION, l'assistant IA personnel de Yawo Zoglo.");
        sb.AppendLine("Tu fais partie de l'écosystème HexaNexus.");
        sb.AppendLine();
        
        // User profile context
        sb.AppendLine("CONTEXTE UTILISATEUR :");
        foreach (var (key, value) in userProfile)
        {
            sb.AppendLine($"- {key}: {value}");
        }
        sb.AppendLine();
        
        // Relevant memories (RAG)
        if (relevantMemories.Any())
        {
            sb.AppendLine("SOUVENIRS PERTINENTS :");
            foreach (var memory in relevantMemories.Take(5))
            {
                sb.AppendLine($"- {memory.Content}");
            }
            sb.AppendLine();
        }
        
        // Behavior rules
        sb.AppendLine("RÈGLES DE COMPORTEMENT :");
        sb.AppendLine("- Réponds toujours en français sauf si explicitement demandé autrement");
        sb.AppendLine("- Sois direct, factuel, technique — Yawo est développeur avancé");
        sb.AppendLine("- Pas de formules de politesse inutiles, pas de \"bien sûr !\", pas de \"certainement !\"");
        sb.AppendLine("- Si tu as un doute sur une information → dis-le clairement");
        sb.AppendLine("- Utilise les tools disponibles avant de répondre si la question nécessite des données fraîches");
        
        if (daemonConnected)
        {
            sb.AppendLine("- Pour les actions système (ouvrir une app, lancer un script) → utilise le daemon");
        }
        
        sb.AppendLine("- Tu connais les projets de Yawo : ShiftStar, HexaNexus 2.0, ORION, EduSocialNews");
        sb.AppendLine();
        
        // Available tools
        if (availableTools.Any())
        {
            sb.AppendLine("TOOLS DISPONIBLES :");
            foreach (var tool in availableTools)
            {
                sb.AppendLine($"- {tool.ToolName}");
            }
            sb.AppendLine();
        }
        
        // Current context
        sb.AppendLine($"DATE ET HEURE ACTUELLES : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"MODE LLM ACTIF : {activeProvider}");
        sb.AppendLine($"DAEMON CONNECTÉ : {(daemonConnected ? "oui" : "non")}");
        
        return sb.ToString();
    }
}
