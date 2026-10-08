using System.Diagnostics.CodeAnalysis;

namespace Kronikol.Tracking;

/// <summary>
/// The form a test identity value (a test name, a test id, a caller name) takes in an HTTP header or in gRPC
/// metadata. A value that is printable ASCII, neither begins nor ends with a space and does not begin with
/// <c>UTF-8''</c> is sent as it is. Any other value is sent as an RFC 8187 <c>ext-value</c>, the form
/// <c>Content-Disposition: filename*=</c> uses: <c>UTF-8''</c> followed by the value's UTF-8 bytes, percent-encoded.
/// <para>
/// The encoded form is always printable ASCII with no space, so it crosses a real HTTP/1.1 or HTTP/2 connection,
/// where a raw non-ASCII value fails the request and a line feed fails it even in memory, and a server cannot strip
/// surrounding spaces from it. <see cref="Decode"/> gives the value back exactly; the one exception is a lone
/// surrogate, which is not valid UTF-16 and comes back as U+FFFD.
/// </para>
/// </summary>
internal static class TrackingHeaderValue
{
    /// <summary>The prefix that marks an encoded value: RFC 8187's charset and an empty language tag.</summary>
    internal const string Prefix = "UTF-8''";

    /// <summary>
    /// The value as it goes on the wire: unchanged when it is plain printable ASCII, otherwise <c>UTF-8''</c>
    /// and the percent-encoded UTF-8 bytes.
    /// </summary>
    [return: NotNullIfNotNull(nameof(value))]
    internal static string? Encode(string? value)
    {
        if (value is null || IsPlain(value))
            return value;

        return Prefix + Uri.EscapeDataString(value);
    }

    /// <summary>
    /// The value a header carried, decoded when it is in the <c>UTF-8''</c> form and returned as it is otherwise.
    /// A malformed escape is left as written; nothing throws.
    /// </summary>
    [return: NotNullIfNotNull(nameof(value))]
    internal static string? Decode(string? value)
    {
        if (value is null || !value.StartsWith(Prefix, StringComparison.Ordinal))
            return value;

        return Uri.UnescapeDataString(value.Substring(Prefix.Length));
    }

    private static bool IsPlain(string value)
    {
        if (value.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        if (value.Length > 0 && (value[0] == ' ' || value[value.Length - 1] == ' '))
            return false;

        foreach (var c in value)
        {
            if (c < (char)0x20 || c > (char)0x7E)
                return false;
        }

        return true;
    }
}
