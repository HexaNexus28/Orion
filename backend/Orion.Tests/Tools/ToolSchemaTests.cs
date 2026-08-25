using System.Reflection;
using System.Text.Json.Nodes;
using Moq;
using Orion.Business.Tools;
using Orion.Core.Interfaces.Tools;

namespace Orion.Tests.Tools;

/// <summary>
/// Le schéma d'entrée d'un outil est ce que le LLM lit pour savoir comment l'appeler.
/// Un schéma malformé rend l'outil inutilisable en silence — le modèle ne proteste pas,
/// il n'appelle simplement jamais l'outil, ou l'appelle avec de mauvais arguments.
///
/// Les 5 outils mémoire déclaraient leurs propriétés À LA RACINE, sans l'enveloppe
/// `type: object` / `properties`. C'est l'une des trois raisons indépendantes pour lesquelles
/// la mémoire d'ORION n'a jamais rien écrit. Ce test balaie TOUS les outils, pas seulement
/// ceux qu'on soupçonne.
/// </summary>
public class ToolSchemaTests
{
    /// <summary>Instancie un outil en bouchonnant chacune de ses dépendances.</summary>
    private static ITool? TryCreate(Type toolType)
    {
        var constructor = toolType.GetConstructors().FirstOrDefault();
        if (constructor is null) return null;

        var args = new List<object?>();
        foreach (var parameter in constructor.GetParameters())
        {
            try
            {
                var mockType = typeof(Mock<>).MakeGenericType(parameter.ParameterType);
                var mock = (Mock)Activator.CreateInstance(mockType)!;
                args.Add(mock.Object);
            }
            catch
            {
                return null; // dépendance non bouchonnable — hors périmètre de ce test
            }
        }

        try
        {
            return (ITool?)constructor.Invoke(args.ToArray());
        }
        catch
        {
            return null;
        }
    }

    public static TheoryData<string, ITool> TousLesOutils()
    {
        var data = new TheoryData<string, ITool>();

        var types = typeof(ToolRegistry).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ITool).IsAssignableFrom(t))
            .OrderBy(t => t.Name);

        foreach (var type in types)
        {
            var tool = TryCreate(type);
            if (tool is not null) data.Add(tool.Name, tool);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(TousLesOutils))]
    public void Le_schema_est_un_objet_JSON_Schema_valide(string name, ITool tool)
    {
        var schema = tool.InputSchema;

        Assert.True(schema.ContainsKey("type"),
            $"'{name}' : le schéma doit déclarer \"type\".");
        Assert.Equal("object", schema["type"]!.GetValue<string>());

        Assert.True(schema.ContainsKey("properties"),
            $"'{name}' : les paramètres doivent être sous \"properties\", pas à la racine du schéma.");

        var properties = schema["properties"] as JsonObject;
        Assert.NotNull(properties);

        foreach (var (property, definition) in properties!)
        {
            var node = Assert.IsType<JsonObject>(definition);
            Assert.True(node.ContainsKey("type"),
                $"'{name}.{property}' : chaque paramètre doit avoir un \"type\".");
            Assert.True(node.ContainsKey("description"),
                $"'{name}.{property}' : sans description, le modèle devine — et devine mal.");
        }
    }

    [Theory]
    [MemberData(nameof(TousLesOutils))]
    public void Les_champs_requis_existent_dans_les_proprietes(string name, ITool tool)
    {
        var schema = tool.InputSchema;
        if (schema["required"] is not JsonArray required) return;

        var properties = schema["properties"] as JsonObject;
        Assert.NotNull(properties);

        foreach (var entry in required)
        {
            var field = entry!.GetValue<string>();
            Assert.True(properties!.ContainsKey(field),
                $"'{name}' : \"{field}\" est declare requis mais absent de \"properties\".");
        }
    }

    [Theory]
    [MemberData(nameof(TousLesOutils))]
    public void Le_nom_et_la_description_permettent_au_modele_de_choisir(string name, ITool tool)
    {
        Assert.Matches("^[a-z][a-z0-9_]*$", tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description),
            $"'{name}' : sans description, le modèle ne peut pas savoir quand l'utiliser.");
    }
}
