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
/// Inline review comments and the resolve rule (V18-53 / PPDO-71).
///
/// <para>
/// ⚠️ <b>The rule under test is the one that is easy to build backwards.</b> Only the side that
/// WROTE a comment may resolve it — not the side it is addressed to, which is the intuitive
/// reading. Both refusal directions are asserted, because getting one right and the other wrong
/// would still leave the soft re-submit gate self-marking in half the cases, and that failure is
/// silent: everything simply looks resolved.
/// </para>
///
/// <para>
/// ⚠️ <b>Commenting must work while the office is LOCKED.</b> `SubmittedToPpdo` closes content
/// writes and is precisely the state a PPDO reviewer comments in. A version that routed comments
/// through <c>AipWriteGuard</c> or <c>ReviewerWriteGuard</c> passes every other test here and fails
/// the one below.
/// </para>
/// </summary>
public sealed class AipReviewCommentServiceTests
{
    private const int RecordId = 900;
    private const int OfficeId = 7;      // the office being commented on
    private const int OtherOffice = 8;
    private const int GroupA = 950;
    private const int ProgramId = 960;
    private const int ProjectId = 970;
    private const int ActivityId = 980;

    private readonly Mock<IAipReviewCommentRepository> _repo = new();
    private readonly Mock<IAipRepository> _aipRepo = new();
    private readonly Mock<IPermissionService> _permissions = new();
    private readonly Mock<IAuditRepository> _auditRepo = new();

    /// <summary>The audit rows the mocked repository filters, the way the real query does.</summary>
    private readonly List<AuditLog> _auditRows = [];

    /// <summary>The store the mocked repository reads and writes, so create/resolve round-trip.</summary>
    private readonly List<AipReviewComment> _store = [];
    private int _nextId = 1;

    private readonly List<AipOffice> _groups =
    [
        new()
        {
            Id = GroupA, AipRecordId = RecordId, OfficeId = OfficeId,
            RefCode = "1000-000-1-01-010", Name = "PPDO", Sector = "GENERAL",
            WorkflowStatus = AipWorkflowStatus.Draft,
        },
    ];

