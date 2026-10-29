# Emulator URL shapes plan: #125

**Written:** 2026-10-27, at 5.12.0 (`v5.12.0`, `1ee6373a`), and checked again at 5.16.0 (`v5.16.0`, `15a95d4c`). The
BigQuery, Blob Storage, Storage Queues and Cloud Storage sources and their test projects last changed in `c63e35f3`
(doc comments only), so what the 5.12.0 packages were measured doing is what 5.16.0 does. **Status: green-lit on
2026-10-29, every question answered by the owner (§10). R1 released as 5.27.6 (tag `v5.27.6`) and R2 as 5.27.9
(tag `v5.27.9`): executed in full (§12).**
Evidence labels: **RUN** (measured here, against the published 5.12.0 packages), **READ** (in the source, `file:line`
at 5.16.0), **DOC** (a vendor's own source or documentation), **INFERRED** (reasoned from facts, stated by none),
**ISSUE** (taken from #125, not re-measured). Every RUN line has its probe and its output in
[`EMULATOR_URL_SHAPES_PLAN.harness/`](EMULATOR_URL_SHAPES_PLAN.harness/README.md).

#125 reports that a BigQuery client pointed at the emulator's root has every call drawn as `Other`, and none recorded
at `Summarised`. It asks for the classifier to accept the path with or without `/bigquery/v2`. The report is right and
the repro holds verbatim against the real emulator. The ask fixes one URL shape of one extension. The defect underneath
is wider, and has four layers:

1. **The classifiers read the absolute URL path as if the client always spoke to the production endpoint.** A client's
   base URI is exactly what an emulator setup changes, and the handler cannot see it. Four extensions break against
   their standard emulator setups: BigQuery behind Testcontainers' endpoint, Blob Storage and Storage Queues against
   Azurite, and Cloud Storage through the SDK's own `STORAGE_EMULATOR_HOST` support, which sets a root base URI.
2. **They also miss requests the SDK sends to the production endpoint.** Of the BigQuery API's 47 methods, 2 are
   `Other` and 15 are misread on Google's own endpoint. Of Cloud Storage's 82 methods, 39 are `Other`, among them the
   `rewriteTo` that `CopyObject` sends, and 33 more are drawn as another operation: deleting a bucket's ACL entry
   reads as deleting the bucket.
3. **`Summarised` drops whatever the classifier could not name, and nothing says so.** The wiki gives the reason as
   SDK housekeeping requests, which these clients never send. So every miss in layers 1 and 2 becomes data missing from
   the report.
4. **No test drives a real SDK client.** Every URL in the BigQuery tests is typed by the test's author, so the tests
   restate the classifier's assumptions. The probes here found all of the above by letting the SDK write the URL.

The fix this plan proposes is a rule per API that finds where the API's own path starts (§5.1 to §5.3); facts driven
by the real SDKs and by the generated request catalogue (§6); and an answer to what `Summarised` does with a call it
cannot name (Q1).

## 0. Summary

| | |
|---|---|
| What ships | **R1, a patch (Google):** BigQuery and Cloud Storage find the API path wherever the client's base URI puts it, and read every method the measured clients send. **R2, a patch (Azure):** Blob Storage and Storage Queues read Azurite's path-style URLs through the Azure SDK's own URI parsers. Both: the misreads in §3, and Q1's answer for `Summarised` |
| What a consumer sees | Against an emulator, every call named as it is against the cloud, at every verbosity. On the cloud endpoints, the IAM, undelete, row access policy, project list and copy calls named correctly. BigQuery's unnamed calls' Detailed URLs without the client's query string. History lines move for the renamed calls; under Q7 (a), history reads across them as no change (§7) |
| What it does not change | `Raw` (the URL as sent), every option, every public type and member, and the eleven other extensions (and the core SQL tracker) that skip `Other` at `Summarised` |
| Tests | Facts that drive the real `BigQueryClient`, `StorageClient`, `BlobServiceClient` and `QueueClient` through each handler into a stub transport, over every base URI and connection string shape measured here; and facts that classify every method the generated Google clients declare (§6). No network and no Docker |
| Bumps | Both patches (§7). Q1 option (b) would have made its part a minor. R1 is 5.27.6, R2 5.27.9 |
| Open | Nothing: the owner answered Q1 to Q7 on 2026-10-29 (§10), Q1 (a), Q7 (a) and the rest as recommended |

## 1. How far each claim was checked

| # | The issue says | Level | Verdict |
|---|---|---|---|
| C1 | The classifier recognises only paths that start `/bigquery/v2/projects/` | READ | **True**, plus the `/upload/` form: `^/(?:upload/)?bigquery/v2/projects/(?<project>[^/]+)/(?<rest>.+)$` (`BigQueryOperationClassifier.cs:13-16`), anything else `Other` (`:25`) |
| C2 | A client pointed at an emulator replaces the whole base, so it sends `/projects/{project}/…` | RUN | **True.** The real `BigQueryClient` 3.12.0 with `BaseUri` `http://127.0.0.1:9050/` sends `/projects/shop/…`, with or without the trailing slash (`results/bigquery-url-shapes.txt`) |
| C3 | Testcontainers' `BigQueryContainer.GetEmulatorEndpoint()` returns the emulator's root | DOC | **True.** `new UriBuilder(Uri.UriSchemeHttp, Hostname, GetMappedPublicPort(BigQueryBuilder.BigQueryPort)).ToString()` (testcontainers-dotnet `develop`, `BigQueryContainer.cs`) |
| C4 | At `Detailed` every call is `Other`, its URL keeping `?key=…&prettyPrint=false` | RUN | **True**, against the stub and against the real emulator 0.8.1 (`results/bigquery-emulator-live.txt`) |
| C5 | At `Summarised` none is recorded, and nothing says so | RUN, READ | **True, and understated.** Of 26 requests for 25 operations, `Summarised` kept 2, the upload's, because uploads alone keep the `/upload/bigquery/v2/` path (F3). The diagnostic page counts the dropped calls as invocations (F8), so the numbers disagree in plain sight and nothing points at it |
| C6 | The emulator also answers under `/bigquery/v2/` | RUN | **True.** Both shapes answer 200 on 0.8.1, and with that base every call is named |
| C7 | A private suite of about 550 scenarios: all 1,276 BigQuery calls in its 4.0.1 report are `Other` | ISSUE | Not re-measured (private). Consistent with C2 and C4 |
| C8 | Ask: `^/(?:upload/)?(?:bigquery/v2/)?projects/(?<project>[^/]+)/(?<rest>.+)$` | RUN | **Fixes the root shape only.** Behind a gateway or a proxy prefix (`http://gateway.local/bq/`) 24 of 26 requests stay `Other` (F2). It does not reach F4 to F8, nor the three sibling extensions (F9 to F14) |
| C9 | Every request this handler sees comes from a BigQuery client, so a `/projects/…` path cannot belong to another API | READ | **True** for the path `WithTestTracking` builds (`TrackingBigQueryHttpClientFactory.cs:19-22`). It is also the key to a better rule than two accepted prefixes: the API's own path can be found wherever the base puts it (§4) |
| C10 | The S3 classifier already accepts both URL shapes its client sends | READ | **True** (`S3OperationClassifier.cs:11-20`), and a different case: S3's two shapes are a closed set AWS defines, while a base URI is open-ended |
| C11 | `ghcr.io/goccy/bigquery-emulator:0.8.1` is "the image Testcontainers' BigQuery module starts" | DOC | **Not quite.** The module's default is `BigQueryBuilder.BigQueryImage = "ghcr.io/goccy/bigquery-emulator:0.4"`, marked `[Obsolete]` with the parameterless constructor that uses it, and a suite gets 0.8.1 by naming the image (testcontainers-dotnet `develop`, `BigQueryBuilder.cs`; pointed out by the session planning #205). It does not move the defect: the URL shape is the client's, whatever the emulator's version. That `/bigquery/v2/` also answers was measured on 0.8.1 only (C6) |

## 2. Where it stands today

Four extensions put a `DelegatingHandler` in their SDK's HTTP pipeline and name each call from its URL:

| Extension | How the handler gets in | What the classifier reads (READ) |
|---|---|---|
| BigQuery | `BigQueryClientBuilder.WithTestTracking` sets `HttpClientFactory`; the factory wraps `base.CreateHandler(args)` (`TrackingBigQueryHttpClientFactory.cs:19-22`) | `AbsolutePath` against a regex anchored on `/bigquery/v2/projects/` (`BigQueryOperationClassifier.cs:13-25`) |
| Cloud Storage | `StorageClientBuilder.WithTestTracking` (`StorageClientBuilderExtensions.cs:10-16`) | an unanchored regex that needs `/storage/v1/b/` (`CloudStorageOperationClassifier.cs:11-16`, `:25`) |
| Blob Storage | `BlobClientOptions.WithTestTracking` sets `Transport` (`BlobClientOptionsExtensions.cs:19-27`) | `^/(?<container>[^/?]+)(?:/(?<blob>[^?]+))?` (`BlobOperationClassifier.cs:19-30`) |
| Storage Queues | `QueueClientOptions.WithTestTracking` (`QueueClientOptionsExtensions.cs:10-16`) | `^/(?<queue>[^/?]+)(?:/messages(?:/(?<msgId>[^/?]+))?)?$` (`StorageQueueOperationClassifier.cs:10`, `:23-25`) |

Each handler records at `Detailed` unless the consumer chooses otherwise (each options type's `Verbosity`, `:18`),
and then does the same three things (BigQuery's lines; the other three are line for line alike):

- At `Summarised` an `Other` call goes to the server unrecorded (`BigQueryTrackingMessageHandler.cs:45-47`), and its
  phase variants carry the same skip (`:93`, `:125`). Beyond these four handlers the same skip is in eleven more
  extensions and the core SQL tracker (READ, a grep for `Summarised && … Other`): the Atlas Data API, Cosmos DB,
  DynamoDB, EF Core, EventBridge, MongoDB, Redis, S3, SNS, SQS and TcpTap's decoders, and `SqlDiagnosticTracker`.
- At `Detailed` the label is the operation's name, so an unnamed call reads `Other`, and `BuildCleanUri` returns the
  URL as sent when nothing was classified (`:171-174`), query string and all.
- The send is awaited with no `catch` (`:95`), so a send that throws leaves a request with no response. That is
  `ROADMAP.md` row 1.15's "ten SDK tracking handlers", still open.

The wiki states the skip as design: "In **Summarised** mode, `Other` operations are silently skipped"
(`Integration-BigQuery-Extension.md:68`, and `Integration-BlobStorage-Extension.md:72`, wiki `origin/master` at
`183dbb0`, 2026-10-27). Its example of an `Other` call is "SDK metadata `GET: /discovery/v1/apis`" (`:48`), and its
gloss is "discovery API, service account metadata" (`:66`).

## 3. Findings the issue does not state

| # | Finding | Level | Where |
|---|---|---|---|
| F1 | **The handler is not given the client's base URI.** `BaseUri` replaces the whole of `https://bigquery.googleapis.com/bigquery/v2/`, and Google's `CreateHttpClientArgs` carries no base URI. `WithTestTracking` holds the builder (`BigQueryClientBuilderExtensions.cs:14-19`) and could hand its `BaseUri` to the factory, but the wiki's own recipe builds the handler with no builder (`new BigQueryTrackingMessageHandler(trackingOptions, new HttpClientHandler())`, `Integration-BigQuery-Extension.md:164`), and an upload and a resumable upload's data leave the base anyway (F3, F16). So the path has to be read from the API's own grammar; a known base could only settle §9's second risk | READ, RUN | §4, §5.1 |
| F2 | **The ask's regex leaves every other base path unread.** With `BaseUri` `http://gateway.local/bq/` (a proxy, a gateway, an in-house emulator), 24 of 26 requests are `Other` and `Summarised` keeps 2. Under the ask's regex they stay so | RUN | §5.1 |
| F3 | **Uploads ignore the base path.** The generated client builds them as `/upload/` + its fixed `bigquery/v2/` + the path, from the host's root. So with a gateway base the upload goes to `/upload/bigquery/v2/projects/…`, outside the gateway's prefix. That is the one shape today's regex reads on every base, and any new rule must keep it | RUN | §5.1 |
| F4 | **On Google's own endpoint, BigQuery misses 2 methods and misreads 15.** `projects.list` (`GET /projects`) and `getServiceAccount` are `Other`, so `Summarised` drops them. The eight IAM methods (`{resource}:getIamPolicy`, `:setIamPolicy`, `:testIamPermissions` on tables, routines and row access policies) read as `Read table/orders:getIamPolicy`. `datasets/{d}:undelete` reads as `Read dataset/sales:undelete` (Detailed `/sales:undelete/sales:undelete`). The six row access policy methods are drawn as reads, updates and deletes of the table. A load job's data `PUT` is `List /job`. Measured over all 47 methods the generated client declares, and for the 25 operations through the real client | RUN | §5.1 |
| F5 | **A dataset's Detailed path names it twice.** `GetDataset("sales")` is drawn `Read /sales/sales`, `DeleteDataset` `Delete /sales/sales`: `BuildCleanUri` adds the dataset as context, then the dataset again as the resource (`BigQueryTrackingMessageHandler.cs:186-191`) | RUN | §5.1 |
| F6 | **An unnamed call's Detailed URL keeps the client's query string:** `key`, `prettyPrint`, `formatOptions.useInt64Timestamp` (`:173-174`). A named call's loses it | RUN, READ | §5.1, F17 |
| F7 | **The skip's stated reason does not occur.** None of the 26 requests the real client sent went anywhere but the BigQuery API, and every one of the API's 47 methods has a path that begins `projects` (the generated `RestPath`s, `results/api-surface.txt`). So for this client, `Summarised` dropping `Other` can only drop real BigQuery calls. The same holds for the 7 Blob, 7 Queue and 10 Cloud Storage requests measured: no housekeeping request among them | RUN | Q1 |
| F8 | **The diagnostic page counts what was dropped.** A component's invocation count includes every skipped call (`DiagnosticReportGenerator.cs:192-218`), so a suite like the issue's shows 1,276 BigQuery invocations and no BigQuery arrow, and nothing joins the two | READ | Q1 |
| F9 | **Blob Storage against Azurite reads the account as the container.** Azurite's URLs are path-style, `/devstoreaccount1/{container}/{blob}`, for `UseDevelopmentStorage=true` and for Testcontainers' Azurite module alike (DOC: its `GetBlobEndpoint()` appends `AzuriteBuilder.AccountName`). Every call names container `devstoreaccount1`; creating a container is drawn as `Upload` of a blob (`BlobOperationClassifier.cs:43`, `:60`), deleting one as a blob `Delete`, and listing blobs is `Other`, dropped at `Summarised` | RUN | R2 |
| F10 | **Storage Queues against Azurite name nothing.** All 7 calls are `Other`, drawn `Other /` at `Detailed`, and `Summarised` records none of them (`StorageQueueOperationClassifier.cs:10`, `:23-25`). This is #125's failure in full, in another package | RUN | R2 |
| F11 | **The Azure SDK's own parsers read Azurite correctly for the common hosts, not for every host.** `BlobUriBuilder` and `QueueUriBuilder` take the first segment as the account when the host is an IPv4 address or the port is one of twenty the SDK reserves for path-style URLs, among them Azurite's 10000 and 10001 (DOC: `IsHostIPEndPointStyle` needs a dot in the host and `IPAddress.TryParse`, or a port in `Constants.Sas.PathStylePorts`: 10000 to 10004, 10100 to 10104, 11000 to 11004, 11100 to 11104; azure-sdk-for-net `main`, `Azure.Storage.Common/src/Shared/UriExtensions.cs`, `Constants.cs`): `127.0.0.1` on any port, and `azurite:10000` as in docker compose. With a host name on another port (`localhost:54321`, or Testcontainers over a TCP Docker host, whose `Hostname` is that host's name), or an IPv6 literal such as `[::1]` on another port, they read the account as the container, as today | RUN, DOC | §5.2 |
| F12 | **Cloud Storage reads a bucket's sub-resources as the bucket.** Its regex has no end anchor (`CloudStorageOperationClassifier.cs:11`), so `b/{bucket}/acl/{entity}` matches as `b/{bucket}`, and bucket-level or object-level is decided by `fullMatch.Contains("/o")` (`:49`). On Google's endpoint 17 sub-resource reads (ACLs, IAM, folders, managed folders, notification configs, caches, the storage layout) are drawn `GetBucket`, 5 sub-resource deletes `DeleteBucket`, so deleting an ACL entry reads as deleting the bucket, and `GET b/{bucket}/operations` `ListObjects`, its `/operations` holding `/o`. The same substring test misreads every bucket whose name starts with `o`: `GetBucket("orders")` is drawn `ListObjects`, and `DeleteBucket("orders")` is `Other`, dropped at `Summarised` | RUN, READ | §5.3 |
| F13 | **Cloud Storage misreads an object's sub-resources, and misses `CopyObject` and an upload's data.** Object ACL and IAM calls are drawn as calls on the object: deleting an ACL entry as `Delete` of the object, setting one or an IAM policy as `Upload`. `objects.update` (`PUT b/{bucket}/o/{object}`, a metadata update) is `Upload` too: the `PUT` rule (`:70`) needs an object in the path, which a resumable upload's data `PUT` (`/upload/storage/v1/b/{bucket}/o?upload_id=…`) never carries, so the one request that rule matches is not an upload, and the upload's data is `Other`. `CopyObject` copies through `rewriteTo` (`POST b/{src}/o/{obj}/rewriteTo/b/{dst}/o/{obj}`), and the classifier knows only the older `copyTo` (`:64`): `Detailed` draws `Other`, `Summarised` records nothing. In all, on Google's endpoint, 39 of the API's 82 methods are `Other` and 33 more are drawn as another operation | RUN | §5.3 |
| F14 | **Cloud Storage with a root base URI names nothing, and the SDK's own emulator support builds that base.** 82 of 82 methods, and 9 of 10 requests through the real client (the upload's start keeps its `/upload/storage/v1/` path). fake-gcs-server's .NET example sets `BaseUri = "http://localhost:8080/storage/v1/"`, but `StorageClientBuilder.EmulatorDetection`, which reads `STORAGE_EMULATOR_HOST`, sets the bare host as the base: `localhost:4443`, `http://localhost:4443`, `http://localhost:4443/` and `127.0.0.1:4443` each give `GET /b/orders`, a download `GET /b/orders/o/order-1.json?alt=media`, and `Other /` at `Detailed` (`results/storage-emulator-env.txt`). BigQuery's client has no emulator detection | RUN, DOC | §5.3 |
| F15 | **No test lets the SDK write the URL.** The BigQuery handler facts build every request from `https://bigquery.googleapis.com/bigquery/v2/projects/my-project` (`BigQueryTrackingMessageHandlerTests.cs:9`), and no test anywhere names Azurite's account (`devstoreaccount1`). The tests restate what the classifier's author believed the SDK sends, so they could not catch F2 to F14 | READ | §6 |
| F16 | **A failed send leaves a request with no response.** Met live: the emulator answers a resumable upload with a session URL on its own bind address (`http://0.0.0.0:9050/…`, the emulator's quirk), the client's `PUT` throws, and the handler has already logged it (`BigQueryTrackingMessageHandler.cs:95`, no `catch`). This is `ROADMAP.md` row 1.15 | RUN | Q5 |
| F17 | **The API key in the URL is never redacted.** Google's client sends `ApiKey` as `?key=` in the query. `CaptureRedaction.Secrets()` redacts an `x-goog-api-key` header (`CaptureRedaction.cs:32`) and reads headers and bodies only (`:117-130`), so the key is stored at `Raw` on every call and at `Detailed` on every unnamed one (F6). Azure's SAS `sig=` is in the same place | READ, RUN | Q4 |
| F18 | **Kronikol4J carries the same patterns.** Its BigQuery classifier holds the same anchored regex (`kronikol4j-gcp/…/BigQueryOperationClassifier.java:16-17`), and `kronikol4j-azure` has ports of the Blob and Queue classifiers (READ at `92d6b28`). How the Java SDKs shape these URLs was not measured | READ | §11 |
| F19 | **The reference consumer never sends the root shape**, which is why the demo never showed #125. BreakfastProvider's docker lanes set `BigQueryConfig__EmulatorEndpoint=http://localhost:9050/bigquery/v2/` (`.github/workflows/_tests.yml:151`), and its in-memory lanes use `InMemoryEmulator.BigQuery`, which routes only `/bigquery/v2/projects/…` (READ: its route strings) | READ | §6 |
| F20 | **Since 5.14.0, a declared party whose calls `Summarised` dropped reads as a wrong name.** Party names come from the recorded calls (`PartyCall`, `PartyResolver.cs:7`). A suite that declares the Azurite queue or the BigQuery emulator in `ReportConfigurationOptions.Parties`, while every call to it was dropped as `Other`, gets `DeclaredPartyNotSeen`: "declares 1 party no call in this run carries … check the name against the participant's" (`:183-204`). It points the reader at the name, not at the drop | READ | Q1 |

## 4. The underlying problem

Each classifier answers "which operation is this?" from the absolute URL path, and so assumes the client was built
with the production base. Three facts break that. An emulator setup changes the base: that is what `BaseUri`, a
connection string's `BlobEndpoint` and Testcontainers' endpoints are for. The handler cannot see the base (F1). And
the SDK, not the classifier's author, decides the shapes: a resumable upload's data request, `rewriteTo` rather than
`copyTo`, an upload path that ignores the base (F3, F13).

The ask treats the symptom as a missing prefix and adds one. A second accepted prefix works for one more setup and
fails the next one, a proxy or a gateway (F2), and it says nothing about Azure, whose emulator moves the account from
the host into the path (F9, F10). A rule that holds for every base comes from the API itself:

- **Google's REST APIs** define every method relative to the service root, and every BigQuery method's relative path
  begins `projects` (RUN, the 47 generated `RestPath`s: 39 directly, and the 8 IAM methods through their
  `{+resource}`, a `projects/…` name). Every Cloud Storage method's begins `b` (75), `projects` (6) or `channels`
  (1). Because the handler sees only its own client's requests (C9), the API path starts at the first of those
  segments, whatever precedes it.
- **Azure's SDK** already ships the parser that splits a URL into account, container and blob, and knows the
  path-style rule its clients follow (F11). A classifier that uses it agrees with the client by construction, and
  needs one fallback, for the hosts the SDK's own rule does not recognise.

Two more defects turn each miss into data loss. `Summarised` drops what was not named, for a reason these clients do
not produce (F7), and no test drives the SDK, so nobody saw the misses (F15). A fix that leaves either in place leaves
the next gap silent: a new SDK version's method, a new emulator, a new base shape.

## 5. The design

### 5.1 BigQuery

**The path rule.** The API path starts at the leftmost `projects` segment that is followed by one of the API's
collections (`datasets`, `jobs`, `queries`, `serviceAccount`) or ends the path. Whatever precedes it is the client's
base and is ignored. The collection anchor keeps the two awkward cases right:

| Path sent | Base | Read as |
|---|---|---|
| `/bigquery/v2/projects/shop/queries` | Google's | `Query /query` |
| `/projects/shop/queries` | an emulator's root | `Query /query` |
| `/bq/projects/shop/queries` | a gateway | `Query /query` |
| `/upload/bigquery/v2/projects/shop/jobs` | any (F3) | `Create /job` |
| `/projects/shop/datasets/projects/tables/t` | a dataset named `projects` | `Read /projects/table/t` |
| `/projects/bq/projects/shop/datasets` | a base path holding a `projects` segment | `Create /dataset`, project `shop` |
| `/bigquery/v2/projects` | Google's | `List /project` |

**The misreads (F4, F5).** Existing `BigQueryOperation` values only, so no public member is added:

| Method | Today | Proposed |
|---|---|---|
| `GET projects` (`projects.list`) | `Other` | `List`, resource `project` |
| `GET projects/{p}/serviceAccount` | `Other` | `Read`, resource `serviceAccount` |
| `POST {resource}:getIamPolicy`, `:testIamPermissions` | `Read table/{t}:getIamPolicy` | `Read`, resource `iamPolicy`, named for the table, routine or policy it is on |
| `POST {resource}:setIamPolicy` | `Read table/{t}:setIamPolicy` | `Update`, resource `iamPolicy` |
| `POST datasets/{d}:undelete` | `Read dataset/{d}:undelete` | `Create`, resource `dataset` (it brings the dataset back) |
| `tables/{t}/rowAccessPolicies`: list, insert, `:batchDelete` | `Read` of the table | `List`, `Create`, `Delete`, resource `rowAccessPolicy` |
| `tables/{t}/rowAccessPolicies/{r}`: get, update, delete | `Read`, `Update`, `Delete` of the table | the same operations, resource `rowAccessPolicy/{r}`; the Detailed path keeps the table it is on, so history can map the line back (§7) |
| `PUT /upload/…/jobs?upload_id=…` (a load job's data) | `List /job` | `Insert /job`, as `insertAll` is `Insert` of a table |
| A dataset's Detailed path | `/sales/sales` | `/sales` |
| An unnamed call's Detailed path | the URL as sent, query and all | the API path, without the query |

A custom method is split off at its colon before the segments are read, so a resource name never carries
`:getIamPolicy` again. `Raw` keeps the URL as sent, as now.

### 5.2 Blob Storage and Storage Queues

The account, container, blob, queue and message id come from the Azure SDK's own parsers, `BlobUriBuilder` and
`QueueUriBuilder` (the packages already depend on `Azure.Storage.Blobs` and `Azure.Storage.Queues`). The query rules
stay as they are (`restype`, `comp`, `peekonly`, `popreceipt`). One fallback covers F11's hosts: when the parser finds
no account and the first path segment is `devstoreaccount1`, Azurite's built-in account and the one
`UseDevelopmentStorage=true` and Testcontainers' Azurite module use, that segment is the account.

A Blob Storage call with no container (listing containers, the service's properties) keeps its URL as sent, query
included, as on Azure today (`BlobTrackingMessageHandler.cs:171-174`), less the account segment Azurite puts in the
path. Without that, R2 would give these calls on Azurite the query string they do not carry today, and a different
line from the same call on Azure.

What stays unread: an Azurite account of the user's own (`AZURITE_ACCOUNTS`) served on a host name with a port other
than the emulator's. The wiki says so, with the way round: use `127.0.0.1` or the emulator's ports. The parser is the
consumer's own version's, and its reserved ports came with the releases of August 2020 (DOC: Azure.Storage.Blobs
12.5.0, "Added support for custom local emulator hostname for blob storage endpoints"; the rule is
Azure.Storage.Common's, which Queues shares). On an older client such an account is unread on the emulator's ports
too. The built-in account, which every setup of §6.1 uses, is read through the fallback on any version, so the floors
#205 sets (§6.1) do not move what R2 names there.

### 5.3 Cloud Storage

The same idea as §5.1, with the same kind of anchor. The API path starts at the leftmost `b` segment that ends the
path (`buckets.list`, `buckets.insert`) or whose bucket is followed by the end, by `o` or by one of the bucket
sub-resources §6.2's catalogue lists; else at `projects/{p}/hmacKeys` or `projects/{p}/serviceAccount`, or at
`channels/stop`. Whatever precedes it is the client's base, and the path is read to its end: a bucket's or an
object's sub-resource is never taken for the bucket or the object (F12, F13).
Bucket-level or object-level comes from the parse, an `o` segment after the bucket, never from a substring. Then:

| Request | Today | Proposed |
|---|---|---|
| `POST …/rewriteTo/…` (`CopyObject`) | `Other` | `Copy`, as `copyTo` is |
| `PUT /upload/storage/v1/b/{b}/o?upload_id=…` (an upload's data) | `Other` | `Upload`, as its start is |
| `PUT b/{b}/o/{o}` (`objects.update`) | `Upload` | `UpdateMetadata`, as `PATCH` is |
| `GET b/{b}`, `DELETE b/{b}` for any bucket name | `ListObjects`, `Other` when the name starts with `o` | `GetBucket`, `DeleteBucket` |
| A sub-resource of a bucket or an object (ACLs, IAM, folders, managed folders, notification configs, caches, operations) | `GetBucket`, `DeleteBucket`, `Delete`, `Upload`, `GetMetadata` | `Other`, with its path |

The methods left at `Other` (those sub-resources, HMAC keys, bucket updates, restores, moves) would need new
`CloudStorageOperation` members to be named, a minor for calls no measured test makes. Drawing them as `Other` is
honest where today's names are wrong; what `Other` costs them at `Summarised` is Q1's answer.

### 5.4 What `Summarised` does with a call it cannot name

This is Q1. The recommendation is to record it, for these four handlers: an unnamed call is drawn at `Summarised` as
`Other`, with its API path and no body or headers, as every `Summarised` call is. The skip (`:45-47`) and the variant
flag (`:93`, `:125`) go from these four handlers, and the wiki's "silently skipped" sentence goes with them. The
reasons: the skip's stated reason does not occur for these clients (F7); `Summarised` exists to keep a report small,
not to make it partial; and after §5.1 to §5.3 an unnamed call is rare, so the arrow costs little. It is also what
keeps the next gap, a method a later SDK adds, from being silent.

The eleven other extensions and the core SQL tracker that skip `Other` are left alone. Some of them may use the skip
as designed, for an SDK's own housekeeping calls, which this plan did not measure (§11).

### 5.5 What does not change

`Raw`, the options types, every public type and member, the operation enums, `BigQueryTracker`'s manual logging (it
takes an already classified operation), and the other extensions.

## 6. Tests

In TDD order. Each fact is written first and seen red on 5.12.0 (§6.4).

### 6.1 Facts that let the SDK write the URL

A small transport helper per SDK family puts the shipped handler over a stub server, exactly as the harness probes do:
a Google `HttpClientFactory` whose `CreateHandler` returns the tracking handler over the stub, and Azure's
`WithTestTracking(options, innerHandler)`. The stub answers with a body every response type can parse, and a
`Location` header for a resumable upload's start. A fact then calls the real client and asserts what each verbosity
recorded.

| Client | Shapes | Operations |
|---|---|---|
| `BigQueryClient` | `BaseUri` default, `http://127.0.0.1:9050/`, the same without the slash, `…/bigquery/v2/`, `http://gateway.local/bq/` | the 25 of `probes/bigquery-url-shapes.cs`, and `ExecuteQuery` on both of its paths: `jobs.query`, and the job path a `QueryOptions.Priority` forces (§12) |
| `StorageClient` | default, `…/storage/v1/`, root, and `STORAGE_EMULATOR_HOST` through `EmulatorDetection` (a root the SDK sets, F14) | the 8 of `probes/sibling-url-shapes.cs`, `CopyObject`, and a bucket whose name starts with `o` |
| `BlobServiceClient` | a production account, `UseDevelopmentStorage=true`, `127.0.0.1` on a random port, `azurite:10000`, `localhost` on a random port, a TCP Docker host's name on a random port | the 7 of `probes/sibling-url-shapes.cs`, plus listing containers |
| `QueueClient` | the same six | the 7 of `probes/sibling-url-shapes.cs`, plus listing queues |

Each asserts the label and the Detailed URL, and that `Summarised` records the call (Q1 (a)) or counts it (Q1 (b)).

#205, green-lit on 2026-10-28 and being built, sets each package's floor and keeps each suite on the newest client
through a direct float, and its CI step runs the same suites on the floor's client too (`-p:FloorRun=true`,
`CLIENT_FLOORS_PLAN.md`, its changes 3 and 4). So these facts meet two clients per SDK. BigQuery's floor, 3.0.0,
sends a query as `jobs.insert` and `getQueryResults`, not `jobs.query` (§12), so each fact asserts how each request
the client sent was named and recorded, never which requests it sent.

### 6.2 Facts over the whole API surface

The generated Google clients declare every method: each request class carries its `RestPath` and `HttpMethod`
(`probes/api-surface.cs` reads them by reflection). A theory fills each template with sample values, puts it under
every base shape of §6.1, and asserts that it classifies to a named operation. A short allow-list names the methods
mapped to `Other` on purpose (§5.3's long tail). A new method in a new SDK version then turns the fact red until
someone decides what it is. Under #205 the suites stay on a float (`3.*`, `4.*`), so CI meets a new method on its
first restore after the SDK ships it, and the floor run reads the floor's generated client, which declares fewer
methods: the allow-list names a method, and never requires that the client declare it.

### 6.3 Facts kept and changed

The existing hand-written facts stay; they pin labels. The ones that assert today's skip (the handler facts'
`MakeUnrecognisedRequest`) change with Q1. New facts for `BuildCleanUri`: a dataset's path once, no query on an
unnamed call. If Q5 is taken, a send that throws records its response with the error, in each of the four handlers.

If Q7 (a) is taken, each release adds two facts. The kept rule matches what shipped: for every request §6.1's clients
and §6.2's catalogue make, the frozen classifier, label and `BuildCleanUri` give the label and URI the last
package published before the release recorded, or that it recorded none, read from a record of those requests made
once with that package and kept in the test project. And history reads across: runs built from that record,
fingerprinted as that package did, compared with a run of the same requests under the new build, read no change for
every row of §7's table, with no quiet run, while a scenario whose stub answers another status still reads as changed
and names the call. The fact also pins the one note that is new once: a service every call to which `Summarised`
dropped reads as a new dependency on the first run (§7). If Q7 (b) is taken instead, the history fact of
`CALL_OPERATION_AND_ORIGIN_PLAN.md` §4.8 holds each
release's translation, and a scenario holding a line from a row §7's table marks **no** reads no behaviour verdict on
its first run under the new build.

### 6.4 Proof

- **Red first.** The new facts copied into a worktree at `v5.12.0`, with any new helper stubbed so they compile: each
  fails on its own assertion, for the reason in §3.
- **Mutations.** Reverting each rule (the collection anchor, the colon split, the parser fallback, the `o` segment
  test, the query cut) turns exactly the facts that name it red.
- **Live, by hand, once per release.** `probes/bigquery-emulator-live.cs` against
  `ghcr.io/goccy/bigquery-emulator:0.8.1`. No emulator joins CI (Q3).

No Playwright fact: nothing in the report's page changes, only the labels and URLs it is given.

## 7. Releases

**R1, a patch: BigQuery and Cloud Storage** (one rule, one test helper). §5.1, §5.3, Q1's answer for both handlers, Q5
if taken, §6's facts for both. Closes #125.

**R2, a patch: Blob Storage and Storage Queues.** §5.2, Q1's answer for both handlers, Q5 if taken, §6's facts for
both.

Both are patches under `CLAUDE.md`: bug fixes, nothing new for a consumer to call. A bug fix that changes observable
behaviour is still a patch, so each changelog entry says what readers will see differently: calls that were `Other`
are named, calls `Summarised` dropped are drawn, BigQuery's unnamed calls lose the query string. Q1 (b) would add a
public member, which makes its part a minor. Every package moves together, per the repo rule.

**Cross-run history.** A call's history line is `Caller>Service METHOD target status` (READ,
`InteractionShape.cs:16-20`). At `Detailed` and `Summarised` the `METHOD` is the operation's label (Cloud Storage's
`Detailed` label also names the bucket and object, `CloudStorageOperationClassifier.cs:83-93`). At `Raw` it is the
HTTP verb, and the `Service` is the configured `ServiceName`, which this plan does not touch. So `Raw` lines do not
move, apart from Q5's status on a failed send, and every other line of a call this plan renames does. The owner asked
on 2026-10-27 that a change of name stop breaking history (`CALL_OPERATION_AND_ORIGIN_PLAN.md` §4.8, #192).

5.13.0, #190's R0, built the way to do that for a change to what a handler records (READ:
`InteractionShape.cs:50-63` and `:222-260`, `RequestResponseLog.cs:28-34`). Its
HTTP handler names a call to an unmapped remote host `{host}:{port}` where it wrote `localhost:{port}`, and the
fingerprint rule moves from 6 to 7. Rule 6 is kept as a function of the raw call (`KeptVersions`): the handler marks
a call its fallback named, in an internal member never written out (`NamedByRemoteHostFallback`), and rule 6 reads
that call under its old name. While a suite's window holds runs under 6, each run made in process also writes its
fingerprints under 6 on its ledger line (`bridge`), and two runs compare through the bridge either one wrote: the
rename reads as no change, with no quiet run, and a real change still reads as changed.

This plan's change is of the same kind, and Q7 (a) bridges it the same way. R1 built it on the general mark #192's R1
added in 5.17.0 (`RequestResponseLog.RecordedBefore8`, an internal `RecordedBefore`: the method and URI the old rule
recorded, or `NotRecorded`): a second mark of the same type, `RecordedBefore9`, set by the two handlers, which rule 8
reads, and which rules 6 and 7 read where no `RecordedBefore8` is set. A response marked `NotRecorded` is a failed
send's (Q5), and rules 6 to 8 read its call as unanswered. Rule 9 is R1's, so `KeptVersions` is `[6, 7, 8, 9]`. Each of
the four handlers keeps today's
classifier, label and `BuildCleanUri` (unchanged since 5.12.0), frozen and internal, and runs them beside the new ones
at `Detailed` and `Summarised`. A call whose old label or URI differs from the new carries the old ones in an internal
mark, and a call the old rule did not record (one `Summarised` skipped as `Other`) carries that. If Q5 is taken, a
send that throws carries that its old record had no response. Each release takes the next free number and adds it to
`KeptVersions`, which keeps every released rule from 6 on (`InteractionShape.cs:52-63`), and each mark names the first
rule that no longer needs it. Rules 6 and 7 both recorded these handlers' calls as today's classifiers name them, so
both read R1's and R2's marks, and a rule from between R1 and R2 reads only R2's. Under a kept rule a marked call reads
as the mark says, and an unrecorded one is left out. Every row of the table below then reads as no change, whatever
its stored line kept.

One note is new once, and it is true: a service every call to which `Summarised` dropped (Storage Queues on Azurite;
BigQuery on an emulator's root, in a suite that uploads nothing) reads as a new dependency on the first run. A bridge
maps a renamed `caller>service` pair, and has no way to say that a pair was absent (`InteractionShape.cs:322-335`,
`HistoryAnalyzer.cs:175-201`); no gate fails on a new dependency (`HistoryCommand.Maintenance.cs:58-67`).

The cost: the frozen code (the four classifier files hold 447 lines today, the four `BuildCleanUri` 67), kept as long
as `KeptVersions` holds a rule from before the release, to which 5.13.0 set no end; a second classification of each
call at `Detailed` and `Summarised`; and `InternalsVisibleTo` for the four packages, which `Kronikol.csproj`'s list
(`:17-64`) did not name at 5.16.0, though it named other extension packages already (gRPC, ProxyTap, ClickHouse.Driver,
Kafka's build interception and four SQL providers among them). 5.16.1 granted it to all four for the phase verbosity
(`a3f2f45a`, `Kronikol.csproj:74-76` and `:86`), so R1 added grants only for the two test projects (`:97-98`) and from
each extension to its own test project. As under 5.13.0, a run rebuilt from a report by
`kronikol merge --history` writes no bridge (`HistoryRunBuilder.cs:30-36`), so a suite recorded that way reads no
behaviour verdict on its first run across the move, as on every move before 5.13.0.

The other way was #192's §4.8 part 2: a translation of the ledger's stored lines (`shapes`, `callSets`, since 3.17.0)
to a form both sides reach. #192 shipped (5.17.0, 5.19.0 and 5.20.0) without it, and Q7 (a) needs none. A `Detailed` or
`Summarised` record keeps no raw URL, so this is what the stored lines would have allowed (READ, each handler's
`BuildCleanUri`):

| Line today | Under this plan | Reached by a translation of the stored line |
|---|---|---|
| BigQuery `Other`, at `Detailed`: 24 of 26 requests on an emulator's root or a gateway, and `projects.list` and `getServiceAccount` on Google's endpoint | named, with its clean path | **yes, exactly**: the old line holds the whole API path, so the new rule reads it |
| BigQuery's dataset path `/sales/sales`, and the IAM and `undelete` misreads | `/sales`, and §5.1's labels | **yes**: the old line keeps the doubled name or the `:getIamPolicy` suffix |
| BigQuery's row access policy calls, read as the table's own; a load job's data `PUT`, read as `List /job` like `jobs.list` | §5.1's labels | **yes, new to old only**: the new line maps back to the old one, provided its path keeps the table it is on (§5.1) |
| Blob Storage on Azurite, the account read as the container | the container and blob | **yes**: the old line starts `/devstoreaccount1/`, which §5.2's fallback reads |
| Cloud Storage on a root base, Storage Queues on Azurite: `Other /` | named | **no**: the old line holds no path (`gcs:///`, `storagequeue:///`) |
| Cloud Storage's bucket and object sub-resources, read as `GetBucket`, `DeleteBucket`, `Delete`, `Upload` | `Other`, with its path | **no**: the old line lost the sub-resource, and the new one does not carry the verb |
| A call `Summarised` dropped | drawn (Q1 (a)) | **no**: there is no old line |

A translation works per call line and can be partial (§4.8 part 3). A scenario that holds a line from a **no** row
reads no behaviour verdict on its first run after the upgrade, and its comparison starts again from runs under the new
rule. It does not read "changed", and every other scenario keeps its memory.

Q7 decides which way R1 and R2 take. With neither a mark nor a translation, every scenario whose lines move reads
"behaviour changed" once, as after 3.15.1's Cosmos DB fix, and the gate's default `--fail-on new-failures`
(`HistoryCommand.Maintenance.cs:62`) does not fail on it, though a gate that opted into `behaviour-change` fails once.
A version move with no mark, as 4.20.0 made for MongoDB,
quieted the first run after the upgrade for every scenario (`HistoryAnalyzer.cs:488-501`). From 5.13.0 a run made in
process bridges the kept rule by reading the new record under it, so the moved lines read "changed" just the same
(`HistoryRunBuilder.cs:137-146` and `HistoryAnalyzer.cs:336-357`). Only a move that kept no earlier
rule would quiet them, and it would quiet every scenario of every consumer and end #190's bridge early. A run line
records `ShapeVersion` and no Kronikol version
(`HistoryModel.cs:224-228`), so a later release can only translate runs made before a fix that moved the version.
#192's R1 writes the tool version on every run line (its §4.8 part 4), which helps only the runs recorded after it.

**Wiki**, after publication: `Integration-BigQuery-Extension.md` (`:1` and the examples at `:39-48` gain the
emulator's shapes, `:66-68` the `Other` rows, `:84` the upload), `Integration-BlobStorage-Extension.md` (`:72`, and an
Azurite section), the Storage Queues and Cloud Storage pages' `Other` sentences (found by a grep at execution), and
each page's emulator setup, the Cloud Storage page's naming `STORAGE_EMULATOR_HOST`. #202 lists wrong text on the same
pages (its item 16, an accessor note naming a DI extension these packages lack, and item 23, a test-context API that
does not exist); whichever change reaches a page first fixes its line of #202 too. The line numbers here are at
`183dbb0`; #205 adds a client versions note to each page, and a section after "Install" on the BigQuery page, so the
lines are found by a grep at execution.

**Kronikol4J:** its ports share the patterns (F18). The release notes it there as a follow-up for the port's own
plan, in a line of the port's divergence ledger (`docs/REMAINING_PARITY.md`, "## .NET-side features shipped after this
audit"), as every .NET release has one: not mirrored, as the port's classifiers keep the anchored patterns and the port
writes no history. (Written at first as "no ledger line", which the ledger's practice contradicts.)

## 8. Edit sites

| File | Change | Release |
|---|---|---|
| `src/Kronikol.Extensions.BigQuery/BigQueryOperationClassifier.cs` | the path rule (`:13-25`), the colon split, `projects` and `serviceAccount`, row access policies, the upload `PUT` (`:155-159`) | R1 |
| `src/Kronikol.Extensions.BigQuery/BigQueryTrackingMessageHandler.cs` | Q1 (`:45-47`, `:93`, `:125`), `BuildCleanUri` (`:171-195`), Q5 (`:95`) | R1 |
| `src/Kronikol.Extensions.CloudStorage/CloudStorageOperationClassifier.cs` | the path rule (`:11-16`, `:25`), the `o` segment (`:49`), `rewriteTo` (`:64`), the upload `PUT` (`:70`) | R1 |
| `src/Kronikol.Extensions.CloudStorage/CloudStorageTrackingMessageHandler.cs` | Q1 (`:45`, `:92`, `:124`), Q5 | R1 |
| `src/Kronikol.Extensions.BlobStorage/BlobOperationClassifier.cs` | `BlobUriBuilder` and the fallback (`:19-30`) | R2 |
| `src/Kronikol.Extensions.BlobStorage/BlobTrackingMessageHandler.cs` | Q1 (`:46`, `:93`, `:125`), Q5 | R2 |
| `src/Kronikol.Extensions.StorageQueues/StorageQueueOperationClassifier.cs` | `QueueUriBuilder` and the fallback (`:10-25`) | R2 |
| `src/Kronikol.Extensions.StorageQueues/StorageQueueTrackingMessageHandler.cs` | Q1 (`:45`, `:92`, `:124`), Q5 | R2 |
| `tests/Kronikol.Tests.BigQuery`, `…CloudStorage`, `…BlobStorage`, `…StorageQueues` | §6 | R1, R2 |
| `src/Kronikol/History/InteractionShape.cs` | under Q7 (a), `Version` moves with its note, `KeptVersions` gains the new number and keeps every rule it holds, and a kept rule reads a marked call as marked, beside `ServiceNameUnder`; under (b), the release's translation, wherever #192's R1 puts translations | R1, R2 |
| `src/Kronikol/Tracking/RequestResponseLog.cs` | under Q7 (a), the internal mark: the label and URI the old rule recorded, or that it recorded none, or (Q5) that it recorded no response, and the first rule that no longer reads it | R1 |
| `src/Kronikol/Kronikol.csproj` | under Q7 (a), `InternalsVisibleTo` for the four packages | R1, R2 |
| The four classifiers and handlers | under Q7 (a), today's classifier, label and `BuildCleanUri` kept as internal copies, and the mark set where they differ from the new; under Q5, the send's failure recorded through the core's `FailedSend`, as `TestTrackingMessageHandler.cs:299-328` does | R1, R2 |
| Doc comments that state the old behaviour | the verbosity enums' `Summarised` summaries and the classifiers' comments, checked by a grep at execution | R1, R2 |

## 9. Risks

- **Labels change for every affected suite**, once. That is the fix, and the changelog says so. What that costs
  cross-run history depends on Q7 (§7).
- **A base path that holds a collection's name** (`/projects/x/datasets/` as a base) would still mislead the rule.
  No such setup was found; the facts pin the cases §5.1 lists.
- **A container named `devstoreaccount1` on a custom domain** would be read as Azurite's account by §5.2's fallback.
  The fallback runs only when the SDK's parser finds no account, so production's `{account}.blob.core.windows.net`
  never reaches it.
- **The allow-list of §6.2 can grow into a list nobody reads.** Each entry names its method and why it stays `Other`.
- **The frozen rule could drift** (Q7 (a)). An edit to a kept copy would change what the kept rule reads, and every
  scenario whose calls it touches would read "changed". The copies share no code with the new classifiers, and
  §6.3's first fact pins them to what the last package published before the release recorded (5.27.2 for R1).

## 10. Open questions

Each is a decision, with a recommendation. **The owner answered every one on 2026-10-29:** Q1 (a), Q2 two
patches, Q3 no, Q4 its own issue, Q5 yes, Q6 as written, Q7 (a). On Q7 the owner noted that #192 had shipped (5.17.0,
5.19.0, 5.20.0): it built the general mark this plan needs (`RequestResponseLog.RecordedBefore8`) and left the
translation of stored lines to this change, so (b) was never coming from #192, and (a) extends #192's mark (§12).

- **Q1. What should `Summarised` do with a call it cannot name, in these four handlers?**
  (a) Record it like any `Summarised` call, labelled `Other`, with its API path. A patch, the behaviour change in the
  changelog. (b) Keep the skip, and count the skipped calls on the diagnostic page, through a new `ITrackingComponent`
  member with a default. A minor. (c) Leave the skip as it is: after §5.1 to §5.3 every request the measured clients
  send is named. **Recommended: (a).** The skip's stated reason does not occur for these clients (F7), and (b) puts the
  signal on a page few read, while (a) puts the call where the reader is looking. (c) leaves the next gap silent.
- **Q2. One release or two?** **Recommended: two**, R1 for the Google pair and R2 for the Azure pair, as each pair
  shares a rule and a test helper. One release of all four is also fine. `CLIENT_FLOORS_PLAN.md` (#205) asked, as its
  Q7, whether its R1 and this plan's R1 go out as one patch. It was green-lit on 2026-10-28 with this plan not, so its
  R1 ships alone; the two change different files of the BigQuery package, so neither waits on the other.
- **Q3. Should an emulator lane join CI** (the BigQuery emulator and Azurite through Testcontainers)? **Recommended:
  no.** What the handler records depends only on the request the SDK writes, which §6's facts reproduce without a
  network. The emulator's upload quirk (F16) would make a load job untestable there anyway. The live probe stays in
  the harness for a check by hand.
- **Q4. The API key in the URL (F17): fix it here, or in a plan of its own?** **Recommended: its own issue and patch.**
  It is the core redaction's gap, not a classifier's, it reaches every SDK that puts a credential in the query
  (`key=`, `sig=`), and its fix changes what `CaptureRedaction.Secrets()` redacts for everyone who opted in.
- **Q5. A send that throws leaves a request with no response (F16, `ROADMAP.md` 1.15). Fix it in the four handlers
  this plan rewrites?** **Recommended: yes**, as the same methods and the same test projects are open. The core HTTP
  handler already answers it: a send that throws is logged as a response whose status is the exception's type and
  whose `Error` is its message chain, then rethrown (`FailedSend`, `TestTrackingMessageHandler.cs:299-328`). The four
  would do the same, so the history line of a failed send moves at every verbosity, `Raw` included, from no status
  to the exception's type; under Q7 (a) the mark covers it. The other six handlers stay with row 1.15.
- **Q6. Are §5.1's labels right?** The IAM methods as `Read` and `Update` of an `iamPolicy`, `undelete` as `Create`,
  a load job's data as `Insert /job`. **Recommended: as written.** They use existing operation values, so no public
  member is added.
- **Q7. How do R1 and R2 carry cross-run history across the fix (§7)?** (a) Mark each call whose record moves, as
  5.13.0 did for #190: each handler keeps today's classifier beside the new one, each release moves
  `InteractionShape.Version` and adds it to `KeptVersions`, and every kept rule reads a marked call as it was
  recorded. Every row of §7's table reads as no change, with no quiet run and no false verdict, and R1 and R2 need
  nothing from #192. One true note is new once: a service every call to which `Summarised` dropped reads as a new
  dependency, which no gate fails on. It costs 514 lines of today's code kept frozen for as long as those rules are
  kept, which nothing yet ends, a second classification of each call at `Detailed` and `Summarised`, and four
  `InternalsVisibleTo` entries, a kind the core already grants other extension packages. The mark is internal, so
  both releases stay patches. (b) Ship after #192's translation of stored lines (its §4.8 part 2, in its R1, which
  is not green-lit): no frozen code, but the scenarios that hold a line from a row marked **no** read no behaviour
  verdict once. (c) Ship without a version move, as 3.15.1 did: "behaviour changed" once, only on the scenarios whose
  lines move, with the calls named, which the default gate does not fail on (a gate that opted into
  `behaviour-change` fails once). A version move with no mark, 4.20.0's way, is not offered: from 5.13.0 it reads as
  (c) (§7). **Recommended: (a)**, the owner having asked that renames stop breaking history.
  It reaches every row, including those no translation can, with the mechanism 5.13.0 already ships.

## 11. What it leaves

- The eleven other extensions and the core SQL tracker that skip `Other` at `Summarised`. Whether their `Other`
  holds housekeeping worth
  skipping (an SDK's own metadata reads, a driver's session statements) or real calls is a question per extension,
  for a sweep of its own.
- Kronikol4J's ports (F18), for that repository's plans.
- The BigQuery emulator's resumable upload URL on `0.0.0.0` (F16), upstream's to fix (goccy/bigquery-emulator).
- The six SDK handlers of row 1.15 that this plan does not touch.
- Names for Cloud Storage's long tail (§5.3), if a suite ever needs them.
- Saying that an emulator answered. 5.15.0, #190's R2, added `RequestResponseLog.TestDouble`, set only by a capturer
  that saw a double answer (`RequestResponseLog.cs:142-148`, `PARTY_IDENTITY_PLAN.md` §5.10), and a party every call
  to which a double answered is drawn as a double (`PartyResolver.cs`). 5.16.0's HTTP handler sets it from the handler
  at the end of its chain, when that is no transport: a stub, a fake, a mock library's handler
  (`AnsweringTestDouble`, `TestTrackingMessageHandler.cs:417-429`). The four handlers are given an inner handler too,
  so the same rule would mark a stubbed SDK's calls, and §5.2's fallback is a second sight: a call to Azurite's
  built-in account went to Azurite. Either is a feature, so a minor, after this plan; a root or gateway base proves
  nothing, so the Google pair could not say an emulator answered from the URL.

## 12. Log

- **2026-10-27**, written by session kronikol-5d at the owner's request to analyse #125 critically and plan a fix for
  the underlying problem. Probes against the published 5.12.0 packages, the real SDK clients and the real emulator
  (`ghcr.io/goccy/bigquery-emulator:0.8.1` in the podman VM). Scope split with kronikol-2d, who is planning #205 (the
  extension's SDK floor and the emulator's TIMESTAMP answers): this plan takes the classifiers, the `Summarised` skip
  and `BuildCleanUri`; #205's takes the package floor, the TIMESTAMP behaviour and DI. kronikol-2d's measurement (RUN
  by that session against the emulator 0.8.1 with a root `BaseUri`; `CLIENT_FLOORS_PLAN.harness/`,
  `results/emu0.8.1-client{3.0.0,3.11.0,3.12.0}-{plain,kronikol}.txt` and `results/matrix.md`, uncommitted on
  branch `plan/205-bigquery-floor` when this was written): Google.Cloud.BigQuery.V2
  3.11.0 runs `ExecuteQueryAsync` as `POST /projects/p/jobs` (`jobs.insert`) then `GET /projects/p/queries/{jobId}`,
  and 3.12.0 as one `POST /projects/p/queries` (`jobs.query`). 3.12.0 keeps the job path when the options rule the
  fast path out: `QueryOptions.Priority`, `JobId` or `JobIdPrefix`, `DestinationTable` and the other job-only
  settings, or a `GetQueryResultsOptions.StartIndex` or `PageToken`. On 5.12.0 every one of those requests was `Other`.
  So a query draws as two arrows on 3.11 (`Create /job`, `Query /query/{jobId}`) and one on 3.12; §5.1's rule reads
  both, and §6.1's facts take both paths.
- **2026-10-27, later.** kronikol-bf, planning #192 (`CALL_OPERATION_AND_ORIGIN_PLAN.md`), pointed out that §7
  accepted "behaviour changed" once per scenario, after the owner had asked that a change of name stop breaking
  history (that plan's §4.8). Checked against the source: §7's history paragraph and table, Q7, the Blob account-level
  line in §5.2 and the row access policy path in §5.1 follow from it. Two points of the message did not hold. The
  renamed part of a line is its `METHOD`, the label, not its `Service`. Cloud Storage's and Storage Queues' lines never
  carried a query string. Coordination: whichever of #192's R1 and this plan's releases moves `InteractionShape.Version`
  first takes the next number, claimed at push, and the other declares its translation from there. kronikol-bf then
  made §4.8's translation work per call line and partially. A line with no common form now costs only its own
  scenario's comparison: that scenario reads no behaviour verdict, not "changed". §7, Q7 and §6.3 follow that rule.
  That plan states it as §4.8 part 3, "Line by line, never 'changed'" (L716-720), and its fact
  `A_line_the_translation_cannot_reach_quiets_only_its_own_scenario` (L866) holds it. Its §4.8 was then restructured.
  Part 1 bridges a change of rule by having each run fingerprint its own raw calls under the earlier rule. Part 2
  translates the stored lines of a change like this one, whose new record keeps no raw URL, and cites §7's table.
  Part 4 records the tool version on every run line from its R1, so that a change shipped without a version move after
  R1 can still be found. A run before R1 carries no tool version. Its §8 has a row for this plan, and a bullet pointing
  at Q7 (L1015, L1023-1026).
- **2026-10-27, later still.** kronikol-67 released #190's R0 as 5.13.0 (`2b74a2e4`, published the
  same day). It builds §4.8 part 1 of #192's plan: the fingerprint rule moves to 7, rule 6 is kept as a
  function of the raw call, and the HTTP handler's change of name is bridged by an internal mark on the call that rule
  6 reads. That is the way §7 had set aside, when the bridge was still #192's to build, as costing more than it saved.
  With the bridge built, the mark costs only the frozen classifiers, reaches every row of §7's table, and frees R1 and
  R2 from #192, so §7 now describes it and Q7 offers it as (a), recommended. The old (b), a version move with no mark,
  is dropped: from 5.13.0 the bridge reads the new record under the kept rule, so it reads as (c). The translation of
  stored lines is now (b). §0, §6.3, §8 and §9 follow. Rule 7 is taken by 5.13.0; R1 and R2 take the next free
  numbers, claimed at push. kronikol-bf told.
- **2026-10-27, at 5.14.0.** kronikol-67 released #190's R1 as 5.14.0 (`8d7d1e1d`, tag `v5.14.0`). It writes the
  service a run was about on the run line, and changes nothing §7 relies on: the rule is still 7, with rule 6 kept,
  and the four extensions are untouched. Of the lines §7 cites in 5.13.0, only the bridging in `HistoryRunBuilder.cs`
  moved, from `:134-143` to `:137-146`.
- **2026-10-27, checked again at 5.14.0**, at the owner's request to make the plan complete and accurate. Every READ
  citation now points at 5.14.0; the four extensions and their tests are unchanged since `c63e35f3`, so every RUN line
  still holds. Corrected: F1 (the builder path could know the base; the wiki's recipe and the uploads cannot), F11
  (IPv4 only, and twenty path-style ports), §7 and Q7 (a release adds its rule to `KeptVersions`, which keeps every
  rule from 6 on, not only the one before; §6.3's "no new dependency" did not hold, since a bridge only renames a
  pair, so one true note is new once). Found: Cloud Storage's own `STORAGE_EMULATOR_HOST` support sets a root base, so
  F14's shape is the SDK's emulator setup, not the less common one (`probes/storage-emulator-env.cs`); F20, 5.14.0's
  declared parties read a dropped service as a wrong name; Q5's failed send moves the line's status at every
  verbosity, which the mark now covers; a gate that opted into `behaviour-change` fails once under Q7 (c). Added:
  §5.3's anchor, written out; #202's lines on the same wiki pages; #190's `testDouble` (§11). No recommendation
  changed.
- **2026-10-28, at 5.15.0.** kronikol-67 released #190's R2 as 5.15.0 (`6bbc2ce1`, tag `v5.15.0`). Of the files this
  plan cites it changes two: `RequestResponseLog.cs` gains `TestDouble` below the lines §7 cites, and
  `PartyResolver.cs` draws a party as a double when every call to it was so marked, which moves F20's lines to
  `:183-204` and leaves its finding as it was, names being matched against the calls' as before. The rule is still 7,
  and the four extensions are untouched. §11's `testDouble` item now cites what shipped.
- **2026-10-28, at 5.16.0.** 5.15.1 (`5ac5022f`) and 5.16.0, #190's R3 (`15a95d4c`, tag `v5.16.0`), change two files
  this plan cites: `RequestResponseLog.cs`, whose `TestDouble` is now `:142-148`, and `TestTrackingMessageHandler.cs`,
  whose failed send (Q5) is now `:299-328` and which now marks a call a stub at the end of its chain answered. §11
  takes that rule as the model for these handlers. The rule is still 7, and the four extensions are untouched.
- **2026-10-28, #205 green-lit.** kronikol-2d is building `CLIENT_FLOORS_PLAN.md` in full: its R1 (BigQuery's floor,
  3.0.0) ships alone, as this plan is not green-lit, and it takes `ROADMAP.md` row 1.34. Its suites stay on a float
  and also run at the floor (`-p:FloorRun=true`), so §6.1's facts assert how each request was named, not which
  requests a client sent, and §6.2's allow-list never requires a method. Its R2b floors the Azure packages, whose
  path-style rule depends on the version (August 2020's releases added the reserved ports); §5.2 now says what that
  leaves unread on an older client, and why the built-in account is read on any. Q2 records #205's answer. Its wiki
  notes land on the same pages as §7's, so §7 finds its lines by a grep.
- **2026-10-29, green-lit.** The owner asked for the plan to be implemented in full, and answered every question
  (§10): Q1 (a), Q2 two patches, Q3 no, Q4 its own issue (filed only once the owner says so), Q5 yes, Q6 as written,
  Q7 (a). Q7 was first put with #192 described as still to come; the owner pointed out that #192 had shipped, and it
  was put again with that corrected.
- **2026-10-29, R1 released as 5.27.6** (tag `v5.27.6`) by kronikol-5d, on 5.27.5, as §5.1, §5.3, Q1 (a) and Q5 say.
  What the plan did not say, or said otherwise:
  - The mark is `RecordedBefore9`, beside #192's `RecordedBefore8` and of its type (§7). A fragment's raw calls carry
    it (`method8`, `path8`, `absent8`, `unanswered8`), so `kronikol history record` bridges a run of the History
    action's flow as a run made in process is bridged.
  - The frozen copies are `BigQueryOperationClassifierBefore9.cs` and `CloudStorageOperationClassifierBefore9.cs`:
    5.27.2's classifier, label and `BuildCleanUri`, internal, 209 and 111 lines.
  - The record is the published 5.27.2's, the last package before the fix: 248 BigQuery and 104 Cloud Storage
    requests (`probes/before9-record-*.cs`), copied verbatim into each test project.
  - §6.3's second fact is `HistoryAcrossTheFixTests`, in each project, over a helper linked as `HistoryGuard` is
    (`tests/Kronikol.Tests/History/HistoryReplay.cs`). It found that the first run after the upgrade names a real
    change as 5.27.2 drew the call (`Read /sales/sales 404`), because the bridge compares it under rule 8. That holds
    at every bridged move, and the changelog says so.
  - The floors (#205) moved three facts. BigQuery 3.0.0's generated client declares `rowAccessPolicies.setIamPolicy`,
    which later clients dropped, so §6.2's map names it. Cloud Storage 4.0.0 has no
    `StorageClientBuilder.EmulatorDetection`, so §6.1 meets the `STORAGE_EMULATOR_HOST` base only where the client has
    it. `BigQueryClient.DeleteJob` and `datasets.undelete` are newer than 3.0.0, so the client fact sends the first
    through the generated method and calls the second by reflection where it is declared.
  - A failed send on a bucket whose name starts with `o` reads, under rule 8, as 5.27.2 misread its request
    (`ListObjects → orders`, F12), as a kept rule must.
  - §7's "no ledger line" was wrong: every .NET release has a line in Kronikol4J's divergence ledger, R1 included.
  - `InternalsVisibleTo`: 5.16.1 had granted the four packages the core's internals, so R1 added the test projects'
    grants only.
  - Proof (§6.4). Red first, on 5.27.2's code: BigQuery 72 of 74 new facts, Cloud Storage 65 of 114, each on its own
    assertion. Mutations: the collection anchor turns 1 fact red, the colon split 17, the query cut 2, the `o` segment
    54, the bucket anchor 62, and the marks 4 in each project. Live: `probes/bigquery-emulator-live-r1.cs` against the
    emulator 0.8.1 names and records 13 of 13 requests under both bases at both verbosities, and answers the
    upload's three failed `PUT`s.
  - Checks: the whole suite (22,598 passed, 66 skipped) before the rebase onto 5.27.5; after it, the core suite (11,538
    passed), HistoryJson's four mutations red, the suites 5.27.4 and 5.27.5 changed and R1's two on the newest client
    and on their floors, the Playwright suite (2,088 passed, 28 skipped), and the Release build of `release.slnf`. The
    changelog has the counts.
  - Left for the owner: Q4's issue (F17, the API key in the URL), not filed until the owner says so.
- **2026-10-29, R2 released as 5.27.9** (tag `v5.27.9`) by kronikol-5d, on 5.27.8, as §5.2, Q1 (a) and Q5 say.
  What the plan did not say, or said otherwise:
  - The mark is `RecordedBefore11`, and history's rule is 11: F26 took 10 (5.27.7, an Oracle call's address) while R2
    was built, and F30 took 5.27.8 (Kronikol.TUnit's `[HeadIn]`), each settled between the sessions before it pushed.
    Rule 10 reads a call from the mark, rule 9 from `RecordedBefore10` or it, rule 8 from `RecordedBefore9`,
    `RecordedBefore10` or it, and rules 6 and 7 with `RecordedBefore8` first, so a call marked by one release is read
    under every earlier rule. F26's rule 9 read every call, as 10 dropped none; R2's mark can say a call was not
    recorded, so rule 9 gains `absent9` and `unanswered9`, and a fragment carries rule 10's reading as `method10`,
    `path10`, `absent10` and `unanswered10`.
  - §5.2 said the container, the blob, the queue and the message come from the SDK's parsers. As built, the parser says
    only where the account is, asked with a URL of its own on the request's scheme, host and port
    (`/path-style/...`, which no account name can be), and the classifiers' patterns read the rest after it, as they
    did. Names taken from `BlobUriBuilder` are unescaped, so a production blob `2026%2F10%2Forder-1.json` would have
    been drawn `2026/10/order-1.json`, and the first draft's account test left out a container named like its account
    on Azure (`shop.blob.core.windows.net/shop/...`). A production account's calls are recorded as before, byte for
    byte, which a fact holds for every Azure row of the record.
  - F11 has a second half: on a host name with another port the SDK's own blob client drops the container.
    `GetBlobClient` on a container client builds the blob's URL with `BlobUriBuilder`, which reads Azurite's account as
    the container there, so the request is `/devstoreaccount1/{blob}` and no classifier can name the container. §6.1's
    fact that every connection reads as production holds for four of the six connections; for `localhost` and a
    Docker host's name on another port, the facts hold what the SDK sends, drawn as `Other` on a container named after
    the blob. Queue clients append to their URL, so Storage Queues reads all six as production.
  - The frozen copies are `BlobOperationClassifierBefore11.cs` and `StorageQueueOperationClassifierBefore11.cs`: the
    classifier, label and clean URI as 5.27.8 had them, internal, 136 and 81 lines.
  - The record is what the published 5.27.8 packages recorded: 156 Blob Storage and 138 Storage Queues requests
    (`probes/before-r2-record-*.cs`), copied verbatim into each test project.
  - Proof (§6.4). Red first, on the code 5.27.8 ships, its Blob classifier given only a stub of `ApiPathOf` that returns
    the path as sent: Blob Storage 37 of 47 new facts, Storage Queues 30 of 38, each on its own assertion. Mutations:
    the account read from the path turns 21 and 18 facts red, the built-in account's fallback 11 and 13, that fallback
    on any host 1 and 1, the request's mark 128 and 112, and the failed send's 1 and 1; each of HistoryJson's six new
    writes turns `HistoryRawCallsTests` red, and each of rules 6 to 10 ignoring the mark turns `ShapeGuardTests` red.
  - Checks: the whole suite on 5.27.7, before the rebase onto 5.27.8 (64 projects, 21,211 passed, 63 skipped, and the 21
    with a floor on their floors, 4,327 passed, 10 skipped); after it, the suites either release changed, each on the
    newest client and on its floor (Blob Storage 475, Storage Queues 431, TUnit 49), the LightBDD.TUnit suite (25), the
    three TUnit examples and the core suite (11,569 passed, 2 skipped); 21 mutations of the fix, each turning its facts
    red; the record probes run again against the published 5.27.8 packages, whose 156 and 138 rows are those 5.27.4 and
    5.27.7 recorded; the Playwright suite (2,087 passed, 28 skipped; the two facts that failed, a Back in WebKit that
    fails now and then and a toggle's timing under the load of other runs, pass alone with their classes, 38 of 38); and
    the Release build of `release.slnf`, whose only warnings are the advisories 5.27.5 lists.
  - Left for the owner: whether #125 closes now, and Q4's issue (F17), not filed until the owner says so.
