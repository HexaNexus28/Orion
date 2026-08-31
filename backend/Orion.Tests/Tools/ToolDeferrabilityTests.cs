using Orion.Core.Interfaces.Tools;

namespace Orion.Tests.Tools;

/// <summary>
/// Quels outils gardent un sens exécutés PLUS TARD.
///
/// Ce n'est pas une question technique mais un jugement produit, et il se relit : la liste est
/// donc écrite en dur ici. Tout nouvel outil qui exige le daemon fait rougir ce test tant que
/// quelqu'un n'a pas tranché son cas — c'est voulu, l'oubli par défaut serait « différable ».
/// </summary>
public class ToolDeferrabilityTests
{
    /// <summary>
    /// Ceux qui agissent, et dont l'effet voulu est le même demain matin qu'à 22 h.
    /// Tout le reste — les LECTURES — n'a aucune valeur différée : répondre demain sur l'état
    /// d'hier n'est pas rendre service, et encombrerait la file pour rien.
    /// </summary>
    private static readonly HashSet<string> Differables = new()
    {
        "open_app",
        "open_browser_url",
        "git_commit",
        "write_file",
    };

    [Theory]
    [MemberData(nameof(ToolSchemaTests.TousLesOutils), MemberType = typeof(ToolSchemaTests))]
    public void Deferrable_List_MatchesDecidedSet(string name, ITool tool)
    {
        Assert.Equal(Differables.Contains(name), tool.IsDeferrable);
    }

    [Theory]
    [MemberData(nameof(ToolSchemaTests.TousLesOutils), MemberType = typeof(ToolSchemaTests))]
    public void Deferrable_Tool_AlwaysRequiresDaemon(string name, ITool tool)
    {
        // Différer n'a de sens que pour ce qui attend le PC. Un outil qui tourne côté serveur
        // et se déclare différable serait un aveu de confusion : rien ne l'empêche de partir.
        if (tool.IsDeferrable)
        {
            Assert.True(tool.RequiresDaemon, $"'{name}' se dit différable sans dépendre du PC.");
        }
    }

    /// <summary>
    /// Le trou trouvé à l'essai réel du 2026-08-21 : `list_files` étant retiré du catalogue,
    /// le modèle a substitué `run_script` avec un `Get-ChildItem`. La lecture refusée par la
    /// porte est repassée par la fenêtre, et l'utilisateur s'est vu promettre pour le lendemain
    /// une réponse qu'il voulait immédiatement.
    ///
    /// Un script est arbitraire : impossible de savoir s'il lit ou s'il écrit, donc impossible
    /// de savoir si le différer garde un sens. On ne diffère pas ce qu'on ne comprend pas.
    /// </summary>
    [Fact]
    public void RunScript_NotDeferrable_ClosesReadLoophole()
    {
        Assert.DoesNotContain("run_script", Differables);
    }
}
