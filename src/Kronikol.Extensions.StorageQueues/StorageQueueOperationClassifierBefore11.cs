using System.Text.RegularExpressions;

namespace Kronikol.Extensions.StorageQueues;

/// <summary>
/// The classifier, label and clean URI as every release before #125's R2 recorded a call, kept unchanged so history's
/// earlier rules can read a call as it was recorded then (<see cref="Tracking.RequestResponseLog.RecordedBefore11"/>,
/// plans/EMULATOR_URL_SHAPES_PLAN.md section 7). It is frozen: an edit here changes what those rules read, and every
/// scenario whose calls it touches would read as changed. <c>RecordedBefore11Tests</c> pins it to what the last package
/// published before the fix recorded.
/// </summary>
internal static partial class StorageQueueOperationClassifierBefore11
{
    [GeneratedRegex(@"^/(?<queue>[^/?]+)(?:/messages(?:/(?<msgId>[^/?]+))?)?$", RegexOptions.Compiled)]
    private static partial Regex QueuePathRegex();

    internal static StorageQueueOperationInfo Classify(HttpRequestMessage request)
    {
        var path = request.RequestUri?.AbsolutePath ?? "";
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

    internal static string GetDiagramLabel(StorageQueueOperationInfo op, StorageQueueTrackingVerbosity verbosity)
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

    internal static Uri BuildCleanUri(StorageQueueOperationInfo op)
    {
        if (op.QueueName is null)
            return new Uri("storagequeue:///");

        return new Uri($"storagequeue:///{op.QueueName}");
    }
}
