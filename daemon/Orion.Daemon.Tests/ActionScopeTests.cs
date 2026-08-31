using System.Text.Json;
using Orion.Daemon.Actions;
using Orion.Daemon.Core.Configuration;

namespace Orion.Daemon.Tests;

/// <summary>
/// Les actions APPLIQUENT-ELLES leur perimetre ?
///
/// POURQUOI CE FICHIER EXISTE, et pourquoi PathScopeTests ne suffit pas. La faille C1 de l'audit
/// du 2026-08-27 n'etait PAS un garde-fou defectueux : c'etait un garde-fou jamais appele.
/// `DaemonOptions` etait injecte dans `ReadFileAction` et `ListFilesAction` et n'y etait
/// simplement jamais lu. Un lecteur croyait voir une protection configurable ; tout le disque
/// etait accessible, `%USERPROFILE%\.ssh\id_rsa` compris.
///
/// Une suite portant sur `PathScope` seul serait restee INTEGRALEMENT VERTE pendant toute la
/// duree de cette faille : le verrou fonctionnait parfaitement, personne ne le posait sur la
/// porte. Verifie par mutation le 2026-09-01 — en retirant le perimetre des deux actions, ces
/// tests tombent (6 echecs) tandis que les 15 PathScopeTests restent verts.
///
/// Tester un garde ne prouve rien sur son APPLICATION.
/// </summary>
public sealed class ActionScopeTests : IDisposable
{
    private readonly string _readableRoot;   // lecture autorisee
    private readonly string _writableRoot;   // lecture ET ecriture autorisees
    private readonly string _outsideRoot;    // hors de tout perimetre

