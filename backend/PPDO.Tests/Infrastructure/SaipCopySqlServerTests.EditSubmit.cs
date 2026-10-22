using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;
using PPDO.Infrastructure.Repositories;
using PPDO.Tests.TestHelpers;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// SAIP T2 against real SQL Server (v1.10.0 — PPDO-247): the query filter only exists in the
/// database, so the copy's doors, edits through the real AIP Entry service, the division rows and
/// the live diff are checked here, each asserting the approved tree did not move.
/// </summary>
public sealed partial class SaipCopySqlServerTests
{
    /// <summary>PTO's own Admin: a department head, and not a PPDO reviewer.</summary>
    private static User PtoAdmin(Seeded s) => new()
    {
        Id = s.UserId, Role = UserRole.Admin, OfficeId = s.PtoId, FullName = "PTO Admin",
        Office = new Office { Id = s.PtoId, OfficeCode = "PTO", IsHostOffice = false },
    };

    private static AipExpenditureService Expenditures(AppDbContext ctx)
    {
        AipRepository aip = new(ctx);
        AipExpenditureRepository exp = new(ctx);
        return new AipExpenditureService(
            aip, exp, new AipActivityTotalsService(aip, exp, NullLogger<AipActivityTotalsService>.Instance),
            Mock.Of<IAipCeilingService>(), new AccountRepository(ctx), new Repository<FundingSource>(ctx),
            Mock.Of<IAuditService>(), new PermissionService(),
            new AipDivisionLock(aip, new AipDivisionSubmissionRepository(ctx), new PermissionService()),
            Mock.Of<IUserRepository>(), new AipReviewCommentRepository(ctx), new RecordingNotificationService(),
            NullLogger<AipExpenditureService>.Instance);
    }

    /// <summary>A copy line with no procurement items (so its amounts are typed, not item totals).</summary>
    private static async Task<AipExpenditure> PlainCopyLineAsync(AppDbContext ctx, int saipId)
        => await ctx.AipExpenditures
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(e => e.Activity.Project.Program.Office.SaipId == saipId && !e.ProcurementItems.Any())
            .OrderBy(e => e.Id)
            .FirstAsync();

    // ── The doors ─────────────────────────────────────────────────────────────

    [SqlServerFact]
    public async Task DraftLookups_FindTheCopyById_AndPerOffice_ButNeverAnApprovedRow()
    {
        Seeded s = await SeedAsync(3101);
        SaipDto saip = await StartAsync(s);

        await using AppDbContext ctx = _db.NewContext();
        AipRepository repo = new(ctx);

        AipOffice? copy = await repo.GetDraftGroupByIdAsync(saip.DraftAipOfficeIds[0]);
        Assert.NotNull(copy);
        Assert.Equal(saip.Id, copy!.SaipId);
        Assert.Null(await repo.GetDraftGroupByIdAsync(s.PtoGroupIds[0]));   // approved: not this door

        IReadOnlyList<AipOffice> pto = await repo.GetDraftGroupsForOfficeAsync(s.RecordId, s.PtoId);
        Assert.Equal(saip.DraftAipOfficeIds.Order(), pto.Select(g => g.Id).Order());
        Assert.Empty(await repo.GetDraftGroupsForOfficeAsync(s.RecordId, s.OpaId));
    }

    // ── Editing the copy through AIP Entry's own service ──────────────────────

