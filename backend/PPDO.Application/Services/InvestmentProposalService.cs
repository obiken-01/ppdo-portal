using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
using PPDO.Application.DTOs.InvestmentProposal;
using PPDO.Application.Validators.InvestmentProposal;
using PPDO.Domain.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <inheritdoc />
public sealed class InvestmentProposalService : IInvestmentProposalService
{
    public const string NotFoundMessage        = "Proposal not found.";
    public const string ProjectNotFoundMessage = "Project not found.";
    public const string AlreadyExistsMessage   = "This project already has a proposal.";
    public const string FinalMessage           = "This proposal is final. Reopen it to make changes.";
    public const string NotFinalMessage        = "This proposal is not final.";
    public const string ChooseOfficeMessage    = "Choose an office.";
    public const string RowVersionMessage      = "The proposal's version is missing. Reload and try again.";
    public const string ValidationMessage      = "Validation failed";

    /// <summary>"Investment proposals start with FY 2028." The year comes from <see cref="AipFiscalYears"/>, never a literal.</summary>
    public static string FirstYearMessage
        => $"Investment proposals start with FY {AipFiscalYears.FirstEnteredFiscalYear}.";

    public const int MaxPageSize = 100;

    private const string AuditTable    = "investment_proposals";
    // 2: adds WorkPlan (the typed Section G rows). Version 1 has none; readers accept both.
    private const int    SchemaVersion = 2;
    private static readonly TimeSpan ManilaOffset = TimeSpan.FromHours(8);

    private static readonly JsonSerializerOptions SnapshotJson = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IInvestmentProposalRepository         _proposals;
    private readonly IAipRepository                        _aip;
    private readonly IAipExpenditureRepository             _expenditures;
    private readonly IAllocationRepository                 _allocation;
    private readonly IAipDivisionLock                      _divisionLock;
    private readonly IPermissionService                    _permissions;
    private readonly IInvestmentPlanningSettingsRepository _settings;
    private readonly IUserRepository                       _users;
    private readonly IAuditService                         _audit;
    private readonly ILogger<InvestmentProposalService>    _logger;
    private readonly TimeProvider                          _clock;

    public InvestmentProposalService(
        IInvestmentProposalRepository         proposals,
        IAipRepository                        aip,
        IAipExpenditureRepository             expenditures,
        IAllocationRepository                 allocation,
        IAipDivisionLock                      divisionLock,
        IPermissionService                    permissions,
        IInvestmentPlanningSettingsRepository settings,
        IUserRepository                       users,
        IAuditService                         audit,
        ILogger<InvestmentProposalService>    logger,
        TimeProvider?                         clock = null)
    {
        _proposals    = proposals;
        _aip          = aip;
        _expenditures = expenditures;
        _allocation   = allocation;
        _divisionLock = divisionLock;
        _permissions  = permissions;
        _settings     = settings;
        _users        = users;
        _audit        = audit;
        _logger       = logger;
        _clock        = clock ?? TimeProvider.System;
    }

    // ── Lists ─────────────────────────────────────────────────────────────────

    public async Task<ServiceResult<ProposalListPageDto>> ListAsync(
        int fiscalYear, int? officeId, string? search, int page, int pageSize, User caller,
        CancellationToken ct = default)
    {
        if (!AipFiscalYears.IsEntered(fiscalYear))
            return ServiceResult<ProposalListPageDto>.BadRequest(FirstYearMessage);

        page     = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        AipRecord? record = await _aip.GetLatestByFiscalYearAsync(fiscalYear, ct);
        if (record is null)
            return ServiceResult<ProposalListPageDto>.Ok(new ProposalListPageDto([], 0, page, pageSize));

        // Out-of-scope officeId is clamped, not refused — the same as the other budget-planning lists.
        ProposalProjectQuery query = await ScopedQueryAsync(
            record, caller, (await ReadOfficeScopeAsync(caller, ct)).Clamp(officeId),
            search, (page - 1) * pageSize, pageSize, ct);
        ProposalProjectPage result = await _proposals.ListProjectsAsync(query, ct);

        List<ProposalListItemDto> items = result.Items
            .Select(r => new ProposalListItemDto(
                r.AipProjectId, r.ProjectRefCode, r.ProjectName, r.ProgramName, r.OfficeName,
                r.ProjectCost, r.ProposalId, r.ProposalStatus ?? "None", AsUtc(r.UpdatedAt), r.UpdatedByName))
            .ToList();
        return ServiceResult<ProposalListPageDto>.Ok(new ProposalListPageDto(items, result.TotalCount, page, pageSize));
    }

    public async Task<ServiceResult<IReadOnlyList<ProposalProjectOptionDto>>> ListProjectOptionsAsync(
        int fiscalYear, int? officeId, User caller, CancellationToken ct = default)
    {
        if (!AipFiscalYears.IsEntered(fiscalYear))
            return ServiceResult<IReadOnlyList<ProposalProjectOptionDto>>.BadRequest(FirstYearMessage);

        OfficeScope office = await ReadOfficeScopeAsync(caller, ct);
        // One office at a time (decision 29). A guest caller is pinned to their own; anyone who sees
        // several must say which, rather than receive every office's projects in one unpaged list.
        if (office.SeeAll && officeId is null)
            return ServiceResult<IReadOnlyList<ProposalProjectOptionDto>>.BadRequest(ChooseOfficeMessage);

        AipRecord? record = await _aip.GetLatestByFiscalYearAsync(fiscalYear, ct);
        if (record is null)
            return ServiceResult<IReadOnlyList<ProposalProjectOptionDto>>.Ok([]);

        ProposalProjectQuery query = await ScopedQueryAsync(
            record, caller, office.Clamp(officeId), search: null, skip: 0, take: null, ct);
        ProposalProjectPage result = await _proposals.ListProjectsAsync(query, ct);

        return ServiceResult<IReadOnlyList<ProposalProjectOptionDto>>.Ok(result.Items
            .Select(r => new ProposalProjectOptionDto(
                r.AipProjectId, r.ProjectRefCode, r.ProjectName, r.ProgramId, r.ProgramRefCode, r.ProgramName,
                r.ProposalId, r.ProposalStatus ?? "None", AsUtc(r.UpdatedAt), r.UpdatedByName))
            .ToList());
    }

