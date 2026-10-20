using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;
using PPDO.Tests.TestHelpers;

namespace PPDO.Tests.Application;

/// <summary>
/// AIP activity comments as threads (v1.9.0 — PPDO-223, <c>AIP_Comment_Threads_Spec.md</c> §3 and §11).
/// Same harness as the rest of <see cref="AipReviewCommentServiceTests"/>.
///
/// <para>
/// ⚠️ <b>Red-tested.</b> Dropping <c>IsHead</c> from <see cref="AipReviewComment.IsUnresolved"/> fails
/// <see cref="Tally_RepliesOnAnOpenPpdoThread_CountOnlyTheHead"/> (the department head's reply counted
/// as an open department-head comment), <see cref="Tally_AfterPpdoResolvesTheHead_IsZeroDespiteTheReply"/>
/// (1 instead of 0: the reply would hold the warning up for good) and
/// <see cref="Reply_ByAnEncoder_IsStoredAsEncoderUnderTheHead_AndTheThreadStaysOpen"/> (the encoder
/// offered Resolve on their own reply). The SQL count's own filter is pinned on
/// SQL Server by <c>AipCommentThreadSqlServerTests</c>.
/// </para>
/// </summary>
public sealed partial class AipReviewCommentServiceTests
{
    private const int DivisionA = 31;
    private const int DivisionB = 32;

    private readonly Mock<INotificationAudience> _audience = new();

