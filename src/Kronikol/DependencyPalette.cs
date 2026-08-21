using System.Collections.Frozen;
using Kronikol.Constants;

namespace Kronikol;

/// <summary>
/// Maps dependency category strings to <see cref="DependencyType"/> classifications
/// and provides default color palette for diagram rendering.
/// </summary>
public static class DependencyPalette
{
    /// <summary>Maps well-known <see cref="Tracking.RequestResponseLog.DependencyCategory"/> strings to <see cref="DependencyType"/>.</summary>
    public static readonly FrozenDictionary<string, DependencyType> CategoryToType =
        new Dictionary<string, DependencyType>(StringComparer.OrdinalIgnoreCase)
        {
            [DependencyCategories.CosmosDB] = DependencyType.Database,
            [DependencyCategories.SQL] = DependencyType.Database,
            [DependencyCategories.BigQuery] = DependencyType.Database,
            [DependencyCategories.Redis] = DependencyType.Cache,
            [DependencyCategories.ServiceBus] = DependencyType.MessageQueue,
            [DependencyCategories.BlobStorage] = DependencyType.Storage,
            [DependencyCategories.HTTP] = DependencyType.HttpApi,
            [DependencyCategories.MediatR] = DependencyType.HttpApi,
            [DependencyCategories.MessageQueue] = DependencyType.MessageQueue,
            [DependencyCategories.MongoDB] = DependencyType.Database,
            [DependencyCategories.DynamoDB] = DependencyType.Database,
            [DependencyCategories.Elasticsearch] = DependencyType.Database,
            [DependencyCategories.Spanner] = DependencyType.Database,
            [DependencyCategories.Bigtable] = DependencyType.Database,
            [DependencyCategories.Database] = DependencyType.Database,
            [DependencyCategories.S3] = DependencyType.Storage,
            [DependencyCategories.CloudStorage] = DependencyType.Storage,
            [DependencyCategories.Grpc] = DependencyType.HttpApi,
            [DependencyCategories.PostgreSQL] = DependencyType.Database,
            [DependencyCategories.SqlServer] = DependencyType.Database,
            [DependencyCategories.MySQL] = DependencyType.Database,
            [DependencyCategories.SQLite] = DependencyType.Database,
            [DependencyCategories.Oracle] = DependencyType.Database,
            [DependencyCategories.ClickHouse] = DependencyType.Database,
            [DependencyCategories.AI] = DependencyType.AI,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>Default hex colors for each <see cref="DependencyType"/>. Palette A (Vivid).</summary>
    public static readonly FrozenDictionary<DependencyType, string> DefaultColors =
        new Dictionary<DependencyType, string>
        {
            [DependencyType.HttpApi] = "#438DD5",
            [DependencyType.Database] = "#E74C3C",
            [DependencyType.Cache] = "#F39C12",
            [DependencyType.MessageQueue] = "#9B59B6",
            [DependencyType.Storage] = "#2ECC71",
            [DependencyType.AI] = "#16A085",
            [DependencyType.Unknown] = "#95A5A6",
        }.ToFrozenDictionary();

    /// <summary>Resolves a DependencyCategory string to a <see cref="DependencyType"/>.</summary>
    /// <remarks>When <paramref name="dependencyCategory"/> is <c>null</c>, defaults to <see cref="DependencyType.HttpApi"/>
    /// since the core HTTP tracking handler does not set a category.</remarks>
    public static DependencyType Resolve(string? dependencyCategory)
    {
        if (string.IsNullOrEmpty(dependencyCategory))
            return DependencyType.HttpApi;

        return CategoryToType.TryGetValue(dependencyCategory, out var type) ? type : DependencyType.Unknown;
    }

    /// <summary>Gets the hex color for a dependency category, using user overrides if provided.</summary>
    public static string GetColor(string? dependencyCategory, Dictionary<string, string>? userOverrides = null)
    {
        if (dependencyCategory is not null && userOverrides?.TryGetValue(dependencyCategory, out var overrideColor) == true)
            return overrideColor;

        var type = Resolve(dependencyCategory);
        return DefaultColors.TryGetValue(type, out var color) ? color : DefaultColors[DependencyType.Unknown];
    }

    /// <summary>Gets the PlantUML sequence diagram participant shape keyword for a dependency type.</summary>
    public static string GetSequenceShape(DependencyType type) => type switch
    {
        DependencyType.HttpApi => "entity",
        DependencyType.Database => "database",
        DependencyType.Cache => "collections",
        DependencyType.MessageQueue => "queue",
        DependencyType.Storage => "database",
        DependencyType.AI => "control",
        DependencyType.Unknown => "participant",
        _ => "participant"
    };
}
