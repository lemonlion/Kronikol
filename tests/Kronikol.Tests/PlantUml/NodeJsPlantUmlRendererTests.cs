using System.Diagnostics;
using Kronikol.PlantUml;

namespace Kronikol.Tests.PlantUml;

public class NodeJsPlantUmlRendererTests
{
    private static bool IsNodeAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo("node", "--version")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(5000);
            return p?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Renders_sequence_diagram_svg()
    {
        Assert.SkipWhen(!IsNodeAvailable(), "Node.js not available on PATH");

        var plantUml = """
            @startuml
            Alice -> Bob : Hello
            @enduml
            """;

        var svgBytes = NodeJsPlantUmlRenderer.Render(plantUml, PlantUmlImageFormat.Svg);
        var svg = System.Text.Encoding.UTF8.GetString(svgBytes);

        Assert.Contains("<svg", svg);
        Assert.Contains("Alice", svg);
        Assert.Contains("Bob", svg);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Non_ascii_text_survives_the_round_trip_to_node()
    {
        Assert.SkipWhen(!IsNodeAvailable(), "Node.js not available on PATH");

        // The loop fragment label Kronikol emits for collapsed calls, plus an accented participant:
        // stdin must be UTF-8 or these come out as `x`, `�` and `?` on Windows (cp1252 console page).
        var plantUml = """
            @startuml
            participant "Zoë" as z
            loop ×2 · 27–43 ms
            z -> Bob : généré
            end
            @enduml
            """;

        var svg = System.Text.Encoding.UTF8.GetString(NodeJsPlantUmlRenderer.Render(plantUml, PlantUmlImageFormat.Svg));

        Assert.Contains("<svg", svg);
        Assert.Contains("×2", svg);
        Assert.Contains("27–43", svg);
        Assert.Contains("Zoë", svg);
        Assert.Contains("généré", svg);
        Assert.DoesNotContain("�", svg);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Renders_class_diagram_svg()
    {
        Assert.SkipWhen(!IsNodeAvailable(), "Node.js not available on PATH");

        var plantUml = """
            @startuml
            class Foo {
              +bar(): void
            }
            class Bar
            Foo --> Bar
            @enduml
            """;

        var svgBytes = NodeJsPlantUmlRenderer.Render(plantUml, PlantUmlImageFormat.Svg);
        var svg = System.Text.Encoding.UTF8.GetString(svgBytes);

        Assert.Contains("<svg", svg);
        Assert.Contains("Foo", svg);
        Assert.Contains("Bar", svg);
    }
}
