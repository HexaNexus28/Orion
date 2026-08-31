using System.Net;
using Microsoft.Extensions.Options;
using Orion.Business.Tools.Internet;
using Orion.Core.Configuration;

namespace Orion.Tests.Tools;

/// <summary>
/// Le périmètre réseau des outils qui vont chercher une page — constat E2 de l'audit du
/// 2026-08-27.
///
/// Ces tests portent sur ce qui ÉTAIT atteignable : loopback (l'API elle-même), 169.254.169.254
/// (les métadonnées d'instance, qui rendent des identifiants sans authentification), et les
/// schémas autres que http/https. Aucun ne dépend du réseau : ils utilisent des adresses
/// littérales, pas des noms à résoudre.
/// </summary>
public class UrlScopeTests
{
    private static UrlScope Perimetre(params string[] domainesBloques)
        => new(Options.Create(new InternetOptions { BlockedDomains = domainesBloques }));

    private static async Task<(Uri? Uri, string Raison)> Verifier(string? url, params string[] bloques)
        => await Perimetre(bloques).VerifierAsync(url);

    // ── Schémas ──────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("file:///C:/Users/moi/.ssh/id_rsa")]
    [InlineData("ftp://exemple.test/fichier")]
    [InlineData("gopher://exemple.test/")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    public async Task Un_schema_hors_http_est_refuse(string url)
    {
        var (uri, raison) = await Verifier(url);

        Assert.Null(uri);
        Assert.Contains("Schéma", raison);
    }

    [Theory]
    [InlineData("http://exemple.test/page")]
    [InlineData("https://exemple.test/page")]
    public async Task Http_et_https_sont_les_seuls_acceptes(string url)
    {
        // Le domaine .test ne résout nulle part : on vérifie ici que le SCHÉMA passe le filtre,
        // le refus éventuel venant alors de la résolution DNS et non du schéma.
        var (_, raison) = await Verifier(url);

        Assert.DoesNotContain("Schéma", raison);
    }

    [Fact]
    public async Task Une_url_vide_ou_invalide_est_refusee()
    {
        Assert.Null((await Verifier(null)).Uri);
        Assert.Null((await Verifier("")).Uri);
        Assert.Null((await Verifier("pas une url")).Uri);
        // Relative : sans hôte, il n'y a rien à contrôler.
        Assert.Null((await Verifier("/api/memory")).Uri);
    }

    // ── Adresses internes ────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("http://127.0.0.1:5107/api/memory")]   // l'API elle-même, en loopback
    [InlineData("http://localhost:5107/api/memory")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]   // métadonnées d'instance
    [InlineData("http://10.0.0.5/")]
    [InlineData("http://172.16.4.2/")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://[::1]:5107/")]
    [InlineData("http://0.0.0.0/")]
    public async Task Une_adresse_interne_est_refusee(string url)
    {
        var (uri, raison) = await Verifier(url);

        Assert.Null(uri);
        Assert.Contains("interne", raison);
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("93.184.216.34")]
    public async Task Une_adresse_publique_passe(string ip)
    {
        var (uri, raison) = await Verifier($"http://{ip}/page");

        Assert.NotNull(uri);
        Assert.Empty(raison);
    }

    // ── La classification, testée directement ────────────────────────────────────────────────

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("127.55.1.9", true)]      // tout 127/8, pas seulement .0.1
    [InlineData("169.254.169.254", true)] // LE cas qui compte
    [InlineData("10.255.255.255", true)]
    [InlineData("172.15.0.1", false)]     // juste SOUS la plage privée
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.255", true)]
    [InlineData("172.32.0.1", false)]     // juste AU-DESSUS
    [InlineData("192.168.0.1", true)]
    [InlineData("192.169.0.1", false)]
    [InlineData("100.64.0.1", true)]      // CGNAT
    [InlineData("100.63.255.255", false)]
    [InlineData("224.0.0.1", true)]       // multicast
    [InlineData("8.8.8.8", false)]
    public void Les_bornes_des_plages_privees_sont_exactes(string ip, bool interne)
    {
        // Les bornes sont l'endroit où ce genre de test attrape vraiment quelque chose :
        // 172.16/12 s'arrête à 172.31, pas à 172.16 ni à 172.255.
        Assert.Equal(interne, UrlScope.EstInterne(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("::1", true)]
    [InlineData("fe80::1", true)]          // lien-local
    [InlineData("fc00::1", true)]          // locale unique
    [InlineData("fd12:3456::1", true)]
    [InlineData("::ffff:127.0.0.1", true)] // IPv4 loopback déguisée en IPv6
    [InlineData("::ffff:169.254.169.254", true)]
    [InlineData("2001:4860:4860::8888", false)]
    public void Les_formes_IPv6_ne_permettent_pas_de_contourner(string ip, bool interne)
    {
        Assert.Equal(interne, UrlScope.EstInterne(IPAddress.Parse(ip)));
    }

    // ── BlockedDomains, qui n'était lu par personne ──────────────────────────────────────────

    [Fact]
    public async Task Un_domaine_bloque_est_refuse()
    {
        var (uri, raison) = await Verifier("https://interdit.test/page", "interdit.test");

        Assert.Null(uri);
        Assert.Contains("BlockedDomains", raison);
    }

    [Fact]
    public async Task Un_sous_domaine_dun_domaine_bloque_est_refuse_aussi()
    {
        Assert.Null((await Verifier("https://api.interdit.test/x", "interdit.test")).Uri);
    }

    [Fact]
    public async Task Un_domaine_qui_se_TERMINE_par_le_meme_texte_nest_pas_bloque()
    {
        // « interdit.test » ne doit pas bloquer « pasinterdit.test » : c'est la faille du
        // Contains() qu'utilisait l'ancien garde de screenshot_page.
        var (_, raison) = await Verifier("https://pasinterdit.test/x", "interdit.test");

        Assert.DoesNotContain("BlockedDomains", raison);
    }

    [Fact]
    public async Task Le_point_de_tete_dans_la_configuration_est_tolere()
    {
        Assert.Null((await Verifier("https://interdit.test/x", ".interdit.test")).Uri);
    }
}
