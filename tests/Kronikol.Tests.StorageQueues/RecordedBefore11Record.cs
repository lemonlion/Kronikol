namespace Kronikol.Tests.StorageQueues;

/// <summary>
/// What the last package published before #125's R2 (Kronikol.Extensions.StorageQueues 5.27.8) recorded for each
/// request a <c>QueueServiceClient</c> sends under six connection shapes, and for the other forms its classifier reads,
/// on Azure and on Azurite's built-in account, at <c>Detailed</c> and <c>Summarised</c>: one line per request,
/// "verbosity, method, url, label, uri" separated by tabs, label and uri "-" when it recorded nothing. Made once by
/// plans/EMULATOR_URL_SHAPES_PLAN.harness/probes/before-r2-record-queues.cs against the published package; never
/// edited.
/// </summary>
internal static class RecordedBefore11Record
{
    public const string Lines = """
Detailed	PUT	https://shop.queue.core.windows.net/orders	CreateQueue	storagequeue:///orders
Detailed	POST	https://shop.queue.core.windows.net/orders/messages	Send → orders	storagequeue:///orders
Detailed	GET	https://shop.queue.core.windows.net/orders/messages	Receive ← orders	storagequeue:///orders
Detailed	GET	https://shop.queue.core.windows.net/orders/messages?peekonly=true	Peek ← orders	storagequeue:///orders
Detailed	DELETE	https://shop.queue.core.windows.net/orders/messages/m1?popreceipt=pop1	Delete	storagequeue:///orders
Detailed	DELETE	https://shop.queue.core.windows.net/orders/messages	Clear → orders	storagequeue:///orders
Detailed	DELETE	https://shop.queue.core.windows.net/orders	DeleteQueue	storagequeue:///orders
Detailed	GET	https://shop.queue.core.windows.net/?comp=list	ListQueues	storagequeue:///
Detailed	PUT	http://127.0.0.1:10001/devstoreaccount1/orders	Other	storagequeue:///
Detailed	POST	http://127.0.0.1:10001/devstoreaccount1/orders/messages	Other	storagequeue:///
Detailed	GET	http://127.0.0.1:10001/devstoreaccount1/orders/messages	Other	storagequeue:///
Detailed	GET	http://127.0.0.1:10001/devstoreaccount1/orders/messages?peekonly=true	Other	storagequeue:///
Detailed	DELETE	http://127.0.0.1:10001/devstoreaccount1/orders/messages/m1?popreceipt=pop1	Other	storagequeue:///
Detailed	DELETE	http://127.0.0.1:10001/devstoreaccount1/orders/messages	Other	storagequeue:///
Detailed	DELETE	http://127.0.0.1:10001/devstoreaccount1/orders	Other	storagequeue:///
Detailed	GET	http://127.0.0.1:10001/devstoreaccount1?comp=list	Other	storagequeue:///devstoreaccount1
Detailed	PUT	http://127.0.0.1:54322/devstoreaccount1/orders	Other	storagequeue:///
Detailed	POST	http://127.0.0.1:54322/devstoreaccount1/orders/messages	Other	storagequeue:///
Detailed	GET	http://127.0.0.1:54322/devstoreaccount1/orders/messages	Other	storagequeue:///
Detailed	GET	http://127.0.0.1:54322/devstoreaccount1/orders/messages?peekonly=true	Other	storagequeue:///
Detailed	DELETE	http://127.0.0.1:54322/devstoreaccount1/orders/messages/m1?popreceipt=pop1	Other	storagequeue:///
Detailed	DELETE	http://127.0.0.1:54322/devstoreaccount1/orders/messages	Other	storagequeue:///
Detailed	DELETE	http://127.0.0.1:54322/devstoreaccount1/orders	Other	storagequeue:///
Detailed	GET	http://127.0.0.1:54322/devstoreaccount1?comp=list	Other	storagequeue:///devstoreaccount1
Detailed	PUT	http://azurite:10001/devstoreaccount1/orders	Other	storagequeue:///
Detailed	POST	http://azurite:10001/devstoreaccount1/orders/messages	Other	storagequeue:///
Detailed	GET	http://azurite:10001/devstoreaccount1/orders/messages	Other	storagequeue:///
Detailed	GET	http://azurite:10001/devstoreaccount1/orders/messages?peekonly=true	Other	storagequeue:///
Detailed	DELETE	http://azurite:10001/devstoreaccount1/orders/messages/m1?popreceipt=pop1	Other	storagequeue:///
Detailed	DELETE	http://azurite:10001/devstoreaccount1/orders/messages	Other	storagequeue:///
Detailed	DELETE	http://azurite:10001/devstoreaccount1/orders	Other	storagequeue:///
Detailed	GET	http://azurite:10001/devstoreaccount1?comp=list	Other	storagequeue:///devstoreaccount1
Detailed	PUT	http://localhost:54322/devstoreaccount1/orders	Other	storagequeue:///
Detailed	POST	http://localhost:54322/devstoreaccount1/orders/messages	Other	storagequeue:///
Detailed	GET	http://localhost:54322/devstoreaccount1/orders/messages	Other	storagequeue:///
Detailed	GET	http://localhost:54322/devstoreaccount1/orders/messages?peekonly=true	Other	storagequeue:///
Detailed	DELETE	http://localhost:54322/devstoreaccount1/orders/messages/m1?popreceipt=pop1	Other	storagequeue:///
Detailed	DELETE	http://localhost:54322/devstoreaccount1/orders/messages	Other	storagequeue:///
Detailed	DELETE	http://localhost:54322/devstoreaccount1/orders	Other	storagequeue:///
Detailed	GET	http://localhost:54322/devstoreaccount1?comp=list	Other	storagequeue:///devstoreaccount1
Detailed	PUT	http://docker-host.internal:54322/devstoreaccount1/orders	Other	storagequeue:///
Detailed	POST	http://docker-host.internal:54322/devstoreaccount1/orders/messages	Other	storagequeue:///
Detailed	GET	http://docker-host.internal:54322/devstoreaccount1/orders/messages	Other	storagequeue:///
Detailed	GET	http://docker-host.internal:54322/devstoreaccount1/orders/messages?peekonly=true	Other	storagequeue:///
Detailed	DELETE	http://docker-host.internal:54322/devstoreaccount1/orders/messages/m1?popreceipt=pop1	Other	storagequeue:///
Detailed	DELETE	http://docker-host.internal:54322/devstoreaccount1/orders/messages	Other	storagequeue:///
Detailed	DELETE	http://docker-host.internal:54322/devstoreaccount1/orders	Other	storagequeue:///
Detailed	GET	http://docker-host.internal:54322/devstoreaccount1?comp=list	Other	storagequeue:///devstoreaccount1
Detailed	GET	https://shop.queue.core.windows.net/orders?comp=metadata	GetProperties	storagequeue:///orders
Detailed	PUT	https://shop.queue.core.windows.net/orders?comp=metadata	SetMetadata	storagequeue:///orders
Detailed	PUT	https://shop.queue.core.windows.net/orders/messages/m1?popreceipt=p1&visibilitytimeout=0	Update	storagequeue:///orders
Detailed	GET	https://shop.queue.core.windows.net/orders/messages?numofmessages=5	Receive ← orders	storagequeue:///orders
Detailed	GET	https://shop.queue.core.windows.net/?comp=list	ListQueues	storagequeue:///
Detailed	GET	https://shop.queue.core.windows.net/?restype=service&comp=properties	Other	storagequeue:///
Detailed	GET	https://shop.queue.core.windows.net/orders?comp=acl	Other	storagequeue:///orders
Detailed	GET	http://127.0.0.1:10001/devstoreaccount1/orders?comp=metadata	Other	storagequeue:///
Detailed	PUT	http://127.0.0.1:10001/devstoreaccount1/orders?comp=metadata	Other	storagequeue:///
Detailed	PUT	http://127.0.0.1:10001/devstoreaccount1/orders/messages/m1?popreceipt=p1&visibilitytimeout=0	Other	storagequeue:///
Detailed	GET	http://127.0.0.1:10001/devstoreaccount1/orders/messages?numofmessages=5	Other	storagequeue:///
Detailed	GET	http://127.0.0.1:10001/devstoreaccount1/?comp=list	Other	storagequeue:///
Detailed	GET	http://127.0.0.1:10001/devstoreaccount1/?restype=service&comp=properties	Other	storagequeue:///
Detailed	GET	http://127.0.0.1:10001/devstoreaccount1/orders?comp=acl	Other	storagequeue:///
Detailed	GET	http://localhost:54322/devstoreaccount1/orders?comp=metadata	Other	storagequeue:///
Detailed	PUT	http://localhost:54322/devstoreaccount1/orders?comp=metadata	Other	storagequeue:///
Detailed	PUT	http://localhost:54322/devstoreaccount1/orders/messages/m1?popreceipt=p1&visibilitytimeout=0	Other	storagequeue:///
Detailed	GET	http://localhost:54322/devstoreaccount1/orders/messages?numofmessages=5	Other	storagequeue:///
Detailed	GET	http://localhost:54322/devstoreaccount1/?comp=list	Other	storagequeue:///
Detailed	GET	http://localhost:54322/devstoreaccount1/?restype=service&comp=properties	Other	storagequeue:///
Detailed	GET	http://localhost:54322/devstoreaccount1/orders?comp=acl	Other	storagequeue:///
Summarised	PUT	https://shop.queue.core.windows.net/orders	CreateQueue	storagequeue:///orders
Summarised	POST	https://shop.queue.core.windows.net/orders/messages	SendMessage	storagequeue:///orders
Summarised	GET	https://shop.queue.core.windows.net/orders/messages	ReceiveMessages	storagequeue:///orders
Summarised	GET	https://shop.queue.core.windows.net/orders/messages?peekonly=true	PeekMessages	storagequeue:///orders
Summarised	DELETE	https://shop.queue.core.windows.net/orders/messages/m1?popreceipt=pop1	DeleteMessage	storagequeue:///orders
Summarised	DELETE	https://shop.queue.core.windows.net/orders/messages	ClearMessages	storagequeue:///orders
Summarised	DELETE	https://shop.queue.core.windows.net/orders	DeleteQueue	storagequeue:///orders
Summarised	GET	https://shop.queue.core.windows.net/?comp=list	ListQueues	storagequeue:///
Summarised	PUT	http://127.0.0.1:10001/devstoreaccount1/orders	-	-
Summarised	POST	http://127.0.0.1:10001/devstoreaccount1/orders/messages	-	-
Summarised	GET	http://127.0.0.1:10001/devstoreaccount1/orders/messages	-	-
Summarised	GET	http://127.0.0.1:10001/devstoreaccount1/orders/messages?peekonly=true	-	-
Summarised	DELETE	http://127.0.0.1:10001/devstoreaccount1/orders/messages/m1?popreceipt=pop1	-	-
Summarised	DELETE	http://127.0.0.1:10001/devstoreaccount1/orders/messages	-	-
Summarised	DELETE	http://127.0.0.1:10001/devstoreaccount1/orders	-	-
Summarised	GET	http://127.0.0.1:10001/devstoreaccount1?comp=list	-	-
Summarised	PUT	http://127.0.0.1:54322/devstoreaccount1/orders	-	-
Summarised	POST	http://127.0.0.1:54322/devstoreaccount1/orders/messages	-	-
Summarised	GET	http://127.0.0.1:54322/devstoreaccount1/orders/messages	-	-
Summarised	GET	http://127.0.0.1:54322/devstoreaccount1/orders/messages?peekonly=true	-	-
Summarised	DELETE	http://127.0.0.1:54322/devstoreaccount1/orders/messages/m1?popreceipt=pop1	-	-
Summarised	DELETE	http://127.0.0.1:54322/devstoreaccount1/orders/messages	-	-
Summarised	DELETE	http://127.0.0.1:54322/devstoreaccount1/orders	-	-
Summarised	GET	http://127.0.0.1:54322/devstoreaccount1?comp=list	-	-
Summarised	PUT	http://azurite:10001/devstoreaccount1/orders	-	-
Summarised	POST	http://azurite:10001/devstoreaccount1/orders/messages	-	-
Summarised	GET	http://azurite:10001/devstoreaccount1/orders/messages	-	-
Summarised	GET	http://azurite:10001/devstoreaccount1/orders/messages?peekonly=true	-	-
Summarised	DELETE	http://azurite:10001/devstoreaccount1/orders/messages/m1?popreceipt=pop1	-	-
Summarised	DELETE	http://azurite:10001/devstoreaccount1/orders/messages	-	-
Summarised	DELETE	http://azurite:10001/devstoreaccount1/orders	-	-
Summarised	GET	http://azurite:10001/devstoreaccount1?comp=list	-	-
Summarised	PUT	http://localhost:54322/devstoreaccount1/orders	-	-
Summarised	POST	http://localhost:54322/devstoreaccount1/orders/messages	-	-
Summarised	GET	http://localhost:54322/devstoreaccount1/orders/messages	-	-
Summarised	GET	http://localhost:54322/devstoreaccount1/orders/messages?peekonly=true	-	-
Summarised	DELETE	http://localhost:54322/devstoreaccount1/orders/messages/m1?popreceipt=pop1	-	-
Summarised	DELETE	http://localhost:54322/devstoreaccount1/orders/messages	-	-
Summarised	DELETE	http://localhost:54322/devstoreaccount1/orders	-	-
Summarised	GET	http://localhost:54322/devstoreaccount1?comp=list	-	-
Summarised	PUT	http://docker-host.internal:54322/devstoreaccount1/orders	-	-
Summarised	POST	http://docker-host.internal:54322/devstoreaccount1/orders/messages	-	-
Summarised	GET	http://docker-host.internal:54322/devstoreaccount1/orders/messages	-	-
Summarised	GET	http://docker-host.internal:54322/devstoreaccount1/orders/messages?peekonly=true	-	-
Summarised	DELETE	http://docker-host.internal:54322/devstoreaccount1/orders/messages/m1?popreceipt=pop1	-	-
Summarised	DELETE	http://docker-host.internal:54322/devstoreaccount1/orders/messages	-	-
Summarised	DELETE	http://docker-host.internal:54322/devstoreaccount1/orders	-	-
Summarised	GET	http://docker-host.internal:54322/devstoreaccount1?comp=list	-	-
Summarised	GET	https://shop.queue.core.windows.net/orders?comp=metadata	GetProperties	storagequeue:///orders
Summarised	PUT	https://shop.queue.core.windows.net/orders?comp=metadata	SetMetadata	storagequeue:///orders
Summarised	PUT	https://shop.queue.core.windows.net/orders/messages/m1?popreceipt=p1&visibilitytimeout=0	UpdateMessage	storagequeue:///orders
Summarised	GET	https://shop.queue.core.windows.net/orders/messages?numofmessages=5	ReceiveMessages	storagequeue:///orders
Summarised	GET	https://shop.queue.core.windows.net/?comp=list	ListQueues	storagequeue:///
Summarised	GET	https://shop.queue.core.windows.net/?restype=service&comp=properties	-	-
Summarised	GET	https://shop.queue.core.windows.net/orders?comp=acl	-	-
Summarised	GET	http://127.0.0.1:10001/devstoreaccount1/orders?comp=metadata	-	-
Summarised	PUT	http://127.0.0.1:10001/devstoreaccount1/orders?comp=metadata	-	-
Summarised	PUT	http://127.0.0.1:10001/devstoreaccount1/orders/messages/m1?popreceipt=p1&visibilitytimeout=0	-	-
Summarised	GET	http://127.0.0.1:10001/devstoreaccount1/orders/messages?numofmessages=5	-	-
Summarised	GET	http://127.0.0.1:10001/devstoreaccount1/?comp=list	-	-
Summarised	GET	http://127.0.0.1:10001/devstoreaccount1/?restype=service&comp=properties	-	-
Summarised	GET	http://127.0.0.1:10001/devstoreaccount1/orders?comp=acl	-	-
Summarised	GET	http://localhost:54322/devstoreaccount1/orders?comp=metadata	-	-
Summarised	PUT	http://localhost:54322/devstoreaccount1/orders?comp=metadata	-	-
Summarised	PUT	http://localhost:54322/devstoreaccount1/orders/messages/m1?popreceipt=p1&visibilitytimeout=0	-	-
Summarised	GET	http://localhost:54322/devstoreaccount1/orders/messages?numofmessages=5	-	-
Summarised	GET	http://localhost:54322/devstoreaccount1/?comp=list	-	-
Summarised	GET	http://localhost:54322/devstoreaccount1/?restype=service&comp=properties	-	-
Summarised	GET	http://localhost:54322/devstoreaccount1/orders?comp=acl	-	-
""";
}
