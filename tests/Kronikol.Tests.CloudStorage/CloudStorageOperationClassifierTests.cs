using Kronikol.Extensions.CloudStorage;

namespace Kronikol.Tests.CloudStorage;

public class CloudStorageOperationClassifierTests
{
    // ─── Upload operations ───────────────────────────────────

    [Fact]
    public void Classify_PostToUploadPath_ReturnsUpload()
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            "https://storage.googleapis.com/upload/storage/v1/b/my-bucket/o?uploadType=multipart");

        var result = CloudStorageOperationClassifier.Classify(request);

        Assert.Equal(CloudStorageOperation.Upload, result.Operation);
        Assert.Equal("my-bucket", result.BucketName);
    }

    [Fact]
    public void Classify_PutWithObject_ReturnsUpdateMetadata()
    {
        // PUT b/{bucket}/o/{object} is objects.update, which replaces the object's metadata; an upload's data goes to
        // /upload/storage/v1/b/{bucket}/o, which names no object (#125, F13).
        var request = new HttpRequestMessage(HttpMethod.Put,
            "https://storage.googleapis.com/storage/v1/b/my-bucket/o/file.txt");

        var result = CloudStorageOperationClassifier.Classify(request);

        Assert.Equal(CloudStorageOperation.UpdateMetadata, result.Operation);
        Assert.Equal("file.txt", result.ObjectName);
    }

    // ─── Download operations ─────────────────────────────────

    [Fact]
    public void Classify_GetWithAltMedia_ReturnsDownload()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            "https://storage.googleapis.com/storage/v1/b/my-bucket/o/file.txt?alt=media");

        var result = CloudStorageOperationClassifier.Classify(request);

        Assert.Equal(CloudStorageOperation.Download, result.Operation);
        Assert.Equal("my-bucket", result.BucketName);
        Assert.Equal("file.txt", result.ObjectName);
    }

    [Fact]
    public void Classify_GetWithoutAltMedia_ReturnsGetMetadata()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            "https://storage.googleapis.com/storage/v1/b/my-bucket/o/file.txt");

        var result = CloudStorageOperationClassifier.Classify(request);

        Assert.Equal(CloudStorageOperation.GetMetadata, result.Operation);
    }

    // ─── Delete operations ───────────────────────────────────

    [Fact]
    public void Classify_DeleteObject_ReturnsDelete()
    {
        var request = new HttpRequestMessage(HttpMethod.Delete,
            "https://storage.googleapis.com/storage/v1/b/my-bucket/o/file.txt");

        var result = CloudStorageOperationClassifier.Classify(request);

        Assert.Equal(CloudStorageOperation.Delete, result.Operation);
        Assert.Equal("my-bucket", result.BucketName);
        Assert.Equal("file.txt", result.ObjectName);
    }

    // ─── List operations ─────────────────────────────────────

    [Fact]
    public void Classify_GetObjectsList_ReturnsListObjects()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            "https://storage.googleapis.com/storage/v1/b/my-bucket/o");

        var result = CloudStorageOperationClassifier.Classify(request);

        Assert.Equal(CloudStorageOperation.ListObjects, result.Operation);
        Assert.Equal("my-bucket", result.BucketName);
        Assert.Null(result.ObjectName);
    }

    // ─── Metadata operations ─────────────────────────────────

    [Fact]
    public void Classify_PatchObject_ReturnsUpdateMetadata()
    {
        var request = new HttpRequestMessage(HttpMethod.Patch,
            "https://storage.googleapis.com/storage/v1/b/my-bucket/o/file.txt");

        var result = CloudStorageOperationClassifier.Classify(request);

        Assert.Equal(CloudStorageOperation.UpdateMetadata, result.Operation);
    }

    // ─── Copy and Compose ────────────────────────────────────

    [Fact]
    public void Classify_CopyPath_ReturnsCopy()
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            "https://storage.googleapis.com/storage/v1/b/src-bucket/o/file.txt/copyTo/b/dest-bucket/o/copy.txt");

        var result = CloudStorageOperationClassifier.Classify(request);

        Assert.Equal(CloudStorageOperation.Copy, result.Operation);
        Assert.Equal("src-bucket", result.BucketName);
        Assert.Equal("file.txt", result.ObjectName);
    }

    [Fact]
    public void Classify_ComposePath_ReturnsCompose()
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            "https://storage.googleapis.com/storage/v1/b/my-bucket/o/combined.txt/compose");

        var result = CloudStorageOperationClassifier.Classify(request);

        Assert.Equal(CloudStorageOperation.Compose, result.Operation);
        Assert.Equal("my-bucket", result.BucketName);
    }

    // ─── Bucket operations ───────────────────────────────────

    [Fact]
    public void Classify_GetBucket_ReturnsGetBucket()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            "https://storage.googleapis.com/storage/v1/b/my-bucket");

        var result = CloudStorageOperationClassifier.Classify(request);

        Assert.Equal(CloudStorageOperation.GetBucket, result.Operation);
        Assert.Equal("my-bucket", result.BucketName);
    }

    [Fact]
    public void Classify_DeleteBucket_ReturnsDeleteBucket()
    {
        var request = new HttpRequestMessage(HttpMethod.Delete,
            "https://storage.googleapis.com/storage/v1/b/my-bucket");

        var result = CloudStorageOperationClassifier.Classify(request);

        Assert.Equal(CloudStorageOperation.DeleteBucket, result.Operation);
    }

    [Fact]
    public void Classify_PostBucket_ReturnsCreateBucket()
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            "https://storage.googleapis.com/storage/v1/b");

        var result = CloudStorageOperationClassifier.Classify(request);

        Assert.Equal(CloudStorageOperation.CreateBucket, result.Operation);
    }

    [Fact]
    public void Classify_GetBucketsList_ReturnsListBuckets()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            "https://storage.googleapis.com/storage/v1/b");

        var result = CloudStorageOperationClassifier.Classify(request);

        Assert.Equal(CloudStorageOperation.ListBuckets, result.Operation);
    }

    // ─── Edge cases ──────────────────────────────────────────

    [Fact]
    public void Classify_UnrecognisedPath_ReturnsOther()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            "https://storage.googleapis.com/discovery/v1/apis");

        var result = CloudStorageOperationClassifier.Classify(request);

        Assert.Equal(CloudStorageOperation.Other, result.Operation);
    }

    [Fact]
    public void Classify_UrlEncodedObjectName_DecodesCorrectly()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            "https://storage.googleapis.com/storage/v1/b/my-bucket/o/path%2Fto%2Ffile.txt?alt=media");

        var result = CloudStorageOperationClassifier.Classify(request);

        Assert.Equal(CloudStorageOperation.Download, result.Operation);
        Assert.Equal("path/to/file.txt", result.ObjectName);
    }

    // ─── Diagram labels ─────────────────────────────────────

    [Fact]
    public void GetDiagramLabel_Detailed_WithObject_IncludesBucketAndObject()
    {
        var op = new CloudStorageOperationInfo(CloudStorageOperation.Upload, "my-bucket", "file.txt");

        var label = CloudStorageOperationClassifier.GetDiagramLabel(op, CloudStorageTrackingVerbosity.Detailed);

        Assert.Equal("Upload → my-bucket/file.txt", label);
    }

    [Fact]
    public void GetDiagramLabel_Detailed_WithoutObject_IncludesBucket()
    {
        var op = new CloudStorageOperationInfo(CloudStorageOperation.ListObjects, "my-bucket");

        var label = CloudStorageOperationClassifier.GetDiagramLabel(op, CloudStorageTrackingVerbosity.Detailed);

        Assert.Equal("ListObjects → my-bucket", label);
    }

    [Fact]
    public void GetDiagramLabel_Summarised_ReturnsOperationOnly()
    {
        var op = new CloudStorageOperationInfo(CloudStorageOperation.Upload, "my-bucket", "file.txt");

        var label = CloudStorageOperationClassifier.GetDiagramLabel(op, CloudStorageTrackingVerbosity.Summarised);

        Assert.Equal("Upload", label);
    }

    [Fact]
    public void GetDiagramLabel_Raw_ReturnsOperationName()
    {
        var op = new CloudStorageOperationInfo(CloudStorageOperation.Upload, "my-bucket", "file.txt");

        var label = CloudStorageOperationClassifier.GetDiagramLabel(op, CloudStorageTrackingVerbosity.Raw);

        Assert.Equal("Upload", label);
    }
}
