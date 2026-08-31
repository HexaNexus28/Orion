namespace Orion.Daemon.Core.Configuration;

public class DaemonOptions
{
    public string RenderWsUrl { get; set; } = "wss://orion-api.onrender.com/daemon";
    public string Token { get; set; } = "";
    public string MachineName { get; set; } = Environment.MachineName;
    public int ReconnectDelayMs { get; set; } = 5000;
    public int MaxReconnectDelayMs { get; set; } = 60000;
    public double ReconnectMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Les dossiers qu'ORION a le droit de LIRE (`read_file`, `list_files`).
    ///
    /// VIDE = RIEN N'EST AUTORISÉ. Ce n'est pas une panne, c'est le défaut voulu : avant, cette
    /// classe était injectée dans les actions de fichier et n'y était jamais lue — tout le disque
    /// était accessible, et `read_file` n'étant pas destructif, sans la moindre confirmation.
    ///
    /// Ces deux actions sont atteignables par injection de prompt (ORION lit le web), et ce
    /// qu'elles lisent repart dans la réponse du modèle. Déclarer une racine, c'est décider ce
    /// qui peut sortir de la machine.
    ///
    /// À déclarer étroit : les dépôts de code et les documents de travail, pas `C:\Users\&lt;toi&gt;`
    /// — le profil contient `.ssh`, les bases de cookies et les jetons.
    /// </summary>
    public string[] AllowedRoots { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Les dossiers où ORION a le droit d'ÉCRIRE (`write_file`).
    ///
    /// VIDE = on retombe sur <see cref="AllowedRoots"/>. Le repli va vers un ensemble PLUS
    /// PETIT ou égal, jamais vers « tout » : les deux listes vides refusent tout.
    ///
    /// Cette clé existe parce que lire et écrire ne sont pas la même permission. Écrire là où
    /// on lit est souvent trop large : le dossier Démarrage suffit à obtenir une persistance
    /// complète, et c'est justement par là que le daemon lui-même est lancé.
    /// </summary>
    public string[] AllowedWriteRoots { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Plafond d'exécution d'un `run_script`, en secondes.
    ///
    /// Sans lui, un script qui ne rend jamais la main bloque l'action indéfiniment. Le daemon
    /// traite les commandes une par une : un seul script suspendu suffit à rendre ORION muet
    /// sur tout le reste, sans le moindre message.
    /// </summary>
    public int ScriptTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Noms refusés même SOUS une racine autorisée. Vide = la liste par défaut de
    /// <see cref="Orion.Daemon.Core.Security.PathScope.DefaultDeniedNames"/> s'applique — un dépôt
    /// de code légitime contient presque toujours un `.env`.
    /// </summary>
    public string[] DeniedNames { get; set; } = Array.Empty<string>();
}
