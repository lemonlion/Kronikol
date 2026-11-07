using System.Reflection;
using System.Runtime.CompilerServices;

namespace Kronikol.Tracking;

/// <summary>
/// Where a call handed to a chain of handlers goes (#219): one end per path through it. A delegating handler hands the
/// call to its inner handler. A handler that holds another handler, or an <see cref="HttpMessageInvoker"/> (an
/// <see cref="HttpClient"/> is one), hands it on to what it holds, and each one it holds is a path. Any other handler is
/// an end: a transport, which carries the call to the service, or a handler that answers the call itself, among them a
/// delegating handler with no inner handler. The walk reads fields only: it runs no getter and sends nothing.
/// </summary>
internal sealed class HandlerChain
{
    private enum End { Socket, Transport, Answerer, Unknown }

    private static readonly HandlerChain Sockets = new(true, null);
    private static readonly HandlerChain OtherTransport = new(false, null);
    private static readonly HandlerChain Unknown = new(false, null);

    // Named rather than referenced: TestServer's handler is internal to Microsoft.AspNetCore.TestHost, and the platform
    // handlers live in assemblies a test project seldom loads.
    private static readonly HashSet<string> Transports = new(StringComparer.Ordinal)
    {
        "Microsoft.AspNetCore.TestHost.ClientHandler",
        "System.Net.Http.WinHttpHandler",
        "System.Net.Http.BrowserHttpHandler",
        "System.Net.Http.NSUrlSessionHandler",
        "System.Net.Http.CFNetworkHandler",
        "Foundation.NSUrlSessionHandler",
        "Xamarin.Android.Net.AndroidMessageHandler",
    };

    // A chain longer than this is not followed to its end, and reads as one whose end is not known.
    internal const int MostHandlers = 64;

    private static readonly ConditionalWeakTable<Type, FieldInfo[]> HeldFields = new();

    private HandlerChain(bool transportWritesTraceContext, string? answeredBy)
    {
        TransportWritesTraceContext = transportWritesTraceContext;
        AnsweredBy = answeredBy;
    }

    /// <summary>
    /// Whether every end is a socket transport: SocketsHttpHandler, and HttpClientHandler on top of it, run the
    /// framework's DiagnosticsHandler, which starts a span for the call and puts it in the call's <c>traceparent</c>.
    /// </summary>
    public bool TransportWritesTraceContext { get; }

    /// <summary>
    /// What answered in the service's place (#190): the type name of the handler at the end of the chain when every end
    /// answers the call itself, so a stub, a fake or a mock library's handler; the first such end when there are several.
    /// Null when any end is a transport, which carries the call to the service, or is not known.
    /// </summary>
    public string? AnsweredBy { get; }

    public static HandlerChain Of(HttpMessageHandler? start) => Of(start, HandlerOf);

    /// <summary>The walk, reading an invoker's handler through <paramref name="handlerOf"/>, which gives null where it cannot.</summary>
    internal static HandlerChain Of(HttpMessageHandler? start, Func<HttpMessageInvoker, HttpMessageHandler?> handlerOf)
    {
        // A chain of delegating handlers that ends in a transport is answered with nothing allocated, as before #219.
        var handler = start;
        for (var passed = 0; handler is DelegatingHandler { InnerHandler: { } inner } && passed < MostHandlers; passed++)
            handler = inner;
        switch (Kind(handler))
        {
            case End.Socket: return Sockets;
            case End.Transport: return OtherTransport;
            case End.Unknown: return Unknown;
        }

        var ends = new List<(End Kind, HttpMessageHandler? Handler)>();
        var seen = new HashSet<HttpMessageHandler>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<HttpMessageHandler?>();
        pending.Push(start);
        while (pending.Count > 0)
        {
            handler = pending.Pop();
            var looped = false;
            while (handler is DelegatingHandler { InnerHandler: { } inner } delegating)
            {
                if (!seen.Add(delegating))
                {
                    looped = true;
                    break;
                }
                handler = inner;
            }
            if (looped || handler is not null && !seen.Add(handler))
                continue;
            if (seen.Count > MostHandlers)
            {
                ends.Add((End.Unknown, handler));
                break;
            }
            var kind = Kind(handler);
            if (kind != End.Answerer)
            {
                ends.Add((kind, handler));
                continue;
            }
            var held = Held(handler!, seen, handlerOf, out var unreadable);
            if (unreadable)
                ends.Add((End.Unknown, handler));
            else if (held.Count == 0)
                ends.Add((End.Answerer, handler));
            for (var i = held.Count - 1; i >= 0; i--)
                pending.Push(held[i]);
        }

        if (ends.Count == 0)
            return Unknown;
        if (ends.TrueForAll(e => e.Kind == End.Socket))
            return Sockets;
        return ends.TrueForAll(e => e.Kind == End.Answerer) ? new HandlerChain(false, ends[0].Handler!.GetType().Name) : OtherTransport;
    }

    private static End Kind(HttpMessageHandler? handler) => handler switch
    {
        null => End.Unknown,
        SocketsHttpHandler or HttpClientHandler => End.Socket,
        _ when Transports.Contains(handler.GetType().FullName ?? "") => End.Transport,
        _ => End.Answerer,
    };

    /// <summary>
    /// The handlers <paramref name="handler"/> holds that the walk has not passed: a handler it passed already (itself,
    /// or one before it) is no way onward, since a call handed back to it would never end.
    /// </summary>
    private static List<HttpMessageHandler> Held(HttpMessageHandler handler, HashSet<HttpMessageHandler> seen,
        Func<HttpMessageInvoker, HttpMessageHandler?> handlerOf, out bool unreadable)
    {
        var held = new List<HttpMessageHandler>();
        unreadable = false;
        foreach (var field in FieldsOf(handler.GetType()))
        {
            switch (field.GetValue(handler))
            {
                case HttpMessageHandler next when !seen.Contains(next):
                    held.Add(next);
                    break;
                case HttpMessageInvoker invoker:
                    if (handlerOf(invoker) is not { } inner)
                        unreadable = true;
                    else if (!seen.Contains(inner))
                        held.Add(inner);
                    break;
            }
        }
        return held;
    }

    /// <summary>The fields of a handler's type and its bases, up to <see cref="HttpMessageHandler"/>, that can hold the rest of a chain.</summary>
    private static FieldInfo[] FieldsOf(Type type) => HeldFields.GetValue(type, static t =>
    {
        var fields = new List<FieldInfo>();
        for (var level = t; level is not null && level != typeof(HttpMessageHandler); level = level.BaseType)
            foreach (var field in level.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                if (typeof(HttpMessageHandler).IsAssignableFrom(field.FieldType) || typeof(HttpMessageInvoker).IsAssignableFrom(field.FieldType))
                    fields.Add(field);
        return [.. fields];
    });

    /// <summary>
    /// The handler an invoker sends through. No member exposes it; the field has had this name since .NET Core 2.1.
    /// Null when a runtime names it otherwise, and the invoker is then an end whose kind is not known.
    /// </summary>
    internal static HttpMessageHandler? HandlerOf(HttpMessageInvoker invoker)
    {
        try
        {
            return InvokerHandler(invoker);
        }
        catch (MissingFieldException)
        {
            return null;
        }
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_handler")]
    private static extern ref HttpMessageHandler InvokerHandler(HttpMessageInvoker invoker);
}
