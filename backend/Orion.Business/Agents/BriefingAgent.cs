using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Orion.Core.DTOs.Requests;
using Orion.Core.Configuration;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Agents;
using Orion.Core.Interfaces.Daemon;
using Orion.Core.Interfaces.LLM;
using Orion.Core.Interfaces.Repositories;
using Orion.Core.Interfaces.Services;

using Orion.Business.LLM;

namespace Orion.Business.Agents;

public class BriefingAgent : IBriefingAgent
{
    private readonly ILLMAgentClient _llmClient;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDaemonClient _daemonClient;
    private readonly INewsCollector _news;
    private readonly INewsQueryPlanner _newsPlanner;
    private readonly ILogger<BriefingAgent> _logger;

    public BriefingAgent(
        ILLMAgentClient llmClient,
        IUnitOfWork unitOfWork,
        IDaemonClient daemonClient,
        INewsCollector news, INewsQueryPlanner newsPlanner, ILogger<BriefingAgent> logger)
    {
        _llmClient = llmClient;
        _unitOfWork = unitOfWork;
        _daemonClient = daemonClient;
        _news = news;
        _newsPlanner = newsPlanner;
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
        // Ce que la proactivité a mis de côté : vrai, mais pas assez urgent pour interrompre.
        // Le briefing est exactement le bon moment pour le dire — une fois, groupé.
        var differes = await RecupererSignauxDifferesAsync(ct);

        // La veille est COLLECTEE, jamais demandee au modele : un LLM a qui on demande « quoi de
        // neuf » invente, et une actualite inventee vaut moins que pas d'actualite.
        // Les flux fixes couvrent les cercles ; les requetes dynamiques suivent ce sur quoi tu
        // travailles vraiment. Sans elles la veille serait un marque-page.
        var dynamicFeeds = await _newsPlanner.PlanAsync(ct);
        var harvest = await _news.CollectAsync(dynamicFeeds, ct);

        // Les sources partent AVEC le texte : sans elles, la garantie « rien n'est invente »
        // existe dans le code et reste invisible pour le lecteur, donc invérifiable.
        var sources = harvest.Items.Select(i => new BriefingSource
        {
            Title = i.Title,
            Url = i.Link,
            Source = i.Source,
            Circle = i.Circle.ToString().ToLowerInvariant(),
        }).ToList();

        var prompt = BuildBriefingPrompt(
            profileDict, recentMemories.Select(m => m.Content).ToList(), differes, harvest, now);

        var request = new LLMRequest
        {
            SystemPrompt = "Tu es ORION, l'assistant IA personnel de l'utilisateur. Génère uniquement le briefing demandé, sans introduction ni conclusion supplémentaire.",
            Messages = [new LLMMessage { Role = "user", Content = prompt }],
            Temperature = 0.7f,
            MaxTokens = 500
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
                ["model"] = _llmClient.ModelId,
                ["newsCollected"] = harvest.Items.Count,
                ["newsFeedsFailed"] = harvest.FailedFeeds.Count
            },
            Sources = sources
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
            // « Ne promets rien » n'est pas une coquetterie : sans cette consigne, le modele
            // annoncait « je vais optimiser les processus » alors qu'aucun outil n'est branche
            // sur ce chemin. Un assistant qui promet et ne fait pas perd toute credibilite —
            // c'est le meme defaut que le fallback silencieux, vu depuis l'utilisateur.
            SystemPrompt =
                "Tu es ORION. Réponds uniquement avec le message à dire à voix haute, sans "
                + "guillemets ni ponctuation excessive. 1-2 phrases maximum. "
                + "Tu SIGNALES, tu n'agis pas : ce message ne déclenche aucune action. "
                + "N'annonce donc jamais que tu vas faire quelque chose — ni « je vais fermer », "
                + "ni « je m'en occupe », ni « j'optimise ». Décris le fait, et propose seulement "
                + "si c'est utile.",
            Messages = [new LLMMessage { Role = "user", Content = prompt }],
            Temperature = 0.8f,
            MaxTokens = 80
        };

        try
        {
            var message = await _llmClient.CompleteTextAsync(request, ct);

            // Le prompt interdit deja de promettre une action, et le modele le fait quand meme
            // alors qu'aucun outil n'est branche sur ce chemin. Un garde-fou dans le prompt n'est
            // pas un garde-fou : c'est la lecon d'ADR-015, appliquee ici.
            if (!string.IsNullOrWhiteSpace(message) && !PromisesAction(message))
            {
                return ApiResponse<string>.SuccessResponse(message.Trim());
            }

            if (!string.IsNullOrWhiteSpace(message))
            {
                _logger.LogWarning("[BriefingAgent] Message proactif REFUSE (promesse d'action) : {Message}",
                    message.Trim());
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[BriefingAgent] LLM indisponible, repli sur un message contextuel");
        }

        // Repli contextuel : un message proactif muet vaut moins qu'un message generique.
        return ApiResponse<string>.SuccessResponse(GetFallbackMessage(pattern, context));
    }