    /// <summary>
    /// The caller's read scope as a query: the office axis (already clamped) and the division axis,
    /// built from the same <see cref="AipReadScope"/> rule AIP Entry uses.
    /// </summary>
    private async Task<ProposalProjectQuery> ScopedQueryAsync(
        AipRecord record, User caller, int? configOfficeId, string? search, int skip, int? take,
        CancellationToken ct)
    {
        IReadOnlyList<AipOffice> recordOffices = await _aip.GetOfficesByAipIdAsync(record.Id, ct);

        // A clamped office id of 0 (no office) matches no AIP office, so the list is empty.
        List<int>? aipOfficeIds = configOfficeId is int oid
            ? recordOffices.Where(o => o.OfficeId == oid).Select(o => o.Id).ToList()
            : null;

        AipReadScope read = AipReadScope.Resolve(caller);
        List<int> narrowed = [];
        List<string> allowed = [];
        if (read.OfficeIdForAssignments is int ownOfficeId)
        {
            narrowed = recordOffices.Where(o => o.OfficeId == ownOfficeId).Select(o => o.Id).ToList();
            allowed  = read.AllowedProgramRefCodes(
                await _allocation.GetProgramDivisionsByOfficeIdAsync(ownOfficeId, ct)).ToList();
        }

        return new ProposalProjectQuery(record.Id, aipOfficeIds, narrowed, allowed, search, skip, take);
    }

    // ── One proposal ──────────────────────────────────────────────────────────

    public async Task<ServiceResult<ProposalDto>> CreateAsync(int aipProjectId, User caller, CancellationToken ct = default)
    {
        ProjectContext? ctx = await LoadContextAsync(aipProjectId, ct);
        if (ctx is null || !await CanReadAsync(ctx, caller, ct))
            return ServiceResult<ProposalDto>.NotFound(ProjectNotFoundMessage);
        if (!AipFiscalYears.IsEntered(ctx.Record.FiscalYear))
            return ServiceResult<ProposalDto>.BadRequest(FirstYearMessage);
        if (await RefuseWriteAsync(ctx, caller, ct) is string refusal)
            return ServiceResult<ProposalDto>.Forbidden(refusal);

        if (await _proposals.GetByProjectIdAsync(aipProjectId, ct) is { } existing)
            return ServiceResult<ProposalDto>.Conflict(AlreadyExistsMessage, new ProposalExistsDto(existing.Id));

        IReadOnlyList<AipActivity> activities = await ActivitiesOfAsync(ctx.Project.Id, ct);
        InvestmentPlanningSettings? settings = await _settings.GetAsync(ct);
        DateTime now = Now();

        // Pre-fill happens once, here (decisions 11, 12, 19). After this the text is the proposal's.
        InvestmentProposal proposal = new()
        {
            AipProjectId        = aipProjectId,
            Status              = InvestmentProposalStatus.Draft,
            Description         = ProposalRichText.FromPlainText(ctx.Project.Description),
            GeneralObjective    = ProposalRichText.FromPlainText(ctx.Project.Objective),
            DirectSameAsSummary = true,
            Signatory1Label     = "Prepared by",
            Signatory1Name      = caller.FullName,
            Signatory1Position  = caller.Position,
            Signatory2Label     = "Submitted by",
            Signatory2Name      = settings?.PpdcName,
            Signatory2Position  = settings?.PpdcPosition,
            Signatory3Label     = "Noted by",
            Signatory3Name      = settings?.LceName,
            Signatory3Position  = settings?.LcePosition,
            CreatedAt           = now,
            CreatedById         = caller.Id,
            UpdatedAt           = now,
            UpdatedById         = caller.Id,
            Benefits            = InvestmentProposalSector.All
                .Select(s => new InvestmentProposalBenefit { Sector = s }).ToList(),
            Logframe            = InvestmentProposalLogframeLevel.All
                .Select(l => new InvestmentProposalLogframe
                {
                    Level  = l,
                    Target = l == InvestmentProposalLogframeLevel.Input ? ActivityNamesCell(activities) : null,
                })
                .ToList(),
        };

        await _proposals.AddAsync(proposal, ct);
        try
        {
            await _proposals.SaveChangesAsync(ct);
        }
        catch (UniqueConstraintViolationException)
        {
            // Two tabs created at once and this one lost the race on the unique project id.
            InvestmentProposal? winner = await _proposals.GetByProjectIdAsync(aipProjectId, ct);
            return ServiceResult<ProposalDto>.Conflict(AlreadyExistsMessage, new ProposalExistsDto(winner?.Id ?? 0));
        }

        _logger.LogInformation(
            "Investment proposal created. ProposalId: {ProposalId}, AipProjectId: {AipProjectId}, UserId: {UserId}",
            proposal.Id, aipProjectId, caller.Id);
        await _audit.LogAsync(AuditTable, proposal.Id, AuditAction.Create,
            null, new { proposal.AipProjectId, proposal.Status }, ct);

        return ServiceResult<ProposalDto>.Ok(await BuildDtoAsync(proposal, ctx, caller, ct));
    }

    public async Task<ServiceResult<ProposalDto>> GetAsync(int id, User caller, CancellationToken ct = default)
    {
        (InvestmentProposal? proposal, ProjectContext? ctx) = await LoadReadableAsync(id, caller, ct);
        if (proposal is null || ctx is null) return ServiceResult<ProposalDto>.NotFound(NotFoundMessage);
        return ServiceResult<ProposalDto>.Ok(await BuildDtoAsync(proposal, ctx, caller, ct));
    }

