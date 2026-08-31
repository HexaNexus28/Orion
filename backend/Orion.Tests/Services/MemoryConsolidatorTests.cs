using Microsoft.Extensions.Logging;
using Moq;
using Orion.Business.Services;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Entities;
using Orion.Core.Enums;
using Orion.Core.Interfaces.LLM;
using Orion.Core.Interfaces.Repositories;
using Orion.Core.Interfaces.Services;

namespace Orion.Tests.Services;

/// <summary>
/// La consolidation décide ce qui entre dans la mémoire durable. Deux échecs symétriques la
/// guettent : ne rien retenir — ORION reste amnésique — ou tout retenir, et la mémoire devient
/// un bruit qu'on cesse de consulter. Ces tests verrouillent le tri.
/// </summary>
public class MemoryConsolidatorTests
{
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IMemoryRepository> _memoires = new();
    private readonly Mock<ILLMAgentClient> _llm = new();
    private readonly Mock<IMemoryService> _memoryService = new();

    private readonly List<(string Contenu, string Slot, float Importance)> _ecrits = new();

    public MemoryConsolidatorTests()
    {
        _unitOfWork.SetupGet(u => u.Memory).Returns(_memoires.Object);

        _memoryService
            .Setup(m => m.SaveMemoryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<float>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, float, CancellationToken>((c, s, i, _) => _ecrits.Add((c, s, i)))
            .ReturnsAsync(ApiResponse<bool>.SuccessResponse(true));

        _memoryService
            .Setup(m => m.DeleteMemoryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<bool>.SuccessResponse(true));
    }

    private MemoryConsolidator Build(string reponseLlm, params MemoryVector[] enMemoire)
    {
        _memoires.Setup(m => m.GetAllAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync(enMemoire.ToList());

        _llm.Setup(c => c.StreamTurnAsync(It.IsAny<LLMRequest>(), It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LLMTurn { Content = reponseLlm });

        return new MemoryConsolidator(
            _unitOfWork.Object, _llm.Object, _memoryService.Object,
            Mock.Of<ILogger<MemoryConsolidator>>());
    }

    private static MemoryVector Episode(string contenu, int joursAge = 0) => new()
    {
        Id = Guid.NewGuid(),
        Content = contenu,
        Source = "conversation",
        CreatedAt = DateTime.UtcNow.AddDays(-joursAge)
    };

    private static MemoryVector Durable(MemorySlot slot, string contenu, int joursAge = 0) => new()
    {
        Id = Guid.NewGuid(),
        Content = contenu,
        Source = slot.ToString(),
        CreatedAt = DateTime.UtcNow.AddDays(-joursAge)
    };

    [Fact]
    public async Task Consolidate_DistilledFacts_FiledInTheirSlot()
    {
        var consolidateur = Build(
            "decisions|1.5|Alex heberge le backend ORION sur un serveur dedie\n" +
            "refs|1.0|La base ORION est le projet Supabase niwciampfbwppjpufbnz",
            Episode("Alex a parle de son hebergement"));

        var rapport = await consolidateur.ConsolidateAsync();

        Assert.True(rapport.Success);
        Assert.Equal(2, rapport.Data!.SouvenirsEcrits);
        Assert.Contains(_ecrits, e => e.Slot == "decisions" && e.Importance == 1.5f);
        Assert.Contains(_ecrits, e => e.Slot == "refs");
    }

    [Fact]
    public async Task Consolidate_MalformedLine_IgnoredNeverWritten()
    {
        var consolidateur = Build(
            "ceci n'est pas au format attendu\n" +
            "decisions|1.0|Un fait correctement forme et suffisamment long",
            Episode("un echange"));

        await consolidateur.ConsolidateAsync();

        Assert.Single(_ecrits);
    }

    [Fact]
    public async Task Consolidate_UnknownSlot_Refused()
    {
        // Le schéma est FERMÉ : pas de cinquième emplacement, même si le modèle en invente un.
        var consolidateur = Build(
            "notes|1.0|Un fait range dans un emplacement qui n'existe pas\n" +
            "episode|1.0|Un episode ne peut pas etre le resultat d'une distillation",
            Episode("un echange"));

        await consolidateur.ConsolidateAsync();

        Assert.Empty(_ecrits);
    }

    [Fact]
    public async Task Consolidate_NoneMarker_MeansNothingWorthKeeping()
    {
        var consolidateur = Build("AUCUN", Episode("bonjour ca va"));

        var rapport = await consolidateur.ConsolidateAsync();

        Assert.Empty(_ecrits);
        Assert.Equal(0, rapport.Data!.SouvenirsEcrits);
        Assert.Contains("rien de durable", rapport.Data.Resume);
    }

    [Fact]
    public async Task Consolidate_NoEpisode_ModelNotCalled()
    {
        // Ne pas payer un tour de LLM pour consolider le vide.
        var consolidateur = Build("decisions|1.0|ne devrait jamais etre demande",
            Durable(MemorySlot.Decisions, "un fait deja consolide"));

        var rapport = await consolidateur.ConsolidateAsync();

        Assert.Contains("Rien à consolider", rapport.Data!.Resume);
        _llm.Verify(c => c.StreamTurnAsync(It.IsAny<LLMRequest>(), It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Consolidate_NewState_PurgesStaleState()
    {
        // `state` est VOLATILE : sans purge on empile des « chantiers en cours » finis depuis
        // longtemps, et la mémoire raconte un présent qui n'existe plus.
        var consolidateur = Build(
            "state|1.0|Chantier en cours : consolidation de la memoire d'ORION",
            Episode("un echange"),
            Durable(MemorySlot.State, "Ancien chantier oublie", joursAge: 30));

        var rapport = await consolidateur.ConsolidateAsync();

        Assert.Equal(1, rapport.Data!.EtatsPerimesSupprimes);
        // Deux suppressions : l'etat perime, puis l'episode consomme apres distillation.
        Assert.Equal(1, rapport.Data.EpisodesConsommes);
        _memoryService.Verify(m => m.DeleteMemoryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Consolidate_RecentState_NotPurged()
    {
        var consolidateur = Build(
            "state|1.0|Chantier en cours : consolidation de la memoire d'ORION",
            Episode("un echange"),
            Durable(MemorySlot.State, "Chantier encore actif", joursAge: 2));

        var rapport = await consolidateur.ConsolidateAsync();

        Assert.Equal(0, rapport.Data!.EtatsPerimesSupprimes);
    }

    [Fact]
    public async Task Consolidate_ReplayedEpisodes_Consumed()
    {
        // Sans consommation, chaque passe laisse le brut a cote du distille et la memoire se
        // remplit de doublons. Aucune perte : les echanges restent dans la table `messages`.
        var consolidateur = Build(
            "decisions|1.0|Un fait durable extrait de l'echange relu",
            Episode("un echange"), Episode("un autre echange"));

        var rapport = await consolidateur.ConsolidateAsync();

        Assert.Equal(2, rapport.Data!.EpisodesConsommes);
        _memoryService.Verify(m => m.DeleteMemoryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Consolidate_DistillationFails_EpisodesNotConsumed()
    {
        // Perdre la matiere premiere sur une panne serait pire que de la garder en double.
        _memoires.Setup(m => m.GetAllAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new List<MemoryVector> { Episode("un echange") });
        _llm.Setup(c => c.StreamTurnAsync(It.IsAny<LLMRequest>(), It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("fournisseur injoignable"));

        var consolidateur = new MemoryConsolidator(
            _unitOfWork.Object, _llm.Object, _memoryService.Object,
            Mock.Of<ILogger<MemoryConsolidator>>());

        var rapport = await consolidateur.ConsolidateAsync();

        Assert.False(rapport.Success);
        _memoryService.Verify(m => m.DeleteMemoryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Consolidate_Importance_Clamped()
    {
        var consolidateur = Build(
            "rules|9.9|Une importance hors bornes doit etre ramenee dans l'intervalle\n" +
            "rules|abc|Une importance illisible retombe sur la valeur par defaut",
            Episode("un echange"));

        await consolidateur.ConsolidateAsync();

        Assert.Equal(2.0f, _ecrits[0].Importance);
        Assert.Equal(1.0f, _ecrits[1].Importance);
    }

    [Fact]
    public async Task Consolidate_FactTooShort_Rejected()
    {
        var consolidateur = Build("rules|1.0|ok", Episode("un echange"));

        await consolidateur.ConsolidateAsync();

        Assert.Empty(_ecrits);
    }

    [Fact]
    public async Task Consolidate_ExistingMemories_RecalledToModel()
    {
        // Sans ça, chaque passe réécrit les mêmes faits et la mémoire se duplique.
        LLMRequest? envoye = null;
        _memoires.Setup(m => m.GetAllAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new List<MemoryVector>
                 {
                     Episode("un echange"),
                     Durable(MemorySlot.Decisions, "Fait deja connu de la memoire")
                 });
        _llm.Setup(c => c.StreamTurnAsync(It.IsAny<LLMRequest>(), It.IsAny<Func<string, Task>>(), It.IsAny<CancellationToken>()))
            .Callback<LLMRequest, Func<string, Task>, CancellationToken>((r, _, _) => envoye = r)
            .ReturnsAsync(new LLMTurn { Content = "AUCUN" });

        var consolidateur = new MemoryConsolidator(
            _unitOfWork.Object, _llm.Object, _memoryService.Object,
            Mock.Of<ILogger<MemoryConsolidator>>());

        await consolidateur.ConsolidateAsync();

        Assert.NotNull(envoye);
        Assert.Contains("Fait deja connu de la memoire", envoye!.SystemPrompt);
        Assert.Contains("ne pas répéter", envoye.SystemPrompt);
    }
}