    public ActionScopeTests()
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "orion-scope-" + Guid.NewGuid().ToString("N"));
        _readableRoot = Path.Combine(sandbox, "repos");
        _writableRoot = Path.Combine(sandbox, "output");
        _outsideRoot = Path.Combine(sandbox, "private");

        Directory.CreateDirectory(_readableRoot);
        Directory.CreateDirectory(_writableRoot);
        Directory.CreateDirectory(_outsideRoot);
    }

    public void Dispose()
    {
        var sandbox = Path.GetDirectoryName(_readableRoot);
        if (sandbox is not null && Directory.Exists(sandbox)) { Directory.Delete(sandbox, recursive: true); }
    }

    /// <summary>Le perimetre reel : on lit dans deux racines, on n'ecrit que dans une seule.</summary>
    private DaemonOptions ScopedOptions() => new()
    {
        AllowedRoots = new[] { _readableRoot, _writableRoot },
        AllowedWriteRoots = new[] { _writableRoot },
    };

    private static JsonElement Payload(object body) => JsonSerializer.SerializeToElement(body);

    // ─── read_file ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadFile_OutsideScope_Refused()
    {
        var secret = Path.Combine(_outsideRoot, "notes.txt");
        await File.WriteAllTextAsync(secret, "private content");

        var response = await new ReadFileAction(ScopedOptions())
            .ExecuteAsync(Payload(new { path = secret }), "c1");

        Assert.False(response.Success);

        // On verifie que c'est bien le PERIMETRE qui refuse, pas un « fichier introuvable » qui
        // passerait pour un refus. « racine » est le seul mot a la fois stable, sans accent, et
        // absent des autres messages d'erreur de l'action.
        //
        // La version precedente cherchait « perimetre » — et passait pour une mauvaise raison :
        // le dossier temporaire du test s'appelait `orion-perimetre-<guid>`, et le message
        // d'erreur contient le chemin COMPLET. L'assertion tombait sur le nom du dossier que le
        // test venait de creer. Renommer le bac a sable a suffi a faire tomber le test : une
        // assertion doit porter sur ce que le CODE produit, jamais sur ce que le test a seme.
        Assert.Contains("racine", response.Error, StringComparison.OrdinalIgnoreCase);

        // Le contenu ne doit apparaitre NULLE PART dans la reponse : ce que l'action renvoie
        // repart au modele, qui le restitue. Ici, la lecture EST l'exfiltration.
        Assert.DoesNotContain("private content", response.Error);
        Assert.Null(response.Data);
    }

    [Fact]
    public async Task ReadFile_InsideScope_Succeeds()
    {
        // Le test qui empeche les autres de mentir. Sans lui, une action qui refuserait TOUT —
        // y compris le legitime — les rendrait tous verts tout en cassant le produit.
        var file = Path.Combine(_readableRoot, "README.md");
        await File.WriteAllTextAsync(file, "one line");

        var response = await new ReadFileAction(ScopedOptions())
            .ExecuteAsync(Payload(new { path = file }), "c2");

        Assert.True(response.Success);
        Assert.NotNull(response.Data);
    }

    [Fact]
    public async Task ReadFile_DotEnvUnderAllowedRoot_Refused()
    {
        // Autoriser « le dossier projet » reste correct : c'est le `.env` qui s'y trouve qui ne
        // doit pas sortir. Une racine autorisee dit ou chercher, pas que tout y est anodin.
        var env = Path.Combine(_readableRoot, ".env.production");
        await File.WriteAllTextAsync(env, "DB_PASSWORD=secret");

        var response = await new ReadFileAction(ScopedOptions())
            .ExecuteAsync(Payload(new { path = env }), "c3");

        Assert.False(response.Success);
        Assert.DoesNotContain("secret", response.Error);
    }

    [Fact]
    public async Task ReadFile_NoAllowedRoot_RefusesEverything()
    {
        // FAIL-CLOSED au niveau de l'ACTION : une configuration absente ferme la porte, elle ne
        // l'ouvre pas. C'est le defaut exact qui manquait avant l'audit.
        var file = Path.Combine(_readableRoot, "anything.txt");
        await File.WriteAllTextAsync(file, "x");

        var response = await new ReadFileAction(new DaemonOptions())
            .ExecuteAsync(Payload(new { path = file }), "c4");

        Assert.False(response.Success);
    }

    // ─── list_files ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListFiles_OutsideScope_Refused()
    {
        var response = await new ListFilesAction(ScopedOptions())
            .ExecuteAsync(Payload(new { path = _outsideRoot }), "c5");

        Assert.False(response.Success);
    }

    [Fact]
    public async Task ListFiles_DeniedNames_HiddenFromListing()
    {
        // Reveler qu'un `.env` existe renseigne l'attaquant meme sans en lire le contenu. Le
        // filtre doit donc porter sur le LISTING autant que sur la lecture.
        await File.WriteAllTextAsync(Path.Combine(_readableRoot, "visible.txt"), "ok");
        await File.WriteAllTextAsync(Path.Combine(_readableRoot, ".env"), "SECRET=1");

        var response = await new ListFilesAction(ScopedOptions())
            .ExecuteAsync(Payload(new { path = _readableRoot }), "c6");

        Assert.True(response.Success);

        var rendered = JsonSerializer.Serialize(response.Data);
        Assert.Contains("visible.txt", rendered);
        Assert.DoesNotContain(".env", rendered);
    }

    // ─── write_file ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WriteFile_OutsideScope_WritesNothingCreatesNoDirectory()
    {
        // LE test le plus important du fichier. Avant le correctif, `Directory.CreateDirectory`
        // s'executait AVANT toute verification : l'arborescence de n'importe quel chemin etait
        // fabriquee au passage. Cible evidente : le dossier Demarrage, d'ou le daemon lui-meme
        // est lance — un fichier depose la s'execute a la prochaine ouverture de session.
        //
        // Verifier le seul code de retour ne suffirait donc pas : on verifie l'ETAT DU DISQUE.
        var target = Path.Combine(_outsideRoot, "nested", "payload.ps1");

        var response = await new WriteFileAction(ScopedOptions())
            .ExecuteAsync(Payload(new { path = target, content = "malicious" }), "c7");

        Assert.False(response.Success);
        Assert.False(File.Exists(target));
        Assert.False(Directory.Exists(Path.GetDirectoryName(target)));
    }

    [Fact]
    public async Task WriteFile_ReadOnlyRoot_Refused()
    {
        // Lire et ecrire ne sont pas la meme permission. `_readableRoot` est lisible, elle n'est
        // pas inscriptible : ORION peut lire le code, jamais le modifier.
        var target = Path.Combine(_readableRoot, "injected.txt");

        var response = await new WriteFileAction(ScopedOptions())
            .ExecuteAsync(Payload(new { path = target, content = "x" }), "c8");

        Assert.False(response.Success);
        Assert.False(File.Exists(target));
    }

    [Fact]
    public async Task WriteFile_InsideWriteRoot_Succeeds()
    {
        var target = Path.Combine(_writableRoot, "report.md");

        var response = await new WriteFileAction(ScopedOptions())
            .ExecuteAsync(Payload(new { path = target, content = "result" }), "c9");

        Assert.True(response.Success);
        Assert.Equal("result", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task WriteFile_NoWriteRoots_FallsBackToReadRoots()
    {
        // `AllowedWriteRoots` vide ne veut pas dire « tout » : on retombe sur `AllowedRoots`,
        // donc vers un ensemble plus petit ou egal. Les deux listes vides refusent tout.
        var options = new DaemonOptions { AllowedRoots = new[] { _writableRoot } };
        var inside = Path.Combine(_writableRoot, "fallback.txt");
        var outside = Path.Combine(_outsideRoot, "fallback.txt");

        var action = new WriteFileAction(options);

        Assert.True((await action.ExecuteAsync(Payload(new { path = inside, content = "a" }), "c10")).Success);
        Assert.False((await action.ExecuteAsync(Payload(new { path = outside, content = "a" }), "c11")).Success);
    }
}
