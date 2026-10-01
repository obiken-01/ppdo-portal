using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.InvestmentProposal;
using PPDO.Application.Services;
using PPDO.Domain.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// <see cref="InvestmentProposalService"/> (PPDO-155): Investment_Proposal_Spec.md §3 and §11.
/// The AIP side is mocked; the proposal store is <see cref="FakeProposalRepository"/>, which bumps
/// the row version on every save and enforces the expected one, so the stale-save path is real.
/// The permission rules are the real <see cref="PermissionService"/>.
/// </summary>
public sealed class InvestmentProposalServiceTests
{
    private const int HostOffice  = 1;    // PPDO
    private const int GuestOffice = 7;    // the project's office
    private const int OtherOffice = 8;

    private const int Record2028 = 28, Record2027 = 27;
    private const int AipOfficeId = 100, OtherAipOfficeId = 101, AipOffice2027 = 102;
    private const int ProgramId = 200, Project = 300, OtherProject = 301, Project2027 = 302;
    private const int ActValidation = 401, ActProcurement = 402;

    private readonly FakeProposalRepository _store = new();
    private readonly Mock<IAipRepository> _aip = new();
    private readonly Mock<IAipExpenditureRepository> _exp = new();
    private readonly Mock<IAipDivisionLock> _lock = new();
    private readonly Mock<IInvestmentPlanningSettingsRepository> _settings = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly List<AipActivity> _activities;
    private readonly InvestmentProposalService _sut;

    private readonly User _encoder   = MakeUser("Office Encoder", UserRole.Staff, GuestOffice);
    private readonly User _deptHead  = MakeUser("Office Head", UserRole.Staff, GuestOffice, review: true);
    private readonly User _stranger  = MakeUser("Other Encoder", UserRole.Staff, OtherOffice);
    private readonly User _reviewer  = MakeUser("PPDO Reviewer", UserRole.Admin, HostOffice, host: true, reviewAll: true);
    private readonly User _hostAdmin = MakeUser("PPDO Admin", UserRole.Admin, HostOffice, host: true);
    private readonly User _noOffice  = MakeUser("Unassigned", UserRole.Staff, null);

    public InvestmentProposalServiceTests()
    {
        AipRecord r28 = new() { Id = Record2028, FiscalYear = 2028 };
        AipRecord r27 = new() { Id = Record2027, FiscalYear = 2027 };
        AipOffice office = new() { Id = AipOfficeId, AipRecordId = Record2028, OfficeId = GuestOffice, Name = "Provincial Agriculture Office", RefCode = "1000" };
        AipOffice other = new() { Id = OtherAipOfficeId, AipRecordId = Record2028, OfficeId = OtherOffice, Name = "Other Office", RefCode = "2000" };
        AipOffice old = new() { Id = AipOffice2027, AipRecordId = Record2027, OfficeId = GuestOffice, Name = "Provincial Agriculture Office", RefCode = "1000" };
        AipProgram program = new() { Id = ProgramId, OfficeId = AipOfficeId, RefCode = "001", Name = "Agricultural Productivity Program" };
        AipProgram otherProgram = new() { Id = 201, OfficeId = OtherAipOfficeId, RefCode = "001", Name = "Other Program" };
        AipProgram oldProgram = new() { Id = 202, OfficeId = AipOffice2027, RefCode = "001", Name = "Old Program" };
        AipProject project = new() { Id = Project, ProgramId = ProgramId, RefCode = "001", Name = "Rice Seed Support",
            Description = "Seed support.\nBefore the wet season.", Objective = "Raise yield & income" };
        AipProject otherProject = new() { Id = OtherProject, ProgramId = 201, RefCode = "001", Name = "Other Project" };
        AipProject oldProject = new() { Id = Project2027, ProgramId = 202, RefCode = "001", Name = "Old Project" };

        _activities =
        [
            new() { Id = ActProcurement, ProjectId = Project, RefCode = "002", Name = "Procurement of seed",
                    StartDate = "February", EndDate = "April", Mooe = 6_090_000m, Total = 6_090_000m, ImplementingOffice = "OPA/GSO" },
            new() { Id = ActValidation, ProjectId = Project, RefCode = "001", Name = "Validation of farmers",
                    StartDate = "January", EndDate = "February", Mooe = 20_200m, Total = 20_200m, ImplementingOffice = "OPA",
                    FundingSourceSnapshot = "GF", CcTypologyCode = "A1" },
        ];

        _aip.Setup(a => a.GetProjectByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => new[] { project, otherProject, oldProject }.FirstOrDefault(p => p.Id == id));
        _aip.Setup(a => a.GetProgramByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => new[] { program, otherProgram, oldProgram }.FirstOrDefault(p => p.Id == id));
        _aip.Setup(a => a.GetOfficeByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => new[] { office, other, old }.FirstOrDefault(o => o.Id == id));
        _aip.Setup(a => a.GetByIntIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => new[] { r28, r27 }.FirstOrDefault(r => r.Id == id));
        _aip.Setup(a => a.GetLatestByFiscalYearAsync(2028, It.IsAny<CancellationToken>())).ReturnsAsync(r28);
        _aip.Setup(a => a.GetOfficesByAipIdAsync(Record2028, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipOffice>)[office, other]);
        _aip.Setup(a => a.GetActivitiesByProjectIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<int> ids, CancellationToken _) =>
                (IReadOnlyList<AipActivity>)_activities.Where(a => ids.Contains(a.ProjectId)).ToList());

