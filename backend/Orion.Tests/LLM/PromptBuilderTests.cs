using System.Text.Json.Nodes;
using Orion.Business.LLM;
using Orion.Core.DTOs.Responses;
using Orion.Core.Entities;
using Orion.Core.Enums;
using Orion.Core.Interfaces.Tools;

namespace Orion.Tests.LLM;

/// <summary>
/// Le prompt système est déterministe : il se teste exactement.
/// Ces tests verrouillent les trois défauts documentés en §1.7 de docs/jarvis-gap-analysis.md —
/// outils jamais listés, aucune consigne d'abstention, aucune distinction lire/écrire.
/// </summary>
public class PromptBuilderTests
{
    private sealed class FakeTool : ITool
    {
        public FakeTool(
            string name,
            string description,
            bool requiresDaemon = false,
            bool destructive = false,
            bool deferrable = false)
        {
            Name = name;
            Description = description;
            RequiresDaemon = requiresDaemon;
            IsDestructive = destructive;
            IsDeferrable = deferrable;
        }

        public string Name { get; }
        public string Description { get; }
        public bool RequiresDaemon { get; }
        public bool IsDestructive { get; }
        public bool IsDeferrable { get; }
        public JsonObject InputSchema => new() { ["type"] = "object" };

        public Task<ApiResponse<ToolResult>> ExecuteAsync(JsonObject input, CancellationToken ct = default)
            => Task.FromResult(ApiResponse<ToolResult>.SuccessResponse(ToolResult.SuccessResult()));
    }

    private static readonly IReadOnlyList<ITool> Tools = new List<ITool>
    {
        new FakeTool("web_search", "Cherche sur le web"),
        new FakeTool("open_app", "Ouvre une application", requiresDaemon: true, deferrable: true),
        new FakeTool("run_script", "Execute un script PowerShell", requiresDaemon: true, destructive: true, deferrable: true),
        new FakeTool("list_files", "Liste un dossier", requiresDaemon: true),
    };

    private static string Build(
        IReadOnlyList<ITool>? tools = null,
        bool daemonConnected = true,
        bool voiceMode = false,
        Dictionary<string, string>? profile = null,
        List<MemoryVector>? memories = null)
        => new PromptBuilder().BuildSystemPrompt(
            profile ?? new Dictionary<string, string> { ["name"] = "Alex" },
            memories ?? new List<MemoryVector>(),
            tools ?? Tools,
            daemonConnected,
            LLMProvider.Ollama,
            voiceMode);

    [Fact]
    public void Les_outils_sont_listes_avec_leur_description()
    {
        // Régression §1.7 : la liste était toujours vide, la section ne s'affichait jamais.
        var prompt = Build();

        Assert.Contains("web_search : Cherche sur le web", prompt);
        Assert.Contains("open_app : Ouvre une application", prompt);
        Assert.Contains("run_script : Execute un script PowerShell", prompt);
    }

    [Fact]
    public void Le_prompt_dit_explicitement_quand_NE_PAS_utiliser_d_outil()
    {
        // Sans cette consigne, un petit modèle sur-déclenche : llama3.2:3b appelait open_app
        // pour « dis bonjour ».
        var prompt = Build();

        Assert.Contains("Quand NE PAS utiliser d'outil", prompt);
        Assert.Contains("salutation", prompt);
        Assert.Contains("SANS aucun outil", prompt);
    }

    [Fact]
    public void Le_prompt_enseigne_le_chainage()
    {
        var prompt = Build();

        Assert.Contains("Enchaîner plusieurs outils", prompt);
        Assert.Contains("le résultat de l'un nourrit le suivant", prompt);
    }

    [Fact]
    public void Les_outils_destructifs_sont_nommes_et_encadres()
    {
        var prompt = Build();

        Assert.Contains("Outils qui modifient l'état", prompt);
        Assert.Contains("run_script", prompt);
        Assert.Contains("demande EXPLICITE", prompt);
        Assert.Contains("[PC requis, modifie l'état]", prompt);
    }

    [Fact]
    public void Sans_outil_destructif_la_section_prudence_disparait()
    {
        var prompt = Build(tools: new List<ITool> { new FakeTool("web_search", "Cherche") });

        Assert.DoesNotContain("Outils qui modifient l'état", prompt);
    }

    [Fact]
    public void Daemon_hors_ligne_le_prompt_previent_au_lieu_de_laisser_echouer()
    {
        var prompt = Build(daemonConnected: false);

        Assert.Contains("LE PC DE L'UTILISATEUR EST ÉTEINT", prompt);
        Assert.Contains("PC de l'utilisateur joignable : NON", prompt);
        Assert.Contains("Ne prétends jamais que c'est déjà fait.", prompt);
    }

