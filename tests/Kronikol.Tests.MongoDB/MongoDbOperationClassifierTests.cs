using MongoDB.Bson;
using Kronikol.Extensions.MongoDB;

namespace Kronikol.Tests.MongoDB;

public class MongoDbOperationClassifierTests
{
    // ─── Command name mapping ────────────────────────────────

    [Theory]
    [InlineData("find", MongoDbOperation.Find)]
    [InlineData("insert", MongoDbOperation.Insert)]
    [InlineData("update", MongoDbOperation.Update)]
    [InlineData("delete", MongoDbOperation.Delete)]
    [InlineData("aggregate", MongoDbOperation.Aggregate)]
    [InlineData("count", MongoDbOperation.Count)]
    [InlineData("countDocuments", MongoDbOperation.Count)]
    [InlineData("findAndModify", MongoDbOperation.FindAndModify)]
    [InlineData("distinct", MongoDbOperation.Distinct)]
    [InlineData("bulkWrite", MongoDbOperation.BulkWrite)]
    [InlineData("createIndexes", MongoDbOperation.CreateIndex)]
    [InlineData("dropIndexes", MongoDbOperation.DropIndex)]
    [InlineData("create", MongoDbOperation.CreateCollection)]
    [InlineData("drop", MongoDbOperation.DropCollection)]
    [InlineData("listCollections", MongoDbOperation.ListCollections)]
    [InlineData("listDatabases", MongoDbOperation.ListDatabases)]
    [InlineData("getMore", MongoDbOperation.GetMore)]
    [InlineData("mapReduce", MongoDbOperation.MapReduce)]
    [InlineData("commitTransaction", MongoDbOperation.CommitTransaction)]
    [InlineData("abortTransaction", MongoDbOperation.AbortTransaction)]
    [InlineData("dropDatabase", MongoDbOperation.DropDatabase)]
    [InlineData("renameCollection", MongoDbOperation.RenameCollection)]
    [InlineData("listIndexes", MongoDbOperation.ListIndexes)]
    [InlineData("serverStatus", MongoDbOperation.ServerStatus)]
    [InlineData("dbStats", MongoDbOperation.DbStats)]
    [InlineData("collStats", MongoDbOperation.CollStats)]
    public void Classify_MapsCommandNameToCorrectOperation(string commandName, MongoDbOperation expected)
    {
        var command = new BsonDocument(commandName, "testcollection");

        var result = MongoDbOperationClassifier.Classify(commandName, "testdb", command);

        Assert.Equal(expected, result.Operation);
    }

    [Fact]
    public void Classify_UnknownCommand_ReturnsOther()
    {
        var result = MongoDbOperationClassifier.Classify("unknownCommand", "testdb", null);

        Assert.Equal(MongoDbOperation.Other, result.Operation);
    }

    [Theory]
    [InlineData("Find")]
    [InlineData("FIND")]
    [InlineData("fInD")]
    public void Classify_CaseInsensitiveCommandName(string commandName)
    {
        var command = new BsonDocument(commandName, "users");

        var result = MongoDbOperationClassifier.Classify(commandName, "testdb", command);

        Assert.Equal(MongoDbOperation.Find, result.Operation);
    }

    // ─── Collection name extraction ──────────────────────────

    [Fact]
    public void Classify_ExtractsCollectionNameFromFindCommand()
    {
        var command = new BsonDocument { { "find", "users" }, { "filter", new BsonDocument() } };

        var result = MongoDbOperationClassifier.Classify("find", "mydb", command);

        Assert.Equal("users", result.CollectionName);
    }

    [Fact]
    public void Classify_ExtractsCollectionNameFromInsertCommand()
    {
        var command = new BsonDocument { { "insert", "orders" }, { "documents", new BsonArray() } };

        var result = MongoDbOperationClassifier.Classify("insert", "mydb", command);

        Assert.Equal("orders", result.CollectionName);
    }

    [Fact]
    public void Classify_ExtractsCollectionNameFromAggregateCommand()
    {
        var command = new BsonDocument { { "aggregate", "products" }, { "pipeline", new BsonArray() } };

        var result = MongoDbOperationClassifier.Classify("aggregate", "mydb", command);

        Assert.Equal("products", result.CollectionName);
    }

    [Fact]
    public void Classify_DatabaseLevelAggregate_CollectionIsNull()
    {
        var command = new BsonDocument { { "aggregate", 1 }, { "pipeline", new BsonArray() } };

        var result = MongoDbOperationClassifier.Classify("aggregate", "mydb", command);

        Assert.Null(result.CollectionName);
    }

