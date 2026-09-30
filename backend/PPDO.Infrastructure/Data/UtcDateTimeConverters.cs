using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace PPDO.Infrastructure.Data;

/// <summary>
/// Reads a SQL <c>datetime2</c> back as <see cref="DateTimeKind.Utc"/> (PPDO-164).
///
/// <para>
/// Every timestamp in this system is written as UTC (<c>DateTime.UtcNow</c>, see CLAUDE.md), but EF
/// Core materializes <c>datetime2</c> as <see cref="DateTimeKind.Unspecified"/>. System.Text.Json
/// then serializes it with no trailing "Z", and the browser parses a zone-less ISO string as LOCAL
/// time — so a value read from the database displayed 8 hours early in Manila, while the same value
/// returned straight from the request that wrote it (which does carry "Z") looked right.
/// </para>
///
/// <para>
/// Applied to every <c>DateTime</c> / <c>DateTime?</c> by <c>AppDbContext.ConfigureConventions</c>.
/// The stored value is unchanged, so this needs no migration. <c>DateOnly</c> is a different CLR type
/// and is not touched.
/// </para>
/// </summary>
public sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(
            // Local values are normalised; Utc and Unspecified are stored as-is (Unspecified is what
            // callers that build a DateTime from a parsed string hand us, and is already UTC by convention).
            v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : v,
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
    {
    }
}

/// <summary>Nullable counterpart of <see cref="UtcDateTimeConverter"/>.</summary>
public sealed class NullableUtcDateTimeConverter : ValueConverter<DateTime?, DateTime?>
{
    public NullableUtcDateTimeConverter()
        : base(
            v => v.HasValue && v.Value.Kind == DateTimeKind.Local ? v.Value.ToUniversalTime() : v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v)
    {
    }
}
