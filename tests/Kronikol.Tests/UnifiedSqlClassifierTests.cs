using System.Data;
using Kronikol.Sql;

namespace Kronikol.Tests;

public class UnifiedSqlClassifierTests
{
    // ─── Basic keyword classification ───────────────────────────

    [Fact]
    public void Select_is_classified()
    {
        var result = UnifiedSqlClassifier.Classify("SELECT * FROM Users WHERE Id = @Id");
        Assert.Equal(UnifiedSqlOperation.Select, result.Operation);
        Assert.Equal("Users", result.TableName);
    }

    [Fact]
    public void Insert_is_classified()
    {
        var result = UnifiedSqlClassifier.Classify("INSERT INTO Users (Name, Email) VALUES (@Name, @Email)");
        Assert.Equal(UnifiedSqlOperation.Insert, result.Operation);
        Assert.Equal("Users", result.TableName);
    }

    [Fact]
    public void Update_is_classified()
    {
        var result = UnifiedSqlClassifier.Classify("UPDATE Users SET Name = @Name WHERE Id = @Id");
        Assert.Equal(UnifiedSqlOperation.Update, result.Operation);
        Assert.Equal("Users", result.TableName);
    }

    [Fact]
    public void Delete_is_classified()
    {
        var result = UnifiedSqlClassifier.Classify("DELETE FROM Users WHERE Id = @Id");
        Assert.Equal(UnifiedSqlOperation.Delete, result.Operation);
        Assert.Equal("Users", result.TableName);
    }

    [Fact]
    public void Merge_is_classified()
    {
        var result = UnifiedSqlClassifier.Classify("MERGE INTO Target USING Source ON Target.Id = Source.Id");
        Assert.Equal(UnifiedSqlOperation.Merge, result.Operation);
        Assert.Equal("Target", result.TableName);
    }

    [Fact]
    public void Exec_is_classified_as_StoredProcedure()
    {
        var result = UnifiedSqlClassifier.Classify("EXEC sp_GetUsers @Role = 'Admin'");
        Assert.Equal(UnifiedSqlOperation.StoredProcedure, result.Operation);
    }

    [Fact]
    public void Execute_is_classified_as_StoredProcedure()
    {
        var result = UnifiedSqlClassifier.Classify("EXECUTE dbo.sp_GetUsers");
        Assert.Equal(UnifiedSqlOperation.StoredProcedure, result.Operation);
    }

    [Fact]
    public void Call_is_classified_as_StoredProcedure()
    {
        var result = UnifiedSqlClassifier.Classify("CALL my_proc(1, 2)");
        Assert.Equal(UnifiedSqlOperation.StoredProcedure, result.Operation);
    }

    [Fact]
    public void CommandType_StoredProcedure_overrides_text()
    {
        var result = UnifiedSqlClassifier.Classify("sp_GetUsers", CommandType.StoredProcedure);
        Assert.Equal(UnifiedSqlOperation.StoredProcedure, result.Operation);
    }

    // ─── DDL classification ─────────────────────────────────────

    [Fact]
    public void Create_table_is_classified()
    {
        var result = UnifiedSqlClassifier.Classify("CREATE TABLE Users (Id INT PRIMARY KEY)");
        Assert.Equal(UnifiedSqlOperation.CreateTable, result.Operation);
        Assert.Equal("Users", result.TableName);
    }

    [Fact]
    public void Alter_table_is_classified()
    {
        var result = UnifiedSqlClassifier.Classify("ALTER TABLE Users ADD Email NVARCHAR(200)");
        Assert.Equal(UnifiedSqlOperation.AlterTable, result.Operation);
        Assert.Equal("Users", result.TableName);
    }

    [Fact]
    public void Drop_table_is_classified()
    {
        var result = UnifiedSqlClassifier.Classify("DROP TABLE Users");
        Assert.Equal(UnifiedSqlOperation.DropTable, result.Operation);
        Assert.Equal("Users", result.TableName);
    }

    [Fact]
    public void Create_index_is_classified()
    {
        var result = UnifiedSqlClassifier.Classify("CREATE INDEX IX_Users_Name ON Users (Name)");
        Assert.Equal(UnifiedSqlOperation.CreateIndex, result.Operation);
    }

    [Fact]
    public void Truncate_is_classified()
    {
        var result = UnifiedSqlClassifier.Classify("TRUNCATE TABLE Users");
        Assert.Equal(UnifiedSqlOperation.Truncate, result.Operation);
        Assert.Equal("Users", result.TableName);
    }

    [Fact]
    public void Begin_transaction_is_classified()
    {
        var result = UnifiedSqlClassifier.Classify("BEGIN TRANSACTION");
        Assert.Equal(UnifiedSqlOperation.BeginTransaction, result.Operation);
    }

