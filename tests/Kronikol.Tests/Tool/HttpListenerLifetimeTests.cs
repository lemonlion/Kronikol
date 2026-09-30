using System.Text.RegularExpressions;

namespace Kronikol.Tests.Tool;

/// <summary>
/// On Linux the managed <see cref="System.Net.HttpListener"/> binds its port again when it is disposed after
/// <c>Stop()</c>: closing removes its prefixes a second time and, finding the endpoint gone, opens a new one. When a test
/// running in parallel has taken the port by then, <c>Dispose</c> throws "Address already in use", as it did in the
/// 4.0.0 Release run (<c>ExportCommandTests</c>), after the test itself had passed. A listener a test disposes is closed
/// there, once.
/// </summary>
public class HttpListenerLifetimeTests
{
    private static readonly string TestsRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tests"));

    [Fact]
    public void No_test_stops_a_listener_it_also_disposes()
    {
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(TestsRoot, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                                 && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            // Code, not commentary: a comment may name the call it warns against.
            var text = string.Join('\n', File.ReadAllLines(file).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
            foreach (Match declared in Regex.Matches(text, @"using\s+var\s+(\w+)\s*=\s*new\s+(?:System\.Net\.)?HttpListener\s*\("))
                if (Regex.IsMatch(Scope(text, declared.Index), $@"\b{declared.Groups[1].Value}\s*\.\s*Stop\s*\(\s*\)"))
                    offenders.Add($"{Path.GetRelativePath(TestsRoot, file).Replace('\\', '/')} ({declared.Groups[1].Value})");
        }

        Assert.True(offenders.Count == 0,
            "these tests stop an HttpListener that `using` also disposes, which on Linux binds its port a second time: "
            + string.Join(", ", offenders) + ". Let the `using` close it.");
    }

    /// <summary>The text from a <c>using var</c> declaration to the end of the block it lives in.</summary>
    private static string Scope(string text, int declaration)
    {
        var depth = 0;
        for (var i = declaration; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && depth-- == 0) return text[declaration..i];
        }
        return text[declaration..];
    }

    [Fact]
    public void The_guard_reads_only_the_block_the_listener_lives_in()
    {
        const string source = """
            void Serve()
            {
                using var listener = new HttpListener();
                listener.Start();
            }
            int FreePort()
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Stop();
                return 0;
            }
            """;
        var declared = Regex.Match(source, @"using\s+var\s+(\w+)\s*=\s*new\s+HttpListener\s*\(");

        Assert.DoesNotContain("listener.Stop()", Scope(source, declared.Index));
        Assert.Contains("listener.Stop()", Scope(source.Replace("listener.Start();", "listener.Start();\n    listener.Stop();"), declared.Index));
    }
}