    public async Task<ServiceResult<ProposalDto>> UpdateAsync(
        int id, UpdateProposalDto dto, User caller, CancellationToken ct = default)
    {
        if (dto.Content is null)
            return ServiceResult<ProposalDto>.BadRequest("Request body is missing or malformed.");
        if (ParseRowVersion(dto.RowVersion) is not byte[] version)
            return ServiceResult<ProposalDto>.BadRequest(RowVersionMessage);

        (InvestmentProposal? proposal, ProjectContext? ctx) = await LoadReadableAsync(id, caller, ct);
        if (proposal is null || ctx is null) return ServiceResult<ProposalDto>.NotFound(NotFoundMessage);
        if (await RefuseWriteAsync(ctx, caller, ct) is string refusal)
            return ServiceResult<ProposalDto>.Forbidden(refusal);
        if (proposal.Status == InvestmentProposalStatus.Final)
            return ServiceResult<ProposalDto>.Conflict(FinalMessage);
        if (!proposal.RowVersion.AsSpan().SequenceEqual(version))
            return await StaleAsync<ProposalDto>(proposal.AipProjectId, ct);

        IReadOnlyList<AipActivity> activities = await ActivitiesOfAsync(ctx.Project.Id, ct);
        Dictionary<string, List<string>> errors =
            UpdateProposalValidator.Validate(dto.Content, activities.Select(a => a.Id).ToHashSet());
        if (errors.Count > 0)
            return ServiceResult<ProposalDto>.BadRequest(ValidationMessage, new ProposalValidationErrorsDto(
                errors.ToDictionary(e => e.Key, e => (IReadOnlyList<string>)e.Value)));

        // The version the client loaded is what the database must still hold at save time.
        _proposals.SetExpectedRowVersion(proposal, version);
        ApplyContent(proposal, dto.Content);
        proposal.UpdatedAt   = Now();     // ⚠️ always: a children-only save must still touch the row
        proposal.UpdatedById = caller.Id;

        if (await SaveAsync<ProposalDto>(proposal, ct) is { } stale) return stale;

        _logger.LogInformation(
            "Investment proposal saved. ProposalId: {ProposalId}, AipProjectId: {AipProjectId}, UserId: {UserId}",
            proposal.Id, proposal.AipProjectId, caller.Id);
        // Content is never logged or audited; it is a person's narrative and can be long.
        await _audit.LogAsync(AuditTable, proposal.Id, AuditAction.Update,
            null, new { proposal.AipProjectId, Saved = "content" }, ct);

        return ServiceResult<ProposalDto>.Ok(await BuildDtoAsync(proposal, ctx, caller, ct));
    }

    public async Task<ServiceResult<ProposalDto>> FinalizeAsync(
        int id, string? rowVersion, User caller, CancellationToken ct = default)
    {
        if (ParseRowVersion(rowVersion) is not byte[] version)
            return ServiceResult<ProposalDto>.BadRequest(RowVersionMessage);

        (InvestmentProposal? proposal, ProjectContext? ctx) = await LoadReadableAsync(id, caller, ct);
        if (proposal is null || ctx is null) return ServiceResult<ProposalDto>.NotFound(NotFoundMessage);
        if (await RefuseWriteAsync(ctx, caller, ct) is string refusal)
            return ServiceResult<ProposalDto>.Forbidden(refusal);
        if (proposal.Status == InvestmentProposalStatus.Final)
            return ServiceResult<ProposalDto>.Conflict("This proposal is already final.");
        if (!proposal.RowVersion.AsSpan().SequenceEqual(version))
            return await StaleAsync<ProposalDto>(proposal.AipProjectId, ct);

        // Freeze every AIP-sourced value (decision 4): a Final proposal prints the same document
        // however the AIP changes afterwards.
        AipView live = await LoadAipViewAsync(ctx, ct);
        DateTime now = Now();
        _proposals.SetExpectedRowVersion(proposal, version);
        proposal.Status        = InvestmentProposalStatus.Final;
        // The typed Section G rows are frozen too. An activity deleted from the AIP after Finalize
        // cascades its stored row away; without this the Final document would lose that row's text.
        IReadOnlyList<ProposalWorkPlanRowDto> workPlan = ContentOf(proposal, live.Rows).WorkPlan;
        proposal.SnapshotJson  = JsonSerializer.Serialize(
            new ProposalSnapshot(SchemaVersion, live.Header, live.Rows, workPlan), SnapshotJson);
        proposal.FinalizedAt   = now;
        proposal.FinalizedById = caller.Id;
        proposal.UpdatedAt     = now;
        proposal.UpdatedById   = caller.Id;

        if (await SaveAsync<ProposalDto>(proposal, ct) is { } stale) return stale;

        _logger.LogInformation(
            "Investment proposal finalized. ProposalId: {ProposalId}, AipProjectId: {AipProjectId}, UserId: {UserId}",
            proposal.Id, proposal.AipProjectId, caller.Id);
        await _audit.LogAsync(AuditTable, proposal.Id, AuditAction.Update,
            new { Status = InvestmentProposalStatus.Draft }, new { Status = InvestmentProposalStatus.Final }, ct);

        return ServiceResult<ProposalDto>.Ok(await BuildDtoAsync(proposal, ctx, caller, ct));
    }

