using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// <see cref="AipReviewService.SearchAsync"/> — the two things the service owns that the SQL does
/// not (V18-75 / PPDO-76, <c>AIP_Review_Spec.md</c> §4.1, §3.4).
///
/// <para>
/// <b>⚠️ The filter SEMANTICS are proved in <c>AipReviewSearchRepositoryTests</c>, against a real
/// database.</b> They are properties of the query, and a mocked repository could only assert that
/// values were copied into a record. What is tested here is the layer above: how raw input is
/// parsed, and how the caller's own permissions narrow it — neither of which the repository can
/// see, and both of which are security-relevant.
/// </para>
///
/// <para>
/// ⚠️ Each test asserts on the <see cref="AipReviewSearchQuery"/> that <i>reached</i> the
/// repository. That is deliberate: it is the boundary where a scope mistake becomes a leak, and
/// asserting on the returned rows instead would let a wrong filter pass whenever the fixture
/// happened to contain nothing it would have exposed.
/// </para>
/// </summary>
public sealed class AipReviewSearchTests
{
    private const int RecordId  = 900;
    private const int HostOffice = 7;
    private const int GuestOffice = 15;
    private const int OtherOffice = 16;

    private readonly Mock<IAipRepository>            _aipRepo     = new();
    private readonly Mock<IRepository<AipOffice>>    _officeRepo  = new();
    private readonly Mock<IOfficeRepository>         _officeConfigRepo = new();
    private readonly Mock<IAipExpenditureRepository> _expRepo     = new();
    private readonly Mock<IPermissionService>        _permissions = new();
    private readonly Mock<IAuditService>             _audit       = new();

    /// <summary>The query the service actually sent down, captured at the boundary.</summary>
    private AipReviewSearchQuery? _sent;

    private AipReviewService Build(bool yearIsOpen = true)
    {
        _aipRepo.Setup(r => r.GetLatestByFiscalYearAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => yearIsOpen
                ? new AipRecord
                {
                    Id = RecordId, FiscalYear = 2028, EntrySource = "Manual",
                    Status = PlanningStatus.Draft,
                    UploadedById = Guid.NewGuid(), UploadedAt = DateTime.UtcNow,
                }
                : null);

        _aipRepo.Setup(r => r.SearchReviewNodesAsync(
                It.IsAny<AipReviewSearchQuery>(), It.IsAny<CancellationToken>()))
            .Callback((AipReviewSearchQuery q, CancellationToken _) => _sent = q)
            .ReturnsAsync(new AipReviewSearchPage(
                [], 0, new Dictionary<string, int>(), new Dictionary<string, int>()));

        return new AipReviewService(
            _aipRepo.Object, _officeRepo.Object, _officeConfigRepo.Object, _expRepo.Object,
            _permissions.Object, _audit.Object, NullLogger<AipReviewService>.Instance);
    }

