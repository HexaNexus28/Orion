namespace Orion.Core.Enums;

/// <summary>
/// Cycle de vie d'une action mise en file parce que le PC était éteint.
///
/// Deux états seulement sont « vivants » (<see cref="Pending"/>, <see cref="AwaitingConfirmation"/>) :
/// ce sont les seuls que le drain relit, et les seuls que l'utilisateur peut annuler.
/// </summary>
public enum DeferredActionStatus
{
    /// <summary>Enfilée, sera exécutée dès que le daemon revient.</summary>
    Pending,

    /// <summary>
    /// Action destructive : le daemon est revenu, mais elle ne se rejoue pas toute seule.
    /// ORION redemande, en montrant l'état RÉEL de la machine au réveil — pas celui d'hier soir.
    /// </summary>
    AwaitingConfirmation,

    Executed,
    Failed,

    /// <summary>TTL dépassé. Une demande d'hier soir exécutée trois jours plus tard est une surprise.</summary>
    Expired,

    /// <summary>Annulée par l'utilisateur depuis la file.</summary>
    Cancelled
}