    [Fact]
    public void Classify_NullCommand_CollectionIsNull()
    {
        var result = MongoDbOperationClassifier.Classify("find", "mydb", null);

        Assert.Null(result.CollectionName);
    }

    [Fact]
    public void Classify_IncludesDatabaseName()
    {
        var result = MongoDbOperationClassifier.Classify("find", "myapp", new BsonDocument("find", "users"));

        Assert.Equal("myapp", result.DatabaseName);
    }

    // ─── Filter extraction ───────────────────────────────────

    [Fact]
    public void Classify_ExtractsFilterFromCommand()
    {
        var filter = new BsonDocument("age", new BsonDocument("$gt", 25));
        var command = new BsonDocument { { "find", "users" }, { "filter", filter } };

        var result = MongoDbOperationClassifier.Classify("find", "mydb", command);

        Assert.NotNull(result.FilterText);
        Assert.Contains("age", result.FilterText);
    }

    [Fact]
    public void Classify_NoFilter_FilterTextIsNull()
    {
        var command = new BsonDocument { { "insert", "orders" }, { "documents", new BsonArray() } };

        var result = MongoDbOperationClassifier.Classify("insert", "mydb", command);

        Assert.Null(result.FilterText);
    }

    [Fact]
    public void Classify_EmptyFilter_FilterTextNotNull()
    {
        var command = new BsonDocument { { "find", "users" }, { "filter", new BsonDocument() } };

        var result = MongoDbOperationClassifier.Classify("find", "mydb", command);

        Assert.NotNull(result.FilterText);
    }

    // ─── Diagram labels ─────────────────────────────────────

    [Fact]
    public void GetDiagramLabel_Raw_IncludesDatabaseAndCollectionAndFilter()
    {
        var info = new MongoDbOperationInfo(MongoDbOperation.Find, "mydb", "users", "{ age: { $gt: 25 } }");

        var label = MongoDbOperationClassifier.GetDiagramLabel(info, MongoDbTrackingVerbosity.Raw);

        Assert.Contains("Find", label);
        Assert.Contains("mydb", label);
        Assert.Contains("users", label);
        Assert.Contains("filter=", label);
    }

    [Fact]
    public void GetDiagramLabel_Detailed_ShowsOperationAndCollection()
    {
        var info = new MongoDbOperationInfo(MongoDbOperation.Find, "mydb", "users");

        var label = MongoDbOperationClassifier.GetDiagramLabel(info, MongoDbTrackingVerbosity.Detailed);

        Assert.Equal("Find ← users", label);
    }

    [Fact]
    public void GetDiagramLabel_Detailed_NoCollection_ShowsOperationOnly()
    {
        var info = new MongoDbOperationInfo(MongoDbOperation.ListDatabases, "mydb", null);

        var label = MongoDbOperationClassifier.GetDiagramLabel(info, MongoDbTrackingVerbosity.Detailed);

        Assert.Equal("ListDatabases", label);
    }

    [Fact]
    public void GetDiagramLabel_Summarised_ShowsOperationOnly()
    {
        var info = new MongoDbOperationInfo(MongoDbOperation.Insert, "mydb", "orders");

        var label = MongoDbOperationClassifier.GetDiagramLabel(info, MongoDbTrackingVerbosity.Summarised);

        Assert.Equal("Insert", label);
    }

    [Fact]
    public void GetDiagramLabel_Raw_NoFilter_OmitsFilterPrefix()
    {
        var info = new MongoDbOperationInfo(MongoDbOperation.Insert, "mydb", "orders");

        var label = MongoDbOperationClassifier.GetDiagramLabel(info, MongoDbTrackingVerbosity.Raw);

        Assert.DoesNotContain("filter=", label);
    }

    // ─── Change stream detection ─────────────────────────────

    [Fact]
    public void Classify_AggregateWithChangeStreamPipeline_ReturnsWatch()
    {
        var pipeline = new BsonArray { new BsonDocument("$changeStream", new BsonDocument()) };
        var command = new BsonDocument { { "aggregate", "orders" }, { "pipeline", pipeline } };

        var result = MongoDbOperationClassifier.Classify("aggregate", "mydb", command);

        Assert.Equal(MongoDbOperation.Watch, result.Operation);
        Assert.Equal("orders", result.CollectionName);
    }

