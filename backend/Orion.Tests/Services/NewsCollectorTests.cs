using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Orion.Business.Services;
using Orion.Core.Configuration;

namespace Orion.Tests.Services;

/// <summary>
/// La collecte est la SOURCE du briefing : ce qu'elle rend est presente a l'utilisateur comme
/// une actualite reelle. Deux exigences en decoulent — ne rien inventer, et ne rien perdre en
/// silence. Un flux mort doit se voir, pas retrecir le briefing sans un mot.
/// </summary>
public class NewsCollectorTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Code, byte[] Body)> _routes;

        public FakeHandler(Dictionary<string, (HttpStatusCode, byte[])> routes) => _routes = routes;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            if (!_routes.TryGetValue(url, out var route))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(route.Code)
            {
                Content = new ByteArrayContent(route.Body),
            });
        }
    }

    private static string Rss20(params (string Title, string Link, DateTimeOffset Date)[] items)
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><rss version=\"2.0\"><channel><title>F</title><link>http://f</link><description>d</description>");
        foreach (var i in items)
        {
            sb.Append($"<item><title>{i.Title}</title><link>{i.Link}</link>")
              .Append($"<pubDate>{i.Date.ToString("r")}</pubDate><description>resume</description></item>");
        }
        return sb.Append("</channel></rss>").ToString();
    }

    private static string Atom(string title, string link, DateTimeOffset date) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
        + "<feed xmlns=\"http://www.w3.org/2005/Atom\"><title>F</title><id>urn:f</id>"
        + $"<updated>{date:O}</updated>"
        + $"<entry><title>{title}</title><id>urn:1</id><link href=\"{link}\"/><updated>{date:O}</updated></entry>"
        + "</feed>";

    /// <summary>RSS 1.0, le format que `SyndicationFeed` ne sait PAS lire.</summary>
    private static string Rdf(string title, string link, DateTimeOffset date) =>
        "<?xml version=\"1.0\" encoding=\"ISO-8859-15\"?>"
        + "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\" "
        + "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns=\"http://purl.org/rss/1.0/\">"
        + "<channel rdf:about=\"http://f\"><title>F</title><link>http://f</link><description>d</description></channel>"
        + $"<item rdf:about=\"{link}\"><title>{title}</title><link>{link}</link>"
        + $"<description>resume</description><dc:date>{date:O}</dc:date></item>"
        + "</rdf:RDF>";

    private static NewsCollector Build(NewsOptions options, Dictionary<string, (HttpStatusCode, byte[])> routes)
        => new(Options.Create(options),
               new HttpClient(new FakeHandler(routes)),
               Mock.Of<ILogger<NewsCollector>>());

    private static Dictionary<string, (HttpStatusCode, byte[])> Route(string url, string body, Encoding? enc = null)
        => new() { [url] = (HttpStatusCode.OK, (enc ?? Encoding.UTF8).GetBytes(body)) };

    [Fact]
    public async Task Collect_Rss20Feed_ItemsParsed()
    {
        var options = new NewsOptions
        {
            Feeds = [new NewsFeed { Name = "F", Url = "http://f/rss", Circle = NewsCircle.World }],
        };
        var routes = Route("http://f/rss", Rss20(("Titre A", "http://a", DateTimeOffset.UtcNow)));

        var harvest = await Build(options, routes).CollectAsync();

        Assert.Empty(harvest.FailedFeeds);
        Assert.Single(harvest.Items);
        Assert.Equal("Titre A", harvest.Items[0].Title);
        Assert.Equal("F", harvest.Items[0].Source);
    }

    [Fact]
    public async Task Collect_AtomFeed_ItemsParsed()
    {
        var options = new NewsOptions
        {
            Feeds = [new NewsFeed { Name = "A", Url = "http://a/atom" }],
        };
        var routes = Route("http://a/atom", Atom("Titre Atom", "http://x", DateTimeOffset.UtcNow));

        var harvest = await Build(options, routes).CollectAsync();

        Assert.Single(harvest.Items);
        Assert.Equal("Titre Atom", harvest.Items[0].Title);
    }

    [Fact]
    public async Task Collect_Rss10RdfFeed_FallbackParsesIt()
    {
        // Sans le repli, ce flux tombe en « format non reconnu » et Le Monde Informatique
        // disparait du briefing sans qu'on sache pourquoi.
        var options = new NewsOptions
        {
            Feeds = [new NewsFeed { Name = "RDF", Url = "http://r/rdf" }],
        };
        var routes = Route("http://r/rdf", Rdf("Titre RDF", "http://r/1", DateTimeOffset.UtcNow),
                           Encoding.Latin1);

        var harvest = await Build(options, routes).CollectAsync();

        Assert.Empty(harvest.FailedFeeds);
        Assert.Single(harvest.Items);
        Assert.Equal("Titre RDF", harvest.Items[0].Title);
    }

    [Fact]
    public async Task Collect_DeadFeed_ReportedNotFatal()
    {
        var options = new NewsOptions
        {
            Feeds =
            [
                new NewsFeed { Name = "Vivant", Url = "http://ok/rss" },
                new NewsFeed { Name = "Mort", Url = "http://dead/rss" },
            ],
        };
        var routes = Route("http://ok/rss", Rss20(("Vivant", "http://v", DateTimeOffset.UtcNow)));

        var harvest = await Build(options, routes).CollectAsync();

        // L'un tombe, l'autre passe : le briefing survit, et la panne est VISIBLE.
        Assert.Single(harvest.Items);
        Assert.Single(harvest.FailedFeeds);
        Assert.Contains("Mort", harvest.FailedFeeds[0]);
    }

    [Fact]
    public async Task Collect_ItemOlderThanWindow_Dropped()
    {
        var options = new NewsOptions
        {
            FreshnessHours = 24,
            Feeds = [new NewsFeed { Name = "F", Url = "http://f/rss" }],
        };
        var routes = Route("http://f/rss", Rss20(
            ("Recent", "http://recent", DateTimeOffset.UtcNow.AddHours(-2)),
            ("Vieux", "http://vieux", DateTimeOffset.UtcNow.AddDays(-5))));

        var harvest = await Build(options, routes).CollectAsync();

        Assert.Single(harvest.Items);
        Assert.Equal("Recent", harvest.Items[0].Title);
    }

    [Fact]
    public async Task Collect_SameArticleTwoFeeds_DedupedKeepingClosestCircle()
    {
        // Google News et la source directe rendent le meme article. On garde UNE entree, et
        // celle du cercle le plus proche : le lecteur veut savoir que ca le concerne de pres.
        var now = DateTimeOffset.UtcNow;
        var options = new NewsOptions
        {
            Feeds =
            [
                new NewsFeed { Name = "Monde", Url = "http://w/rss", Circle = NewsCircle.World },
                new NewsFeed { Name = "Local", Url = "http://l/rss", Circle = NewsCircle.Local },
            ],
        };
        var routes = new Dictionary<string, (HttpStatusCode, byte[])>
        {
            ["http://w/rss"] = (HttpStatusCode.OK, Encoding.UTF8.GetBytes(Rss20(("Meme article", "http://news/1?utm_source=x", now)))),
            ["http://l/rss"] = (HttpStatusCode.OK, Encoding.UTF8.GetBytes(Rss20(("Meme article", "http://news/1", now)))),
        };

        var harvest = await Build(options, routes).CollectAsync();

        Assert.Single(harvest.Items);
        Assert.Equal(NewsCircle.Local, harvest.Items[0].Circle);
    }

    [Fact]
    public async Task Collect_ManyItems_CappedPerFeedAndTotal()
    {
        // Une requete Google News rend une centaine d'entrees : sans plafond, le prompt du
        // briefing explose et le cout avec lui.
        var now = DateTimeOffset.UtcNow;
        var items = Enumerable.Range(0, 30)
            .Select(i => ($"Titre {i}", $"http://n/{i}", now.AddMinutes(-i)))
            .ToArray();

        var options = new NewsOptions
        {
            MaxItemsPerFeed = 3,
            MaxItemsTotal = 2,
            Feeds = [new NewsFeed { Name = "F", Url = "http://f/rss" }],
        };
        var routes = Route("http://f/rss", Rss20(items));

        var harvest = await Build(options, routes).CollectAsync();

        Assert.Equal(2, harvest.Items.Count);
    }

    [Fact]
    public async Task Collect_Disabled_ReturnsNothingWithoutCallingAnything()
    {
        var options = new NewsOptions
        {
            Enabled = false,
            Feeds = [new NewsFeed { Name = "F", Url = "http://f/rss" }],
        };

        // Aucune route : si le collecteur appelait quand meme, le flux serait signale en echec.
        var harvest = await Build(options, new Dictionary<string, (HttpStatusCode, byte[])>()).CollectAsync();

        Assert.Empty(harvest.Items);
        Assert.Empty(harvest.FailedFeeds);
    }
}
