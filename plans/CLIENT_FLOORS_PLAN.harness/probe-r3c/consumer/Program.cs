using System.Text.RegularExpressions;
using Kronikol;

try
{
    Console.WriteLine($"layout engine: {IkvmPlantUmlRenderer.LayoutEngine}");
    foreach (var (name, source) in new[]
    {
        ("sequence", "@startuml\nparticipant A\nparticipant B\nA -> B : hello\n@enduml"),
        ("component", "@startuml\ncomponent [Payments API]\ncomponent [Ledger]\n[Payments API] --> [Ledger]\n@enduml"),
    })
    {
        var svg = System.Text.Encoding.UTF8.GetString(IkvmPlantUmlRenderer.Render(source, PlantUmlImageFormat.Svg));
        var text = Regex.Replace(Regex.Replace(svg, "<[^>]+>", " "), @"\s+", " ").Trim();
        Console.WriteLine($"--- {name}: {svg.Length} bytes; text: {text[..Math.Min(text.Length, 400)]}");
    }
}
catch (Exception e)
{
    Console.WriteLine($"FAILED: {e.GetType().FullName}: {e.Message}");
    for (var inner = e.InnerException; inner != null; inner = inner.InnerException)
        Console.WriteLine($"  inner: {inner.GetType().FullName}: {inner.Message}");
    return 1;
}
return 0;
