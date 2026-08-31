using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Orion.Business.Daemon;
using Orion.Core.DTOs.Responses;
using Orion.Core.Entities;
using Orion.Core.Enums;
using Orion.Core.Interfaces.Tools;
using Orion.Data.Context;
using Orion.Data.UnitOfWork;

namespace Orion.Tests.Daemon;

/// <summary>
/// Le réveil du PC.
///
/// La règle produit tient en une phrase : les actions destructives SE REDEMANDENT, elles ne se
/// rejouent pas. Elles s'exécuteraient sur un état de machine que l'utilisateur n'a pas vu —
/// c'est exactement ce que la file est censée éviter, pas provoquer.
/// </summary>
public class DeferredActionServiceTests : IDisposable
{
    private readonly OrionDbContext _context;
    private readonly UnitOfWork _unitOfWork;
    private readonly Mock<IToolInvoker> _invoker = new();

    public DeferredActionServiceTests()
    {
        var options = new DbContextOptionsBuilder<OrionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new OrionDbContext(options);
        _unitOfWork = new UnitOfWork(_context);

        _invoker
            .Setup(i => i.InvokeNowAsync(It.IsAny<string>(), It.IsAny<JsonObject>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<ToolResult>.SuccessResponse(ToolResult.SuccessResult(new { ok = true })));
    }

    public void Dispose()
    {
        _unitOfWork.Dispose();
        _context.Dispose();
    }

    private DeferredActionService Build() =>
        new(_unitOfWork, _invoker.Object, Mock.Of<ILogger<DeferredActionService>>());

    private async Task<DeferredAction> Enfiler(
        string toolName = "open_app",
        bool destructive = false,
        DeferredActionStatus statut = DeferredActionStatus.Pending,
        DateTime? expiration = null)
    {
        var action = new DeferredAction
        {
            ToolName = toolName,
            Arguments = "{\"appName\":\"notepad\"}",
            IsDestructive = destructive,
            Status = statut,
            ExpiresAt = expiration ?? DateTime.UtcNow.AddHours(12)
        };

        _context.DeferredActions.Add(action);
        await _context.SaveChangesAsync();
        return action;
    }

    [Fact]
    public async Task Drain_NonDestructiveAction_RunsOnItsOwn()
    {
        await Enfiler("open_app");

        var rapport = (await Build().DrainAsync()).Data!;

        Assert.Single(rapport.Executed);
        Assert.Empty(rapport.AwaitingConfirmation);
        Assert.Equal(DeferredActionStatus.Executed, _context.DeferredActions.Single().Status);
    }

    [Fact]
    public async Task Drain_DestructiveAction_AsksAgainNeverReplays()
    {
        await Enfiler("git_commit", destructive: true);

        var rapport = (await Build().DrainAsync()).Data!;

        Assert.Empty(rapport.Executed);
        Assert.Single(rapport.AwaitingConfirmation);
        Assert.Equal(DeferredActionStatus.AwaitingConfirmation, _context.DeferredActions.Single().Status);
        _invoker.Verify(
            i => i.InvokeNowAsync(It.IsAny<string>(), It.IsAny<JsonObject>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Drain_ExpiredAction_ExpiresInsteadOfRunning()
    {
        await Enfiler("open_app", expiration: DateTime.UtcNow.AddMinutes(-1));

        var rapport = (await Build().DrainAsync()).Data!;

        Assert.Equal(1, rapport.Expired);
        Assert.Empty(rapport.Executed);
        Assert.Equal(DeferredActionStatus.Expired, _context.DeferredActions.Single().Status);
        _invoker.Verify(
            i => i.InvokeNowAsync(It.IsAny<string>(), It.IsAny<JsonObject>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Drain_ActionFails_KeptWithItsReason()
    {
        _invoker
            .Setup(i => i.InvokeNowAsync(It.IsAny<string>(), It.IsAny<JsonObject>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<ToolResult>.SuccessResponse(
                ToolResult.ErrorResult("dépôt introuvable", toolName: "git_commit")));

        await Enfiler("git_commit");

        var rapport = (await Build().DrainAsync()).Data!;

        Assert.Single(rapport.Failed);
        var action = _context.DeferredActions.Single();
        Assert.Equal(DeferredActionStatus.Failed, action.Status);
        Assert.Equal("dépôt introuvable", action.Error);
    }

    [Fact]
    public async Task Confirm_DestructiveAction_FinallyRuns()
    {
        var action = await Enfiler("git_commit", destructive: true, statut: DeferredActionStatus.AwaitingConfirmation);

        var resultat = await Build().ConfirmAsync(action.Id);

        Assert.True(resultat.Success);
        Assert.Equal(DeferredActionStatus.Executed, _context.DeferredActions.Single().Status);
        _invoker.Verify(
            i => i.InvokeNowAsync("git_commit", It.IsAny<JsonObject>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Confirm_ExpiredAction_Refused()
    {
        var action = await Enfiler(
            "git_commit",
            destructive: true,
            statut: DeferredActionStatus.AwaitingConfirmation,
            expiration: DateTime.UtcNow.AddMinutes(-5));

        var resultat = await Build().ConfirmAsync(action.Id);

        Assert.Equal(410, resultat.StatusCode);
        Assert.Equal(DeferredActionStatus.Expired, _context.DeferredActions.Single().Status);
    }

    [Fact]
    public async Task Confirm_ActionThatAskedNothing_Refused()
    {
        var action = await Enfiler("open_app", statut: DeferredActionStatus.Executed);

        var resultat = await Build().ConfirmAsync(action.Id);

        Assert.Equal(409, resultat.StatusCode);
    }

    [Fact]
    public async Task Cancel_PendingAction_Succeeds()
    {
        var action = await Enfiler("git_commit", destructive: true);

        var resultat = await Build().CancelAsync(action.Id);

        Assert.True(resultat.Success);
        Assert.Equal(DeferredActionStatus.Cancelled, _context.DeferredActions.Single().Status);
    }

    [Fact]
    public async Task Cancel_CompletedAction_Refused()
    {
        var action = await Enfiler("open_app", statut: DeferredActionStatus.Executed);

        var resultat = await Build().CancelAsync(action.Id);

        Assert.Equal(409, resultat.StatusCode);
    }

    [Fact]
    public async Task Sweep_PcNeverReturns_ExpiresWithoutRunning()
    {
        await Enfiler("open_app", expiration: DateTime.UtcNow.AddHours(-1));
        await Enfiler("git_commit", destructive: true, expiration: DateTime.UtcNow.AddHours(-2));
        await Enfiler("open_browser_url", expiration: DateTime.UtcNow.AddHours(6));

        var expirees = (await Build().ExpireStaleAsync()).Data;

        Assert.Equal(2, expirees);
        Assert.Equal(1, _context.DeferredActions.Count(a => a.Status == DeferredActionStatus.Pending));
        _invoker.Verify(
            i => i.InvokeNowAsync(It.IsAny<string>(), It.IsAny<JsonObject>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task List_Queue_ReturnsPendingAndCompleted()
    {
        await Enfiler("open_app");
        await Enfiler("git_commit", statut: DeferredActionStatus.Executed);

        var file = (await Build().GetQueueAsync()).Data!;

        Assert.Equal(2, file.Count);
        Assert.Contains(file, a => a.Status == "pending");
        Assert.Contains(file, a => a.Status == "executed");
    }
}
