using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Google.Apis.Requests;
using Google.Apis.Storage.v1;
using Kronikol.Extensions.CloudStorage;

namespace Kronikol.Tests.CloudStorage;

/// <summary>
/// Every method the generated Cloud Storage client declares, read by reflection from each request class's
/// <c>RestPath</c> and <c>HttpMethod</c>, filled with sample values and classified under Google's base, an emulator's
/// <c>/storage/v1/</c> path and an emulator's root (#125, plans/EMULATOR_URL_SHAPES_PLAN.md section 6.2). The named
/// methods are named as <see cref="Named"/> says under every base, and every other method is <c>Other</c> on purpose
/// (<see cref="OtherOnPurpose"/>): a sub-resource is never drawn as its bucket or object. A method neither names, which a
/// new SDK version may add, fails the fact until someone decides what it is; a method the floor's client lacks is
/// simply not met.
/// </summary>
public class CloudStorageApiSurfaceTests
{
    private static readonly string[] Bases = ["https://storage.googleapis.com/storage/v1/", "http://127.0.0.1:4443/storage/v1/", "http://127.0.0.1:4443/"];

    private static readonly Dictionary<string, CloudStorageOperation> Named = new()
    {
        ["BucketsResource.DeleteRequest"] = CloudStorageOperation.DeleteBucket,
        ["BucketsResource.GetRequest"] = CloudStorageOperation.GetBucket,
        ["BucketsResource.InsertRequest"] = CloudStorageOperation.CreateBucket,
        ["BucketsResource.ListRequest"] = CloudStorageOperation.ListBuckets,
        ["ObjectsResource.ComposeRequest"] = CloudStorageOperation.Compose,
        ["ObjectsResource.CopyRequest"] = CloudStorageOperation.Copy,
        ["ObjectsResource.DeleteRequest"] = CloudStorageOperation.Delete,
        ["ObjectsResource.GetRequest"] = CloudStorageOperation.GetMetadata,
        ["ObjectsResource.ListRequest"] = CloudStorageOperation.ListObjects,
        ["ObjectsResource.PatchRequest"] = CloudStorageOperation.UpdateMetadata,
        ["ObjectsResource.RewriteRequest"] = CloudStorageOperation.Copy,
        ["ObjectsResource.UpdateRequest"] = CloudStorageOperation.UpdateMetadata,
    };

