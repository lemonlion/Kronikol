using System.Text.RegularExpressions;
using Azure.Storage.Blobs;

namespace Kronikol.Extensions.BlobStorage;

/// <summary>
/// Classifies Azure Blob Storage HTTP requests into specific operations based on URL patterns and HTTP methods.
/// </summary>
public static partial class BlobOperationClassifier
{
    // Azure Blob Storage REST API paths, after the account:
    //   /{container}?restype=container              → container operations
    //   /{container}?restype=container&comp=list     → list blobs
    //   /{container}/{blob}                          → blob operations
    //   /{container}/{blob}?comp=metadata            → metadata operations
    //   /{container}/{blob}?comp=copy                → copy
    //   /{container}/{blob}?comp=block&blockid=...   → put block
    //   /{container}/{blob}?comp=blocklist            → put block list
    //   /{container}/{blob}?comp=lease               → lease
    // Azure puts the account in the host; Azurite puts it first in the path, /devstoreaccount1/{container}/{blob} (#125).
    [GeneratedRegex(
        @"^/(?<container>[^/?]+)(?:/(?<blob>[^?]+))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex BlobPathRegex();

    // Azurite's built-in account, which UseDevelopmentStorage=true and Testcontainers' Azurite module use.
    internal const string AzuriteAccount = "devstoreaccount1";

    // A first segment no account name can be (they are letters and digits), to ask the SDK's parser where a host and
    // port put the account.
    private const string Probe = "path-style";

    public static BlobOperationInfo Classify(HttpRequestMessage request)
    {
        var path = request.RequestUri is { } uri ? ApiPathOf(uri) : "";
        var query = request.RequestUri?.Query ?? "";
        var method = request.Method.Method.ToUpperInvariant();

        var match = BlobPathRegex().Match(path);
        var container = match.Groups["container"].Value;
        var blob = match.Groups["blob"].Value;

        var hasBlob = !string.IsNullOrEmpty(blob);
        var queryParams = ParseQueryString(query);
        var isContainer = queryParams.ContainsKey("restype") &&
                          queryParams["restype"].Equals("container", StringComparison.OrdinalIgnoreCase);
        var comp = queryParams.GetValueOrDefault("comp")?.ToLowerInvariant();

        var operation = (method, hasBlob, isContainer, comp) switch
        {
            // Container operations
            ("PUT", false, true, null or "") => BlobOperation.CreateContainer,
            ("DELETE", false, true, _) => BlobOperation.DeleteContainer,
            (_, false, true, "list") => BlobOperation.ListBlobs,

            // Blob metadata
            ("PUT", true, _, "metadata") => BlobOperation.SetMetadata,
            ("GET", true, _, "metadata") => BlobOperation.GetMetadata,

            // Blob composition
            ("PUT", true, _, "copy") => BlobOperation.Copy,
            ("PUT", true, _, "block") => BlobOperation.PutBlock,
            ("PUT", true, _, "blocklist") => BlobOperation.PutBlockList,

            // Lease
            (_, true, _, "lease") => BlobOperation.Lease,

            // Standard blob CRUD
            ("PUT", true, _, null or "") => BlobOperation.Upload,
            ("GET", true, _, null or "") => BlobOperation.Download,
            ("DELETE", true, _, null or "") => BlobOperation.Delete,
            ("HEAD", true, _, null or "") => BlobOperation.GetProperties,

            _ => BlobOperation.Other
        };

        return new BlobOperationInfo(
            operation,
            NullIfEmpty(container),
            hasBlob ? blob : null);
    }

    public static string GetDiagramLabel(BlobOperationInfo op, BlobTrackingVerbosity verbosity)
    {
        return verbosity switch
        {
            BlobTrackingVerbosity.Summarised or BlobTrackingVerbosity.Detailed => op.Operation.ToString(),
            _ => null!
        };
    }

    /// <summary>
    /// The path a URL names after its account, as sent. On Azure the account is in the host, and the path is the URL's
    /// own; on Azurite it is the path's first segment, left out here. Where the account is comes from the Azure SDK's own
    /// parser (<see cref="BlobUriBuilder"/>), which reads it from the path on an IP address and on the ports it reserves
    /// for path-style URLs, Azurite's among them. On another host and port the parser reads the account as the
    /// container, and so does this, but for Azurite's built-in account, which is left out wherever the host names none.
    /// </summary>
    internal static string ApiPathOf(Uri uri)
    {
        var path = uri.AbsolutePath;
        var end = path.IndexOf('/', 1);
        var first = end < 0 ? path[1..] : path[1..end];
        if (first.Length == 0)
            return path;

        var account = new BlobUriBuilder(new UriBuilder(uri.Scheme, uri.Host, uri.Port, "/" + Probe + "/c/b").Uri).AccountName;
        var inPath = account == Probe || (string.IsNullOrEmpty(account) && first == AzuriteAccount);
        if (!inPath)
            return path;
        return end < 0 ? "/" : path[end..];
    }

    private static Dictionary<string, string> ParseQueryString(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query)) return result;

        var q = query.TrimStart('?');
        foreach (var pair in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eqIndex = pair.IndexOf('=');
            if (eqIndex < 0)
            {
                result[Uri.UnescapeDataString(pair)] = "";
            }
            else
            {
                var key = Uri.UnescapeDataString(pair[..eqIndex]);
                var value = Uri.UnescapeDataString(pair[(eqIndex + 1)..]);
                result[key] = value;
            }
        }
        return result;
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;
}
