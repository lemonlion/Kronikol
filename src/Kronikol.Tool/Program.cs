using Kronikol.Tool;

// Everything is in Commands.Dispatch so that it is reachable from a test: top-level statements are not.
return Commands.Dispatch(args, Console.Out, Console.Error);
