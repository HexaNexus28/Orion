using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orion.Core.Configuration;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Enums;
using Orion.Core.Interfaces.LLM;

namespace Orion.Business.LLM;

/// <summary>
/// Client Ollama utilisant le CLI directement (plus fiable que l'API HTTP pour les modèles locaux)
/// </summary>
public class OllamaCliClient : ILLMClient
{
    private readonly ILogger<OllamaCliClient> _logger;
    private readonly OllamaOptions _options;

    public OllamaCliClient(IOptions<OllamaOptions> options, ILogger<OllamaCliClient> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public LLMProvider Provider => LLMProvider.Ollama;

    public bool IsAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ollama",
                Arguments = "list",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            process?.WaitForExit(5000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<ApiResponse<LLMResponse>> CompleteAsync(LLMRequest request, CancellationToken ct = default)
    {
        var model = request.Model ?? _options.Model;
        var lastMessage = request.Messages.LastOrDefault()?.Content ?? "";
        
        _logger.LogInformation("[OllamaCli] Calling model {Model} with prompt: {Prompt}", model, lastMessage[..Math.Min(50, lastMessage.Length)]);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ollama",
                Arguments = $"generate {model} --prompt \"{lastMessage.Replace("\"", "\\\"")}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return ApiResponse<LLMResponse>.ErrorResponse("Failed to start ollama process", 500);
            }

            // Use longer timeout for CLI (5 minutes) - model generation can take time
            using var cliCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, cliCts.Token);
            
            var output = await process.StandardOutput.ReadToEndAsync(linkedCts.Token);
            var error = await process.StandardError.ReadToEndAsync(linkedCts.Token);
            
            await process.WaitForExitAsync(linkedCts.Token);

            if (process.ExitCode != 0)
            {
                _logger.LogError("[OllamaCli] Error: {Error}", error);
                return ApiResponse<LLMResponse>.ErrorResponse($"Ollama error: {error}", 500);
            }

            var content = output.Trim();
            _logger.LogInformation("[OllamaCli] Response received: {Length} chars", content.Length);

            return ApiResponse<LLMResponse>.SuccessResponse(new LLMResponse
            {
                Content = content,
                Provider = LLMProvider.Ollama,
                Model = model,
                TokensUsed = null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OllamaCli] Failed to complete");
            return ApiResponse<LLMResponse>.ErrorResponse($"Ollama error: {ex.Message}", 500);
        }
    }

    public async Task StreamAsync(LLMRequest request, Func<string, Task> onChunk, CancellationToken ct = default)
    {
        // CLI mode doesn't support true streaming - simulate with complete response
        var response = await CompleteAsync(request, ct);
        if (response.Success && response.Data != null)
        {
            var words = response.Data.Content.Split(' ');
            foreach (var word in words)
            {
                await onChunk(word + " ");
                await Task.Delay(50, ct);
            }
        }
    }
}
