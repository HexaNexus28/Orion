using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Moq;
using Orion.Business.Tools.System;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Interfaces.Daemon;

namespace Orion.Tests.Tools;

/// <summary>
/// Verrouille le contrat de payload entre les tools backend et les actions du daemon.
///
/// Ces deux moitiés vivent dans des solutions séparées et se parlent en JSON non typé : rien ne
/// les empêche de diverger silencieusement. C'est arrivé — `OpenAppTool` envoyait `appName`
/// alors que `OpenAppAction` lisait `application`, et `ReadFileTool` envoyait `filePath` pour un
/// `path` attendu. Le symptôme visible était un timeout opaque, jamais un message d'erreur.
///
/// Les clés attendues ci-dessous sont celles réellement lues par
/// `daemon/Orion.Daemon.Actions/*.cs`. Si l'une des deux moitiés change, ce test rougit.
/// </summary>
public class DaemonToolContractTests
{
    private static (Mock<IDaemonClient> Daemon, List<DaemonActionRequest> Sent) BuildDaemon()
    {
        var sent = new List<DaemonActionRequest>();
        var daemon = new Mock<IDaemonClient>();

        daemon.SetupGet(d => d.IsConnected).Returns(true);
        daemon
            .Setup(d => d.SendActionAsync(It.IsAny<DaemonActionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<DaemonActionRequest, CancellationToken>((request, _) => sent.Add(request))
            .ReturnsAsync(ApiResponse<DaemonActionResponse>.SuccessResponse(new DaemonActionResponse
            {
                Success = true,
                Data = new { ok = true }
            }));

        return (daemon, sent);
    }

    /// <summary>Sérialise le payload comme il partira sur le fil, puis en extrait les clés.</summary>
    private static HashSet<string> WireKeys(DaemonActionRequest request)
    {
        var json = JsonSerializer.Serialize(request.Payload);
        var node = JsonNode.Parse(json)?.AsObject();
        return node is null
            ? new HashSet<string>()
            : node.Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public async Task OpenAppTool_envoie_la_cle_attendue_par_le_daemon()
    {
        // OpenAppAction lit payload.GetProperty("application") — PAS "appName".
        var (daemon, sent) = BuildDaemon();
        var tool = new OpenAppTool(daemon.Object, Mock.Of<ILogger<OpenAppTool>>());

        await tool.ExecuteAsync(new JsonObject { ["appName"] = "notepad" }, CancellationToken.None);

        var request = Assert.Single(sent);
        Assert.Equal("open_app", request.Action);
        Assert.Contains("application", WireKeys(request));
        Assert.Equal("notepad", JsonNode.Parse(JsonSerializer.Serialize(request.Payload))!["application"]!.GetValue<string>());
    }

    [Fact]
    public async Task ReadFileTool_envoie_la_cle_attendue_par_le_daemon()
    {
        // ReadFileAction lit payload.GetProperty("path") — PAS "filePath".
        var (daemon, sent) = BuildDaemon();
        var tool = new ReadFileTool(daemon.Object, Mock.Of<ILogger<ReadFileTool>>());

        await tool.ExecuteAsync(new JsonObject { ["filePath"] = @"C:\tmp\a.txt" }, CancellationToken.None);

        var request = Assert.Single(sent);
        Assert.Equal("read_file", request.Action);
        Assert.Contains("path", WireKeys(request));
    }

    [Fact]
    public async Task WriteFileTool_envoie_path_et_content()
    {
        var (daemon, sent) = BuildDaemon();
        var tool = new WriteFileTool(daemon.Object, Mock.Of<ILogger<WriteFileTool>>());

        await tool.ExecuteAsync(
            new JsonObject { ["path"] = @"C:\tmp\a.txt", ["content"] = "bonjour" },
            CancellationToken.None);

        var keys = WireKeys(Assert.Single(sent));
        Assert.Contains("path", keys);
        Assert.Contains("content", keys);
    }

    [Fact]
    public async Task GitCommitTool_envoie_message()
    {
        var (daemon, sent) = BuildDaemon();
        var tool = new GitCommitTool(daemon.Object, Mock.Of<ILogger<GitCommitTool>>());

        await tool.ExecuteAsync(
            new JsonObject { ["path"] = @"C:\repo", ["message"] = "feat: test" },
            CancellationToken.None);

        Assert.Contains("message", WireKeys(Assert.Single(sent)));
    }

    [Fact]
    public async Task OpenBrowserUrlTool_envoie_url()
    {
        var (daemon, sent) = BuildDaemon();
        var tool = new OpenBrowserUrlTool(daemon.Object, Mock.Of<ILogger<OpenBrowserUrlTool>>());

        await tool.ExecuteAsync(new JsonObject { ["url"] = "https://example.com" }, CancellationToken.None);

        Assert.Contains("url", WireKeys(Assert.Single(sent)));
    }

    [Fact]
    public async Task RunScriptTool_envoie_script()
    {
        var (daemon, sent) = BuildDaemon();
        var tool = new RunScriptTool(daemon.Object, Mock.Of<ILogger<RunScriptTool>>());

        await tool.ExecuteAsync(new JsonObject { ["script"] = "Get-Date" }, CancellationToken.None);

        Assert.Contains("script", WireKeys(Assert.Single(sent)));
    }
}
