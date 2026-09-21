#:project src/Kronikol/Kronikol.csproj
using Kronikol.History;

foreach (var path in args)
{
    var read = HistoryLedgerReader.Read(path, 50);
    var current = read.Ledger!.Runs("Big").Last();
    var roster = read.Ledger.Roster(current.RosterHash)!;
    var shapes = read.Ledger.Shapes(current.ShapesHash!);
    HistoryAnalyzer.Analyse(read.Ledger, roster, current, new HistoryAnalysisOptions(), shapes: shapes); // warm
    var readings = new List<long>();
    for (var i = 0; i < 3; i++)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var verdicts = HistoryAnalyzer.Analyse(read.Ledger, roster, current, new HistoryAnalysisOptions(), shapes: shapes);
        readings.Add(GC.GetAllocatedBytesForCurrentThread() - before);
        GC.KeepAlive(verdicts);
    }
    Console.WriteLine($"{path}: scenarios {roster.Count}, allocated per analysis [{string.Join(", ", readings.Select(b => (b / 1024.0 / 1024.0).ToString("F1") + " MB"))}], {readings[0] / roster.Count} bytes per scenario");
}