    private User MakeUser(int? officeId, bool hostOffice, bool ppdoReviewer)
    {
        User u = new()
        {
            Id = Guid.NewGuid(), Username = "u", PasswordHash = "h", FullName = "Test User",
            Role = UserRole.Staff, OfficeId = officeId,
            Office = officeId is int oid
                ? new Office
                {
                    Id = oid, OfficeCode = $"O{oid}", OfficeName = "Office",
                    IsHostOffice = hostOffice, IsActive = true,
                }
                : null,
            IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        _permissions.Setup(p => p.CanReviewAllOfficesAsync(u, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ppdoReviewer);
        return u;
    }

    /// <summary>A PPDO consolidated reviewer — the only caller who sees across offices.</summary>
    private User Reviewer() => MakeUser(HostOffice, hostOffice: true, ppdoReviewer: true);

    /// <summary>A guest-office encoder. No reviewer flag, one office.</summary>
    private User GuestEncoder() => MakeUser(GuestOffice, hostOffice: false, ppdoReviewer: false);

    private static AipReviewSearchRequestDto Request(
        IReadOnlyList<int>? officeIds = null,
        IReadOnlyList<string>? sectors = null,
        IReadOnlyList<string>? statuses = null,
        string? refCode = null,
        string? title = null,
        bool mine = false,
        int page = 1, int pageSize = 25)
        => new(2028, officeIds, sectors, statuses, refCode, title, mine, page, pageSize);

    // ── Parsing: the OR-list, and the field that must never be split ─────────

    [Fact]
    public async Task Search_RefCodeOrList_IsSplitIntoAFlatSetOfPrefixes()
    {
        AipReviewService sut = Build();

        await sut.SearchAsync(
            Request(refCode: "1000-000-1-01-010 OR 3000-000-1-01-010"), Reviewer());

        Assert.Equal(["1000-000-1-01-010", "3000-000-1-01-010"], _sent!.RefCodePrefixes);
    }

    [Fact]
    public async Task Search_RefCodeCommaList_IsAnEquivalentSeparator()
    {
        AipReviewService sut = Build();

        await sut.SearchAsync(Request(refCode: "1000-, 3000-"), Reviewer());

        Assert.Equal(["1000-", "3000-"], _sent!.RefCodePrefixes);
    }

    /// <summary>
    /// ⚠️ <b>The single most load-bearing parsing rule in this ticket.</b> "Aid or relief
    /// distribution" is one project name. Splitting the title the way the ref-code box is split
    /// would silently turn this into two searches and return rows nobody asked for — and it would
    /// look like a feature, not a bug.
    /// </summary>
    [Fact]
    public async Task Search_TitleContainingTheWordOr_ReachesTheQueryWhole()
    {
        AipReviewService sut = Build();

        await sut.SearchAsync(Request(title: "Aid or relief distribution"), Reviewer());

        Assert.Equal("Aid or relief distribution", _sent!.Title);
        Assert.Empty(_sent.RefCodePrefixes);
    }

    [Fact]
    public async Task Search_BlankRefCode_IsNoFilterRatherThanAnEmptyMatch()
    {
        AipReviewService sut = Build();

        await sut.SearchAsync(Request(refCode: "   "), Reviewer());

        Assert.Empty(_sent!.RefCodePrefixes);
    }

    // ── Scope: clamped, never refused (§3.4) ─────────────────────────────────

    /// <summary>
    /// ⚠️ <b>Clamped, not 403.</b> A guest office asking about someone else gets its own rows back —
    /// refusing would confirm the other office exists, which is the enumeration PPDO-46 closed off.
    /// </summary>
    [Fact]
    public async Task Search_GuestEncoderAskingAboutAnotherOffice_IsClampedToTheirOwn()
    {
        AipReviewService sut = Build();

        ServiceResult<AipReviewSearchResultDto> result =
            await sut.SearchAsync(Request(officeIds: [OtherOffice]), GuestEncoder());

        Assert.True(result.IsSuccess);                  // not a refusal
        Assert.Equal([GuestOffice], _sent!.OfficeIds);  // and not the office they asked for
    }

    [Fact]
    public async Task Search_ReviewerAskingForEveryOffice_IsNotNarrowed()
    {
        AipReviewService sut = Build();

        await sut.SearchAsync(Request(), Reviewer());

        // Empty means "no office filter" — the reviewer genuinely sees across the province.
        Assert.Empty(_sent!.OfficeIds);
    }

    /// <summary>
    /// ⚠️ A user with no office resolves to "sees nothing" (DECISION F), and an empty office list
    /// means "no filter" one layer down — so the service must short-circuit rather than pass it on.
    /// Getting this wrong shows an unassigned account every office in the province.
    /// </summary>
    [Fact]
    public async Task Search_ByAUserWithNoOffice_ReturnsNothingAndNeverQueries()
    {
        AipReviewService sut = Build();
        User unassigned = MakeUser(null, hostOffice: false, ppdoReviewer: false);

        ServiceResult<AipReviewSearchResultDto> result =
            await sut.SearchAsync(Request(), unassigned);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        Assert.Equal(0, result.Value.TotalCount);
        Assert.Null(_sent);   // the repository was never asked
    }

    // ── "Mine", resolved from the caller's own flags ─────────────────────────

    /// <summary>
    /// For a cross-office reviewer, "applicable to me" is their actual queue — the offices sitting
    /// at PPDO waiting on a decision. Anything else would just be "everything".
    /// </summary>
    [Fact]
    public async Task Search_MineAsAReviewer_MeansTheOfficesWaitingOnThem()
    {
        AipReviewService sut = Build();

        await sut.SearchAsync(Request(mine: true), Reviewer());

        Assert.Equal([AipWorkflowStatus.SubmittedToPpdo], _sent!.WorkflowStatuses);
        Assert.Empty(_sent.OfficeIds);
    }

    [Fact]
    public async Task Search_MineAsAnEncoder_MeansTheirOwnOffice()
    {
        AipReviewService sut = Build();

        await sut.SearchAsync(Request(mine: true), GuestEncoder());

        Assert.Equal([GuestOffice], _sent!.OfficeIds);
        Assert.Empty(_sent.WorkflowStatuses);
    }

    /// <summary>
    /// ⚠️ "Mine" must not overwrite a status the reviewer explicitly chose — it is a shortcut, not
    /// a mode. A reviewer who ticked "Returned" and then "mine" wants their returned work, not
    /// their pending work.
    /// </summary>
    [Fact]
    public async Task Search_MineDoesNotOverrideAnExplicitStatusFilter()
    {
        AipReviewService sut = Build();

        await sut.SearchAsync(
            Request(statuses: [AipWorkflowStatus.ReturnedByPpdo], mine: true), Reviewer());

        Assert.Equal([AipWorkflowStatus.ReturnedByPpdo], _sent!.WorkflowStatuses);
    }

    // ── Paging ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Search_PageTwo_SkipsAFullPage()
    {
        AipReviewService sut = Build();

        await sut.SearchAsync(Request(page: 3, pageSize: 20), Reviewer());

        Assert.Equal(40, _sent!.Skip);
        Assert.Equal(20, _sent.Take);
    }

    /// <summary>
    /// ⚠️ The page size is capped server-side. A client asking for everything at once would defeat
    /// the paging the slim DTO exists to make possible.
    /// </summary>
    [Fact]
    public async Task Search_AnAbsurdPageSize_IsCapped()
    {
        AipReviewService sut = Build();

        await sut.SearchAsync(Request(pageSize: 100_000), Reviewer());

        Assert.Equal(100, _sent!.Take);
    }

    [Fact]
    public async Task Search_NonsensePaging_FallsBackToTheFirstPage()
    {
        AipReviewService sut = Build();

        await sut.SearchAsync(Request(page: 0, pageSize: 0), Reviewer());

        Assert.Equal(0, _sent!.Skip);
        Assert.Equal(25, _sent.Take);
    }

    // ── A year nobody has opened ─────────────────────────────────────────────

    /// <summary>
    /// ⚠️ An empty page, not a 404. At the start of a season nobody has opened the year yet, and
    /// putting a red error in front of that ordinary situation is how a working app reads as broken.
    /// </summary>
    [Fact]
    public async Task Search_AFiscalYearWithNoRecord_IsAnEmptyPageNotAnError()
    {
        AipReviewService sut = Build(yearIsOpen: false);

        ServiceResult<AipReviewSearchResultDto> result =
            await sut.SearchAsync(Request(), Reviewer());

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        Assert.Equal(2028, result.Value.FiscalYear);
        Assert.Null(_sent);
    }
}
