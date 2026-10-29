namespace Kronikol.Tests.BlobStorage;

/// <summary>
/// What the last package published before #125's R2 (Kronikol.Extensions.BlobStorage 5.27.8) recorded for each request
/// a <c>BlobServiceClient</c> sends under six connection shapes, and for the other forms its classifier reads, on Azure
/// and on Azurite's built-in account, at <c>Detailed</c> and <c>Summarised</c>: one line per request, "verbosity,
/// method, url, label, uri" separated by tabs, label and uri "-" when it recorded nothing. Made once by
/// plans/EMULATOR_URL_SHAPES_PLAN.harness/probes/before-r2-record-blob.cs against the published package; never edited.
/// </summary>
internal static class RecordedBefore11Record
{
    public const string Lines = """
Detailed	PUT	https://shop.blob.core.windows.net/orders?restype=container	CreateContainer	https://shop.blob.core.windows.net/orders
Detailed	PUT	https://shop.blob.core.windows.net/orders/order-1.json	Upload	https://shop.blob.core.windows.net/orders/order-1.json
Detailed	GET	https://shop.blob.core.windows.net/orders/order-1.json	Download	https://shop.blob.core.windows.net/orders/order-1.json
Detailed	HEAD	https://shop.blob.core.windows.net/orders/order-1.json	GetProperties	https://shop.blob.core.windows.net/orders/order-1.json
Detailed	GET	https://shop.blob.core.windows.net/orders?restype=container&comp=list	ListBlobs	https://shop.blob.core.windows.net/orders
Detailed	DELETE	https://shop.blob.core.windows.net/orders/order-1.json	Delete	https://shop.blob.core.windows.net/orders/order-1.json
Detailed	DELETE	https://shop.blob.core.windows.net/orders?restype=container	DeleteContainer	https://shop.blob.core.windows.net/orders
Detailed	GET	https://shop.blob.core.windows.net/?comp=list	Other	https://shop.blob.core.windows.net/?comp=list
Detailed	PUT	http://127.0.0.1:10000/devstoreaccount1/orders?restype=container	Upload	http://127.0.0.1:10000/devstoreaccount1/orders
Detailed	PUT	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json	Upload	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json
Detailed	GET	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json	Download	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json
Detailed	HEAD	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json	GetProperties	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json
Detailed	GET	http://127.0.0.1:10000/devstoreaccount1/orders?restype=container&comp=list	Other	http://127.0.0.1:10000/devstoreaccount1/orders
Detailed	DELETE	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json	Delete	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json
Detailed	DELETE	http://127.0.0.1:10000/devstoreaccount1/orders?restype=container	Delete	http://127.0.0.1:10000/devstoreaccount1/orders
Detailed	GET	http://127.0.0.1:10000/devstoreaccount1/?comp=list	Other	http://127.0.0.1:10000/devstoreaccount1
Detailed	PUT	http://127.0.0.1:54321/devstoreaccount1/orders?restype=container	Upload	http://127.0.0.1:54321/devstoreaccount1/orders
Detailed	PUT	http://127.0.0.1:54321/devstoreaccount1/orders/order-1.json	Upload	http://127.0.0.1:54321/devstoreaccount1/orders/order-1.json
Detailed	GET	http://127.0.0.1:54321/devstoreaccount1/orders/order-1.json	Download	http://127.0.0.1:54321/devstoreaccount1/orders/order-1.json
Detailed	HEAD	http://127.0.0.1:54321/devstoreaccount1/orders/order-1.json	GetProperties	http://127.0.0.1:54321/devstoreaccount1/orders/order-1.json
Detailed	GET	http://127.0.0.1:54321/devstoreaccount1/orders?restype=container&comp=list	Other	http://127.0.0.1:54321/devstoreaccount1/orders
Detailed	DELETE	http://127.0.0.1:54321/devstoreaccount1/orders/order-1.json	Delete	http://127.0.0.1:54321/devstoreaccount1/orders/order-1.json
Detailed	DELETE	http://127.0.0.1:54321/devstoreaccount1/orders?restype=container	Delete	http://127.0.0.1:54321/devstoreaccount1/orders
Detailed	GET	http://127.0.0.1:54321/devstoreaccount1/?comp=list	Other	http://127.0.0.1:54321/devstoreaccount1
Detailed	PUT	http://azurite:10000/devstoreaccount1/orders?restype=container	Upload	http://azurite:10000/devstoreaccount1/orders
Detailed	PUT	http://azurite:10000/devstoreaccount1/orders/order-1.json	Upload	http://azurite:10000/devstoreaccount1/orders/order-1.json
Detailed	GET	http://azurite:10000/devstoreaccount1/orders/order-1.json	Download	http://azurite:10000/devstoreaccount1/orders/order-1.json
Detailed	HEAD	http://azurite:10000/devstoreaccount1/orders/order-1.json	GetProperties	http://azurite:10000/devstoreaccount1/orders/order-1.json
Detailed	GET	http://azurite:10000/devstoreaccount1/orders?restype=container&comp=list	Other	http://azurite:10000/devstoreaccount1/orders
Detailed	DELETE	http://azurite:10000/devstoreaccount1/orders/order-1.json	Delete	http://azurite:10000/devstoreaccount1/orders/order-1.json
Detailed	DELETE	http://azurite:10000/devstoreaccount1/orders?restype=container	Delete	http://azurite:10000/devstoreaccount1/orders
Detailed	GET	http://azurite:10000/devstoreaccount1/?comp=list	Other	http://azurite:10000/devstoreaccount1
Detailed	PUT	http://localhost:54321/devstoreaccount1/orders?restype=container	Upload	http://localhost:54321/devstoreaccount1/orders
Detailed	PUT	http://localhost:54321/devstoreaccount1/order-1.json	Upload	http://localhost:54321/devstoreaccount1/order-1.json
Detailed	GET	http://localhost:54321/devstoreaccount1/order-1.json	Download	http://localhost:54321/devstoreaccount1/order-1.json
Detailed	HEAD	http://localhost:54321/devstoreaccount1/order-1.json	GetProperties	http://localhost:54321/devstoreaccount1/order-1.json
Detailed	GET	http://localhost:54321/devstoreaccount1/orders?restype=container&comp=list	Other	http://localhost:54321/devstoreaccount1/orders
Detailed	DELETE	http://localhost:54321/devstoreaccount1/order-1.json	Delete	http://localhost:54321/devstoreaccount1/order-1.json
Detailed	DELETE	http://localhost:54321/devstoreaccount1/orders?restype=container	Delete	http://localhost:54321/devstoreaccount1/orders
Detailed	GET	http://localhost:54321/devstoreaccount1/?comp=list	Other	http://localhost:54321/devstoreaccount1
Detailed	PUT	http://docker-host.internal:54321/devstoreaccount1/orders?restype=container	Upload	http://docker-host.internal:54321/devstoreaccount1/orders
Detailed	PUT	http://docker-host.internal:54321/devstoreaccount1/order-1.json	Upload	http://docker-host.internal:54321/devstoreaccount1/order-1.json
Detailed	GET	http://docker-host.internal:54321/devstoreaccount1/order-1.json	Download	http://docker-host.internal:54321/devstoreaccount1/order-1.json
Detailed	HEAD	http://docker-host.internal:54321/devstoreaccount1/order-1.json	GetProperties	http://docker-host.internal:54321/devstoreaccount1/order-1.json
Detailed	GET	http://docker-host.internal:54321/devstoreaccount1/orders?restype=container&comp=list	Other	http://docker-host.internal:54321/devstoreaccount1/orders
Detailed	DELETE	http://docker-host.internal:54321/devstoreaccount1/order-1.json	Delete	http://docker-host.internal:54321/devstoreaccount1/order-1.json
Detailed	DELETE	http://docker-host.internal:54321/devstoreaccount1/orders?restype=container	Delete	http://docker-host.internal:54321/devstoreaccount1/orders
Detailed	GET	http://docker-host.internal:54321/devstoreaccount1/?comp=list	Other	http://docker-host.internal:54321/devstoreaccount1
Detailed	PUT	https://shop.blob.core.windows.net/orders/order-1.json?comp=metadata	SetMetadata	https://shop.blob.core.windows.net/orders/order-1.json
Detailed	GET	https://shop.blob.core.windows.net/orders/order-1.json?comp=metadata	GetMetadata	https://shop.blob.core.windows.net/orders/order-1.json
Detailed	PUT	https://shop.blob.core.windows.net/orders/order-1.json?comp=copy	Copy	https://shop.blob.core.windows.net/orders/order-1.json
Detailed	PUT	https://shop.blob.core.windows.net/orders/order-1.json?comp=block&blockid=YQ%3D%3D	PutBlock	https://shop.blob.core.windows.net/orders/order-1.json
Detailed	PUT	https://shop.blob.core.windows.net/orders/order-1.json?comp=blocklist	PutBlockList	https://shop.blob.core.windows.net/orders/order-1.json
Detailed	PUT	https://shop.blob.core.windows.net/orders/order-1.json?comp=lease	Lease	https://shop.blob.core.windows.net/orders/order-1.json
Detailed	GET	https://shop.blob.core.windows.net/orders/2026%2F10%2Forder-1.json	Download	https://shop.blob.core.windows.net/orders/2026%2F10%2Forder-1.json
Detailed	GET	https://shop.blob.core.windows.net/orders/a b.json	Download	https://shop.blob.core.windows.net/orders/a b.json
Detailed	GET	https://shop.blob.core.windows.net/?restype=service&comp=properties	Other	https://shop.blob.core.windows.net/?restype=service&comp=properties
Detailed	GET	https://shop.blob.core.windows.net/orders?restype=container&comp=acl	Other	https://shop.blob.core.windows.net/orders
Detailed	PUT	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json?comp=metadata	SetMetadata	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json
Detailed	GET	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json?comp=metadata	GetMetadata	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json
Detailed	PUT	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json?comp=copy	Copy	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json
Detailed	PUT	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json?comp=block&blockid=YQ%3D%3D	PutBlock	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json
Detailed	PUT	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json?comp=blocklist	PutBlockList	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json
Detailed	PUT	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json?comp=lease	Lease	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json
Detailed	GET	http://127.0.0.1:10000/devstoreaccount1/orders/2026%2F10%2Forder-1.json	Download	http://127.0.0.1:10000/devstoreaccount1/orders/2026%2F10%2Forder-1.json
Detailed	GET	http://127.0.0.1:10000/devstoreaccount1/orders/a b.json	Download	http://127.0.0.1:10000/devstoreaccount1/orders/a b.json
Detailed	GET	http://127.0.0.1:10000/devstoreaccount1/?restype=service&comp=properties	Other	http://127.0.0.1:10000/devstoreaccount1
Detailed	GET	http://127.0.0.1:10000/devstoreaccount1/orders?restype=container&comp=acl	Other	http://127.0.0.1:10000/devstoreaccount1/orders
Detailed	PUT	http://localhost:54321/devstoreaccount1/orders/order-1.json?comp=metadata	SetMetadata	http://localhost:54321/devstoreaccount1/orders/order-1.json
Detailed	GET	http://localhost:54321/devstoreaccount1/orders/order-1.json?comp=metadata	GetMetadata	http://localhost:54321/devstoreaccount1/orders/order-1.json
Detailed	PUT	http://localhost:54321/devstoreaccount1/orders/order-1.json?comp=copy	Copy	http://localhost:54321/devstoreaccount1/orders/order-1.json
Detailed	PUT	http://localhost:54321/devstoreaccount1/orders/order-1.json?comp=block&blockid=YQ%3D%3D	PutBlock	http://localhost:54321/devstoreaccount1/orders/order-1.json
Detailed	PUT	http://localhost:54321/devstoreaccount1/orders/order-1.json?comp=blocklist	PutBlockList	http://localhost:54321/devstoreaccount1/orders/order-1.json
Detailed	PUT	http://localhost:54321/devstoreaccount1/orders/order-1.json?comp=lease	Lease	http://localhost:54321/devstoreaccount1/orders/order-1.json
Detailed	GET	http://localhost:54321/devstoreaccount1/orders/2026%2F10%2Forder-1.json	Download	http://localhost:54321/devstoreaccount1/orders/2026%2F10%2Forder-1.json
Detailed	GET	http://localhost:54321/devstoreaccount1/orders/a b.json	Download	http://localhost:54321/devstoreaccount1/orders/a b.json
Detailed	GET	http://localhost:54321/devstoreaccount1/?restype=service&comp=properties	Other	http://localhost:54321/devstoreaccount1
Detailed	GET	http://localhost:54321/devstoreaccount1/orders?restype=container&comp=acl	Other	http://localhost:54321/devstoreaccount1/orders
Summarised	PUT	https://shop.blob.core.windows.net/orders?restype=container	CreateContainer	https://shop.blob.core.windows.net/orders
Summarised	PUT	https://shop.blob.core.windows.net/orders/order-1.json	Upload	https://shop.blob.core.windows.net/orders/order-1.json
Summarised	GET	https://shop.blob.core.windows.net/orders/order-1.json	Download	https://shop.blob.core.windows.net/orders/order-1.json
Summarised	HEAD	https://shop.blob.core.windows.net/orders/order-1.json	GetProperties	https://shop.blob.core.windows.net/orders/order-1.json
Summarised	GET	https://shop.blob.core.windows.net/orders?restype=container&comp=list	ListBlobs	https://shop.blob.core.windows.net/orders
Summarised	DELETE	https://shop.blob.core.windows.net/orders/order-1.json	Delete	https://shop.blob.core.windows.net/orders/order-1.json
Summarised	DELETE	https://shop.blob.core.windows.net/orders?restype=container	DeleteContainer	https://shop.blob.core.windows.net/orders
Summarised	GET	https://shop.blob.core.windows.net/?comp=list	-	-
Summarised	PUT	http://127.0.0.1:10000/devstoreaccount1/orders?restype=container	Upload	http://127.0.0.1:10000/devstoreaccount1/orders
Summarised	PUT	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json	Upload	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json
Summarised	GET	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json	Download	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json
Summarised	HEAD	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json	GetProperties	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json
Summarised	GET	http://127.0.0.1:10000/devstoreaccount1/orders?restype=container&comp=list	-	-
Summarised	DELETE	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json	Delete	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json
Summarised	DELETE	http://127.0.0.1:10000/devstoreaccount1/orders?restype=container	Delete	http://127.0.0.1:10000/devstoreaccount1/orders
Summarised	GET	http://127.0.0.1:10000/devstoreaccount1/?comp=list	-	-
Summarised	PUT	http://127.0.0.1:54321/devstoreaccount1/orders?restype=container	Upload	http://127.0.0.1:54321/devstoreaccount1/orders
Summarised	PUT	http://127.0.0.1:54321/devstoreaccount1/orders/order-1.json	Upload	http://127.0.0.1:54321/devstoreaccount1/orders/order-1.json
Summarised	GET	http://127.0.0.1:54321/devstoreaccount1/orders/order-1.json	Download	http://127.0.0.1:54321/devstoreaccount1/orders/order-1.json
Summarised	HEAD	http://127.0.0.1:54321/devstoreaccount1/orders/order-1.json	GetProperties	http://127.0.0.1:54321/devstoreaccount1/orders/order-1.json
Summarised	GET	http://127.0.0.1:54321/devstoreaccount1/orders?restype=container&comp=list	-	-
Summarised	DELETE	http://127.0.0.1:54321/devstoreaccount1/orders/order-1.json	Delete	http://127.0.0.1:54321/devstoreaccount1/orders/order-1.json
Summarised	DELETE	http://127.0.0.1:54321/devstoreaccount1/orders?restype=container	Delete	http://127.0.0.1:54321/devstoreaccount1/orders
Summarised	GET	http://127.0.0.1:54321/devstoreaccount1/?comp=list	-	-
Summarised	PUT	http://azurite:10000/devstoreaccount1/orders?restype=container	Upload	http://azurite:10000/devstoreaccount1/orders
Summarised	PUT	http://azurite:10000/devstoreaccount1/orders/order-1.json	Upload	http://azurite:10000/devstoreaccount1/orders/order-1.json
Summarised	GET	http://azurite:10000/devstoreaccount1/orders/order-1.json	Download	http://azurite:10000/devstoreaccount1/orders/order-1.json
Summarised	HEAD	http://azurite:10000/devstoreaccount1/orders/order-1.json	GetProperties	http://azurite:10000/devstoreaccount1/orders/order-1.json
Summarised	GET	http://azurite:10000/devstoreaccount1/orders?restype=container&comp=list	-	-
Summarised	DELETE	http://azurite:10000/devstoreaccount1/orders/order-1.json	Delete	http://azurite:10000/devstoreaccount1/orders/order-1.json
Summarised	DELETE	http://azurite:10000/devstoreaccount1/orders?restype=container	Delete	http://azurite:10000/devstoreaccount1/orders
Summarised	GET	http://azurite:10000/devstoreaccount1/?comp=list	-	-
Summarised	PUT	http://localhost:54321/devstoreaccount1/orders?restype=container	Upload	http://localhost:54321/devstoreaccount1/orders
Summarised	PUT	http://localhost:54321/devstoreaccount1/order-1.json	Upload	http://localhost:54321/devstoreaccount1/order-1.json
Summarised	GET	http://localhost:54321/devstoreaccount1/order-1.json	Download	http://localhost:54321/devstoreaccount1/order-1.json
Summarised	HEAD	http://localhost:54321/devstoreaccount1/order-1.json	GetProperties	http://localhost:54321/devstoreaccount1/order-1.json
Summarised	GET	http://localhost:54321/devstoreaccount1/orders?restype=container&comp=list	-	-
Summarised	DELETE	http://localhost:54321/devstoreaccount1/order-1.json	Delete	http://localhost:54321/devstoreaccount1/order-1.json
Summarised	DELETE	http://localhost:54321/devstoreaccount1/orders?restype=container	Delete	http://localhost:54321/devstoreaccount1/orders
Summarised	GET	http://localhost:54321/devstoreaccount1/?comp=list	-	-
Summarised	PUT	http://docker-host.internal:54321/devstoreaccount1/orders?restype=container	Upload	http://docker-host.internal:54321/devstoreaccount1/orders
Summarised	PUT	http://docker-host.internal:54321/devstoreaccount1/order-1.json	Upload	http://docker-host.internal:54321/devstoreaccount1/order-1.json
Summarised	GET	http://docker-host.internal:54321/devstoreaccount1/order-1.json	Download	http://docker-host.internal:54321/devstoreaccount1/order-1.json
Summarised	HEAD	http://docker-host.internal:54321/devstoreaccount1/order-1.json	GetProperties	http://docker-host.internal:54321/devstoreaccount1/order-1.json
Summarised	GET	http://docker-host.internal:54321/devstoreaccount1/orders?restype=container&comp=list	-	-
Summarised	DELETE	http://docker-host.internal:54321/devstoreaccount1/order-1.json	Delete	http://docker-host.internal:54321/devstoreaccount1/order-1.json
Summarised	DELETE	http://docker-host.internal:54321/devstoreaccount1/orders?restype=container	Delete	http://docker-host.internal:54321/devstoreaccount1/orders
Summarised	GET	http://docker-host.internal:54321/devstoreaccount1/?comp=list	-	-
Summarised	PUT	https://shop.blob.core.windows.net/orders/order-1.json?comp=metadata	SetMetadata	https://shop.blob.core.windows.net/orders/order-1.json
Summarised	GET	https://shop.blob.core.windows.net/orders/order-1.json?comp=metadata	GetMetadata	https://shop.blob.core.windows.net/orders/order-1.json
Summarised	PUT	https://shop.blob.core.windows.net/orders/order-1.json?comp=copy	Copy	https://shop.blob.core.windows.net/orders/order-1.json
Summarised	PUT	https://shop.blob.core.windows.net/orders/order-1.json?comp=block&blockid=YQ%3D%3D	PutBlock	https://shop.blob.core.windows.net/orders/order-1.json
Summarised	PUT	https://shop.blob.core.windows.net/orders/order-1.json?comp=blocklist	PutBlockList	https://shop.blob.core.windows.net/orders/order-1.json
Summarised	PUT	https://shop.blob.core.windows.net/orders/order-1.json?comp=lease	Lease	https://shop.blob.core.windows.net/orders/order-1.json
Summarised	GET	https://shop.blob.core.windows.net/orders/2026%2F10%2Forder-1.json	Download	https://shop.blob.core.windows.net/orders/2026%2F10%2Forder-1.json
Summarised	GET	https://shop.blob.core.windows.net/orders/a b.json	Download	https://shop.blob.core.windows.net/orders/a b.json
Summarised	GET	https://shop.blob.core.windows.net/?restype=service&comp=properties	-	-
Summarised	GET	https://shop.blob.core.windows.net/orders?restype=container&comp=acl	-	-
Summarised	PUT	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json?comp=metadata	SetMetadata	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json
Summarised	GET	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json?comp=metadata	GetMetadata	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json
Summarised	PUT	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json?comp=copy	Copy	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json
Summarised	PUT	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json?comp=block&blockid=YQ%3D%3D	PutBlock	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json
Summarised	PUT	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json?comp=blocklist	PutBlockList	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json
Summarised	PUT	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json?comp=lease	Lease	http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json
Summarised	GET	http://127.0.0.1:10000/devstoreaccount1/orders/2026%2F10%2Forder-1.json	Download	http://127.0.0.1:10000/devstoreaccount1/orders/2026%2F10%2Forder-1.json
Summarised	GET	http://127.0.0.1:10000/devstoreaccount1/orders/a b.json	Download	http://127.0.0.1:10000/devstoreaccount1/orders/a b.json
Summarised	GET	http://127.0.0.1:10000/devstoreaccount1/?restype=service&comp=properties	-	-
Summarised	GET	http://127.0.0.1:10000/devstoreaccount1/orders?restype=container&comp=acl	-	-
Summarised	PUT	http://localhost:54321/devstoreaccount1/orders/order-1.json?comp=metadata	SetMetadata	http://localhost:54321/devstoreaccount1/orders/order-1.json
Summarised	GET	http://localhost:54321/devstoreaccount1/orders/order-1.json?comp=metadata	GetMetadata	http://localhost:54321/devstoreaccount1/orders/order-1.json
Summarised	PUT	http://localhost:54321/devstoreaccount1/orders/order-1.json?comp=copy	Copy	http://localhost:54321/devstoreaccount1/orders/order-1.json
Summarised	PUT	http://localhost:54321/devstoreaccount1/orders/order-1.json?comp=block&blockid=YQ%3D%3D	PutBlock	http://localhost:54321/devstoreaccount1/orders/order-1.json
Summarised	PUT	http://localhost:54321/devstoreaccount1/orders/order-1.json?comp=blocklist	PutBlockList	http://localhost:54321/devstoreaccount1/orders/order-1.json
Summarised	PUT	http://localhost:54321/devstoreaccount1/orders/order-1.json?comp=lease	Lease	http://localhost:54321/devstoreaccount1/orders/order-1.json
Summarised	GET	http://localhost:54321/devstoreaccount1/orders/2026%2F10%2Forder-1.json	Download	http://localhost:54321/devstoreaccount1/orders/2026%2F10%2Forder-1.json
Summarised	GET	http://localhost:54321/devstoreaccount1/orders/a b.json	Download	http://localhost:54321/devstoreaccount1/orders/a b.json
Summarised	GET	http://localhost:54321/devstoreaccount1/?restype=service&comp=properties	-	-
Summarised	GET	http://localhost:54321/devstoreaccount1/orders?restype=container&comp=acl	-	-
""";
}