    // ─── Upsert detection ───────────────────────────────────────

    [Fact]
    public void Insert_on_conflict_do_update_is_upsert()
    {
        var sql = "INSERT INTO Users (Id, Name) VALUES (1, 'Test') ON CONFLICT (Id) DO UPDATE SET Name = 'Test'";
        var result = UnifiedSqlClassifier.Classify(sql);
        Assert.Equal(UnifiedSqlOperation.Upsert, result.Operation);
        Assert.Equal("Users", result.TableName);
    }

    [Fact]
    public void Insert_on_duplicate_key_update_is_upsert()
    {
        var sql = "INSERT INTO Users (Id, Name) VALUES (1, 'Test') ON DUPLICATE KEY UPDATE Name = 'Test'";
        var result = UnifiedSqlClassifier.Classify(sql);
        Assert.Equal(UnifiedSqlOperation.Upsert, result.Operation);
        Assert.Equal("Users", result.TableName);
    }

    [Fact]
    public void Insert_or_replace_is_upsert()
    {
        var sql = "INSERT OR REPLACE INTO Users (Id, Name) VALUES (1, 'Test')";
        var result = UnifiedSqlClassifier.Classify(sql);
        Assert.Equal(UnifiedSqlOperation.Upsert, result.Operation);
    }

    [Fact]
    public void Insert_or_update_is_upsert()
    {
        var sql = "INSERT OR UPDATE INTO Users (Id, Name) VALUES (1, 'Test')";
        var result = UnifiedSqlClassifier.Classify(sql);
        Assert.Equal(UnifiedSqlOperation.Upsert, result.Operation);
    }

    [Fact]
    public void Insert_or_ignore_is_insert()
    {
        var sql = "INSERT OR IGNORE INTO Users (Id, Name) VALUES (1, 'Test')";
        var result = UnifiedSqlClassifier.Classify(sql);
        Assert.Equal(UnifiedSqlOperation.Insert, result.Operation);
    }

    // ─── Multi-dialect table name extraction ────────────────────

    [Fact]
    public void Extracts_table_from_sql_server_brackets()
    {
        var result = UnifiedSqlClassifier.Classify("SELECT * FROM [dbo].[Users]");
        Assert.Equal("Users", result.TableName);
    }

    [Fact]
    public void Extracts_table_from_postgresql_quotes()
    {
        var result = UnifiedSqlClassifier.Classify("SELECT * FROM \"public\".\"Users\"");
        Assert.Equal("Users", result.TableName);
    }

    [Fact]
    public void Extracts_table_from_mysql_backticks()
    {
        var result = UnifiedSqlClassifier.Classify("SELECT * FROM `mydb`.`Users`");
        Assert.Equal("Users", result.TableName);
    }

    // ─── Prefix stripping ───────────────────────────────────────

    [Fact]
    public void Set_nocount_is_stripped()
    {
        var result = UnifiedSqlClassifier.Classify("SET NOCOUNT ON;\nSELECT * FROM Users");
        Assert.Equal(UnifiedSqlOperation.Select, result.Operation);
        Assert.Equal("Users", result.TableName);
    }

    [Fact]
    public void Spanner_hint_is_stripped()
    {
        var result = UnifiedSqlClassifier.Classify("@{PDML_MAX_PARALLELISM=10} SELECT * FROM Users");
        Assert.Equal(UnifiedSqlOperation.Select, result.Operation);
    }

    [Fact]
    public void CTE_prefix_is_stripped()
    {
        var result = UnifiedSqlClassifier.Classify("WITH cte AS (SELECT 1) SELECT * FROM cte");
        Assert.Equal(UnifiedSqlOperation.Select, result.Operation);
    }

    // ─── Edge cases ─────────────────────────────────────────────

    [Fact]
    public void Empty_string_is_Other()
    {
        var result = UnifiedSqlClassifier.Classify("");
        Assert.Equal(UnifiedSqlOperation.Other, result.Operation);
    }

    [Fact]
    public void Null_is_Other()
    {
        var result = UnifiedSqlClassifier.Classify(null);
        Assert.Equal(UnifiedSqlOperation.Other, result.Operation);
    }

    [Theory]
    [InlineData("select * from Users")]
    [InlineData("Select * From Users")]
    [InlineData("SELECT * FROM Users")]
    public void Classification_is_case_insensitive(string sql)
    {
        var result = UnifiedSqlClassifier.Classify(sql);
        Assert.Equal(UnifiedSqlOperation.Select, result.Operation);
    }

    [Fact]
    public void Leading_whitespace_is_handled()
    {
        var result = UnifiedSqlClassifier.Classify("   SELECT * FROM Users");
        Assert.Equal(UnifiedSqlOperation.Select, result.Operation);
    }

