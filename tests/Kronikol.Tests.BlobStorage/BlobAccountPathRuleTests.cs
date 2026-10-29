using Kronikol.Extensions.BlobStorage;

namespace Kronikol.Tests.BlobStorage;

/// <summary>
/// The classifier reads the container and the blob after the account, wherever the URL puts it (#125,
/// plans/EMULATOR_URL_SHAPES_PLAN.md section 5.2): in the host on Azure, first in the path on Azurite. It reads them as
/// the Azure SDK's own <c>BlobUriBuilder</c> does, with one fallback for Azurite's built-in account on a host and port
/// the SDK's rule does not take for path-style (F11).
/// </summary>
public class BlobAccountPathRuleTests
{
    private static BlobOperationInfo Classify(string method, string url) =>
        BlobOperationClassifier.Classify(new HttpRequestMessage(new HttpMethod(method), url));

    [Theory]
    [InlineData("https://shop.blob.core.windows.net/orders/order-1.json")]
    [InlineData("http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json")]
    [InlineData("http://127.0.0.1:54321/devstoreaccount1/orders/order-1.json")]
    [InlineData("http://azurite:10000/devstoreaccount1/orders/order-1.json")]
    [InlineData("http://localhost:54321/devstoreaccount1/orders/order-1.json")]
    [InlineData("http://docker-host.internal:54321/devstoreaccount1/orders/order-1.json")]
    [InlineData("http://[::1]:54321/devstoreaccount1/orders/order-1.json")]
    public void A_blob_is_read_after_the_account_on_any_host(string url)
    {
        Assert.Equal(new BlobOperationInfo(BlobOperation.Download, "orders", "order-1.json"), Classify("GET", url));
    }

    [Theory]
    [InlineData("https://shop.blob.core.windows.net/orders?restype=container")]
    [InlineData("http://127.0.0.1:10000/devstoreaccount1/orders?restype=container")]
    [InlineData("http://localhost:54321/devstoreaccount1/orders?restype=container")]
    public void A_container_is_created_listed_and_deleted_as_a_container(string url)
    {
        Assert.Equal(new BlobOperationInfo(BlobOperation.CreateContainer, "orders", null), Classify("PUT", url));
        Assert.Equal(new BlobOperationInfo(BlobOperation.ListBlobs, "orders", null), Classify("GET", url + "&comp=list"));
        Assert.Equal(new BlobOperationInfo(BlobOperation.DeleteContainer, "orders", null), Classify("DELETE", url));
    }

    [Fact]
    public void A_blob_name_holding_slashes_is_one_name()
    {
        Assert.Equal(new BlobOperationInfo(BlobOperation.Download, "orders", "2026/10/order-1.json"),
            Classify("GET", "http://127.0.0.1:10000/devstoreaccount1/orders/2026/10/order-1.json"));
    }

    [Theory]
    [InlineData("https://shop.blob.core.windows.net/?comp=list")]
    [InlineData("http://127.0.0.1:10000/devstoreaccount1?comp=list")]
    [InlineData("http://localhost:54321/devstoreaccount1?comp=list")]
    public void A_call_on_the_account_names_no_container_and_its_path_leaves_Azurites_account_out(string url)
    {
        Assert.Equal(new BlobOperationInfo(BlobOperation.Other, null, null), Classify("GET", url));
        Assert.Equal("/", BlobOperationClassifier.ApiPathOf(new Uri(url)));
    }

    [Theory]
    [InlineData("https://shop.blob.core.windows.net/shop/order-1.json", "shop")]
    [InlineData("https://shop.blob.core.windows.net/devstoreaccount1/order-1.json", "devstoreaccount1")]
    public void A_container_named_as_an_account_on_Azure_is_a_container(string url, string container)
    {
        // The account is left out of the path only where the parser reads it there, or the host names none: on Azure
        // the host holds it.
        Assert.Equal(new BlobOperationInfo(BlobOperation.Download, container, "order-1.json"), Classify("GET", url));
        Assert.Equal(new Uri(url).AbsolutePath, BlobOperationClassifier.ApiPathOf(new Uri(url)));
    }

    [Fact]
    public void A_blob_name_is_kept_as_the_URL_escapes_it()
    {
        // As every release before #125 read it, so a call on Azure records the same URI it did.
        Assert.Equal(new BlobOperationInfo(BlobOperation.Download, "orders", "2026%2F10%2Forder-1.json"),
            Classify("GET", "http://127.0.0.1:10000/devstoreaccount1/orders/2026%2F10%2Forder-1.json"));
    }

    [Fact]
    public void An_account_of_its_own_on_a_host_name_and_another_port_is_read_as_the_SDK_reads_it()
    {
        // What stays unread (5.2): a suite's own Azurite account served on a host name with a port the SDK does not take
        // for path-style. The SDK reads its name as the container, and so does the classifier; 127.0.0.1 or the
        // emulator's ports read it.
        Assert.Equal(new BlobOperationInfo(BlobOperation.Download, "myaccount", "orders/order-1.json"),
            Classify("GET", "http://localhost:54321/myaccount/orders/order-1.json"));
        Assert.Equal(new BlobOperationInfo(BlobOperation.Download, "orders", "order-1.json"),
            Classify("GET", "http://127.0.0.1:54321/myaccount/orders/order-1.json"));
    }
}
