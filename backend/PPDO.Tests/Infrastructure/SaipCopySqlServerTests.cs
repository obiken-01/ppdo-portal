using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.DTOs.ExternalApi;
using PPDO.Application.DTOs.ExternalApi;
using PPDO.Application.Services;
using PPDO.Domain.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;
using PPDO.Infrastructure.Repositories;
using PPDO.Tests.Application;
using PPDO.Tests.TestHelpers;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// The SAIP draft copy on a <b>real SQL Server</b> (v1.10.0 — PPDO-246, SAIP_Spec.md §5 and §11):
/// the copy itself, its transaction, cancel's cascade, the one-open index — and the global query
/// filter that keeps a copy out of every existing read.
///
/// <para>
/// <b>Why SQL Server and not Sqlite.</b> The risks are the database's: the filtered unique index, the
/// ON DELETE CASCADE that cancel relies on, and whether EF applies the <c>saip_id IS NULL</c> filter to
/// the navigation joins the ceiling sums use. Throwaway database from the EF model
/// (<see cref="SqlServerTestDatabase"/>); skipped where there is no SQL Server, as on CI.
/// </para>
///
/// <para>
/// <b>How the filter tests work.</b> Each runs a read, starts an SAIP (so a full copy exists), then
/// <b>inflates the copy</b> — a million pesos on every copied line and activity, the copied groups
/// renamed and marked Consolidated — and runs the read again. The two results must be identical. A copy that leaked into the
/// read would change a sum, a count or a name.
/// </para>
///
/// <para>
/// ⚠️ <b>Red-tested (PPDO-246).</b> With <c>HasQueryFilter</c> removed from
/// <c>AipOfficeConfiguration</c>, every <c>Filter_*</c> test fails. With <c>IgnoreQueryFilters()</c>
/// removed from <c>SaipRepository.DeleteDraftCopyAsync</c>,
/// <see cref="Cancel_RemovesTheWholeCopyAndNothingElse"/> fails.
/// </para>
/// </summary>
public sealed partial class SaipCopySqlServerTests : IClassFixture<SqlServerTestDatabase>
{
    private readonly SqlServerTestDatabase _db;

    public SaipCopySqlServerTests(SqlServerTestDatabase db) => _db = db;

    // ── Seed ──────────────────────────────────────────────────────────────────

    private sealed record Seeded(
        int RecordId, int FiscalYear, int PtoId, int OpaId, int FundId, Guid UserId, IReadOnlyList<int> PtoGroupIds);

    /// <summary>
    /// One FY record holding PTO (two sector groups) and OPA (one group), all Consolidated. Each group
    /// has a program → project → two activities, each activity one General Fund line, and the first
    /// line of each group is itemised. Every test uses its own fiscal year and offices, because the
    /// fixture's database is shared across the class.
    /// </summary>
    private async Task<Seeded> SeedAsync(int fiscalYear)
    {
        await using AppDbContext ctx = _db.NewContext();
        Guid user = await ctx.Users.Select(u => u.Id).FirstAsync();
        DateTime now = DateTime.UtcNow;

        Office pto = new() { OfficeCode = $"PTO{fiscalYear}", OfficeName = "Provincial Treasurer", CreatedAt = now, UpdatedAt = now };
        Office opa = new() { OfficeCode = $"OPA{fiscalYear}", OfficeName = "Provincial Agriculturist", CreatedAt = now, UpdatedAt = now };
        FundingSource gf = new() { Code = $"GF{fiscalYear}", Name = "General Fund", CreatedAt = now, UpdatedAt = now };
        ctx.AddRange(pto, opa, gf);
        AipRecord record = new()
        {
            FiscalYear = fiscalYear, EntrySource = "Manual", Status = PlanningStatus.Draft,
            UploadedById = user, UploadedAt = now,
        };
        ctx.Add(record);
        await ctx.SaveChangesAsync();

        AipOffice ptoMain = Group(record, pto, "1000-000-1-01-020", "PTO", "GENERAL", gf, now);
        AipOffice ptoSub  = Group(record, pto, "1000-000-1-01-020", "PTO - Assessor", "ECONOMIC", gf, now);
        AipOffice opaMain = Group(record, opa, "8000-000-1-01-030", "OPA", "ECONOMIC", gf, now);
        ctx.AddRange(ptoMain, ptoSub, opaMain);
        await ctx.SaveChangesAsync();

        // Lines are not reachable from an activity, so they are added once the activities have ids.
        foreach (AipOffice g in new[] { ptoMain, ptoSub, opaMain })
        {
            bool first = true;
            foreach (AipActivity a in g.Programs.Single().Projects.Single().Activities)
            {
                AipExpenditure line = new()
                {
                    ActivityId = a.Id, FundingSourceId = gf.Id, FundingSourceSnapshot = gf.Code,
                    FundingSourceNameSnapshot = gf.Name, Mooe = 1000m, Co = 500m, CreatedAt = now, UpdatedAt = now,
                };
                line.Recalculate();
                if (first)
                {
                    AipProcurementItem item = new() { Name = "Bond paper", Unit = "ream", UnitPrice = 250m, Qty = 4m };
                    item.Recalculate();
                    line.ProcurementItems.Add(item);
                    first = false;
                }
                ctx.Add(line);
            }
        }
        await ctx.SaveChangesAsync();

        return new Seeded(record.Id, fiscalYear, pto.Id, opa.Id, gf.Id, user, [ptoMain.Id, ptoSub.Id]);
    }

