using System.Data;
using Kronikol.Sql;

namespace Kronikol.Extensions.EfCore.Relational;

/// <summary>
/// Classifies Entity Framework Core HTTP requests into specific operations based on URL patterns and HTTP methods.
/// </summary>
public static class SqlOperationClassifier
{
    private static readonly Dictionary<UnifiedSqlOperation, SqlOperation> OperationMap = new()
    {
        [UnifiedSqlOperation.Select] = SqlOperation.Select,
        [UnifiedSqlOperation.Insert] = SqlOperation.Insert,
        [UnifiedSqlOperation.Update] = SqlOperation.Update,
        [UnifiedSqlOperation.Delete] = SqlOperation.Delete,
        [UnifiedSqlOperation.Merge] = SqlOperation.Merge,
        [UnifiedSqlOperation.Upsert] = SqlOperation.Upsert,
        [UnifiedSqlOperation.StoredProcedure] = SqlOperation.StoredProc,
    };

    public static SqlOperationInfo Classify(string? commandText, CommandType commandType = CommandType.Text) =>
        From(UnifiedSqlClassifier.Classify(commandText, commandType));

    /// <summary>
    /// How <see cref="Classify"/> read <paramref name="commandText"/> until #192 R1, when that differs from how it reads
    /// it now (<see cref="UnifiedSqlClassifier.ClassifiedBefore8"/>): a batch opening with two SET statements.
    /// </summary>
    internal static (SqlOperationInfo Info, string? RawKeyword)? ClassifiedBefore8(string? commandText, CommandType commandType = CommandType.Text) =>
        UnifiedSqlClassifier.ClassifiedBefore8(commandText, commandType) is { } before ? (From(before.Info), before.RawKeyword) : null;

    private static SqlOperationInfo From(UnifiedSqlOperationInfo unified) =>
        new(OperationMap.GetValueOrDefault(unified.Operation, SqlOperation.Other), unified.TableName, unified.CommandText);

    public static string? GetDiagramLabel(SqlOperationInfo op, SqlTrackingVerbosity verbosity)
    {
        return verbosity switch
        {
            SqlTrackingVerbosity.Summarised or SqlTrackingVerbosity.Detailed => op.Operation.ToString(),
            _ => null
        };
    }

    public static string? GetRawKeyword(string? commandText) =>
        UnifiedSqlClassifier.GetRawKeyword(commandText);
}