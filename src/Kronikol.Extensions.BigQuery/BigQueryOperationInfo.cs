namespace Kronikol.Extensions.BigQuery;

/// <summary>
/// The result of classifying a BigQuery operation, containing the operation type and metadata.
/// </summary>
public record BigQueryOperationInfo(
    BigQueryOperation Operation,
    string? ResourceType,
    string? ResourceName,
    string? ProjectId,
    string? DatasetId)
{
    /// <summary>
    /// What the resource is on, when it is named under another: <c>table/orders</c> for a table's IAM policy or row
    /// access policy. The Detailed path keeps it, so the call reads as the resource it was made on.
    /// </summary>
    internal string? Parent { get; init; }
}