    // The long tail (plans/EMULATOR_URL_SHAPES_PLAN.md section 5.3): naming any of these needs a new
    // CloudStorageOperation member, a minor, for calls no measured suite makes.
    private static readonly HashSet<string> OtherOnPurpose =
    [
        "AnywhereCachesResource.DisableRequest",
        "AnywhereCachesResource.GetRequest",
        "AnywhereCachesResource.InsertRequest",
        "AnywhereCachesResource.ListRequest",
        "AnywhereCachesResource.PauseRequest",
        "AnywhereCachesResource.ResumeRequest",
        "AnywhereCachesResource.UpdateRequest",
        "BucketAccessControlsResource.DeleteRequest",
        "BucketAccessControlsResource.GetRequest",
        "BucketAccessControlsResource.InsertRequest",
        "BucketAccessControlsResource.ListRequest",
        "BucketAccessControlsResource.PatchRequest",
        "BucketAccessControlsResource.UpdateRequest",
        "BucketsResource.GetIamPolicyRequest",
        "BucketsResource.GetStorageLayoutRequest",
        "BucketsResource.LockRetentionPolicyRequest",
        "BucketsResource.PatchRequest",
        "BucketsResource.RelocateRequest",
        "BucketsResource.RestoreRequest",
        "BucketsResource.SetIamPolicyRequest",
        "BucketsResource.TestIamPermissionsRequest",
        "BucketsResource.UpdateRequest",
        "ChannelsResource.StopRequest",
        "DefaultObjectAccessControlsResource.DeleteRequest",
        "DefaultObjectAccessControlsResource.GetRequest",
        "DefaultObjectAccessControlsResource.InsertRequest",
        "DefaultObjectAccessControlsResource.ListRequest",
        "DefaultObjectAccessControlsResource.PatchRequest",
        "DefaultObjectAccessControlsResource.UpdateRequest",
        "FoldersResource.DeleteRecursiveRequest",
        "FoldersResource.DeleteRequest",
        "FoldersResource.GetRequest",
        "FoldersResource.InsertRequest",
        "FoldersResource.ListRequest",
        "FoldersResource.RenameRequest",
        "ManagedFoldersResource.DeleteRequest",
        "ManagedFoldersResource.GetIamPolicyRequest",
        "ManagedFoldersResource.GetRequest",
        "ManagedFoldersResource.InsertRequest",
        "ManagedFoldersResource.ListRequest",
        "ManagedFoldersResource.SetIamPolicyRequest",
        "ManagedFoldersResource.TestIamPermissionsRequest",
        "NotificationsResource.DeleteRequest",
        "NotificationsResource.GetRequest",
        "NotificationsResource.InsertRequest",
        "NotificationsResource.ListRequest",
        "ObjectAccessControlsResource.DeleteRequest",
        "ObjectAccessControlsResource.GetRequest",
        "ObjectAccessControlsResource.InsertRequest",
        "ObjectAccessControlsResource.ListRequest",
        "ObjectAccessControlsResource.PatchRequest",
        "ObjectAccessControlsResource.UpdateRequest",
        "ObjectsResource.BulkRestoreRequest",
        "ObjectsResource.GetIamPolicyRequest",
        "ObjectsResource.InsertRequest",
        "ObjectsResource.MoveRequest",
        "ObjectsResource.RestoreRequest",
        "ObjectsResource.SetIamPolicyRequest",
        "ObjectsResource.TestIamPermissionsRequest",
        "ObjectsResource.WatchAllRequest",
        "OperationsResource.AdvanceRelocateBucketRequest",
        "OperationsResource.CancelRequest",
        "OperationsResource.GetRequest",
        "OperationsResource.ListRequest",
        "ProjectsResource.HmacKeysResource.CreateRequest",
        "ProjectsResource.HmacKeysResource.DeleteRequest",
        "ProjectsResource.HmacKeysResource.GetRequest",
        "ProjectsResource.HmacKeysResource.ListRequest",
        "ProjectsResource.HmacKeysResource.UpdateRequest",
        "ProjectsResource.ServiceAccountResource.GetRequest",
    ];

    public static TheoryData<string, string, string> Methods()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var type in typeof(StorageService).Assembly.GetTypes()
                     .Where(t => !t.IsAbstract && !t.ContainsGenericParameters && typeof(IClientServiceRequest).IsAssignableFrom(t)))
        {
            var request = (IClientServiceRequest)RuntimeHelpers.GetUninitializedObject(type);
            data.Add(type.FullName!.Replace("Google.Apis.Storage.v1.", "").Replace("+", "."), request.HttpMethod, request.RestPath);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Methods))]
    public void Every_method_is_named_or_Other_as_the_map_says_under_every_base(string name, string method, string restPath)
    {
        Assert.True(Named.ContainsKey(name) || OtherOnPurpose.Contains(name),
            $"{name} ({method} {restPath}) is a method the map does not name: decide what it is and add it to Named or OtherOnPurpose.");
        var expected = Named.TryGetValue(name, out var operation) ? operation : CloudStorageOperation.Other;

        foreach (var root in Bases)
            Assert.Equal(expected, CloudStorageOperationClassifier.Classify(new HttpRequestMessage(new HttpMethod(method), root + Fill(restPath))).Operation);
    }

    private static string Fill(string template) => Regex.Replace(template, @"\{\+?(?<n>[^}]+)\}", m => m.Groups["n"].Value switch
    {
        "bucket" or "sourceBucket" => "orders",
        "destinationBucket" => "archive",
        "object" or "sourceObject" or "destinationObject" => "order-1.json",
        "entity" => "allUsers",
        var other => other.ToLowerInvariant(),
    });
}
