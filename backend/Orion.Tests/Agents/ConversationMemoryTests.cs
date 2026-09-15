using Microsoft.Extensions.Logging;
using Moq;
using Orion.Business.Agents;
using Orion.Business.LLM;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Entities;
using Orion.Core.Enums;
using Orion.Core.Interfaces.Agents;
using Orion.Core.Interfaces.Daemon;
using Orion.Core.Interfaces.LLM;
using Orion.Core.Interfaces.Repositories;
using Orion.Core.Interfaces.Services;
using Orion.Core.Interfaces.Tools;

namespace Orion.Tests.Agents;

/// <summary>
/// Un échange terminé DOIT laisser une trace en mémoire.
///
/// Rien n'écrivait automatiquement : les seuls chemins d'écriture étaient les outils
/// `memory_save` et `memory_reflect`, donc uniquement si le modèle y pensait. La table restait
/// quasi vide, et trois symptômes en découlaient — ORION ne se souvenait de rien, le briefing
/// sortait générique faute de matière à injecter, et l'écran mémoire n'affichait rien.
///
/// Ces tests verrouillent l'écriture ET son absence dans le seul cas où elle serait du bruit.
/// </summary>
public class ConversationMemoryTests
{
    private readonly Mock<IAgentLoop> _boucle = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IMessageRepository> _messages = new();
    private readonly Mock<IMemoryService> _memoire = new();
    private readonly Mock<IDaemonClient> _daemon = new();

    private ConversationAgent Construire(params string[] jetons)
    {
        _unitOfWork.SetupGet(u => u.Messages).Returns(_messages.Object);
        _messages
            .Setup(m => m.AddAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Message m, CancellationToken _) => m);
        _unitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _memoire
            .Setup(m => m.SaveMemoryAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<float>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<bool>.SuccessResponse(true));

        _boucle
            .Setup(b => b.RunAsync(
                It.IsAny<LLMRequest>(),
                It.IsAny<Func<string, string, CancellationToken, Task<ToolOutcome>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Emettre(jetons));

        return new ConversationAgent(
            _boucle.Object,
            new Mock<ILLMAgentClient>().Object,
            _unitOfWork.Object,
            new Mock<IEmbeddingService>().Object,
            new PromptBuilder(),
            new Mock<IToolRegistry>().Object,
            new Mock<IToolInvoker>().Object,
            _daemon.Object,
            _memoire.Object,
            new Mock<ILogger<ConversationAgent>>().Object);
    }

    private static async IAsyncEnumerable<AgentEvent> Emettre(string[] jetons)
    {
        foreach (var jeton in jetons)
        {
            yield return AgentEvent.Token(jeton, 1);
        }
        await Task.CompletedTask;
    }

    private static StreamContext Contexte(string demande) => new()
    {
        SessionId = Guid.NewGuid(),
        ConversationId = Guid.NewGuid(),
        UserMessage = demande,
        LlmRequest = new LLMRequest()
    };

    private static async Task ConsommerAsync(IAsyncEnumerable<AgentEvent> flux)
    {
        await foreach (var _ in flux) { }
    }

    [Fact]
    public async Task CompletedTurn_IsWrittenAsEpisode()
    {
        var agent = Construire("Il est ", "14 h 30.");

        await ConsommerAsync(agent.StreamLLMAsync(Contexte("Quelle heure est-il ?")));

        _memoire.Verify(m => m.SaveMemoryAsync(
            It.IsAny<string>(),
            nameof(MemorySlot.Episode),
            It.IsAny<float>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Episode_CarriesBothHalvesOfTheExchange()
    {
        var agent = Construire("Il est 14 h 30.");
        string? ecrit = null;

        _memoire
            .Setup(m => m.SaveMemoryAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<float>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, float, CancellationToken>((contenu, _, _, _) => ecrit = contenu)
            .ReturnsAsync(ApiResponse<bool>.SuccessResponse(true));

        await ConsommerAsync(agent.StreamLLMAsync(Contexte("Quelle heure est-il ?")));

        // Un episode coupe en deux perdrait le lien entre la demande et la reponse : la
        // distillation relirait des fragments sans contexte.
        Assert.NotNull(ecrit);
        Assert.Contains("Quelle heure est-il ?", ecrit);
        Assert.Contains("Il est 14 h 30.", ecrit);
    }

    [Fact]
    public async Task SilentModel_WritesNoEpisode()
    {
        // Le modele peut rendre zero caractere sans erreur. ORION repond alors un aveu fabrique —
        // le stocker comme souvenir polluerait la distillation avec ses propres excuses.
        var agent = Construire();

        await ConsommerAsync(agent.StreamLLMAsync(Contexte("Une demande sans reponse")));

        _memoire.Verify(m => m.SaveMemoryAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<float>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task MemoryFailure_DoesNotBreakTheTurn()
    {
        // La reponse est deja rendue quand l'ecriture a lieu. Une panne d'embedding ou de base
        // ne doit pas transformer un tour reussi en erreur.
        var agent = Construire("Une reponse complete.");

        _memoire
            .Setup(m => m.SaveMemoryAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<float>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("base injoignable"));

        var exception = await Record.ExceptionAsync(
            () => ConsommerAsync(agent.StreamLLMAsync(Contexte("Une demande"))));

        Assert.Null(exception);
    }
}
