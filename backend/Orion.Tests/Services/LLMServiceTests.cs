using Moq;
using Orion.Business.Services;
using Orion.Core.DTOs.Requests;
using Orion.Core.DTOs.Responses;
using Orion.Core.Enums;
using Orion.Core.Interfaces.LLM;

namespace Orion.Tests.Services;

/// <summary>
/// `ILLMService` est la frontière métier au-dessus du transport LLM : le métier interroge
/// CE service, jamais un client LLM directement. Ces tests verrouillent son contrat.
/// </summary>
public class LLMServiceTests
{
    private static Mock<ILLMAgentClient> Client(LLMProvider provider, string model)
    {
        var mock = new Mock<ILLMAgentClient>();
        mock.SetupGet(c => c.Provider).Returns(provider);
        mock.SetupGet(c => c.ModelId).Returns(model);
        return mock;
    }

    [Fact]
    public void GetStatus_expose_le_fournisseur_ET_le_modele_actif()
    {
        // Le modèle fait partie du contrat : c'est l'information qui manquait quand ORION
        // tournait sur un repli dégradé sans que rien ne le signale.
        var service = new LLMService(Client(LLMProvider.Nim, "nvidia/nemotron-3-super-120b-a12b").Object);

        var result = service.GetStatus();

        Assert.True(result.Success);
        Assert.Equal(LLMProvider.Nim, result.Data!.Provider);
        Assert.Equal("nvidia/nemotron-3-super-120b-a12b", result.Data.Model);
        Assert.True(result.Data.IsOnline);
    }

    [Fact]
    public void GetStatus_signale_hors_ligne_quand_aucun_fournisseur_n_est_elu()
    {
        var service = new LLMService(Client(LLMProvider.None, "aucun").Object);

        var result = service.GetStatus();

        Assert.True(result.Success);
        Assert.False(result.Data!.IsOnline);
        Assert.Equal(LLMProvider.None, result.Data.Provider);
    }

    [Fact]
    public void HealthService_reporte_le_modele_dans_le_health_check()
    {
        var llm = new Mock<Orion.Core.Interfaces.Services.ILLMService>();
        llm.Setup(s => s.GetStatus()).Returns(ApiResponse<LLMStatusDto>.SuccessResponse(new LLMStatusDto
        {
            Provider = LLMProvider.Nim,
            Model = "nvidia/nemotron-3-super-120b-a12b",
            IsOnline = true
        }));

        var result = new HealthService(llm.Object).GetHealthStatus();

        Assert.True(result.Success);
        Assert.Equal("Nim", result.Data!.LlmProvider);
        Assert.Equal("nvidia/nemotron-3-super-120b-a12b", result.Data.LlmModel);
    }

    [Fact]
    public void HealthService_dit_None_quand_le_LLM_est_absent()
    {
        var llm = new Mock<Orion.Core.Interfaces.Services.ILLMService>();
        llm.Setup(s => s.GetStatus()).Returns(ApiResponse<LLMStatusDto>.SuccessResponse(new LLMStatusDto
        {
            Provider = LLMProvider.None,
            Model = "aucun",
            IsOnline = false
        }));

        var result = new HealthService(llm.Object).GetHealthStatus();

        Assert.Equal("None", result.Data!.LlmProvider);
    }

    [Fact]
    public void Le_metier_ne_depend_jamais_du_transport_directement()
    {
        // Garde-fou d'architecture : HealthService doit se construire avec un ILLMService,
        // pas avec un client LLM. Si quelqu'un recâble le raccourci, ceci ne compile plus.
        var constructors = typeof(HealthService).GetConstructors();
        var parameters = Assert.Single(constructors).GetParameters();

        Assert.Single(parameters);
        Assert.Equal(typeof(Orion.Core.Interfaces.Services.ILLMService), parameters[0].ParameterType);
    }

    [Fact]
    public void Le_contrat_du_service_reste_minimal()
    {
        // Zéro code mort : le service n'expose que ce qui a un appelant réel.
        var methods = typeof(Orion.Core.Interfaces.Services.ILLMService).GetMethods();

        Assert.Single(methods);
        Assert.Equal(nameof(Orion.Core.Interfaces.Services.ILLMService.GetStatus), methods[0].Name);
    }
}
