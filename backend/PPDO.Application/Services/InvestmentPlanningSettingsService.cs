using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// The province-wide Investment Planning settings (v1.8.0 — PPDO-136). Spec:
/// <c>docs/v1.8/Default_Fiscal_Year_Spec.md</c>.
///
/// Validation lives here rather than in a FluentValidation class: no service in the codebase
/// uses one (every <c>Validators/*</c> folder is empty), and a single range check does not
/// justify being the first.
/// </summary>
public sealed class InvestmentPlanningSettingsService : IInvestmentPlanningSettingsService
{
    /// <summary>The earliest year the default may be set to.</summary>
    internal const int MinFiscalYear = 2020;

    /// <summary>How many years past the current Manila year the default may reach.</summary>
    internal const int MaxYearsAhead = 3;

    private static readonly TimeSpan ManilaOffset = TimeSpan.FromHours(8);

    private readonly IInvestmentPlanningSettingsRepository     _repo;
    private readonly IUserRepository                           _users;
    private readonly IAuditService                             _audit;
    private readonly ILogger<InvestmentPlanningSettingsService> _logger;
    private readonly TimeProvider                              _clock;

    public InvestmentPlanningSettingsService(
        IInvestmentPlanningSettingsRepository      repo,
        IUserRepository                            users,
        IAuditService                              audit,
        ILogger<InvestmentPlanningSettingsService> logger,
        TimeProvider?                              clock = null)
    {
        _repo   = repo;
        _users  = users;
        _audit  = audit;
        _logger = logger;
        _clock  = clock ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<DefaultFiscalYearDto> GetDefaultFiscalYearAsync(CancellationToken cancellationToken = default)
    {
        InvestmentPlanningSettings? row = await _repo.GetAsync(cancellationToken);

        // A missing seed row reads as "unset" rather than failing the config page. Writes are
        // stricter — see UpdateDefaultFiscalYearAsync.
        return row is null
            ? new DefaultFiscalYearDto(null, null, null)
            : new DefaultFiscalYearDto(row.DefaultFiscalYear, AsUtc(row.UpdatedAt), row.UpdatedBy?.FullName);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<DefaultFiscalYearDto>> UpdateDefaultFiscalYearAsync(
        int? defaultFiscalYear, Guid actorId, CancellationToken cancellationToken = default)
    {
        int maxFiscalYear = _clock.GetUtcNow().ToOffset(ManilaOffset).Year + MaxYearsAhead;

        if (defaultFiscalYear is int year && (year < MinFiscalYear || year > maxFiscalYear))
            return ServiceResult<DefaultFiscalYearDto>.BadRequest(
                $"Fiscal year must be between {MinFiscalYear} and {maxFiscalYear}.");

        // The row is the migration's to create. If it is missing, the migration did not run —
        // fail loudly (500) instead of quietly inserting a row nobody else knows about.
        InvestmentPlanningSettings row = await _repo.GetAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "investment_planning_settings has no row — has the AddInvestmentPlanningSettings migration run?");

        // Saving the value already stored is a no-op: no write, no audit row, and the
        // last-changed line keeps naming whoever actually changed it.
        if (row.DefaultFiscalYear == defaultFiscalYear)
            return ServiceResult<DefaultFiscalYearDto>.Ok(
                new DefaultFiscalYearDto(row.DefaultFiscalYear, AsUtc(row.UpdatedAt), row.UpdatedBy?.FullName));

        int? oldFiscalYear = row.DefaultFiscalYear;
        row.DefaultFiscalYear = defaultFiscalYear;
        row.UpdatedAt         = _clock.GetUtcNow().UtcDateTime;
        row.UpdatedById       = actorId;

        try
        {
            await _repo.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Saving the default fiscal year failed. FiscalYear: {FiscalYear}, UserId: {UserId}",
                defaultFiscalYear, actorId);
            throw;
        }

        _logger.LogInformation(
            "Default fiscal year changed. OldFiscalYear: {OldFiscalYear}, NewFiscalYear: {NewFiscalYear}, UserId: {UserId}",
            oldFiscalYear, defaultFiscalYear, actorId);

        await _audit.LogAsync(
            "investment_planning_settings", InvestmentPlanningSettings.SingletonId, AuditAction.Update,
            oldValues: new { DefaultFiscalYear = oldFiscalYear },
            newValues: new { DefaultFiscalYear = defaultFiscalYear },
            cancellationToken);

        User? actor = await _users.GetByIdAsync(actorId, cancellationToken);

        return ServiceResult<DefaultFiscalYearDto>.Ok(
            new DefaultFiscalYearDto(row.DefaultFiscalYear, AsUtc(row.UpdatedAt), actor?.FullName));
    }

    /// <summary>
    /// The stamp marked as UTC, which it always is (it is written from <c>UtcDateTime</c>). EF reads a
    /// SQL <c>datetime2</c> back as <see cref="DateTimeKind.Unspecified"/>, which serializes with no
    /// "Z", and the browser then parsed it as local (Manila) time — 8 hours early (PPDO-163).
    /// </summary>
    private static DateTime? AsUtc(DateTime? value) =>
        value is DateTime v ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : null;
}