    public async Task<ServiceResult<ProposalDto>> ReopenAsync(
        int id, string? rowVersion, User caller, CancellationToken ct = default)
    {
        if (ParseRowVersion(rowVersion) is not byte[] version)
            return ServiceResult<ProposalDto>.BadRequest(RowVersionMessage);

        (InvestmentProposal? proposal, ProjectContext? ctx) = await LoadReadableAsync(id, caller, ct);
        if (proposal is null || ctx is null) return ServiceResult<ProposalDto>.NotFound(NotFoundMessage);
        if (!await MayReopenAsync(ctx, caller, ct))
            return ServiceResult<ProposalDto>.Forbidden("Forbidden");
        if (proposal.Status != InvestmentProposalStatus.Final)
            return ServiceResult<ProposalDto>.Conflict(NotFinalMessage);
        if (!proposal.RowVersion.AsSpan().SequenceEqual(version))
            return await StaleAsync<ProposalDto>(proposal.AipProjectId, ct);

        // Reopening discards the snapshot; the next read is live again (decision 5).
        _proposals.SetExpectedRowVersion(proposal, version);
        proposal.Status        = InvestmentProposalStatus.Draft;
        proposal.SnapshotJson  = null;
        proposal.FinalizedAt   = null;
        proposal.FinalizedById = null;
        proposal.UpdatedAt     = Now();
        proposal.UpdatedById   = caller.Id;

        if (await SaveAsync<ProposalDto>(proposal, ct) is { } stale) return stale;

        _logger.LogInformation(
            "Investment proposal reopened. ProposalId: {ProposalId}, AipProjectId: {AipProjectId}, UserId: {UserId}",
            proposal.Id, proposal.AipProjectId, caller.Id);
        await _audit.LogAsync(AuditTable, proposal.Id, AuditAction.Update,
            new { Status = InvestmentProposalStatus.Final }, new { Status = InvestmentProposalStatus.Draft }, ct);

        return ServiceResult<ProposalDto>.Ok(await BuildDtoAsync(proposal, ctx, caller, ct));
    }

    public async Task<ServiceResult<bool>> DeleteAsync(
        int id, string? rowVersion, User caller, CancellationToken ct = default)
    {
        if (ParseRowVersion(rowVersion) is not byte[] version)
            return ServiceResult<bool>.BadRequest(RowVersionMessage);

        (InvestmentProposal? proposal, ProjectContext? ctx) = await LoadReadableAsync(id, caller, ct);
        if (proposal is null || ctx is null) return ServiceResult<bool>.NotFound(NotFoundMessage);
        if (await RefuseWriteAsync(ctx, caller, ct) is string refusal)
            return ServiceResult<bool>.Forbidden(refusal);
        if (proposal.Status == InvestmentProposalStatus.Final)
            return ServiceResult<bool>.Conflict(FinalMessage);
        if (!proposal.RowVersion.AsSpan().SequenceEqual(version))
            return await StaleAsync<bool>(proposal.AipProjectId, ct);

        _proposals.SetExpectedRowVersion(proposal, version);
        _proposals.Remove(proposal);
        if (await SaveAsync<bool>(proposal, ct) is { } stale) return stale;

        _logger.LogInformation(
            "Investment proposal deleted. ProposalId: {ProposalId}, AipProjectId: {AipProjectId}, UserId: {UserId}",
            id, proposal.AipProjectId, caller.Id);
        await _audit.LogAsync(AuditTable, id, AuditAction.Delete,
            new { proposal.AipProjectId, proposal.Status }, null, ct);

        return ServiceResult<bool>.Ok(true);
    }

    // ── Scope ─────────────────────────────────────────────────────────────────

    private sealed record ProjectContext(AipProject Project, AipProgram Program, AipOffice Office, AipRecord Record);

    private async Task<ProjectContext?> LoadContextAsync(int aipProjectId, CancellationToken ct)
    {
        // Sequential awaits: one DbContext (CLAUDE.md).
        AipProject? project = await _aip.GetProjectByIdAsync(aipProjectId, ct);
        if (project is null) return null;
        AipProgram? program = await _aip.GetProgramByIdAsync(project.ProgramId, ct);
        if (program is null) return null;
        AipOffice? office = await _aip.GetOfficeByIdAsync(program.OfficeId, ct);
        if (office is null) return null;
        AipRecord? record = await _aip.GetByIntIdAsync(office.AipRecordId, ct);
        return record is null ? null : new ProjectContext(project, program, office, record);
    }

    /// <summary>The proposal and its project, or (null, null) when missing OR outside the caller's read scope.</summary>
    private async Task<(InvestmentProposal?, ProjectContext?)> LoadReadableAsync(int id, User caller, CancellationToken ct)
    {
        InvestmentProposal? proposal = await _proposals.GetByIdAsync(id, ct);
        if (proposal is null) return (null, null);
        ProjectContext? ctx = await LoadContextAsync(proposal.AipProjectId, ct);
        if (ctx is null || !await CanReadAsync(ctx, caller, ct)) return (null, null);
        return (proposal, ctx);
    }

    /// <summary>The read office axis: a cross-office reviewer reads every office (<see cref="OfficeScope.ResolveForReview"/>).</summary>
    private async Task<OfficeScope> ReadOfficeScopeAsync(User caller, CancellationToken ct)
        => OfficeScope.ResolveForReview(caller, await _permissions.CanReviewAllOfficesAsync(caller, ct));

    /// <summary>
    /// §3.2 read scope: the office axis, then AIP Entry's division axis on the program, so a
    /// division encoder sees the proposals of exactly the programs AIP Entry shows them.
    /// </summary>
    private async Task<bool> CanReadAsync(ProjectContext ctx, User caller, CancellationToken ct)
    {
        if (!(await ReadOfficeScopeAsync(caller, ct)).Permits(ctx.Office.OfficeId)) return false;

        AipReadScope read = AipReadScope.Resolve(caller);
        IReadOnlyList<ProgramDivision> own = read.OfficeIdForAssignments is int ownOfficeId
            ? await _allocation.GetProgramDivisionsByOfficeIdAsync(ownOfficeId, ct)
            : [];
        return read.FilterPrograms([ctx.Program], [ctx.Office], own).Count == 1;
    }

    /// <summary>
    /// Why the caller may not write this project's proposal, or null. Call only after
    /// <see cref="CanReadAsync"/> passed. Division submit does NOT lock proposals (§3.2): only an
    /// encoder with no division in a divisioned office is refused, as in AIP Entry.
    /// </summary>
    private async Task<string?> RefuseWriteAsync(ProjectContext ctx, User caller, CancellationToken ct)
    {
        // The endpoint already applies this; repeated so the service is safe on its own.
        if (await ReviewerWriteGuard.DeniesWriteAsync(caller, _permissions, ct)) return "Forbidden";
        // A cross-office reviewer reads other offices; writing is their own office's only.
        if (!OfficeScope.Resolve(caller).Permits(ctx.Office.OfficeId)) return "Forbidden";
        AipDivisionContext div = await _divisionLock.LoadAsync(ctx.Office, caller, ct);
        return div.RefuseContainerWrite();
    }

