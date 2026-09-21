#:project src/Kronikol/Kronikol.csproj
using System.Diagnostics;
using Kronikol.History;

foreach (var path in args)
{
    var readWatch = Stopwatch.StartNew();
    var read = HistoryLedgerReader.Read(path, 50);
    readWatch.Stop();
    var current = read.Ledger!.Runs("Big").Last();
    var roster = read.Ledger.Roster(current.RosterHash)!;
    var times = new List<long>();
    for (var i = 0; i < 4; i++)
    {
        var watch = Stopwatch.StartNew();
        HistoryAnalyzer.Analyse(read.Ledger, roster, current, new HistoryAnalysisOptions());
        times.Add(watch.ElapsedMilliseconds);
    }

    // The lookup alone: what AnalyseScenario asks of every prior roster, for every scenario.
    var lookup = Stopwatch.StartNew();
    var found = 0;
    for (var r = 0; r < 50; r++)
        for (var i = 0; i < roster.Count; i++)
            if (roster.IndexOf(roster.Ids[i], roster.Slots[i]) >= 0) found++;
    lookup.Stop();

    Console.WriteLine($"{path}: scenarios {roster.Count}, read {readWatch.ElapsedMilliseconds} ms, analyse [{string.Join(", ", times)}] ms, IndexOf x50 rosters alone {lookup.ElapsedMilliseconds} ms ({found} found)");
}
