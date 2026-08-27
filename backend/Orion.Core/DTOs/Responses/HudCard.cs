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
/// Gravite, PAS une couleur.
///
/// Le backend n a aucune raison de connaitre la charte graphique : s il envoyait « #f59e0b »,
/// changer le theme obligerait a modifier des outils metier. Il dit ce qui SE PASSE, le front
/// decide a quoi ca ressemble.
/// </summary>
public enum HudCardState { Neutral, Ok, Warn, Critical }

/// <summary>
/// Duree de vie a l ecran. C est ce qui separe un widget d une notification.
///
/// Sans cette distinction, tout se valait : une carte produite par un outil et un panneau
/// permanent occupaient le meme espace et disparaissaient ensemble au message suivant. Un HUD
/// n est pas un flux — certaines choses doivent RESTER.
/// </summary>
public enum HudCardLifetime
{
    /// <summary>Widget permanent : contexte de travail, depots, echeances. Se rafraichit, ne disparait pas.</summary>
    Pinned,

    /// <summary>Le tour en cours : outil qui tourne, sources consultees. S efface ensuite.</summary>
    Live,

    /// <summary>Signal ponctuel qui demande l attention, puis s efface.</summary>
    Event,
}

/// <summary>
/// Un geste proposé sur une carte : « je corrige ? », « je commit ? ».
///
/// UNE ACTION EST UN APPEL D OUTIL, rien d autre. Pas un second mécanisme à sécuriser : elle
/// emprunte le chemin du modèle — même authentification, même ToolInvoker, donc même garde-fou
/// de confirmation sur les outils irréversibles. Un bouton « commit » sur une carte se retrouve
/// dans la file d attente exactement comme si le modèle l avait demandé.
///
/// C est ce qui fait passer le HUD du tableau de bord au cockpit : il n affiche plus un état,
/// il propose le geste suivant.
/// </summary>
public class HudCardAction
{
    /// <summary>Ce que lit l utilisateur : « Corriger », « Commit », « Relancer les tests ».</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Nom de l outil à appeler, tel qu il est enregistré dans le registre.</summary>
    public string Tool { get; set; } = string.Empty;

    /// <summary>Arguments JSON, sérialisés. Vide = aucun argument.</summary>
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
/// Carte du HUD, produite par un OUTIL a partir de son resultat reel.
///
/// POURQUOI CE CONTRAT EXISTE. Avant, le front fabriquait ses cartes en passant des expressions
/// regulieres sur la PROSE de la reponse : toute statistique en gras devenait une carte, par
/// accident, et rien n apparaissait quand ca comptait. Une carte est desormais la consequence
/// d une action reellement executee — deterministe, et reproductible.
///
/// L identifiant est STABLE et porte le sujet (« git.ShiftStar », « system.host »), pas le
/// moment. Rappeler le meme outil MET A JOUR la carte au lieu d en empiler une seconde. C est
/// aussi ce qui rendra possible les panneaux permanents : une carte rafraichie par le daemon
/// remplacera simplement celle qui porte le meme identifiant.
/// </summary>
public class HudCard
{
    public string Id { get; set; } = string.Empty;
    public HudCardKind Kind { get; set; }
    public string Label { get; set; } = string.Empty;

    /// <summary>Valeur principale (Metric, Status). Deja formatee pour l affichage.</summary>
    public string? Value { get; set; }

    /// <summary>Unite affichee a cote de la valeur (« Mo », « min »).</summary>
    public string? Unit { get; set; }

    public HudCardState State { get; set; } = HudCardState.Neutral;

    public List<HudCardItem>? Items { get; set; }

    /// <summary>Gestes proposés. Absent = la carte est en lecture seule.</summary>
    public List<HudCardAction>? Actions { get; set; }

    /// <summary>Par defaut Live : une carte ne devient permanente que si elle le declare.</summary>
    public HudCardLifetime Lifetime { get; set; } = HudCardLifetime.Live;

    public DateTime ProducedAt { get; set; } = DateTime.UtcNow;
}