    // ─── GetDiagramLabel ────────────────────────────────────────

    [Fact]
    public void Label_Raw_returns_full_sql()
    {
        var op = new UnifiedSqlOperationInfo(UnifiedSqlOperation.Select, "Users", "SELECT * FROM Users");
        var label = UnifiedSqlClassifier.GetDiagramLabel(op, SqlTrackingVerbosityLevel.Raw);
        Assert.Equal("SELECT * FROM Users", label);
    }

    [Fact]
    public void Label_Detailed_includes_table()
    {
        var op = new UnifiedSqlOperationInfo(UnifiedSqlOperation.Select, "Users");
        var label = UnifiedSqlClassifier.GetDiagramLabel(op, SqlTrackingVerbosityLevel.Detailed);
        Assert.Equal("SELECT FROM Users", label);
    }

    [Fact]
    public void Label_Detailed_insert_includes_table()
    {
        var op = new UnifiedSqlOperationInfo(UnifiedSqlOperation.Insert, "Orders");
        var label = UnifiedSqlClassifier.GetDiagramLabel(op, SqlTrackingVerbosityLevel.Detailed);
        Assert.Equal("INSERT INTO Orders", label);
    }

    [Fact]
    public void Label_Detailed_upsert_includes_table()
    {
        var op = new UnifiedSqlOperationInfo(UnifiedSqlOperation.Upsert, "Users");
        var label = UnifiedSqlClassifier.GetDiagramLabel(op, SqlTrackingVerbosityLevel.Detailed);
        Assert.Equal("UPSERT Users", label);
    }

    [Fact]
    public void Label_Detailed_storedproc_extracts_name()
    {
        var op = new UnifiedSqlOperationInfo(UnifiedSqlOperation.StoredProcedure, null, "EXEC sp_GetUsers @Role = 'Admin'");
        var label = UnifiedSqlClassifier.GetDiagramLabel(op, SqlTrackingVerbosityLevel.Detailed);
        Assert.Equal("EXEC sp_GetUsers", label);
    }

    [Fact]
    public void Label_Summarised_returns_keyword_only()
    {
        var op = new UnifiedSqlOperationInfo(UnifiedSqlOperation.Select, "Users");
        var label = UnifiedSqlClassifier.GetDiagramLabel(op, SqlTrackingVerbosityLevel.Summarised);
        Assert.Equal("SELECT", label);
    }

    [Fact]
    public void Label_Summarised_upsert()
    {
        var op = new UnifiedSqlOperationInfo(UnifiedSqlOperation.Upsert, "Users");
        var label = UnifiedSqlClassifier.GetDiagramLabel(op, SqlTrackingVerbosityLevel.Summarised);
        Assert.Equal("UPSERT", label);
    }

    [Fact]
    public void Label_Summarised_create_table()
    {
        var op = new UnifiedSqlOperationInfo(UnifiedSqlOperation.CreateTable, "Users");
        var label = UnifiedSqlClassifier.GetDiagramLabel(op, SqlTrackingVerbosityLevel.Summarised);
        Assert.Equal("CREATE TABLE", label);
    }

    // ─── GetRawKeyword ──────────────────────────────────────────

    [Fact]
    public void GetRawKeyword_returns_uppercase_keyword()
    {
        Assert.Equal("SELECT", UnifiedSqlClassifier.GetRawKeyword("select * from Users"));
    }

    [Fact]
    public void GetRawKeyword_null_returns_null()
    {
        Assert.Null(UnifiedSqlClassifier.GetRawKeyword(null));
    }

    // ─── ExtractProcName ────────────────────────────────────────

    [Fact]
    public void ExtractProcName_from_EXEC()
    {
        Assert.Equal("sp_GetUsers", UnifiedSqlClassifier.ExtractProcName("EXEC sp_GetUsers @Role = 'Admin'"));
    }

    [Fact]
    public void ExtractProcName_from_CALL()
    {
        Assert.Equal("my_proc", UnifiedSqlClassifier.ExtractProcName("CALL my_proc(1)"));
    }

    [Fact]
    public void ExtractProcName_null_returns_question_mark()
    {
        Assert.Equal("?", UnifiedSqlClassifier.ExtractProcName(null));
    }

    // ─── Create table with IF NOT EXISTS ────────────────────────

    [Fact]
    public void Create_table_if_not_exists()
    {
        var result = UnifiedSqlClassifier.Classify("CREATE TABLE IF NOT EXISTS Users (Id INT)");
        Assert.Equal(UnifiedSqlOperation.CreateTable, result.Operation);
        Assert.Equal("Users", result.TableName);
    }

