using Kronikol.Extensions.StorageQueues;

namespace Kronikol.Tests.StorageQueues;

/// <summary>
/// The classifier reads the queue and the message after the account, wherever the URL puts it (#125,
/// plans/EMULATOR_URL_SHAPES_PLAN.md section 5.2): in the host on Azure, first in the path on Azurite. It reads them as
/// the Azure SDK's own <c>QueueUriBuilder</c> does, with one fallback for Azurite's built-in account on a host and port
/// the SDK's rule does not take for path-style (F11).
/// </summary>
public class QueueAccountPathRuleTests
{
    private static StorageQueueOperationInfo Classify(string method, string url) =>
        StorageQueueOperationClassifier.Classify(new HttpRequestMessage(new HttpMethod(method), url));

    [Theory]
    [InlineData("https://shop.queue.core.windows.net")]
    [InlineData("http://127.0.0.1:10001/devstoreaccount1")]
    [InlineData("http://127.0.0.1:54322/devstoreaccount1")]
    [InlineData("http://azurite:10001/devstoreaccount1")]
    [InlineData("http://localhost:54322/devstoreaccount1")]
    [InlineData("http://docker-host.internal:54322/devstoreaccount1")]
    [InlineData("http://[::1]:54322/devstoreaccount1")]
    public void A_queue_and_its_messages_are_read_after_the_account_on_any_host(string root)
    {
        Assert.Equal(new StorageQueueOperationInfo(StorageQueueOperation.CreateQueue, "orders"), Classify("PUT", root + "/orders"));
        Assert.Equal(new StorageQueueOperationInfo(StorageQueueOperation.SendMessage, "orders"), Classify("POST", root + "/orders/messages"));
        Assert.Equal(new StorageQueueOperationInfo(StorageQueueOperation.PeekMessages, "orders"), Classify("GET", root + "/orders/messages?peekonly=true"));
        Assert.Equal(new StorageQueueOperationInfo(StorageQueueOperation.DeleteMessage, "orders", "m1"), Classify("DELETE", root + "/orders/messages/m1?popreceipt=p1"));
        Assert.Equal(new StorageQueueOperationInfo(StorageQueueOperation.DeleteQueue, "orders"), Classify("DELETE", root + "/orders"));
    }

    [Theory]
    [InlineData("https://shop.queue.core.windows.net/?comp=list")]
    [InlineData("http://127.0.0.1:10001/devstoreaccount1?comp=list")]
    [InlineData("http://localhost:54322/devstoreaccount1?comp=list")]
    public void Listing_the_queues_is_ListQueues_on_any_host(string url)
    {
        Assert.Equal(new StorageQueueOperationInfo(StorageQueueOperation.ListQueues, null), Classify("GET", url));
    }

    [Fact]
    public void A_queue_named_messages_is_a_queue()
    {
        Assert.Equal(new StorageQueueOperationInfo(StorageQueueOperation.SendMessage, "messages"),
            Classify("POST", "http://localhost:54322/devstoreaccount1/messages/messages"));
    }

    [Fact]
    public void An_account_of_its_own_on_a_host_name_and_another_port_stays_unread()
    {
        // What stays unread (5.2): a suite's own Azurite account served on a host name with a port the SDK does not take
        // for path-style. The SDK reads its name as the queue; the classifier keeps the path as sent, which names no
        // queue, as before #125. 127.0.0.1 or the emulator's ports read it.
        Assert.Equal(new StorageQueueOperationInfo(StorageQueueOperation.Other, null),
            Classify("POST", "http://localhost:54322/myaccount/orders/messages"));
        Assert.Equal(new StorageQueueOperationInfo(StorageQueueOperation.SendMessage, "orders"),
            Classify("POST", "http://127.0.0.1:54322/myaccount/orders/messages"));
    }

    [Fact]
    public void A_queue_named_as_Azurites_account_on_Azure_is_a_queue()
    {
        // The account is left out of the path only where the host names none: on Azure the host holds it.
        Assert.Equal(new StorageQueueOperationInfo(StorageQueueOperation.SendMessage, "devstoreaccount1"),
            Classify("POST", "https://shop.queue.core.windows.net/devstoreaccount1/messages"));
    }
}
