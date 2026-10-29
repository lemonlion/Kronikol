using System.Text.RegularExpressions;
using Azure.Storage.Queues;

namespace Kronikol.Extensions.StorageQueues;

/// <summary>
/// Classifies Azure Storage Queues HTTP requests into specific operations based on URL patterns and HTTP methods.
/// </summary>
public static partial class StorageQueueOperationClassifier
{
    // The path after the account: Azure puts the account in the host, Azurite first in the path,
    // /devstoreaccount1/{queue}/messages (#125).
    [GeneratedRegex(@"^/(?<queue>[^/?]+)(?:/messages(?:/(?<msgId>[^/?]+))?)?$", RegexOptions.Compiled)]
    private static partial Regex QueuePathRegex();

    // Azurite's built-in account, which UseDevelopmentStorage=true and Testcontainers' Azurite module use.
    internal const string AzuriteAccount = "devstoreaccount1";

    // A first segment no account name can be (they are letters and digits), to ask the SDK's parser where a host and
    // port put the account.
    private const string Probe = "path-style";

    public static StorageQueueOperationInfo Classify(HttpRequestMessage request)
    {
        var path = request.RequestUri is { } uri ? ApiPathOf(uri) : "";
        var method = request.Method;
        var query = request.RequestUri?.Query ?? "";

        // List queues: GET /?comp=list
        if (path == "/" && query.Contains("comp=list"))
            return new(StorageQueueOperation.ListQueues, null);

        var match = QueuePathRegex().Match(path);
        if (!match.Success)
            return new(StorageQueueOperation.Other, null);

        var queue = match.Groups["queue"].Value;
        var messageId = match.Groups["msgId"].Success && match.Groups["msgId"].Value.Length > 0
            ? match.Groups["msgId"].Value : null;
        var hasMessages = path.Contains("/messages");

        return (method.Method, hasMessages, messageId is not null) switch
        {
            ("POST", true, false) => new(StorageQueueOperation.SendMessage, queue),
            ("GET", true, false) when query.Contains("peekonly=true") =>
                new(StorageQueueOperation.PeekMessages, queue),
            ("GET", true, false) => new(StorageQueueOperation.ReceiveMessages, queue),
            ("DELETE", true, true) => new(StorageQueueOperation.DeleteMessage, queue, messageId),
            ("PUT", true, true) => new(StorageQueueOperation.UpdateMessage, queue, messageId),
            ("DELETE", true, false) => new(StorageQueueOperation.ClearMessages, queue),
            ("PUT", false, _) when query.Contains("comp=metadata") =>
                new(StorageQueueOperation.SetMetadata, queue),
            ("GET", false, _) when query.Contains("comp=metadata") =>
                new(StorageQueueOperation.GetProperties, queue),
            ("PUT", false, _) => new(StorageQueueOperation.CreateQueue, queue),
            ("DELETE", false, _) => new(StorageQueueOperation.DeleteQueue, queue),
            _ => new(StorageQueueOperation.Other, queue)
        };
    }

    public static string GetDiagramLabel(StorageQueueOperationInfo op, StorageQueueTrackingVerbosity verbosity)
    {
        return verbosity switch
        {
            StorageQueueTrackingVerbosity.Detailed => op.Operation switch
            {
                StorageQueueOperation.SendMessage => $"Send → {op.QueueName}",
                StorageQueueOperation.ReceiveMessages => $"Receive ← {op.QueueName}",
                StorageQueueOperation.PeekMessages => $"Peek ← {op.QueueName}",
                StorageQueueOperation.DeleteMessage => "Delete",
                StorageQueueOperation.UpdateMessage => "Update",
                StorageQueueOperation.ClearMessages => $"Clear → {op.QueueName}",
                _ => op.Operation.ToString()
            },
            StorageQueueTrackingVerbosity.Summarised => op.Operation.ToString(),
            _ => op.Operation.ToString() // Raw uses HTTP method
        };
    }

    /// <summary>
    /// The path a URL names after its account, as sent. On Azure the account is in the host, and the path is the URL's
    /// own; on Azurite it is the path's first segment, left out here. Where the account is comes from the Azure SDK's own
    /// parser (<see cref="QueueUriBuilder"/>), which reads it from the path on an IP address and on the ports it reserves
    /// for path-style URLs, Azurite's among them. On another host and port the parser reads the account as the queue,
    /// and so does this, but for Azurite's built-in account, which is left out wherever the host names none.
    /// </summary>
    internal static string ApiPathOf(Uri uri)
    {
        var path = uri.AbsolutePath;
        var end = path.IndexOf('/', 1);
        var first = end < 0 ? path[1..] : path[1..end];
        if (first.Length == 0)
            return path;

        var account = new QueueUriBuilder(new UriBuilder(uri.Scheme, uri.Host, uri.Port, "/" + Probe + "/q").Uri).AccountName;
        var inPath = account == Probe || (string.IsNullOrEmpty(account) && first == AzuriteAccount);
        if (!inPath)
            return path;
        return end < 0 ? "/" : path[end..];
    }
}
