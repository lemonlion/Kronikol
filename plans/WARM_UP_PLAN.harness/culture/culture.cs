// What Kronikol's culture-sensitive duration formats write on a comma-decimal machine (plans/WARM_UP_PLAN.md F12).
// Run: dotnet run --file culture.cs
using System.Globalization;

foreach (var name in new[] { "en-US", "de-DE", "fr-FR" })
{
    CultureInfo.CurrentCulture = new CultureInfo(name);
    var seconds = 1.2345;
    var widthPercent = 12.345;
    var ms = 1265.4;
    // ReportGenerator.cs:5059, :5205 (XML) and :5346, :5492 (YAML): DurationSeconds
    var xmlYaml = seconds.ToString("F3");
    // ReportGenerator.cs:1948: the Scenario Timeline bar's inline width
    var css = $"width:{widthPercent:F1}%";
    // QueryWriter.cs:510 (every duration the query tool prints) and QueryCommand.Overview.cs:123 (Slowest)
    var cli = ms < 1000 ? $"{ms:0} ms" : $"{ms / 1000:0.##} s";
    var slowest = $"{seconds:0.##}s";
    var parses = double.TryParse(xmlYaml, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    Console.WriteLine($"{name}: DurationSeconds={xmlYaml} (parses as a number: {parses}); timeline style=\"{css}\"; query tool \"{cli}\", Slowest \"{slowest}\"");
}
