using System.Globalization;

namespace Kronikol;

/// <summary>
/// Sets the current culture to the invariant one until disposed, then puts the previous one back. The culture is part of
/// the execution context, so the tasks started inside the scope format under it too, and a scope on one thread leaves
/// every other thread's culture alone.
/// </summary>
/// <remarks>
/// For code whose output is a format other programs read and which runs nothing a consumer wrote (the query engine): a
/// number or date formatted inside it is written the same way on every machine. The report pipeline does not use it,
/// because it calls the consumer's processors and renderers, which keep the consumer's culture; its writers pass
/// <see cref="CultureInfo.InvariantCulture"/> themselves.
/// </remarks>
internal readonly struct InvariantCultureScope : IDisposable
{
    private readonly CultureInfo _previous;

    private InvariantCultureScope(CultureInfo previous) => _previous = previous;

    public static InvariantCultureScope Begin()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        return new InvariantCultureScope(previous);
    }

    public void Dispose()
    {
        if (_previous is not null)
            CultureInfo.CurrentCulture = _previous;
    }
}
