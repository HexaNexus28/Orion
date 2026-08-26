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

    public DateTime ProducedAt { get; set; } = DateTime.UtcNow;
}