    [Fact]
    public void Classify_AggregateWithoutChangeStreamPipeline_ReturnsAggregate()
    {
        var pipeline = new BsonArray { new BsonDocument("$match", new BsonDocument("status", "active")) };
        var command = new BsonDocument { { "aggregate", "orders" }, { "pipeline", pipeline } };

        var result = MongoDbOperationClassifier.Classify("aggregate", "mydb", command);

        Assert.Equal(MongoDbOperation.Aggregate, result.Operation);
    }

    [Fact]
    public void Classify_AggregateWithEmptyPipeline_ReturnsAggregate()
    {
        var command = new BsonDocument { { "aggregate", "orders" }, { "pipeline", new BsonArray() } };

        var result = MongoDbOperationClassifier.Classify("aggregate", "mydb", command);

        Assert.Equal(MongoDbOperation.Aggregate, result.Operation);
    }

    [Fact]
    public void Classify_AggregateWithNoPipeline_ReturnsAggregate()
    {
        var command = new BsonDocument { { "aggregate", "orders" } };

        var result = MongoDbOperationClassifier.Classify("aggregate", "mydb", command);

        Assert.Equal(MongoDbOperation.Aggregate, result.Operation);
    }

    // ─── Directional arrows in Detailed mode ─────────────────

    [Theory]
    [InlineData(MongoDbOperation.Find, "users", "Find ← users")]
    [InlineData(MongoDbOperation.Aggregate, "orders", "Aggregate ← orders")]
    [InlineData(MongoDbOperation.Watch, "orders", "Watch ← orders")]
    [InlineData(MongoDbOperation.Count, "users", "Count ← users")]
    [InlineData(MongoDbOperation.Distinct, "users", "Distinct ← users")]
    [InlineData(MongoDbOperation.GetMore, "users", "GetMore ← users")]
    [InlineData(MongoDbOperation.MapReduce, "orders", "MapReduce ← orders")]
    [InlineData(MongoDbOperation.Insert, "users", "Insert → users")]
    [InlineData(MongoDbOperation.Update, "users", "Update → users")]
    [InlineData(MongoDbOperation.Delete, "users", "Delete → users")]
    [InlineData(MongoDbOperation.BulkWrite, "users", "BulkWrite → users")]
    [InlineData(MongoDbOperation.FindAndModify, "users", "FindAndModify ↔ users")]
    public void GetDiagramLabel_Detailed_ShowsDirectionalArrow(
        MongoDbOperation op, string collection, string expectedLabel)
    {
        var info = new MongoDbOperationInfo(op, "mydb", collection);

        var label = MongoDbOperationClassifier.GetDiagramLabel(info, MongoDbTrackingVerbosity.Detailed);

        Assert.Equal(expectedLabel, label);
    }

    [Theory]
    [InlineData(MongoDbOperation.ListCollections)]
    [InlineData(MongoDbOperation.ListDatabases)]
    [InlineData(MongoDbOperation.ServerStatus)]
    [InlineData(MongoDbOperation.DbStats)]
    [InlineData(MongoDbOperation.CollStats)]
    [InlineData(MongoDbOperation.CommitTransaction)]
    [InlineData(MongoDbOperation.AbortTransaction)]
    public void GetDiagramLabel_Detailed_AdminOps_NoArrow(MongoDbOperation op)
    {
        var info = new MongoDbOperationInfo(op, "mydb", null);

        var label = MongoDbOperationClassifier.GetDiagramLabel(info, MongoDbTrackingVerbosity.Detailed);

        Assert.Equal(op.ToString(), label);
    }

    [Theory]
    [InlineData(MongoDbOperation.CreateIndex, "users", "CreateIndex → users")]
    [InlineData(MongoDbOperation.DropIndex, "users", "DropIndex → users")]
    [InlineData(MongoDbOperation.CreateCollection, "users", "CreateCollection → users")]
    [InlineData(MongoDbOperation.DropCollection, "users", "DropCollection → users")]
    [InlineData(MongoDbOperation.DropDatabase, null, "DropDatabase")]
    [InlineData(MongoDbOperation.RenameCollection, "users", "RenameCollection → users")]
    [InlineData(MongoDbOperation.ListIndexes, "users", "ListIndexes ← users")]
    public void GetDiagramLabel_Detailed_SchemaOps_DirectionalArrow(
        MongoDbOperation op, string? collection, string expectedLabel)
    {
        var info = new MongoDbOperationInfo(op, "mydb", collection);

        var label = MongoDbOperationClassifier.GetDiagramLabel(info, MongoDbTrackingVerbosity.Detailed);

        Assert.Equal(expectedLabel, label);
    }

