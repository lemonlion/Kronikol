using Kronikol;

// Calls the formatter the prototype feeds ITest.DisplayName to (and Kronikol's ApplyResults feeds a matched
// display name to) on the strings given, or on the four the plan asked about. One line per input:
// the input as a JSON string, then "=> <output as a JSON string>" or "THROWS <exception type>: <message>".
string[] inputs = args.Length > 0
    ? args
    : ["Order API returns 404 for v1.2", "Does the thing.", "Ns.Class.M(x: Foo())", ""];

foreach (var input in inputs)
{
    string outcome;
    try
    {
        outcome = "=> " + System.Text.Json.JsonSerializer.Serialize(ScenarioTitleResolver.FormatScenarioDisplayName(input));
    }
    catch (Exception ex)
    {
        outcome = $"THROWS {ex.GetType().FullName}: {ex.Message}";
    }
    Console.WriteLine($"FormatScenarioDisplayName({System.Text.Json.JsonSerializer.Serialize(input)}) {outcome}");
}
