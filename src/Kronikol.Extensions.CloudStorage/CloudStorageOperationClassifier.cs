namespace Kronikol.Extensions.CloudStorage;

/// <summary>
/// Classifies Google Cloud Storage HTTP requests into specific operations based on URL patterns and HTTP methods.
/// </summary>
/// <remarks>
/// The handler is not given the client's base URI, which an emulator replaces, as the SDK's own
/// <c>STORAGE_EMULATOR_HOST</c> support does with a bare host, so the API's own path is found in the URL: every Cloud
/// Storage method's path begins <c>b</c> (the buckets, or one bucket and what it holds), <c>projects</c> or
/// <c>channels</c>. The path is read to its end, so a bucket's or an object's sub-resource is never taken for the bucket
/// or the object (#125, plans/EMULATOR_URL_SHAPES_PLAN.md section 5.3).
/// </remarks>
public static class CloudStorageOperationClassifier
{
    // What follows a bucket in the API's own paths (the generated client's RestPaths): its objects, then its
    // sub-resources. The `b` the API path starts at is followed by a bucket and one of these, or by a bucket alone.
    private static readonly HashSet<string> BucketChildren = new(StringComparer.Ordinal)
    {
        "o", "acl", "defaultObjectAcl", "iam", "lockRetentionPolicy", "notificationConfigs", "folders", "managedFolders",
        "anywhereCaches", "storageLayout", "operations", "restore", "relocate"
    };

    public static CloudStorageOperationInfo Classify(HttpRequestMessage request)
    {
        var segments = (request.RequestUri?.AbsolutePath ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries);
        var method = request.Method.Method;

        var start = ApiPathStart(segments);
        if (start < 0 || segments[start] != "b")
            return new(CloudStorageOperation.Other, null);

        // An upload is sent from the host's root, whatever the base: /upload/storage/v1/b/{bucket}/o.
        var isUpload = segments[0].Equals("upload", StringComparison.Ordinal);
        var api = segments[start..];

        // b → the buckets
        if (api.Length == 1)
            return method switch
            {
                "GET" => new(CloudStorageOperation.ListBuckets, null),
                "POST" => new(CloudStorageOperation.CreateBucket, null),
                _ => new(CloudStorageOperation.Other, null)
            };

        var bucket = api[1];

        // b/{bucket} → the bucket itself, whatever its name
        if (api.Length == 2)
            return method switch
            {
                "GET" => new(CloudStorageOperation.GetBucket, bucket),
                "DELETE" => new(CloudStorageOperation.DeleteBucket, bucket),
                _ => new(CloudStorageOperation.Other, bucket)
            };

        // b/{bucket}/{sub-resource} → no member names one
        if (api[2] != "o")
            return new(CloudStorageOperation.Other, bucket);

        // b/{bucket}/o → list, or an upload's start and its data
        if (api.Length == 3)
            return (method, isUpload) switch
            {
                ("POST", true) or ("PUT", true) => new(CloudStorageOperation.Upload, bucket),
                ("GET", false) => new(CloudStorageOperation.ListObjects, bucket),
                _ => new(CloudStorageOperation.Other, bucket)
            };

        var obj = Uri.UnescapeDataString(api[3]);

        // b/{bucket}/o/{object} → the object itself; a PUT replaces its metadata (objects.update), as PATCH does
        if (api.Length == 4)
            return method switch
            {
                "GET" when HasAltMediaParam(request) => new(CloudStorageOperation.Download, bucket, obj),
                "GET" => new(CloudStorageOperation.GetMetadata, bucket, obj),
                "DELETE" => new(CloudStorageOperation.Delete, bucket, obj),
                "PATCH" or "PUT" => new(CloudStorageOperation.UpdateMetadata, bucket, obj),
                _ => new(CloudStorageOperation.Other, bucket, obj)
            };

        // b/{bucket}/o/{object}/... → a copy (CopyObject sends rewriteTo), a compose, or a sub-resource no member names
        return api[4] switch
        {
            "copyTo" or "rewriteTo" => new(CloudStorageOperation.Copy, bucket, obj),
            "compose" => new(CloudStorageOperation.Compose, bucket, obj),
            _ => new(CloudStorageOperation.Other, bucket, obj)
        };
    }

    /// <summary>
    /// The API's own path in <paramref name="uri"/>, from its <c>b</c>, <c>projects</c> or <c>channels</c> segment, without
    /// the client's base or the query: what a call no member names is drawn with, so it reads the same under any base. A
    /// path with none is drawn as sent.
    /// </summary>
    internal static string ApiPathOf(Uri uri)
    {
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var start = ApiPathStart(segments);
        return start < 0 ? uri.AbsolutePath : "/" + string.Join("/", segments[start..]);
    }

    // The index of the segment the API path starts at, or -1: the leftmost `b` that ends the path or is followed by a
    // bucket and then nothing or one of its children; else `projects/{p}/hmacKeys` or `serviceAccount`; else
    // `channels/stop`.
    private static int ApiPathStart(string[] segments)
    {
        for (var i = 0; i < segments.Length; i++)
        {
            var rest = segments.Length - i - 1;
            if (segments[i] == "b" && (rest == 0 || rest == 1 || BucketChildren.Contains(segments[i + 2])))
                return i;
            if (segments[i] == "projects" && rest >= 2 && segments[i + 2] is "hmacKeys" or "serviceAccount")
                return i;
            if (segments[i] == "channels" && rest == 1 && segments[i + 1] == "stop")
                return i;
        }
        return -1;
    }

    private static bool HasAltMediaParam(HttpRequestMessage request) =>
        request.RequestUri?.Query.Contains("alt=media") == true;

    public static string GetDiagramLabel(CloudStorageOperationInfo op, CloudStorageTrackingVerbosity verbosity)
    {
        return verbosity switch
        {
            CloudStorageTrackingVerbosity.Detailed => op.ObjectName is not null
                ? $"{op.Operation} → {op.BucketName}/{op.ObjectName}"
                : op.BucketName is not null
                    ? $"{op.Operation} → {op.BucketName}"
                    : op.Operation.ToString(),
            CloudStorageTrackingVerbosity.Summarised => op.Operation.ToString(),
            _ => op.Operation.ToString() // Raw uses HTTP method
        };
    }
}
