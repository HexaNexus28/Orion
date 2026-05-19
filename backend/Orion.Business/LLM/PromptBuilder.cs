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
        LLMProvider activeProvider,
        bool voiceMode = false)
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

        if (voiceMode)
        {
            sb.AppendLine();
            sb.AppendLine("MODE VOIX ACTIF — règles spéciales :");
            sb.AppendLine("- Réponds comme dans une CONVERSATION ORALE naturelle");
            sb.AppendLine("- Phrases COURTES (max 2-3 phrases par idée), rythme conversationnel");
            sb.AppendLine("- AUCUN markdown : pas de **, pas de ```, pas de listes à puces, pas de #");
            sb.AppendLine("- AUCUN formatage visuel — tout sera lu à voix haute");
            sb.AppendLine("- Utilise des connecteurs oraux : 'alors', 'du coup', 'en gros', 'par contre'");
            sb.AppendLine("- Pour les chiffres : dis 'environ 1500' pas '~1,500'");
            sb.AppendLine("- Si la réponse est longue, résume en 3-4 phrases et propose d'approfondir");
            sb.AppendLine("- Ton naturel, comme un collègue dev qui explique — pas un robot qui lit de la doc");
        }
        else
        {
            sb.AppendLine("- Pour afficher des stats/chiffres clairement : utilise le format **Label**: Valeur");
        }
        
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
