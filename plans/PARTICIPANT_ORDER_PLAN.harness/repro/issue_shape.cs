#:project ../../../src/Kronikol/Kronikol.csproj
#:property JsonSerializerIsReflectionEnabledByDefault=true

// The #228 scenario through the real PlantUmlCreator: the test makes its folder on the disk and commits files to git
// (Given), runs the app (When), and the app reads git and keeps a file on the disk. Prints each diagram's declarations
// and arrows, once with the calls' phases unknown (a plain test) and once with Given = Setup and When = Action (what a
// BDD adapter records). Run: dotnet run --file plans/PARTICIPANT_ORDER_PLAN.harness/repro/issue_shape.cs
using System.Net;
using Kronikol.PlantUml;
using Kronikol.Tracking;

foreach (var phased in new[] { false, true })
{
    var logs = Scenario(phased);
    var diagram = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs).Single();
    Console.WriteLine(phased ? "== phases recorded (Given = Setup, When = Action)" : "== phases unknown");
    foreach (var (plantUml, _) in diagram.PlantUmls)
        foreach (var line in plantUml.Split('\n'))
            if (line.StartsWith("actor ") || line.StartsWith("participant ") || line.StartsWith("database ")
                || System.Text.RegularExpressions.Regex.IsMatch(line, @"^\w+ -"))
                Console.WriteLine("  " + line.Trim());
}

static List<RequestResponseLog> Scenario(bool phased)
{
    var logs = new List<RequestResponseLog>();
    void Call(string caller, string service, string method, string path, string category, TestPhase phase)
    {
        var id = Guid.NewGuid();
        var trace = Guid.NewGuid();
        foreach (var type in new[] { RequestResponseType.Request, RequestResponseType.Response })
            logs.Add(new RequestResponseLog("Keeps what it reads", "t1", method, null, new Uri("http://" + service + path),
                [], service, caller, type, trace, id, false,
                type == RequestResponseType.Response ? HttpStatusCode.OK : null,
                DependencyCategory: category)
            { Phase = phased ? phase : TestPhase.Unknown });
    }

    Call("Caller", "disk", "Make a folder", "/work", "Database", TestPhase.Setup);
    Call("Caller", "git", "Commit files", "/repo", "Database", TestPhase.Setup);
    Call("Caller", "app", "Run", "/run", null!, TestPhase.Action);
    Call("app", "git", "Read files", "/repo", "Database", TestPhase.Action);
    Call("app", "disk", "Keep", "/work/out", "Database", TestPhase.Action);
    return logs;
}
