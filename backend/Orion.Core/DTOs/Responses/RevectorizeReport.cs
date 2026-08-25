namespace Orion.Core.DTOs.Responses;

/// <summary>
/// Bilan d'une revectorisation. `Total` est le nombre de lignes qui ETAIENT a revectoriser :
/// c'est la reponse a « combien y en avait-il ? », question qu'on ne pouvait pas trancher avant
/// de lancer l'operation.
/// </summary>
public class RevectorizeReport
{
    public string Model { get; set; } = string.Empty;
    public int Dimensions { get; set; }
    public int Total { get; set; }
    public int Done { get; set; }
    public int Failed { get; set; }
    public int Remaining { get; set; }
    public double DurationSeconds { get; set; }
}
