#:package Azure.Storage.Blobs@12.30.1
#:package Azure.Storage.Queues@12.28.1
#:property TargetFramework=net10.0
#:property PublishAot=false

// Does the Azure SDK's own URI parser split every URL shape its clients send into account, container or queue,
// and blob or message? If so, a classifier built on it agrees with the client by construction.
// Usage: dotnet run --file azure-uri-builders.cs

using Azure.Storage.Blobs;
using Azure.Storage.Queues;

string[] blobs =
[
    "https://acct.blob.core.windows.net/orders?restype=container",
    "https://acct.blob.core.windows.net/orders/2026/order-1.json",
    "http://127.0.0.1:10000/devstoreaccount1/orders?restype=container",        // Azurite, UseDevelopmentStorage=true
    "http://127.0.0.1:10000/devstoreaccount1/orders/2026/order-1.json",
    "http://azurite:10000/devstoreaccount1/orders/order-1.json",               // docker compose service name
    "http://localhost:10000/devstoreaccount1/orders/order-1.json",
    "http://127.0.0.1:54321/devstoreaccount1/orders/order-1.json",             // Testcontainers' random host port
    "http://localhost:54321/devstoreaccount1/orders/order-1.json",
    "http://127.0.0.1:10000/devstoreaccount1?comp=list",                       // list containers
    "https://acct.blob.core.windows.net/?comp=list",
];
foreach (var url in blobs)
{
    var b = new BlobUriBuilder(new Uri(url));
    Console.WriteLine($"blob  {url,-70} account={b.AccountName,-17} container={b.BlobContainerName,-8} blob={b.BlobName}");
}

string[] queues =
[
    "https://acct.queue.core.windows.net/orders/messages",
    "https://acct.queue.core.windows.net/orders/messages/m1?popreceipt=p",
    "http://127.0.0.1:10001/devstoreaccount1/orders",
    "http://127.0.0.1:10001/devstoreaccount1/orders/messages/m1?popreceipt=p",
    "http://azurite:10001/devstoreaccount1/orders/messages",
    "http://localhost:54322/devstoreaccount1/orders/messages",
];
foreach (var url in queues)
{
    var q = new QueueUriBuilder(new Uri(url));
    Console.WriteLine($"queue {url,-70} account={q.AccountName,-17} queue={q.QueueName,-8} messages={q.Messages} id={q.MessageId}");
}
