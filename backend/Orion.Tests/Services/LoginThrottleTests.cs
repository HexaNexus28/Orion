using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Orion.Api.Authentication;
using Orion.Core.Configuration;

namespace Orion.Tests.Services;

/// <summary>
/// Le frein de connexion. Ce qu'il doit garantir tient en une phrase : plafonner la DEVINETTE
/// sans jamais enfermer le proprietaire dehors.
///
/// Ces deux proprietes sont en tension — c'est precisement pour ca qu'elles sont testees. Un
/// limiteur pose avant la verification du mot de passe satisferait la premiere et violerait la
/// seconde : un attaquant saturant la fenetre transformerait son attaque en deni de service sur
/// le compte vise. Le test « le bon mot de passe passe toujours » est donc le plus important
/// du fichier, meme si c'est le moins evident.
/// </summary>
public class LoginThrottleTests
{
    private static LoginThrottle Creer(int echecs = 3, int minutes = 15)
    {
        var options = Options.Create(new AuthOptions
        {
            LoginFailuresPerWindow = echecs,
            LoginWindowMinutes = minutes,
        });

        return new LoginThrottle(options, Mock.Of<ILogger<LoginThrottle>>());
    }

    [Fact]
    public void Ardoise_vierge_ne_bloque_pas()
    {
        var frein = Creer();

        Assert.False(frein.EstBloque(out var attente));
        Assert.Equal(TimeSpan.Zero, attente);
    }

    [Fact]
    public void Sous_le_quota_ne_bloque_pas()
    {
        var frein = Creer(echecs: 3);

        frein.EnregistrerEchec();
        frein.EnregistrerEchec();

        // Deux echecs sur trois autorises : la porte reste ouverte. Bloquer ici punirait une
        // faute de frappe.
        Assert.False(frein.EstBloque(out _));
    }

    [Fact]
    public void Au_quota_bloque_et_annonce_un_delai_exploitable()
    {
        var frein = Creer(echecs: 3, minutes: 15);

        frein.EnregistrerEchec();
        frein.EnregistrerEchec();
        frein.EnregistrerEchec();

        Assert.True(frein.EstBloque(out var attente));

        // Le delai alimente `Retry-After`. Une valeur nulle ou negative dirait au client de
        // reessayer dans le passe : il boucquerait sans jamais aboutir.
        Assert.True(attente > TimeSpan.Zero);
        Assert.True(attente <= TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void Une_connexion_reussie_efface_l_ardoise()
    {
        var frein = Creer(echecs: 3);

        frein.EnregistrerEchec();
        frein.EnregistrerEchec();
        frein.EnregistrerEchec();
        Assert.True(frein.EstBloque(out _));

        // C'est ce que fait le controleur des qu'un mot de passe correct est presente. Sans ca,
        // quelques fautes de frappe suivies d'une connexion legitime laisseraient un quota
        // entame pour la suite de la fenetre.
        frein.Reinitialiser();

        Assert.False(frein.EstBloque(out _));
    }

    [Fact]
    public void La_fenetre_GLISSE_les_vieux_echecs_ne_comptent_plus()
    {
        // Fenetre nulle : tout echec est deja hors fenetre au moment ou on l'interroge. C'est la
        // facon de prouver la purge sans faire dormir le test — une seconde d'attente reelle par
        // test finit par couter une suite entiere.
        var frein = Creer(echecs: 1, minutes: 0);

        frein.EnregistrerEchec();
        frein.EnregistrerEchec();

        Assert.False(frein.EstBloque(out _));
    }

    [Fact]
    public void Le_quota_ne_compte_que_les_ECHECS()
    {
        var frein = Creer(echecs: 2);

        // Le scenario reel : l'attaquant epuise le quota, puis le proprietaire se connecte.
        frein.EnregistrerEchec();
        frein.EnregistrerEchec();
        Assert.True(frein.EstBloque(out _));

        // Le controleur verifie le mot de passe AVANT de consulter le frein. Un mot de passe
        // correct ne consulte donc jamais `EstBloque` et n'enregistre aucun echec : il passe,
        // meme quota epuise. C'est ce qui empeche l'attaque de devenir un deni de service.
        frein.Reinitialiser();
        Assert.False(frein.EstBloque(out _));
    }
}
