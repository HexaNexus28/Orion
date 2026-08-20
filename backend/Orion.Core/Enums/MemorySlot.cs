namespace Orion.Core.Enums;

/// <summary>
/// Schéma FERMÉ de la mémoire durable d'ORION. Quatre emplacements, jamais un cinquième.
///
/// C'est le garde-fou qui distingue une mémoire d'un dépotoir. Une IA qui écrit librement
/// produit en un mois des dizaines de souvenirs contradictoires que plus personne ne relit :
/// la mémoire devient du bruit, et on cesse de la consulter. Le schéma fermé force le tri
/// au moment de l'écriture, pas à la lecture.
///
/// Test d'affectation : « est-ce encore vrai dans six mois ? »
/// oui → Rules / Decisions / Refs · non → State.
/// </summary>
public enum MemorySlot
{
    /// <summary>Brut : un échange non encore distillé. Matière première de la consolidation.</summary>
    Episode,

    /// <summary>Comment ORION doit se comporter — corrections reçues, posture attendue. APPEND.</summary>
    Rules,

    /// <summary>Décisions durables et leur POURQUOI, datées. APPEND.</summary>
    Decisions,

    /// <summary>Ce qui est en cours et se périmera. OVERWRITE : le neuf remplace l'ancien.</summary>
    State,

    /// <summary>Pointeurs stables : chemins, ports, URLs, identifiants. APPEND.</summary>
    Refs
}