    private static AipOffice Group(
        AipRecord record, Office office, string refCode, string name, string sector, FundingSource gf, DateTime now)
    {
        AipActivity Activity(string suffix) => new()
        {
            RefCode = $"{refCode}-001-001-{suffix}", Name = $"{name} activity {suffix}", EsreCode = "SS",
            FundingSourceId = gf.Id, FundingSourceSnapshot = gf.Code, Mooe = 1000m, Co = 500m, Total = 1500m,
            UpdatedAt = now,
        };

        return new AipOffice
        {
            AipRecordId = record.Id, RefCode = refCode, Name = name, Sector = sector, OfficeId = office.Id,
            WorkflowStatus = AipWorkflowStatus.Consolidated,
            Programs =
            {
                new AipProgram
                {
                    RefCode = $"{refCode}-001-{sector[..3]}", Name = $"{name} program", FunctionBand = "CORE",
                    Projects =
                    {
                        new AipProject
                        {
                            RefCode = $"{refCode}-001-001", Name = $"{name} project",
                            Activities = { Activity("001"), Activity("002") },
                        },
                    },
                },
            },
        };
    }

    private static User SuperAdmin(Guid id) => new() { Id = id, Role = UserRole.SuperAdmin, FullName = "Super Admin" };

    private static SaipService Service(AppDbContext ctx)
    {
        Mock<IUserRepository> users = new();
        users.Setup(u => u.GetNamesByIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string>());
        return new SaipService(
            new SaipRepository(ctx), new AipRepository(ctx), new AipExpenditureRepository(ctx),
            new PermissionService(), Mock.Of<IAuditService>(), users.Object,
            Mock.Of<IFileStore>(), Mock.Of<IAipSubmitService>(), Mock.Of<IInvestmentProposalRepository>(),
            new RecordingNotificationService(), Mock.Of<IAipCeilingService>(),
            Mock.Of<IAipDivisionSubmissionRepository>(), NullLogger<SaipService>.Instance);
    }

    private async Task<SaipDto> StartAsync(Seeded s)
    {
        await using AppDbContext ctx = _db.NewContext();
        ServiceResult<SaipDto> started = await Service(ctx).StartAsync(s.RecordId, s.PtoId, SuperAdmin(s.UserId));
        Assert.True(started.IsSuccess, started.Error);
        return started.Value!;
    }

    /// <summary>Every row under one SAIP's copy, counted per table, bypassing the filter on purpose.</summary>
    private async Task<(int Groups, int Programs, int Projects, int Activities, int Lines, int Items)> CountCopyAsync(int saipId)
    {
        await using AppDbContext ctx = _db.NewContext();
        IQueryable<AipOffice> groups = ctx.AipOffices.IgnoreQueryFilters().Where(o => o.SaipId == saipId);
        return (
            await groups.CountAsync(),
            await ctx.AipPrograms.CountAsync(p => groups.Any(g => g.Id == p.OfficeId)),
            await ctx.AipProjects.CountAsync(j => groups.Any(g => g.Id == j.Program.OfficeId)),
            await ctx.AipActivities.CountAsync(a => groups.Any(g => g.Id == a.Project.Program.OfficeId)),
            await ctx.AipExpenditures.CountAsync(e => groups.Any(g => g.Id == e.Activity.Project.Program.OfficeId)),
            await ctx.AipProcurementItems.CountAsync(i => groups.Any(g => g.Id == i.Expenditure.Activity.Project.Program.OfficeId)));
    }

