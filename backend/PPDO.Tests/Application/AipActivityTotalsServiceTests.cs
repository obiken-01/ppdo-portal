using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Services;
using PPDO.Domain.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// <see cref="AipActivityTotalsService"/> (v1.8.0 Phase 2 — V18-34).
///
/// <para>
/// The behaviour against a real database is pinned by
/// <see cref="PPDO.Tests.Infrastructure.AipActivityTotalsRecomputeTests"/>. What is left for this
/// suite is the one thing a database cannot show: that the two public methods differ **only** in
/// the flag they pass, and that they pass the right one. That flag decides whether an activity with
/// no lines keeps its imported figures or is written to ₱0, so a swap here is a silent wipe of every
/// FY≤2027 activity the caller touches.
/// </para>
/// </summary>
public sealed class AipActivityTotalsServiceTests
{
    private const int ActivityId = 42;

    private static (AipActivityTotalsService sut,
                    Mock<IAipRepository> aipRepo,
                    Mock<IAipExpenditureRepository> expenditureRepo)
        Build(AipExpenditureTotalsDto? totals = null, bool applyReturns = true)
    {
        Mock<IAipRepository>            aipRepo         = new();
        Mock<IAipExpenditureRepository> expenditureRepo = new();

        expenditureRepo
            .Setup(r => r.SumByActivityIdAsync(ActivityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(totals ?? new AipExpenditureTotalsDto(1_000m, 500m, 0m, 1_500m, 2));

        aipRepo
            .Setup(r => r.ApplyActivityTotalsAsync(
                It.IsAny<int>(), It.IsAny<AipExpenditureTotalsDto>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(applyReturns);

        aipRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        return (new AipActivityTotalsService(
            aipRepo.Object, expenditureRepo.Object, NullLogger<AipActivityTotalsService>.Instance),
            aipRepo, expenditureRepo);
    }

    [Fact]
    public async Task Recalculate_DoesNotAllowZeroing_SoAnImportedActivitySurvives()
    {
        var (sut, aipRepo, _) = Build();

        await sut.RecalculateAsync(ActivityId, CancellationToken.None);

        aipRepo.Verify(r => r.ApplyActivityTotalsAsync(
            ActivityId, It.IsAny<AipExpenditureTotalsDto>(), false, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RecalculateAfterLineDelete_AllowsZeroing_SoAnEmptiedActivityFallsToZero()
    {
        var (sut, aipRepo, _) = Build();

        await sut.RecalculateAfterLineDeleteAsync(ActivityId, CancellationToken.None);

        aipRepo.Verify(r => r.ApplyActivityTotalsAsync(
            ActivityId, It.IsAny<AipExpenditureTotalsDto>(), true, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Recalculate_PassesTheSummedTotalsStraightThrough_WithoutRecomputingInMemory()
    {
        AipExpenditureTotalsDto summed = new(7_000m, 250.50m, 1m, 7_251.50m, 3);
        var (sut, aipRepo, _) = Build(summed);

        await sut.RecalculateAsync(ActivityId, CancellationToken.None);

        // The sum is SQL's answer, not one this service reassembles — a GROUP BY belongs in the
        // repository, per PERFORMANCE_GUIDELINES.md.
        aipRepo.Verify(r => r.ApplyActivityTotalsAsync(
            ActivityId, summed, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Recalculate_WhenTheParentChanged_SavesOnce()
    {
        var (sut, aipRepo, _) = Build(applyReturns: true);

        bool changed = await sut.RecalculateAsync(ActivityId, CancellationToken.None);

        Assert.True(changed);
        aipRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Recalculate_WhenTheParentWasLeftAlone_DoesNotSave()
    {
        // The no-lines guard tripped. Saving here would be an empty unit of work, and worse, it
        // would suggest to a reader that something was written.
        var (sut, aipRepo, _) = Build(
            new AipExpenditureTotalsDto(0m, 0m, 0m, 0m, 0), applyReturns: false);

        bool changed = await sut.RecalculateAsync(ActivityId, CancellationToken.None);

        Assert.False(changed);
        aipRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Recalculate_SumsBeforeApplying_AndTouchesTheContextOnlyOnceAtATime()
    {
        // Both repositories share one AppDbContext, which is not thread-safe — Task.WhenAll over
        // two repo calls sharing it is what produced the GetStatsAsync production 500. This pins
        // the ordering that keeps them sequential.
        var (sut, aipRepo, expenditureRepo) = Build();
        MockSequence sequence = new();

        expenditureRepo.InSequence(sequence)
            .Setup(r => r.SumByActivityIdAsync(ActivityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipExpenditureTotalsDto(1m, 0m, 0m, 1m, 1));
        aipRepo.InSequence(sequence)
            .Setup(r => r.ApplyActivityTotalsAsync(
                ActivityId, It.IsAny<AipExpenditureTotalsDto>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await sut.RecalculateAsync(ActivityId, CancellationToken.None);

        expenditureRepo.Verify(r => r.SumByActivityIdAsync(ActivityId, It.IsAny<CancellationToken>()), Times.Once);
        aipRepo.Verify(r => r.ApplyActivityTotalsAsync(
            ActivityId, It.IsAny<AipExpenditureTotalsDto>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── PPDO-302: a details save on the same activity lands mid-recompute ─────

    private static ConcurrencyConflictException Conflict() =>
        new("The row changed since it was loaded.", new InvalidOperationException());

    [Fact]
    public async Task Recalculate_WhenTheActivitySaveConflicts_ReloadsAndRetriesOnce()
    {
        // The activity was loaded before a details save on it committed, so its rowversion is
        // stale. The totals are a pure function of the lines, and the details save never writes
        // them, so reloading and writing them again loses nothing. Red-test: remove the catch.
        var (sut, aipRepo, expenditureRepo) = Build();
        AipActivity activity = new() { Id = ActivityId };
        aipRepo.Setup(r => r.GetActivityByIdAsync(ActivityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(activity);
        aipRepo.SetupSequence(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(Conflict())
            .ReturnsAsync(1);

        bool changed = await sut.RecalculateAsync(ActivityId, CancellationToken.None);

        Assert.True(changed);
        aipRepo.Verify(r => r.ReloadAsync(activity, It.IsAny<CancellationToken>()), Times.Once);
        aipRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        // Summed again after the reload, so a line written in between is counted.
        expenditureRepo.Verify(r => r.SumByActivityIdAsync(ActivityId, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        aipRepo.Verify(r => r.ApplyActivityTotalsAsync(
            ActivityId, It.IsAny<AipExpenditureTotalsDto>(), false, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Recalculate_WhenTheRetryConflictsToo_Throws_SoTheCallerDecides()
    {
        // One retry, not a loop. A second conflict goes back to the caller, which answers a 409.
        var (sut, aipRepo, _) = Build();
        aipRepo.Setup(r => r.GetActivityByIdAsync(ActivityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipActivity { Id = ActivityId });
        aipRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(Conflict());

        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => sut.RecalculateAsync(ActivityId, CancellationToken.None));

        aipRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
