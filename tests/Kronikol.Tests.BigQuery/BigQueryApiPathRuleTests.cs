using Kronikol.Extensions.BigQuery;

namespace Kronikol.Tests.BigQuery;

/// <summary>
/// The classifier finds where the API's own path starts, whatever base the client was built with (#125,
/// plans/EMULATOR_URL_SHAPES_PLAN.md section 5.1), and names the methods it used to miss or misread on Google's own
/// endpoint (F4). The URLs are the ones the generated client's <c>RestPath</c>s make, under each base.
/// </summary>
public class BigQueryApiPathRuleTests
{
    private static BigQueryOperationInfo Classify(string method, string url) =>
        BigQueryOperationClassifier.Classify(new HttpRequestMessage(new HttpMethod(method), url));

    [Theory]
    [InlineData("https://bigquery.googleapis.com/bigquery/v2/projects/shop/queries")]
    [InlineData("http://127.0.0.1:9050/projects/shop/queries")]
    [InlineData("http://gateway.local/bq/projects/shop/queries")]
    public void A_query_is_read_under_any_base(string url)
    {
        var result = Classify("POST", url);

        Assert.Equal(BigQueryOperation.Query, result.Operation);
        Assert.Equal("shop", result.ProjectId);
    }

    [Fact]
    public void An_upload_is_read_from_the_hosts_root_whatever_the_base()
    {
        var result = Classify("POST", "http://gateway.local/upload/bigquery/v2/projects/shop/jobs?uploadType=resumable");

        Assert.Equal((BigQueryOperation.Create, "job"), (result.Operation, result.ResourceType));
    }

    [Fact]
    public void A_dataset_named_projects_is_a_dataset()
    {
        var result = Classify("GET", "http://127.0.0.1:9050/projects/shop/datasets/projects/tables/t");

        Assert.Equal((BigQueryOperation.Read, "table", "t", "projects"), (result.Operation, result.ResourceType, result.ResourceName, result.DatasetId));
    }

    [Fact]
    public void A_base_path_holding_a_projects_segment_is_passed_over()
    {
        var result = Classify("POST", "http://gateway.local/projects/bq/projects/shop/datasets");

        Assert.Equal((BigQueryOperation.Create, "dataset", "shop"), (result.Operation, result.ResourceType, result.ProjectId));
    }

    [Fact]
    public void The_project_list_is_a_list_of_projects()
    {
        var result = Classify("GET", "https://bigquery.googleapis.com/bigquery/v2/projects?prettyPrint=false");

        Assert.Equal((BigQueryOperation.List, "project"), (result.Operation, result.ResourceType));
    }

    [Fact]
    public void The_service_account_is_read()
    {
        var result = Classify("GET", "https://bigquery.googleapis.com/bigquery/v2/projects/shop/serviceAccount");

        Assert.Equal((BigQueryOperation.Read, "serviceAccount"), (result.Operation, result.ResourceType));
    }

    [Theory]
    [InlineData("tables/orders:getIamPolicy", "Read", "table/orders")]
    [InlineData("tables/orders:testIamPermissions", "Read", "table/orders")]
    [InlineData("tables/orders:setIamPolicy", "Update", "table/orders")]
    [InlineData("routines/r1:getIamPolicy", "Read", "routine/r1")]
    [InlineData("tables/orders/rowAccessPolicies/p1:setIamPolicy", "Update", "table/orders/rowAccessPolicy/p1")]
    public void An_IAM_method_reads_or_replaces_the_policy_on_its_resource(string path, string operation, string parent)
    {
        var result = Classify("POST", $"https://bigquery.googleapis.com/bigquery/v2/projects/shop/datasets/sales/{path}");

        Assert.Equal(Enum.Parse<BigQueryOperation>(operation), result.Operation);
        Assert.Equal("iamPolicy", result.ResourceType);
        Assert.Equal(parent, result.Parent);
        Assert.Null(result.ResourceName);
    }

    [Fact]
    public void Undeleting_a_dataset_brings_it_back()
    {
        var result = Classify("POST", "https://bigquery.googleapis.com/bigquery/v2/projects/shop/datasets/sales:undelete");

        Assert.Equal((BigQueryOperation.Create, "dataset", "sales"), (result.Operation, result.ResourceType, result.ResourceName));
    }

    [Theory]
    [InlineData("GET", "rowAccessPolicies", "List", null)]
    [InlineData("POST", "rowAccessPolicies", "Create", null)]
    [InlineData("POST", "rowAccessPolicies:batchDelete", "Delete", null)]
    [InlineData("GET", "rowAccessPolicies/p1", "Read", "p1")]
    [InlineData("PUT", "rowAccessPolicies/p1", "Update", "p1")]
    [InlineData("DELETE", "rowAccessPolicies/p1", "Delete", "p1")]
    public void A_row_access_policy_is_named_for_itself_on_its_table(string method, string path, string operation, string? name)
    {
        var result = Classify(method, $"https://bigquery.googleapis.com/bigquery/v2/projects/shop/datasets/sales/tables/orders/{path}");

        Assert.Equal(Enum.Parse<BigQueryOperation>(operation), result.Operation);
        Assert.Equal(("rowAccessPolicy", name, "table/orders"), (result.ResourceType, result.ResourceName, result.Parent));
    }

    [Fact]
    public void A_load_jobs_data_is_an_insert_into_the_job()
    {
        var result = Classify("PUT", "http://127.0.0.1:9050/upload/bigquery/v2/projects/shop/jobs?uploadType=resumable&upload_id=u1");

        Assert.Equal((BigQueryOperation.Insert, "job"), (result.Operation, result.ResourceType));
    }

    [Fact]
    public void A_path_no_rule_finds_is_Other_and_is_drawn_with_the_path_as_sent()
    {
        var result = Classify("GET", "https://bigquery.googleapis.com/discovery/v1/apis?key=k");

        Assert.Equal(BigQueryOperation.Other, result.Operation);
        Assert.Equal("/discovery/v1/apis", BigQueryOperationClassifier.ApiPathOf(new Uri("https://bigquery.googleapis.com/discovery/v1/apis?key=k")));
        Assert.Equal("/projects/shop/unknown", BigQueryOperationClassifier.ApiPathOf(new Uri("http://gateway.local/bq/projects/shop/unknown?key=k")));
    }
}