    /// <summary>Makes any leak visible: a million pesos on every copied line and activity, groups renamed and marked Consolidated.</summary>
    private async Task InflateCopyAsync(int saipId)
    {
        await using AppDbContext ctx = _db.NewContext();
        await ctx.Database.ExecuteSqlRawAsync("""
            UPDATE e SET e.mooe = e.mooe + 1000000, e.total = e.total + 1000000
            FROM aip_expenditures e
            JOIN aip_activities a ON a.id = e.activity_id
            JOIN aip_projects j ON j.id = a.project_id
            JOIN aip_programs p ON p.id = j.program_id
            JOIN aip_offices o ON o.id = p.office_id
            WHERE o.saip_id = {0};
            UPDATE a SET a.mooe = a.mooe + 1000000, a.total = a.total + 1000000
            FROM aip_activities a
            JOIN aip_projects j ON j.id = a.project_id
            JOIN aip_programs p ON p.id = j.program_id
            JOIN aip_offices o ON o.id = p.office_id
            WHERE o.saip_id = {0};
            -- Consolidated too, so a leak cannot hide behind a state filter (Annex B prints Consolidated only).
            UPDATE aip_offices SET name = 'LEAKED COPY', workflow_status = 'Consolidated' WHERE saip_id = {0};
            """, saipId);
    }

    private static string Json(object? value) => JsonSerializer.Serialize(value);

    /// <summary>Runs <paramref name="read"/>, makes and inflates a copy, runs it again: must be identical.</summary>
    private async Task AssertUnchangedByACopyAsync(int fiscalYear, Func<AppDbContext, Seeded, Task<object?>> read)
    {
        Seeded s = await SeedAsync(fiscalYear);

        string before;
        await using (AppDbContext ctx = _db.NewContext()) before = Json(await read(ctx, s));

        SaipDto saip = await StartAsync(s);
        Assert.True((await CountCopyAsync(saip.Id)).Activities > 0, "the copy must exist for this test to mean anything");
        await InflateCopyAsync(saip.Id);

        string after;
        await using (AppDbContext ctx = _db.NewContext()) after = Json(await read(ctx, s));

        Assert.Equal(before, after);
    }

    // ── The copy ──────────────────────────────────────────────────────────────

    [SqlServerFact]
    public async Task Start_CopiesTheOfficesWholeTree_WithSourceIds_AndLeavesTheApprovedTreeUntouched()
    {
        Seeded s = await SeedAsync(2040);
        string approvedBefore = await ApprovedTreeJsonAsync(s.RecordId);

        SaipDto saip = await StartAsync(s);

        Assert.Equal(1, saip.Number);
        Assert.Equal(SaipStatus.Draft, saip.Status);
        Assert.Equal(2, saip.DraftAipOfficeIds.Count);
        // PTO only: 2 groups, 2 programs, 2 projects, 4 activities, 4 lines, 2 items. OPA is not copied.
        Assert.Equal((2, 2, 2, 4, 4, 2), await CountCopyAsync(saip.Id));

        await using AppDbContext ctx = _db.NewContext();
        List<AipOffice> copies = await ctx.AipOffices.IgnoreQueryFilters()
            .Where(o => o.SaipId == saip.Id).ToListAsync();
        Assert.Equal(s.PtoGroupIds.Order(), copies.Select(c => c.SourceId!.Value).Order());
        Assert.All(copies, c => Assert.Equal(AipWorkflowStatus.Draft, c.WorkflowStatus));

        // Every copied activity points at an approved PTO activity, with the same figures.
        List<AipActivity> viaIgnore = await ctx.AipActivities.IgnoreQueryFilters()
            .Where(a => copies.Select(c => c.Id).Contains(a.Project.Program.OfficeId)).ToListAsync();
        Assert.Equal(4, viaIgnore.Count);
        Dictionary<int, AipActivity> approved = await ctx.AipActivities
            .Where(a => viaIgnore.Select(v => v.SourceId!.Value).Contains(a.Id)).ToDictionaryAsync(a => a.Id);
        Assert.Equal(4, approved.Count);
        Assert.All(viaIgnore, c =>
        {
            AipActivity src = approved[c.SourceId!.Value];
            Assert.Equal(src.RefCode, c.RefCode);
            Assert.Equal(src.Total, c.Total);
        });

        Assert.Equal(approvedBefore, await ApprovedTreeJsonAsync(s.RecordId));
    }

