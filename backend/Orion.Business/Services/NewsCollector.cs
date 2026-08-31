using System.ServiceModel.Syndication;
using System.Xml;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orion.Core.Configuration;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Services;

namespace Orion.Business.Services;

/// <summary>
/// Collecte les flux de veille. `SyndicationFeed` couvre RSS 2.0 et Atom ; RSS 1.0 (RDF) passe
/// par un repli maison — Le Monde Informatique en sert encore, en ISO-8859-15.
///
/// Rien ici n'appelle le modele : la collecte doit rester verifiable ligne a ligne.
/// </summary>
public class NewsCollector : INewsCollector
{
    static NewsCollector()
    {
        // .NET Core n'embarque QUE l'UTF-8 et le Latin-1. Les flux francais servent encore de
        // l'ISO-8859-15, et XmlReader leve « System does not support 'ISO-8859-15' encoding » —
        // le flux passe alors pour mort. Constate par le banc de test le 2026-09-01.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private readonly NewsOptions _options;
    private readonly HttpClient _http;
    private readonly ILogger<NewsCollector> _logger;

    public NewsCollector(IOptions<NewsOptions> options, HttpClient http, ILogger<NewsCollector> logger)
    {
        _options = options.Value;
        _http = http;
        _logger = logger;

        _http.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        if (!_http.DefaultRequestHeaders.UserAgent.TryParseAdd(_options.UserAgent))
        {
            _logger.LogWarning("[News] User-Agent invalide, valeur par defaut du client conservee");
        }
    }

    public async Task<NewsHarvest> CollectAsync(
        IEnumerable<NewsFeed>? extraFeeds = null, CancellationToken ct = default)
    {
        var harvest = new NewsHarvest();

        var feeds = _options.Feeds.Concat(extraFeeds ?? Enumerable.Empty<NewsFeed>())
            .Where(f => !string.IsNullOrWhiteSpace(f.Url))
            .ToList();

        if (!_options.Enabled || feeds.Count == 0)
        {
            _logger.LogInformation("[News] Collecte desactivee ou aucun flux configure");
            return harvest;
        }

        var cutoff = DateTimeOffset.UtcNow.AddHours(-_options.FreshnessHours);

        // En parallele : une trentaine de flux en serie, a 15 s de delai d'attente chacun,
        // ferait attendre le briefing plusieurs minutes dans le pire cas.
        var tasks = feeds.Select(feed => ReadFeedAsync(feed, cutoff, ct)).ToArray();
        var results = await Task.WhenAll(tasks);

        foreach (var (feed, items, error) in results)
        {
            if (error is not null) { harvest.FailedFeeds.Add($"{feed.Name} : {error}"); continue; }
            harvest.Items.AddRange(items);
        }

        // Deduplication par lien : Google News et une source directe renvoient le meme article.
        var deduped = harvest.Items
            .GroupBy(i => NormalizeLink(i.Link))
            .Select(g => g.OrderBy(i => (int)i.Circle).First())   // on garde le cercle le plus proche
            .OrderBy(i => (int)i.Circle)
            .ThenByDescending(i => i.PublishedAt)
            .Take(_options.MaxItemsTotal)
            .ToList();

        harvest.Items = deduped;

        _logger.LogInformation("[News] {Count} article(s) retenus, {Failed} flux en echec",
            harvest.Items.Count, harvest.FailedFeeds.Count);

        return harvest;
    }

    private async Task<(NewsFeed Feed, List<NewsItem> Items, string? Error)> ReadFeedAsync(
        NewsFeed feed, DateTimeOffset cutoff, CancellationToken ct)
    {
        try
        {
            // OCTETS, pas string : Le Monde Informatique sert de l'ISO-8859-15 et `GetStringAsync`
            // decode selon l'en-tete HTTP, pas selon le prologue XML. Sur un flux XML c'est le
            // prologue qui fait foi — passer le flux brut a XmlReader lui laisse ce choix.
            var bytes = await _http.GetByteArrayAsync(feed.Url, ct);

            var items = ReadSyndication(bytes, feed) ?? ReadRdf(bytes, feed);

            var retenus = items
                .Where(i => i.PublishedAt >= cutoff && !string.IsNullOrWhiteSpace(i.Title))
                .OrderByDescending(i => i.PublishedAt)
                .Take(_options.MaxItemsPerFeed)
                .ToList();

            return (feed, retenus, null);
        }
        catch (Exception ex)
        {
            // Un flux mort ne doit jamais faire tomber le briefing : il est signale, pas fatal.
            _logger.LogWarning("[News] Flux « {Feed} » illisible : {Message}", feed.Name, ex.Message);
            return (feed, new List<NewsItem>(), ex.Message);
        }
    }

    /// <summary>
    /// RSS 2.0 et Atom. Rend <c>null</c> — et non une liste vide — quand le format n'est pas
    /// reconnu : c'est ce qui declenche le repli RDF sans confondre « format inconnu » et
    /// « flux sans article ».
    /// </summary>
    private static List<NewsItem>? ReadSyndication(byte[] bytes, NewsFeed feed)
    {
        try
        {
            using var reader = XmlReader.Create(new MemoryStream(bytes), SafeXmlSettings());
            return SyndicationFeed.Load(reader).Items.Select(i => ToNewsItem(i, feed)).ToList();
        }
        catch (XmlException) { return null; }
        catch (InvalidOperationException) { return null; }   // format non reconnu par le formatteur
    }

    /// <summary>
    /// Repli RSS 1.0 (RDF), que `SyndicationFeed` ne sait pas lire. Toujours repandu sur les
    /// sites francais — Le Monde Informatique en sert.
    /// </summary>
    private static List<NewsItem> ReadRdf(byte[] bytes, NewsFeed feed)
    {
        XNamespace rss = "http://purl.org/rss/1.0/";
        XNamespace dc = "http://purl.org/dc/elements/1.1/";

        using var reader = XmlReader.Create(new MemoryStream(bytes), SafeXmlSettings());
        var doc = XDocument.Load(reader);

        return doc.Descendants(rss + "item").Select(item => new NewsItem
        {
            Title = (string?)item.Element(rss + "title") is { } t ? t.Trim() : string.Empty,
            Link = (string?)item.Element(rss + "link") ?? string.Empty,
            Source = feed.Name,
            Circle = feed.Circle,
            Tags = feed.Tags,
            PublishedAt = DateTimeOffset.TryParse((string?)item.Element(dc + "date"), out var d)
                ? d
                : DateTimeOffset.UtcNow,
            Summary = Shorten((string?)item.Element(rss + "description")),
        }).ToList();
    }

    /// <summary>
    /// Un flux public est une entree NON FIABLE : sans ces deux reglages, une DTD distante ou
    /// recursive suffirait a lire un fichier local ou a saturer la memoire.
    /// </summary>
    private static XmlReaderSettings SafeXmlSettings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
    };

