using Kronikol.Reports;

namespace Kronikol.Tracking.Tabular;

/// <summary>
/// Implemented by tabular parameter types (<see cref="TabularAttributes.TabularInputs{T}"/>
/// and <see cref="TabularAttributes.TabularOutputs{T}"/>) so that
/// <see cref="StepCollector"/> can produce a <see cref="StepParameterKind.Tabular"/>
/// step parameter automatically.
/// </summary>
public interface ITabularParameterData
{
    TabularColumn[] GetColumns();
    TabularRow[] GetRows();
    bool IsLinkedOutput => false;
}