    private async Task<string> ApprovedTreeJsonAsync(int recordId)
    {
        await using AppDbContext ctx = _db.NewContext();
        var rows = await ctx.AipExpenditures.AsNoTracking()
            .Where(e => e.Activity.Project.Program.Office.AipRecordId == recordId)
            .OrderBy(e => e.Id)
            .Select(e => new
            {
                e.Id, e.ActivityId, e.Mooe, e.Co, e.Total, e.SourceId,
                Group = e.Activity.Project.Program.Office.Id, e.Activity.Project.Program.Office.WorkflowStatus,
                Items = e.ProcurementItems.OrderBy(i => i.Id).Select(i => new { i.Id, i.LineTotal }).ToList(),
            })
            .ToListAsync();
        return Json(rows);
    }

    [SqlServerFact]
    public async Task Start_WhenTheCopyFailsPartWay_SavesNothing()
    {
        Seeded s = await SeedAsync(2041);

        DbContextOptions<AppDbContext> failing = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_db.ConnectionString)
            .AddInterceptors(new FailWhenCopyingInterceptor())
            .Options;

        await using (AppDbContext ctx = new(failing))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => Service(ctx).StartAsync(s.RecordId, s.PtoId, SuperAdmin(s.UserId)));
        }

        await using AppDbContext check = _db.NewContext();
        Assert.False(await check.AipSaips.AnyAsync(x => x.AipRecordId == s.RecordId));
        Assert.False(await check.AipOffices.IgnoreQueryFilters()
            .AnyAsync(o => o.AipRecordId == s.RecordId && o.SaipId != null));
    }

    /// <summary>Throws on the save that inserts the copy — after the SAIP row has been saved in the same transaction.</summary>
    private sealed class FailWhenCopyingInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<AipProgram>().Any(e => e.State == EntityState.Added))
                throw new InvalidOperationException("Simulated failure while saving the copy.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    [SqlServerFact]
    public async Task Cancel_RemovesTheWholeCopyAndNothingElse()
    {
        Seeded s = await SeedAsync(2042);
        string approvedBefore = await ApprovedTreeJsonAsync(s.RecordId);
        SaipDto saip = await StartAsync(s);

        ServiceResult<SaipDto> cancelled;
        await using (AppDbContext ctx = _db.NewContext())
            cancelled = await Service(ctx).CancelAsync(s.RecordId, s.PtoId, saip.Id, SuperAdmin(s.UserId));

        Assert.True(cancelled.IsSuccess, cancelled.Error);
        Assert.Equal(SaipStatus.Cancelled, cancelled.Value!.Status);
        Assert.Equal((0, 0, 0, 0, 0, 0), await CountCopyAsync(saip.Id));
        Assert.Equal(approvedBefore, await ApprovedTreeJsonAsync(s.RecordId));

        // The SAIP row stays, as history (decision 12).
        await using AppDbContext check = _db.NewContext();
        AipSaip row = await check.AipSaips.SingleAsync(x => x.Id == saip.Id);
        Assert.Equal(SaipStatus.Cancelled, row.Status);
        Assert.NotNull(row.CancelledAt);
    }

    [SqlServerFact]
    public async Task AfterCancel_TheNextSaipIsNumberTwo()
    {
        Seeded s = await SeedAsync(2043);
        SaipDto first = await StartAsync(s);
        await using (AppDbContext ctx = _db.NewContext())
            await Service(ctx).CancelAsync(s.RecordId, s.PtoId, first.Id, SuperAdmin(s.UserId));

        SaipDto second = await StartAsync(s);

        Assert.Equal(2, second.Number);
    }

    [SqlServerFact]
    public async Task SecondStartWhileOneIsOpen_Returns409()
    {
        Seeded s = await SeedAsync(2044);
        await StartAsync(s);

        await using AppDbContext ctx = _db.NewContext();
        ServiceResult<SaipDto> again = await Service(ctx).StartAsync(s.RecordId, s.PtoId, SuperAdmin(s.UserId));

        Assert.Equal(ServiceErrorCode.Conflict, again.Code);
        Assert.Equal("SAIP 1 is still open.", again.Error);
    }

    /// <summary>The filtered unique index on its own — what holds when two starts race past the service's check.</summary>
    [SqlServerFact]
    public async Task OneOpenIndex_RefusesASecondOpenSaip_ButAllowsClosedOnes()
    {
        Seeded s = await SeedAsync(2045);

        await using AppDbContext ctx = _db.NewContext();
        SaipRepository repo = new(ctx);
        AipSaip Row(int n, string status) => new()
        {
            AipRecordId = s.RecordId, OfficeId = s.PtoId, Number = n, Status = status,
            StartedById = s.UserId, StartedAt = DateTime.UtcNow,
        };

        await repo.AddAsync(Row(1, SaipStatus.Cancelled));
        await repo.AddAsync(Row(2, SaipStatus.Approved));
        await repo.AddAsync(Row(3, SaipStatus.Draft));
        await repo.SaveChangesAsync();   // closed ones do not count against the open one

        await repo.AddAsync(Row(4, SaipStatus.SubmittedToPpdo));
        UniqueConstraintViolationException ex =
            await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => repo.SaveChangesAsync());
        Assert.Equal("UX_aip_saips_one_open_per_office", ex.IndexName);
    }

    // ── The global query filter: every existing read ignores the copy (spec §11) ──

    [SqlServerFact]
    public Task Filter_AipEntryTree_OfficeAndChildReads()
        => AssertUnchangedByACopyAsync(2050, async (ctx, s) =>
        {
            // AIP Entry, the submit checklist and every report walk the tree this way.
            AipRepository repo = new(ctx);
            IReadOnlyList<AipOffice> offices = await repo.GetOfficesByAipIdAsync(s.RecordId);
            IReadOnlyList<AipOffice> noTracking = await repo.GetOfficesByAipIdNoTrackingAsync(s.RecordId);
            IReadOnlyList<AipOffice> byIds = await repo.GetOfficesByAipIdsAsync([s.RecordId]);
            IReadOnlyList<AipProgram> programs = await repo.GetProgramsByOfficeIdsAsync(offices.Select(o => o.Id).ToList());
            IReadOnlyList<AipProject> projects = await repo.GetProjectsByProgramIdsAsync(programs.Select(p => p.Id).ToList());
            IReadOnlyList<AipActivity> activities = await repo.GetActivitiesByProjectIdsAsync(projects.Select(p => p.Id).ToList());
            return new
            {
                Offices    = offices.Select(o => new { o.Id, o.Name, o.WorkflowStatus }),
                NoTracking = noTracking.Select(o => o.Id),
                ByIds      = byIds.Select(o => o.Id),
                Programs   = programs.Select(p => p.Id),
                Projects   = projects.Select(p => p.Id),
                Activities = activities.Select(a => new { a.Id, a.Total }),
            };
        });

    [SqlServerFact]
    public async Task Filter_GetOfficeByIdAsync_DoesNotFindACopyGroup()
    {
        Seeded s = await SeedAsync(2051);
        SaipDto saip = await StartAsync(s);

        await using AppDbContext ctx = _db.NewContext();
        AipRepository repo = new(ctx);
        // Every by-id write path in AipService walks up to its office through this call and answers
        // 404 when it is null — so no existing endpoint can reach a copy's rows.
        foreach (int copyId in saip.DraftAipOfficeIds)
            Assert.Null(await repo.GetOfficeByIdAsync(copyId));
        Assert.NotNull(await repo.GetOfficeByIdAsync(s.PtoGroupIds[0]));
    }

    [SqlServerFact]
    public Task Filter_DashboardReadiness_Rollups()
        => AssertUnchangedByACopyAsync(2052, async (ctx, s) =>
        {
            AipRepository repo = new(ctx);
            IReadOnlyList<AipOffice> offices = await repo.GetOfficesByAipIdNoTrackingAsync(s.RecordId);
            List<int> ids = offices.Select(o => o.Id).ToList();
            return new
            {
                Offices   = await repo.GetOfficeRollupsAsync(s.RecordId),
                Programs  = await repo.GetProgramRollupsAsync(ids),
                Divisions = await repo.GetDivisionRollupsAsync(ids),
            };
        });

    [SqlServerFact]
    public Task Filter_NotificationCounts()
        => AssertUnchangedByACopyAsync(2053, async (ctx, s) =>
        {
            AipRepository repo = new(ctx);
            return new
            {
                Draft        = await repo.CountOfficesAtStatusByFiscalYearAsync(AipWorkflowStatus.Draft, PlanningStatus.Draft, s.FiscalYear),
                Consolidated = await repo.CountOfficesAtStatusByFiscalYearAsync(AipWorkflowStatus.Consolidated, PlanningStatus.Draft, s.FiscalYear),
                Statuses     = await repo.GetOfficeStatusesAsync(s.PtoId, PlanningStatus.Draft, s.FiscalYear),
            };
        });

    [SqlServerFact]
    public Task Filter_ReviewSearchList()
        => AssertUnchangedByACopyAsync(2054, async (ctx, s) =>
        {
            AipReviewSearchPage page = await new AipRepository(ctx).SearchReviewNodesAsync(
                new AipReviewSearchQuery(s.RecordId, [], [], [], [], null, 0, 500));
            return new { page.TotalCount, page.Items, page.SectorCounts, page.WorkflowStatusCounts };
        });

    /// <summary>
    /// ⚠️ The ceiling and dashboard sums start from aip_expenditures and reach the office only through
    /// navigation (e.Activity.Project.Program.Office). These are the reads the filter protects by
    /// EF's navigation join, not by construction — the ones most worth a real-provider test.
    /// </summary>
    [SqlServerFact]
    public Task Filter_CeilingAndDashboardSums()
        => AssertUnchangedByACopyAsync(2055, async (ctx, s) =>
        {
            AipExpenditureRepository repo = new(ctx);
            return new
            {
                ByOfficeAndFund = await repo.SumMooeCoByConfigOfficeAndFundAsync(s.RecordId, s.PtoId, s.FundId),
                ByRecordAndFund = await repo.SumMooeCoByRecordAndFundAsync(s.RecordId, s.FundId),
                ByOffice        = await repo.SumMooeCoByConfigOfficeAsync(s.RecordId, s.PtoId),
                FundCodes       = await repo.GetFundCodesByAipRecordAsync(s.RecordId),
                OutsideOffice   = await repo.CountByFundingSourceOutsideOfficeAsync(s.FundId, s.OpaId),
            };
        });

    [SqlServerFact]
    public Task Filter_AnnexBConsolidatedSheet()
        => AssertUnchangedByACopyAsync(2056, async (ctx, s) =>
        {
            AipConsolidatedService sut = new(
                new AipRepository(ctx), new AipExpenditureRepository(ctx), new OfficeRepository(ctx),
                Mock.Of<IAipFormExcelService>(), new PermissionService(), NullLogger<AipConsolidatedService>.Instance);
            List<object?> sheets = [];
            foreach (string sector in AipSector.All)
            {
                ServiceResult<AipConsolidatedSheetDto> r = await sut.GetSheetAsync(s.FiscalYear, sector, null, SuperAdmin(s.UserId));
                Assert.True(r.IsSuccess, r.Error);
                sheets.Add(r.Value);
            }
            return sheets;
        });

    [SqlServerFact]
    public Task Filter_ExternalGsoFeed()
        => AssertUnchangedByACopyAsync(2057, async (ctx, s) =>
        {
            ExternalAipReadService sut = new(
                new AipRepository(ctx), new AipExpenditureRepository(ctx), new AuditRepository(ctx),
                new OfficeRepository(ctx), new Repository<FundingSource>(ctx), new PriceIndexItemRepository(ctx));
            ExternalAipDto? feed = await sut.GetAsync(s.FiscalYear, null);
            Assert.NotNull(feed);   // PTO and OPA are Consolidated, so they are released
            return feed with { GeneratedAt = "" };   // a clock stamp, not data: it differs run to run
        });

    [SqlServerFact]
    public Task Filter_InitialWfpReport()
        => AssertUnchangedByACopyAsync(2058, async (ctx, s) =>
        {
            WfpReportService sut = new(
                new AipRepository(ctx), new WfpRepository(ctx), Mock.Of<IWfpExpenditureService>(),
                new OfficeRepository(ctx), new Repository<Account>(ctx), new AipExpenditureRepository(ctx),
                new PermissionService(),
                new PassThroughConfigCache(offices: new Repository<Office>(ctx), funds: new Repository<FundingSource>(ctx)));
            ServiceResult<WfpReportDto> r = await sut.GetInitialReportAsync(s.PtoId, s.FiscalYear, null, SuperAdmin(s.UserId));
            Assert.True(r.IsSuccess, r.Error);
            return r.Value;
        });
}
