using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Agents;
using Orion.Core.Interfaces.LLM;
using Orion.Core.Interfaces.Repositories;

using Orion.Business.LLM;

namespace Orion.Business.Agents;

public class BriefingAgent : IBriefingAgent
{
    private readonly ILLMAgentClient _llmClient;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<BriefingAgent> _logger;

    public BriefingAgent(ILLMAgentClient llmClient, IUnitOfWork unitOfWork, ILogger<BriefingAgent> logger)
    {
        _llmClient = llmClient;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<ApiResponse<BriefingDto>> GenerateBriefingAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("[BriefingAgent] Generating morning briefing...");

        var profiles = await _unitOfWork.UserProfile.GetAllAsync(ct);
        var profileDict = profiles.ToDictionary(p => p.Key, p => p.Value);

        var memories = await _unitOfWork.Memory.GetAllAsync(ct);
        var recentMemories = memories
            .Where(m => m.CreatedAt > DateTime.UtcNow.AddDays(-7))
            .OrderByDescending(m => m.Importance)
            .Take(10)
            .ToList();

        var now = DateTime.Now;
        var prompt = BuildBriefingPrompt(profileDict, recentMemories.Select(m => m.Content).ToList(), now);

        var request = new LLMRequest
        {
            SystemPrompt = "Tu es ORION, l'assistant IA personnel de l'utilisateur. Génère uniquement le briefing demandé, sans introduction ni conclusion supplémentaire.",
            Messages = [new LLMMessage { Role = "user", Content = prompt }],
            Temperature = 0.7f,
            MaxTokens = 300
        };

        string briefingText;
        try
        {
            briefingText = await _llmClient.CompleteTextAsync(request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[BriefingAgent] LLM indisponible");
            return ApiResponse<BriefingDto>.ErrorResponse("LLM non disponible pour le briefing", 503);
        }

        var briefing = new BriefingDto
        {
            Id = Guid.NewGuid(),
            Content = briefingText.Trim(),
            CreatedAt = now,
            Stats = new Dictionary<string, object>
            {
                ["memoriesUsed"] = recentMemories.Count,
                ["profileKeys"] = profileDict.Count,
                ["model"] = _llmClient.ModelId
            }
        };

        _logger.LogInformation("[BriefingAgent] Briefing generated: {Preview}",
            briefing.Content.Length > 80 ? briefing.Content[..80] + "..." : briefing.Content);

        return ApiResponse<BriefingDto>.SuccessResponse(briefing);
    }

    public async Task<ApiResponse<string>> GenerateProactiveMessageAsync(
        string pattern, string context, CancellationToken ct = default)
    {
        _logger.LogInformation("[BriefingAgent] Generating proactive message for pattern: {Pattern}", pattern);

        var profiles = await _unitOfWork.UserProfile.GetAllAsync(ct);
        var userName = profiles.FirstOrDefault(p => p.Key == "name")?.Value ?? "User";

        var prompt = BuildProactivePrompt(pattern, context, userName);

        var request = new LLMRequest
        {
            SystemPrompt = "Tu es ORION. Réponds uniquement avec le message à dire à voix haute, sans guillemets ni ponctuation excessive. 1-2 phrases maximum.",
            Messages = [new LLMMessage { Role = "user", Content = prompt }],
            Temperature = 0.8f,
            MaxTokens = 80
        };

        try
        {
            var message = await _llmClient.CompleteTextAsync(request, ct);
            if (!string.IsNullOrWhiteSpace(message))
                return ApiResponse<string>.SuccessResponse(message.Trim());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[BriefingAgent] LLM indisponible, repli sur un message contextuel");
        }

        // Repli contextuel : un message proactif muet vaut moins qu'un message generique.
        return ApiResponse<string>.SuccessResponse(GetFallbackMessage(pattern, context));
    }

    private static string BuildBriefingPrompt(
        Dictionary<string, string> profile,
        List<string> recentMemoryContents,
        DateTime now)
    {
        var sb = new StringBuilder();
        var culture = new CultureInfo("fr-FR");
        sb.AppendLine($"Nous sommes le {now.ToString("dddd d MMMM yyyy", culture)} à {now:HH:mm}.");
        sb.AppendLine();

        if (profile.Count > 0)
        {
            sb.AppendLine("Mon profil :");
            foreach (var (key, value) in profile)
                sb.AppendLine($"- {key}: {value}");
            sb.AppendLine();
        }

        if (recentMemoryContents.Count > 0)
        {
            sb.AppendLine("Ce que tu sais de moi cette semaine :");
            foreach (var content in recentMemoryContents)
                sb.AppendLine($"- {content}");
            sb.AppendLine();
        }

        sb.AppendLine("Génère mon briefing matinal en 3 à 5 phrases naturelles et directes.");
        sb.AppendLine("Parle à la deuxième personne, comme si tu me parlais maintenant.");
        sb.AppendLine("Rappelle les priorités ou projets en cours si tu les connais.");
        sb.AppendLine("Termine par une note motivante si pertinent.");
        sb.AppendLine("Pas de markdown, pas de titres, pas de listes — texte continu pour être lu à voix haute.");

        return sb.ToString();
    }

    private static string BuildProactivePrompt(string pattern, string context, string userName)
    {
        var patternDescription = pattern switch
        {
            "skip_meal" => "l'utilisateur n'a pas mangé depuis plusieurs heures",
            "overwork" => "l'utilisateur travaille sans pause depuis longtemps",
            "meal_time" => "c'est l'heure du repas",
            "break_time" => "c'est l'heure d'une pause",
            "night_time" => "il est tard le soir",
            "high_cpu" => "le CPU est fortement chargé",
            "high_ram" => "la RAM est presque pleine",
            _ => context
        };

        return $"Situation détectée : {patternDescription}. Contexte : {context}\n" +
               $"Dis quelque chose d'utile et naturel à {userName} en 1-2 phrases. Ton direct, pas de formules.";
    }

    private static string GetFallbackMessage(string pattern, string context) => pattern switch
    {
        "skip_meal" => "Hé, t'as pensé à manger ? Ça fait un moment.",
        "overwork" => "Tu travailles depuis longtemps. Prends une pause de 5 minutes.",
        "meal_time" => "C'est l'heure de manger.",
        "break_time" => "Pause méritée. Éloigne-toi de l'écran.",
        "night_time" => "Il est tard. Pense à dormir pour être efficace demain.",
        "high_cpu" => "Ton CPU est surchargé. Ferme ce que tu n'utilises pas.",
        "high_ram" => "RAM presque pleine. Redémarre ou ferme des apps.",
        _ => context
    };
}