    /// <summary>
    /// Le défaut que ce test empêche : le modèle voit un outil au catalogue, l'appelle, reçoit
    /// « mis en file » et l'annonce comme un échec. La file existerait alors sans que
    /// l'utilisateur en profite jamais.
    /// </summary>
    [Fact]
    public void Daemon_hors_ligne_le_prompt_dit_que_differer_n_est_pas_echouer()
    {
        var prompt = Build(daemonConnected: false);

        Assert.Contains("MIS EN FILE", prompt);
        Assert.Contains("Ce n'est PAS un échec", prompt);
        Assert.Contains("open_app, run_script", prompt);
        Assert.Contains("[PC éteint — sera différé]", prompt);
    }

    /// <summary>
    /// L'autre moitié de la règle : une LECTURE ne se diffère pas. Répondre demain matin sur
    /// l'état d'hier soir n'est pas rendre service, et encombrerait la file pour rien.
    /// </summary>
    [Fact]
    public void Daemon_hors_ligne_les_lectures_ne_sont_pas_annoncees_comme_differables()
    {
        var prompt = Build(daemonConnected: false);

        var differables = prompt[prompt.IndexOf("En revanche, ceux-ci restent utilisables")..];
        Assert.DoesNotContain("list_files", differables[..200]);
    }

    [Fact]
    public void Daemon_connecte_pas_d_avertissement_parasite()
    {
        var prompt = Build(daemonConnected: true);

        Assert.DoesNotContain("EST ÉTEINT", prompt);
        Assert.Contains("PC de l'utilisateur joignable : oui", prompt);
    }

    [Fact]
    public void Aucun_outil_enregistre_le_prompt_le_dit_honnetement()
    {
        var prompt = Build(tools: new List<ITool>());

        Assert.Contains("Aucun outil n'est disponible", prompt);
        Assert.DoesNotContain("Quand NE PAS utiliser d'outil", prompt);
    }

    [Fact]
    public void Mode_voix_interdit_le_markdown_et_reclame_des_phrases_courtes()
    {
        var prompt = Build(voiceMode: true);

        Assert.Contains("MODE VOIX ACTIF", prompt);
        Assert.Contains("AUCUN markdown", prompt);
        Assert.Contains("Phrases COURTES", prompt);
        // Un silence pendant un outil fait croire à un plantage quand on n'a pas l'écran.
        Assert.Contains("un silence lui fait croire que tu as planté", prompt);
    }

    [Fact]
    public void Mode_texte_autorise_le_formatage()
    {
        var prompt = Build(voiceMode: false);

        Assert.DoesNotContain("MODE VOIX ACTIF", prompt);
        Assert.Contains("**Label** : valeur", prompt);
    }

    [Fact]
    public void Le_profil_et_les_souvenirs_sont_injectes()
    {
        var prompt = Build(
            profile: new Dictionary<string, string> { ["name"] = "Alex", ["role"] = "Fondateur" },
            memories: new List<MemoryVector> { new() { Content = "Prefere les reponses courtes" } });

        Assert.Contains("assistant IA personnel de Alex", prompt);
        Assert.Contains("role : Fondateur", prompt);
        Assert.Contains("CE DONT TU TE SOUVIENS", prompt);
        Assert.Contains("Prefere les reponses courtes", prompt);
    }

    [Fact]
    public void Sans_souvenir_la_section_memoire_disparait()
    {
        var prompt = Build(memories: new List<MemoryVector>());

        Assert.DoesNotContain("CE DONT TU TE SOUVIENS", prompt);
    }

    [Fact]
    public void La_doctrine_de_memoire_dit_quand_retenir_ET_quand_s_abstenir()
    {
        var prompt = Build(tools: new List<ITool>
        {
            new FakeTool("memory_save", "Enregistre un souvenir"),
            new FakeTool("profile_update", "Met a jour le profil"),
        });

        Assert.Contains("Se souvenir", prompt);
        Assert.Contains("fait DURABLE", prompt);
        // Les deux moitiés comptent : sans le « quand s'abstenir », la mémoire devient du bruit.
        Assert.Contains("N'enregistre RIEN", prompt);
        Assert.Contains("AUTONOME", prompt);
        Assert.Contains("profile_update", prompt);
    }

    [Fact]
    public void Sans_outil_memoire_la_doctrine_disparait()
    {
        var prompt = Build(tools: new List<ITool> { new FakeTool("web_search", "Cherche") });

        Assert.DoesNotContain("Se souvenir", prompt);
    }

    [Fact]
    public void Le_prompt_interdit_d_inventer_des_arguments_et_de_mentir()
    {
        var prompt = Build();

        Assert.Contains("N'INVENTE JAMAIS un argument", prompt);
        Assert.Contains("Ne prétends JAMAIS avoir fait quelque chose que tu n'as pas fait", prompt);
    }
}
