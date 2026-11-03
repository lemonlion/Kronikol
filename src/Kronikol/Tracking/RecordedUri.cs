namespace Kronikol.Tracking;

/// <summary>
/// What a record's address says, whichever shape it has. A consumer may log any <see cref="Uri"/>, a relative one among them
/// (<c>new Uri("orders", UriKind.Relative)</c> through <see cref="MessageTracker"/>), and <see cref="Uri"/> refuses these members on
/// a relative one with an <see cref="InvalidOperationException"/>, which cost a run its diagram and its report.
/// </summary>
internal static class RecordedUri
{
    /// <summary>The path and query, or a relative address as it was written.</summary>
    internal static string PathAndQuery(Uri uri) => uri.IsAbsoluteUri ? uri.PathAndQuery : uri.OriginalString;

    /// <summary>A recorded address read as an absolute one. On Linux <see cref="Uri.TryCreate(string, UriKind, out Uri)"/> takes <c>/orders?page=1</c> for a file, whose query it escapes into the path.</summary>
    internal static bool TryAbsolute(string address, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Uri? uri)
    {
        uri = null;
        return address.Contains("://", StringComparison.Ordinal) && Uri.TryCreate(address, UriKind.Absolute, out uri);
    }

    /// <summary>The path, or a relative address as it was written up to its query or fragment.</summary>
    internal static string AbsolutePath(Uri uri)
    {
        if (uri.IsAbsoluteUri)
            return uri.AbsolutePath;

        var written = uri.OriginalString;
        var end = written.IndexOfAny(['?', '#']);
        return end < 0 ? written : written[..end];
    }
}
