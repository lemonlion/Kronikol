namespace Kronikol.Tests.Reports;

/// <summary>
/// A fact that moves the process's working directory runs here, after every parallel collection.
/// </summary>
/// <remarks>
/// The working directory is process state: any test running beside one that moves it would read its relative paths
/// against the wrong folder. Here nothing runs beside it.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WorkingDirectoryCollection
{
    public const string Name = "WorkingDirectory";
}
