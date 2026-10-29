using Kronikol.Extensions.CloudStorage;

namespace Kronikol.Tests.CloudStorage;

/// <summary>
/// The classifier finds where the API's own path starts, whatever base the client was built with, and reads it to its
/// end (#125, plans/EMULATOR_URL_SHAPES_PLAN.md section 5.3): a bucket's or an object's sub-resource is never taken for
/// the bucket or the object (F12, F13). The URLs are the ones the generated client's <c>RestPath</c>s make.
/// </summary>
public class CloudStorageApiPathRuleTests
{
    private static CloudStorageOperationInfo Classify(string method, string url) =>
        CloudStorageOperationClassifier.Classify(new HttpRequestMessage(new HttpMethod(method), url));

    [Theory]
    [InlineData("https://storage.googleapis.com/storage/v1/b/orders")]
    [InlineData("http://localhost:4443/storage/v1/b/orders")]
    [InlineData("http://localhost:4443/b/orders")]
    [InlineData("http://gateway.local/gcs/b/orders")]
    public void A_bucket_is_read_under_any_base_whatever_its_name(string url)
    {
        Assert.Equal(new CloudStorageOperationInfo(CloudStorageOperation.GetBucket, "orders"), Classify("GET", url));
        Assert.Equal(new CloudStorageOperationInfo(CloudStorageOperation.DeleteBucket, "orders"), Classify("DELETE", url));
    }

    [Fact]
    public void An_object_path_holding_b_and_o_segments_is_the_object_it_names()
    {
        Assert.Equal(new CloudStorageOperationInfo(CloudStorageOperation.GetMetadata, "b", "o"), Classify("GET", "http://localhost:4443/b/b/o/o"));
    }

    [Theory]
    [InlineData("GET", "b/orders/acl/user-a")]
    [InlineData("DELETE", "b/orders/acl/user-a")]
    [InlineData("GET", "b/orders/iam")]
    [InlineData("GET", "b/orders/operations")]
    [InlineData("GET", "b/orders/notificationConfigs")]
    [InlineData("POST", "b/orders/managedFolders")]
    public void A_buckets_sub_resource_is_not_the_bucket(string method, string path)
    {
        Assert.Equal(new CloudStorageOperationInfo(CloudStorageOperation.Other, "orders"), Classify(method, $"https://storage.googleapis.com/storage/v1/{path}"));
    }

    [Theory]
    [InlineData("DELETE", "acl/user-a")]
    [InlineData("PUT", "acl/user-a")]
    [InlineData("POST", "iam")]
    public void An_objects_sub_resource_is_not_the_object(string method, string path)
    {
        Assert.Equal(new CloudStorageOperationInfo(CloudStorageOperation.Other, "orders", "a.json"),
            Classify(method, $"https://storage.googleapis.com/storage/v1/b/orders/o/a.json/{path}"));
    }

    [Fact]
    public void CopyObject_sends_rewriteTo_which_is_a_copy()
    {
        var result = Classify("POST", "http://localhost:4443/b/orders/o/a.json/rewriteTo/b/archive/o/a.json");

        Assert.Equal(new CloudStorageOperationInfo(CloudStorageOperation.Copy, "orders", "a.json"), result);
    }

    [Fact]
    public void An_uploads_data_is_part_of_the_upload()
    {
        var result = Classify("PUT", "http://localhost:4443/upload/storage/v1/b/orders/o?uploadType=resumable&upload_id=u1");

        Assert.Equal(new CloudStorageOperationInfo(CloudStorageOperation.Upload, "orders"), result);
    }

    [Fact]
    public void An_object_name_holding_a_slash_is_one_name()
    {
        var result = Classify("GET", "https://storage.googleapis.com/storage/v1/b/orders/o/2026%2F10%2Forder-1.json?alt=media");

        Assert.Equal(new CloudStorageOperationInfo(CloudStorageOperation.Download, "orders", "2026/10/order-1.json"), result);
    }

    [Fact]
    public void A_path_no_member_names_is_drawn_with_the_API_path()
    {
        Assert.Equal("/b/orders/acl/user-a", CloudStorageOperationClassifier.ApiPathOf(new Uri("http://localhost:4443/storage/v1/b/orders/acl/user-a?userProject=p")));
        Assert.Equal("/projects/p/hmacKeys", CloudStorageOperationClassifier.ApiPathOf(new Uri("https://storage.googleapis.com/storage/v1/projects/p/hmacKeys")));
        Assert.Equal("/discovery/v1/apis", CloudStorageOperationClassifier.ApiPathOf(new Uri("https://storage.googleapis.com/discovery/v1/apis")));
    }
}
