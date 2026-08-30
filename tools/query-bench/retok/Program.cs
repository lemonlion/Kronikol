using System.Buffers;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// The BCL-only re-tokenization benchmark behind QUERY_PERF_PLAN.md sections 1.3/1.6: a faithful minimal
// scan of a TestRunReport.json doing only the scanner's real obligations - every property name
// materialized, every "content" string unescaped and SHA-1-hashed - with none of the walker's
// bookkeeping. The difference between this and ReportScanner.Scan is the scanner's overhead.
//
//   dotnet run -c Release --project tools/query-bench/retok -- <report.json> [reps]

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: retok <report.json> [reps]");
    return 2;
}

var path = args[0];
var reps = args.Length > 1 ? int.Parse(args[1]) : 3;
var times = new List<double>();

for (var rep = 0; rep < reps + 1; rep++) // one warmup + reps
{
    var watch = Stopwatch.StartNew();
    var (properties, hashed) = Scan(path);
    watch.Stop();
    if (rep > 0)
        times.Add(watch.Elapsed.TotalSeconds);
    Console.WriteLine($"{(rep == 0 ? "warmup" : "rep " + rep),-7} {watch.Elapsed.TotalSeconds:F2} s   {properties:N0} property names, {hashed:N0} content strings hashed");
}

times.Sort();
Console.WriteLine($"median  {times[times.Count / 2]:F2} s over {reps} reps");
return 0;

static (long Properties, long Hashed) Scan(string path)
{
    const int InitialWindow = 128 * 1024;
    using var stream = File.OpenRead(path);
    var buffer = ArrayPool<byte>.Shared.Rent(InitialWindow);
    long properties = 0, hashed = 0;
    var wasContent = false;

    try
    {
        var state = new JsonReaderState(new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip });
        var filled = 0;
        var eof = false;

        while (true)
        {
            if (!eof)
            {
                var read = stream.Read(buffer, filled, buffer.Length - filled);
                filled += read;
                if (read == 0)
                    eof = true;
            }

            var reader = new Utf8JsonReader(buffer.AsSpan(0, filled), eof, state);
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        var name = reader.GetString() ?? "";
                        properties++;
                        wasContent = name == "content";
                        break;
                    case JsonTokenType.String when wasContent:
                        var content = reader.GetString();
                        if (content is not null)
                        {
                            SHA1.HashData(Encoding.UTF8.GetBytes(content));
                            hashed++;
                        }
                        wasContent = false;
                        break;
                    default:
                        wasContent = false;
                        break;
                }
            }
            state = reader.CurrentState;
            var consumed = (int)reader.BytesConsumed;

            if (eof && consumed >= filled)
                break;

            if (consumed == 0 && filled == buffer.Length)
            {
                var bigger = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                buffer.AsSpan(0, filled).CopyTo(bigger);
                ArrayPool<byte>.Shared.Return(buffer);
                buffer = bigger;
                continue;
            }

            if (eof && consumed == 0)
                break;

            Buffer.BlockCopy(buffer, consumed, buffer, 0, filled - consumed);
            filled -= consumed;
        }
    }
    finally
    {
        ArrayPool<byte>.Shared.Return(buffer);
    }

    return (properties, hashed);
}
