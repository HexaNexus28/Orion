namespace Orion.Core.Configuration;

/// <summary>
/// Transcription distante — Voxtral (Mistral), compatible OpenAI /audio/transcriptions.
///
/// POURQUOI. Whisper small tournait EN LOCAL sur le VPS : 5,0 s pour 4,6 s de parole, meme
/// apres avoir quadruple les ressources du conteneur (il etait plafonne a 1,5 coeur et 1 Go,
/// sature en permanence). Un assistant vocal qu on attend cinq secondes ne sert pas.
///
/// Mesure du 2026-08-27, meme audio, meme phrase :
///   Whisper small local  5,00 s   « donne-moi l etat de mon postesse il te plait »
///   Voxtral mini distant 0,35 s   « donne-moi l etat de mon poste s il te plait »  (exact)
///
/// Quatorze fois plus rapide ET plus fidele. Aucun compte supplementaire : la cle Mistral sert
/// deja aux embeddings de la memoire. Quotas releves : 3600 secondes d audio par minute, soit
/// soixante fois le temps reel — hors d atteinte pour un usage personnel.
/// </summary>
public class TranscriptionOptions
{
    public const string SectionName = "Transcription";

    public string BaseUrl { get; set; } = "https://api.mistral.ai/v1";

    /// <summary>
    /// Vide par defaut : le demarrage retombe alors sur Embedding:ApiKey, meme fournisseur et
    /// meme compte. Dupliquer le secret dans le coffre creerait deux valeurs a faire tourner
    /// ensemble — et une seule finirait par l etre.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "voxtral-mini-latest";

    /// <summary>
    /// 15 s : au-dela, le repli local est de toute facon plus rapide que d attendre. Mesure
    /// habituelle 0,35 s, donc cette borne ne se declenche que sur une vraie panne reseau.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>Permet de revenir au tout-local sans redeployer, en cas de souci fournisseur.</summary>
    public bool Enabled { get; set; } = true;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}