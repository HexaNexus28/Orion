namespace Orion.Core.Configuration;

/// <summary>
/// Cercle de proximite d'une source. Sert de poids au classement : a fraicheur egale, une source
/// proche passe avant une source lointaine.
/// </summary>
public enum NewsCircle
{
    /// <summary>Sources les plus proches de l'utilisateur.</summary>
    Local = 0,

    /// <summary>Sources regionales.</summary>
    Regional = 1,

    /// <summary>Le reste.</summary>
    World = 2,
}

public class NewsFeed
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public NewsCircle Circle { get; set; } = NewsCircle.World;

    /// <summary>Etiquettes libres (« ia », « securite », « emploi ») reprises dans le briefing.</summary>
    public string[] Tags { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Veille par flux RSS/Atom. La collecte ne passe PAS par le modele : un LLM a qui on demande
/// « quoi de neuf » invente, et une actualite inventee vaut moins que pas d'actualite du tout.
/// Le modele ne trie et ne resume que ce que les flux ont reellement rendu.
/// </summary>
public class NewsOptions
{
    public const string SectionName = "News";

    public bool Enabled { get; set; } = true;

    /// <summary>Au-dela, un article n'est plus une nouvelle.</summary>
    public int FreshnessHours { get; set; } = 30;

    /// <summary>
    /// Plafond par flux. Une requete Google News rend une centaine d'entrees : sans plafond, le
    /// prompt du briefing exploserait et le cout avec lui.
    /// </summary>
    public int MaxItemsPerFeed { get; set; } = 8;

    /// <summary>Plafond global soumis au modele, tous flux confondus.</summary>
    public int MaxItemsTotal { get; set; } = 40;

    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// Duree de vie de la recolte en memoire. Le briefing est regenere a CHAQUE ouverture de
    /// l'overlay : sans cache, ouvrir deux fois interrogeait onze flux deux fois.
    /// </summary>
    public int CacheMinutes { get; set; } = 30;

    /// <summary>
    /// Requetes composees a l'execution a partir de ce qu'ORION sait de l'utilisateur, en plus
    /// des flux fixes. Sans elles la veille est un marque-page : elle ne suit ni les projets en
    /// cours ni ce qui vient d'apparaitre.
    ///
    /// Le modele genere les REQUETES, jamais les articles : chaque requete devient un vrai flux
    /// interroge, et seul ce qu'il rend entre dans le briefing.
    /// </summary>
    public bool DynamicQueriesEnabled { get; set; } = true;

    public int MaxDynamicQueries { get; set; } = 4;

    /// <summary>
    /// Google News rend n'importe quelle recherche sous forme de flux RSS : c'est ce qui permet
    /// a une requete generee de rejoindre exactement le meme tuyau que les flux fixes.
    /// <c>{query}</c> est remplace par la requete encodee.
    /// </summary>
    public string DynamicFeedUrlTemplate { get; set; } =
        "https://news.google.com/rss/search?q={query}&hl=fr&gl=FR&ceid=FR:fr";

    /// <summary>
    /// Certains serveurs repondent 403 a un client sans navigateur.
    /// </summary>
    public string UserAgent { get; set; } = "OrionNewsBot/1.0";

    /// <summary>
    /// Liste par DEFAUT, dans le code et non dans appsettings.json — ce fichier est gitignore,
    /// donc absent de l'image Docker : la veille y serait vide en production sans aucun message.
    /// La configuration peut surcharger cette liste, elle n'a pas besoin de la fournir.
    ///
    /// </summary>
    public List<NewsFeed> Feeds { get; set; } = new()
    {
        // Cercle local
        new() { Name = "Le Monde Informatique", Circle = NewsCircle.Local, Tags = ["it", "entreprises"],
                Url = "https://www.lemondeinformatique.fr/flux-rss/thematique/toutes-les-actualites/rss.xml" },
        new() { Name = "Maddyness", Circle = NewsCircle.Local, Tags = ["startups", "levees"],
                Url = "https://www.maddyness.com/feed/" },
        new() { Name = "Les Numeriques", Circle = NewsCircle.Local, Tags = ["tech", "materiel"],
                Url = "https://www.lesnumeriques.com/rss.xml" },

        // Cercle mondial
        new() { Name = "Hacker News", Circle = NewsCircle.World, Tags = ["code", "ingenierie"],
                Url = "https://hnrss.org/frontpage" },
        new() { Name = "Ars Technica", Circle = NewsCircle.World, Tags = ["tech", "entreprises"],
                Url = "https://feeds.arstechnica.com/arstechnica/index" },
        new() { Name = "arXiv cs.AI", Circle = NewsCircle.World, Tags = ["ia", "recherche"],
                Url = "http://export.arxiv.org/rss/cs.AI" },
        new() { Name = "arXiv cs.LG", Circle = NewsCircle.World, Tags = ["ia", "apprentissage"],
                Url = "http://export.arxiv.org/rss/cs.LG" },
    };
}
