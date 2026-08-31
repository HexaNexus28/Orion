using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orion.Core.Configuration;
using Orion.Core.DTOs.Requests;
using Orion.Core.Interfaces.LLM;
using Orion.Core.Interfaces.Repositories;
using Orion.Core.Interfaces.Services;

namespace Orion.Business.Services;

/// <summary>
/// Compose des flux de veille a partir du profil et des souvenirs recents.
///
/// CE QUE LE MODELE FAIT ICI, ET RIEN D'AUTRE : produire des mots-cles. Chaque mot-cle devient
/// une requete Google News reellement interrogee, et seuls les articles rendus par ce flux
/// entrent dans le briefing. Un modele qui inventerait un sujet produirait donc, au pire, un
/// flux vide — jamais une fausse nouvelle.
/// </summary>
public class LlmNewsQueryPlanner : INewsQueryPlanner
{
    private readonly NewsOptions _options;
    private readonly ILLMAgentClient _llm;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<LlmNewsQueryPlanner> _logger;

    public LlmNewsQueryPlanner(
        IOptions<NewsOptions> options,
        ILLMAgentClient llm,
        IUnitOfWork unitOfWork,
        ILogger<LlmNewsQueryPlanner> logger)
    {
        _options = options.Value;
        _llm = llm;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<IReadOnlyList<NewsFeed>> PlanAsync(CancellationToken ct = default)
    {
        if (!_options.DynamicQueriesEnabled || _options.MaxDynamicQueries <= 0)
        {
            return Array.Empty<NewsFeed>();
        }

        try
        {
            var context = await BuildContextAsync(ct);
            if (string.IsNullOrWhiteSpace(context))
            {
                _logger.LogInformation("[News] Aucun contexte utilisateur : pas de requete dynamique");
                return Array.Empty<NewsFeed>();
            }

            var turn = await _llm.StreamTurnAsync(new LLMRequest
            {
                SystemPrompt =
                    "Tu produis UNIQUEMENT des requetes de recherche d'actualite. Jamais de reponse, "
                    + "jamais de commentaire. Format : un tableau JSON de chaines, rien d'autre.",
                Messages =
                [
                    new LLMMessage
                    {
                        Role = "user",
                        Content =
                            $"Voici ce que tu sais de moi :\n{context}\n\n"
                            + $"Donne au plus {_options.MaxDynamicQueries} requetes courtes (2 a 5 mots) "
                            + "pour suivre l'actualite qui me sert vraiment : technologies que j'utilise, "
                            + "entreprises et ecoles qui me concernent, sujets de recherche proches. "
                            + "Pas de sujet deja couvert par une requete generique comme « actualite tech ». "
                            + "Reponds par le seul tableau JSON."
                    }
                ],
                Temperature = 0.4f,
                MaxTokens = 200,
            }, _ => Task.CompletedTask, ct);

            var queries = ParseQueries(turn.Content).Take(_options.MaxDynamicQueries).ToList();

            _logger.LogInformation("[News] {Count} requete(s) dynamique(s) : {Queries}",
                queries.Count, string.Join(" | ", queries));

            return queries.Select(ToFeed).ToList();
        }
        catch (Exception ex)
        {
            // La veille doit survivre a un modele indisponible : on retombe sur les flux fixes.
            _logger.LogWarning("[News] Planification dynamique impossible : {Message}", ex.Message);
            return Array.Empty<NewsFeed>();
        }
    }

    private NewsFeed ToFeed(string query) => new()
    {
        Name = $"Recherche : {query}",
        Url = _options.DynamicFeedUrlTemplate.Replace("{query}", Uri.EscapeDataString(query)),

        // Cercle Local : ces requetes viennent de CE que fait l'utilisateur, elles sont donc par
        // construction plus proches de lui qu'une actualite mondiale generique.
        Circle = NewsCircle.Local,
        Tags = ["dynamique"],
    };

    /// <summary>
    /// Le modele encadre souvent son JSON de texte ou de balises de code. On extrait le premier
    /// tableau plutot que d'exiger une reponse parfaite — et on rend une liste vide si rien
    /// n'est exploitable, jamais une requete devinee.
    /// </summary>
    private static List<string> ParseQueries(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];

        var start = raw.IndexOf('[');
        var end = raw.LastIndexOf(']');
        if (start < 0 || end <= start) return [];

        try
        {
            return JsonSerializer.Deserialize<List<string>>(raw[start..(end + 1)])
                   ?.Select(q => q.Trim())
                   .Where(q => q.Length is >= 3 and <= 80)
                   .ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task<string> BuildContextAsync(CancellationToken ct)
    {
        var sb = new StringBuilder();

        var profiles = await _unitOfWork.UserProfile.GetAllAsync(ct);
        foreach (var p in profiles.Take(15))
        {
            sb.AppendLine($"- {p.Key} : {p.Value}");
        }

        var memories = await _unitOfWork.Memory.GetAllAsync(ct);
        foreach (var m in memories.OrderByDescending(m => m.Importance).Take(10))
        {
            sb.AppendLine($"- {m.Content}");
        }

        return sb.ToString().Trim();
    }
}
