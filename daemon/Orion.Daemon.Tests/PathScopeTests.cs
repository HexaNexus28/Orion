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
    public void Sans_racine_configuree_tout_est_refuse()
    {
        var perimetre = new PathScope(Array.Empty<string>());

        Assert.False(perimetre.EstConfigure);
        Assert.Null(perimetre.Resoudre(Racine("projets"), out var raison));
        Assert.Contains("AllowedRoots", raison);
    }

    [Fact]
    public void Une_liste_nulle_vaut_une_liste_vide_donc_refus()
    {
        Assert.Null(new PathScope(null).Resoudre(Racine("projets"), out _));
    }

    // ── Le cas passant ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Un_fichier_sous_une_racine_autorisee_passe()
    {
        var attendu = Path.Combine(Racine("projets"), "orion", "README.md");

        var resolu = Perimetre().Resoudre(attendu, out var raison);

        Assert.Equal(attendu, resolu);
        Assert.Empty(raison);
    }

    [Fact]
    public void La_racine_elle_meme_est_autorisee()
    {
        Assert.NotNull(Perimetre().Resoudre(Racine("projets"), out _));
    }

    // ── Les contournements ───────────────────────────────────────────────────────────────────

    [Fact]
    public void La_remontee_par_deux_points_ne_sort_pas_du_perimetre()
    {
        // Le cœur du sujet : comparer la CHAÎNE D'ENTRÉE laisserait passer ce chemin, qui
        // commence bien par une racine autorisée mais désigne autre chose une fois normalisé.
        var evasion = Path.Combine(Racine("projets"), "..", "..", "secrets.txt");

        Assert.Null(Perimetre().Resoudre(evasion, out var raison));
        Assert.Contains("hors périmètre", raison);
    }

    [Fact]
    public void Une_racine_voisine_au_nom_plus_long_nest_pas_incluse()
    {
        // « /tmp/orion-tests/projets » ne doit PAS autoriser « /tmp/orion-tests/projets-prives ».
        // Un StartsWith nu se fait avoir ici ; la comparaison par segment non.
        var voisin = Racine("projets-prives") + Path.DirectorySeparatorChar + "notes.md";

        Assert.Null(Perimetre().Resoudre(voisin, out var raison));
        Assert.Contains("hors périmètre", raison);
    }

    [Fact]
    public void Un_chemin_vide_est_refuse()
    {
        Assert.Null(Perimetre().Resoudre("", out _));
        Assert.Null(Perimetre().Resoudre("   ", out _));
        Assert.Null(Perimetre().Resoudre(null, out _));
    }

    // ── Les noms sensibles, même sous une racine autorisée ───────────────────────────────────

    [Theory]
    [InlineData(".ssh")]
    [InlineData(".aws")]
    [InlineData(".git")]
    [InlineData("secrets.json")]
    [InlineData("appsettings.Production.json")]
    public void Un_emplacement_sensible_est_refuse_sous_une_racine_autorisee(string nom)
    {
        var chemin = Path.Combine(Racine("projets"), nom, "peu-importe");

        Assert.Null(Perimetre().Resoudre(chemin, out var raison));
        Assert.Contains("sensible", raison);
    }

    [Theory]
    [InlineData(".env")]
    [InlineData(".env.local")]
    [InlineData(".env.production")]
    public void Toutes_les_variantes_de_env_sont_refusees(string nom)
    {
        // Les lister une par une garantirait d'en oublier une : c'est le préfixe qui tient.
        var chemin = Path.Combine(Racine("projets"), "orion", nom);

        Assert.Null(Perimetre().Resoudre(chemin, out var raison));
        Assert.Contains("sensible", raison);
    }

    [Fact]
    public void Un_nom_sensible_enfoui_est_refuse_aussi()
    {
        // Ce n'est pas le DERNIER segment qui est sensible ici : le contrôle doit regarder
        // toute la descente, sinon `projet/.ssh/id_rsa` passe.
        var chemin = Path.Combine(Racine("projets"), "orion", ".ssh", "id_rsa");

        Assert.Null(Perimetre().Resoudre(chemin, out _));
    }

    [Fact]
    public void La_casse_ne_permet_pas_de_contourner_un_nom_sensible()
    {
        // Windows est insensible à la casse : « .SSH » et « .ssh » désignent le même dossier.
        var chemin = Path.Combine(Racine("projets"), ".SSH", "id_rsa");

        Assert.Null(Perimetre().Resoudre(chemin, out _));
    }

    [Fact]
    public void Un_nom_sensible_present_dans_la_RACINE_ne_bloque_pas_tout()
    {
        // Le filtre ne s'applique qu'AU-DESSOUS de la racine : si l'utilisateur autorise
        // explicitement un dossier dont le nom est sensible, c'est son choix, et tout ce qu'il
        // contient ne doit pas devenir illisible pour autant.
        var perimetre = new PathScope(new[] { Path.Combine(Racine("coffre"), ".ssh") });
        var chemin = Path.Combine(Racine("coffre"), ".ssh", "config");

        Assert.NotNull(perimetre.Resoudre(chemin, out _));
    }

    // ── Racines multiples ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Plusieurs_racines_sont_acceptees_independamment()
    {
        var perimetre = new PathScope(new[] { Racine("depots"), Racine("documents") });

        Assert.NotNull(perimetre.Resoudre(Path.Combine(Racine("depots"), "a.txt"), out _));
        Assert.NotNull(perimetre.Resoudre(Path.Combine(Racine("documents"), "b.txt"), out _));
        Assert.Null(perimetre.Resoudre(Path.Combine(Racine("autre"), "c.txt"), out _));
    }

    [Fact]
    public void Une_racine_vide_dans_la_liste_est_ignoree_et_nautorise_rien()
    {
        // Une entrée blanche ne doit surtout pas se normaliser en répertoire courant et
        // ouvrir un périmètre que personne n'a déclaré.
        var perimetre = new PathScope(new[] { "", "   ", Racine("projets") });

        Assert.NotNull(perimetre.Resoudre(Path.Combine(Racine("projets"), "a.txt"), out _));
        Assert.Null(perimetre.Resoudre(Path.Combine(Racine("ailleurs"), "b.txt"), out _));
    }

    [Fact]
    public void Une_liste_de_noms_refuses_explicite_remplace_le_defaut()
    {
        var perimetre = new PathScope(new[] { Racine("projets") }, new[] { "interdit" });

        Assert.Null(perimetre.Resoudre(Path.Combine(Racine("projets"), "interdit", "x"), out _));
        // « secrets.json » n'est plus dans la liste : le choix explicite prime.
        Assert.NotNull(perimetre.Resoudre(Path.Combine(Racine("projets"), "secrets.json"), out _));
    }

    // ── Racine de volume : le cas où l'arithmétique naïve se trompe ──────────────────────────

    [Fact]
    public void Une_racine_de_volume_reste_franchissable()
    {
        // « C:\ » (ou « / ») porte déjà son séparateur. Lui en ajouter un second produit un
        // préfixe que plus aucun chemin ne satisfait : le périmètre se fermerait TOTALEMENT,
        // et la panne ressemblerait à un problème de configuration.
        var volume = Path.GetPathRoot(Path.GetTempPath())!;
        var perimetre = new PathScope(new[] { volume });

        Assert.NotNull(perimetre.Resoudre(Path.Combine(Path.GetTempPath(), "a.txt"), out var raison));
        Assert.Empty(raison);
    }

    [Fact]
    public void Sous_une_racine_de_volume_les_noms_sensibles_restent_refuses()
    {
        // Le pendant du test précédent : en découpant les segments par arithmétique sur les
        // longueurs, « .ssh » perd son premier caractère et devient « ssh » — le filtre ne
        // reconnaît plus rien et laisse tout passer.
        var volume = Path.GetPathRoot(Path.GetTempPath())!;
        var perimetre = new PathScope(new[] { volume });

        Assert.Null(perimetre.Resoudre(Path.Combine(volume, ".ssh", "id_rsa"), out var raison));
        Assert.Contains("sensible", raison);
    }

    // ── Périmètre d'écriture (C2) ────────────────────────────────────────────────────────────

    [Fact]
    public void Le_perimetre_decriture_peut_etre_plus_etroit_que_celui_de_lecture()
    {
        // Lire un dépôt et pouvoir y écrire ne sont pas la même permission : c'est la raison
        // d'être d'AllowedWriteRoots. Ici, ORION lit deux dossiers mais n'écrit que dans un.
        var lecture = new PathScope(new[] { Racine("depots"), Racine("documents") });
        var ecriture = new PathScope(new[] { Racine("documents") });

        var cible = Path.Combine(Racine("depots"), "orion", "fichier.txt");

        Assert.NotNull(lecture.Resoudre(cible, out _));
        Assert.Null(ecriture.Resoudre(cible, out _));
    }

    // ── EstVisible, utilisé par le listing ───────────────────────────────────────────────────

    [Fact]
    public void EstVisible_suit_exactement_les_memes_regles()
    {
        var perimetre = Perimetre();

        Assert.True(perimetre.EstVisible(Path.Combine(Racine("projets"), "orion", "README.md")));
        Assert.False(perimetre.EstVisible(Path.Combine(Racine("projets"), ".env")));
        Assert.False(perimetre.EstVisible(Path.Combine(Racine("ailleurs"), "x.txt")));
    }
}
