using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="InvestmentPlanningSettingsService"/> (PPDO-136): the province-wide
/// default fiscal year — get, set, change, clear, the no-op on an unchanged value, the audit row,
/// and the 2020 … Manila year + 3 range. Spec: <c>docs/v1.8/Default_Fiscal_Year_Spec.md</c> §3.1.
/// </summary>
public sealed class InvestmentPlanningSettingsServiceTests
{
    private static readonly Guid ActorId = Guid.NewGuid();

    /// <summary>
    /// A clock pinned to 2026-12-31 20:00 UTC — already 2027-01-01 04:00 in Manila. The range's
    /// upper bound must follow the Manila year (2027 + 3 = 2030), not the UTC one (2029).
    /// </summary>
    private static readonly DateTimeOffset NewYearsEveUtc = new(2026, 12, 31, 20, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static InvestmentPlanningSettings Row(int? year = null, User? updatedBy = null) => new()
    {
        Id                = InvestmentPlanningSettings.SingletonId,
        DefaultFiscalYear = year,
        UpdatedAt         = year is null ? null : new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        UpdatedById       = updatedBy?.Id,
        UpdatedBy         = updatedBy,
    };

    private static (InvestmentPlanningSettingsService sut,
                    Mock<IInvestmentPlanningSettingsRepository> repo,
                    Mock<IAuditService> audit) Build(
        InvestmentPlanningSettings? row, DateTimeOffset? now = null, User? actor = null)
    {
        Mock<IInvestmentPlanningSettingsRepository> repo = new();
        repo.Setup(r => r.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(row);

        Mock<IUserRepository> users = new();
        users.Setup(u => u.GetByIdAsync(ActorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(actor ?? new User { Id = ActorId, FullName = "Juan Dela Cruz" });

        Mock<IAuditService> audit = new();

        InvestmentPlanningSettingsService sut = new(
            repo.Object, users.Object, audit.Object,
            NullLogger<InvestmentPlanningSettingsService>.Instance,
            new FixedClock(now ?? NewYearsEveUtc));

        return (sut, repo, audit);
    }

    // ── Get ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDefaultFiscalYearAsync_Unset_ReturnsAllNulls()
    {
        (InvestmentPlanningSettingsService sut, _, _) = Build(Row());

        DefaultFiscalYearDto result = await sut.GetDefaultFiscalYearAsync();

        Assert.Equal(new DefaultFiscalYearDto(null, null, null), result);
    }

    [Fact]
    public async Task GetDefaultFiscalYearAsync_Set_ReturnsYearAndWhoSetIt()
    {
        User setter = new() { Id = Guid.NewGuid(), FullName = "Maria Santos" };
        (InvestmentPlanningSettingsService sut, _, _) = Build(Row(2028, setter));

        DefaultFiscalYearDto result = await sut.GetDefaultFiscalYearAsync();

        Assert.Equal(2028, result.DefaultFiscalYear);
        Assert.Equal("Maria Santos", result.UpdatedByName);
        Assert.NotNull(result.UpdatedAt);
    }

    [Fact]
    public async Task GetDefaultFiscalYearAsync_StoredStampHasNoKind_ReturnsItMarkedUtc()
    {
        // PPDO-163 — EF reads a SQL datetime2 back as DateTimeKind.Unspecified, which serializes with
        // no "Z"; the browser then parsed the UTC stamp as Manila time and showed it 8 hours early.
        InvestmentPlanningSettings row = Row(2028);
        row.UpdatedAt = new DateTime(2026, 9, 30, 1, 54, 0, DateTimeKind.Unspecified);
        (InvestmentPlanningSettingsService sut, _, _) = Build(row);

        DefaultFiscalYearDto result = await sut.GetDefaultFiscalYearAsync();

        Assert.Equal(DateTimeKind.Utc, result.UpdatedAt!.Value.Kind);
        Assert.Equal(new DateTime(2026, 9, 30, 1, 54, 0, DateTimeKind.Utc), result.UpdatedAt);
    }

    [Fact]
    public async Task UpdateDefaultFiscalYearAsync_SameValue_ReturnsTheStoredStampMarkedUtc()
    {
        InvestmentPlanningSettings row = Row(2028);
        row.UpdatedAt = new DateTime(2026, 9, 30, 1, 54, 0, DateTimeKind.Unspecified);
        (InvestmentPlanningSettingsService sut, _, _) = Build(row);

        ServiceResult<DefaultFiscalYearDto> result = await sut.UpdateDefaultFiscalYearAsync(2028, ActorId);

        Assert.Equal(DateTimeKind.Utc, result.Value!.UpdatedAt!.Value.Kind);
    }

    [Fact]
    public async Task GetDefaultFiscalYearAsync_SeedRowMissing_ReturnsAllNullsRatherThanThrowing()
    {
        (InvestmentPlanningSettingsService sut, _, _) = Build(row: null);

        DefaultFiscalYearDto result = await sut.GetDefaultFiscalYearAsync();

        Assert.Null(result.DefaultFiscalYear);
    }

    // ── Update: happy paths ──────────────────────────────────────────────────

    [Fact]
    public async Task UpdateDefaultFiscalYearAsync_FromUnset_SavesStampsAndReturnsTheNewState()
    {
        InvestmentPlanningSettings row = Row();
        (InvestmentPlanningSettingsService sut, Mock<IInvestmentPlanningSettingsRepository> repo, _) = Build(row);

        ServiceResult<DefaultFiscalYearDto> result = await sut.UpdateDefaultFiscalYearAsync(2028, ActorId);

        Assert.True(result.IsSuccess);
        Assert.Equal(2028, result.Value!.DefaultFiscalYear);
        Assert.Equal("Juan Dela Cruz", result.Value.UpdatedByName);
        Assert.Equal(2028, row.DefaultFiscalYear);
        Assert.Equal(ActorId, row.UpdatedById);
        Assert.Equal(NewYearsEveUtc.UtcDateTime, row.UpdatedAt);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateDefaultFiscalYearAsync_Change_AuditsOldAndNewYear()
    {
        (InvestmentPlanningSettingsService sut, _, Mock<IAuditService> audit) = Build(Row(2027));

        await sut.UpdateDefaultFiscalYearAsync(2028, ActorId);

        audit.Verify(a => a.LogAsync(
            "investment_planning_settings", InvestmentPlanningSettings.SingletonId, AuditAction.Update,
            It.Is<object>(o => o.ToString()!.Contains("2027")),
            It.Is<object>(o => o.ToString()!.Contains("2028")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateDefaultFiscalYearAsync_Null_ClearsTheDefault()
    {
        InvestmentPlanningSettings row = Row(2028);
        (InvestmentPlanningSettingsService sut, Mock<IInvestmentPlanningSettingsRepository> repo, _) = Build(row);

        ServiceResult<DefaultFiscalYearDto> result = await sut.UpdateDefaultFiscalYearAsync(null, ActorId);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.DefaultFiscalYear);
        Assert.Null(row.DefaultFiscalYear);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(2028)]
    [InlineData(null)]
    public async Task UpdateDefaultFiscalYearAsync_SameValue_WritesNothingAndAuditsNothing(int? year)
    {
        User setter = new() { Id = Guid.NewGuid(), FullName = "Maria Santos" };
        InvestmentPlanningSettings row = Row(year, year is null ? null : setter);
        (InvestmentPlanningSettingsService sut, Mock<IInvestmentPlanningSettingsRepository> repo,
            Mock<IAuditService> audit) = Build(row);

        ServiceResult<DefaultFiscalYearDto> result = await sut.UpdateDefaultFiscalYearAsync(year, ActorId);

        Assert.True(result.IsSuccess);
        Assert.Equal(year, result.Value!.DefaultFiscalYear);
        Assert.Equal(year is null ? null : setter.Id, row.UpdatedById);   // not re-stamped with the actor
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        audit.Verify(a => a.LogAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateDefaultFiscalYearAsync_YearWithNoAipRecord_IsAllowed()
    {
        // Decision 3: the point of a stored value is moving everyone to a year before any AIP
        // record for it exists. The service must not look the year up.
        (InvestmentPlanningSettingsService sut, _, _) = Build(Row());

        ServiceResult<DefaultFiscalYearDto> result = await sut.UpdateDefaultFiscalYearAsync(2029, ActorId);

        Assert.True(result.IsSuccess);
    }

    // ── Update: range ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(2020)]
    [InlineData(2030)]   // Manila year 2027 + 3
    public async Task UpdateDefaultFiscalYearAsync_AtEitherBound_IsAccepted(int year)
    {
        (InvestmentPlanningSettingsService sut, _, _) = Build(Row());

        ServiceResult<DefaultFiscalYearDto> result = await sut.UpdateDefaultFiscalYearAsync(year, ActorId);

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData(2019)]
    [InlineData(2031)]
    [InlineData(0)]
    [InlineData(-2028)]
    public async Task UpdateDefaultFiscalYearAsync_OutOfRange_IsRejectedWithTheRangeMessage(int year)
    {
        InvestmentPlanningSettings row = Row(2028);
        (InvestmentPlanningSettingsService sut, Mock<IInvestmentPlanningSettingsRepository> repo,
            Mock<IAuditService> audit) = Build(row);

        ServiceResult<DefaultFiscalYearDto> result = await sut.UpdateDefaultFiscalYearAsync(year, ActorId);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Equal("Fiscal year must be between 2020 and 2030.", result.Error);
        Assert.Equal(2028, row.DefaultFiscalYear);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        audit.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateDefaultFiscalYearAsync_UpperBound_FollowsTheManilaYearNotUtc()
    {
        // 2026-12-31 20:00 UTC is 2027 in Manila, so 2030 is in range. Against the UTC year the
        // bound would be 2029 and this would be rejected — the bug this test pins.
        (InvestmentPlanningSettingsService sut, _, _) = Build(Row(), now: NewYearsEveUtc);

        ServiceResult<DefaultFiscalYearDto> result = await sut.UpdateDefaultFiscalYearAsync(2030, ActorId);

        Assert.True(result.IsSuccess);
    }

    // ── Update: failures ─────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateDefaultFiscalYearAsync_SeedRowMissing_Throws()
    {
        // A missing seed row means the migration did not run. Fail loudly (500) rather than
        // quietly inserting a row the migration was supposed to own.
        (InvestmentPlanningSettingsService sut, _, _) = Build(row: null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.UpdateDefaultFiscalYearAsync(2028, ActorId));
    }

    [Fact]
    public async Task UpdateDefaultFiscalYearAsync_SaveFails_RethrowsAndWritesNoAuditRow()
    {
        (InvestmentPlanningSettingsService sut, Mock<IInvestmentPlanningSettingsRepository> repo,
            Mock<IAuditService> audit) = Build(Row(2027));
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.UpdateDefaultFiscalYearAsync(2028, ActorId));

        audit.VerifyNoOtherCalls();
    }

    // ── Signatory defaults (PPDO-155) ─────────────────────────────────────────

    [Fact]
    public async Task UpdateSignatoryDefaultsAsync_TrimsSavesAndAudits()
    {
        InvestmentPlanningSettings row = Row();
        (InvestmentPlanningSettingsService sut, Mock<IInvestmentPlanningSettingsRepository> repo, Mock<IAuditService> audit) = Build(row);

        ServiceResult<SignatoryDefaultsDto> result = await sut.UpdateSignatoryDefaultsAsync(
            new UpdateSignatoryDefaultsDto("  PPDC Name ", "PPDC", " ", "Provincial Governor"), ActorId);

        Assert.Equal(new SignatoryDefaultsDto("PPDC Name", "PPDC", null, "Provincial Governor"), result.Value);
        Assert.Equal(("PPDC Name", (string?)null), (row.PpdcName, row.LceName));
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        audit.Verify(a => a.LogAsync("investment_planning_settings", InvestmentPlanningSettings.SingletonId,
            AuditAction.Update, It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateSignatoryDefaultsAsync_Unchanged_WritesNothing()
    {
        InvestmentPlanningSettings row = Row();
        row.PpdcName = "PPDC Name";
        (InvestmentPlanningSettingsService sut, Mock<IInvestmentPlanningSettingsRepository> repo, Mock<IAuditService> audit) = Build(row);

        await sut.UpdateSignatoryDefaultsAsync(new UpdateSignatoryDefaultsDto("PPDC Name", null, null, null), ActorId);

        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        audit.Verify(a => a.LogAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateSignatoryDefaultsAsync_TooLong_Returns400()
    {
        (InvestmentPlanningSettingsService sut, Mock<IInvestmentPlanningSettingsRepository> repo, _) = Build(Row());

        ServiceResult<SignatoryDefaultsDto> result = await sut.UpdateSignatoryDefaultsAsync(
            new UpdateSignatoryDefaultsDto(new string('x', 201), null, null, null), ActorId);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetSignatoryDefaultsAsync_NoRow_ReturnsAllNulls()
    {
        (InvestmentPlanningSettingsService sut, _, _) = Build(null);
        Assert.Equal(new SignatoryDefaultsDto(null, null, null, null), await sut.GetSignatoryDefaultsAsync());
    }
}
