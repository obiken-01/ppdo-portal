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

    private readonly IAipReviewCommentRepository       _comments;
    private readonly IAipRepository                    _aipRepo;
    private readonly IPermissionService                _permissions;
    private readonly ILogger<AipReviewCommentService>  _logger;

    public AipReviewCommentService(
        IAipReviewCommentRepository      comments,
        IAipRepository                   aipRepo,
        IPermissionService               permissions,
        ILogger<AipReviewCommentService> logger)
    {
        _comments    = comments;
        _aipRepo     = aipRepo;
        _permissions = permissions;
        _logger      = logger;
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

        List<AipReviewCommentDto> dtos = rows
            .Select(c => Map(c, callerSide, refCodes))
            .ToList();

        return ServiceResult<AipReviewCommentsDto>.Ok(new AipReviewCommentsDto(
            aipRecordId, officeId, dtos, Tally(rows), CanComment: callerSide is not null));
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

        // ⚠️ The encoder never comments. Refused as Forbidden rather than NotFound: this caller can
        // legitimately see the office — they just have nothing to say here in this system's model.
        AipCommentSide? side = await ResolveSideAsync(caller, ctx, ct);
        if (side is null)
            return ServiceResult<AipReviewCommentDto>.Forbidden(
                "Only a reviewer can leave a comment. Encoders act on comments and re-submit.");

        // ⚠️ Parsed, not cast. The wire carries the enum NAME (see the DTO's remarks), so an
        // unknown value is a client error rather than a silent fall-through to Program = 0.
        if (!Enum.TryParse(dto.NodeType, ignoreCase: true, out AipCommentNodeType nodeType))
            return ServiceResult<AipReviewCommentDto>.BadRequest(
                $"'{dto.NodeType}' is not a commentable row type. "
                + "Expected Program, Project or Activity.");

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
        await _comments.SaveChangesAsync(ct);

        _logger.LogInformation(
            "AIP review comment resolved. CommentId: {CommentId}, Side: {Side}, UserId: {UserId}",
            commentId, comment.AuthorSide, caller.Id);

        return ServiceResult<AipReviewCommentDto>.Ok(
            Map(comment, side, new Dictionary<int, string>(), resolvedNameOverride: caller.FullName));
    }

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
            await _aipRepo.GetProgramsByOfficeIdsAsync(ctx.GroupIds, ct);
        if (type == AipCommentNodeType.Program)
            return programs.ToDictionary(p => p.Id, p => p.RefCode);

        IReadOnlyList<AipProject> projects =
            await _aipRepo.GetProjectsByProgramIdsAsync(programs.Select(p => p.Id).ToList(), ct);
        if (type == AipCommentNodeType.Project)
            return projects.ToDictionary(p => p.Id, p => p.RefCode);

        IReadOnlyList<AipActivity> activities =
            await _aipRepo.GetActivitiesByProjectIdsAsync(projects.Select(p => p.Id).ToList(), ct);
        return activities.ToDictionary(a => a.Id, a => a.RefCode);
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
            await _aipRepo.GetProgramsByOfficeIdsAsync(ctx.GroupIds, ct);

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
            await _aipRepo.GetProjectsByProgramIdsAsync(programs.Select(p => p.Id).ToList(), ct);

        if (type == AipCommentNodeType.Project)
            return projects.FirstOrDefault(p => p.Id == nodeId)?.ProgramId ?? 0;

        IReadOnlyList<AipActivity> activities =
            await _aipRepo.GetActivitiesByProjectIdsAsync(projects.Select(p => p.Id).ToList(), ct);
        int projectId = activities.FirstOrDefault(a => a.Id == nodeId)?.ProjectId ?? 0;
        return projects.FirstOrDefault(p => p.Id == projectId)?.ProgramId ?? 0;
    }

    private static AipReviewCommentDto Map(
        AipReviewComment c,
        AipCommentSide? callerSide,
        IReadOnlyDictionary<int, string> refCodes,
        string? authorNameOverride = null,
        string? resolvedNameOverride = null)
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
            // ⚠️ Unresolved AND the caller is on the authoring side. Both halves matter: an already
            // resolved comment offers no control, and neither does one addressed to this reader.
            CanResolve: c.IsUnresolved && callerSide == c.AuthorSide,
            IsOrphaned: refCode is null);
    }

    private sealed record CommentContext(AipRecord Record, int OfficeId, List<AipOffice> Groups)
    {
        public List<int> GroupIds { get; } = Groups.Select(g => g.Id).ToList();
    }
}