    [SqlServerFact]
    public async Task EditingACopyLine_ChangesTheCopyOnly_AndTheAmendedSumsSeeIt()
    {
        Seeded s = await SeedAsync(3102);
        SaipDto saip = await StartAsync(s);

        AipExpenditure line;
        await using (AppDbContext ctx = _db.NewContext()) line = await PlainCopyLineAsync(ctx, saip.Id);

        await using (AppDbContext ctx = _db.NewContext())
        {
            ServiceResult<AipExpenditureWriteResultDto> result = await Expenditures(ctx).UpdateAsync(
                line.Id, new UpdateAipExpenditureDto(null, s.FundId, 0m, 4000m, 500m), PtoAdmin(s), line.RowVersion);
            Assert.True(result.IsSuccess, result.Error);
            Assert.Equal(4500m, result.Value!.ActivityTotal);   // the copy activity's total, recomputed
        }

        await using (AppDbContext ctx = _db.NewContext())
        {
            AipExpenditure approved = await ctx.AipExpenditures.AsNoTracking().SingleAsync(e => e.Id == line.SourceId);
            Assert.Equal((1000m, 500m), (approved.Mooe, approved.Co));

            AipExpenditureRepository exp = new(ctx);
            decimal approvedMooe = (await exp.SumMooeCoByConfigOfficeAndFundAsync(s.RecordId, s.PtoId, s.FundId)).Sum(a => a.Mooe);
            decimal amendedMooe  = (await exp.SumMooeCoBySaipAndFundAsync(saip.Id, s.FundId)).Sum(a => a.Mooe);
            Assert.Equal(4000m, approvedMooe);           // four approved lines × ₱1,000, unchanged
            Assert.Equal(7000m, amendedMooe);            // one of them now ₱4,000 in the copy

            IReadOnlyList<AipActivityFundCodeDto> codes = await exp.GetFundCodesBySaipAsync(saip.Id);
            Assert.Equal(4, codes.Select(c => c.ActivityId).Distinct().Count());
        }
    }

    [SqlServerFact]
    public async Task DeletingAnApprovedCopyLine_IsRefused_ButALineAddedInTheSaipCanGo()
    {
        Seeded s = await SeedAsync(3103);
        SaipDto saip = await StartAsync(s);

        AipExpenditure line;
        await using (AppDbContext ctx = _db.NewContext()) line = await PlainCopyLineAsync(ctx, saip.Id);

        await using (AppDbContext ctx = _db.NewContext())
        {
            ServiceResult<AipExpenditureWriteResultDto> refused =
                await Expenditures(ctx).DeleteAsync(line.Id, PtoAdmin(s), line.RowVersion);
            Assert.Equal(ServiceErrorCode.BadRequest, refused.Code);
            Assert.Equal(AipWriteGuard.SaipApprovedDeleteMessage, refused.Error);
        }

        int addedId;
        await using (AppDbContext ctx = _db.NewContext())
        {
            Assert.True(await ctx.AipExpenditures.AnyAsync(e => e.Id == line.Id), "the approved copy line must still be there");
            ServiceResult<AipExpenditureWriteResultDto> added = await Expenditures(ctx).AddAsync(
                line.ActivityId, new CreateAipExpenditureDto(null, s.FundId, 0m, 250m, 0m), PtoAdmin(s));
            Assert.True(added.IsSuccess, added.Error);
            addedId = added.Value!.Line!.Id;
        }

        await using (AppDbContext ctx = _db.NewContext())
        {
            AipExpenditure fresh = await ctx.AipExpenditures.AsNoTracking().SingleAsync(e => e.Id == addedId);
            Assert.Null(fresh.SourceId);
            ServiceResult<AipExpenditureWriteResultDto> deleted =
                await Expenditures(ctx).DeleteAsync(addedId, PtoAdmin(s), fresh.RowVersion);
            Assert.True(deleted.IsSuccess, deleted.Error);
        }
    }

    [SqlServerFact]
    public async Task ACopyThatHasBeenSentToPpdo_IsLockedForTheOffice()
    {
        Seeded s = await SeedAsync(3104);
        SaipDto saip = await StartAsync(s);
        await using (AppDbContext ctx = _db.NewContext())
            await ctx.Database.ExecuteSqlRawAsync(
                "UPDATE aip_offices SET workflow_status = 'SubmittedToPpdo' WHERE saip_id = {0}", saip.Id);

        await using (AppDbContext ctx = _db.NewContext())
        {
            AipExpenditure line = await PlainCopyLineAsync(ctx, saip.Id);
            ServiceResult<AipExpenditureWriteResultDto> result = await Expenditures(ctx).UpdateAsync(
                line.Id, new UpdateAipExpenditureDto(null, s.FundId, 0m, 9m, 0m), PtoAdmin(s), line.RowVersion);
            Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        }
    }

    // ── Division rows ─────────────────────────────────────────────────────────

