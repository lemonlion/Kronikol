using System.Globalization;

namespace Kronikol.Query;

/// <summary>
/// <c>sizes</c>: what fills the report file, so a report that outgrew a limit can be explained without opening it
/// (#119, <c>plans/REPORT_FORMAT_PLAN.md</c> section 4.9). The bytes by part, stored and decoded, and the scenarios
/// that hold the most. <see cref="ReportSizes"/> makes the measurement.
/// </summary>
public static partial class QueryCommand
{
    /// <summary>How many scenarios the answer lists, largest first.</summary>
    internal const int LargestScenarios = 10;

    private static int Sizes(ReportIndex index, QueryOptions options, QueryWriter writer, TextWriter error)
    {
        if (options.Positional.Count > 0)
        {
            error.WriteLine($"sizes measures the whole file; it has no per-scenario form ({options.Positional[0]} was given).");
            error.WriteLine("For one scenario's calls and the bytes they carry: `interactions <address> --group-by service`.");
            return 2;
        }

        ReportSizes sizes;
        using (var stream = ReportScanner.OpenShared(index.Path))
        {
            // Measured by a second pass over the file the index was built from, so it must still be that file. One check
            // after the pass covers both ways it can stop being so: a file replaced before the pass is measured whole and
            // then differs from the index, and one rewritten during it differs on the handle the pass read. Half of a
            // rewrite is not JSON, which the pass meets first.
            try
            {
                sizes = ReportSizes.Measure(stream);
            }
            catch (System.Text.Json.JsonException)
            {
                ReportScanner.ThrowIfChanged(stream, index);
                throw;
            }
            ReportScanner.ThrowIfChanged(stream, index);
        }

        var decodedTotal = sizes.DecodedTotal;
        string Share(long bytes) => decodedTotal == 0 ? "-" : (100.0 * bytes / decodedTotal).ToString("0.0", CultureInfo.InvariantCulture) + "%";
        static string Bytes(long bytes) => bytes.ToString("N0", CultureInfo.InvariantCulture);

        writer.Data("report", new
        {
            file = Path.GetFileName(index.Path),
            bytesOnDisk = index.FileLength,
            decodedBytes = decodedTotal,
            formatVersion = sizes.FormatVersion,
            compressedPayloads = sizes.CompressedPayloads
        });

        writer.Line($"{Path.GetFileName(index.Path)}: {Bytes(index.FileLength)} bytes on disk, {Bytes(decodedTotal)} decoded"
                    + (sizes.FormatVersion is { } version ? $" · formatVersion {version}" : "")
                    + (sizes.CompressedPayloads > 0 ? $" · {Bytes(sizes.CompressedPayloads)} payloads stored compressed" : ""));
        writer.Line($"{"part",-22} {"stored",13} {"decoded",13} {"share",6}");

        for (var part = 0; part < ReportSizes.PartNames.Length; part++)
        {
            var name = ReportSizes.PartNames[part];
            var stored = sizes.Stored(part);
            var decoded = sizes.Decoded(part);
            var detail = name switch
            {
                "steps" => $"{Bytes(sizes.StepEmptyKeyBytes)} of them keys holding null or nothing",
                "calls" => "everything in a call but its body",
                "bodies" => $"{Bytes(sizes.BodyCount)} bodies, {Bytes(sizes.DistinctBodies)} distinct: {Bytes(sizes.DistinctBodyBytes)} decoded",
                "whitespace" => "outside strings",
                _ => ""
            };
            writer.Line($"{name,-22} {Bytes(stored),13} {Bytes(decoded),13} {Share(decoded),6}" + (detail.Length > 0 ? "  " + detail : ""));

            var item = new Dictionary<string, object?>
            {
                ["part"] = name,
                ["storedBytes"] = stored,
                ["decodedBytes"] = decoded
            };
            if (name == "steps")
                item["emptyKeyBytes"] = sizes.StepEmptyKeyBytes;
            if (name == "bodies")
            {
                item["count"] = sizes.BodyCount;
                item["distinct"] = sizes.DistinctBodies;
                item["distinctDecodedBytes"] = sizes.DistinctBodyBytes;
            }
            writer.Item(item);
        }

        var largest = sizes.Scenarios
            .Select((bytes, ordinal) => (Ordinal: ordinal, bytes.Stored, bytes.Decoded))
            .OrderByDescending(s => s.Decoded)
            .ThenBy(s => s.Ordinal)
            .Take(LargestScenarios)
            .ToList();

        writer.Data("largestScenarios", largest.Select(s => new
        {
            address = $"s{s.Ordinal}",
            feature = index.Scenario(s.Ordinal)?.FeatureName,
            scenario = index.Scenario(s.Ordinal)?.Name,
            storedBytes = s.Stored,
            decodedBytes = s.Decoded
        }).ToList());

        if (largest.Count > 0)
        {
            writer.Line();
            writer.Line(largest.Count < sizes.Scenarios.Count
                ? $"The {largest.Count} largest of {sizes.Scenarios.Count} scenarios, decoded:"
                : "Scenarios, largest first, decoded:");
            foreach (var (ordinal, _, decoded) in largest)
            {
                var scenario = index.Scenario(ordinal);
                writer.Line($"{"s" + ordinal.ToString(CultureInfo.InvariantCulture),-6} {Bytes(decoded),13} {Share(decoded),6}  "
                            + QueryWriter.OneLine($"{scenario?.FeatureName} / {scenario?.Name}", 90));
            }
        }

        return 0;
    }
}
