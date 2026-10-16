using System.Text.Json;
using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>Implementation of <see cref="IAipReviewCommentService"/> (V18-53 / PPDO-71).</summary>
public sealed class AipReviewCommentService : IAipReviewCommentService
{
    private const int MaxBodyLength = 2000;

    // v1.9.0 — PPDO-223 (AIP_Comment_Threads_Spec.md §3). Same sentences as the proposal threads
    // (InvestmentProposalService.Comments) where the rule is the same.
    public const string EncoderStartsThreadMessage = "Encoders reply inside a reviewer's thread; they can't start one.";
    public const string ParentNotFoundMessage      = "The comment to reply to was not found on this office's AIP.";
    public const string ReplyToReplyMessage        = "Reply to the thread's first comment.";
    public const string ThreadResolvedMessage      = "This thread is resolved. Start a new one.";
    public const string HeadOnlyResolveMessage     = "Only a thread's first comment can be resolved.";
    public const string RemovedRowReplyMessage     = "The activity this thread is on has been removed, so encoders can no longer reply to it.";
    public const string NotThisOfficeReplyMessage  = "Only a reviewer or this office's encoders can reply here.";

    private readonly IAipReviewCommentRepository       _comments;
    private readonly IAipRepository                    _aipRepo;
    private readonly IAuditRepository                  _auditRepo;
    private readonly IPermissionService                _permissions;
    private readonly IAipDivisionLock                  _divisionLock;
    private readonly INotificationService              _notifications;
    private readonly ILogger<AipReviewCommentService>  _logger;

    public AipReviewCommentService(
        IAipReviewCommentRepository      comments,
        IAipRepository                   aipRepo,
        IAuditRepository                 auditRepo,
        IPermissionService               permissions,
        IAipDivisionLock                 divisionLock,
        INotificationService             notifications,
        ILogger<AipReviewCommentService> logger)
    {
        _comments      = comments;
        _aipRepo       = aipRepo;
        _auditRepo     = auditRepo;
        _permissions   = permissions;
        _divisionLock  = divisionLock;
        _notifications = notifications;
        _logger        = logger;
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    public async Task<ServiceResult<AipReviewCommentsDto>> GetForOfficeAsync(
        int aipRecordId, int officeId, User caller, CancellationToken ct = default)
    {
        CommentContext? ctx = await ResolveAsync(aipRecordId, officeId, caller, ct);
        if (ctx is null)
            return ServiceResult<AipReviewCommentsDto>.NotFound(NotFoundMessage(aipRecordId, officeId));

        IReadOnlyList<AipReviewComment> rows =
            await _comments.GetByOfficeIdsAsync(ctx.GroupIds, ct);

        IReadOnlyDictionary<int, string> refCodes = await ResolveRefCodesAsync(ctx, rows, ct);

        AipCommentSide? callerSide = await ResolveSideAsync(caller, ctx, ct);
        Func<AipReviewComment, bool> canReply = await ReplyRuleAsync(caller, ctx, callerSide, rows, ct);

        List<AipReviewCommentDto> dtos = rows
            .Select(c => Map(c, callerSide, refCodes, canReply: canReply(c)))
            .ToList();

        return ServiceResult<AipReviewCommentsDto>.Ok(new AipReviewCommentsDto(
            aipRecordId, officeId, dtos, Tally(rows), CanComment: callerSide is not null));
    }

    // ── History (V18-77 / PPDO-77) ────────────────────────────────────────────

    /// <summary>The hand-offs the chain is made of — named constants, so the filter needs no JSON.</summary>
    private static readonly IReadOnlyList<string> HandOffActions = AuditAction.AipHandOffs;

    public async Task<ServiceResult<AipOfficeHistoryDto>> GetHistoryAsync(
        int aipRecordId, int officeId, User caller, CancellationToken ct = default)
    {
        // ⚠️ The comments read's resolver, not AipReviewService's: the office's own people read this
        // too (spec §4), and the review resolver refuses anyone without the cross-office flag.
        CommentContext? ctx = await ResolveAsync(aipRecordId, officeId, caller, ct);
        if (ctx is null)
            return ServiceResult<AipOfficeHistoryDto>.NotFound(NotFoundMessage(aipRecordId, officeId));

        // ⚠️ EVERY group id, never Groups[0]. A transition writes one audit row keyed on whichever
        // group was first at the time (spec §5.2), and group order is not a promise. Sequential
        // awaits — these share one DbContext, which is not thread-safe (CLAUDE.md).
        IReadOnlyList<AuditLog> handOffs = await _auditRepo.GetByRecordIdsAsync(
            "aip_offices", ctx.GroupIds, HandOffActions, ct);
        IReadOnlyList<AipReviewComment> rows = await _comments.GetByOfficeIdsAsync(ctx.GroupIds, ct);
        IReadOnlyDictionary<int, string> refCodes = await ResolveRefCodesAsync(ctx, rows, ct);

        List<AuditLog> oldestFirst = handOffs.OrderBy(a => a.ChangedAt).ThenBy(a => a.Id).ToList();
        Dictionary<long, List<AipReviewCommentDto>> byHandOff =
            oldestFirst.ToDictionary(a => a.Id, _ => new List<AipReviewCommentDto>());
        List<AipReviewCommentDto> beforeFirst = [];

        foreach (AipReviewComment comment in rows.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id))
        {
            // ⚠️ Null caller side: the history is read-only, so no comment offers a resolve control.
            // Resolving belongs where the row is in front of the reader — the tree and the modal.
            AipReviewCommentDto dto = Map(comment, callerSide: null, refCodes);

            // The hand-off that was most recently in force when the comment was written, i.e. the
            // state the office sat in. At-or-before, so a comment stamped in the same instant as a
            // hand-off lands with the state it was written into.
            AuditLog? openedBy = oldestFirst.LastOrDefault(a => a.ChangedAt <= comment.CreatedAt);
            if (openedBy is null) beforeFirst.Add(dto);
            else byHandOff[openedBy.Id].Add(dto);
        }

        List<AipHistoryEntryDto> entries = oldestFirst
            .AsEnumerable()
            .Reverse()
            .Select(a => new AipHistoryEntryDto(
                a.Id,
                a.Action,
                ReadWorkflowStatus(a.OldValues),
                TargetOf(a.Action),
                a.ChangedBy?.FullName ?? "Unknown",
                ActorSideOf(a.Action),
                a.ChangedAt,
                byHandOff[a.Id]))
            .ToList();

        return ServiceResult<AipOfficeHistoryDto>.Ok(new AipOfficeHistoryDto(
            aipRecordId, officeId, ctx.Groups[0].WorkflowStatus, entries, beforeFirst));
    }

