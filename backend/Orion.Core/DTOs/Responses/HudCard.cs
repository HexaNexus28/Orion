namespace Orion.Core.DTOs.Responses;

/// <summary>Forme de la carte. Le front choisit le composant a partir de ca.</summary>
public enum HudCardKind
{
    /// <summary>Une valeur unique et sa legende (memoire, duree, compteur).</summary>
    Metric,

    /// <summary>Un etat, eventuellement detaille par une liste de lignes.</summary>
    Status,

    /// <summary>Une liste d elements sans valeur numerique.</summary>
    List,

    /// <summary>Des sources externes, chacune ouvrable.</summary>
    Sources
}

/// <summary>
/// Gravite, PAS une couleur : le backend dit ce qui se passe, le front decide de l apparence.
/// Envoyer « #f59e0b » ferait dependre les outils metier de la charte graphique.
/// </summary>
public enum HudCardState { Neutral, Ok, Warn, Critical }

/// <summary>Duree de vie a l ecran — c est ce qui separe un widget d une notification.</summary>
public enum HudCardLifetime
{
    /// <summary>Widget permanent : se rafraichit, ne disparait pas.</summary>
    Pinned,

    /// <summary>Le tour en cours : outil qui tourne, sources consultees. S efface ensuite.</summary>
    Live,

    /// <summary>Signal ponctuel qui demande l attention, puis s efface.</summary>
    Event,
}

/// <summary>
/// Un geste propose sur une carte. UNE ACTION EST UN APPEL D OUTIL, rien d autre : meme
/// authentification, meme ToolInvoker, donc meme file de confirmation sur l irreversible.
/// Ce n est pas un second mecanisme a securiser.
/// </summary>
public class HudCardAction
{
    /// <summary>Ce que lit l utilisateur : « Corriger », « Commit ».</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Nom de l outil, tel qu il est enregistre dans le registre.</summary>
    public string Tool { get; set; } = string.Empty;

    /// <summary>Arguments JSON serialises. Vide = aucun argument.</summary>
    public string? Arguments { get; set; }
}

/// <summary>Une ligne dans une carte Status, List ou Sources.</summary>
public class HudCardItem
{
    public string Label { get; set; } = string.Empty;
    public string? Value { get; set; }

    /// <summary>Lien externe, s il y en a un. Le front en fait un element ouvrable.</summary>
    public string? Url { get; set; }
}

/// <summary>
/// Carte du HUD, produite par un OUTIL a partir de son resultat reel — donc deterministe.
///
/// L identifiant est STABLE et porte le sujet (« git.ShiftStar »), jamais le moment : rappeler
/// le meme outil MET A JOUR la carte au lieu d en empiler une seconde.
/// </summary>
public class HudCard
{
    public string Id { get; set; } = string.Empty;
    public HudCardKind Kind { get; set; }
    public string Label { get; set; } = string.Empty;

    /// <summary>Valeur principale, deja formatee pour l affichage.</summary>
    public string? Value { get; set; }

    /// <summary>Unite affichee a cote de la valeur (« Mo », « min »).</summary>
    public string? Unit { get; set; }

    public HudCardState State { get; set; } = HudCardState.Neutral;

    public List<HudCardItem>? Items { get; set; }

    /// <summary>Gestes proposes. Absent = carte en lecture seule.</summary>
    public List<HudCardAction>? Actions { get; set; }

    /// <summary>Par defaut Live : une carte ne devient permanente que si elle le declare.</summary>
    public HudCardLifetime Lifetime { get; set; } = HudCardLifetime.Live;

    public DateTime ProducedAt { get; set; } = DateTime.UtcNow;
}
