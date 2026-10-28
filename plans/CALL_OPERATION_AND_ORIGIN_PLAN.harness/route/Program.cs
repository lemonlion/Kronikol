// Prototype of the operation key of plans/CALL_OPERATION_AND_ORIGIN_PLAN.md (4.1, 4.2, 4.4), measured.
// Prints aggregates and excerpts under 120 characters only; never a whole report, body or diagram.
//
// usage:
//   dotnet run -c Release --project route.csproj -- corpus <outFile>
//       the GraphQL step over every body GraphQlOperationDetectorTests.cs passes to the detector, plus probes
//   dotnet run -c Release --project route.csproj -- measure <outDir> <label>=<analysisFile>[;<engineFile>] ...
//       analysisFile: any report form, walked as JSON; engineFile: the file the engine-path timing scans (default the same)

using System.Globalization;
using RouteProbe;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

return args.Length switch
{
    >= 2 when args[0] == "corpus" => Corpus.Run(args[1]),
    >= 3 when args[0] == "measure" => Measure.Run(args[1], args[2..]),
    _ => Usage()
};

static int Usage()
{
    Console.Error.WriteLine("usage: corpus <outFile> | measure <outDir> <label>=<analysisFile>[;<engineFile>] ...");
    return 2;
}
