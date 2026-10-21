using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
using PPDO.Application.DTOs.InvestmentProposal;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;

namespace PPDO.Application.Services;

// Comments on an Investment Proposal (v1.9.0 — PPDO-219, docs/v1.9/Investment_Proposal_Comments_Spec.md).
// Modelled on AipReviewCommentService (the side rule, the resolve rule), but one thread per proposal.
public sealed partial class InvestmentProposalService
{
    public const int MaxCommentLength = 2000;

    public const string CommentRequiredMessage = "Write a comment.";
    public const string CommentTooLongMessage  = "A comment is at most 2,000 characters.";
    public const string AlreadyResolvedMessage = "This comment is already resolved.";
    public const string CommentNotFoundMessage = "Comment not found.";
    public const string ParentNotFoundMessage     = "The comment to reply to was not found on this proposal.";
    public const string ReplyToReplyMessage       = "Reply to the thread's first comment.";
    public const string ThreadResolvedMessage     = "This thread is resolved. Start a new one.";
    public const string HeadOnlyResolveMessage    = "Only a thread's first comment can be resolved.";
    public const string OnlyPpdoResolvesMessage   = "Only PPDO can resolve a PPDO comment.";
    public const string OnlyOfficeResolvesMessage = "Only the proposal's office can resolve an Office comment.";

    /// <summary>"Resolve the 1 open comment before finalizing."</summary>
    public static string OpenCommentsMessage(int open)
        => $"Resolve the {open} open {(open == 1 ? "comment" : "comments")} before finalizing.";

    public async Task<ServiceResult<IReadOnlyList<ProposalCommentDto>>> ListCommentsAsync(
        int proposalId, User caller, CancellationToken ct = default)
    {
        (InvestmentProposal? proposal, ProjectContext? ctx) = await LoadReadableAsync(proposalId, caller, ct);
        if (proposal is null || ctx is null)
            return ServiceResult<IReadOnlyList<ProposalCommentDto>>.NotFound(NotFoundMessage);

        ProposalCommentSide callerSide = SideOf(caller, ctx);
        IReadOnlyList<InvestmentProposalComment> rows = await _comments.GetByProposalIdAsync(proposal.Id, ct);
        return ServiceResult<IReadOnlyList<ProposalCommentDto>>.Ok(rows.Select(c => MapComment(c, callerSide)).ToList());
    }

    public async Task<ServiceResult<ProposalCommentDto>> AddCommentAsync(
        int proposalId, string? body, int? parentCommentId, User caller, CancellationToken ct = default)
    {
        // Scope first: a proposal the caller cannot see is a 404 whatever the body says.
        (InvestmentProposal? proposal, ProjectContext? ctx) = await LoadReadableAsync(proposalId, caller, ct);
        if (proposal is null || ctx is null) return ServiceResult<ProposalCommentDto>.NotFound(NotFoundMessage);

        string text = (body ?? string.Empty).Trim();
        if (text.Length == 0) return ServiceResult<ProposalCommentDto>.BadRequest(CommentRequiredMessage);
        if (text.Length > MaxCommentLength) return ServiceResult<ProposalCommentDto>.BadRequest(CommentTooLongMessage);

        // A reply goes under an OPEN HEAD of this proposal. Anyone who can read the proposal may reply,
        // from either side; the reply carries its author's own side.
        if (parentCommentId is int parentId)
        {
            InvestmentProposalComment? parent = await _comments.GetByIdAsync(parentId, ct);
            if (parent is null || parent.ProposalId != proposal.Id)
                return ServiceResult<ProposalCommentDto>.BadRequest(ParentNotFoundMessage);
            if (!parent.IsHead)
                return ServiceResult<ProposalCommentDto>.BadRequest(ReplyToReplyMessage);
            if (!parent.IsOpen)
                return ServiceResult<ProposalCommentDto>.BadRequest(ThreadResolvedMessage);
        }

        ProposalCommentSide side = SideOf(caller, ctx);
        InvestmentProposalComment comment = new()
        {
            ProposalId = proposal.Id,
            ParentCommentId = parentCommentId,
            Body       = text,
            AuthorSide = side,
            AuthorId   = caller.Id,
            CreatedAt  = Now(),
            Author     = caller,
        };
        await _comments.AddAsync(comment, ct);
        // PPDO-221 — staged before the save so the comment and its notifications land together.
        await StageCommentNotificationAsync(proposal, ctx, parentCommentId, text, side, caller, ct);
        await _comments.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Investment proposal comment added. ProposalId: {ProposalId}, CommentId: {CommentId}, Side: {Side}, UserId: {UserId}",
            proposal.Id, comment.Id, side, caller.Id);
        await _audit.LogAsync("investment_proposal_comments", comment.Id, AuditAction.Create,
            null, new { comment.ProposalId, Side = side.ToString() }, ct);

