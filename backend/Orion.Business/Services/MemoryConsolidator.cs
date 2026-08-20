using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orion.Business.LLM;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Entities;
using Orion.Core.Enums;
using Orion.Core.Interfaces.LLM;
using Orion.Core.Interfaces.Repositories;
using Orion.Core.Interfaces.Services;

namespace Orion.Business.Services;

/// <summary>
/// Distille les épisodes bruts en faits durables, rangés dans le schéma fermé à 4 slots.
///
/// L'ancienne « réflexion » comptait les souvenirs par source et affichait les 30 premiers
/// caractères de cinq d'entre eux. Elle ne distillait rien — son propre commentaire le
/// reconnaissait (« could be enhanced with LLM »).
/// </summary>
public class MemoryConsolidator : IMemoryConsolidator
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILLMAgentClient _llmClient;
    private readonly IMemoryService _memoryService;
    private readonly ILogger<MemoryConsolidator> _logger;

    /// <summary>Au-delà, le prompt de distillation devient plus coûteux que la valeur extraite.</summary>
    private const int EpisodesParPasse = 30;

    /// <summary>Un état « en cours » plus vieux que ça décrit un chantier terminé ou abandonné.</summary>
    private const int JoursDeRetentionEtat = 14;

    public MemoryConsolidator(
        IUnitOfWork unitOfWork,
        ILLMAgentClient llmClient,
        IMemoryService memoryService,
        ILogger<MemoryConsolidator> logger)
    {
        _unitOfWork = unitOfWork;
        _llmClient = llmClient;
        _memoryService = memoryService;
        _logger = logger;
    }

    public async Task<ApiResponse<ConsolidationReport>> ConsolidateAsync(CancellationToken ct = default)
    {
        try
        {
            var tous = (await _unitOfWork.Memory.GetAllAsync(ct)).ToList();

            var episodes = tous
                .Where(m => EstEpisode(m.Source))
                .OrderByDescending(m => m.CreatedAt)
                .Take(EpisodesParPasse)
                .ToList();

            if (episodes.Count == 0)
            {
                return ApiResponse<ConsolidationReport>.SuccessResponse(new ConsolidationReport
                {
                    Resume = "Rien à consolider : aucun épisode brut en mémoire."
                });
            }

            var deja = tous.Where(m => !EstEpisode(m.Source)).ToList();

            List<FaitDistille> distilles;
            try
            {
                distilles = await DistillerAsync(episodes, deja, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // La distillation a échoué : on NE consomme PAS les épisodes. Ils seront relus
                // à la prochaine passe. Perdre de la matière première sur une panne serait pire
                // que de la garder en double.
                _logger.LogError(ex, "[Consolidation] Distillation impossible — episodes conserves");
                return ApiResponse<ConsolidationReport>.ErrorResponse(
                    $"Distillation impossible : {ex.Message}", 503);
            }

            var rapport = new ConsolidationReport { EpisodesExamines = episodes.Count };

            foreach (var fait in distilles)
            {
                // L'état est VOLATILE : le neuf remplace l'ancien, sinon on empile des
                // « chantiers en cours » terminés depuis longtemps.
                if (fait.Slot == MemorySlot.State)
                    rapport.EtatsPerimesSupprimes += await PurgerEtatAsync(deja, ct);

                var enregistre = await _memoryService.SaveMemoryAsync(
                    fait.Contenu,
                    fait.Slot.ToString().ToLowerInvariant(),
                    fait.Importance,
                    ct);

                if (!enregistre.Success) continue;

                rapport.SouvenirsEcrits++;
                rapport.Distilles.Add($"[{fait.Slot.ToString().ToLowerInvariant()}] {fait.Contenu}");
            }

            // Les épisodes relus sont CONSOMMÉS : sans ça, chaque passe laisse le brut à côté
            // du distillé et la mémoire se remplit de doublons — précisément le bruit que le
            // schéma fermé doit empêcher.
            //
            // Aucune perte : les échanges bruts restent intégralement dans la table `messages`,
            // qui est l'historique de conversation. `memory_vectors` ne porte que du savoir.
            foreach (var episode in episodes)
            {
                await _memoryService.DeleteMemoryAsync(episode.Id.ToString(), ct);
                rapport.EpisodesConsommes++;
            }

            rapport.Resume = rapport.SouvenirsEcrits == 0
                ? $"{episodes.Count} épisode(s) relu(s) — rien de durable à en tirer."
                : $"{episodes.Count} épisode(s) relu(s) → {rapport.SouvenirsEcrits} fait(s) durable(s).";

            _logger.LogInformation("[Consolidation] {Resume}", rapport.Resume);
            return ApiResponse<ConsolidationReport>.SuccessResponse(rapport);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Consolidation] Echec");
            return ApiResponse<ConsolidationReport>.ErrorResponse($"Consolidation impossible : {ex.Message}", 500);
        }
    }

    /// <summary>Tout ce qui n'appartient pas au schéma fermé est un épisode brut.</summary>
    private static bool EstEpisode(string? source)
        => !Enum.TryParse<MemorySlot>(source, ignoreCase: true, out var slot)
           || slot == MemorySlot.Episode;

    private async Task<int> PurgerEtatAsync(List<MemoryVector> existants, CancellationToken ct)
    {
        var perimes = existants
            .Where(m => string.Equals(m.Source, nameof(MemorySlot.State), StringComparison.OrdinalIgnoreCase)
                        && m.CreatedAt < DateTime.UtcNow.AddDays(-JoursDeRetentionEtat))
            .ToList();

        foreach (var perime in perimes)
        {
            await _memoryService.DeleteMemoryAsync(perime.Id.ToString(), ct);
            existants.Remove(perime);
        }

        return perimes.Count;
    }

    private sealed record FaitDistille(MemorySlot Slot, string Contenu, float Importance);

    private async Task<List<FaitDistille>> DistillerAsync(
        List<MemoryVector> episodes,
        List<MemoryVector> deja,
        CancellationToken ct)
    {
        var request = new LLMRequest
        {
            SystemPrompt = ConstruirePrompt(deja),
            Messages = new List<LLMMessage>
            {
                new() { Role = "user", Content = ConstruireCorpus(episodes) }
            },
            Temperature = 0.2f, // distillation : on veut de la constance, pas de la créativité
            MaxTokens = 900
        };

        var reponse = await _llmClient.CompleteTextAsync(request, ct);
        return Analyser(reponse);
    }

    private static string ConstruirePrompt(List<MemoryVector> deja)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Tu consolides la mémoire long terme d'ORION.");
        sb.AppendLine("On te donne des échanges bruts. Tu en extrais UNIQUEMENT ce qui restera vrai");
        sb.AppendLine("dans six mois, et tu le ranges dans l'un des quatre emplacements suivants :");
        sb.AppendLine();
        sb.AppendLine("- rules     : comment se comporter — une correction reçue, une posture attendue");
        sb.AppendLine("- decisions : une décision durable ET son pourquoi");
        sb.AppendLine("- refs      : un pointeur stable — chemin, port, URL, identifiant");
        sb.AppendLine("- state     : ce qui est en cours et se périmera");
        sb.AppendLine();
        sb.AppendLine("RÈGLES DE TRI");
        sb.AppendLine("- Un fait par ligne, formulé de façon AUTONOME : il sera relu hors contexte.");
        sb.AppendLine("- N'invente RIEN. Si un échange ne contient aucun fait durable, ne produis rien pour lui.");
        sb.AppendLine("- N'écris pas ce qui figure déjà en mémoire (liste ci-dessous) ni une simple reformulation.");
        sb.AppendLine("- Mieux vaut ZÉRO ligne qu'une ligne creuse. La valeur d'une mémoire tient à sa brièveté.");
        sb.AppendLine();

        if (deja.Count > 0)
        {
            sb.AppendLine("DÉJÀ EN MÉMOIRE — ne pas répéter :");
            foreach (var m in deja.Take(40))
                sb.AppendLine($"- [{m.Source}] {m.Content}");
            sb.AppendLine();
        }

        sb.AppendLine("FORMAT DE SORTIE — une ligne par fait, rien d'autre, aucun préambule :");
        sb.AppendLine("slot|importance|fait");
        sb.AppendLine("Exemple : decisions|1.5|Yawo héberge le backend ORION sur un VPS IONOS, la base sur Supabase Cloud.");
        sb.AppendLine("L'importance va de 0.5 à 2.0. Si rien n'est à retenir, réponds exactement : AUCUN");

        return sb.ToString();
    }

    private static string ConstruireCorpus(List<MemoryVector> episodes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Échanges à relire :");
        foreach (var e in episodes.OrderBy(e => e.CreatedAt))
            sb.AppendLine($"- ({e.CreatedAt:yyyy-MM-dd}) {e.Content}");
        return sb.ToString();
    }

    /// <summary>
    /// Analyse la sortie du modèle. Tolérante par conception : une ligne mal formée est ignorée,
    /// jamais écrite « au cas où ». Une mémoire ne se remplit pas de ce qu'on n'a pas compris.
    /// </summary>
    private List<FaitDistille> Analyser(string reponse)
    {
        var faits = new List<FaitDistille>();
        if (string.IsNullOrWhiteSpace(reponse)) return faits;

        foreach (var ligne in reponse.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var nettoyee = ligne.Trim().TrimStart('-', '*', ' ');
            if (nettoyee.Equals("AUCUN", StringComparison.OrdinalIgnoreCase)) break;

            var parts = nettoyee.Split('|', 3);
            if (parts.Length != 3) continue;

            if (!Enum.TryParse<MemorySlot>(parts[0].Trim(), ignoreCase: true, out var slot)
                || slot == MemorySlot.Episode)
            {
                _logger.LogWarning("[Consolidation] Emplacement inconnu ignore : {Slot}", parts[0].Trim());
                continue;
            }

            var contenu = parts[2].Trim();
            if (contenu.Length < 15) continue; // trop court pour porter un fait autonome

            var importance = float.TryParse(parts[1].Trim(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var i)
                ? Math.Clamp(i, 0.5f, 2.0f)
                : 1.0f;

            faits.Add(new FaitDistille(slot, contenu, importance));
        }

        return faits;
    }
}
