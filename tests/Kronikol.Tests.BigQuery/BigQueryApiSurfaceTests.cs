using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Google.Apis.Bigquery.v2;
using Google.Apis.Requests;
using Kronikol.Extensions.BigQuery;

namespace Kronikol.Tests.BigQuery;

/// <summary>
/// Every method the generated BigQuery client declares, read by reflection from each request class's <c>RestPath</c> and
/// <c>HttpMethod</c>, filled with sample values and classified under Google's base, an emulator's root and a gateway's
/// prefix (#125, plans/EMULATOR_URL_SHAPES_PLAN.md section 6.2). Each is named as <see cref="Expected"/> says under
/// every base. A method the map does not name, which a new SDK version may add, fails the fact until someone decides
/// what it is. The suite also runs on the package's floor (<c>-p:FloorRun=true</c>), so the map names the methods of
/// both clients: a method one of them lacks is simply not met.
/// </summary>
public class BigQueryApiSurfaceTests
{
    private static readonly string[] Bases = ["https://bigquery.googleapis.com/bigquery/v2/", "http://127.0.0.1:9050/", "http://gateway.local/bq/"];

    private static readonly Dictionary<string, (BigQueryOperation Operation, string ResourceType)> Expected = new()
    {
        ["ProjectsResource.ListRequest"] = (BigQueryOperation.List, "project"),
        ["ProjectsResource.GetServiceAccountRequest"] = (BigQueryOperation.Read, "serviceAccount"),
        ["DatasetsResource.ListRequest"] = (BigQueryOperation.List, "dataset"),
        ["DatasetsResource.InsertRequest"] = (BigQueryOperation.Create, "dataset"),
        ["DatasetsResource.GetRequest"] = (BigQueryOperation.Read, "dataset"),
        ["DatasetsResource.PatchRequest"] = (BigQueryOperation.Update, "dataset"),
        ["DatasetsResource.UpdateRequest"] = (BigQueryOperation.Update, "dataset"),
        ["DatasetsResource.DeleteRequest"] = (BigQueryOperation.Delete, "dataset"),
        ["DatasetsResource.UndeleteRequest"] = (BigQueryOperation.Create, "dataset"),
        ["TablesResource.ListRequest"] = (BigQueryOperation.List, "table"),
        ["TablesResource.InsertRequest"] = (BigQueryOperation.Create, "table"),
        ["TablesResource.GetRequest"] = (BigQueryOperation.Read, "table"),
        ["TablesResource.PatchRequest"] = (BigQueryOperation.Update, "table"),
        ["TablesResource.UpdateRequest"] = (BigQueryOperation.Update, "table"),
        ["TablesResource.DeleteRequest"] = (BigQueryOperation.Delete, "table"),
        ["TablesResource.GetIamPolicyRequest"] = (BigQueryOperation.Read, "iamPolicy"),
        ["TablesResource.SetIamPolicyRequest"] = (BigQueryOperation.Update, "iamPolicy"),
        ["TablesResource.TestIamPermissionsRequest"] = (BigQueryOperation.Read, "iamPolicy"),
        ["TabledataResource.ListRequest"] = (BigQueryOperation.List, "tabledata"),
        ["TabledataResource.InsertAllRequest"] = (BigQueryOperation.Insert, "table"),
        ["ModelsResource.ListRequest"] = (BigQueryOperation.List, "model"),
        ["ModelsResource.GetRequest"] = (BigQueryOperation.Read, "model"),
        ["ModelsResource.PatchRequest"] = (BigQueryOperation.Update, "model"),
        ["ModelsResource.DeleteRequest"] = (BigQueryOperation.Delete, "model"),
        ["RoutinesResource.ListRequest"] = (BigQueryOperation.List, "routine"),
        ["RoutinesResource.InsertRequest"] = (BigQueryOperation.Create, "routine"),
        ["RoutinesResource.GetRequest"] = (BigQueryOperation.Read, "routine"),
        ["RoutinesResource.UpdateRequest"] = (BigQueryOperation.Update, "routine"),
        ["RoutinesResource.DeleteRequest"] = (BigQueryOperation.Delete, "routine"),
        ["RoutinesResource.GetIamPolicyRequest"] = (BigQueryOperation.Read, "iamPolicy"),
        ["RoutinesResource.SetIamPolicyRequest"] = (BigQueryOperation.Update, "iamPolicy"),
        ["RoutinesResource.TestIamPermissionsRequest"] = (BigQueryOperation.Read, "iamPolicy"),
        ["RowAccessPoliciesResource.ListRequest"] = (BigQueryOperation.List, "rowAccessPolicy"),
        ["RowAccessPoliciesResource.InsertRequest"] = (BigQueryOperation.Create, "rowAccessPolicy"),
        ["RowAccessPoliciesResource.BatchDeleteRequest"] = (BigQueryOperation.Delete, "rowAccessPolicy"),
        ["RowAccessPoliciesResource.GetRequest"] = (BigQueryOperation.Read, "rowAccessPolicy"),
        ["RowAccessPoliciesResource.UpdateRequest"] = (BigQueryOperation.Update, "rowAccessPolicy"),
        ["RowAccessPoliciesResource.DeleteRequest"] = (BigQueryOperation.Delete, "rowAccessPolicy"),
        ["RowAccessPoliciesResource.GetIamPolicyRequest"] = (BigQueryOperation.Read, "iamPolicy"),
        ["RowAccessPoliciesResource.TestIamPermissionsRequest"] = (BigQueryOperation.Read, "iamPolicy"),
        // Declared by the floor's generated client (Google.Apis.Bigquery.v2 1.57), not by the newest.
        ["RowAccessPoliciesResource.SetIamPolicyRequest"] = (BigQueryOperation.Update, "iamPolicy"),
        ["JobsResource.ListRequest"] = (BigQueryOperation.List, "job"),
        ["JobsResource.InsertRequest"] = (BigQueryOperation.Create, "job"),
        ["JobsResource.GetRequest"] = (BigQueryOperation.Read, "job"),
        ["JobsResource.CancelRequest"] = (BigQueryOperation.Cancel, "job"),
        ["JobsResource.DeleteRequest"] = (BigQueryOperation.Delete, "job"),
        ["JobsResource.QueryRequest"] = (BigQueryOperation.Query, "query"),
        ["JobsResource.GetQueryResultsRequest"] = (BigQueryOperation.Query, "query"),
    };