        return ServiceResult<ProposalCommentDto>.Ok(MapComment(comment, side));
    }

    public async Task<ServiceResult<ProposalCommentDto>> ResolveCommentAsync(
        int proposalId, int commentId, User caller, CancellationToken ct = default)
    {
        (InvestmentProposal? proposal, ProjectContext? ctx) = await LoadReadableAsync(proposalId, caller, ct);
        if (proposal is null || ctx is null) return ServiceResult<ProposalCommentDto>.NotFound(NotFoundMessage);

        // A comment id under another proposal is as missing as a wrong id.
        InvestmentProposalComment? comment = await _comments.GetByIdAsync(commentId, ct);
        if (comment is null || comment.ProposalId != proposal.Id)
            return ServiceResult<ProposalCommentDto>.NotFound(CommentNotFoundMessage);

        // Resolving closes a THREAD, so it is the head's act. A reply is never resolved on its own.
        if (!comment.IsHead)
            return ServiceResult<ProposalCommentDto>.BadRequest(HeadOnlyResolveMessage);

        // ⚠️ The side rule. Only the side that started the thread resolves it; otherwise the office could clear
        // PPDO's remark and the Finalize gate would be self-marking.
        ProposalCommentSide side = SideOf(caller, ctx);
        if (side != comment.AuthorSide)
            return ServiceResult<ProposalCommentDto>.Forbidden(
                comment.AuthorSide == ProposalCommentSide.Ppdo ? OnlyPpdoResolvesMessage : OnlyOfficeResolvesMessage);

        if (comment.ResolvedAt is not null)
            return ServiceResult<ProposalCommentDto>.BadRequest(AlreadyResolvedMessage);

        comment.ResolvedAt   = Now();
        comment.ResolvedById = caller.Id;
        comment.ResolvedBy   = caller;
        // PPDO-221 — "thread resolved" goes to the thread's starter and repliers, before the save.
        await _notifications.StageAsync(NotificationTypes.ProposalResolved, caller, async (audience, c) =>
            new NotificationBatch(
                NotificationText.ProposalResolved(caller.FullName, ctx.Project.RefCode, ctx.Project.Name),
                NotificationLinks.Proposal(proposal.Id),
                await ThreadParticipantReadersAsync(audience, proposal, ctx, comment.Id, c)), ct);
        await _comments.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Investment proposal comment resolved. ProposalId: {ProposalId}, CommentId: {CommentId}, Side: {Side}, UserId: {UserId}",
            proposal.Id, comment.Id, comment.AuthorSide, caller.Id);
        await _audit.LogAsync("investment_proposal_comments", comment.Id, AuditAction.Update,
            new { Resolved = false }, new { Resolved = true }, ct);

        return ServiceResult<ProposalCommentDto>.Ok(MapComment(comment, side));
    }

    // ── Notifications (PPDO-221, Notification_Centre_Spec.md §3.1) ────────────

    /// <summary>
    /// A new thread started by PPDO goes to everyone who can open the proposal; one started by the
    /// office goes to the office's own readers only (PPDO-239). A reply goes to the thread's starter
    /// and everyone who has replied. The actor is removed by <see cref="INotificationService.StageAsync"/>.
    /// </summary>
    private Task StageCommentNotificationAsync(
        InvestmentProposal proposal, ProjectContext ctx, int? parentCommentId, string body,
        ProposalCommentSide side, User caller, CancellationToken ct)
    {
        string refCode = ctx.Project.RefCode, project = ctx.Project.Name;
        string link = NotificationLinks.Proposal(proposal.Id);

        if (parentCommentId is not int headId)
            return _notifications.StageAsync(NotificationTypes.ProposalComment, caller, async (audience, c) =>
                new NotificationBatch(
                    NotificationText.ProposalThreadStarted(SideLabel(side, caller), refCode, project, body),
                    link,
                    // PPDO-239: the office's own discussion stays in the office; PPDO's reaches everyone
                    // who can open the proposal, every PPDO reviewer included.
                    side == ProposalCommentSide.Office
                        ? await audience.ProposalOfficeReadersAsync(ctx.Office, ctx.Program, c)
                        : await audience.ProposalReadersAsync(ctx.Office, ctx.Program, c)), ct);

        return _notifications.StageAsync(NotificationTypes.ProposalReply, caller, async (audience, c) =>
            new NotificationBatch(
                NotificationText.ProposalReply(caller.FullName, refCode, project, body),
                link,
                await ThreadParticipantReadersAsync(audience, proposal, ctx, headId, c)), ct);
    }

    /// <summary>
    /// The thread's starter and repliers, kept only while they can still open the proposal: the entry
    /// quotes the comment, so someone who has lost access since must not be sent it.
    /// </summary>
    private async Task<IReadOnlyList<User>> ThreadParticipantReadersAsync(
        INotificationAudience audience, InvestmentProposal proposal, ProjectContext ctx, int headId, CancellationToken ct)
    {
        HashSet<Guid> participants = (await _comments.GetByProposalIdAsync(proposal.Id, ct))
            .Where(c => c.Id == headId || c.ParentCommentId == headId)
            .Select(c => c.AuthorId)
            .ToHashSet();
        if (participants.Count == 0) return [];

        return (await audience.ProposalReadersAsync(ctx.Office, ctx.Program, ct))
            .Where(u => participants.Contains(u.Id))
            .ToList();
    }

    /// <summary>"PPDO", or the commenting office's code ("PTO").</summary>
    private static string SideLabel(ProposalCommentSide side, User caller)
        => side == ProposalCommentSide.Ppdo
            ? "PPDO"
            : caller.Office?.OfficeCode is { Length: > 0 } code ? code : "The office";

    /// <summary>
    /// Office when the caller's office owns the proposal, otherwise PPDO. ⚠️ A caller with no office
    /// can only have got here as a cross-office reviewer, so they are PPDO.
    /// </summary>
    private static ProposalCommentSide SideOf(User caller, ProjectContext ctx)
        => caller.OfficeId is int officeId && officeId == ctx.Office.OfficeId
            ? ProposalCommentSide.Office
            : ProposalCommentSide.Ppdo;

    private static ProposalCommentDto MapComment(InvestmentProposalComment c, ProposalCommentSide callerSide) => new(
        c.Id, c.ParentCommentId, c.Body, c.AuthorSide.ToString(), c.Author?.FullName ?? "Unknown",
        DateTime.SpecifyKind(c.CreatedAt, DateTimeKind.Utc),
        AsUtc(c.ResolvedAt), c.ResolvedBy?.FullName,
        CanResolve: c.IsHead && c.IsOpen && callerSide == c.AuthorSide);
}
