using Kronikol.Tool;

if (args.Length == 0)
{
    Console.Error.WriteLine("Kronikol command-line tool.");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Commands:");
    Console.Error.WriteLine("  merge   Combine mergeable TestRunReport.json files into one TestRunReport.html.");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Run 'kronikol merge --help' for details.");
    return 2;
}

return args[0] switch
{
    "merge" => MergeCommand.Run(args[1..], Console.Out, Console.Error),
    "-h" or "--help" or "help" => PrintTopLevelHelp(),
    _ => UnknownCommand(args[0])
};

static int PrintTopLevelHelp()
{
    Console.Out.WriteLine("Kronikol command-line tool.");
    Console.Out.WriteLine();
    MergeCommand.PrintUsage(Console.Out);
    return 0;
}

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"Unknown command: {command}");
    Console.Error.WriteLine("Run 'kronikol --help' for available commands.");
    return 2;
}