    [Fact]
    public void Drop_table_if_exists()
    {
        var result = UnifiedSqlClassifier.Classify("DROP TABLE IF EXISTS Users");
        Assert.Equal(UnifiedSqlOperation.DropTable, result.Operation);
        Assert.Equal("Users", result.TableName);
    }

    // ─── ClickHouse dialect ─────────────────────────────────────

    [Fact]
    public void ClickHouse_alter_table_update_mutation_is_classified_as_Update()
    {
        var result = UnifiedSqlClassifier.Classify("ALTER TABLE events UPDATE status = 'done' WHERE id = 1");
        Assert.Equal(UnifiedSqlOperation.Update, result.Operation);
        Assert.Equal("events", result.TableName);
    }

    [Fact]
    public void ClickHouse_alter_table_delete_mutation_is_classified_as_Delete()
    {
        var result = UnifiedSqlClassifier.Classify("ALTER TABLE events DELETE WHERE id = 1");
        Assert.Equal(UnifiedSqlOperation.Delete, result.Operation);
        Assert.Equal("events", result.TableName);
    }

    [Fact]
    public void ClickHouse_alter_table_update_on_cluster_is_classified_as_Update()
    {
        var result = UnifiedSqlClassifier.Classify("ALTER TABLE db.events ON CLUSTER my_cluster UPDATE x = 1 WHERE y = 2");
        Assert.Equal(UnifiedSqlOperation.Update, result.Operation);
        Assert.Equal("events", result.TableName);
    }

    [Fact]
    public void ClickHouse_alter_table_add_column_remains_AlterTable()
    {
        var result = UnifiedSqlClassifier.Classify("ALTER TABLE events ADD COLUMN amount Int32");
        Assert.Equal(UnifiedSqlOperation.AlterTable, result.Operation);
        Assert.Equal("events", result.TableName);
    }

    [Fact]
    public void ClickHouse_optimize_table_is_classified()
    {
        var result = UnifiedSqlClassifier.Classify("OPTIMIZE TABLE events FINAL");
        Assert.Equal(UnifiedSqlOperation.Optimize, result.Operation);
        Assert.Equal("events", result.TableName);
    }

    [Fact]
    public void ClickHouse_rename_table_is_classified()
    {
        var result = UnifiedSqlClassifier.Classify("RENAME TABLE events TO events_old");
        Assert.Equal(UnifiedSqlOperation.Rename, result.Operation);
        Assert.Equal("events", result.TableName);
    }

    [Fact]
    public void ClickHouse_attach_table_is_classified()
    {
        var result = UnifiedSqlClassifier.Classify("ATTACH TABLE events");
        Assert.Equal(UnifiedSqlOperation.Attach, result.Operation);
        Assert.Equal("events", result.TableName);
    }

    [Fact]
    public void ClickHouse_detach_table_is_classified()
    {
        var result = UnifiedSqlClassifier.Classify("DETACH TABLE events");
        Assert.Equal(UnifiedSqlOperation.Detach, result.Operation);
        Assert.Equal("events", result.TableName);
    }

    [Fact]
    public void ClickHouse_insert_with_format_is_classified_as_Insert()
    {
        var result = UnifiedSqlClassifier.Classify("INSERT INTO events FORMAT JSONEachRow");
        Assert.Equal(UnifiedSqlOperation.Insert, result.Operation);
        Assert.Equal("events", result.TableName);
    }

    [Fact]
    public void ClickHouse_create_table_with_engine_is_classified()
    {
        var result = UnifiedSqlClassifier.Classify("CREATE TABLE events (id UInt64) ENGINE = MergeTree() ORDER BY id");
        Assert.Equal(UnifiedSqlOperation.CreateTable, result.Operation);
        Assert.Equal("events", result.TableName);
    }

    [Theory]
    [InlineData(UnifiedSqlOperation.Optimize, "OPTIMIZE events", "OPTIMIZE")]
    [InlineData(UnifiedSqlOperation.Rename, "RENAME events", "RENAME")]
    [InlineData(UnifiedSqlOperation.Attach, "ATTACH events", "ATTACH")]
    [InlineData(UnifiedSqlOperation.Detach, "DETACH events", "DETACH")]
    public void Label_ClickHouse_ops(UnifiedSqlOperation op, string expectedDetailed, string expectedSummarised)
    {
        var info = new UnifiedSqlOperationInfo(op, "events");
        Assert.Equal(expectedDetailed, UnifiedSqlClassifier.GetDiagramLabel(info, SqlTrackingVerbosityLevel.Detailed));
        Assert.Equal(expectedSummarised, UnifiedSqlClassifier.GetDiagramLabel(info, SqlTrackingVerbosityLevel.Summarised));
    }
}
