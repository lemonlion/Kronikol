namespace Kronikol.Reports;

/// <summary>
/// A parameter associated with a test step, rendered as inline text, a table, or a tree.
/// </summary>
public record StepParameter
{
    public required string Name { get; set; }
    public StepParameterKind Kind { get; set; }
    public InlineParameterValue? InlineValue { get; set; }
    public TabularParameterValue? TabularValue { get; set; }
    public TreeParameterValue? TreeValue { get; set; }
}

/// <summary>
/// The display style for a step parameter.
/// </summary>
public enum StepParameterKind
{
    /// <summary>A simple inline text value.</summary>
    Inline,

    /// <summary>A tabular value with columns and rows.</summary>
    Tabular,

    /// <summary>A tree-structured value with nested nodes.</summary>
    Tree
}

/// <summary>An inline step parameter value with optional expectation and verification status.</summary>
public record InlineParameterValue(string Value, string? Expectation, VerificationStatus Status);

/// <summary>A tabular step parameter value with column definitions and data rows.</summary>
public record TabularParameterValue(TabularColumn[] Columns, TabularRow[] Rows, bool IsLinkedOutput = false);

/// <summary>A column in a tabular step parameter.</summary>
public record TabularColumn(string Name, bool IsKey);

/// <summary>A row in a tabular step parameter.</summary>
public record TabularRow(TableRowType Type, TabularCell[] Values);

/// <summary>A cell in a tabular step parameter row.</summary>
public record TabularCell(string Value, string? Expectation, VerificationStatus Status);

/// <summary>
/// The verification outcome of a step parameter value.
/// </summary>
public enum VerificationStatus
{
    /// <summary>Verification is not applicable for this value.</summary>
    NotApplicable,

    /// <summary>The value matched the expectation.</summary>
    Success,

    /// <summary>The value did not match the expectation.</summary>
    Failure,

    /// <summary>An exception occurred during verification.</summary>
    Exception,

    /// <summary>No expected value was provided.</summary>
    NotProvided
}

/// <summary>
/// The type of row in a tabular step parameter.
/// </summary>
public enum TableRowType
{
    /// <summary>The row matches an expected row.</summary>
    Matching,

    /// <summary>The row exists in the actual result but was not expected.</summary>
    Surplus,

    /// <summary>The row was expected but not found in the actual result.</summary>
    Missing
}

/// <summary>A tree-structured step parameter value.</summary>
public record TreeParameterValue(TreeNode Root);

/// <summary>A node in a tree-structured step parameter.</summary>
public record TreeNode(string Path, string Node, string Value, string? Expectation, VerificationStatus Status, TreeNode[]? Children);