    private AipReviewCommentService Build()
    {
        _aipRepo.Setup(r => r.GetByIntIdAsync(RecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipRecord
            {
                Id = RecordId, FiscalYear = 2028, EntrySource = "Manual",
                Status = PlanningStatus.Draft, UploadedById = Guid.NewGuid(),
                UploadedAt = DateTime.UtcNow,
            });
        _aipRepo.Setup(r => r.GetOfficesByAipIdAsync(RecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_groups);
        _aipRepo.Setup(r => r.GetOfficeByIdAsync(GroupA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _groups.FirstOrDefault(g => g.Id == GroupA));

        _aipRepo.Setup(r => r.GetProgramsByOfficeIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProgram>)
                [new AipProgram { Id = ProgramId, OfficeId = GroupA, RefCode = "…-010-001", Name = "Prog" }]);
        _aipRepo.Setup(r => r.GetProjectsByProgramIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProject>)
                [new AipProject { Id = ProjectId, ProgramId = ProgramId, RefCode = "…-010-001-001", Name = "Proj" }]);
        _aipRepo.Setup(r => r.GetActivitiesByProjectIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _activities);

        _repo.Setup(r => r.GetByOfficeIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<int> ids, CancellationToken _) =>
                _store.Where(c => ids.Contains(c.AipOfficeId)).ToList());
        _repo.Setup(r => r.GetByIntIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => _store.FirstOrDefault(c => c.Id == id));
        _repo.Setup(r => r.AddAsync(It.IsAny<AipReviewComment>(), It.IsAny<CancellationToken>()))
            .Callback((AipReviewComment c, CancellationToken _) => { c.Id = _nextId++; _store.Add(c); })
            .Returns(Task.CompletedTask);
        _repo.Setup(r => r.UpdateAsync(It.IsAny<AipReviewComment>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        // ⚠️ Filters on the ids it is GIVEN. A service that passed only Groups[0] would miss a row
        // keyed on another group, and this mock would let the test see that.
        _auditRepo.Setup(r => r.GetByRecordIdsAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<int>>(), It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string table, IReadOnlyList<int> ids, IReadOnlyList<string> actions, CancellationToken _) =>
                _auditRows
                    .Where(a => a.TableName == table && a.RecordId is int id && ids.Contains(id)
                        && actions.Contains(a.Action))
                    .OrderByDescending(a => a.ChangedAt)
                    .ToList());

        return new AipReviewCommentService(
            _repo.Object, _aipRepo.Object, _auditRepo.Object, _permissions.Object,
            NullLogger<AipReviewCommentService>.Instance);
    }

    private List<AipActivity> _activities =
    [
        new() { Id = ActivityId, ProjectId = ProjectId, RefCode = "…-010-001-001-001", Name = "Act" },
    ];

    // ── Callers ───────────────────────────────────────────────────────────────

    private User MakeUser(int? officeId, bool deptHead, bool ppdoReviewer)
    {
        User u = new()
        {
            Id = Guid.NewGuid(), Username = "u", PasswordHash = "h", FullName = "Test User",
            Role = UserRole.Staff, OfficeId = officeId,
            Office = officeId is int oid
                ? new Office { Id = oid, OfficeCode = $"O{oid}", OfficeName = "Office", IsActive = true }
                : null,
            IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        _permissions.Setup(p => p.CanReviewBudgetPlanningAsync(u, It.IsAny<CancellationToken>()))
            .ReturnsAsync(deptHead);
        _permissions.Setup(p => p.CanReviewAllOfficesAsync(u, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ppdoReviewer);
        return u;
    }

    private User Encoder()    => MakeUser(OfficeId, deptHead: false, ppdoReviewer: false);
    private User DeptHead()   => MakeUser(OfficeId, deptHead: true,  ppdoReviewer: false);
    /// <summary>A PPDO consolidated reviewer who belongs to a DIFFERENT office.</summary>
    private User PpdoReviewer() => MakeUser(OtherOffice, deptHead: false, ppdoReviewer: true);

    private static CreateAipReviewCommentDto OnActivity(string body = "Please revise this cost.")
        => new(nameof(AipCommentNodeType.Activity), ActivityId, body);

    // ── History (V18-77 / PPDO-77) ────────────────────────────────────────────

    private static readonly DateTime T0 = new(2026, 9, 3, 1, 20, 0, DateTimeKind.Utc);

    private void HandOff(
        long id, string action, string from, int hoursAfterT0,
        int groupId = GroupA, string actor = "Maria Dela Cruz", string? oldValues = null)
        => _auditRows.Add(new AuditLog
        {
            Id = id, TableName = "aip_offices", RecordId = groupId, Action = action,
            ChangedById = Guid.NewGuid(), ChangedAt = T0.AddHours(hoursAfterT0),
            OldValues = oldValues ?? $"{{\"WorkflowStatus\":\"{from}\"}}",
            ChangedBy = new User
            {
                Id = Guid.NewGuid(), Username = "actor", PasswordHash = "h", FullName = actor,
                Role = UserRole.Staff, IsActive = true, CreatedAt = T0, UpdatedAt = T0,
            },
        });

    private AipReviewComment CommentAt(int hoursAfterT0, bool resolved = false)
    {
        AipReviewComment c = new()
        {
            Id = _nextId++, AipOfficeId = GroupA, NodeType = AipCommentNodeType.Activity,
            NodeId = ActivityId, AuthorId = Guid.NewGuid(), AuthorSide = AipCommentSide.Ppdo,
            Body = $"Written at +{hoursAfterT0}h", CreatedAt = T0.AddHours(hoursAfterT0),
            ResolvedAt = resolved ? T0.AddHours(hoursAfterT0 + 1) : null,
        };
        _store.Add(c);
        return c;
    }

    /// <summary>The acceptance line in spec §10: submit, return, re-submit — one entry each, newest first.</summary>
    [Fact]
    public async Task GetHistoryAsync_ReturnedAndResubmitted_ListsEachHandOffOnceNewestFirst()
    {
        HandOff(1, AuditAction.SubmitToDeptHead, AipWorkflowStatus.Draft, 0, actor: "Ana Ramos");
        HandOff(2, AuditAction.SubmitToPpdo, AipWorkflowStatus.DepartmentReview, 24);
        HandOff(3, AuditAction.ReturnByPpdo, AipWorkflowStatus.SubmittedToPpdo, 48, actor: "Jose Santos");
        HandOff(4, AuditAction.SubmitToPpdo, AipWorkflowStatus.ReturnedByPpdo, 72);
        AipReviewCommentService sut = Build();

        ServiceResult<AipOfficeHistoryDto> result =
            await sut.GetHistoryAsync(RecordId, OfficeId, PpdoReviewer());

        Assert.True(result.IsSuccess);
        IReadOnlyList<AipHistoryEntryDto> entries = result.Value!.Entries;
        Assert.Equal([4L, 3L, 2L, 1L], entries.Select(e => e.Id));

        // ⚠️ The from-status is what makes the newest one a RE-submit on screen.
        Assert.Equal(AipWorkflowStatus.ReturnedByPpdo, entries[0].FromStatus);
        Assert.Equal(AipWorkflowStatus.SubmittedToPpdo, entries[0].ToStatus);
        Assert.Equal(AipWorkflowStatus.DepartmentReview, entries[2].FromStatus);

        Assert.Equal("Jose Santos", entries[1].ActorName);
        Assert.Equal("Ppdo", entries[1].ActorSide);
        Assert.Equal(AipWorkflowStatus.ReturnedByPpdo, entries[1].ToStatus);
        Assert.Equal("DepartmentHead", entries[0].ActorSide);
        Assert.Equal("Office", entries[3].ActorSide);
        Assert.Equal(AipWorkflowStatus.DepartmentReview, entries[3].ToStatus);
    }

    /// <summary>
    /// ⚠️ One transition, one audit row — keyed on whichever group was first when it was written. The
    /// read must look across EVERY group id, or an office whose group order shifted loses its history.
    /// </summary>
    [Fact]
    public async Task GetHistoryAsync_RowKeyedOnASecondGroup_IsStillFoundAndShownOnce()
    {
        const int groupB = 951;
        _groups.Add(new AipOffice
        {
            Id = groupB, AipRecordId = RecordId, OfficeId = OfficeId,
            RefCode = "3000-000-1-01-010", Name = "PPDO", Sector = "ECONOMIC",
            WorkflowStatus = AipWorkflowStatus.DepartmentReview,
        });
        HandOff(1, AuditAction.SubmitToDeptHead, AipWorkflowStatus.Draft, 0, groupId: groupB);
        AipReviewCommentService sut = Build();

        ServiceResult<AipOfficeHistoryDto> result =
            await sut.GetHistoryAsync(RecordId, OfficeId, PpdoReviewer());

        AipHistoryEntryDto only = Assert.Single(result.Value!.Entries);
        Assert.Equal(AuditAction.SubmitToDeptHead, only.Action);
    }

    /// <summary>
    /// A comment sits under the hand-off in force when it was written — the state the office was in —
    /// never under the next one. Anything older than every hand-off is kept, in its own bucket.
    /// </summary>
    [Fact]
    public async Task GetHistoryAsync_Comments_SitUnderTheHandOffInForceWhenWritten()
    {
        HandOff(1, AuditAction.SubmitToDeptHead, AipWorkflowStatus.Draft, 10);
        HandOff(2, AuditAction.SubmitToPpdo, AipWorkflowStatus.DepartmentReview, 20);
        HandOff(3, AuditAction.ReturnByPpdo, AipWorkflowStatus.SubmittedToPpdo, 30);
        AipReviewComment beforeAny = CommentAt(5);
        AipReviewComment whileAtPpdoA = CommentAt(21, resolved: true);
        AipReviewComment whileAtPpdoB = CommentAt(25);
        AipReviewComment atTheReturn = CommentAt(30);
        AipReviewCommentService sut = Build();

        ServiceResult<AipOfficeHistoryDto> result =
            await sut.GetHistoryAsync(RecordId, OfficeId, PpdoReviewer());

        AipOfficeHistoryDto history = result.Value!;
        Assert.Equal([beforeAny.Id], history.BeforeFirstSubmission.Select(c => c.Id));

        AipHistoryEntryDto sentToPpdo = history.Entries.Single(e => e.Id == 2);
        Assert.Equal([whileAtPpdoA.Id, whileAtPpdoB.Id], sentToPpdo.Comments.Select(c => c.Id));

        // Stamped in the same instant as the return: it belongs to the state it was written into.
        AipHistoryEntryDto returned = history.Entries.Single(e => e.Id == 3);
        Assert.Equal([atTheReturn.Id], returned.Comments.Select(c => c.Id));

        Assert.Empty(history.Entries.Single(e => e.Id == 1).Comments);
    }

    /// <summary>
    /// ⚠️ Read-only. Even the authoring side gets no resolve control in the history — resolving stays
    /// where the row is in front of the reader.
    /// </summary>
    [Fact]
    public async Task GetHistoryAsync_Comments_NeverOfferResolve()
    {
        HandOff(1, AuditAction.SubmitToPpdo, AipWorkflowStatus.DepartmentReview, 0);
        CommentAt(1);
        AipReviewCommentService sut = Build();

        ServiceResult<AipOfficeHistoryDto> result =
            await sut.GetHistoryAsync(RecordId, OfficeId, PpdoReviewer());

        AipReviewCommentDto comment = Assert.Single(Assert.Single(result.Value!.Entries).Comments);
        Assert.True(comment.ResolvedAt is null);
        Assert.False(comment.CanResolve);
    }

    /// <summary>Spec §4 as corrected: the office's own encoder reads its history, same as the comments.</summary>
    [Fact]
    public async Task GetHistoryAsync_EncoderOfTheOwnOffice_ReadsIt()
    {
        HandOff(1, AuditAction.SubmitToDeptHead, AipWorkflowStatus.Draft, 0);
        AipReviewCommentService sut = Build();

        ServiceResult<AipOfficeHistoryDto> result =
            await sut.GetHistoryAsync(RecordId, OfficeId, Encoder());

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Entries);
    }

    /// <summary>⚠️ Another office's history is a 404 worded like a missing office (PPDO-46), not a 403.</summary>
    [Fact]
    public async Task GetHistoryAsync_CallerFromAnotherOfficeWithoutReviewFlag_IsNotFound()
    {
        HandOff(1, AuditAction.SubmitToDeptHead, AipWorkflowStatus.Draft, 0);
        AipReviewCommentService sut = Build();

        ServiceResult<AipOfficeHistoryDto> result = await sut.GetHistoryAsync(
            RecordId, OfficeId, MakeUser(OtherOffice, deptHead: false, ppdoReviewer: false));

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
        Assert.Null(result.Value);
    }

    /// <summary>
    /// ⚠️ An unreadable payload leaves the from-status null — never a guess, because a guessed
    /// ReturnedByPpdo is what relabels "Sent to PPDO" as a re-submit — and never fails the read.
    /// </summary>
    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"Other\":\"x\"}")]
    public async Task GetHistoryAsync_UnreadableOldValues_LeavesFromStatusNull(string oldValues)
    {
        HandOff(1, AuditAction.SubmitToPpdo, "ignored", 0, oldValues: oldValues);
        AipReviewCommentService sut = Build();

        ServiceResult<AipOfficeHistoryDto> result =
            await sut.GetHistoryAsync(RecordId, OfficeId, PpdoReviewer());

        AipHistoryEntryDto entry = Assert.Single(result.Value!.Entries);
        Assert.Null(entry.FromStatus);
        Assert.Equal(AipWorkflowStatus.SubmittedToPpdo, entry.ToStatus);
    }

