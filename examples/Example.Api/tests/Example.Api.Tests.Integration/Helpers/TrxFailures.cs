using System.Xml;
using System.Xml.Linq;

namespace Example.Api.Tests.Integration.Helpers;

/// <summary>
/// What a nested run's results file says went wrong: each failed test's message and stack trace, a message shared by
/// several tests once, then the run's own messages. A test assembly's cleanup failure reaches the console as one line
/// naming the exception's type (the ReqNRoll xUnit3 example on CI, 2026-10-13 and 2026-10-20), and the VSTest adapter
/// attaches its message and stack trace to each of the assembly's tests, so this is where they can be read.
/// </summary>
public static class TrxFailures
{
    private static readonly XNamespace Ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    public static string Read(string path)
    {
        XDocument trx;
        try { trx = XDocument.Load(path); }
        catch (Exception e) when (e is IOException or XmlException or UnauthorizedAccessException) { return ""; }

        var failures = trx.Descendants(Ns + "UnitTestResult")
            .Where(r => (string?)r.Attribute("outcome") == "Failed")
            .Select(r => r.Element(Ns + "Output")?.Element(Ns + "ErrorInfo"))
            .Where(e => e is not null)
            .Select(e => string.Join("\n", new[] { (string?)e!.Element(Ns + "Message"), (string?)e.Element(Ns + "StackTrace") }
                .Where(s => !string.IsNullOrWhiteSpace(s))))
            .Where(s => s.Length > 0)
            .Distinct();
        var messages = trx.Descendants(Ns + "RunInfo").Select(i => (string?)i.Element(Ns + "Text"))
            .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct();
        return string.Join("\n\n", failures.Concat(messages!));
    }
}
