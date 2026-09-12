using System.Text;

namespace Kronikol.Tool.Query;

/// <summary>
/// Writes to two sinks at once. Used for one thing: under <c>--json</c> the tool's error prose still has
/// to reach stderr exactly as it always has, while a copy is kept so a failure can also be reported on
/// stdout as an envelope. Reproducing the message rather than re-deriving it is deliberate — the sentence
/// a verb chose is the one a caller should see, and forty call sites choose their own.
/// </summary>
internal sealed class TeeTextWriter(TextWriter primary, TextWriter copy) : TextWriter
{
    public override Encoding Encoding => primary.Encoding;

    public override void Write(char value)
    {
        primary.Write(value);
        copy.Write(value);
    }

    public override void Write(string? value)
    {
        primary.Write(value);
        copy.Write(value);
    }

    public override void WriteLine(string? value)
    {
        primary.WriteLine(value);
        copy.WriteLine(value);
    }

    public override void Flush()
    {
        primary.Flush();
        copy.Flush();
    }
}
