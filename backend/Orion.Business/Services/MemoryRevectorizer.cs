using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orion.Core.Configuration;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Repositories;
using Orion.Core.Interfaces.Services;

namespace Orion.Business.Services;

/// <inheritdoc cref="IMemoryRevectorizer"/>
public class MemoryRevectorizer : IMemoryRevectorizer
{
    // Lots volontairement petits : le palier gratuit du fournisseur est limite en requetes par
    // minute, et une revectorisation n'a aucune raison d'etre rapide. Mieux vaut lente et sure
    // qu'interrompue a mi-parcours avec une table a moitie dans chaque espace vectoriel.
    private const int BatchSize = 25;
    private const int DelayBetweenCallsMs = 120;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmbeddingService _embeddingService;
    private readonly EmbeddingOptions _options;
    private readonly ILogger<MemoryRevectorizer> _logger;

    public MemoryRevectorizer(
        IUnitOfWork unitOfWork,
        IEmbeddingService embeddingService,
        IOptions<EmbeddingOptions> options,
        ILogger<MemoryRevectorizer> logger)
    {
        _unitOfWork = unitOfWork;
        _embeddingService = embeddingService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ApiResponse<RevectorizeReport>> RunAsync(int? maxRows = null, CancellationToken ct = default)
    {
        var model = _embeddingService.ModelName;
        var sw = Stopwatch.StartNew();

        // Sonde AVANT de commencer : si le modele est mort (404/410 — deja vu quatre fois sur le
        // catalogue NVIDIA), autant l'apprendre maintenant que sur la 300e ligne.
        var probe = await _embeddingService.GenerateEmbeddingAsync(
            "sonde de revectorisation", EmbeddingInputType.Passage, ct);

        if (!probe.Success || probe.Data == null)
        {
            _logger.LogError("[Revectorize] Sonde en echec sur {Model} — operation annulee", model);
            return ApiResponse<RevectorizeReport>.ErrorResponse(
                $"Modele {model} injoignable : {probe.Message}. Rien n'a ete modifie.");
        }

        var total = await _unitOfWork.Memory.CountPendingRevectorizationAsync(model, ct);
        _logger.LogInformation("[Revectorize] {Total} souvenir(s) a revectoriser avec {Model} ({Dims} dims)",
            total, model, probe.Data.Length);

        var report = new RevectorizeReport
        {
            Model = model,
            Dimensions = probe.Data.Length,
            Total = total
        };

        if (total == 0)
        {
            sw.Stop();
            report.DurationSeconds = Math.Round(sw.Elapsed.TotalSeconds, 1);
            return ApiResponse<RevectorizeReport>.SuccessResponse(report);
        }

        var budget = maxRows ?? int.MaxValue;

        while (report.Done + report.Failed < budget && !ct.IsCancellationRequested)
        {
            var batch = await _unitOfWork.Memory.GetPendingRevectorizationAsync(model, BatchSize, ct);
            if (batch.Count == 0) break;

            foreach (var (id, content) in batch)
            {
                if (report.Done + report.Failed >= budget || ct.IsCancellationRequested) break;

                // Passage : ces textes sont du contenu STOCKE, jamais des recherches.
                var embedding = await _embeddingService.GenerateEmbeddingAsync(
                    content, EmbeddingInputType.Passage, ct);

                if (!embedding.Success || embedding.Data == null)
                {
                    report.Failed++;
                    _logger.LogWarning("[Revectorize] Echec sur {Id} : {Message}", id, embedding.Message);
                    continue;
                }

                await _unitOfWork.Memory.SaveEmbeddingAsync(id, embedding.Data, model, ct);
                report.Done++;

                if (report.Done % 25 == 0)
                    _logger.LogInformation("[Revectorize] {Done}/{Total}", report.Done, total);

                await Task.Delay(DelayBetweenCallsMs, ct);
            }

            // Un lot entierement en echec signifie que le probleme n'est pas la donnee mais le
            // fournisseur. Continuer ne ferait qu'empiler les echecs et consommer le quota.
            if (batch.Count > 0 && report.Done == 0 && report.Failed >= batch.Count)
            {
                _logger.LogError("[Revectorize] Lot entier en echec — arret. Le fournisseur est en cause.");
                break;
            }
        }

        report.Remaining = await _unitOfWork.Memory.CountPendingRevectorizationAsync(model, ct);
        sw.Stop();
        report.DurationSeconds = Math.Round(sw.Elapsed.TotalSeconds, 1);

        _logger.LogInformation("[Revectorize] Termine : {Done} ok, {Failed} en echec, {Remaining} restant(s) en {Sec}s",
            report.Done, report.Failed, report.Remaining, report.DurationSeconds);

        return ApiResponse<RevectorizeReport>.SuccessResponse(report);
    }
}