    // ─── Document count extraction ───────────────────────────

    [Fact]
    public void Classify_Insert_ExtractsDocumentCount()
    {
        var docs = new BsonArray { new BsonDocument("name", "Alice"), new BsonDocument("name", "Bob") };
        var command = new BsonDocument { { "insert", "users" }, { "documents", docs } };

        var result = MongoDbOperationClassifier.Classify("insert", "mydb", command);

        Assert.Equal(2, result.DocumentCount);
    }

    [Fact]
    public void Classify_InsertSingleDocument_CountIsOne()
    {
        var docs = new BsonArray { new BsonDocument("name", "Alice") };
        var command = new BsonDocument { { "insert", "users" }, { "documents", docs } };

        var result = MongoDbOperationClassifier.Classify("insert", "mydb", command);

        Assert.Equal(1, result.DocumentCount);
    }

    [Fact]
    public void Classify_Find_DocumentCountIsNull()
    {
        var command = new BsonDocument { { "find", "users" }, { "filter", new BsonDocument() } };

        var result = MongoDbOperationClassifier.Classify("find", "mydb", command);

        Assert.Null(result.DocumentCount);
    }

    // ─── Document ID extraction ──────────────────────────────

    [Fact]
    public void Classify_FindWithIdFilter_ExtractsDocumentId()
    {
        var filter = new BsonDocument("_id", "abc123");
        var command = new BsonDocument { { "find", "users" }, { "filter", filter } };

        var result = MongoDbOperationClassifier.Classify("find", "mydb", command);

        Assert.Equal("abc123", result.DocumentId);
    }

    [Fact]
    public void Classify_FindWithObjectIdFilter_ExtractsDocumentId()
    {
        var oid = new ObjectId("507f1f77bcf86cd799439011");
        var filter = new BsonDocument("_id", oid);
        var command = new BsonDocument { { "find", "users" }, { "filter", filter } };

        var result = MongoDbOperationClassifier.Classify("find", "mydb", command);

        Assert.Equal("507f1f77bcf86cd799439011", result.DocumentId);
    }

    [Fact]
    public void Classify_FindWithComplexFilter_DocumentIdIsNull()
    {
        var filter = new BsonDocument("age", new BsonDocument("$gt", 25));
        var command = new BsonDocument { { "find", "users" }, { "filter", filter } };

        var result = MongoDbOperationClassifier.Classify("find", "mydb", command);

        Assert.Null(result.DocumentId);
    }

    [Fact]
    public void Classify_DeleteWithIdFilter_ExtractsDocumentId()
    {
        var deletes = new BsonArray
        {
            new BsonDocument { { "q", new BsonDocument("_id", "xyz789") }, { "limit", 1 } }
        };
        var command = new BsonDocument { { "delete", "users" }, { "deletes", deletes } };

        var result = MongoDbOperationClassifier.Classify("delete", "mydb", command);

        Assert.Equal("xyz789", result.DocumentId);
    }

    // ─── Pipeline stages extraction ──────────────────────────

    [Fact]
    public void Classify_Aggregate_ExtractsPipelineStages()
    {
        var pipeline = new BsonArray
        {
            new BsonDocument("$match", new BsonDocument("status", "active")),
            new BsonDocument("$group", new BsonDocument("_id", "$category")),
            new BsonDocument("$sort", new BsonDocument("count", -1))
        };
        var command = new BsonDocument { { "aggregate", "orders" }, { "pipeline", pipeline } };

        var result = MongoDbOperationClassifier.Classify("aggregate", "mydb", command);

        Assert.Equal("$match, $group, $sort", result.PipelineStages);
    }

    [Fact]
    public void Classify_AggregateEmptyPipeline_PipelineStagesIsNull()
    {
        var command = new BsonDocument { { "aggregate", "orders" }, { "pipeline", new BsonArray() } };

        var result = MongoDbOperationClassifier.Classify("aggregate", "mydb", command);

        Assert.Null(result.PipelineStages);
    }

    [Fact]
    public void Classify_NonAggregate_PipelineStagesIsNull()
    {
        var command = new BsonDocument { { "find", "users" }, { "filter", new BsonDocument() } };

        var result = MongoDbOperationClassifier.Classify("find", "mydb", command);

        Assert.Null(result.PipelineStages);
    }

    // ─── GridFS detection ────────────────────────────────────