    /// <summary>
    /// Le message promet-il une action que rien n'executera ?
    ///
    /// Ce chemin n'a AUCUN outil branche : le daemon signale, il n'agit pas. Un message qui dit
    /// « je redemarre » ou « attends » decrit un travail qui n'aura pas lieu, et l'utilisateur
    /// attend pour rien. C'est le meme defaut qu'un repli silencieux, vu depuis l'utilisateur.
    ///
    /// On refuse et on retombe sur le message deterministe plutot que de reformuler : une
    /// seconde generation pourrait promettre autre chose.
    /// </summary>
    public static bool PromisesAction(string message)
    {
        // Premiere personne + verbe d'action, ou une demande d'attendre. Volontairement large :
        // un faux positif coute un message generique, un faux negatif coute la credibilite.
        return Regex.IsMatch(
            message,
            @"\b(je\s+(vais|vais\s+te|m'en\s+occupe|redémarre|redemarre|relance|corrige|répare|repare|vérifie|verifie|regarde|lance|optimise|nettoie|ferme|installe|configure)"
            + @"|j'(ai\s+lancé|ai\s+lance|optimise|analyse|examine)"
            + @"|laisse-moi|attends|patiente|un\s+instant|je\s+te\s+tiens\s+au\s+courant)\b",
            RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Vide la file des signaux différés du daemon. Sans cet appel, la boucle de décision
    /// construisait une file d'attente SANS SORTIE : ce qui n'interrompait pas n'était
    /// jamais dit.
    /// </summary>
    private async Task<List<string>> RecupererSignauxDifferesAsync(CancellationToken ct)
    {
        if (!_daemonClient.IsConnected) return new List<string>();

        try
        {
            var reponse = await _daemonClient.SendActionAsync(new DaemonActionRequest
            {
                RequestId = Guid.NewGuid().ToString("N"),
                Action = "proactive_deferred",
                Payload = new { }
            }, ct);

            if (!reponse.Success || reponse.Data?.Data is null) return new List<string>();

            var json = System.Text.Json.JsonSerializer.Serialize(reponse.Data.Data);
            using var doc = System.Text.Json.JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("signals", out var signaux))
                return new List<string>();

            var resultat = signaux.EnumerateArray()
                .Select(s => s.TryGetProperty("context", out var c) ? c.GetString() : null)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c!)
                .ToList();

            if (resultat.Count > 0)
                _logger.LogInformation("[BriefingAgent] {Count} signal(aux) differe(s) repris au briefing", resultat.Count);

            return resultat;
        }
        catch (Exception ex)
        {
            // Le daemon peut être absent ou lent : un briefing sans les différés vaut mieux
            // que pas de briefing du tout.
            _logger.LogWarning(ex, "[BriefingAgent] Signaux differes indisponibles");
            return new List<string>();
        }
    }

    private static string BuildBriefingPrompt(
        Dictionary<string, string> profile,
        List<string> recentMemoryContents,
        List<string> signauxDifferes,
        NewsHarvest harvest,
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

        if (signauxDifferes.Count > 0)
        {
            sb.AppendLine("Signalé pendant que je travaillais, sans t'interrompre :");
            foreach (var signal in signauxDifferes)
                sb.AppendLine($"- {signal}");
            sb.AppendLine();
        }

        if (harvest.Items.Count > 0)
        {
            sb.AppendLine("Veille du jour — ce sont de VRAIS articles collectés, classés du plus proche au plus lointain :");
            foreach (var item in harvest.Items)
            {
                var cercle = item.Circle switch
                {
                    NewsCircle.Local => "France/ESIEA/EDF",
                    NewsCircle.Africa => "Togo/Afrique",
                    _ => "monde"
                };
                sb.AppendLine($"- [{cercle}] {item.Title} ({item.Source})");
            }
            sb.AppendLine();
        }

        sb.AppendLine("Génère mon briefing matinal en 3 à 5 phrases naturelles et directes.");
        sb.AppendLine("Parle à la deuxième personne, comme si tu me parlais maintenant.");
        sb.AppendLine("Rappelle les priorités ou projets en cours si tu les connais.");
        sb.AppendLine("Mentionne ce qui a été signalé sans interrompre — c'est précisément le moment.");
        sb.AppendLine("Sur la veille : retiens au plus trois sujets, ceux qui servent vraiment un etudiant");
        sb.AppendLine("en informatique travaillant avec EDF. N'INVENTE AUCUNE actualite : si un sujet");
        sb.AppendLine("n'est pas dans la liste ci-dessus, il n'existe pas. Cite la source.");
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
