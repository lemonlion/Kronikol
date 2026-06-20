namespace Kronikol.Sql;

/// <summary>
/// Classified SQL operation types recognised by <see cref="UnifiedSqlClassifier"/>.
/// </summary>
public enum UnifiedSqlOperation
{
    /// <summary>A SELECT query.</summary>
    Select,

    /// <summary>An INSERT statement.</summary>
    Insert,

    /// <summary>An UPDATE statement.</summary>
    Update,

    /// <summary>A DELETE statement.</summary>
    Delete,

    /// <summary>A MERGE statement (SQL Server).</summary>
    Merge,

    /// <summary>An upsert operation (INSERT ON CONFLICT, INSERT ON DUPLICATE KEY, etc.).</summary>
    Upsert,

    /// <summary>A stored procedure invocation (EXEC, CALL).</summary>
    StoredProcedure,

    /// <summary>A CREATE TABLE DDL statement.</summary>
    CreateTable,

    /// <summary>An ALTER TABLE DDL statement.</summary>
    AlterTable,

    /// <summary>A DROP TABLE DDL statement.</summary>
    DropTable,

    /// <summary>A CREATE INDEX DDL statement.</summary>
    CreateIndex,

    /// <summary>A TRUNCATE TABLE statement.</summary>
    Truncate,

    /// <summary>A BEGIN TRANSACTION statement.</summary>
    BeginTransaction,

    /// <summary>A COMMIT statement.</summary>
    Commit,

    /// <summary>A ROLLBACK statement.</summary>
    Rollback,

    /// <summary>An OPTIMIZE TABLE statement (ClickHouse).</summary>
    Optimize,

    /// <summary>A RENAME TABLE statement (ClickHouse, MySQL).</summary>
    Rename,

    /// <summary>An ATTACH TABLE/DATABASE statement (ClickHouse).</summary>
    Attach,

    /// <summary>A DETACH TABLE/DATABASE statement (ClickHouse).</summary>
    Detach,

    /// <summary>An unrecognised SQL operation.</summary>
    Other
}
