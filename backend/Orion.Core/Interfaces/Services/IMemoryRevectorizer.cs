using Orion.Core.DTOs.Responses;

namespace Orion.Core.Interfaces.Services;

/// <summary>
/// Recalcule les vecteurs des souvenirs avec le modele d'embedding COURANT.
///
/// Necessaire des qu'on change de fournisseur : chaque modele projette dans son propre espace,
/// et comparer des vecteurs de deux espaces differents renvoie des resultats absurdes SANS lever
/// d'erreur. C'est aussi le plan de reprise si un fournisseur disparait — la panne devient une
/// operation de quelques dizaines de minutes au lieu d'une crise.
/// </summary>
public interface IMemoryRevectorizer
{
    /// <param name="maxRows">Limite de securite pour un premier essai. null = tout traiter.</param>
    Task<ApiResponse<RevectorizeReport>> RunAsync(int? maxRows = null, CancellationToken ct = default);
}