    [SqlServerFact]
    public async Task DivisionRows_TheAipAndAnSaipEachHaveTheirOwn_AndOrdinaryReadsSeeOnlyTheAips()
    {
        Seeded s = await SeedAsync(3105);
        SaipDto saip = await StartAsync(s);
        int divisionId;
        await using (AppDbContext ctx = _db.NewContext())
        {
            Division d = new() { OfficeId = s.PtoId, Code = "TRS", Name = "Treasury", IsActive = true };
            ctx.Add(d);
            await ctx.SaveChangesAsync();
            divisionId = d.Id;

            ctx.AddRange(
                new AipDivisionSubmission { AipRecordId = s.RecordId, OfficeId = s.PtoId, DivisionId = d.Id, Status = AipDivisionStatus.Submitted },
                new AipDivisionSubmission { AipRecordId = s.RecordId, OfficeId = s.PtoId, DivisionId = d.Id, Status = AipDivisionStatus.Draft, SaipId = saip.Id });
            await ctx.SaveChangesAsync();   // same (record, division): allowed, because saip_id differs
        }

        await using (AppDbContext ctx = _db.NewContext())
        {
            AipDivisionSubmissionRepository repo = new(ctx);
            AipDivisionSubmission aip = Assert.Single(await repo.GetForOfficeAsync(s.RecordId, s.PtoId));
            Assert.Null(aip.SaipId);
            Assert.Equal(AipDivisionStatus.Submitted, aip.Status);
            Assert.Null(Assert.Single(await repo.GetForOfficesAsync(s.RecordId, [s.PtoId])).SaipId);

            AipDivisionSubmission mine = Assert.Single(await repo.GetForSaipAsync(saip.Id, s.PtoId));
            Assert.Equal(AipDivisionStatus.Draft, mine.Status);
        }

        await using (AppDbContext ctx = _db.NewContext())
        {
            // A second row for the same SAIP and division is a duplicate, as for the AIP.
            ctx.Add(new AipDivisionSubmission { AipRecordId = s.RecordId, OfficeId = s.PtoId, DivisionId = divisionId, SaipId = saip.Id });
            await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        }
    }

    // ── The live diff on real rows ────────────────────────────────────────────

    [SqlServerFact]
    public async Task Changes_AfterOneEdit_ListThatEditAndTheNet()
    {
        Seeded s = await SeedAsync(3106);
        SaipDto saip = await StartAsync(s);

        AipExpenditure line;
        await using (AppDbContext ctx = _db.NewContext()) line = await PlainCopyLineAsync(ctx, saip.Id);
        await using (AppDbContext ctx = _db.NewContext())
            Assert.True((await Expenditures(ctx).UpdateAsync(
                line.Id, new UpdateAipExpenditureDto(null, s.FundId, 0m, 1000m, 0m), PtoAdmin(s), line.RowVersion)).IsSuccess);

        await using (AppDbContext ctx = _db.NewContext())
        {
            ServiceResult<SaipChangesDto> result = await ChangesService(ctx).GetChangesAsync(s.RecordId, s.PtoId, saip.Id, PtoAdmin(s));

            Assert.True(result.IsSuccess, result.Error);
            SaipChangesDto changes = result.Value!;
            // The line's CO went 500 → 0, so its total and the activity's CO and total moved with it.
            Assert.Contains(changes.Changes, c => c.NodeType == SaipNodeType.Expenditure && c.Field == "Co"
                                               && c.Before == "500.00" && c.After == "0.00" && c.SourceId == line.SourceId);
            Assert.Contains(changes.Changes, c => c.NodeType == SaipNodeType.Activity && c.Field == "Total");
            Assert.DoesNotContain(changes.Changes, c => c.Change == SaipChangeKind.Removed);
            Assert.Equal(-500m, changes.NetChange);
            Assert.Equal(6000m, changes.ApprovedTotal);   // four lines × ₱1,500
        }
    }

    private static SaipService ChangesService(AppDbContext ctx)
    {
        Mock<IUserRepository> users = new();
        users.Setup(u => u.GetNamesByIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string>());
        Mock<IAipCeilingService> ceiling = new();
        ceiling.Setup(c => c.GetStatusAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipCeilingStatusDto(null, false, 0m, 0m, 0m, false, []));
        return new SaipService(
            new SaipRepository(ctx), new AipRepository(ctx), new AipExpenditureRepository(ctx),
            new PermissionService(), Mock.Of<IAuditService>(), users.Object,
            Mock.Of<IFileStore>(), Mock.Of<IAipSubmitService>(), Mock.Of<IInvestmentProposalRepository>(),
            new RecordingNotificationService(), ceiling.Object, new AipDivisionSubmissionRepository(ctx),
            NullLogger<SaipService>.Instance);
    }
}
