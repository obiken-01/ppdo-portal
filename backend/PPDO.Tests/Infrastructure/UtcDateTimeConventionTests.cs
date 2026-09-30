using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using PPDO.Infrastructure.Data;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// PPDO-164 — every <c>DateTime</c> read from SQL must come back as <see cref="DateTimeKind.Utc"/>.
///
/// <para>
/// EF materializes <c>datetime2</c> as <see cref="DateTimeKind.Unspecified"/>, which serializes with no
/// "Z" and is parsed by the browser as local time — 8 hours early in Manila. The fix is a single
/// convention in <c>AppDbContext.ConfigureConventions</c>. These tests pin that convention against the
/// real model, so a NEW entity is covered without anyone remembering to opt it in, and deleting the
/// convention fails here. No database is opened: the converter is what materialization runs.
/// </para>
/// </summary>
public sealed class UtcDateTimeConventionTests
{
    // What the SQL provider hands back for a datetime2 column.
    private static readonly DateTime FromSql = new(2026, 9, 30, 2, 5, 38, DateTimeKind.Unspecified);

    private static IModel Model()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=none;Database=none;Trusted_Connection=True;")
            .Options;

        using AppDbContext context = new(options);
        return context.Model;
    }

    private static List<IProperty> DateTimeProperties() =>
        Model().GetEntityTypes()
            .SelectMany(e => e.GetProperties())
            .Where(p => p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?))
            .ToList();

    [Fact]
    public void Model_HasDateTimeProperties_SoTheOtherTestsAreNotVacuous()
    {
        Assert.True(DateTimeProperties().Count > 20);
    }

    [Fact]
    public void EveryDateTimeProperty_ReadFromTheDatabase_HasKindUtc()
    {
        foreach (IProperty property in DateTimeProperties())
        {
            Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter? converter = property.GetValueConverter();
            Assert.True(converter is not null,
                $"{property.DeclaringType.DisplayName()}.{property.Name} has no UTC converter — " +
                "it would read back as Unspecified and display 8h early in Manila.");

            DateTime? read = (DateTime?)converter!.ConvertFromProvider(FromSql);

            Assert.Equal(DateTimeKind.Utc, read!.Value.Kind);
            Assert.Equal(FromSql.Ticks, read.Value.Ticks); // Kind is stamped, the instant is untouched
        }
    }

    [Fact]
    public void UtcConverter_LocalValue_IsWrittenAsUtc()
    {
        DateTime local = new(2026, 9, 30, 10, 5, 38, DateTimeKind.Local);

        DateTime stored = (DateTime)new UtcDateTimeConverter().ConvertToProvider(local)!;

        Assert.Equal(local.ToUniversalTime(), stored);
        Assert.Equal(DateTimeKind.Utc, stored.Kind);
    }

    [Fact]
    public void UtcConverter_UtcValue_IsWrittenUnchanged()
    {
        DateTime utc = new(2026, 9, 30, 2, 5, 38, DateTimeKind.Utc);

        Assert.Equal(utc, (DateTime)new UtcDateTimeConverter().ConvertToProvider(utc)!);
    }

    [Fact]
    public void NullableUtcConverter_Null_StaysNull()
    {
        NullableUtcDateTimeConverter converter = new();

        Assert.Null(converter.ConvertFromProvider(null));
        Assert.Null(converter.ConvertToProvider(null));
    }
}
