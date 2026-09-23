namespace Kronikol.Tests.Reports;

/// <summary>
/// <see cref="Console.SetOut"/> is process-wide, and a run's diagnostics lines (<c>Report diagnostics: …</c>,
/// the warnings under it) carry no directory to scope them by, so <see cref="ConsoleLines.About"/> cannot
/// pick them out. This captures only what the creating thread writes: report generation prints its
/// diagnostics synchronously on the caller's thread, while a test in another collection printing meanwhile
/// writes on its own thread and passes straight through to the writer that was installed before.
/// </summary>
internal sealed class ThreadScopedConsole : IDisposable
{
    private readonly TextWriter _previous;
    private readonly StringWriter _captured = new();
    private readonly int _thread = Environment.CurrentManagedThreadId;

    public ThreadScopedConsole()
    {
        _previous = Console.Out;
        Console.SetOut(new Router(this));
    }

    /// <summary>Everything the creating thread wrote to the console since construction.</summary>
    public string Text => _captured.ToString();

    public void Dispose() => Console.SetOut(_previous);

    private TextWriter Target => Environment.CurrentManagedThreadId == _thread ? _captured : _previous;

    private sealed class Router(ThreadScopedConsole owner) : TextWriter
    {
        public override System.Text.Encoding Encoding => owner._previous.Encoding;
        public override void Write(char value) => owner.Target.Write(value);
        public override void Write(string? value) => owner.Target.Write(value);
        public override void Write(char[] buffer, int index, int count) => owner.Target.Write(buffer, index, count);
        public override void Flush() => owner.Target.Flush();
    }
}