    private static NewsItem ToNewsItem(SyndicationItem item, NewsFeed feed) => new()
    {
        Title = item.Title?.Text?.Trim() ?? string.Empty,
        Link = item.Links.FirstOrDefault()?.Uri?.ToString() ?? string.Empty,
        Source = feed.Name,
        Circle = feed.Circle,
        Tags = feed.Tags,

        // Certains flux ne remplissent que LastUpdatedTime, d'autres laissent les deux a
        // MinValue : dans ce dernier cas l'article serait rejete par la fraicheur, donc on le
        // considere comme arrivant maintenant plutot que de le perdre.
        PublishedAt = item.PublishDate > DateTimeOffset.MinValue ? item.PublishDate
                    : item.LastUpdatedTime > DateTimeOffset.MinValue ? item.LastUpdatedTime
                    : DateTimeOffset.UtcNow,

        Summary = Shorten(item.Summary?.Text),
    };

    /// <summary>Le resume d'un flux contient souvent du HTML complet : on le borne avant le prompt.</summary>
    private static string Shorten(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var plain = System.Text.RegularExpressions.Regex.Replace(text, "<.*?>", " ");
        plain = System.Net.WebUtility.HtmlDecode(plain);
        plain = System.Text.RegularExpressions.Regex.Replace(plain, @"\s+", " ").Trim();
        return plain.Length <= 280 ? plain : plain[..280] + "…";
    }

    /// <summary>Le fragment et les parametres de suivi font passer le meme article pour deux.</summary>
    private static string NormalizeLink(string link)
    {
        if (string.IsNullOrWhiteSpace(link)) return Guid.NewGuid().ToString();
        var cut = link.Split('#')[0];
        var q = cut.IndexOf('?');
        return (q >= 0 ? cut[..q] : cut).TrimEnd('/').ToLowerInvariant();
    }
}