    [Theory]
    [InlineData("fs.files")]
    [InlineData("fs.chunks")]
    public void Classify_GridFsCollection_SetsIsGridFs(string collectionName)
    {
        var command = new BsonDocument { { "find", collectionName } };

        var result = MongoDbOperationClassifier.Classify("find", "mydb", command);

        Assert.True(result.IsGridFs);
    }

    [Fact]
    public void Classify_RegularCollection_IsGridFsFalse()
    {
        var command = new BsonDocument { { "find", "users" } };

        var result = MongoDbOperationClassifier.Classify("find", "mydb", command);

        Assert.False(result.IsGridFs);
    }

    // ─── Enriched labels with metadata ───────────────────────

    [Fact]
    public void GetDiagramLabel_Detailed_InsertWithDocumentCount()
    {
        var info = new MongoDbOperationInfo(MongoDbOperation.Insert, "mydb", "users",
            DocumentCount: 5);

        var label = MongoDbOperationClassifier.GetDiagramLabel(info, MongoDbTrackingVerbosity.Detailed);

        Assert.Equal("Insert (×5) → users", label);
    }

    [Fact]
    public void GetDiagramLabel_Detailed_AggregateWithPipelineStages()
    {
        var info = new MongoDbOperationInfo(MongoDbOperation.Aggregate, "mydb", "orders",
            PipelineStages: "$match, $group");

        var label = MongoDbOperationClassifier.GetDiagramLabel(info, MongoDbTrackingVerbosity.Detailed);

        Assert.Equal("Aggregate ($match, $group) ← orders", label);
    }

    [Fact]
    public void GetDiagramLabel_Detailed_GridFs_ShowsAnnotation()
    {
        var info = new MongoDbOperationInfo(MongoDbOperation.Insert, "mydb", "fs.files",
            IsGridFs: true);

        var label = MongoDbOperationClassifier.GetDiagramLabel(info, MongoDbTrackingVerbosity.Detailed);

        Assert.Equal("Insert → fs.files (GridFS)", label);
    }

    [Fact]
    public void GetDiagramLabel_Summarised_IgnoresMetadata()
    {
        var info = new MongoDbOperationInfo(MongoDbOperation.Insert, "mydb", "users",
            DocumentCount: 5, IsGridFs: false);

        var label = MongoDbOperationClassifier.GetDiagramLabel(info, MongoDbTrackingVerbosity.Summarised);

        Assert.Equal("Insert", label);
    }

    [Fact]
    public void A_single_insert_and_a_single_update_name_their_document()
    {
        // Neither did before: an inserted document never reached the correlation store, so a change stream
        // or a later identity-less command could not find the scenario that wrote it.
        var insert = MongoDbOperationClassifier.Classify("insert", "db", new BsonDocument { { "insert", "orders" }, { "documents", new BsonArray { new BsonDocument("_id", "order-1") } } });
        var update = MongoDbOperationClassifier.Classify("update", "db", new BsonDocument { { "update", "orders" }, { "updates", new BsonArray { new BsonDocument { { "q", new BsonDocument("_id", "order-1") }, { "u", new BsonDocument("$set", new BsonDocument("status", "x")) } } } } });
        var many = MongoDbOperationClassifier.Classify("insert", "db", new BsonDocument { { "insert", "orders" }, { "documents", new BsonArray { new BsonDocument("_id", "a"), new BsonDocument("_id", "b") } } });

        Assert.Equal("order-1", insert.DocumentId);
        Assert.Equal("order-1", update.DocumentId);
        Assert.Null(many.DocumentId);
    }

    [Fact]
    public void A_findAndModify_names_its_document_by_its_query()
    {
        // The command's selector is `query`, not `filter`, so the writer of a document claimed this way never
        // reached the correlation store either, although the option's documentation said it did.
        var claim = MongoDbOperationClassifier.Classify("findAndModify", "db", new BsonDocument
        {
            { "findAndModify", "outbox" },
            { "query", new BsonDocument("_id", "msg-1") },
            { "update", new BsonDocument("$set", new BsonDocument("status", "claimed")) },
        });
        var wide = MongoDbOperationClassifier.Classify("findAndModify", "db", new BsonDocument
        {
            { "findAndModify", "outbox" },
            { "query", new BsonDocument { { "status", "pending" }, { "attempts", new BsonDocument("$lt", 3) } } },
            { "update", new BsonDocument("$set", new BsonDocument("status", "claimed")) },
        });

        Assert.Equal("msg-1", claim.DocumentId);
        Assert.Null(wide.DocumentId);
    }
}
