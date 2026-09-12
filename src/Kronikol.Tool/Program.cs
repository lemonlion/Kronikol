using System.Text;
using Kronikol.Tool;

// Stdout is UTF-8 because the tool has already decided it is: every line is charged against
// --max-bytes with Encoding.UTF8.GetByteCount, and the addresses a reader is told to feed back are
// separated by "·" and "›". Without this the encoding is whatever code page the host
// console reports - 437 or 1252 on a default Windows machine, neither of which has those characters,
// so they encode to "?" and a byte budget is enforced in an encoding the bytes are not in. Unix is
// already UTF-8, which is exactly why this went unnoticed: it is a no-op there and on any terminal
// somebody has set to 65001.
//
// Guarded because the setter throws when there is no stdout handle to configure at all - a detached
// or service-hosted process - and losing the run over the encoding of output nobody is reading would
// be the worse trade.
try
{
    Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
}
catch (Exception exception) when (exception is IOException or PlatformNotSupportedException)
{
    // Keep the host default.
}

// Everything is in Commands.Dispatch so that it is reachable from a test: top-level statements are not.
return Commands.Dispatch(args, Console.Out, Console.Error);
