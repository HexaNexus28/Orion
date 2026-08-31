using Orion.Daemon.Core.Security;

namespace Orion.Daemon.Tests;

/// <summary>
/// Le périmètre des lectures disque — constat C1 de l'audit du 2026-08-27.
///
/// Ces tests portent sur les CONTOURNEMENTS, pas sur le cas passant : `..\..\`, le préfixe de
/// chaîne, la casse, le nom sensible enfoui. C'est là que ce genre de garde se fait avoir, et
/// c'est ce qui distingue un vrai périmètre d'une comparaison de chaînes.
///
/// Les chemins sont construits avec Path.Combine plutôt qu'écrits en dur : le daemon vise
/// Windows, mais ces tests doivent tourner partout où passe la CI.
/// </summary>
public class PathScopeTests
{
    private static string Racine(string nom)
        => Path.Combine(Path.GetTempPath(), "orion-tests", nom);

    private static PathScope Perimetre(params string[] racines)
        => new(racines.Length == 0 ? new[] { Racine("projets") } : racines);

    // ── Le défaut est le refus ───────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_NoConfiguredRoot_RefusesEverything()
    {
        var perimetre = new PathScope(Array.Empty<string>());

        Assert.False(perimetre.IsConfigured);
        Assert.Null(perimetre.Resolve(Racine("projets"), out var raison));
        Assert.Contains("AllowedRoots", raison);
    }

    [Fact]
    public void Resolve_NullRootList_RefusesLikeEmpty()
    {
        Assert.Null(new PathScope(null).Resolve(Racine("projets"), out _));
    }

    // ── Le cas passant ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_FileUnderAllowedRoot_Passes()
    {
        var attendu = Path.Combine(Racine("projets"), "orion", "README.md");

        var resolu = Perimetre().Resolve(attendu, out var raison);

        Assert.Equal(attendu, resolu);
        Assert.Empty(raison);
    }

    [Fact]
    public void Resolve_RootItself_Passes()
    {
        Assert.NotNull(Perimetre().Resolve(Racine("projets"), out _));
    }

    // ── Les contournements ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_DotDotTraversal_StaysInScope()
    {
        // Le cœur du sujet : comparer la CHAÎNE D'ENTRÉE laisserait passer ce chemin, qui
        // commence bien par une racine autorisée mais désigne autre chose une fois normalisé.
        var evasion = Path.Combine(Racine("projets"), "..", "..", "secrets.txt");

        Assert.Null(Perimetre().Resolve(evasion, out var raison));
        Assert.Contains("hors périmètre", raison);
    }

    [Fact]
    public void Resolve_SiblingRootWithLongerName_Refused()
    {
        // « /tmp/orion-tests/projets » ne doit PAS autoriser « /tmp/orion-tests/projets-prives ».
        // Un StartsWith nu se fait avoir ici ; la comparaison par segment non.
        var voisin = Racine("projets-prives") + Path.DirectorySeparatorChar + "notes.md";

        Assert.Null(Perimetre().Resolve(voisin, out var raison));
        Assert.Contains("hors périmètre", raison);
    }

    [Fact]
    public void Resolve_EmptyPath_Refused()
    {
        Assert.Null(Perimetre().Resolve("", out _));
        Assert.Null(Perimetre().Resolve("   ", out _));
        Assert.Null(Perimetre().Resolve(null, out _));
    }

    // ── Les noms sensibles, même sous une racine autorisée ───────────────────────────────────

    [Theory]
    [InlineData(".ssh")]
    [InlineData(".aws")]
    [InlineData(".git")]
    [InlineData("secrets.json")]
    [InlineData("appsettings.Production.json")]
    public void Resolve_SensitiveNameUnderAllowedRoot_Refused(string nom)
    {
        var chemin = Path.Combine(Racine("projets"), nom, "peu-importe");

        Assert.Null(Perimetre().Resolve(chemin, out var raison));
        Assert.Contains("sensible", raison);
    }

    [Theory]
    [InlineData(".env")]
    [InlineData(".env.local")]
    [InlineData(".env.production")]
    public void Resolve_AnyDotEnvVariant_Refused(string nom)
    {
        // Les lister une par une garantirait d'en oublier une : c'est le préfixe qui tient.
        var chemin = Path.Combine(Racine("projets"), "orion", nom);

        Assert.Null(Perimetre().Resolve(chemin, out var raison));
        Assert.Contains("sensible", raison);
    }

    [Fact]
    public void Resolve_BuriedSensitiveName_Refused()
    {
        // Ce n'est pas le DERNIER segment qui est sensible ici : le contrôle doit regarder
        // toute la descente, sinon `projet/.ssh/id_rsa` passe.
        var chemin = Path.Combine(Racine("projets"), "orion", ".ssh", "id_rsa");

        Assert.Null(Perimetre().Resolve(chemin, out _));
    }

    [Fact]
    public void Resolve_DifferentCasing_StillRefused()
    {
        // Windows est insensible à la casse : « .SSH » et « .ssh » désignent le même dossier.
        var chemin = Path.Combine(Racine("projets"), ".SSH", "id_rsa");

        Assert.Null(Perimetre().Resolve(chemin, out _));
    }

