using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
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
    /// <summary>Instancie un outil en résolvant chacune de ses dépendances.</summary>
    private static ITool? TryCreate(Type toolType)
    {
        var constructor = toolType.GetConstructors().FirstOrDefault();
        if (constructor is null) return null;

        var args = new List<object?>();
        foreach (var parameter in constructor.GetParameters())
        {
            var dependance = Resolve(parameter.ParameterType);
            if (dependance is null) return null;
            args.Add(dependance);
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

    /// <summary>
    /// Fabrique une dépendance, par bouchon quand c'est possible et RÉELLEMENT sinon.
    ///
    /// Le bouchon seul ne suffit plus : `UrlScope` est scellé, donc Moq ne sait pas le dériver,
    /// et un `Mock&lt;IOptions&lt;T&gt;&gt;` rend un `.Value` nul qui fait exploser le constructeur
    /// qui le lit. Dans les deux cas l'ancienne version renvoyait `null`, et l'outil DISPARAISSAIT
    /// silencieusement du balayage — une couverture qui rétrécit sans que rien ne rougisse.
    /// C'est exactement le genre de perte que ce fichier existe pour empêcher, d'où le garde
    /// <see cref="Registry_Scan_MissesNoTool"/>.
    /// </summary>
    private static object? Resolve(Type type)
    {
        // IOptions<T> : rendre une vraie valeur par défaut, jamais un bouchon dont .Value est nul.
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IOptions<>))
        {
            var contenu = type.GetGenericArguments()[0];
            var valeur = Activator.CreateInstance(contenu);
            // "Create" en litteral : `nameof(Options.Create)` porte sur une methode GENERIQUE,
            // dont le compilateur ne peut pas inferer les arguments de type dans ce contexte.
            return typeof(Options).GetMethod("Create")!
                .MakeGenericMethod(contenu)
                .Invoke(null, new[] { valeur });
        }

        try
        {
            var mockType = typeof(Mock<>).MakeGenericType(type);
            return ((Mock)Activator.CreateInstance(mockType)!).Object;
        }
        catch
        {
            // Type scellé ou sans constructeur dérivable : on tente l'instance RÉELLE, en
            // résolvant ses propres dépendances de la même façon.
            var constructeur = type.GetConstructors().FirstOrDefault();
            if (constructeur is null) return null;

            var args = new List<object?>();
            foreach (var parametre in constructeur.GetParameters())
            {
                var dependance = Resolve(parametre.ParameterType);
                if (dependance is null) return null;
                args.Add(dependance);
            }

            try { return constructeur.Invoke(args.ToArray()); }
            catch { return null; }
        }
    }

    /// <summary>
    /// Le balayage ci-dessous ignore ce qu'il n'arrive pas à construire. Sans ce garde, ajouter
    /// une dépendance non résoluble à un outil le retirerait de TOUTES les vérifications de
    /// schéma — et le test resterait vert.
    /// </summary>
    [Fact]
    public void Registry_Scan_MissesNoTool()
    {
        var decouverts = typeof(ToolRegistry).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ITool).IsAssignableFrom(t))
            .Select(t => t.Name)
            .OrderBy(n => n)
            .ToList();

        var construits = typeof(ToolRegistry).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ITool).IsAssignableFrom(t))
            .Where(t => TryCreate(t) is not null)
            .Select(t => t.Name)
            .OrderBy(n => n)
            .ToList();

        var manquants = decouverts.Except(construits).ToList();

        Assert.True(manquants.Count == 0,
            "Ces outils n'ont pas pu être instanciés et échappent donc à toute vérification "
            + $"de schéma : {string.Join(", ", manquants)}. Ajouter leur dépendance à Resolve().");
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
    public void Schema_Emitted_IsValidJsonSchemaObject(string name, ITool tool)
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
    public void Schema_RequiredFields_ExistInProperties(string name, ITool tool)
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
    public void Schema_NameAndDescription_LetModelChoose(string name, ITool tool)
    {
        Assert.Matches("^[a-z][a-z0-9_]*$", tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description),
            $"'{name}' : sans description, le modèle ne peut pas savoir quand l'utiliser.");
    }
}
