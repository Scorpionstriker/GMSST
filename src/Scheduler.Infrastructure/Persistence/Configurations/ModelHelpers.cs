using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Scheduler.Infrastructure.Persistence.Configurations;

/// <summary>Helpers that keep the entity configurations short and consistent.</summary>
internal static class ModelHelpers
{
    /// <summary>Stores an enum as its name in a VARCHAR column with a CHECK constraint listing the allowed values (BR-07).</summary>
    public static PropertyBuilder<TEnum> IsEnumText<TEntity, TEnum>(this PropertyBuilder<TEnum> property, EntityTypeBuilder<TEntity> entity,
        string column, int length)
        where TEntity : class
        where TEnum : struct, Enum
    {
        var values = string.Join(", ", Enum.GetNames<TEnum>().Select(n => $"'{n}'"));
        entity.ToTable(t => t.HasCheckConstraint($"ck_{entity.Metadata.GetTableName()}_{column}", $"{column} IN ({values})"));
        return property.HasColumnName(column).HasConversion<string>().HasMaxLength(length).IsRequired();
    }

    public static PropertyBuilder<TEnum?> IsEnumText<TEntity, TEnum>(this PropertyBuilder<TEnum?> property, EntityTypeBuilder<TEntity> entity,
        string column, int length)
        where TEntity : class
        where TEnum : struct, Enum
    {
        var values = string.Join(", ", Enum.GetNames<TEnum>().Select(n => $"'{n}'"));
        entity.ToTable(t => t.HasCheckConstraint($"ck_{entity.Metadata.GetTableName()}_{column}", $"{column} IS NULL OR {column} IN ({values})"));
        return property.HasColumnName(column).HasConversion<string>().HasMaxLength(length);
    }
}
