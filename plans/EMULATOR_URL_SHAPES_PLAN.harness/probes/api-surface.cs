#:package Kronikol.Extensions.BigQuery@5.12.0
#:package Kronikol.Extensions.CloudStorage@5.12.0
#:package Google.Cloud.BigQuery.V2@3.12.0
#:package Google.Cloud.Storage.V1@4.16.0
#:property TargetFramework=net10.0
#:property PublishAot=false

// Every method of the BigQuery v2 and Cloud Storage v1 REST APIs, as the installed generated clients
// (Google.Apis.Bigquery.v2, Google.Apis.Storage.v1) declare them: each request class carries its RestPath template
// and HttpMethod. The template is filled with sample values, put under Google's own base path and under an emulator
// root, and classified by the shipped 5.12.0 classifiers. A method the classifier misses is drawn as Other at
// Detailed and dropped at Summarised.
// Usage: dotnet run --file api-surface.cs

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Google.Apis.Requests;
using Kronikol.Extensions.BigQuery;
using Kronikol.Extensions.CloudStorage;

Survey("BigQuery v2", typeof(Google.Apis.Bigquery.v2.BigqueryService).Assembly, "https://bigquery.googleapis.com/bigquery/v2/", "http://127.0.0.1:9050/",
    (m, u) => BigQueryOperationClassifier.Classify(new HttpRequestMessage(m, u)) is var i ? ($"{i.Operation} {i.ResourceType ?? "-"}/{i.ResourceName ?? "-"}", i.Operation == BigQueryOperation.Other) : default);
Survey("Cloud Storage v1", typeof(Google.Apis.Storage.v1.StorageService).Assembly, "https://storage.googleapis.com/storage/v1/", "http://127.0.0.1:4443/",
    (m, u) => CloudStorageOperationClassifier.Classify(new HttpRequestMessage(m, u)) is var i ? ($"{i.Operation} {i.BucketName ?? "-"}/{i.ObjectName ?? "-"}", i.Operation == CloudStorageOperation.Other) : default);

static void Survey(string api, Assembly assembly, string googleBase, string emulatorBase, Func<HttpMethod, Uri, (string Text, bool IsOther)> classify)
{
    var requests = assembly.GetTypes()
        .Where(t => !t.IsAbstract && !t.ContainsGenericParameters && typeof(IClientServiceRequest).IsAssignableFrom(t))
        .Select(t => (Type: t, Request: (IClientServiceRequest)RuntimeHelpers.GetUninitializedObject(t)))
        .Select(x => (Name: x.Type.FullName!.Replace("Google.Apis.", "").Replace("+", "."), Method: x.Request.HttpMethod, Path: x.Request.RestPath))
        .OrderBy(x => x.Path).ThenBy(x => x.Method)
        .ToList();
    var otherAtGoogle = 0;
    var otherAtEmulator = 0;
    Console.WriteLine($"=== {api}: {requests.Count} methods");
    foreach (var (name, method, path) in requests)
    {
        var filled = Fill(path);
        var relative = filled.StartsWith('/') ? filled : null; // a path that starts with '/' is from the host root (uploads)
        var atGoogle = new Uri(relative is null ? googleBase + filled : new Uri(googleBase).GetLeftPart(UriPartial.Authority) + relative);
        var atEmulator = new Uri(relative is null ? emulatorBase + filled : new Uri(emulatorBase).GetLeftPart(UriPartial.Authority) + relative);
        var g = classify(new HttpMethod(method), atGoogle);
        var e = classify(new HttpMethod(method), atEmulator);
        if (g.IsOther) otherAtGoogle++;
        if (e.IsOther) otherAtEmulator++;
        Console.WriteLine($"  {method,-6} {path,-78} google: {g.Text,-38} emulator root: {e.Text,-30} {name}");
    }
    Console.WriteLine($"  Other at Google's endpoint: {otherAtGoogle} of {requests.Count}; at an emulator root: {otherAtEmulator} of {requests.Count}");
}

// Sample values: a reserved expansion ({+x}) may hold slashes, so a resource name gets a full table path.
static string Fill(string template) => Regex.Replace(template, @"\{\+?(?<n>[^}]+)\}", m => m.Groups["n"].Value switch
{
    "projectId" => "shop",
    "datasetId" => "sales",
    "tableId" => "orders",
    "jobId" => "job_1",
    "modelId" => "m1",
    "routineId" => "r1",
    "policyId" => "p1",
    "resource" => "projects/shop/datasets/sales/tables/orders",
    "bucket" => Environment.GetEnvironmentVariable("BUCKET") ?? "orders",
    "object" => "order-1.json",
    "sourceBucket" => "orders",
    "sourceObject" => "order-1.json",
    "destinationBucket" => "archive",
    "destinationObject" => "order-1.json",
    "entity" => "allUsers",
    var other => other.ToLowerInvariant().Replace(".", "-"),
});
