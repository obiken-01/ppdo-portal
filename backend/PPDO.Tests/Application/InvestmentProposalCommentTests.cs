using PPDO.Application.Common;
using PPDO.Application.DTOs.InvestmentProposal;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Comments on an Investment Proposal (v1.9.0 — PPDO-219, <c>Investment_Proposal_Comments_Spec.md</c>
/// §3 and §11). Shares the harness of <see cref="InvestmentProposalServiceTests"/> (same AIP mocks,
/// real <see cref="PermissionService"/>); only the comment store is new.
///
/// <para>
/// Red-tested (2026-10-15): the side rule in both directions, the Finalize gate and its ordering
/// before the version check. Each was removed and the named tests failed; see the PR.
/// </para>
/// </summary>
public sealed partial class InvestmentProposalServiceTests
{
    // ── Posting ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddComment_ByOfficeEncoder_IsOfficeSideAndOpen()
    {
        int id = await CreateAsync();

        ServiceResult<ProposalCommentDto> result = await _sut.AddCommentAsync(id, "  Fix the objective  ", _encoder);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("Office", result.Value!.Side);
        Assert.Equal("Fix the objective", result.Value.Body);          // trimmed
        Assert.Equal("Office Encoder", result.Value.AuthorName);
        Assert.Null(result.Value.ResolvedAt);
        Assert.Single(_commentStore.Rows);
    }

    [Fact]
    public async Task AddComment_ByPpdoReviewer_IsPpdoSide_EvenThoughTheyCannotEditTheProposal()
    {
        int id = await CreateAsync();

        ServiceResult<ProposalCommentDto> result = await _sut.AddCommentAsync(id, "Check the budget line", _reviewer);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("Ppdo", result.Value!.Side);
    }

    [Fact]
    public async Task AddComment_ByHostAdminOnAnotherOffice_IsPpdoSide()
    {
        int id = await CreateAsync();
        Assert.Equal("Ppdo", (await _sut.AddCommentAsync(id, "Note", _hostAdmin)).Value!.Side);
    }

    [Fact]
    public async Task AddComment_ByTheDepartmentHead_IsOfficeSide()
    {
        int id = await CreateAsync();
        Assert.Equal("Office", (await _sut.AddCommentAsync(id, "Note", _deptHead)).Value!.Side);
    }

    [Fact]
    public async Task AddComment_OnAFinalProposal_IsAllowed_AndTheProposalStaysFinal()
    {
        int id = await CreateAsync();
        await _sut.FinalizeAsync(id, Version(id), _encoder);

        ServiceResult<ProposalCommentDto> result = await _sut.AddCommentAsync(id, "Late remark", _reviewer);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(InvestmentProposalStatus.Final, _store.Proposals[id].Status);
    }

    [Fact]
    public async Task AddComment_OnAFinalProposal_BlocksTheNextFinalizeAfterAReopen()
    {
        int id = await CreateAsync();
        await _sut.FinalizeAsync(id, Version(id), _encoder);
        await _sut.AddCommentAsync(id, "Late remark", _reviewer);
        Assert.True((await _sut.ReopenAsync(id, Version(id), _hostAdminWithReopen())).IsSuccess);

        ServiceResult<ProposalDto> result = await _sut.FinalizeAsync(id, Version(id), _encoder);

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
        Assert.Equal("Resolve the 1 open comment before finalizing.", result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task AddComment_EmptyBody_Returns400_AndWritesNothing(string? body)
    {
        int id = await CreateAsync();

        ServiceResult<ProposalCommentDto> result = await _sut.AddCommentAsync(id, body, _encoder);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Equal(InvestmentProposalService.CommentRequiredMessage, result.Error);
        Assert.Empty(_commentStore.Rows);
    }

    [Fact]
    public async Task AddComment_2001Characters_Returns400_And2000IsAccepted()
    {
        int id = await CreateAsync();

        ServiceResult<ProposalCommentDto> tooLong = await _sut.AddCommentAsync(id, new string('x', 2001), _encoder);
        ServiceResult<ProposalCommentDto> exact   = await _sut.AddCommentAsync(id, new string('x', 2000), _encoder);

        Assert.Equal(ServiceErrorCode.BadRequest, tooLong.Code);
        Assert.Equal(InvestmentProposalService.CommentTooLongMessage, tooLong.Error);
        Assert.True(exact.IsSuccess);
        Assert.Single(_commentStore.Rows);
    }

    // ── Scope: 404 for out-of-scope and missing alike ─────────────────────────

    [Fact]
    public async Task Comments_OutOfScope_AreNotFound_ForReadPostAndResolve()
    {
        int id = await CreateAsync();
        int commentId = (await _sut.AddCommentAsync(id, "Mine", _encoder)).Value!.Id;

        foreach (User outsider in new[] { _stranger, _noOffice })
        {
            Assert.Equal(ServiceErrorCode.NotFound, (await _sut.ListCommentsAsync(id, outsider)).Code);
            ServiceResult<ProposalCommentDto> post = await _sut.AddCommentAsync(id, "Hi", outsider);
            Assert.Equal(ServiceErrorCode.NotFound, post.Code);
            Assert.Equal(InvestmentProposalService.NotFoundMessage, post.Error);
            Assert.Equal(ServiceErrorCode.NotFound, (await _sut.ResolveCommentAsync(id, commentId, outsider)).Code);
        }
        Assert.Single(_commentStore.Rows);
        Assert.Null(_commentStore.Rows[0].ResolvedAt);
    }

    [Fact]
    public async Task Comments_NoSuchProposal_AreNotFound()
    {
        Assert.Equal(ServiceErrorCode.NotFound, (await _sut.ListCommentsAsync(9999, _encoder)).Code);
        Assert.Equal(ServiceErrorCode.NotFound, (await _sut.AddCommentAsync(9999, "Hi", _encoder)).Code);
        Assert.Equal(ServiceErrorCode.NotFound, (await _sut.ResolveCommentAsync(9999, 1, _encoder)).Code);
    }

    [Fact]
    public async Task AddComment_OutOfScopeWithEmptyBody_IsStill404_NotAValidationError()
    {
        // Scope first: a 400 would confirm the proposal exists.
        int id = await CreateAsync();
        Assert.Equal(ServiceErrorCode.NotFound, (await _sut.AddCommentAsync(id, "", _stranger)).Code);
    }

    [Fact]
    public async Task ListComments_ReturnsTheThreadOldestFirst_ForBothSides()
    {
        int id = await CreateAsync();
        await _sut.AddCommentAsync(id, "first", _encoder);
        await _sut.AddCommentAsync(id, "second", _reviewer);

        IReadOnlyList<ProposalCommentDto> asOffice = (await _sut.ListCommentsAsync(id, _deptHead)).Value!;
        IReadOnlyList<ProposalCommentDto> asPpdo   = (await _sut.ListCommentsAsync(id, _reviewer)).Value!;

        Assert.Equal(["first", "second"], asOffice.Select(c => c.Body).ToArray());
        Assert.Equal(2, asPpdo.Count);
        // CanResolve follows the caller's side: the office may resolve "first", PPDO "second".
        Assert.Equal([true, false], asOffice.Select(c => c.CanResolve).ToArray());
        Assert.Equal([false, true], asPpdo.Select(c => c.CanResolve).ToArray());
    }

    // ── Resolving: the side rule, both directions ─────────────────────────────

    [Fact]
    public async Task ResolveComment_OfficeUserOnAPpdoComment_Returns403_AndLeavesItOpen()
    {
        int id = await CreateAsync();
        int commentId = (await _sut.AddCommentAsync(id, "PPDO remark", _reviewer)).Value!.Id;

        ServiceResult<ProposalCommentDto> result = await _sut.ResolveCommentAsync(id, commentId, _encoder);

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
        Assert.Equal("Only PPDO can resolve a PPDO comment.", result.Error);
        Assert.Null(_commentStore.Rows.Single().ResolvedAt);
    }

    [Fact]
    public async Task ResolveComment_PpdoUserOnAnOfficeComment_Returns403_AndLeavesItOpen()
    {
        int id = await CreateAsync();
        int commentId = (await _sut.AddCommentAsync(id, "Office remark", _encoder)).Value!.Id;

        foreach (User ppdo in new[] { _reviewer, _hostAdmin })
        {
            ServiceResult<ProposalCommentDto> result = await _sut.ResolveCommentAsync(id, commentId, ppdo);
            Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
            Assert.Equal(InvestmentProposalService.OnlyOfficeResolvesMessage, result.Error);
        }
        Assert.Null(_commentStore.Rows.Single().ResolvedAt);
    }

    [Fact]
    public async Task ResolveComment_AColleagueOnTheAuthorsSide_Resolves_WithWhoAndWhen()
    {
        int id = await CreateAsync();
        int office = (await _sut.AddCommentAsync(id, "Office remark", _encoder)).Value!.Id;
        int ppdo   = (await _sut.AddCommentAsync(id, "PPDO remark", _reviewer)).Value!.Id;

        ServiceResult<ProposalCommentDto> byHead  = await _sut.ResolveCommentAsync(id, office, _deptHead);
        ServiceResult<ProposalCommentDto> byAdmin = await _sut.ResolveCommentAsync(id, ppdo, _hostAdmin);

        Assert.True(byHead.IsSuccess, byHead.Error);
        Assert.Equal("Office Head", byHead.Value!.ResolvedByName);
        Assert.NotNull(byHead.Value.ResolvedAt);
        Assert.True(byAdmin.IsSuccess, byAdmin.Error);
        Assert.Equal("PPDO Admin", byAdmin.Value!.ResolvedByName);
    }

    [Fact]
    public async Task ResolveComment_Twice_Returns400()
    {
        int id = await CreateAsync();
        int commentId = (await _sut.AddCommentAsync(id, "Remark", _encoder)).Value!.Id;
        await _sut.ResolveCommentAsync(id, commentId, _encoder);

        ServiceResult<ProposalCommentDto> again = await _sut.ResolveCommentAsync(id, commentId, _deptHead);

        Assert.Equal(ServiceErrorCode.BadRequest, again.Code);
        Assert.Equal("This comment is already resolved.", again.Error);
    }

    [Fact]
    public async Task ResolveComment_UnderAnotherProposal_IsNotFound()
    {
        int id = await CreateAsync();
        int commentId = (await _sut.AddCommentAsync(id, "Remark", _encoder)).Value!.Id;

        ServiceResult<ProposalCommentDto> result = await _sut.ResolveCommentAsync(id, commentId + 100, _encoder);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);

        // A comment that belongs to a different proposal id is just as missing.
        _commentStore.Rows[0].ProposalId = id + 1;
        Assert.Equal(ServiceErrorCode.NotFound, (await _sut.ResolveCommentAsync(id, commentId, _encoder)).Code);
    }

    // ── The Finalize gate ─────────────────────────────────────────────────────

    [Fact]
    public async Task Finalize_WithOneOpenComment_Returns409_AndChangesNothing()
    {
        int id = await CreateAsync();
        await _sut.AddCommentAsync(id, "Fix the objective", _encoder);
        byte[] before = _store.Proposals[id].RowVersion;

        ServiceResult<ProposalDto> result = await _sut.FinalizeAsync(id, Version(id), _encoder);

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
        Assert.Equal("Resolve the 1 open comment before finalizing.", result.Error);
        Assert.Equal(InvestmentProposalStatus.Draft, _store.Proposals[id].Status);
        Assert.Equal(before, _store.Proposals[id].RowVersion);
        Assert.Null(_store.Proposals[id].SnapshotJson);
    }

    [Fact]
    public async Task Finalize_WithSeveralOpenComments_NamesTheCount()
    {
        int id = await CreateAsync();
        await _sut.AddCommentAsync(id, "one", _encoder);
        await _sut.AddCommentAsync(id, "two", _reviewer);
        await _sut.AddCommentAsync(id, "three", _reviewer);

        Assert.Equal("Resolve the 3 open comments before finalizing.",
            (await _sut.FinalizeAsync(id, Version(id), _encoder)).Error);
    }

    [Fact]
    public async Task Finalize_OpenPpdoCommentBlocksTheOffice_UntilPpdoResolvesIt()
    {
        int id = await CreateAsync();
        int commentId = (await _sut.AddCommentAsync(id, "PPDO remark", _reviewer)).Value!.Id;

        Assert.Equal(ServiceErrorCode.Conflict, (await _sut.FinalizeAsync(id, Version(id), _encoder)).Code);
        // The office cannot clear it (side rule) ...
        Assert.Equal(ServiceErrorCode.Forbidden, (await _sut.ResolveCommentAsync(id, commentId, _encoder)).Code);
        Assert.Equal(ServiceErrorCode.Conflict, (await _sut.FinalizeAsync(id, Version(id), _encoder)).Code);
        // ... PPDO can, and then Finalize goes through.
        await _sut.ResolveCommentAsync(id, commentId, _reviewer);
        Assert.True((await _sut.FinalizeAsync(id, Version(id), _encoder)).IsSuccess);
    }

    [Fact]
    public async Task Finalize_AllCommentsResolved_Succeeds()
    {
        int id = await CreateAsync();
        int a = (await _sut.AddCommentAsync(id, "office", _encoder)).Value!.Id;
        int b = (await _sut.AddCommentAsync(id, "ppdo", _reviewer)).Value!.Id;
        await _sut.ResolveCommentAsync(id, a, _encoder);
        await _sut.ResolveCommentAsync(id, b, _reviewer);

        ServiceResult<ProposalDto> result = await _sut.FinalizeAsync(id, Version(id), _encoder);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(InvestmentProposalStatus.Final, result.Value!.Status);
    }

    [Fact]
    public async Task Finalize_OpenComment_IsReportedBeforeAStaleVersion()
    {
        int id = await CreateAsync();
        await _sut.AddCommentAsync(id, "remark", _encoder);

        // A version that no longer matches: the user should hear about the comment, not a stale save.
        ServiceResult<ProposalDto> result = await _sut.FinalizeAsync(id, Convert.ToBase64String(BitConverter.GetBytes(999L)), _encoder);

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
        Assert.Equal("Resolve the 1 open comment before finalizing.", result.Error);
    }

    [Fact]
    public async Task Finalize_OnlyThisProposalsCommentsCount()
    {
        int id = await CreateAsync();
        // A comment on some other proposal must not block this one.
        _commentStore.Rows.Add(new InvestmentProposalComment { Id = 500, ProposalId = id + 50, Body = "elsewhere", CreatedAt = DateTime.UtcNow });

        Assert.True((await _sut.FinalizeAsync(id, Version(id), _encoder)).IsSuccess);
    }

    // ── Delete, list ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_DraftWithOpenComments_Succeeds()
    {
        // Spec decision 8: open comments do not block a delete. The rows go with the proposal
        // (ON DELETE CASCADE, verified on SQL Server — see the PR).
        int id = await CreateAsync();
        await _sut.AddCommentAsync(id, "remark", _encoder);

        Assert.True((await _sut.DeleteAsync(id, Version(id), _encoder)).IsSuccess);
        Assert.False(_store.Proposals.ContainsKey(id));
    }

    [Fact]
    public async Task List_RowsCarryTheirOpenCommentCount_AndResolvedOnesDoNotCount()
    {
        int withComments = await CreateAsync();
        int a = (await _sut.AddCommentAsync(withComments, "one", _encoder)).Value!.Id;
        await _sut.AddCommentAsync(withComments, "two", _reviewer);
        await _sut.ResolveCommentAsync(withComments, a, _encoder);

        _store.ListRows.Add(Row(Project, withComments));
        _store.ListRows.Add(Row(OtherProject, proposalId: null));

        ProposalListPageDto page = (await _sut.ListAsync(2028, null, null, 1, 25, _encoder)).Value!;

        Assert.Equal(1, page.Items.Single(i => i.AipProjectId == Project).OpenCommentCount);
        Assert.Equal(0, page.Items.Single(i => i.AipProjectId == OtherProject).OpenCommentCount);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ProposalProjectRow Row(int projectId, int? proposalId) => new(
        projectId, "001", "Project", 200, "001", "Program", 100, "Office", 0m,
        proposalId, proposalId is null ? null : "Draft", null, null);

    /// <summary>
    /// Reopening needs <c>CanReopenInvestmentProposal</c> for the office; the host admin has it in
    /// the fixture's default permission model, so the test reopens as them.
    /// </summary>
    private User _hostAdminWithReopen() => _hostAdmin;

    /// <summary>An in-memory <see cref="IInvestmentProposalCommentRepository"/>.</summary>
    private sealed class FakeCommentRepository : IInvestmentProposalCommentRepository
    {
        public List<InvestmentProposalComment> Rows { get; } = [];
        private int _nextId = 1;
        private readonly List<InvestmentProposalComment> _pending = [];

        public Task<IReadOnlyList<InvestmentProposalComment>> GetByProposalIdAsync(int proposalId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<InvestmentProposalComment>>(
                Rows.Where(c => c.ProposalId == proposalId).OrderBy(c => c.CreatedAt).ThenBy(c => c.Id).ToList());

        public Task<InvestmentProposalComment?> GetByIdAsync(int id, CancellationToken ct = default)
            => Task.FromResult(Rows.FirstOrDefault(c => c.Id == id));

        public Task<int> CountOpenAsync(int proposalId, CancellationToken ct = default)
            => Task.FromResult(Rows.Count(c => c.ProposalId == proposalId && c.ResolvedAt is null));

        public Task<IReadOnlyDictionary<int, int>> CountOpenByProposalIdsAsync(IReadOnlyList<int> proposalIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<int, int>>(Rows
                .Where(c => proposalIds.Contains(c.ProposalId) && c.ResolvedAt is null)
                .GroupBy(c => c.ProposalId).ToDictionary(g => g.Key, g => g.Count()));

        public Task AddAsync(InvestmentProposalComment comment, CancellationToken ct = default)
        {
            _pending.Add(comment);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct = default)
        {
            foreach (InvestmentProposalComment c in _pending) { c.Id = _nextId++; Rows.Add(c); }
            _pending.Clear();
            return Task.CompletedTask;
        }
    }
}
