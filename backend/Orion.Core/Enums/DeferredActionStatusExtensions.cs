namespace Orion.Core.Enums;

/// <summary>
/// Traduction canonique statut ↔ texte. Un seul endroit, parce qu'elle sert à trois :
/// la colonne `status`, la contrainte CHECK de la migration 004, et le JSON rendu à l'UI.
/// Trois orthographes divergentes du même état seraient trois bugs en attente.
/// </summary>
public static class DeferredActionStatusExtensions
{
    public static string ToSlug(this DeferredActionStatus status) => status switch
    {
        DeferredActionStatus.Pending => "pending",
        DeferredActionStatus.AwaitingConfirmation => "awaiting_confirmation",
        DeferredActionStatus.Executed => "executed",
        DeferredActionStatus.Failed => "failed",
        DeferredActionStatus.Expired => "expired",
        DeferredActionStatus.Cancelled => "cancelled",
        _ => "pending"
    };

    public static DeferredActionStatus FromSlug(string? slug) => slug switch
    {
        "awaiting_confirmation" => DeferredActionStatus.AwaitingConfirmation,
        "executed" => DeferredActionStatus.Executed,
        "failed" => DeferredActionStatus.Failed,
        "expired" => DeferredActionStatus.Expired,
        "cancelled" => DeferredActionStatus.Cancelled,
        _ => DeferredActionStatus.Pending
    };
}