        AipExpenditure seedLine = new()
        {
            Id = 900, ActivityId = ActProcurement, AccountNumberSnapshot = "5020306000",
            AccountTitleSnapshot = "Agricultural and Marine Supplies Expenses", FundingSourceSnapshot = "GF",
            FundingSourceNameSnapshot = "General Fund", Mooe = 6_090_000m,
        };
        _exp.Setup(e => e.GetByActivityIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<int> ids, CancellationToken _) =>
                (IReadOnlyList<AipExpenditure>)new[] { seedLine }.Where(l => ids.Contains(l.ActivityId)).ToList());
        _exp.Setup(e => e.GetProcurementItemsByExpenditureIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProcurementItem>)
            [
                new() { Id = 1, ExpenditureId = 900, Name = "Certified inbred rice seed", UnitPrice = 1450m, Qty = 4200m, Unit = "bags", NumberOfDays = 1m },
            ]);

        Mock<IAllocationRepository> allocation = new();
        allocation.Setup(a => a.GetProgramDivisionsByOfficeIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ProgramDivision>)[]);
        _lock.Setup(l => l.LoadAsync(It.IsAny<AipOffice>(), It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AipDivisionContext.None);
        _settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new InvestmentPlanningSettings
        {
            Id = 1, PpdcName = "PPDC Name", PpdcPosition = "PPDC", LceName = "Governor Name", LcePosition = "Provincial Governor",
        });
        User[] everyone = [_encoder, _deptHead, _stranger, _reviewer, _hostAdmin, _noOffice];
        _users.Setup(u => u.GetNamesByIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Guid> ids, CancellationToken _) =>
                (IReadOnlyDictionary<Guid, string>)everyone.Where(u => ids.Contains(u.Id)).ToDictionary(u => u.Id, u => u.FullName));

        _sut = new InvestmentProposalService(
            _store, _aip.Object, _exp.Object, allocation.Object, _lock.Object, new PermissionService(),
            _settings.Object, _users.Object, new Mock<IAuditService>().Object,
            NullLogger<InvestmentProposalService>.Instance);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_PrefillsDescriptionObjectiveInputRowAndSignatories()
    {
        ProposalDto dto = (await _sut.CreateAsync(Project, _encoder)).Value!;

        Assert.Equal(InvestmentProposalStatus.Draft, dto.Status);
        Assert.Equal("<p>Seed support.<br>Before the wet season.</p>", dto.Content.Description);
        Assert.Equal("<p>Raise yield &amp; income</p>", dto.Content.GeneralObjective);
        // Activity names, one per line, in ref-code order (001 before 002).
        Assert.Equal("Validation of farmers\nProcurement of seed",
            dto.Content.Logframe.Single(l => l.Level == InvestmentProposalLogframeLevel.Input).Target);
        Assert.True(dto.Content.DirectSameAsSummary);
        Assert.Equal(InvestmentProposalSector.All, dto.Content.Benefits.Select(b => b.Sector!).ToList());
        Assert.Equal(("Prepared by", "Office Encoder"), (dto.Content.Signatories[0].Label, dto.Content.Signatories[0].Name));
        Assert.Equal(("Submitted by", "PPDC Name"), (dto.Content.Signatories[1].Label, dto.Content.Signatories[1].Name));
        Assert.Equal(("Noted by", "Governor Name"), (dto.Content.Signatories[2].Label, dto.Content.Signatories[2].Name));
        Assert.Null(dto.Content.Signatories[3].Name);   // slot 4 blank, so not printed
    }

    [Fact]
    public async Task Create_Fy2027Project_Returns400()
    {
        ServiceResult<ProposalDto> result = await _sut.CreateAsync(Project2027, _encoder);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Equal(InvestmentProposalService.FirstYearMessage, result.Error);
    }

    [Fact]
    public async Task Create_Twice_Returns409WithTheExistingId()
    {
        int id = (await _sut.CreateAsync(Project, _encoder)).Value!.Id;

        ServiceResult<ProposalDto> second = await _sut.CreateAsync(Project, _encoder);

        Assert.Equal(ServiceErrorCode.Conflict, second.Code);
        Assert.Equal(id, Assert.IsType<ProposalExistsDto>(second.ErrorDetails).ProposalId);
    }

    [Fact]
    public async Task Create_AnotherOfficesProject_Returns404()
        => Assert.Equal(ServiceErrorCode.NotFound, (await _sut.CreateAsync(Project, _stranger)).Code);

    [Fact]
    public async Task Create_CrossOfficeReviewer_Returns403()
        => Assert.Equal(ServiceErrorCode.Forbidden, (await _sut.CreateAsync(Project, _reviewer)).Code);

    [Fact]
    public async Task Create_EncoderWithNoDivisionInADivisionedOffice_Returns403()
    {
        _lock.Setup(l => l.LoadAsync(It.IsAny<AipOffice>(), It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipDivisionContext(true, false, null,
                new Dictionary<int, Division> { [5] = new() { Id = 5, Name = "Admin", IsActive = true } }, new HashSet<int>()));

        ServiceResult<ProposalDto> result = await _sut.CreateAsync(Project, _encoder);

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
        Assert.Equal(AipDivisionContext.NotAssignedMessage, result.Error);
    }

    // ── Get: scope ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_GuestStaffOfAnotherOffice_Returns404()
    {
        int id = await CreateAsync();
        ServiceResult<ProposalDto> result = await _sut.GetAsync(id, _stranger);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
        Assert.Equal(InvestmentProposalService.NotFoundMessage, result.Error);
    }

    [Fact]
    public async Task Get_UserWithNoOffice_Returns404()
        => Assert.Equal(ServiceErrorCode.NotFound, (await _sut.GetAsync(await CreateAsync(), _noOffice)).Code);

    [Fact]
    public async Task Get_CrossOfficeReviewer_ReadsButCannotEdit()
    {
        ProposalDto dto = (await _sut.GetAsync(await CreateAsync(), _reviewer)).Value!;
        Assert.False(dto.CanEdit);
        Assert.False(dto.CanReopen);
    }

    // ── Get: the AIP side ─────────────────────────────────────────────────────

    [Fact]
    public async Task Get_HeaderAndTimelines_ComeFromTheAipInCalendarOrder()
    {
        ProposalDto dto = (await _sut.GetAsync(await CreateAsync(), _encoder)).Value!;

        Assert.Equal("Agricultural Productivity Program", dto.Header.ProgramTitle);
        Assert.Equal("Provincial Agriculture Office", dto.Header.Proponent);
        Assert.Equal("January 2028", dto.Header.ScheduleStart);   // not "April", the alphabetical min
        Assert.Equal("April 2028", dto.Header.ScheduleEnd);
        Assert.Equal(6_110_200m, dto.Header.ProjectCost);          // full pesos, no rounding
        Assert.Equal("January–February 2028", dto.AipRows.Single(r => r.ActivityId == ActValidation).Timeline);
        Assert.Equal(["General Fund"], dto.AipRows.Single(r => r.ActivityId == ActProcurement).FundNames);
        // The activity with no expenditure lines is the decision-18 warning.
        Assert.Equal(ActValidation, Assert.Single(dto.Warnings.ActivitiesWithoutLines).ActivityId);
    }

    [Fact]
    public async Task Get_AppendsActivitiesWithNoStoredRow_Ungrouped_AndDropsDeletedOnes()
    {
        int id = await CreateAsync();
        InvestmentProposal stored = _store.Proposals[id];
        InvestmentProposalGroup group = new() { Id = 1, Label = "Pre-implementation" };
        stored.Groups.Add(group);
        stored.WorkPlanRows.Add(new() { Id = 1, AipActivityId = ActProcurement, GroupId = 1, SortOrder = 0 });
        stored.WorkPlanRows.Add(new() { Id = 2, AipActivityId = 999, SortOrder = 1 });   // deleted from the AIP

        IReadOnlyList<ProposalWorkPlanRowDto> plan = (await _sut.GetAsync(id, _encoder)).Value!.Content.WorkPlan;

        Assert.Equal([ActProcurement, ActValidation], plan.Select(r => r.AipActivityId!.Value).ToList());
        Assert.Equal("g1", plan[0].GroupKey);
        Assert.Null(plan[1].GroupKey);   // the new one, appended ungrouped
    }

    // ── Put ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Put_ReplacesChildCollectionsWhole()
    {
        int id = await CreateAsync();
        await SaveAsync(id, c => c with { TeamMembers = [new(null, "Ana", "F", null, null, null), new(null, "Ben", "M", null, null, null)] });

        ProposalDto after = await SaveAsync(id, c => c with { TeamMembers = [new(null, "Cara", "F", null, null, "GST")] });

        ProposalTeamMemberDto only = Assert.Single(after.Content.TeamMembers);
        Assert.Equal(("Cara", "GST"), (only.Name, only.RequiredTraining));
    }

    [Fact]
    public async Task Put_ActivityFromAnotherProject_Returns400()
    {
        int id = await CreateAsync();
        ServiceResult<ProposalDto> result = await TrySaveAsync(id, c => c with
        {
            WorkPlan = [new(null, 12345, null, null, null, null, null, null)],
        });

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("workPlan[0].aipActivityId",
            Assert.IsType<ProposalValidationErrorsDto>(result.ErrorDetails).Errors.Keys);
    }

    [Fact]
    public async Task Put_SameActivityTwice_Returns400()
    {
        int id = await CreateAsync();
        ServiceResult<ProposalDto> result = await TrySaveAsync(id, c => c with
        {
            WorkPlan = [new(null, ActValidation, null, null, null, null, null, null), new(null, ActValidation, null, null, null, null, null, null)],
        });

        Assert.Contains("workPlan[1].aipActivityId",
            Assert.IsType<ProposalValidationErrorsDto>(result.ErrorDetails).Errors.Keys);
    }

    [Fact]
    public async Task Put_RichText_IsSanitizedToTheAllowList()
    {
        int id = await CreateAsync();
        ProposalDto after = await SaveAsync(id, c => c with
        {
            Rationale = "<table><tr><td>cell</td></tr></table><script>alert(1)</script>"
                      + "<p class=\"x\" onclick=\"y()\">Hi <strong>bold</strong> <a href=\"http://x\">link</a></p>",
        });

        string html = after.Content.Rationale!;
        Assert.DoesNotContain("<table", html);
        Assert.DoesNotContain("script", html);
        Assert.DoesNotContain("alert", html);   // a script's text goes with it, not left behind as text
        Assert.DoesNotContain("onclick", html);
        Assert.DoesNotContain("class=", html);
        Assert.DoesNotContain("<a", html);
        Assert.Contains("<p>Hi <strong>bold</strong> link</p>", html);
    }

    [Fact]
    public async Task Put_DirectSameAsSummary_StoresNoDirectRows()
    {
        int id = await CreateAsync();
        ProposalDto after = await SaveAsync(id, c => c with
        {
            DirectSameAsSummary = true,
            TargetBeneficiaries = [new(null, "Direct", "Farmers", 1, 2), new(null, "Indirect", "Families", 3, 4)],
        });

        Assert.Equal(["Indirect"], after.Content.TargetBeneficiaries.Select(t => t.Kind).ToList());
    }

    [Fact]
    public async Task Put_StaleVersion_Returns409NamingWhoSaved_AndWritesNothing()
    {
        int id = await CreateAsync();
        ProposalDto loaded = (await _sut.GetAsync(id, _encoder)).Value!;
        // The department head saves first.
        await _sut.UpdateAsync(id, new UpdateProposalDto(loaded.RowVersion, loaded.Content with { ProjectLocation = "Mamburao" }), _deptHead);

        ServiceResult<ProposalDto> late = await _sut.UpdateAsync(
            id, new UpdateProposalDto(loaded.RowVersion, loaded.Content with { ProjectLocation = "Sablayan" }), _encoder);

        Assert.Equal(ServiceErrorCode.Conflict, late.Code);
        Assert.StartsWith("Office Head saved this proposal at ", late.Error);
        Assert.Equal("Office Head", Assert.IsType<ProposalConflictDto>(late.ErrorDetails).UpdatedByName);
        Assert.Equal("Mamburao", _store.Proposals[id].ProjectLocation);
    }

    [Fact]
    public async Task Put_StaleAtSaveTime_Returns409()
    {
        // Someone saves between this request's load and its save: the database catches it.
        int id = await CreateAsync();
        ProposalDto loaded = (await _sut.GetAsync(id, _encoder)).Value!;
        _store.SimulateConcurrentSaveBeforeNextSave = true;

        ServiceResult<ProposalDto> result = await _sut.UpdateAsync(
            id, new UpdateProposalDto(loaded.RowVersion, loaded.Content with { ProjectLocation = "X" }), _encoder);

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
    }

    [Fact]
    public async Task Put_WhenFinal_Returns409()
    {
        int id = await CreateAsync();
        ProposalDto final = (await _sut.FinalizeAsync(id, Version(id), _encoder)).Value!;

        ServiceResult<ProposalDto> result = await _sut.UpdateAsync(id, new UpdateProposalDto(final.RowVersion, final.Content), _encoder);

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
        Assert.Equal(InvestmentProposalService.FinalMessage, result.Error);
    }

    // ── Finalize, reopen, delete ──────────────────────────────────────────────

    [Fact]
    public async Task Finalize_FreezesTheAip_AndFlagsLaterChanges()
    {
        int id = await CreateAsync();
        ProposalDto final = (await _sut.FinalizeAsync(id, Version(id), _encoder)).Value!;
        Assert.NotNull(_store.Proposals[id].SnapshotJson);
        Assert.False(final.AipChangedSinceFinal);

        // The AIP changes after Finalize: the proposal keeps printing the finalized figures.
        _activities.Single(a => a.Id == ActValidation).Total = 99_999m;
        ProposalDto later = (await _sut.GetAsync(id, _encoder)).Value!;

        Assert.Equal(6_110_200m, later.Header.ProjectCost);
        Assert.True(later.AipChangedSinceFinal);
    }

    [Fact]
    public async Task Reopen_ByTheOfficesDepartmentHead_ClearsTheSnapshot()
    {
        int id = await CreateAsync();
        await _sut.FinalizeAsync(id, Version(id), _encoder);

        ServiceResult<ProposalDto> reopened = await _sut.ReopenAsync(id, Version(id), _deptHead);

        Assert.Equal(InvestmentProposalStatus.Draft, reopened.Value!.Status);
        Assert.Null(_store.Proposals[id].SnapshotJson);
    }

    [Fact]
    public async Task Reopen_ByAnEncoder_Returns403()
    {
        int id = await CreateAsync();
        await _sut.FinalizeAsync(id, Version(id), _encoder);
        Assert.Equal(ServiceErrorCode.Forbidden, (await _sut.ReopenAsync(id, Version(id), _encoder)).Code);
    }

    [Fact]
    public async Task Reopen_ByAHostAdmin_IsAllowed()
    {
        int id = await CreateAsync();
        await _sut.FinalizeAsync(id, Version(id), _encoder);
        Assert.True((await _sut.ReopenAsync(id, Version(id), _hostAdmin)).IsSuccess);
    }

    [Fact]
    public async Task Delete_Final_Returns409_AndDraft_Deletes()
    {
        int id = await CreateAsync();
        await _sut.FinalizeAsync(id, Version(id), _encoder);
        Assert.Equal(ServiceErrorCode.Conflict, (await _sut.DeleteAsync(id, Version(id), _encoder)).Code);

        await _sut.ReopenAsync(id, Version(id), _deptHead);
        Assert.True((await _sut.DeleteAsync(id, Version(id), _encoder)).IsSuccess);
        Assert.False(_store.Proposals.ContainsKey(id));
    }

    // ── Lists: the office pin ─────────────────────────────────────────────────

    [Fact]
    public async Task ListProjectOptions_GuestCaller_IsPinnedToTheirOwnOffice()
    {
        // Asks for another office's projects; gets their own office's query.
        await _sut.ListProjectOptionsAsync(2028, OtherOffice, _encoder);
        Assert.Equal([AipOfficeId], _store.LastQuery!.AipOfficeIds);
    }

    [Fact]
    public async Task ListProjectOptions_CallerWhoSeesEveryOffice_MustNameOne()
    {
        ServiceResult<IReadOnlyList<ProposalProjectOptionDto>> result = await _sut.ListProjectOptionsAsync(2028, null, _hostAdmin);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Equal(InvestmentProposalService.ChooseOfficeMessage, result.Error);
    }

    [Fact]
    public async Task List_UserWithNoOffice_QueriesNoOffice()
    {
        await _sut.ListAsync(2028, null, null, 1, 25, _noOffice);
        Assert.Empty(_store.LastQuery!.AipOfficeIds!);
    }

    [Fact]
    public async Task List_Fy2027_Returns400()
        => Assert.Equal(ServiceErrorCode.BadRequest, (await _sut.ListAsync(2027, null, null, 1, 25, _encoder)).Code);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<int> CreateAsync() => (await _sut.CreateAsync(Project, _encoder)).Value!.Id;

    private string Version(int id) => Convert.ToBase64String(_store.Proposals[id].RowVersion);

    private async Task<ServiceResult<ProposalDto>> TrySaveAsync(int id, Func<ProposalContentDto, ProposalContentDto> edit)
    {
        ProposalDto loaded = (await _sut.GetAsync(id, _encoder)).Value!;
        return await _sut.UpdateAsync(id, new UpdateProposalDto(loaded.RowVersion, edit(loaded.Content)), _encoder);
    }

    private async Task<ProposalDto> SaveAsync(int id, Func<ProposalContentDto, ProposalContentDto> edit)
    {
        ServiceResult<ProposalDto> result = await TrySaveAsync(id, edit);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!;
    }

    private static User MakeUser(string name, UserRole role, int? officeId,
        bool host = false, bool review = false, bool reviewAll = false) => new()
    {
        Id = Guid.NewGuid(), FullName = name, Position = "Officer", Role = role,
        OfficeId = officeId,
        Office = officeId is int id ? new Office { Id = id, OfficeCode = host ? "PPDO" : $"O{id}", IsHostOffice = host } : null,
        OverrideCanReviewBudgetPlanning = review ? true : null,
        OverrideCanReviewAllOffices     = reviewAll ? true : null,
    };

    /// <summary>
    /// An in-memory <see cref="IInvestmentProposalRepository"/>: the same entity instance is
    /// "tracked" across calls, every save bumps the version, and a save checks the expected one.
    /// </summary>
    private sealed class FakeProposalRepository : IInvestmentProposalRepository
    {
        public Dictionary<int, InvestmentProposal> Proposals { get; } = [];
        public ProposalProjectQuery? LastQuery { get; private set; }
        public bool SimulateConcurrentSaveBeforeNextSave { get; set; }

        private readonly HashSet<InvestmentProposal> _added = [];
        private readonly HashSet<InvestmentProposal> _removed = [];
        private readonly Dictionary<InvestmentProposal, byte[]> _expected = [];
        private int _nextId = 1;
        private long _version = 1;

        public Task<ProposalProjectPage> ListProjectsAsync(ProposalProjectQuery query, CancellationToken ct = default)
        {
            LastQuery = query;
            return Task.FromResult(new ProposalProjectPage([], 0));
        }

        public Task<bool> ExistsForAnyProjectAsync(IReadOnlyList<int> ids, CancellationToken ct = default)
            => Task.FromResult(Proposals.Values.Any(p => ids.Contains(p.AipProjectId)));

        public Task<IReadOnlyDictionary<string, string>> GetFundNamesByCodesAsync(IReadOnlyList<string> codes, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string> { ["GF"] = "General Fund" });

        public Task<IReadOnlyDictionary<string, string>> GetTypologyNamesByCodesAsync(IReadOnlyList<string> codes, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string> { ["A1"] = "Food security" });

        public Task<InvestmentProposal?> GetByIdAsync(int id, CancellationToken ct = default)
            => Task.FromResult(Proposals.GetValueOrDefault(id));

        public Task<InvestmentProposal?> GetByProjectIdAsync(int aipProjectId, CancellationToken ct = default)
            => Task.FromResult(Proposals.Values.FirstOrDefault(p => p.AipProjectId == aipProjectId));

        public Task<bool> ExistsForProjectAsync(int aipProjectId, CancellationToken ct = default)
            => Task.FromResult(Proposals.Values.Any(p => p.AipProjectId == aipProjectId));

        public Task AddAsync(InvestmentProposal proposal, CancellationToken ct = default)
        {
            _added.Add(proposal);
            return Task.CompletedTask;
        }

        public void Remove(InvestmentProposal proposal) => _removed.Add(proposal);

        public void SetExpectedRowVersion(InvestmentProposal proposal, byte[] expectedRowVersion)
            => _expected[proposal] = expectedRowVersion;

        public Task SaveChangesAsync(CancellationToken ct = default)
        {
            foreach (InvestmentProposal p in _added)
            {
                if (Proposals.Values.Any(x => x.AipProjectId == p.AipProjectId))
                    throw new UniqueConstraintViolationException("dup", "UX_investment_proposals_aip_project_id", new Exception());
                p.Id = _nextId++;
                p.RowVersion = NextVersion();
                Proposals[p.Id] = p;
            }
            _added.Clear();

            if (SimulateConcurrentSaveBeforeNextSave)
            {
                SimulateConcurrentSaveBeforeNextSave = false;
                foreach (InvestmentProposal p in _expected.Keys) p.RowVersion = NextVersion();
            }

            foreach ((InvestmentProposal p, byte[] expected) in _expected)
                if (!p.RowVersion.AsSpan().SequenceEqual(expected))
                {
                    _expected.Clear();
                    throw new ConcurrencyConflictException("stale", new Exception());
                }

            foreach (InvestmentProposal p in _removed) Proposals.Remove(p.Id);
            foreach (InvestmentProposal p in _expected.Keys.Where(k => !_removed.Contains(k))) p.RowVersion = NextVersion();
            _removed.Clear();
            _expected.Clear();
            return Task.CompletedTask;
        }

        private byte[] NextVersion() => BitConverter.GetBytes(++_version);
    }
}
