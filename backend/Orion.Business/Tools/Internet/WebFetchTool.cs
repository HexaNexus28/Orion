using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Tools;

namespace Orion.Business.Tools.Internet;

public class WebFetchTool : ITool
{
    /// <summary>
    /// Redirections suivies À LA MAIN, et chacune revérifiée. Les laisser au HttpClient rendait
    /// le garde contournable en une ligne : une URL publique qui répond 302 vers
    /// 169.254.169.254 passait le contrôle d'entrée, et c'est la destination qui était lue.
    /// Le handler est configuré avec AllowAutoRedirect = false dans Program.cs.
    /// </summary>
    private const int MaxRedirections = 5;

    private readonly HttpClient _httpClient;
    private readonly UrlScope _perimetre;
    private readonly ILogger<WebFetchTool> _logger;

    public string Name => "web_fetch";
    public string Description => "Récupère le contenu texte d'une URL (article, doc, page)";

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["url"] = new JsonObject { ["type"] = "string", ["description"] = "URL à récupérer" },
            ["max_length"] = new JsonObject { ["type"] = "integer", ["description"] = "Longueur max du contenu", ["default"] = 5000 }
        },
        ["required"] = new JsonArray { "url" }
    };

    public WebFetchTool(HttpClient httpClient, UrlScope perimetre, ILogger<WebFetchTool> logger)
    {
        _httpClient = httpClient;
        _perimetre = perimetre;
        _logger = logger;
    }

    public async Task<ApiResponse<ToolResult>> ExecuteAsync(JsonObject input, CancellationToken ct)
    {
        var url = input["url"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(url))
        {
            return ApiResponse<ToolResult>.ErrorResponse("URL parameter required", 400);
        }

        // Le périmètre AVANT la requête : schéma, domaines bloqués, et surtout résolution DNS
        // — un nom parfaitement public peut pointer sur 127.0.0.1.
        var (uri, raison) = await _perimetre.VerifierAsync(url, ct);
        if (uri is null)
        {
            _logger.LogWarning("[web_fetch] URL refusée : {Raison}", raison);
            return ApiResponse<ToolResult>.ErrorResponse(raison, 400);
        }

        var maxLength = input["max_length"]?.GetValue<int>() ?? 5000;

        try
        {
            var (response, finale) = await SuivreAsync(uri, ct);
            using (response)
            {
                response.EnsureSuccessStatusCode();

                var html = await response.Content.ReadAsStringAsync(ct);
                var result = ExtractContent(html, finale.ToString(), maxLength);

                var toolResult = new ToolResult
                {
                    Success = true,
                    Data = JsonSerializer.SerializeToNode(result)
                };

                return ApiResponse<ToolResult>.SuccessResponse(toolResult);
            }
        }
        catch (InvalidOperationException ex)
        {
            // Levée par SuivreAsync quand une redirection sort du périmètre : ce n'est pas une
            // panne réseau, c'est un refus, et il doit se lire comme tel.
            _logger.LogWarning("[web_fetch] Redirection refusée pour {Url} : {Message}", url, ex.Message);
            return ApiResponse<ToolResult>.ErrorResponse(ex.Message, 400);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Web fetch failed for URL: {Url}", url);
            return ApiResponse<ToolResult>.ErrorResponse($"Fetch failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Suit les redirections en revalidant CHAQUE saut. L'en-tête User-Agent est posé par
    /// requête plutôt que sur DefaultRequestHeaders : le client typé est partagé, et le muter
    /// à chaque appel est une course silencieuse.
    /// </summary>
    private async Task<(HttpResponseMessage Response, Uri Finale)> SuivreAsync(Uri uri, CancellationToken ct)
    {
        for (var saut = 0; saut <= MaxRedirections; saut++)
        {
            using var requete = new HttpRequestMessage(HttpMethod.Get, uri);
            requete.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (compatible; ORION/1.0)");

            var reponse = await _httpClient.SendAsync(requete, HttpCompletionOption.ResponseHeadersRead, ct);

            var emplacement = reponse.Headers.Location;
            if (!EstRedirection(reponse.StatusCode) || emplacement is null)
            {
                return (reponse, uri);
            }

            // Location peut être relative — la résoudre contre l'URI courante avant de vérifier.
            var suivante = emplacement.IsAbsoluteUri ? emplacement : new Uri(uri, emplacement);
            reponse.Dispose();

            var (validee, raison) = await _perimetre.VerifierAsync(suivante.ToString(), ct);
            if (validee is null)
                throw new InvalidOperationException($"Redirection refusée vers {suivante} — {raison}");

            uri = validee;
        }

        throw new InvalidOperationException($"Trop de redirections (plus de {MaxRedirections}).");
    }

    /// <summary>
    /// ⚠️ Le type est importé en haut du fichier, JAMAIS écrit « System.Net.HttpStatusCode » ici.
    ///
    /// Ce fichier vit dans `Orion.Business.Tools.Internet`. Le compilateur résout un nom en
    /// remontant les namespaces englobants : « System » tombe alors sur
    /// `Orion.Business.Tools.System` — le dossier des outils système — avant d'atteindre le
    /// `System` global. Il cherche donc `Orion.Business.Tools.System.Net`, qui n'existe pas.
    ///
    /// CS0234, et un message qui parle d'une référence d'assembly manquante alors que le
    /// problème est une collision de noms. Les directives `using` en tête de fichier, elles,
    /// sont résolues AVANT la déclaration de namespace : elles n'ont pas ce problème.
    /// </summary>
    private static bool EstRedirection(HttpStatusCode code)
        => (int)code is >= 300 and <= 399;

    private WebFetchResultDto ExtractContent(string html, string url, int maxLength)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Remove script and style elements
        doc.DocumentNode.Descendants()
            .Where(n => n.Name is "script" or "style" or "nav" or "footer" or "header" or "aside")
            .ToList()
            .ForEach(n => n.Remove());

        var title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim() ?? "No title";

        // Try to get main content
        var contentNode = doc.DocumentNode.SelectSingleNode("//article") 
            ?? doc.DocumentNode.SelectSingleNode("//main")
            ?? doc.DocumentNode.SelectSingleNode("//div[@role='main']")
            ?? doc.DocumentNode.SelectSingleNode("//body");

        var content = contentNode?.InnerText ?? "";
        
        // Clean up whitespace
        var sb = new StringBuilder();
        var lines = content.Split('\n', '\r');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed) && trimmed.Length > 2)
            {
                sb.Append(trimmed).Append(' ');
            }
        }

        content = sb.ToString();
        
        // Truncate if too long
        if (content.Length > maxLength)
        {
            content = content[..maxLength] + "... [truncated]";
        }

        return new WebFetchResultDto
        {
            Url = url,
            Title = title,
            Content = content,
            WordCount = content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length
        };
    }
}
