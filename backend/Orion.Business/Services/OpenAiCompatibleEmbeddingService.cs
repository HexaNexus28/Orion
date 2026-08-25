using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orion.Core.Configuration;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Services;

namespace Orion.Business.Services;

/// <summary>
/// Embeddings via NVIDIA NIM (API hébergée, compatible OpenAI).
///
/// POURQUOI ce service remplace l'ancien EmbeddingService sur Ollama : le cerveau était déjà passé
/// sur NIM en J3, mais la vectorisation appelait toujours `localhost:11434`. Sur le poste ça
/// fonctionne — Ollama y tourne en service Windows. Sur le VPS il n'y a PAS d'Ollama : la mémoire
/// serait morte, silencieusement, sans qu'aucune requête n'échoue visiblement. C'était le dernier
/// obstacle au fonctionnement d'ORION 24/7, PC éteint.
///
/// Une seule dépendance distante désormais : même fournisseur, même clé que le cerveau.
/// </summary>
public class OpenAiCompatibleEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenAiCompatibleEmbeddingService> _logger;
    private readonly EmbeddingOptions _options;

    public OpenAiCompatibleEmbeddingService(HttpClient httpClient, IOptions<EmbeddingOptions> options, ILogger<OpenAiCompatibleEmbeddingService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;

        _httpClient.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _options.ApiKey);
    }

    public string ModelName => _options.Model;

    public async Task<ApiResponse<float[]>> GenerateEmbeddingAsync(
        string text,
        EmbeddingInputType inputType,
        CancellationToken ct = default)
    {
        if (!_options.IsConfigured)
            return ApiResponse<float[]>.ErrorResponse("Embedding:ApiKey absent — embedding impossible");

        if (string.IsNullOrWhiteSpace(text))
            return ApiResponse<float[]>.ErrorResponse("Texte vide — rien à vectoriser");

        try
        {
            // `input_type` (query/passage) est EXIGE par les modeles de recherche NVIDIA, qui sont
            // asymetriques, et REFUSE par Mistral. On ne l'envoie donc que si le fournisseur le
            // comprend. Pas de `dimensions` : nemotron le refuse (HTTP 400), mistral-embed est fixe.
            //
            // Dictionnaire et NON type anonyme : System.Text.Json serialise le type DECLARE. Un
            // `object` portant un type anonyme partirait en `{}` — requete vide, panne muette.
            var request = new Dictionary<string, object>
            {
                ["model"] = _options.Model,
                ["input"] = new[] { text }
            };

            if (_options.SupportsInputType)
            {
                request["input_type"] = inputType == EmbeddingInputType.Query ? "query" : "passage";
                request["encoding_format"] = "float";
            }

            var response = await _httpClient.PostAsJsonAsync("embeddings", request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                // 404/410 = modèle retiré du service tout en restant au catalogue. Le dire
                // explicitement : c'est la panne qui a fait tourner ORION des mois en dégradé.
                _logger.LogError("[Embedding] {Status} sur {Model} — modele retire ou indisponible. {Error}",
                    (int)response.StatusCode, _options.Model, error);
                return ApiResponse<float[]>.ErrorResponse(
                    $"{_options.Model} {(int)response.StatusCode} sur {_options.Model}");
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
            {
                _logger.LogWarning("[Embedding] Reponse sans tableau 'data'");
                return ApiResponse<float[]>.ErrorResponse("Reponse sans embedding");
            }

            var embedding = data[0].GetProperty("embedding")
                .EnumerateArray()
                .Select(e => (float)e.GetDouble())
                .ToArray();

            // Garde-fou : une dimension inattendue signifie que le modele a change sous nos pieds.
            // Ecrire de tels vecteurs dans une colonne vector(N) echouerait cote base, mais plus
            // tard et avec un message incomprehensible. On refuse ici, ou la cause est lisible.
            if (embedding.Length != _options.Dimensions)
            {
                _logger.LogError("[Embedding] {Model} renvoie {Got} dims, {Expected} attendues — schema incompatible",
                    _options.Model, embedding.Length, _options.Dimensions);
                return ApiResponse<float[]>.ErrorResponse(
                    $"Dimensions inattendues : {embedding.Length} au lieu de {_options.Dimensions}");
            }

            _logger.LogDebug("[Embedding] {Dims} dims ({Type}, {Chars} caracteres)",
                embedding.Length, inputType, text.Length);

            return ApiResponse<float[]>.SuccessResponse(embedding);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Embedding] Echec de vectorisation");
            return ApiResponse<float[]>.ErrorResponse($"Echec embedding : {ex.Message}");
        }
    }
}
