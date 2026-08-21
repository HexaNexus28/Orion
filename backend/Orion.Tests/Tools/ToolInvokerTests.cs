using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Orion.Business.Tools;
using Orion.Core.Configuration;
using Orion.Core.DTOs.Internal.Tools;
using Orion.Core.DTOs.Responses;
using Orion.Core.Enums;
using Orion.Core.Interfaces.Daemon;
using Orion.Core.Interfaces.Tools;
using Orion.Data.Context;
using Orion.Data.UnitOfWork;

namespace Orion.Tests.Tools;

/// <summary>
/// Le point d'application unique : c'est ici, et nulle part ailleurs, qu'on décide si un outil
/// s'exécute, se diffère ou se refuse.
///
/// Ces tests verrouillent la distinction qui fait tout le sens de la file : « indisponible »
/// n'est pas « inutile ». Une action garde sa valeur demain matin, une lecture non.
/// </summary>
public class ToolInvokerTests : IDisposable
{
    private readonly OrionDbContext _context;
    private readonly UnitOfWork _unitOfWork;
    private readonly Mock<IDaemonClient> _daemon = new();

    public ToolInvokerTests()
    {
        var options = new DbContextOptionsBuilder<OrionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new OrionDbContext(options);
        _unitOfWork = new UnitOfWork(_context);
    }

    public void Dispose()
    {
        _unitOfWork.Dispose();
        _context.Dispose();
    }

    private ToolInvoker Build(params ITool[] tools)
    {
        var registry = new ToolRegistry(Mock.Of<ILogger<ToolRegistry>>(), tools);
        return new ToolInvoker(
            registry,
            _daemon.Object,
            _unitOfWork,
            Options.Create(new DaemonOptions()),
            Mock.Of<ILogger<ToolInvoker>>());
    }

    [Fact]
    public async Task PC_allume_l_outil_s_execute_normalement()
    {
        _daemon.SetupGet(d => d.IsConnected).Returns(true);
        var outil = new OutilFactice("open_app", requiresDaemon: true, deferrable: true);

        var resultat = await Build(outil).InvokeAsync(
            "open_app", new JsonObject { ["appName"] = "notepad" }, ToolInvocationContext.Direct);

        Assert.True(resultat.Data!.Success);
        Assert.True(outil.AEteExecute);
        Assert.Empty(_context.DeferredActions);
    }

    [Fact]
    public async Task PC_eteint_un_outil_differable_est_mis_en_file_et_non_execute()
    {
        _daemon.SetupGet(d => d.IsConnected).Returns(false);
        var outil = new OutilFactice("git_commit", requiresDaemon: true, deferrable: true, destructive: true);

        var resultat = await Build(outil).InvokeAsync(
            "git_commit",
            new JsonObject { ["message"] = "wip" },
            new ToolInvocationContext(null, ToolInvocationContext.OrigineChat, "commit le travail"));

        Assert.False(outil.AEteExecute);
        Assert.True(resultat.Data!.Success);

        var enFile = Assert.Single(_context.DeferredActions);
        Assert.Equal("git_commit", enFile.ToolName);
        Assert.Equal(DeferredActionStatus.Pending, enFile.Status);
        Assert.Contains("wip", enFile.Arguments);
        Assert.Equal("commit le travail", enFile.RequestedBy);
    }

    [Fact]
    public async Task Le_caractere_destructif_est_fige_dans_la_ligne_pas_relu_plus_tard()
    {
        // Une action déjà en file doit garder le régime sous lequel elle a été demandée,
        // même si le drapeau de l'outil change ensuite dans le code.
        _daemon.SetupGet(d => d.IsConnected).Returns(false);
        var outil = new OutilFactice("run_script", requiresDaemon: true, deferrable: true, destructive: true);

        await Build(outil).InvokeAsync("run_script", new JsonObject(), ToolInvocationContext.Direct);

        Assert.True(Assert.Single(_context.DeferredActions).IsDestructive);
    }

    [Fact]
    public async Task PC_eteint_une_lecture_est_refusee_franchement_et_n_encombre_pas_la_file()
    {
        _daemon.SetupGet(d => d.IsConnected).Returns(false);
        var outil = new OutilFactice("list_files", requiresDaemon: true, deferrable: false);

        var resultat = await Build(outil).InvokeAsync("list_files", new JsonObject(), ToolInvocationContext.Direct);

        Assert.False(outil.AEteExecute);
        Assert.False(resultat.Data!.Success);
        Assert.Equal("daemon_offline", resultat.Data.ErrorCode);
        Assert.Empty(_context.DeferredActions);
    }

    [Fact]
    public async Task Un_outil_sans_daemon_marche_meme_PC_eteint()
    {
        _daemon.SetupGet(d => d.IsConnected).Returns(false);
        var outil = new OutilFactice("memory_save", requiresDaemon: false);

        var resultat = await Build(outil).InvokeAsync("memory_save", new JsonObject(), ToolInvocationContext.Direct);

        Assert.True(outil.AEteExecute);
        Assert.True(resultat.Data!.Success);
    }

    [Fact]
    public async Task Un_outil_inconnu_rend_404_sans_rien_enfiler()
    {
        _daemon.SetupGet(d => d.IsConnected).Returns(false);

        var resultat = await Build().InvokeAsync("outil_fantome", new JsonObject(), ToolInvocationContext.Direct);

        Assert.Equal(404, resultat.StatusCode);
        Assert.Empty(_context.DeferredActions);
    }

    [Fact]
    public async Task InvokeNow_execute_meme_si_le_daemon_est_declare_absent()
    {
        // Chemin du drain : à ce moment le daemon vient de revenir. Re-différer ferait tourner
        // l'action en rond jusqu'à son expiration.
        _daemon.SetupGet(d => d.IsConnected).Returns(false);
        var outil = new OutilFactice("open_app", requiresDaemon: true, deferrable: true);

        var resultat = await Build(outil).InvokeNowAsync("open_app", new JsonObject());

        Assert.True(outil.AEteExecute);
        Assert.True(resultat.Data!.Success);
        Assert.Empty(_context.DeferredActions);
    }

    [Fact]
    public async Task Un_outil_qui_leve_rend_un_echec_au_lieu_de_casser_la_boucle()
    {
        _daemon.SetupGet(d => d.IsConnected).Returns(true);
        var outil = new OutilFactice("open_app", requiresDaemon: true, deferrable: true) { Explose = true };

        var resultat = await Build(outil).InvokeAsync("open_app", new JsonObject(), ToolInvocationContext.Direct);

        Assert.True(resultat.Success);
        Assert.False(resultat.Data!.Success);
    }

    private sealed class OutilFactice : ITool
    {
        public OutilFactice(string name, bool requiresDaemon = false, bool deferrable = false, bool destructive = false)
        {
            Name = name;
            RequiresDaemon = requiresDaemon;
            IsDeferrable = deferrable;
            IsDestructive = destructive;
        }

        public string Name { get; }
        public string Description => "outil de test";
        public JsonObject InputSchema => new() { ["type"] = "object" };
        public bool RequiresDaemon { get; }
        public bool IsDestructive { get; }
        public bool IsDeferrable { get; }

        public bool AEteExecute { get; private set; }
        public bool Explose { get; init; }

        public Task<ApiResponse<ToolResult>> ExecuteAsync(JsonObject input, CancellationToken ct = default)
        {
            if (Explose) throw new InvalidOperationException("le daemon a raccroché");

            AEteExecute = true;
            return Task.FromResult(ApiResponse<ToolResult>.SuccessResponse(
                ToolResult.SuccessResult(new { ok = true }, Name)));
        }
    }
}
