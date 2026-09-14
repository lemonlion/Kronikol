using Kronikol.Extensions.CosmosDB;

namespace Kronikol.Tests.CosmosDB;

public class CosmosOperationClassifierTests
{
    // ──────────────────────────────────────────────────────────
    //  Document CRUD operations (named paths)
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void Classify_PostToDocs_WithoutUpsertHeader_ReturnsCreate()
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            "https://account.documents.azure.com/dbs/mydb/colls/mycoll/docs");

        var result = CosmosOperationClassifier.Classify(request);

        Assert.Equal(CosmosOperation.Create, result.Operation);
        Assert.Equal("mydb", result.DatabaseName);
        Assert.Equal("mycoll", result.CollectionName);
        Assert.Null(result.DocumentId);
    }

    [Fact]
    public void Classify_PostToDocs_WithUpsertHeader_ReturnsUpsert()
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            "https://account.documents.azure.com/dbs/mydb/colls/mycoll/docs");
        request.Headers.Add("x-ms-documentdb-is-upsert", "true");

        var result = CosmosOperationClassifier.Classify(request);

        Assert.Equal(CosmosOperation.Upsert, result.Operation);
    }

    [Fact]
    public void Classify_PostToDocs_WithIsQueryHeader_ReturnsQuery()
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            "https://account.documents.azure.com/dbs/mydb/colls/mycoll/docs")
        {
            Content = new StringContent("""{"query": "SELECT * FROM c WHERE c.status = @status", "parameters": []}""")
        };
        request.Headers.Add("x-ms-documentdb-isquery", "True");

        var result = CosmosOperationClassifier.Classify(request);

        Assert.Equal(CosmosOperation.Query, result.Operation);
        Assert.Equal("SELECT * FROM c WHERE c.status = @status", result.QueryText);
    }

    [Fact]
    public void Classify_PostToDocs_WithQueryPlanHeader_IsNotACreate()
    {
        // Before running a query the SDK fetches its plan with a POST to the documents resource that
        // carries the query text but not the query header. Measured on a consumer's report, every Query
        // was preceded by a "Create" that never happened, and a scenario that only queried was shown
        // creating documents.
        var request = new HttpRequestMessage(HttpMethod.Post,
            "https://account.documents.azure.com/dbs/mydb/colls/mycoll/docs")
        {
            Content = new StringContent("""{"query": "SELECT VALUE root FROM root WHERE (root[\"status\"] = \"Pending\")", "parameters": []}""")
        };
        request.Headers.Add("x-ms-cosmos-is-query-plan-request", "True");
        request.Headers.Add("x-ms-cosmos-supported-query-features", "NonValueAggregate, Aggregate, Distinct, MultipleOrderBy, OffsetAndLimit, OrderBy, Top, CompositeAggregate, GroupBy, MultipleAggregates");

        var result = CosmosOperationClassifier.Classify(request);

        Assert.NotEqual(CosmosOperation.Create, result.Operation);
        Assert.Equal(CosmosOperation.Other, result.Operation);
        Assert.Equal("mycoll", result.CollectionName);
    }

    [Fact]
    public void Classify_GetDocById_ReturnsRead()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            "https://account.documents.azure.com/dbs/mydb/colls/mycoll/docs/doc123");

        var result = CosmosOperationClassifier.Classify(request);

        Assert.Equal(CosmosOperation.Read, result.Operation);
        Assert.Equal("doc123", result.DocumentId);
    }

    [Fact]
    public void Classify_GetDocs_ReturnsList()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            "https://account.documents.azure.com/dbs/mydb/colls/mycoll/docs");

        var result = CosmosOperationClassifier.Classify(request);

        Assert.Equal(CosmosOperation.List, result.Operation);
        Assert.Equal("mycoll", result.CollectionName);
        Assert.Null(result.DocumentId);
    }

    [Fact]
    public void Classify_PutDocById_ReturnsReplace()
    {
        var request = new HttpRequestMessage(HttpMethod.Put,
            "https://account.documents.azure.com/dbs/mydb/colls/mycoll/docs/doc123");

        var result = CosmosOperationClassifier.Classify(request);

        Assert.Equal(CosmosOperation.Replace, result.Operation);
        Assert.Equal("doc123", result.DocumentId);
    }

    [Fact]
    public void Classify_PatchDocById_ReturnsPatch()
    {
        var request = new HttpRequestMessage(HttpMethod.Patch,
            "https://account.documents.azure.com/dbs/mydb/colls/mycoll/docs/doc123");

        var result = CosmosOperationClassifier.Classify(request);

        Assert.Equal(CosmosOperation.Patch, result.Operation);
    }

    [Fact]
    public void Classify_DeleteDocById_ReturnsDelete()
    {
        var request = new HttpRequestMessage(HttpMethod.Delete,
            "https://account.documents.azure.com/dbs/mydb/colls/mycoll/docs/doc123");

        var result = CosmosOperationClassifier.Classify(request);

        Assert.Equal(CosmosOperation.Delete, result.Operation);
        Assert.Equal("doc123", result.DocumentId);
    }

    // ──────────────────────────────────────────────────────────
    //  _rid-encoded paths (as sent by the SDK internally)
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void Classify_PostToRidEncodedDocs_ReturnsCreate()
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            "https://account.documents.azure.com/dbs/Sl8fAA==/colls/Sl8fALN4sw4=/docs");

        var result = CosmosOperationClassifier.Classify(request);

        Assert.Equal(CosmosOperation.Create, result.Operation);
        Assert.Equal("Sl8fAA==", result.DatabaseName);
        Assert.Equal("Sl8fALN4sw4=", result.CollectionName);
    }

    [Fact]
    public void Classify_GetRidEncodedDocById_ReturnsRead()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            "https://account.documents.azure.com/dbs/Sl8fAA==/colls/Sl8fALN4sw4=/docs/Sl8fALN4sw4BAAAAAAAAAA==");

        var result = CosmosOperationClassifier.Classify(request);

        Assert.Equal(CosmosOperation.Read, result.Operation);
        Assert.Equal("Sl8fALN4sw4BAAAAAAAAAA==", result.DocumentId);
    }

    // ──────────────────────────────────────────────────────────
    //  Stored procedures
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void Classify_PostToSproc_ReturnsExecStoredProc()
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            "https://account.documents.azure.com/dbs/mydb/colls/mycoll/sprocs/mysproc");

        var result = CosmosOperationClassifier.Classify(request);

        Assert.Equal(CosmosOperation.ExecStoredProc, result.Operation);
        Assert.Equal("mysproc", result.DocumentId);
    }

    // ──────────────────────────────────────────────────────────
    //  Non-document resource paths → Other
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void Classify_GetDatabase_ReturnsOther()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            "https://account.documents.azure.com/dbs/mydb");

        var result = CosmosOperationClassifier.Classify(request);

        Assert.Equal(CosmosOperation.Other, result.Operation);
    }

    [Fact]
    public void Classify_GetCollection_ReturnsOther()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            "https://account.documents.azure.com/dbs/mydb/colls/mycoll");

        var result = CosmosOperationClassifier.Classify(request);

        Assert.Equal(CosmosOperation.Other, result.Operation);
    }

    [Fact]
    public void Classify_GetPkRanges_ReturnsOther()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            "https://account.documents.azure.com/dbs/mydb/colls/mycoll/pkranges");

        var result = CosmosOperationClassifier.Classify(request);

        Assert.Equal(CosmosOperation.Other, result.Operation);
    }

    // ──────────────────────────────────────────────────────────
    //  Query text extraction
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void Classify_Query_WithNoBody_HasNullQueryText()
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            "https://account.documents.azure.com/dbs/mydb/colls/mycoll/docs");
        request.Headers.Add("x-ms-documentdb-isquery", "True");

        var result = CosmosOperationClassifier.Classify(request);

        Assert.Equal(CosmosOperation.Query, result.Operation);
        Assert.Null(result.QueryText);
    }

    [Fact]
    public void Classify_Query_WithMalformedJson_HasNullQueryText()
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            "https://account.documents.azure.com/dbs/mydb/colls/mycoll/docs")
        {
            Content = new StringContent("not json at all")
        };
        request.Headers.Add("x-ms-documentdb-isquery", "True");

        var result = CosmosOperationClassifier.Classify(request);

        Assert.Equal(CosmosOperation.Query, result.Operation);
        Assert.Null(result.QueryText);
    }

    [Fact]
    public void Classify_Query_WithJsonMissingQueryField_HasNullQueryText()
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            "https://account.documents.azure.com/dbs/mydb/colls/mycoll/docs")
        {
            Content = new StringContent("""{"parameters": []}""")
        };
        request.Headers.Add("x-ms-documentdb-isquery", "True");

        var result = CosmosOperationClassifier.Classify(request);

        Assert.Null(result.QueryText);
    }

    // ──────────────────────────────────────────────────────────
    //  Header case insensitivity
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void Classify_UpsertHeader_CaseInsensitive()
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            "https://account.documents.azure.com/dbs/mydb/colls/mycoll/docs");
        request.Headers.Add("x-ms-documentdb-is-upsert", "True"); // capital T

        var result = CosmosOperationClassifier.Classify(request);

        Assert.Equal(CosmosOperation.Upsert, result.Operation);
    }

    // ──────────────────────────────────────────────────────────
    //  Query takes precedence over upsert when both headers present
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void Classify_BothUpsertAndQueryHeaders_ReturnsQuery()
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            "https://account.documents.azure.com/dbs/mydb/colls/mycoll/docs");
        request.Headers.Add("x-ms-documentdb-is-upsert", "true");
        request.Headers.Add("x-ms-documentdb-isquery", "True");

        var result = CosmosOperationClassifier.Classify(request);

        // Query is matched first in the switch because isQuery=true takes priority
        Assert.Equal(CosmosOperation.Query, result.Operation);
    }
}
