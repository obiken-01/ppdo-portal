using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace PPDO.Infrastructure.Data;

/// <summary>
/// Reads meaning out of SQL Server errors (v1.8.0 Phase 3 — V18-44 / PPDO-50).
///
/// <para>
/// Infrastructure is the only layer that may know these numbers. <c>PPDO.Application</c>
/// references only <c>PPDO.Domain</c> and cannot see <c>SqlException</c> at all, which is why
/// <see cref="Repository{T}"/> translates rather than letting Application inspect.
/// </para>
/// </summary>
internal static class SqlErrors
{
    /// <summary>Duplicate key row in an object with a unique index.</summary>
    private const int DuplicateKeyRow = 2601;

    /// <summary>Duplicate key violation of a UNIQUE KEY / PRIMARY KEY constraint.</summary>
    private const int DuplicateKeyConstraint = 2627;

    /// <summary>
    /// Whether <paramref name="ex"/> is a unique-index rejection rather than any other write
    /// failure.
    ///
    /// <para>
    /// ⚠️ <b>Both numbers are needed.</b> SQL Server reports 2601 for a unique <i>index</i> and
    /// 2627 for a unique <i>constraint</i>, and the AIP ref-code indexes are the former. Checking
    /// only 2627 — the one most examples show — would let every ref-code conflict fall through as
    /// an unhandled exception and quietly restore the bug this exists to fix.
    /// </para>
    /// </summary>
    internal static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is SqlException sql
           && sql.Number is DuplicateKeyRow or DuplicateKeyConstraint;

    /// <summary>
    /// The index named in the provider's message, or <c>null</c> when it cannot be read.
    ///
    /// <para>
    /// ⚠️ <b>Diagnostic only — never branch on this.</b> It is scraped from an English message
    /// and is both locale- and version-fragile. Code that needs to know which constraint fired
    /// should narrow its <c>try</c> to a single write instead, which is what
    /// <c>RefCodeAllocator</c> does.
    /// </para>
    /// </summary>
    internal static string? IndexNameOf(DbUpdateException ex)
    {
        if (ex.InnerException is not SqlException sql) return null;

        Match match = Regex.Match(
            sql.Message,
            @"(?:unique index|constraint) '([^']+)'",
            RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(100));

        return match.Success ? match.Groups[1].Value : null;
    }
}
