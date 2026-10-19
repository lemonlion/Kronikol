# S10a (the tests): databases in deep cyan (R17.22, P35). The six asserts that read the database's colour from the
# palette move to #0E7C86, and a fact pins the component diagram's database box. usage: python s10a-tests-colour.py <root>
import sys
ROOT = sys.argv[1].rstrip('/') + '/'
T = ROOT + 'tests/Kronikol.Tests/'


def edit(path, pairs):
    s = open(path, encoding='utf-8', newline='').read()
    for old, new, *count in pairs:
        n = count[0] if count else 1
        assert s.count(old) == n, (path, old[:80], s.count(old))
        s = s.replace(old, new)
    open(path, 'w', encoding='utf-8', newline='').write(s)


edit(T + 'ComponentDiagram/ComponentDiagramGeneratorTests.cs', [
    ("""        // Database color = #E74C3C
        Assert.Contains("$tags=\\"#E74C3C\\"", result);""",
     """        // Database color = #0E7C86 (deep cyan from 5.0.0: red read as a failure, plans/OPTION_C_PLAN.md R17.22)
        Assert.Contains("$tags=\\"#0E7C86\\"", result);"""),
    ("""        Assert.Contains("#E74C3C", result); // Database""",
     """        Assert.Contains("#0E7C86", result); // Database (deep cyan from 5.0.0, R17.22)"""),
])
edit(T + 'PlantUml/PlantUmlCreatorTests.cs', [
    ("""        Assert.Contains("webApp -[#E74C3C]> cosmosDB:", plantUml);
        Assert.Contains("cosmosDB -[#E74C3C]-> webApp:", plantUml);""",
     """        // a database's colour is deep cyan from 5.0.0 (red read as a failure, plans/OPTION_C_PLAN.md R17.22)
        Assert.Contains("webApp -[#0E7C86]> cosmosDB:", plantUml);
        Assert.Contains("cosmosDB -[#0E7C86]-> webApp:", plantUml);"""),
    ("""        Assert.Contains("database \\"Cosmos DB\\" as cosmosDB #E74C3C", plantUml);""",
     """        Assert.Contains("database \\"Cosmos DB\\" as cosmosDB #0E7C86", plantUml);"""),
    ("""        Assert.Contains("-[#E74C3C]>", plantUml); // Cosmos""",
     """        Assert.Contains("-[#0E7C86]>", plantUml); // Cosmos (deep cyan from 5.0.0, R17.22)"""),
])
edit(T + 'PlantUml/DiagramLookTests.cs', [
    ("""        Assert.Equal("#0E7C86", DependencyPalette.GetColor(DependencyCategories.SQL));
    }""",
     """        Assert.Equal("#0E7C86", DependencyPalette.GetColor(DependencyCategories.SQL));
    }

    // The component diagram's database box is a shade deeper than the arrows, so its white name reads at 6.15:1 (P35).
    [Fact]
    public void A_component_diagrams_database_box_is_deep_cyan()
    {
        var plantUml = ComponentDiagramGenerator.GeneratePlantUml(
            [new ComponentRelationship("App", "Db", "CosmosDB", ["Query"], 1, 1, DependencyCategory: "CosmosDB")]);

        Assert.Contains("skinparam database {\\n  BackgroundColor #0B6C75\\n  FontColor #FFFFFF\\n  BorderColor #09616A\\n}",
            plantUml.ReplaceLineEndings("\\n"));
    }"""),
    ("using Kronikol.Constants;\n", "using Kronikol.ComponentDiagram;\nusing Kronikol.Constants;\n"),
])
print('ok')