    [Fact]
    public void Resolve_SensitiveNameInRootItself_DoesNotBlockAll()
    {
        // Le filtre ne s'applique qu'AU-DESSOUS de la racine : si l'utilisateur autorise
        // explicitement un dossier dont le nom est sensible, c'est son choix, et tout ce qu'il
        // contient ne doit pas devenir illisible pour autant.
        var perimetre = new PathScope(new[] { Path.Combine(Racine("coffre"), ".ssh") });
        var chemin = Path.Combine(Racine("coffre"), ".ssh", "config");

        Assert.NotNull(perimetre.Resolve(chemin, out _));
    }

    // ── Racines multiples ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_MultipleRoots_EachAcceptedIndependently()
    {
        var perimetre = new PathScope(new[] { Racine("depots"), Racine("documents") });

        Assert.NotNull(perimetre.Resolve(Path.Combine(Racine("depots"), "a.txt"), out _));
        Assert.NotNull(perimetre.Resolve(Path.Combine(Racine("documents"), "b.txt"), out _));
        Assert.Null(perimetre.Resolve(Path.Combine(Racine("autre"), "c.txt"), out _));
    }

    [Fact]
    public void Resolve_EmptyRootEntry_IgnoredAndAllowsNothing()
    {
        // Une entrée blanche ne doit surtout pas se normaliser en répertoire courant et
        // ouvrir un périmètre que personne n'a déclaré.
        var perimetre = new PathScope(new[] { "", "   ", Racine("projets") });

        Assert.NotNull(perimetre.Resolve(Path.Combine(Racine("projets"), "a.txt"), out _));
        Assert.Null(perimetre.Resolve(Path.Combine(Racine("ailleurs"), "b.txt"), out _));
    }

    [Fact]
    public void Ctor_ExplicitDeniedNames_ReplacesDefaults()
    {
        var perimetre = new PathScope(new[] { Racine("projets") }, new[] { "interdit" });

        Assert.Null(perimetre.Resolve(Path.Combine(Racine("projets"), "interdit", "x"), out _));
        // « secrets.json » n'est plus dans la liste : le choix explicite prime.
        Assert.NotNull(perimetre.Resolve(Path.Combine(Racine("projets"), "secrets.json"), out _));
    }

    // ── Racine de volume : le cas où l'arithmétique naïve se trompe ──────────────────────────

    [Fact]
    public void Resolve_VolumeRoot_StaysTraversable()
    {
        // « C:\ » (ou « / ») porte déjà son séparateur. Lui en ajouter un second produit un
        // préfixe que plus aucun chemin ne satisfait : le périmètre se fermerait TOTALEMENT,
        // et la panne ressemblerait à un problème de configuration.
        var volume = Path.GetPathRoot(Path.GetTempPath())!;
        var perimetre = new PathScope(new[] { volume });

        Assert.NotNull(perimetre.Resolve(Path.Combine(Path.GetTempPath(), "a.txt"), out var raison));
        Assert.Empty(raison);
    }

    [Fact]
    public void Resolve_SensitiveNameUnderVolumeRoot_Refused()
    {
        // Le pendant du test précédent : en découpant les segments par arithmétique sur les
        // longueurs, « .ssh » perd son premier caractère et devient « ssh » — le filtre ne
        // reconnaît plus rien et laisse tout passer.
        var volume = Path.GetPathRoot(Path.GetTempPath())!;
        var perimetre = new PathScope(new[] { volume });

        Assert.Null(perimetre.Resolve(Path.Combine(volume, ".ssh", "id_rsa"), out var raison));
        Assert.Contains("sensible", raison);
    }

    // ── Périmètre d'écriture (C2) ────────────────────────────────────────────────────────────

    [Fact]
    public void WriteScope_NarrowerThanReadScope_Enforced()
    {
        // Lire un dépôt et pouvoir y écrire ne sont pas la même permission : c'est la raison
        // d'être d'AllowedWriteRoots. Ici, ORION lit deux dossiers mais n'écrit que dans un.
        var lecture = new PathScope(new[] { Racine("depots"), Racine("documents") });
        var ecriture = new PathScope(new[] { Racine("documents") });

        var cible = Path.Combine(Racine("depots"), "orion", "fichier.txt");

        Assert.NotNull(lecture.Resolve(cible, out _));
        Assert.Null(ecriture.Resolve(cible, out _));
    }

    // ── IsVisible, utilisé par le listing ───────────────────────────────────────────────────

    [Fact]
    public void IsVisible_SameRulesAsResolve()
    {
        var perimetre = Perimetre();

        Assert.True(perimetre.IsVisible(Path.Combine(Racine("projets"), "orion", "README.md")));
        Assert.False(perimetre.IsVisible(Path.Combine(Racine("projets"), ".env")));
        Assert.False(perimetre.IsVisible(Path.Combine(Racine("ailleurs"), "x.txt")));
    }
}
