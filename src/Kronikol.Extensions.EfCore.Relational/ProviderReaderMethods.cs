using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kronikol.Extensions.EfCore.Relational;

/// <summary>
/// Whether a context's queries read some value through a method of the provider's own reader. EF Core's shaper reads a
/// column with its type mapping's <see cref="RelationalTypeMapping.GetDataReaderMethod()"/>, and casts the reader to the
/// method's declaring type first, so a mapping read through <c>SqlDataReader.GetSqlBytes</c> (SQL Server's spatial
/// columns) or <c>OracleDataReader.GetDateTimeOffset</c> fails on any reader that is not the provider's own, a
/// TrackingDbDataReader included.
/// </summary>
internal static class ProviderReaderMethods
{
    private sealed class Verdict(string? firstUse)
    {
        public string? FirstUse { get; } = firstUse;
    }

    private static readonly ConditionalWeakTable<IModel, Verdict> Verdicts = new();

    // The types raw SQL commonly projects without an entity (SqlQuery<DateTimeOffset>), asked of the type mapping source.
    private static readonly Type[] ProbedClrTypes = [typeof(DateTimeOffset), typeof(DateOnly), typeof(TimeOnly), typeof(TimeSpan)];

    /// <summary>
    /// The first value the context's queries read through a provider's own reader method, as text
    /// (<c>Place.Location is read through SqlDataReader.GetSqlBytes</c>), or null when every one is read through
    /// <see cref="DbDataReader"/>'s own. Reached once per model.
    /// </summary>
    internal static string? FirstUse(DbContext context)
    {
        var model = context.Model;
        return Verdicts.GetValue(model, _ => new Verdict(Find(context, model))).FirstUse;
    }

    internal static bool IsKnown(IModel model) => Verdicts.TryGetValue(model, out _);

    private static string? Find(DbContext context, IModel model)
    {
        try
        {
            foreach (var entityType in model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties())
                    if (ProviderMethod(property.FindRelationalTypeMapping()) is { } method)
                        return $"{entityType.DisplayName()}.{property.Name} is read through {method}";
                foreach (var complexProperty in entityType.GetComplexProperties())
                    if (InComplexType(complexProperty.ComplexType) is { } use)
                        return use;
            }

            var source = context.GetService<IRelationalTypeMappingSource>();
            foreach (var clrType in ProbedClrTypes)
                if (ProviderMethod(source.FindMapping(clrType)) is { } method)
                    return $"{clrType.Name} values are read through {method}";

            return null;
        }
        catch (Exception ex)
        {
            // A model that cannot be checked is left to the provider's reader: the application's reads are what matter.
            return $"the model could not be checked ({ex.GetType().Name})";
        }
    }

    private static string? InComplexType(IComplexType complexType)
    {
        foreach (var property in complexType.GetProperties())
            if (ProviderMethod(property.FindRelationalTypeMapping()) is { } method)
                return $"{complexType.DisplayName()}.{property.Name} is read through {method}";
        foreach (var nested in complexType.GetComplexProperties())
            if (InComplexType(nested.ComplexType) is { } use)
                return use;
        return null;
    }

    private static string? ProviderMethod(RelationalTypeMapping? mapping)
    {
        if (mapping is null) return null;
        var method = mapping.GetDataReaderMethod();
        var declaring = method.DeclaringType;
        return declaring is null || declaring.IsAssignableFrom(typeof(DbDataReader)) ? null : $"{declaring.Name}.{method.Name}";
    }
}
