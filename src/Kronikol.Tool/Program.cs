using Kronikol.Tool;

if (args.Length == 0)
{
    Console.Error.WriteLine("Kronikol command-line tool.");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Commands:");
    Console.Error.WriteLine("  merge    Combine mergeable TestRunReport.json files into one TestRunReport.html.");
    Console.Error.WriteLine("  ingest   Replay NDJSON interaction captures (any language) into a full Kronikol report.");
    Console.Error.WriteLine("  query    Answer questions about a TestRunReport.json without reading it.");
    Console.Error.WriteLine("  export   Push NDJSON interaction captures to an OTLP/HTTP collector as OpenTelemetry spans.");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Run 'kronikol merge --help', 'kronikol ingest --help', 'kronikol query --help' or 'kronikol export --help' for details.");
    return 2;
}

return args[0] switch
{
    "merge" => MergeCommand.Run(args[1..], Console.Out, Console.Error),
    "ingest" => IngestCommand.Run(args[1..], Console.Out, Console.Error),
    "query" => QueryCommand.Run(args[1..], Console.Out, Console.Error),
    "export" => ExportCommand.Run(args[1..], Console.Out, Console.Error),
    "-h" or "--help" or "help" => PrintTopLevelHelp(),
    _ => UnknownCommand(args[0])
};

static int PrintTopLevelHelp()
{
    Console.Out.WriteLine("Kronikol command-line tool.");
    Console.Out.WriteLine();
    MergeCommand.PrintUsage(Console.Out);
    Console.Out.WriteLine();
    IngestCommand.PrintUsage(Console.Out);
    Console.Out.WriteLine();
    QueryCommand.PrintUsage(Console.Out);
    Console.Out.WriteLine();
    ExportCommand.PrintUsage(Console.Out);
    return 0;
}

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"Unknown command: {command}");
    Console.Error.WriteLine("Run 'kronikol --help' for available commands.");
    return 2;
}