    public static TheoryData<string, string, string> Methods()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var type in typeof(BigqueryService).Assembly.GetTypes()
                     .Where(t => !t.IsAbstract && !t.ContainsGenericParameters && typeof(IClientServiceRequest).IsAssignableFrom(t)))
        {
            var request = (IClientServiceRequest)RuntimeHelpers.GetUninitializedObject(type);
            data.Add(Name(type), request.HttpMethod, request.RestPath);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Methods))]
    public void Every_method_is_named_as_the_map_says_under_every_base(string name, string method, string restPath)
    {
        Assert.True(Expected.TryGetValue(name, out var expected),
            $"{name} ({method} {restPath}) is a method the map does not name: decide what it is and add it to Expected.");

        foreach (var root in Bases)
        {
            var result = BigQueryOperationClassifier.Classify(new HttpRequestMessage(new HttpMethod(method), root + Fill(restPath, name)));
            Assert.Equal((expected.Operation, expected.ResourceType), (result.Operation, result.ResourceType));
        }
    }

    private static string Name(Type type) => type.FullName!.Replace("Google.Apis.Bigquery.v2.", "").Replace("+", ".");

    // Sample values; a reserved expansion ({+resource}) holds the full name of what the IAM method is on.
    private static string Fill(string template, string name) => Regex.Replace(template, @"\{\+?(?<n>[^}]+)\}", m => m.Groups["n"].Value switch
    {
        "projectId" => "shop",
        "datasetId" => "sales",
        "tableId" => "orders",
        "jobId" => "job_1",
        "modelId" => "m1",
        "routineId" => "r1",
        "policyId" => "p1",
        "resource" when name.StartsWith("Routines", StringComparison.Ordinal) => "projects/shop/datasets/sales/routines/r1",
        "resource" when name.StartsWith("RowAccessPolicies", StringComparison.Ordinal) => "projects/shop/datasets/sales/tables/orders/rowAccessPolicies/p1",
        "resource" => "projects/shop/datasets/sales/tables/orders",
        var other => other.ToLowerInvariant(),
    });
}