    private async Task<bool> MayReopenAsync(ProjectContext ctx, User caller, CancellationToken ct)
        => !await ReviewerWriteGuard.DeniesWriteAsync(caller, _permissions, ct)
           && ctx.Office.OfficeId is int officeId
           && await _permissions.CanReopenInvestmentProposalAsync(caller, officeId, ct);

    // ── Concurrency ───────────────────────────────────────────────────────────

    private static byte[]? ParseRowVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try { return Convert.FromBase64String(value); }
        catch (FormatException) { return null; }
    }

    /// <summary>Saves, or returns the stale-version 409 when someone saved in between (nothing is written).</summary>
    private async Task<ServiceResult<T>?> SaveAsync<T>(InvestmentProposal proposal, CancellationToken ct)
    {
        try
        {
            await _proposals.SaveChangesAsync(ct);
            return null;
        }
        catch (ConcurrencyConflictException)
        {
            return await StaleAsync<T>(proposal.AipProjectId, ct);
        }
    }

    /// <summary>409 "{Name} saved this proposal at {time}. Reload to see their changes." with the current version.</summary>
    private async Task<ServiceResult<T>> StaleAsync<T>(int aipProjectId, CancellationToken ct)
    {
        // Untracked re-read: what the database holds now, not this request's stale entity.
        InvestmentProposal? current = await _proposals.GetByProjectIdAsync(aipProjectId, ct);
        if (current is null) return ServiceResult<T>.NotFound(NotFoundMessage);

        string? name = current.UpdatedById is Guid uid
            ? (await _users.GetNamesByIdsAsync([uid], ct)).GetValueOrDefault(uid)
            : null;
        DateTime updatedAt = DateTime.SpecifyKind(current.UpdatedAt, DateTimeKind.Utc);
        string time = new DateTimeOffset(updatedAt).ToOffset(ManilaOffset)
            .ToString("h:mm tt", CultureInfo.InvariantCulture);

        return ServiceResult<T>.Conflict(
            $"{name ?? "Someone"} saved this proposal at {time}. Reload to see their changes.",
            new ProposalConflictDto(Convert.ToBase64String(current.RowVersion), name, updatedAt));
    }

    // ── Saving content ────────────────────────────────────────────────────────

    /// <summary>
    /// Copies the validated content onto the tracked proposal. Every child collection is replaced
    /// whole (spec §4); blank rows are dropped; rich text is sanitized to the decision-20 allow-list.
    /// </summary>
    private static void ApplyContent(InvestmentProposal p, ProposalContentDto c)
    {
        p.ProjectLocation           = Text(c.ProjectLocation);
        p.HgdgChecklist             = Text(c.HgdgChecklist);
        p.HgdgScore                 = c.HgdgScore;
        p.Description               = ProposalRichText.Sanitize(c.Description);
        p.Rationale                 = ProposalRichText.Sanitize(c.Rationale);
        p.GeneralObjective          = ProposalRichText.Sanitize(c.GeneralObjective);
        p.PartnershipSustainability = ProposalRichText.Sanitize(c.PartnershipSustainability);
        p.WomensImpactStrategy      = Text(c.WomensImpactStrategy);
        p.ProjectSupervisor         = Text(c.ProjectSupervisor);
        p.ProjectManager            = Text(c.ProjectManager);
        p.DirectSameAsSummary       = c.DirectSameAsSummary;

        // ── Beneficiaries: A, then F (Direct only while not "same as A") ──────
        List<InvestmentProposalBeneficiary> beneficiaries = c.BeneficiariesSummary
            .Where(b => !string.IsNullOrWhiteSpace(b.Indicator) || b.Male is not null || b.Female is not null)
            .Select((b, i) => new InvestmentProposalBeneficiary
            {
                Section = InvestmentProposalBeneficiarySection.Summary,
                Label = b.Indicator!.Trim(), Male = b.Male, Female = b.Female, SortOrder = i,
            })
            .ToList();
        beneficiaries.AddRange(c.TargetBeneficiaries
            .Where(t => !string.IsNullOrWhiteSpace(t.Name) || t.Male is not null || t.Female is not null)
            .Where(t => !(c.DirectSameAsSummary && t.Kind == InvestmentProposalBeneficiarySection.Direct))
            .Select((t, i) => new InvestmentProposalBeneficiary
            {
                Section = t.Kind!, Label = t.Name!.Trim(), Male = t.Male, Female = t.Female, SortOrder = i,
            }));
        Replace(p.Beneficiaries, beneficiaries);

        // D and E keep their fixed order; the validator guaranteed each appears once.
        Replace(p.Benefits, InvestmentProposalSector.All
            .Select(s => c.Benefits.First(b => b.Sector == s))
            .Select(b => new InvestmentProposalBenefit
            {
                Sector = b.Sector!, Benefit = ProposalRichText.Sanitize(b.Benefit), Cost = ProposalRichText.Sanitize(b.Cost),
            })
            .ToList());
        Replace(p.Logframe, InvestmentProposalLogframeLevel.All
            .Select(l => c.Logframe.First(x => x.Level == l))
            .Select(x => new InvestmentProposalLogframe { Level = x.Level!, Target = Text(x.Target), Verification = Text(x.Verification) })
            .ToList());

        // ── G: groups first, so rows can point at them by client key ──────────
        Dictionary<string, InvestmentProposalGroup> groups = new(StringComparer.Ordinal);
        List<InvestmentProposalGroup> groupRows = [];
        for (int i = 0; i < c.Groups.Count; i++)
        {
            InvestmentProposalGroup g = new() { Label = c.Groups[i].Label!.Trim(), SortOrder = i };
            groups[c.Groups[i].ClientKey!] = g;
            groupRows.Add(g);
        }

        List<InvestmentProposalWorkPlanRow> rows = [];
        foreach (ProposalWorkPlanRowDto r in c.WorkPlan)
        {
            bool aipRow = r.AipActivityId is not null;
            if (!aipRow && string.IsNullOrWhiteSpace(r.Name)) continue;   // blank proposal-only row
            rows.Add(new InvestmentProposalWorkPlanRow
            {
                AipActivityId     = r.AipActivityId,
                Group             = r.GroupKey is { } key && groups.TryGetValue(key, out InvestmentProposalGroup? g) ? g : null,
                Name              = aipRow ? null : r.Name!.Trim(),
                PerformanceTarget = Text(r.PerformanceTarget),
                GenderIssues      = Text(r.GenderIssues),
                // Timeline and OPR are the AIP's for AIP rows (decision 16).
                Timeline          = aipRow ? null : Text(r.Timeline),
                Opr               = aipRow ? null : Text(r.Opr),
                SortOrder         = rows.Count,
            });
        }
        // Rows go before groups, so the NO ACTION group FK never blocks the group delete.
        Replace(p.WorkPlanRows, rows);
        Replace(p.Groups, groupRows);

        // ── I, K, L ───────────────────────────────────────────────────────────
        Replace(p.TeamMembers, c.TeamMembers
            .Where(m => !string.IsNullOrWhiteSpace(m.Name))
            .Select((m, i) => new InvestmentProposalTeamMember
            {
                Name = m.Name!.Trim(), Sex = m.Sex!, GadTrainings = Text(m.GadTrainings),
                Expertise = Text(m.Expertise), RequiredTraining = Text(m.RequiredTraining), SortOrder = i,
            })
            .ToList());
        Replace(p.Monitoring, c.Monitoring
            .Where(m => !string.IsNullOrWhiteSpace(m.Activity))
            .Select((m, i) => new InvestmentProposalMonitoring
            {
                Phase = m.Phase!, Activity = m.Activity!.Trim(), Schedule = Text(m.Schedule), Tools = Text(m.Tools), SortOrder = i,
            })
            .ToList());
        Replace(p.Risks, c.Risks
            .Where(r => !string.IsNullOrWhiteSpace(r.Risk))
            .Select((r, i) => new InvestmentProposalRisk
            {
                Risk = r.Risk!.Trim(), Prevention = Text(r.Prevention), Monitoring = Text(r.Monitoring), SortOrder = i,
            })
            .ToList());

        ProposalSignatoryDto Slot(int n) => c.Signatories.First(s => s.Slot == n);
        (p.Signatory1Label, p.Signatory1Name, p.Signatory1Position) = (Text(Slot(1).Label), Text(Slot(1).Name), Text(Slot(1).Position));
        (p.Signatory2Label, p.Signatory2Name, p.Signatory2Position) = (Text(Slot(2).Label), Text(Slot(2).Name), Text(Slot(2).Position));
        (p.Signatory3Label, p.Signatory3Name, p.Signatory3Position) = (Text(Slot(3).Label), Text(Slot(3).Name), Text(Slot(3).Position));
        (p.Signatory4Label, p.Signatory4Name, p.Signatory4Position) = (Text(Slot(4).Label), Text(Slot(4).Name), Text(Slot(4).Position));
    }

    private static void Replace<T>(ICollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (T item in source) target.Add(item);
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // ── Reading the AIP ───────────────────────────────────────────────────────

    /// <summary>The header and the AIP rows, as the proposal prints them. Live while Draft.</summary>
    private sealed record AipView(ProposalHeaderDto Header, IReadOnlyList<ProposalAipRowDto> Rows);

    /// <summary>What is frozen on Finalize. Readers must accept every schema version ever written.</summary>
    /// <remarks>
    /// <see cref="WorkPlan"/> is null in a version-1 snapshot; the stored rows are used then.
    /// </remarks>
    private sealed record ProposalSnapshot(
        int SchemaVersion, ProposalHeaderDto Header, IReadOnlyList<ProposalAipRowDto> AipRows,
        IReadOnlyList<ProposalWorkPlanRowDto>? WorkPlan = null);

    private async Task<IReadOnlyList<AipActivity>> ActivitiesOfAsync(int projectId, CancellationToken ct)
        => (await _aip.GetActivitiesByProjectIdsAsync([projectId], ct))
            .OrderBy(a => a.RefCode, StringComparer.Ordinal).ThenBy(a => a.Id).ToList();

    private async Task<AipView> LoadAipViewAsync(ProjectContext ctx, CancellationToken ct)
    {
        // Sequential awaits: one DbContext.
        IReadOnlyList<AipActivity> activities = await ActivitiesOfAsync(ctx.Project.Id, ct);
        List<int> activityIds = activities.Select(a => a.Id).ToList();
        IReadOnlyList<AipExpenditure> lines = activityIds.Count == 0 ? [] : await _expenditures.GetByActivityIdsAsync(activityIds, ct);
        List<int> lineIds = lines.Select(l => l.Id).ToList();
        IReadOnlyList<AipProcurementItem> items = lineIds.Count == 0 ? [] : await _expenditures.GetProcurementItemsByExpenditureIdsAsync(lineIds, ct);

        // Fund names: the line's own name snapshot, else the funding-source list by code.
        List<string> codes = lines.Where(l => l.FundingSourceNameSnapshot is null).Select(l => l.FundingSourceSnapshot)
            .Concat(activities.Select(a => a.FundingSourceSnapshot))
            .Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c!).Distinct().ToList();
        IReadOnlyDictionary<string, string> fundNames = await _proposals.GetFundNamesByCodesAsync(codes, ct);
        string? FundName(string? nameSnapshot, string? code)
            => nameSnapshot ?? (code is null ? null : fundNames.GetValueOrDefault(code) ?? code);

        List<string> typologyCodes = activities.Select(a => a.CcTypologyCode)
            .Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c!.Trim()).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToList();
        IReadOnlyDictionary<string, string> typologyNames = await _proposals.GetTypologyNamesByCodesAsync(typologyCodes, ct);

        int fy = ctx.Record.FiscalYear;
        List<ProposalAipRowDto> rows = activities.Select(a =>
        {
            List<ProposalExpenditureDto> blocks = lines.Where(l => l.ActivityId == a.Id).OrderBy(l => l.Id)
                .Select(l => new ProposalExpenditureDto(
                    l.AccountNumberSnapshot, l.AccountTitleSnapshot, l.Ps, l.Mooe, l.Co, l.Total,
                    FundName(l.FundingSourceNameSnapshot, l.FundingSourceSnapshot),
                    items.Where(i => i.ExpenditureId == l.Id).OrderBy(i => i.PeriodNo).ThenBy(i => i.Id)
                        .Select(i => new ProposalItemDto(i.Name, i.UnitPrice, i.Qty, i.Unit, i.NumberOfDays, i.LineTotal))
                        .ToList()))
                .ToList();
            List<string> funds = blocks.Count > 0
                ? blocks.Select(b => b.FundName).Where(n => n is not null).Select(n => n!).Distinct().ToList()
                : FundName(null, a.FundingSourceSnapshot) is string own ? [own] : [];
            return new ProposalAipRowDto(
                a.Id, a.RefCode, a.Name, Timeline(a.StartDate, a.EndDate, fy), Text(a.ImplementingOffice),
                a.Ps ?? 0m, a.Mooe ?? 0m, a.Co ?? 0m, a.Total ?? 0m, funds, blocks);
        }).ToList();

        int? startMonth = activities.Select(a => MonthOf(a.StartDate)).Where(m => m is not null).Min();
        int? endMonth   = activities.Select(a => MonthOf(a.EndDate)).Where(m => m is not null).Max();
        string typology = typologyCodes.Count == 0
            ? "N/A"
            : string.Join(", ", typologyCodes.Select(c => typologyNames.TryGetValue(c, out string? n) ? $"{c} – {n}" : c));

        ProposalHeaderDto header = new(
            ctx.Project.Id, ctx.Program.Id, ctx.Office.Id, ctx.Office.OfficeId, fy,
            ctx.Program.Name, ctx.Project.RefCode, ctx.Project.Name, ctx.Office.Name,
            startMonth is int s ? $"{MonthName(s)} {fy}" : null,
            endMonth is int e ? $"{MonthName(e)} {fy}" : null,
            rows.Sum(r => r.Total),
            rows.SelectMany(r => r.FundNames).Distinct().ToList(),
            typology,
            AttributedGadBudget: null);   // set in BuildDtoAsync: it needs the proposal's score

        return new AipView(header, rows);
    }

    /// <summary>The Input/Activities target pre-fill: the activity names, one per line, within the 4,000 cap.</summary>
    private static string? ActivityNamesCell(IReadOnlyList<AipActivity> activities)
    {
        if (activities.Count == 0) return null;
        string names = string.Join("\n", activities.Select(a => a.Name.Trim()));
        return names.Length <= UpdateProposalValidator.MaxPlainCell ? names : names[..UpdateProposalValidator.MaxPlainCell];
    }

    /// <summary>
    /// "January–December 2028", or "March 2028" for one month. Calendar order, never alphabetical.
    /// AIP months are stored as names; a value that is not one is printed as typed.
    /// </summary>
    internal static string? Timeline(string? start, string? end, int fiscalYear)
    {
        string? s = MonthOf(start) is int sm ? MonthName(sm) : Text(start);
        string? e = MonthOf(end) is int em ? MonthName(em) : Text(end);
        if (s is null && e is null) return null;
        if (s is null || e is null || s == e) return $"{s ?? e} {fiscalYear}";
        return $"{s}–{e} {fiscalYear}";
    }

    internal static int? MonthOf(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string v = value.Trim().TrimEnd('.');
        if (int.TryParse(v, out int n) && n is >= 1 and <= 12) return n;
        DateTimeFormatInfo f = DateTimeFormatInfo.InvariantInfo;
        for (int m = 1; m <= 12; m++)
        {
            if (string.Equals(f.GetMonthName(m), v, StringComparison.OrdinalIgnoreCase)
                || string.Equals(f.GetAbbreviatedMonthName(m), v, StringComparison.OrdinalIgnoreCase))
                return m;
        }
        return null;
    }

    private static string MonthName(int month) => DateTimeFormatInfo.InvariantInfo.GetMonthName(month);

    // ── Building the DTO ──────────────────────────────────────────────────────

    private async Task<ProposalDto> BuildDtoAsync(InvestmentProposal p, ProjectContext ctx, User caller, CancellationToken ct)
    {
        AipView live = await LoadAipViewAsync(ctx, ct);
        AipView shown = live;
        bool aipChanged = false;
        IReadOnlyList<ProposalWorkPlanRowDto>? frozenWorkPlan = null;

        if (p.Status == InvestmentProposalStatus.Final && p.SnapshotJson is not null
            && JsonSerializer.Deserialize<ProposalSnapshot>(p.SnapshotJson, SnapshotJson) is { } snap)
        {
            shown = new AipView(snap.Header, snap.AipRows);
            frozenWorkPlan = snap.WorkPlan;
            // Cheap on purpose (§3.1): cost and activity count, not a deep compare.
            aipChanged = live.Header.ProjectCost != snap.Header.ProjectCost || live.Rows.Count != snap.AipRows.Count;
        }

        List<Guid> userIds = new[] { p.UpdatedById, p.FinalizedById }.Where(g => g is not null).Select(g => g!.Value).Distinct().ToList();
        IReadOnlyDictionary<Guid, string> names = userIds.Count == 0 ? new Dictionary<Guid, string>() : await _users.GetNamesByIdsAsync(userIds, ct);

        bool canEdit = p.Status == InvestmentProposalStatus.Draft && await RefuseWriteAsync(ctx, caller, ct) is null;
        bool canReopen = p.Status == InvestmentProposalStatus.Final && await MayReopenAsync(ctx, caller, ct);

        return new ProposalDto(
            p.Id,
            p.Status,
            Convert.ToBase64String(p.RowVersion),
            AsUtc(p.FinalizedAt),
            p.FinalizedById is Guid f ? names.GetValueOrDefault(f) : null,
            DateTime.SpecifyKind(p.UpdatedAt, DateTimeKind.Utc),
            p.UpdatedById is Guid u ? names.GetValueOrDefault(u) : null,
            aipChanged,
            canEdit,
            canReopen,
            // Decision 9: Project Cost × the HGDG band. Computed on read from the typed score, so
            // a Final proposal applies it to its frozen cost.
            shown.Header with { AttributedGadBudget = HgdgAttribution.AttributedBudget(p.HgdgScore, shown.Header.ProjectCost) },
            new ProposalWarningsDto(shown.Rows.Where(r => r.Expenditures.Count == 0)
                .Select(r => new ProposalActivityRefDto(r.ActivityId, r.RefCode, r.Name)).ToList()),
            frozenWorkPlan is null ? ContentOf(p, shown.Rows) : ContentOf(p, shown.Rows) with { WorkPlan = frozenWorkPlan },
            shown.Rows);
    }

    /// <summary>
    /// The editable content. <c>workPlan</c> always lists every current AIP activity: stored rows in
    /// their saved order, then any activity with no stored row, ungrouped (spec §4, decision 16).
    /// </summary>
    private static ProposalContentDto ContentOf(InvestmentProposal p, IReadOnlyList<ProposalAipRowDto> aipRows)
    {
        HashSet<int> current = aipRows.Select(r => r.ActivityId).ToHashSet();
        List<InvestmentProposalWorkPlanRow> stored = p.WorkPlanRows.OrderBy(r => r.SortOrder).ThenBy(r => r.Id).ToList();
        HashSet<int> storedActivityIds = stored.Where(r => r.AipActivityId is not null).Select(r => r.AipActivityId!.Value).ToHashSet();

        List<ProposalWorkPlanRowDto> workPlan = stored
            // A Final proposal's snapshot may not list an activity whose row survived; keep rows
            // only for activities the shown AIP has, and every proposal-only row.
            .Where(r => r.AipActivityId is null || current.Contains(r.AipActivityId.Value))
            .Select(r => new ProposalWorkPlanRowDto(
                r.Id, r.AipActivityId, r.Name, r.GroupId is int gid ? GroupKey(gid) : null,
                r.PerformanceTarget, r.GenderIssues, r.Timeline, r.Opr))
            .Concat(aipRows.Where(a => !storedActivityIds.Contains(a.ActivityId))
                .Select(a => new ProposalWorkPlanRowDto(null, a.ActivityId, null, null, null, null, null, null)))
            .ToList();

        return new ProposalContentDto(
            p.ProjectLocation,
            p.HgdgChecklist,
            p.HgdgScore,
            p.Beneficiaries.Where(b => b.Section == InvestmentProposalBeneficiarySection.Summary)
                .OrderBy(b => b.SortOrder).Select(b => new ProposalBeneficiaryDto(b.Id, b.Label, b.Male, b.Female)).ToList(),
            p.Description,
            p.Rationale,
            InvestmentProposalSector.All
                .Select(s => p.Benefits.FirstOrDefault(b => b.Sector == s) is { } b
                    ? new ProposalBenefitDto(s, b.Benefit, b.Cost)
                    : new ProposalBenefitDto(s, null, null))
                .ToList(),
            p.GeneralObjective,
            InvestmentProposalLogframeLevel.All
                .Select(l => p.Logframe.FirstOrDefault(x => x.Level == l) is { } x
                    ? new ProposalLogframeDto(l, x.Target, x.Verification)
                    : new ProposalLogframeDto(l, null, null))
                .ToList(),
            p.DirectSameAsSummary,
            p.Beneficiaries.Where(b => b.Section != InvestmentProposalBeneficiarySection.Summary)
                .OrderBy(b => b.Section == InvestmentProposalBeneficiarySection.Direct ? 0 : 1).ThenBy(b => b.SortOrder)
                .Select(b => new ProposalTargetBeneficiaryDto(b.Id, b.Section, b.Label, b.Male, b.Female)).ToList(),
            p.Groups.OrderBy(g => g.SortOrder).ThenBy(g => g.Id)
                .Select(g => new ProposalGroupDto(g.Id, GroupKey(g.Id), g.Label)).ToList(),
            workPlan,
            p.ProjectSupervisor,
            p.ProjectManager,
            p.TeamMembers.OrderBy(m => m.SortOrder)
                .Select(m => new ProposalTeamMemberDto(m.Id, m.Name, m.Sex, m.GadTrainings, m.Expertise, m.RequiredTraining)).ToList(),
            p.PartnershipSustainability,
            p.Monitoring.OrderBy(m => InvestmentProposalMonitoringPhase.All.ToList().IndexOf(m.Phase)).ThenBy(m => m.SortOrder)
                .Select(m => new ProposalMonitoringDto(m.Id, m.Phase, m.Activity, m.Schedule, m.Tools)).ToList(),
            p.Risks.OrderBy(r => r.SortOrder).Select(r => new ProposalRiskDto(r.Id, r.Risk, r.Prevention, r.Monitoring)).ToList(),
            p.WomensImpactStrategy,
            [
                new ProposalSignatoryDto(1, p.Signatory1Label, p.Signatory1Name, p.Signatory1Position),
                new ProposalSignatoryDto(2, p.Signatory2Label, p.Signatory2Name, p.Signatory2Position),
                new ProposalSignatoryDto(3, p.Signatory3Label, p.Signatory3Name, p.Signatory3Position),
                new ProposalSignatoryDto(4, p.Signatory4Label, p.Signatory4Name, p.Signatory4Position),
            ]);
    }

    private static string GroupKey(int groupId) => $"g{groupId}";

    private DateTime Now() => _clock.GetUtcNow().UtcDateTime;

    private static DateTime? AsUtc(DateTime? value)
        => value is DateTime v ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : null;
}
