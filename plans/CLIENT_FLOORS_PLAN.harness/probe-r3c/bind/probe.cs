#:property TargetFramework=net10.0
#:property PublishAot=false
#:project C:/Code/Kronikol-r3c/src/Kronikol.PlantUml.Ikvm/Kronikol.PlantUml.Ikvm.csproj
#:package IKVM@8.16.3
#:package IKVM.Image@8.16.3
#:package IKVM.Image.JDK@8.16.3
#:package IKVM.Image.JRE@8.16.3
// R3c: Kronikol.PlantUml.Ikvm's PlantUML assembly, compiled by the IKVM its project pins (8.9.0), run on IKVM 8.16.3,
// as a consumer that references a newer IKVM runs it. Prints the text of what each diagram draws.
using System.Text.RegularExpressions;
using Kronikol;

Console.WriteLine($"layout engine: {IkvmPlantUmlRenderer.LayoutEngine}");
foreach (var (name, source) in new[]
{
    ("sequence", "@startuml\nparticipant A\nparticipant B\nA -> B : hello\n@enduml"),
    ("component", "@startuml\ncomponent [Payments API]\ncomponent [Ledger]\n[Payments API] --> [Ledger]\n@enduml"),
})
{
    var svg = System.Text.Encoding.UTF8.GetString(IkvmPlantUmlRenderer.Render(source, PlantUmlImageFormat.Svg));
    var text = Regex.Replace(Regex.Replace(svg, "<[^>]+>", " "), @"\s+", " ").Trim();
    Console.WriteLine($"--- {name}: {svg.Length} bytes; text: {text[..Math.Min(text.Length, 1200)]}");
}