    /// <summary>An office never submitted: an empty chain and the current state, not an error.</summary>
    [Fact]
    public async Task GetHistoryAsync_NoHandOffs_ReturnsEmptyChainWithCurrentState()
    {
        AipReviewCommentService sut = Build();

        ServiceResult<AipOfficeHistoryDto> result =
            await sut.GetHistoryAsync(RecordId, OfficeId, PpdoReviewer());

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Entries);
        Assert.Empty(result.Value.BeforeFirstSubmission);
        Assert.Equal(AipWorkflowStatus.Draft, result.Value.WorkflowStatus);
    }

    // ── Creating ──────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠️ Activities only (PPDO-79, spec decision 7 as narrowed). Refused by the SERVER, not merely
    /// hidden: a comment is what the re-submit warning counts, so one the screen no longer offers a
    /// place for would still hold that count up. Asserted against the store as well as the status,
    /// because a 400 that had already written the row would pass a status-only assertion.
    /// </summary>
    [Theory]
    [InlineData(nameof(AipCommentNodeType.Program), ProgramId)]
    [InlineData(nameof(AipCommentNodeType.Project), ProjectId)]
    public async Task Create_OnAProgramOrProject_IsRejectedAndNothingIsStored(string nodeType, int nodeId)
    {
        AipReviewCommentService sut = Build();

        ServiceResult<AipReviewCommentDto> result = await sut.CreateAsync(
            RecordId, OfficeId, new CreateAipReviewCommentDto(nodeType, nodeId, "Revise this."),
            PpdoReviewer());

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("activity", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_store);
    }

    /// <summary>
    /// ⚠️ The other half of the same rule: a program comment written BEFORE it must stay readable
    /// and resolvable. That is why the enum and column were kept — narrowing create must not strand
    /// an existing unresolved comment where nobody can clear it.
    /// </summary>
    [Fact]
    public async Task ALegacyProgramComment_IsStillReadAndStillResolvable()
    {
        AipReviewCommentService sut = Build();
        _store.Add(new AipReviewComment
        {
            Id = 50, AipOfficeId = GroupA, NodeType = AipCommentNodeType.Program, NodeId = ProgramId,
            AuthorId = Guid.NewGuid(), AuthorSide = AipCommentSide.Ppdo,
            Body = "Written before activities-only.", CreatedAt = DateTime.UtcNow,
        });

        ServiceResult<AipReviewCommentsDto> read =
            await sut.GetForOfficeAsync(RecordId, OfficeId, PpdoReviewer());
        AipReviewCommentDto legacy = Assert.Single(read.Value!.Comments);
        Assert.False(legacy.IsOrphaned);
        Assert.Equal(1, read.Value.Unresolved.FromPpdo);

        ServiceResult<AipReviewCommentDto> resolved = await sut.ResolveAsync(50, PpdoReviewer());
        Assert.True(resolved.IsSuccess);
    }

    [Fact]
    public async Task Create_ByTheDepartmentHead_IsRecordedAsTheDepartmentHeadSide()
    {
        AipReviewCommentService sut = Build();

        ServiceResult<AipReviewCommentDto> result =
            await sut.CreateAsync(RecordId, OfficeId, OnActivity(), DeptHead());

        Assert.True(result.IsSuccess);
        Assert.Equal(nameof(AipCommentSide.DepartmentHead), result.Value!.AuthorSide);
        Assert.Equal("…-010-001-001-001", result.Value.NodeRefCode);
    }

    [Fact]
    public async Task Create_ByAPpdoReviewerOnAnotherOffice_IsRecordedAsThePpdoSide()
    {
        AipReviewCommentService sut = Build();

        ServiceResult<AipReviewCommentDto> result =
            await sut.CreateAsync(RecordId, OfficeId, OnActivity(), PpdoReviewer());

        Assert.True(result.IsSuccess);
        Assert.Equal(nameof(AipCommentSide.Ppdo), result.Value!.AuthorSide);
    }

    /// <summary>⚠️ The encoder reads comments and acts on them. They never write one (spec §3.1).</summary>
    [Fact]
    public async Task Create_ByAnEncoder_IsForbidden()
    {
        AipReviewCommentService sut = Build();

        ServiceResult<AipReviewCommentDto> result =
            await sut.CreateAsync(RecordId, OfficeId, OnActivity(), Encoder());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
        Assert.Empty(_store);
    }

    /// <summary>
    /// ⚠️ <b>The test a guard-routed implementation fails.</b> `SubmittedToPpdo` closes content
    /// writes — and is exactly the state in which PPDO reviews and comments. A comment is not a
    /// content write.
    /// </summary>
    [Fact]
    public async Task Create_WhileTheOfficeIsLockedAtPpdo_IsStillAllowed()
    {
        _groups[0].WorkflowStatus = AipWorkflowStatus.SubmittedToPpdo;
        AipReviewCommentService sut = Build();

        ServiceResult<AipReviewCommentDto> result =
            await sut.CreateAsync(RecordId, OfficeId, OnActivity(), PpdoReviewer());

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Create_OnANodeOutsideThisOffice_IsNotFound()
    {
        AipReviewCommentService sut = Build();

        ServiceResult<AipReviewCommentDto> result = await sut.CreateAsync(
            RecordId, OfficeId, new(nameof(AipCommentNodeType.Activity), 999_999, "x"), PpdoReviewer());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_WithAnEmptyBody_IsRejected(string body)
    {
        AipReviewCommentService sut = Build();

        ServiceResult<AipReviewCommentDto> result =
            await sut.CreateAsync(RecordId, OfficeId, OnActivity(body), PpdoReviewer());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task Create_WithABodyOverTheColumnWidth_IsRejectedRatherThanTruncated()
    {
        AipReviewCommentService sut = Build();

        ServiceResult<AipReviewCommentDto> result =
            await sut.CreateAsync(RecordId, OfficeId, OnActivity(new string('x', 2001)), PpdoReviewer());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    // ── Resolving: the rule ───────────────────────────────────────────────────

    /// <summary>
    /// ⚠️ <b>Direction one.</b> The office may not clear a comment PPDO addressed to it. Reverse
    /// this and an office re-submits with every PPDO remark marked done and none of them addressed.
    /// </summary>
    [Fact]
    public async Task Resolve_APpdoCommentByTheOffice_IsForbidden()
    {
        AipReviewCommentService sut = Build();
        int id = (await sut.CreateAsync(RecordId, OfficeId, OnActivity(), PpdoReviewer())).Value!.Id;

        foreach (User officeUser in new[] { Encoder(), DeptHead() })
        {
            ServiceResult<AipReviewCommentDto> result = await sut.ResolveAsync(id, officeUser);

            Assert.False(result.IsSuccess);
            Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
            Assert.Null(_store.Single().ResolvedAt);
        }
    }

    /// <summary>
    /// ⚠️ <b>Direction two.</b> A PPDO reviewer may not clear a department head's comment either —
    /// the rule is symmetric, not "PPDO outranks the office".
    ///
    /// ⚠️ <b>This is the test that discriminates, and the obvious one does not.</b> An encoder
    /// resolving anything is refused by "you are not a reviewer at all", so an implementation that
    /// checked only <i>whether</i> the caller is a reviewer — rather than <i>which side</i> they
    /// are — still passes the encoder case. Verified by inverting the rule: the encoder test kept
    /// passing and this one failed.
    /// </summary>
    [Fact]
    public async Task Resolve_ADepartmentHeadCommentByAPpdoReviewer_IsForbidden()
    {
        AipReviewCommentService sut = Build();
        int id = (await sut.CreateAsync(RecordId, OfficeId, OnActivity(), DeptHead())).Value!.Id;

        ServiceResult<AipReviewCommentDto> result = await sut.ResolveAsync(id, PpdoReviewer());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
        Assert.Null(_store.Single().ResolvedAt);
    }

    /// <summary>
    /// An encoder may not clear their own department head's comment.
    ///
    /// ℹ️ Weaker than it looks — this is refused because an encoder holds no side at all, so it
    /// passes even against a rule that ignores the side entirely. It is kept because the case is
    /// real and worth pinning; the assertion with teeth is the test above.
    /// </summary>
    [Fact]
    public async Task Resolve_ADepartmentHeadCommentByTheEncoder_IsForbidden()
    {
        AipReviewCommentService sut = Build();
        int id = (await sut.CreateAsync(RecordId, OfficeId, OnActivity(), DeptHead())).Value!.Id;

        ServiceResult<AipReviewCommentDto> result = await sut.ResolveAsync(id, Encoder());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
        Assert.Null(_store.Single().ResolvedAt);
    }

    [Fact]
    public async Task Resolve_ByTheAuthoringSide_Succeeds()
    {
        AipReviewCommentService sut = Build();
        int id = (await sut.CreateAsync(RecordId, OfficeId, OnActivity(), PpdoReviewer())).Value!.Id;

        // ⚠️ A DIFFERENT PPDO reviewer, not the author: the side resolves, not the individual, so
        // one reviewer going on leave cannot strand a comment.
        ServiceResult<AipReviewCommentDto> result = await sut.ResolveAsync(id, PpdoReviewer());

        Assert.True(result.IsSuccess);
        Assert.NotNull(_store.Single().ResolvedAt);
    }

    [Fact]
    public async Task Resolve_ACommentAlreadyResolved_IsRefusedRatherThanSilentlyReassigned()
    {
        AipReviewCommentService sut = Build();
        int id = (await sut.CreateAsync(RecordId, OfficeId, OnActivity(), PpdoReviewer())).Value!.Id;
        await sut.ResolveAsync(id, PpdoReviewer());
        Guid firstResolver = _store.Single().ResolvedById!.Value;

        ServiceResult<AipReviewCommentDto> second = await sut.ResolveAsync(id, PpdoReviewer());

        Assert.False(second.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, second.Code);
        Assert.Equal(firstResolver, _store.Single().ResolvedById);
    }

    /// <summary>Resolving marks; it never deletes. PPDO-77's history reads these back.</summary>
    [Fact]
    public async Task Resolve_KeepsTheCommentAndItsAuthorship()
    {
        AipReviewCommentService sut = Build();
        int id = (await sut.CreateAsync(RecordId, OfficeId, OnActivity(), PpdoReviewer())).Value!.Id;

        await sut.ResolveAsync(id, PpdoReviewer());

        ServiceResult<AipReviewCommentsDto> read =
            await sut.GetForOfficeAsync(RecordId, OfficeId, Encoder());
        Assert.Single(read.Value!.Comments);
        Assert.Equal(nameof(AipCommentSide.Ppdo), read.Value.Comments[0].AuthorSide);
        Assert.NotNull(read.Value.Comments[0].ResolvedAt);
        Assert.Equal(id, read.Value.Comments[0].Id);
    }

    // ── Reading ───────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠️ Two numbers, never one. The reader can resolve neither set themselves, so a merged total
    /// would imply an action that does not exist for them.
    /// </summary>
    [Fact]
    public async Task Get_SplitsTheUnresolvedCountByAuthoringSide()
    {
        AipReviewCommentService sut = Build();
        await sut.CreateAsync(RecordId, OfficeId, OnActivity("ppdo 1"), PpdoReviewer());
        await sut.CreateAsync(RecordId, OfficeId, OnActivity("ppdo 2"), PpdoReviewer());
        await sut.CreateAsync(RecordId, OfficeId, OnActivity("dh 1"), DeptHead());
        int resolved = (await sut.CreateAsync(RecordId, OfficeId, OnActivity("dh 2"), DeptHead())).Value!.Id;
        await sut.ResolveAsync(resolved, DeptHead());

        AipReviewCommentsDto read =
            (await sut.GetForOfficeAsync(RecordId, OfficeId, Encoder())).Value!;

        Assert.Equal(2, read.Unresolved.FromPpdo);
        Assert.Equal(1, read.Unresolved.FromDepartmentHead);
        Assert.Equal(3, read.Unresolved.Total);
        Assert.Equal(4, read.Comments.Count); // resolved ones are still returned
    }

    /// <summary>
    /// ⚠️ Anchoring is polymorphic and has no FK, so a deleted row leaves the comment dangling.
    /// It renders as orphaned rather than disappearing — a comment is part of the record of why
    /// the work changed, and deleting it with the row it questioned destroys that.
    /// </summary>
    [Fact]
    public async Task Get_WhenTheAnchoredRowWasDeleted_KeepsTheCommentAndFlagsItOrphaned()
    {
        AipReviewCommentService sut = Build();
        await sut.CreateAsync(RecordId, OfficeId, OnActivity(), PpdoReviewer());

        _activities = [];   // the activity is deleted while the office is back in Draft

        AipReviewCommentsDto read =
            (await sut.GetForOfficeAsync(RecordId, OfficeId, Encoder())).Value!;

        Assert.Single(read.Comments);
        Assert.True(read.Comments[0].IsOrphaned);
        Assert.Null(read.Comments[0].NodeRefCode);
        Assert.Equal(1, read.Unresolved.FromPpdo);   // still counts against the gate
    }

    [Fact]
    public async Task Get_TellsTheCallerWhetherTheyMayComment()
    {
        AipReviewCommentService sut = Build();

        Assert.False((await sut.GetForOfficeAsync(RecordId, OfficeId, Encoder())).Value!.CanComment);
        Assert.True((await sut.GetForOfficeAsync(RecordId, OfficeId, DeptHead())).Value!.CanComment);
        Assert.True((await sut.GetForOfficeAsync(RecordId, OfficeId, PpdoReviewer())).Value!.CanComment);
    }

    /// <summary>
    /// CanResolve is per caller and per comment — false for the side it is addressed to, so the UI
    /// hides a control the endpoint would refuse anyway.
    /// </summary>
    [Fact]
    public async Task Get_ExposesCanResolveOnlyToTheAuthoringSide()
    {
        AipReviewCommentService sut = Build();
        await sut.CreateAsync(RecordId, OfficeId, OnActivity(), PpdoReviewer());

        Assert.False((await sut.GetForOfficeAsync(RecordId, OfficeId, DeptHead())).Value!.Comments[0].CanResolve);
        Assert.False((await sut.GetForOfficeAsync(RecordId, OfficeId, Encoder())).Value!.Comments[0].CanResolve);
        Assert.True((await sut.GetForOfficeAsync(RecordId, OfficeId, PpdoReviewer())).Value!.Comments[0].CanResolve);
    }

    // ── Scope ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠️ NotFound, not Forbidden — an office a caller has no business seeing must be
    /// indistinguishable from one that does not exist (PPDO-46).
    /// </summary>
    [Fact]
    public async Task Get_ForAnotherOfficeAsAnEncoder_IsNotFound()
    {
        AipReviewCommentService sut = Build();

        ServiceResult<AipReviewCommentsDto> result =
            await sut.GetForOfficeAsync(RecordId, OtherOffice, Encoder());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    /// <summary>The cross-office reviewer reads every office — that is the flag's whole purpose.</summary>
    [Fact]
    public async Task Get_ForAnotherOfficeAsAPpdoReviewer_IsAllowed()
    {
        AipReviewCommentService sut = Build();

        ServiceResult<AipReviewCommentsDto> result =
            await sut.GetForOfficeAsync(RecordId, OfficeId, PpdoReviewer());

        Assert.True(result.IsSuccess);
    }
}
