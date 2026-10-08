using System.Globalization;
using System.Xml.Linq;

namespace Kronikol.Tests.xUnit2.Lane;

/// <summary>One test result as the runner recorded it.</summary>
/// <param name="ClassName">The test class's full name.</param>
/// <param name="MethodName">The test method's name.</param>
/// <param name="DisplayName">xUnit's display name: the method path and a row's arguments, or a DisplayName.</param>
/// <param name="Outcome">Passed, Failed or NotExecuted (skipped).</param>
/// <param name="Duration">The runner's duration for the test.</param>
/// <param name="Output">What the test wrote to its ITestOutputHelper.</param>
/// <param name="ErrorMessage">The runner's text of the failure, or null.</param>
public sealed record TrxTest(
    string ClassName,
    string MethodName,
    string DisplayName,
    string Outcome,
    TimeSpan Duration,
    string Output,
    string? ErrorMessage)
{
    public string ClassSimpleName => ClassName[(ClassName.LastIndexOfAny(['.', '+']) + 1)..];

    /// <summary>The calls the test made, in order, from the lines its body wrote before each one.</summary>
    public IReadOnlyList<string> Calls =>
        Output.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("fixture-call: GET ", StringComparison.Ordinal))
            .Select(line => line["fixture-call: GET ".Length..])
            .ToArray();

    public override string ToString() => $"{ClassSimpleName}: {DisplayName} ({Outcome})";
}

/// <summary>A TRX file, read for what the lane compares a report with.</summary>
public sealed class TrxFile
{
    private static readonly XNamespace Ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    public required IReadOnlyList<TrxTest> Tests { get; init; }

    public static TrxFile Read(string path)
    {
        var document = XDocument.Load(path);
        var methods = document.Descendants(Ns + "UnitTest").ToDictionary(
            unit => (string)unit.Attribute("id")!,
            unit => unit.Element(Ns + "TestMethod")!);

        var tests = document.Descendants(Ns + "UnitTestResult").Select(result =>
        {
            var method = methods[(string)result.Attribute("testId")!];
            var output = result.Element(Ns + "Output");
            return new TrxTest(
                ClassName: (string)method.Attribute("className")!,
                MethodName: (string)method.Attribute("name")!,
                DisplayName: (string)result.Attribute("testName")!,
                Outcome: (string)result.Attribute("outcome")!,
                Duration: result.Attribute("duration") is { } duration
                    ? TimeSpan.Parse(duration.Value, CultureInfo.InvariantCulture)
                    : TimeSpan.Zero,
                Output: (string?)output?.Element(Ns + "StdOut") ?? "",
                ErrorMessage: (string?)output?.Element(Ns + "ErrorInfo")?.Element(Ns + "Message"));
        }).ToArray();

        return new TrxFile { Tests = tests };
    }
}