    /// <summary>The thread paths Build() wires: activity → project → program → group, and no divisions.</summary>
    private void SetUpThreads()
    {
        _aipRepo.Setup(r => r.GetActivityByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => _activities.FirstOrDefault(a => a.Id == id));
        _aipRepo.Setup(r => r.GetProjectByIdAsync(ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipProject { Id = ProjectId, ProgramId = ProgramId, RefCode = "…-010-001-001", Name = "Proj" });
        _aipRepo.Setup(r => r.GetProgramByIdAsync(ProgramId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipProgram { Id = ProgramId, OfficeId = GroupA, RefCode = "…-010-001", Name = "Prog" });

        if (_divisionLock.Setups.Count == 0) Divisions(AipDivisionContext.None);
    }

    /// <summary>The division context every load answers, for every group row.</summary>
    private void Divisions(AipDivisionContext div)
    {
        _divisionLock.Setup(d => d.LoadAsync(It.IsAny<AipOffice>(), It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(div);
        _divisionLock.Setup(d => d.LoadForOfficesAsync(
                It.IsAny<AipRecord>(), It.IsAny<IReadOnlyList<AipOffice>>(), It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AipRecord _, IReadOnlyList<AipOffice> offices, User _, CancellationToken _) =>
                offices.ToDictionary(o => o.Id, _ => div));
    }

    /// <summary>A divisioned office (A and B) seen by an encoder of <paramref name="callerDivision"/>.</summary>
    private static AipDivisionContext DivisionedFor(int? callerDivision, params int[] submitted)
        => new(
            hasDivisions: true, isDepartmentHead: false, callerDivisionId: callerDivision,
            new Dictionary<int, Division>
            {
                [DivisionA] = new() { Id = DivisionA, OfficeId = OfficeId, Name = "Division A", IsActive = true },
                [DivisionB] = new() { Id = DivisionB, OfficeId = OfficeId, Name = "Division B", IsActive = true },
            },
            submitted.ToHashSet());

    private static CreateAipReviewCommentDto ReplyTo(int headId, string body = "Fixed, amount corrected.")
        => new(nameof(AipCommentNodeType.Activity), ActivityId, body, headId);

    private async Task<int> PpdoThreadAsync(AipReviewCommentService sut, User? reviewer = null)
        => (await sut.CreateAsync(RecordId, OfficeId, OnActivity(), reviewer ?? PpdoReviewer())).Value!.Id;

    // ── Row 1 / row 2: the encoder replies, never starts ──────────────────────

    [Fact]
    public async Task Reply_ByAnEncoder_IsStoredAsEncoderUnderTheHead_AndTheThreadStaysOpen()
    {
        AipReviewCommentService sut = Build();
        int head = await PpdoThreadAsync(sut);

        ServiceResult<AipReviewCommentDto> result = await sut.CreateAsync(RecordId, OfficeId, ReplyTo(head), Encoder());

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(nameof(AipCommentSide.Encoder), result.Value!.AuthorSide);
        Assert.Equal(head, result.Value.ParentCommentId);
        Assert.False(result.Value.CanResolve);

        AipReviewComment stored = _store.Single(c => c.Id == result.Value.Id);
        AipReviewComment headRow = _store.Single(c => c.Id == head);
        // ⚠️ The head's anchor and group row, so every node delete takes the reply with its head.
        Assert.Equal((headRow.AipOfficeId, headRow.NodeType, headRow.NodeId), (stored.AipOfficeId, stored.NodeType, stored.NodeId));
        Assert.Null(headRow.ResolvedAt);
    }

    [Fact]
    public async Task Reply_TakesItsAnchorFromTheHead_NotFromTheRequest()
    {
        AipReviewCommentService sut = Build();
        int head = await PpdoThreadAsync(sut);

        ServiceResult<AipReviewCommentDto> result = await sut.CreateAsync(RecordId, OfficeId,
            new CreateAipReviewCommentDto(nameof(AipCommentNodeType.Program), 12345, "Done", head), Encoder());

        Assert.True(result.IsSuccess, result.Error);
        AipReviewComment stored = _store.Single(c => c.Id == result.Value!.Id);
        Assert.Equal((AipCommentNodeType.Activity, ActivityId), (stored.NodeType, stored.NodeId));
    }

    [Fact]
    public async Task Create_AThreadByAnEncoder_Is403WithTheReplyOnlySentence()
    {
        AipReviewCommentService sut = Build();

        ServiceResult<AipReviewCommentDto> result = await sut.CreateAsync(RecordId, OfficeId, OnActivity(), Encoder());

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
        Assert.Equal(AipReviewCommentService.EncoderStartsThreadMessage, result.Error);
        Assert.Empty(_store);
        Assert.Empty(_notifications.Calls);
    }

    [Fact]
    public async Task Reply_ByEitherReviewer_KeepsTheReviewersOwnSide()
    {
        AipReviewCommentService sut = Build();
        int head = await PpdoThreadAsync(sut);

        // The head as returned to its author offers them Reply straight away, like the read does.
        Assert.True(_store.Count == 1 && (await sut.GetForOfficeAsync(RecordId, OfficeId, PpdoReviewer())).Value!.Comments[0].CanReply);
        AipReviewCommentDto created = (await sut.CreateAsync(RecordId, OfficeId, OnActivity("Second"), PpdoReviewer())).Value!;
        Assert.True(created.CanReply);

        AipReviewCommentDto byHead = (await sut.CreateAsync(RecordId, OfficeId, ReplyTo(head), DeptHead())).Value!;
        AipReviewCommentDto byPpdo = (await sut.CreateAsync(RecordId, OfficeId, ReplyTo(head), PpdoReviewer())).Value!;

        Assert.Equal(nameof(AipCommentSide.DepartmentHead), byHead.AuthorSide);
        Assert.Equal(nameof(AipCommentSide.Ppdo), byPpdo.AuthorSide);
    }

    /// <summary>Decision 10 — a reply is not AIP content, so the office being at PPDO does not close it.</summary>
    [Fact]
    public async Task Reply_ByAnEncoder_WhileTheOfficeIsAtPpdo_IsAllowed()
    {
        _groups[0].WorkflowStatus = AipWorkflowStatus.SubmittedToPpdo;
        AipReviewCommentService sut = Build();
        int head = await PpdoThreadAsync(sut);

        ServiceResult<AipReviewCommentDto> result = await sut.CreateAsync(RecordId, OfficeId, ReplyTo(head), Encoder());

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(AipWorkflowStatus.SubmittedToPpdo, _groups[0].WorkflowStatus);
    }

    // ── Rows 5–7: the refusals ────────────────────────────────────────────────

    [Fact]
    public async Task Resolve_AReply_Is400()
    {
        AipReviewCommentService sut = Build();
        int head = await PpdoThreadAsync(sut);
        int reply = (await sut.CreateAsync(RecordId, OfficeId, ReplyTo(head), PpdoReviewer())).Value!.Id;

        ServiceResult<AipReviewCommentDto> result = await sut.ResolveAsync(reply, PpdoReviewer());

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Equal(AipReviewCommentService.HeadOnlyResolveMessage, result.Error);
        Assert.Null(_store.Single(c => c.Id == reply).ResolvedAt);
    }

    [Fact]
    public async Task Reply_OnAResolvedThread_Is400()
    {
        AipReviewCommentService sut = Build();
        int head = await PpdoThreadAsync(sut);
        await sut.ResolveAsync(head, PpdoReviewer());

        ServiceResult<AipReviewCommentDto> result = await sut.CreateAsync(RecordId, OfficeId, ReplyTo(head), Encoder());

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Equal(AipReviewCommentService.ThreadResolvedMessage, result.Error);
    }

    [Fact]
    public async Task Reply_OnAReviewerEditNote_Is400()
    {
        AipReviewCommentService sut = Build();
        AipReviewComment note = new()
        {
            Id = _nextId++, AipOfficeId = GroupA, NodeType = AipCommentNodeType.Activity, NodeId = ActivityId,
            AuthorId = Guid.NewGuid(), AuthorSide = AipCommentSide.Ppdo, Body = "PPDO edited Act",
            CreatedAt = DateTime.UtcNow, ResolvedAt = DateTime.UtcNow, IsReviewerEdit = true,
        };
        _store.Add(note);

        foreach (User caller in new[] { Encoder(), PpdoReviewer(), DeptHead() })
        {
            ServiceResult<AipReviewCommentDto> result = await sut.CreateAsync(RecordId, OfficeId, ReplyTo(note.Id), caller);
            Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
            Assert.Equal(AipReviewCommentService.ThreadResolvedMessage, result.Error);
        }
        Assert.False((await sut.GetForOfficeAsync(RecordId, OfficeId, PpdoReviewer())).Value!.Comments[0].CanReply);
    }

    [Fact]
    public async Task Reply_ToAReply_Is400()
    {
        AipReviewCommentService sut = Build();
        int head = await PpdoThreadAsync(sut);
        int reply = (await sut.CreateAsync(RecordId, OfficeId, ReplyTo(head), Encoder())).Value!.Id;

        ServiceResult<AipReviewCommentDto> result = await sut.CreateAsync(RecordId, OfficeId, ReplyTo(reply), PpdoReviewer());

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Equal(AipReviewCommentService.ReplyToReplyMessage, result.Error);
    }

    [Fact]
    public async Task Reply_ToAHeadOfAnotherOffice_Is400()
    {
        AipReviewCommentService sut = Build();
        AipReviewComment elsewhere = new()
        {
            Id = _nextId++, AipOfficeId = 4242, NodeType = AipCommentNodeType.Activity, NodeId = 1,
            AuthorId = Guid.NewGuid(), AuthorSide = AipCommentSide.Ppdo, Body = "Other office", CreatedAt = DateTime.UtcNow,
        };
        _store.Add(elsewhere);

        ServiceResult<AipReviewCommentDto> result = await sut.CreateAsync(RecordId, OfficeId, ReplyTo(elsewhere.Id), PpdoReviewer());
        ServiceResult<AipReviewCommentDto> missing = await sut.CreateAsync(RecordId, OfficeId, ReplyTo(99999), PpdoReviewer());

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Equal(AipReviewCommentService.ParentNotFoundMessage, result.Error);
        Assert.Equal(ServiceErrorCode.BadRequest, missing.Code);
        Assert.Single(_store);
    }

    /// <summary>An encoder of another office cannot even see this office's comments: 404, as before.</summary>
    [Fact]
    public async Task Reply_ByAnotherOfficesEncoder_Is404()
    {
        AipReviewCommentService sut = Build();
        int head = await PpdoThreadAsync(sut);

        ServiceResult<AipReviewCommentDto> result = await sut.CreateAsync(RecordId, OfficeId, ReplyTo(head),
            MakeUser(OtherOffice, deptHead: false, ppdoReviewer: false));

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    // ── Row 8: the division axis ──────────────────────────────────────────────

    [Fact]
    public async Task Reply_ByAnEncoderOfDivisionA_OnADivisionBActivity_Is403()
    {
        _activities[0].DivisionId = DivisionB;
        Divisions(DivisionedFor(DivisionA));
        AipReviewCommentService sut = Build();
        int head = await PpdoThreadAsync(sut);

        ServiceResult<AipReviewCommentDto> result = await sut.CreateAsync(RecordId, OfficeId, ReplyTo(head), Encoder());

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
        Assert.Equal("This activity belongs to Division B.", result.Error);
        Assert.False((await sut.GetForOfficeAsync(RecordId, OfficeId, Encoder())).Value!.Comments[0].CanReply);
    }

    /// <summary>Decision 10 — their own division having submitted does not stop the answer.</summary>
    [Fact]
    public async Task Reply_ByAnEncoderOnTheirOwnSubmittedDivisionsActivity_IsAllowed()
    {
        _activities[0].DivisionId = DivisionA;
        Divisions(DivisionedFor(DivisionA, submitted: DivisionA));
        AipReviewCommentService sut = Build();
        int head = await PpdoThreadAsync(sut);

        ServiceResult<AipReviewCommentDto> result = await sut.CreateAsync(RecordId, OfficeId, ReplyTo(head), Encoder());

        Assert.True(result.IsSuccess, result.Error);
        Assert.True((await sut.GetForOfficeAsync(RecordId, OfficeId, Encoder())).Value!.Comments[0].CanReply);
    }

    [Fact]
    public async Task Reply_ByAnEncoderOnAnUntaggedActivity_Is403()
    {
        _activities[0].DivisionId = null;
        Divisions(DivisionedFor(DivisionA));
        AipReviewCommentService sut = Build();
        int head = await PpdoThreadAsync(sut);

        ServiceResult<AipReviewCommentDto> result = await sut.CreateAsync(RecordId, OfficeId, ReplyTo(head), Encoder());

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
        Assert.Equal(AipDivisionContext.UntaggedActivityMessage, result.Error);
    }

    [Fact]
    public async Task Reply_ByAnEncoderWithNoDivisionInADivisionedOffice_Is403()
    {
        _activities[0].DivisionId = DivisionA;
        Divisions(DivisionedFor(callerDivision: null));
        AipReviewCommentService sut = Build();
        int head = await PpdoThreadAsync(sut);

        ServiceResult<AipReviewCommentDto> result = await sut.CreateAsync(RecordId, OfficeId, ReplyTo(head), Encoder());

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
        Assert.Equal(AipDivisionContext.NotAssignedMessage, result.Error);
    }

    [Fact]
    public async Task Reply_ByAnEncoderOnARemovedActivity_Is403_ButAReviewerMayStillAnswer()
    {
        AipReviewCommentService sut = Build();
        int head = await PpdoThreadAsync(sut);
        _activities = [];

        ServiceResult<AipReviewCommentDto> encoder = await sut.CreateAsync(RecordId, OfficeId, ReplyTo(head), Encoder());
        ServiceResult<AipReviewCommentDto> reviewer = await sut.CreateAsync(RecordId, OfficeId, ReplyTo(head), PpdoReviewer());

        Assert.Equal(ServiceErrorCode.Forbidden, encoder.Code);
        Assert.True(reviewer.IsSuccess, reviewer.Error);
    }

    // ── The read: canReply, canResolve, canComment ────────────────────────────

    [Fact]
    public async Task Get_CanReplyIsOnOpenHeadsOnly_AndCanResolveNeverOnAReply()
    {
        AipReviewCommentService sut = Build();
        int open = await PpdoThreadAsync(sut);
        int closed = await PpdoThreadAsync(sut);
        int reply = (await sut.CreateAsync(RecordId, OfficeId, ReplyTo(open), Encoder())).Value!.Id;
        await sut.ResolveAsync(closed, PpdoReviewer());

        foreach (User reader in new[] { PpdoReviewer(), Encoder() })
        {
            Dictionary<int, AipReviewCommentDto> byId = (await sut.GetForOfficeAsync(RecordId, OfficeId, reader))
                .Value!.Comments.ToDictionary(c => c.Id);
            Assert.True(byId[open].CanReply);
            Assert.False(byId[closed].CanReply);
            Assert.False(byId[reply].CanReply);
            Assert.False(byId[reply].CanResolve);
            Assert.Equal(open, byId[reply].ParentCommentId);
        }

        // ⚠️ Still no new-thread composer for the encoder.
        Assert.False((await sut.GetForOfficeAsync(RecordId, OfficeId, Encoder())).Value!.CanComment);
    }

    // ── Rows 3, 4 and 11: every open count ignores replies ────────────────────

    /// <summary>
    /// ⚠️ <b>The re-submit warning's number.</b> One open PPDO thread with an encoder reply and a
    /// department-head reply is ONE open comment, from PPDO. Red-tested: see the class remarks.
    ///
    /// <para>
    /// The encoder's reply alone would not move this tally even without the head filter, because its
    /// side is <c>Encoder</c> and the tally splits by the two reviewer sides. A reviewer's reply would,
    /// which is why both are here. (The side-blind per-row counts on the frontend are where an encoder
    /// reply alone would show: <c>aip-comment-threads.test.ts</c>.)
    /// </para>
    /// </summary>
    [Fact]
    public async Task Tally_RepliesOnAnOpenPpdoThread_CountOnlyTheHead()
    {
        AipReviewCommentService sut = Build();
        int head = await PpdoThreadAsync(sut);
        await sut.CreateAsync(RecordId, OfficeId, ReplyTo(head), Encoder());
        await sut.CreateAsync(RecordId, OfficeId, ReplyTo(head, "Please do"), DeptHead());

        AipUnresolvedCountsDto tally = (await sut.GetForOfficeAsync(RecordId, OfficeId, DeptHead())).Value!.Unresolved;

        Assert.Equal(1, tally.FromPpdo);
        Assert.Equal(0, tally.FromDepartmentHead);
        Assert.Equal(1, tally.Total);
    }

    [Fact]
    public async Task Tally_AfterPpdoResolvesTheHead_IsZeroDespiteTheReply()
    {
        AipReviewCommentService sut = Build();
        int head = await PpdoThreadAsync(sut);
        await sut.CreateAsync(RecordId, OfficeId, ReplyTo(head), Encoder());
        await sut.CreateAsync(RecordId, OfficeId, ReplyTo(head), DeptHead());

        Assert.True((await sut.ResolveAsync(head, PpdoReviewer())).IsSuccess);

        Assert.Equal(0, (await sut.GetForOfficeAsync(RecordId, OfficeId, DeptHead())).Value!.Unresolved.Total);
    }

    [Fact]
    public async Task Get_OldFlatComments_AreHeadsWithNoReplies_AndCountAsBefore()
    {
        AipReviewComment old = CommentAt(0);   // written before PPDO-223: no parent
        AipReviewCommentService sut = Build();

        AipReviewCommentsDto read = (await sut.GetForOfficeAsync(RecordId, OfficeId, PpdoReviewer())).Value!;

        AipReviewCommentDto only = Assert.Single(read.Comments);
        Assert.Null(only.ParentCommentId);
        Assert.True(only.CanReply);
        Assert.True(only.CanResolve);
        Assert.Equal(1, read.Unresolved.FromPpdo);
        Assert.Equal(old.Id, only.Id);
    }

    // ── Rows 9 and 10: the bell ───────────────────────────────────────────────

    private User Named(User u, string name) { u.FullName = name; return u; }

    private async Task<NotificationBatch?> Run(RecordingNotificationService.Call call)
        => await call.Build(_audience.Object, default);

    /// <summary>
    /// PPDO-239: a thread PPDO starts goes to the office's activity readers through AIP Entry, and to every
    /// PPDO reviewer through the review drill-down. The author is in the reviewer batch here and is
    /// dropped later by StageAsync.
    /// </summary>
    [Fact]
    public async Task Bell_NewThreadByPpdo_GoesToTheOfficesReaders_AndEveryPpdoReviewer_EachByTheirOwnLink()
    {
        User encoder = Encoder(), head = DeptHead(), reviewer = PpdoReviewer(), otherReviewer = PpdoReviewer();
        _audience.Setup(a => a.ActivityReadersAsync(
                It.Is<AipOffice>(o => o.Id == GroupA), It.Is<AipProgram>(p => p.Id == ProgramId), It.IsAny<CancellationToken>()))
            .ReturnsAsync([encoder, head]);
        _audience.Setup(a => a.CrossOfficeReviewersAsync(It.IsAny<CancellationToken>())).ReturnsAsync([reviewer, otherReviewer]);
        AipReviewCommentService sut = Build();

        await sut.CreateAsync(RecordId, OfficeId, OnActivity("Check the unit\ncost"), reviewer);

        List<RecordingNotificationService.Call> calls = _notifications.Calls.Where(c => c.Type == NotificationTypes.AipComment).ToList();
        Assert.Equal(2, calls.Count);
        Assert.All(calls, c => Assert.Same(reviewer, c.Actor));
        NotificationBatch office = (await Run(calls[0]))!;
        NotificationBatch ppdo = (await Run(calls[1]))!;

        Assert.Equal("PPDO commented on Act: \"Check the unit cost\"", office.Text);
        Assert.Equal($"/budget-planning/aip/entry?fiscalYear=2028&programId={ProgramId}&projectId={ProjectId}&activityId={ActivityId}", office.Link);
        Assert.Equal([encoder, head], office.Recipients);

        Assert.Equal(office.Text, ppdo.Text);
        Assert.Equal($"/budget-planning/aip/review?officeId={OfficeId}&fiscalYear=2028&programId={ProgramId}&projectId={ProjectId}&activityId={ActivityId}", ppdo.Link);
        Assert.Equal([reviewer, otherReviewer], ppdo.Recipients);
    }

    /// <summary>
    /// PPDO-239: PPDO commenting on PPDO's own AIP — a PPDO user who is both an office reader and a
    /// reviewer gets one entry (the AIP Entry one), not two.
    /// </summary>
    [Fact]
    public async Task Bell_NewThreadByPpdo_AUserWhoIsBothAnOfficeReaderAndAReviewer_GetsOneEntry()
    {
        User author = PpdoReviewer(), both = PpdoReviewer(), otherReviewer = PpdoReviewer(), encoder = Encoder();
        _audience.Setup(a => a.ActivityReadersAsync(It.IsAny<AipOffice>(), It.IsAny<AipProgram>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([encoder, both]);
        _audience.Setup(a => a.CrossOfficeReviewersAsync(It.IsAny<CancellationToken>())).ReturnsAsync([author, both, otherReviewer]);
        AipReviewCommentService sut = Build();

        await sut.CreateAsync(RecordId, OfficeId, OnActivity(), author);

        List<RecordingNotificationService.Call> calls = _notifications.Calls.Where(c => c.Type == NotificationTypes.AipComment).ToList();
        Assert.Equal(2, calls.Count);
        Assert.Equal([encoder, both], (await Run(calls[0]))!.Recipients);
        Assert.Equal([author, otherReviewer], (await Run(calls[1]))!.Recipients);
    }

    /// <summary>
    /// PPDO-239: a thread the office's department head starts stays in the office — one batch, to the
    /// office's activity readers. ⚠️ The reviewers below are set up so a stray reviewer batch would show.
    /// </summary>
    [Fact]
    public async Task Bell_NewThreadByTheOffice_GoesToTheOfficesReadersOnly_NeverToPpdoReviewers()
    {
        User encoder = Encoder(), head = DeptHead();
        _audience.Setup(a => a.ActivityReadersAsync(It.IsAny<AipOffice>(), It.IsAny<AipProgram>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([encoder, head]);
        _audience.Setup(a => a.CrossOfficeReviewersAsync(It.IsAny<CancellationToken>())).ReturnsAsync([PpdoReviewer(), PpdoReviewer()]);
        AipReviewCommentService sut = Build();

        await sut.CreateAsync(RecordId, OfficeId, OnActivity("Split this"), head);

        RecordingNotificationService.Call call = _notifications.Single(NotificationTypes.AipComment);
        NotificationBatch batch = (await Run(call))!;
        Assert.Equal([encoder, head], batch.Recipients);
        Assert.StartsWith("/budget-planning/aip/entry?", batch.Link);
    }

    [Fact]
    public async Task Bell_NewThreadByTheDepartmentHead_NamesThem()
    {
        _audience.Setup(a => a.ActivityReadersAsync(It.IsAny<AipOffice>(), It.IsAny<AipProgram>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        AipReviewCommentService sut = Build();

        await sut.CreateAsync(RecordId, OfficeId, OnActivity("Split this"), Named(DeptHead(), "Juan Cruz"));

        Assert.StartsWith("Juan Cruz commented on Act", (await Run(_notifications.Single(NotificationTypes.AipComment)))!.Text);
    }

    /// <summary>
    /// A reply goes to the thread's starter and earlier repliers only: the PPDO starter through the review
    /// link, the office's earlier replier through AIP Entry. ⚠️ Not the other office readers who never
    /// took part. The actor (the replier) is dropped later by StageAsync.
    /// </summary>
    [Fact]
    public async Task Bell_Reply_GoesToTheStarterAndEarlierRepliers_EachByTheirOwnLink()
    {
        User reviewer = PpdoReviewer(), encoder = Named(Encoder(), "Ana Reyes"), head = DeptHead(), bystander = Encoder();
        _audience.Setup(a => a.ActivityReadersAsync(It.IsAny<AipOffice>(), It.IsAny<AipProgram>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([encoder, head, bystander]);
        _audience.Setup(a => a.CrossOfficeReviewersAsync(It.IsAny<CancellationToken>())).ReturnsAsync([reviewer]);
        AipReviewCommentService sut = Build();
        int thread = await PpdoThreadAsync(sut, reviewer);
        await sut.CreateAsync(RecordId, OfficeId, ReplyTo(thread, "Which line?"), head);
        _notifications.Calls.Clear();

        await sut.CreateAsync(RecordId, OfficeId, ReplyTo(thread, "Fixed"), encoder);

        List<RecordingNotificationService.Call> calls = _notifications.Calls.Where(c => c.Type == NotificationTypes.AipReply).ToList();
        Assert.Equal(2, calls.Count);
        Assert.All(calls, c => Assert.Same(encoder, c.Actor));
        NotificationBatch office = (await Run(calls[0]))!;
        NotificationBatch ppdo = (await Run(calls[1]))!;

        Assert.Equal("Ana Reyes replied on Act: \"Fixed\"", office.Text);
        Assert.Equal([head], office.Recipients);   // the earlier replier; the bystander never took part
        Assert.StartsWith("/budget-planning/aip/entry?", office.Link);

        Assert.Equal([reviewer], ppdo.Recipients);  // the starter
        Assert.Equal($"/budget-planning/aip/review?officeId={OfficeId}&fiscalYear=2028&programId={ProgramId}&projectId={ProjectId}&activityId={ActivityId}", ppdo.Link);
    }

    [Fact]
    public async Task Bell_Resolved_GoesToTheStarterAndRepliers()
    {
        User reviewer = PpdoReviewer(), resolver = Named(PpdoReviewer(), "Jose Santos"), encoder = Encoder(), bystander = DeptHead();
        _audience.Setup(a => a.ActivityReadersAsync(It.IsAny<AipOffice>(), It.IsAny<AipProgram>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([encoder, bystander]);
        _audience.Setup(a => a.CrossOfficeReviewersAsync(It.IsAny<CancellationToken>())).ReturnsAsync([reviewer, resolver]);
        AipReviewCommentService sut = Build();
        int thread = await PpdoThreadAsync(sut, reviewer);
        await sut.CreateAsync(RecordId, OfficeId, ReplyTo(thread), encoder);
        _notifications.Calls.Clear();

        await sut.ResolveAsync(thread, resolver);

        List<RecordingNotificationService.Call> calls = _notifications.Calls.Where(c => c.Type == NotificationTypes.AipResolved).ToList();
        Assert.Equal(2, calls.Count);
        Assert.All(calls, c => Assert.Same(resolver, c.Actor));
        NotificationBatch office = (await Run(calls[0]))!;
        Assert.Equal("Jose Santos resolved your comment on Act", office.Text);
        Assert.Equal([encoder], office.Recipients);
        Assert.Equal([reviewer], (await Run(calls[1]))!.Recipients);   // the resolver never took part
    }

    [Fact]
    public async Task Bell_ARefusedReplyOrResolve_NotifiesNobody()
    {
        AipReviewCommentService sut = Build();
        int head = await PpdoThreadAsync(sut);
        int reply = (await sut.CreateAsync(RecordId, OfficeId, ReplyTo(head), PpdoReviewer())).Value!.Id;
        _notifications.Calls.Clear();

        await sut.CreateAsync(RecordId, OfficeId, ReplyTo(reply), Encoder());   // reply to a reply
        await sut.ResolveAsync(reply, PpdoReviewer());                          // resolve a reply
        await sut.ResolveAsync(head, DeptHead());                               // wrong side

        Assert.Empty(_notifications.Calls);
    }
}