    /// <summary>Where each hand-off takes the office. Fixed by the action, so nothing is read from JSON.</summary>
    private static string TargetOf(string action) => action switch
    {
        AuditAction.SubmitToDeptHead => AipWorkflowStatus.DepartmentReview,
        AuditAction.SubmitToPpdo     => AipWorkflowStatus.SubmittedToPpdo,
        AuditAction.ReturnByPpdo     => AipWorkflowStatus.ReturnedByPpdo,
        AuditAction.ReopenByPpdo     => AipWorkflowStatus.ReturnedByPpdo,
        AuditAction.ReturnToEncoder  => AipWorkflowStatus.Draft,
        _                            => AipWorkflowStatus.Consolidated,
    };

    /// <summary>
    /// Who performs each hand-off <b>by rule</b>. ⚠️ Not the actor's current flags: those can change
    /// after the fact, and a history that relabelled last month's submit would be rewriting it.
    /// </summary>
    private static string ActorSideOf(string action) => action switch
    {
        AuditAction.SubmitToDeptHead                             => "Office",
        AuditAction.SubmitToPpdo or AuditAction.ReturnToEncoder  => "DepartmentHead",
        _                                                        => "Ppdo",
    };

    /// <summary>
    /// The <c>WorkflowStatus</c> out of an audit payload, for display only — the query never parses
    /// JSON. ⚠️ Null on anything unreadable rather than a guess, and never an exception: one odd row
    /// must not take the whole history down with it.
    /// </summary>
    private static string? ReadWorkflowStatus(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (JsonProperty property in doc.RootElement.EnumerateObject())
                if (string.Equals(property.Name, "WorkflowStatus", StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.String)
                    return property.Value.GetString();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ── Create ────────────────────────────────────────────────────────────────

    public async Task<ServiceResult<AipReviewCommentDto>> CreateAsync(
        int aipRecordId, int officeId, CreateAipReviewCommentDto dto, User caller,
        CancellationToken ct = default)
    {
        CommentContext? ctx = await ResolveAsync(aipRecordId, officeId, caller, ct);
        if (ctx is null)
            return ServiceResult<AipReviewCommentDto>.NotFound(NotFoundMessage(aipRecordId, officeId));

        string body = (dto.Body ?? string.Empty).Trim();
        if (body.Length == 0)
            return ServiceResult<AipReviewCommentDto>.BadRequest("A comment cannot be empty.");
        if (body.Length > MaxBodyLength)
            return ServiceResult<AipReviewCommentDto>.BadRequest(
                $"A comment cannot be longer than {MaxBodyLength} characters.");

        AipCommentSide? side = await ResolveSideAsync(caller, ctx, ct);

        // ↩️ PPDO-223 — a reply goes under an open head of this office, from a reviewer or an in-scope
        // encoder. Everything about where it sits comes from the head.
        if (dto.ParentCommentId is int parentId)
            return await ReplyAsync(aipRecordId, officeId, ctx, parentId, body, side, caller, ct);

        // ⚠️ A thread is started by a reviewer only. Refused as Forbidden rather than NotFound: this
        // caller can legitimately see the office. ↩️ PPDO-223: an encoder now replies, but still never
        // starts a thread.
        if (side is null)
            return ServiceResult<AipReviewCommentDto>.Forbidden(
                caller.OfficeId == ctx.OfficeId
                    ? EncoderStartsThreadMessage
                    : "Only a reviewer can leave a comment. Encoders act on comments and re-submit.");

        // ⚠️ Parsed, not cast. The wire carries the enum NAME (see the DTO's remarks), so an
        // unknown value is a client error rather than a silent fall-through to Program = 0.
        if (!Enum.TryParse(dto.NodeType, ignoreCase: true, out AipCommentNodeType nodeType))
            return ServiceResult<AipReviewCommentDto>.BadRequest(
                $"'{dto.NodeType}' is not a commentable row type. Expected Activity.");

        // ⚠️ Activities only (PPDO-79 — spec decision 7, narrowed 2026-09-13). Programs and projects
        // carry no figures of their own on the printed form, so a remark about one is really a
        // remark about some activity under it — and anchoring it higher hides which row has to
        // change. Refused here rather than merely hidden in the UI, because a comment is what the
        // re-submit warning counts: one the screen cannot show would still hold that count up.
        //
        // ⚠️ The enum and column keep all three values ON PURPOSE. Rows written before this rule
        // must stay readable and resolvable, so this is a create-time refusal, not a schema change.
        if (nodeType != AipCommentNodeType.Activity)
            return ServiceResult<AipReviewCommentDto>.BadRequest(
                "Only an activity can be commented on. Leave the comment on the activity that needs to change.");

        // The node must belong to THIS office. Without this a reviewer could anchor a comment to
        // another office's activity by id and it would render on a document they may not even see.
        IReadOnlyDictionary<int, string> nodes = await NodeRefCodesAsync(ctx, nodeType, ct);
        if (!nodes.TryGetValue(dto.NodeId, out string? refCode))
            return ServiceResult<AipReviewCommentDto>.NotFound(
                $"{nodeType} {dto.NodeId} not found in this office's AIP.");

        AipReviewComment comment = new()
        {
            // ⚠️ Anchored to the group row that actually holds the node, not to ctx.GroupIds[0] —
            // an office with several sub-office groups would otherwise file every comment against
            // its first group and the office's own subtree reads would still find them, but a
            // per-group read never would.
            AipOfficeId = await OwningGroupIdAsync(ctx, nodeType, dto.NodeId, ct),
            NodeType    = nodeType,
            NodeId      = dto.NodeId,
            AuthorId    = caller.Id,
            AuthorSide  = side.Value,
            Body        = body,
            CreatedAt   = DateTime.UtcNow,
        };

        await _comments.AddAsync(comment, ct);
        // PPDO-223 — a new thread tells the office's users who can open the activity, in the same save.
        await StageThreadStartedAsync(ctx, comment, side.Value, caller, ct);
        await _comments.SaveChangesAsync(ct);

        _logger.LogInformation(
            "AIP review comment added. AipRecordId: {AipRecordId}, OfficeId: {OfficeId}, "
            + "NodeType: {NodeType}, NodeId: {NodeId}, Side: {Side}, UserId: {UserId}",
            aipRecordId, officeId, nodeType, dto.NodeId, side.Value, caller.Id);

        return ServiceResult<AipReviewCommentDto>.Ok(
            Map(comment, side, new Dictionary<int, string> { [dto.NodeId] = refCode },
                authorNameOverride: caller.FullName));
    }

    // ── Resolve ───────────────────────────────────────────────────────────────

    public async Task<ServiceResult<AipReviewCommentDto>> ResolveAsync(
        int commentId, User caller, CancellationToken ct = default)
    {
        AipReviewComment? comment = await _comments.GetByIntIdAsync(commentId, ct);
        if (comment is null)
            return ServiceResult<AipReviewCommentDto>.NotFound($"Comment {commentId} not found.");

        AipOffice? group = await _aipRepo.GetOfficeByIdAsync(comment.AipOfficeId, ct);
        if (group is null)
            return ServiceResult<AipReviewCommentDto>.NotFound($"Comment {commentId} not found.");

        CommentContext? ctx = await ResolveAsync(group.AipRecordId, group.OfficeId ?? 0, caller, ct);
        if (ctx is null)
            return ServiceResult<AipReviewCommentDto>.NotFound($"Comment {commentId} not found.");

        // PPDO-223 — resolving closes a THREAD, so it is the head's act. A reply is never resolved on
        // its own (and is stored unresolved for good, which is why every count skips replies).
        if (!comment.IsHead)
            return ServiceResult<AipReviewCommentDto>.BadRequest(HeadOnlyResolveMessage);

        // ⚠️ THE RULE. Only the side that wrote it may clear it — never the side it is addressed
        // to, however senior they are. Reversing this makes the re-submit gate self-marking, and
        // the failure is silent: everything simply looks resolved.
        AipCommentSide? side = await ResolveSideAsync(caller, ctx, ct);
        if (side != comment.AuthorSide)
            return ServiceResult<AipReviewCommentDto>.Forbidden(
                comment.AuthorSide == AipCommentSide.Ppdo
                    ? "Only a PPDO reviewer can resolve a PPDO comment."
                    : "Only the department-head reviewer can resolve their own comment.");

        // Refused rather than treated as success: a second click must not quietly reassign who
        // cleared it, and the history is what "Show History" reads back.
        if (comment.ResolvedAt is not null)
            return ServiceResult<AipReviewCommentDto>.BadRequest("This comment is already resolved.");

        comment.ResolvedAt   = DateTime.UtcNow;
        comment.ResolvedById = caller.Id;
        await _comments.UpdateAsync(comment, ct);
        // PPDO-223 — the thread's starter and repliers are told, before the save.
        await StageParticipantsAsync(NotificationTypes.AipResolved, ctx, comment, caller,
            activity => NotificationText.AipResolved(caller.FullName, activity), ct);
        await _comments.SaveChangesAsync(ct);

        _logger.LogInformation(
            "AIP review comment resolved. CommentId: {CommentId}, Side: {Side}, UserId: {UserId}",
            commentId, comment.AuthorSide, caller.Id);

        return ServiceResult<AipReviewCommentDto>.Ok(
            Map(comment, side, new Dictionary<int, string>(), resolvedNameOverride: caller.FullName));
    }

    // ── Mark as seen (v1.9.0 — PPDO-213) ──────────────────────────────────────

    /// <summary>
    /// The edited office marks every PPDO reviewer edit it has not yet seen as seen.
    ///
    /// <para>
    /// ⚠️ <b>Seeing the office is not enough; it has to be yours.</b> A PPDO reviewer and a host-office
    /// user both pass the read scope for any office, and both are refused here with a 403: the
    /// acknowledgement says "the office has seen what PPDO changed", which only the office can say.
    /// Anyone who cannot even see the office gets the usual not-found (PPDO-46).
    /// </para>
    /// </summary>
    public async Task<ServiceResult<AipReviewerEditsAcknowledgedDto>> AcknowledgeReviewerEditsAsync(
        int aipRecordId, int officeId, User caller, CancellationToken ct = default)
    {
        CommentContext? ctx = await ResolveAsync(aipRecordId, officeId, caller, ct);
        if (ctx is null)
            return ServiceResult<AipReviewerEditsAcknowledgedDto>.NotFound(NotFoundMessage(aipRecordId, officeId));

        if (caller.OfficeId != officeId)
            return ServiceResult<AipReviewerEditsAcknowledgedDto>.Forbidden(
                "Only the office whose AIP was edited can mark PPDO's changes as seen.");

        int stamped = await _comments.AcknowledgeReviewerEditsAsync(ctx.GroupIds, caller.Id, DateTime.UtcNow, ct);

        _logger.LogInformation(
            "PPDO reviewer edits acknowledged. AipRecordId: {AipRecordId}, OfficeId: {OfficeId}, Count: {Count}, UserId: {UserId}",
            aipRecordId, officeId, stamped, caller.Id);

        return ServiceResult<AipReviewerEditsAcknowledgedDto>.Ok(new AipReviewerEditsAcknowledgedDto(stamped));
    }

    // ── Replies (v1.9.0 — PPDO-223) ───────────────────────────────────────────

    private async Task<ServiceResult<AipReviewCommentDto>> ReplyAsync(
        int aipRecordId, int officeId, CommentContext ctx, int parentId, string body,
        AipCommentSide? reviewerSide, User caller, CancellationToken ct)
    {
        // ⚠️ 400, not 404, for a head of another office (spec §4): the caller can see THIS office, so
        // the request is malformed rather than prying. A missing id reads the same.
        AipReviewComment? head = await _comments.GetByIntIdAsync(parentId, ct);
        if (head is null || !ctx.GroupIds.Contains(head.AipOfficeId))
            return ServiceResult<AipReviewCommentDto>.BadRequest(ParentNotFoundMessage);
        if (!head.IsHead)
            return ServiceResult<AipReviewCommentDto>.BadRequest(ReplyToReplyMessage);
        // ⚠️ Includes every reviewer-edit note: those are created resolved (PPDO-213), so they take
        // no replies by this same rule rather than by a special case.
        if (head.ResolvedAt is not null)
            return ServiceResult<AipReviewCommentDto>.BadRequest(ThreadResolvedMessage);

        AipCommentSide side;
        if (reviewerSide is AipCommentSide reviewer)
        {
            side = reviewer;
        }
        else
        {
            // ⚠️ An encoder replies only where their AIP Entry scope reaches (spec decision 9): their
            // own office, and in a divisioned office only their own division's activity. The
            // workflow state is NOT checked — a reply is not AIP content (decision 10).
            string? refusal = await RefuseEncoderReplyAsync(caller, ctx, head, ct);
            if (refusal is not null) return ServiceResult<AipReviewCommentDto>.Forbidden(refusal);
            side = AipCommentSide.Encoder;
        }

        AipReviewComment reply = new()
        {
            // The head's anchor and group row, so the node delete (PPDO-88) and the office-row
            // cascade take the whole thread with the head.
            AipOfficeId     = head.AipOfficeId,
            NodeType        = head.NodeType,
            NodeId          = head.NodeId,
            ParentCommentId = head.Id,
            AuthorId        = caller.Id,
            AuthorSide      = side,
            Body            = body,
            CreatedAt       = DateTime.UtcNow,
        };

        // Participants are read before the reply is added, so the replier (the actor, dropped anyway)
        // is not counted twice.
        await StageParticipantsAsync(NotificationTypes.AipReply, ctx, head, caller,
            activity => NotificationText.AipReply(caller.FullName, activity, body), ct);
        await _comments.AddAsync(reply, ct);
        await _comments.SaveChangesAsync(ct);

        _logger.LogInformation(
            "AIP review comment reply added. AipRecordId: {AipRecordId}, OfficeId: {OfficeId}, "
            + "ParentCommentId: {ParentCommentId}, Side: {Side}, UserId: {UserId}",
            aipRecordId, officeId, head.Id, side, caller.Id);

        IReadOnlyDictionary<int, string> refCodes = await NodeRefCodesAsync(ctx, head.NodeType, ct);
        return ServiceResult<AipReviewCommentDto>.Ok(
            Map(reply, side, refCodes, authorNameOverride: caller.FullName));
    }

    /// <summary>
    /// Why this non-reviewer may not reply on <paramref name="head"/>'s thread, or null when they may.
    /// ⚠️ The same answer <see cref="ReplyRuleAsync"/> gives the read's <c>CanReply</c>; both go
    /// through <see cref="AipDivisionContext.RefuseActivityReply"/>.
    /// </summary>
    private async Task<string?> RefuseEncoderReplyAsync(
        User caller, CommentContext ctx, AipReviewComment head, CancellationToken ct)
    {
        // Another office's user can only have reached here through a review scope without a reviewer
        // flag (a host-office reader): they are not this office's encoder.
        if (caller.OfficeId != ctx.OfficeId) return NotThisOfficeReplyMessage;

        AipOffice group = ctx.Groups.FirstOrDefault(g => g.Id == head.AipOfficeId) ?? ctx.Groups[0];
        AipDivisionContext div = await _divisionLock.LoadAsync(group, caller, ct);

        // A legacy program/project head (before PPDO-79) carries no division of its own.
        if (head.NodeType != AipCommentNodeType.Activity) return div.RefuseContainerWrite();

        AipActivity? activity = await _aipRepo.GetActivityByIdAsync(head.NodeId, ct);
        if (activity is null) return RemovedRowReplyMessage;
        return div.RefuseActivityReply(activity.DivisionId);
    }

    /// <summary>
    /// Whether this caller may reply on each comment, for the read's <c>CanReply</c>: open heads only,
    /// and then any reviewer, or an encoder whose scope covers the activity.
    ///
    /// ⚠️ At most one division-lock load and three node queries, whatever the comment count — never
    /// one per comment. A caller with nothing open pays nothing.
    /// </summary>
    private async Task<Func<AipReviewComment, bool>> ReplyRuleAsync(
        User caller, CommentContext ctx, AipCommentSide? callerSide,
        IReadOnlyList<AipReviewComment> rows, CancellationToken ct)
    {
        static bool Open(AipReviewComment c) => c.IsHead && c.ResolvedAt is null;

        if (!rows.Any(Open)) return _ => false;
        if (callerSide is not null) return Open;
        if (caller.OfficeId != ctx.OfficeId) return _ => false;

        IReadOnlyDictionary<int, AipDivisionContext> divByGroup =
            await _divisionLock.LoadForOfficesAsync(ctx.Record, ctx.Groups, caller, ct);

        Dictionary<int, int?> activityDivisions = (await ActivitiesAsync(ctx, ct))
            .ToDictionary(a => a.Id, a => a.DivisionId);

        return c =>
        {
            if (!Open(c)) return false;
            AipDivisionContext div = divByGroup.GetValueOrDefault(c.AipOfficeId, AipDivisionContext.None);
            if (c.NodeType != AipCommentNodeType.Activity) return div.RefuseContainerWrite() is null;
            return activityDivisions.TryGetValue(c.NodeId, out int? division)
                && div.RefuseActivityReply(division) is null;
        };
    }

    // ── Notifications (v1.9.0 — PPDO-221 feed, PPDO-223 events) ───────────────

    /// <summary>
    /// A new thread: the office's users who can open the activity (AIP Entry's read, division axis
    /// included), with a link to it. The author is removed by <see cref="INotificationService.StageAsync"/>.
    /// </summary>
    private Task StageThreadStartedAsync(
        CommentContext ctx, AipReviewComment head, AipCommentSide side, User caller, CancellationToken ct)
        => _notifications.StageAsync(NotificationTypes.AipComment, caller, async (audience, c) =>
        {
            ActivityPath? path = await PathOfAsync(head, c);
            if (path is null) return null;
            string sideLabel = side == AipCommentSide.Ppdo ? "PPDO" : caller.FullName;
            return new NotificationBatch(
                NotificationText.AipThreadStarted(sideLabel, path.Activity.Name, head.Body),
                NotificationLinks.AipActivity(ctx.Record.FiscalYear, path.Program.Id, path.Project.Id, path.Activity.Id),
                await audience.ActivityReadersAsync(path.Office, path.Program, c));
        }, ct);

    /// <summary>
    /// A reply or a resolve: the thread's starter and everyone who has replied, kept only while they
    /// can still open it. Two batches because the link differs: the office's own users go to AIP
    /// Entry, PPDO's cross-office reviewers to the review drill-down. The actor is removed by StageAsync.
    /// </summary>
    private async Task StageParticipantsAsync(
        string type, CommentContext ctx, AipReviewComment head, User caller,
        Func<string, string> text, CancellationToken ct)
    {
        // Read once, here rather than inside a build: both batches need it.
        HashSet<Guid> participants = (await _comments.GetByOfficeIdsAsync([head.AipOfficeId], ct))
            .Where(c => c.Id == head.Id || c.ParentCommentId == head.Id)
            .Select(c => c.AuthorId)
            .ToHashSet();

        HashSet<Guid> toldInOffice = [];

        await _notifications.StageAsync(type, caller, async (audience, c) =>
        {
            ActivityPath? path = await PathOfAsync(head, c);
            if (path is null) return null;
            List<User> readers = (await audience.ActivityReadersAsync(path.Office, path.Program, c))
                .Where(u => participants.Contains(u.Id))
                .ToList();
            toldInOffice.UnionWith(readers.Select(u => u.Id));
            return new NotificationBatch(
                text(path.Activity.Name),
                NotificationLinks.AipActivity(ctx.Record.FiscalYear, path.Program.Id, path.Project.Id, path.Activity.Id),
                readers);
        }, ct);

        await _notifications.StageAsync(type, caller, async (audience, c) =>
        {
            ActivityPath? path = await PathOfAsync(head, c);
            if (path is null) return null;
            // ⚠️ Not someone already told above: a PPDO user reviewing PPDO's own AIP is in both.
            List<User> reviewers = (await audience.CrossOfficeReviewersAsync(c))
                .Where(u => participants.Contains(u.Id) && !toldInOffice.Contains(u.Id))
                .ToList();
            return new NotificationBatch(
                text(path.Activity.Name),
                NotificationLinks.AipReviewActivity(ctx.OfficeId, ctx.Record.FiscalYear, path.Program.Id, path.Project.Id, path.Activity.Id),
                reviewers);
        }, ct);
    }

    /// <summary>
    /// The activity a thread is on, with its project, program and group row; null when the thread is
    /// not on an activity (a legacy head) or the activity is gone, since an entry needs a row to open.
    /// </summary>
    private async Task<ActivityPath?> PathOfAsync(AipReviewComment head, CancellationToken ct)
    {
        if (head.NodeType != AipCommentNodeType.Activity) return null;
        AipActivity? activity = await _aipRepo.GetActivityByIdAsync(head.NodeId, ct);
        if (activity is null) return null;
        AipProject? project = await _aipRepo.GetProjectByIdAsync(activity.ProjectId, ct);
        if (project is null) return null;
        AipProgram? program = await _aipRepo.GetProgramByIdAsync(project.ProgramId, ct);
        if (program is null) return null;
        AipOffice? office = await _aipRepo.GetOfficeByIdAsync(program.OfficeId, ct);
        return office is null ? null : new ActivityPath(office, program, project, activity);
    }

    private sealed record ActivityPath(AipOffice Office, AipProgram Program, AipProject Project, AipActivity Activity);

    // ── Internals ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Which side this caller writes and resolves as, or null when they are neither reviewer.
    ///
    /// <para>
    /// ⚠️ <b>Own office wins.</b> The two flags resolve independently and one person may hold both
    /// (spec §3.1). On their own office such a person is acting as its department head; on anyone
    /// else's they are acting for PPDO. Deciding it this way keeps the answer deterministic —
    /// without a rule, a holder of both would file comments under whichever branch happened to be
    /// tested first, and the resolve rule would then depend on that accident.
    /// </para>
    ///
    /// <para>
    /// ⚠️ SuperAdmin resolves every flag true, so on their own office they are a department head
    /// here. That is the same bypass every other feature gives them and it grants nothing extra:
    /// the resolve rule still binds them to one side.
    /// </para>
    /// </summary>
    private async Task<AipCommentSide?> ResolveSideAsync(
        User caller, CommentContext ctx, CancellationToken ct)
    {
        bool ownOffice = caller.OfficeId is int mine && mine == ctx.OfficeId;

        if (ownOffice && await _permissions.CanReviewBudgetPlanningAsync(caller, ct))
            return AipCommentSide.DepartmentHead;

        if (await _permissions.CanReviewAllOfficesAsync(caller, ct))
            return AipCommentSide.Ppdo;

        return null;
    }

    /// <summary>
    /// The record, the office and its group rows — or null when the caller may not see it.
    ///
    /// ⚠️ Scoped with <c>ResolveForReview</c>, never <c>Resolve</c>: a PPDO consolidated reviewer
    /// reads every office, and their own office is ignored rather than added to the set.
    /// </summary>
    private async Task<CommentContext?> ResolveAsync(
        int aipRecordId, int officeId, User caller, CancellationToken ct)
    {
        AipRecord? record = await _aipRepo.GetByIntIdAsync(aipRecordId, ct);
        if (record is null) return null;

        bool crossOffice = await _permissions.CanReviewAllOfficesAsync(caller, ct);
        OfficeScope scope = OfficeScope.ResolveForReview(caller, crossOffice);
        if (!scope.Permits(officeId)) return null;

        IReadOnlyList<AipOffice> all = await _aipRepo.GetOfficesByAipIdAsync(aipRecordId, ct);
        List<AipOffice> groups = all.Where(o => o.OfficeId == officeId).ToList();
        if (groups.Count == 0) return null;

        return new CommentContext(record, officeId, groups);
    }

    /// <summary>One sentence for both "no such record" and "not yours" — see PPDO-46.</summary>
    private static string NotFoundMessage(int aipRecordId, int officeId)
        => $"AIP office {officeId} not found in record {aipRecordId}.";

    private static AipUnresolvedCountsDto Tally(IReadOnlyList<AipReviewComment> rows)
        => new(
            rows.Count(c => c.IsUnresolved && c.AuthorSide == AipCommentSide.DepartmentHead),
            rows.Count(c => c.IsUnresolved && c.AuthorSide == AipCommentSide.Ppdo));

    /// <summary>
    /// Ref codes for every node any of these comments points at, so the UI can name the row.
    /// Absent means the node was deleted — the comment renders as orphaned rather than vanishing.
    /// </summary>
    private async Task<IReadOnlyDictionary<int, string>> ResolveRefCodesAsync(
        CommentContext ctx, IReadOnlyList<AipReviewComment> rows, CancellationToken ct)
    {
        Dictionary<int, string> byId = [];
        foreach (AipCommentNodeType type in rows.Select(r => r.NodeType).Distinct())
            foreach (KeyValuePair<int, string> kv in await NodeRefCodesAsync(ctx, type, ct))
                byId[kv.Key] = kv.Value;
        return byId;
    }

    /// <summary>
    /// Every node of one kind in this office, as id → ref code.
    ///
    /// ⚠️ Three queries at worst regardless of comment count, never one per comment. Node ids are
    /// unique per table, so a single dictionary per kind is enough.
    /// </summary>
    private async Task<IReadOnlyDictionary<int, string>> NodeRefCodesAsync(
        CommentContext ctx, AipCommentNodeType type, CancellationToken ct)
    {
        IReadOnlyList<AipProgram> programs =
            await _aipRepo.GetProgramsByOfficeIdsNoTrackingAsync(ctx.GroupIds, ct);
        if (type == AipCommentNodeType.Program)
            return programs.ToDictionary(p => p.Id, p => p.RefCode);

        IReadOnlyList<AipProject> projects =
            await _aipRepo.GetProjectsByProgramIdsNoTrackingAsync(programs.Select(p => p.Id).ToList(), ct);
        if (type == AipCommentNodeType.Project)
            return projects.ToDictionary(p => p.Id, p => p.RefCode);

        IReadOnlyList<AipActivity> activities =
            await _aipRepo.GetActivitiesByProjectIdsNoTrackingAsync(projects.Select(p => p.Id).ToList(), ct);
        return activities.ToDictionary(a => a.Id, a => a.RefCode);
    }

    /// <summary>Every activity of the office (three queries), for the encoder reply rule.</summary>
    private async Task<IReadOnlyList<AipActivity>> ActivitiesAsync(CommentContext ctx, CancellationToken ct)
    {
        IReadOnlyList<AipProgram> programs =
            await _aipRepo.GetProgramsByOfficeIdsNoTrackingAsync(ctx.GroupIds, ct);
        IReadOnlyList<AipProject> projects =
            await _aipRepo.GetProjectsByProgramIdsNoTrackingAsync(programs.Select(p => p.Id).ToList(), ct);
        return await _aipRepo.GetActivitiesByProjectIdsNoTrackingAsync(projects.Select(p => p.Id).ToList(), ct);
    }

    /// <summary>
    /// Which of the office's group rows actually owns this node. Single-group offices — most of
    /// them — take the cheap path.
    /// </summary>
    private async Task<int> OwningGroupIdAsync(
        CommentContext ctx, AipCommentNodeType type, int nodeId, CancellationToken ct)
    {
        if (ctx.Groups.Count == 1) return ctx.Groups[0].Id;

        IReadOnlyList<AipProgram> programs =
            await _aipRepo.GetProgramsByOfficeIdsNoTrackingAsync(ctx.GroupIds, ct);

        int programId = type switch
        {
            AipCommentNodeType.Program => nodeId,
            _ => await ProgramIdOfAsync(programs, type, nodeId, ct),
        };

        return programs.FirstOrDefault(p => p.Id == programId)?.OfficeId ?? ctx.Groups[0].Id;
    }

    private async Task<int> ProgramIdOfAsync(
        IReadOnlyList<AipProgram> programs, AipCommentNodeType type, int nodeId,
        CancellationToken ct)
    {
        IReadOnlyList<AipProject> projects =
            await _aipRepo.GetProjectsByProgramIdsNoTrackingAsync(programs.Select(p => p.Id).ToList(), ct);

        if (type == AipCommentNodeType.Project)
            return projects.FirstOrDefault(p => p.Id == nodeId)?.ProgramId ?? 0;

        IReadOnlyList<AipActivity> activities =
            await _aipRepo.GetActivitiesByProjectIdsNoTrackingAsync(projects.Select(p => p.Id).ToList(), ct);
        int projectId = activities.FirstOrDefault(a => a.Id == nodeId)?.ProjectId ?? 0;
        return projects.FirstOrDefault(p => p.Id == projectId)?.ProgramId ?? 0;
    }

    private static AipReviewCommentDto Map(
        AipReviewComment c,
        AipCommentSide? callerSide,
        IReadOnlyDictionary<int, string> refCodes,
        string? authorNameOverride = null,
        string? resolvedNameOverride = null,
        bool canReply = false)
    {
        refCodes.TryGetValue(c.NodeId, out string? refCode);

        return new AipReviewCommentDto(
            c.Id, c.AipOfficeId, c.NodeType.ToString(), c.NodeId, refCode, c.Body,
            c.AuthorId,
            authorNameOverride ?? c.Author?.FullName ?? "Unknown",
            c.AuthorSide.ToString(),
            c.CreatedAt,
            c.ResolvedAt,
            resolvedNameOverride ?? c.ResolvedBy?.FullName,
            // ⚠️ An open HEAD and the caller is on the authoring side. Every part matters: a resolved
            // comment offers no control, neither does one addressed to this reader, and a reply is
            // never resolved on its own (PPDO-223 — IsUnresolved is false on every reply).
            CanResolve: c.IsUnresolved && callerSide == c.AuthorSide,
            IsOrphaned: refCode is null,
            IsReviewerEdit: c.IsReviewerEdit,
            AcknowledgedAt: c.AcknowledgedAt,
            AcknowledgedByName: c.AcknowledgedBy?.FullName,
            ParentCommentId: c.ParentCommentId,
            CanReply: canReply);
    }

    private sealed record CommentContext(AipRecord Record, int OfficeId, List<AipOffice> Groups)
    {
        public List<int> GroupIds { get; } = Groups.Select(g => g.Id).ToList();
    }
}
