namespace Kronikol.Tool.Query;

/// <summary>
/// The flags every <c>kronikol query</c> command shares, plus the positional arguments each one reads for
/// itself. Parsed once so that <c>--max-bytes</c>, <c>--offset</c> and <c>--out</c> mean the same thing
/// everywhere — a budget an agent has to re-learn per command is a budget it will get wrong.
/// </summary>
internal sealed class QueryOptions
{
    public string? File { get; private set; }
    public List<string> Positional { get; } = [];

    public int MaxBytes { get; private set; } = 6000;
    public int Offset { get; private set; }
    public int Limit { get; private set; } = int.MaxValue;
    public bool Count { get; private set; }
    public string? Out { get; private set; }

    public string? Result { get; private set; }
    public string? Feature { get; private set; }
    public string? Label { get; private set; }
    public string? Service { get; private set; }
    public string? Status { get; private set; }
    public string? Method { get; private set; }
    public string? Grep { get; private set; }
    public string? Step { get; private set; }
    public string? Sort { get; private set; }
    public string? Path { get; private set; }
    public string? In { get; private set; }
    public double? SlowerThan { get; private set; }
    public (int From, int To)? LineRange { get; private set; }

    public bool Failed { get; private set; }
    public bool ErrorsOnly { get; private set; }
    public bool Headers { get; private set; }
    public bool Body { get; private set; }
    public bool Keys { get; private set; }
    public bool Values { get; private set; }
    public bool Group { get; private set; }
    public bool Raw { get; private set; }

    /// <summary>Null when a flag was malformed; the message has already been written to <paramref name="error"/>.</summary>
    public static QueryOptions? Parse(IReadOnlyList<string> args, TextWriter error)
    {
        var options = new QueryOptions();

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            string? Next(string flag)
            {
                if (++i < args.Count)
                    return args[i];
                error.WriteLine("Missing value for " + flag);
                return null;
            }

            switch (arg)
            {
                case "--max-bytes":
                    if (Next(arg) is not { } maxBytes) return null;
                    if (!int.TryParse(maxBytes, out var parsedMax) || parsedMax < 0)
                    {
                        error.WriteLine("--max-bytes takes a non-negative number of bytes (0 removes the budget).");
                        return null;
                    }
                    options.MaxBytes = parsedMax;
                    break;

                case "--offset":
                    if (Next(arg) is not { } offset) return null;
                    if (!int.TryParse(offset, out var parsedOffset) || parsedOffset < 0)
                    {
                        error.WriteLine("--offset takes a non-negative row number.");
                        return null;
                    }
                    options.Offset = parsedOffset;
                    break;

                case "--limit":
                    if (Next(arg) is not { } limit) return null;
                    if (!int.TryParse(limit, out var parsedLimit) || parsedLimit <= 0)
                    {
                        error.WriteLine("--limit takes a positive row count.");
                        return null;
                    }
                    options.Limit = parsedLimit;
                    break;

                case "--slower-than":
                    if (Next(arg) is not { } slower) return null;
                    if (!double.TryParse(slower.TrimEnd('s'), out var parsedSlower))
                    {
                        error.WriteLine("--slower-than takes a number of seconds.");
                        return null;
                    }
                    options.SlowerThan = parsedSlower;
                    break;

                case "--lines":
                    if (Next(arg) is not { } lines) return null;
                    var range = lines.Split('-', 2);
                    if (range.Length != 2 || !int.TryParse(range[0], out var from) || !int.TryParse(range[1], out var to))
                    {
                        error.WriteLine("--lines takes a range like 20-60.");
                        return null;
                    }
                    options.LineRange = (from, to);
                    break;

                case "--out": if (Next(arg) is not { } output) return null; options.Out = output; break;
                case "--result": if (Next(arg) is not { } result) return null; options.Result = result; break;
                case "--feature": if (Next(arg) is not { } feature) return null; options.Feature = feature; break;
                case "--label": if (Next(arg) is not { } label) return null; options.Label = label; break;
                case "--service": if (Next(arg) is not { } service) return null; options.Service = service; break;
                case "--status": if (Next(arg) is not { } status) return null; options.Status = status; break;
                case "--method": if (Next(arg) is not { } method) return null; options.Method = method; break;
                case "--grep": if (Next(arg) is not { } grep) return null; options.Grep = grep; break;
                case "--step": if (Next(arg) is not { } step) return null; options.Step = step; break;
                case "--sort": if (Next(arg) is not { } sort) return null; options.Sort = sort; break;
                case "--path": if (Next(arg) is not { } path) return null; options.Path = path; break;
                case "--in": if (Next(arg) is not { } inTargets) return null; options.In = inTargets; break;

                case "--count": options.Count = true; break;
                case "--failed": options.Failed = true; break;
                case "--errors-only": options.ErrorsOnly = true; break;
                case "--headers": options.Headers = true; break;
                case "--body": options.Body = true; break;
                case "--keys": options.Keys = true; break;
                case "--values": options.Values = true; break;
                case "--group": options.Group = true; break;
                case "--raw": options.Raw = true; break;

                default:
                    if (arg.StartsWith("--", StringComparison.Ordinal))
                    {
                        error.WriteLine("Unknown option: " + arg);
                        return null;
                    }

                    if (options.File is null)
                        options.File = arg;
                    else
                        options.Positional.Add(arg);
                    break;
            }
        }

        return options;
    }

    /// <summary>The flags that must be repeated for a paged re-run to mean the same thing.</summary>
    public string RerunPrefix()
    {
        var parts = new List<string>();
        if (Service is not null) parts.Add($"--service {Service}");
        if (Status is not null) parts.Add($"--status {Status}");
        if (Method is not null) parts.Add($"--method {Method}");
        if (Grep is not null) parts.Add($"--grep \"{Grep}\"");
        if (Result is not null) parts.Add($"--result {Result}");
        if (Feature is not null) parts.Add($"--feature \"{Feature}\"");
        if (Label is not null) parts.Add($"--label {Label}");
        if (Failed) parts.Add("--failed");
        if (Group) parts.Add("--group");
        return parts.Count == 0 ? "" : string.Join(" ", parts) + " ";
    }
}
