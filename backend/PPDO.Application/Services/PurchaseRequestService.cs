using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;
using PPDO.Application.DTOs.PurchaseRequest;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Purchase Request business logic — PR number generation, division-scope enforcement,
/// Items Master auto-creation for unknown stock numbers, and Excel import orchestration.
///
/// PR No. format: 101-1041-GF-YYYY-MM-DD-XXX  (Manila time, global 3-digit sequence)
/// Status transitions (Open → PartiallyDelivered → FullyDelivered) are handled by DeliveryService.
/// </summary>
public sealed class PurchaseRequestService : IPurchaseRequestService
{
    private readonly IPurchaseRequestRepository _prs;
    private readonly IItemMasterRepository      _items;
    private readonly IPermissionService         _permissions;
    private readonly IExcelService              _excel;
    private readonly IAccountService            _accounts;
    private readonly IRepository<Division>      _divisions;
    private readonly IOfficeRepository          _offices;
    private readonly ILogger<PurchaseRequestService> _logger;

    // Manila is UTC+8. Try IANA first (Linux/Azure), fall back to Windows identifier.
    private static readonly TimeZoneInfo ManilaZone = LoadManilaZone();

    private static TimeZoneInfo LoadManilaZone()
    {
        try   { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila"); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time"); }
    }

    public PurchaseRequestService(
        IPurchaseRequestRepository prs,
        IItemMasterRepository items,
        IPermissionService permissions,
        IExcelService excel,
        IAccountService accounts,
        IRepository<Division> divisions,
        IOfficeRepository offices,
        ILogger<PurchaseRequestService> logger)
    {
        _prs         = prs;
        _items       = items;
        _permissions = permissions;
        _excel       = excel;
        _accounts    = accounts;
        _divisions   = divisions;
        _offices     = offices;
        _logger      = logger;
    }

    /// <summary>
    /// office_code of PPDO itself. Inventory is a PPDO-internal feature — a Purchase Request
    /// always belongs to one of PPDO's own divisions, never another office's.
    /// Mirrors PPDO_OFFICE_CODE in frontend/src/lib/config.ts.
    /// </summary>
    private const string PpdoOfficeCode = "PPDO";

    /// <summary>
    /// Resolves a division name (or code) to its active configurable division, or null
    /// (v1.2 — RAL-97).
    ///
    /// Matching is scoped to PPDO's own divisions. Division names are only unique WITHIN an
    /// office, so an unscoped match is ambiguous: "Administrative" exists under several
    /// offices, and an unscoped FirstOrDefault could silently attach a PPDO purchase request
    /// to another office's division. If the PPDO office row is missing (misconfigured), this
    /// falls back to matching across all active divisions rather than failing every create.
    ///
    /// Both Name and Code are accepted (case-insensitive, trimmed) — Name wins. The Excel
    /// import path carries whatever the user typed into the Division cell, where the short
    /// code ("ADMIN") is as likely as the full name ("Administrative Division").
    /// </summary>
    private async Task<Division?> ResolveDivisionByNameAsync(string? name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        string needle = name.Trim();
        IReadOnlyList<Division> candidates = await GetSelectableDivisionsAsync(ct);

        return candidates.FirstOrDefault(d => string.Equals(d.Name, needle, StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault(d => string.Equals(d.Code, needle, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The active divisions a Purchase Request may be assigned to — PPDO's own, or all active
    /// divisions if the PPDO office row is not configured.
    /// </summary>
    private async Task<IReadOnlyList<Division>> GetSelectableDivisionsAsync(CancellationToken ct)
    {
        // Sequential awaits — never Task.WhenAll over a shared DbContext (see CLAUDE.md).
        Office? ppdo = await _offices.GetByCodeAsync(PpdoOfficeCode, ct);
        IReadOnlyList<Division> all = await _divisions.GetAllAsync(ct);

        List<Division> active = all.Where(d => d.IsActive).ToList();
        if (ppdo is null) return active;

        List<Division> scoped = active.Where(d => d.OfficeId == ppdo.Id).ToList();
        return scoped.Count > 0 ? scoped : active;
    }

    /// <summary>
    /// Builds the "not found" message for an unresolvable division, listing what IS valid.
    /// Only called on the failure path — the extra read never runs on a successful create.
    /// </summary>
    private async Task<string> DivisionNotFoundMessageAsync(string? supplied, CancellationToken ct)
    {
        IReadOnlyList<Division> candidates = await GetSelectableDivisionsAsync(ct);

        if (candidates.Count == 0)
            return "No active divisions are configured. Add one in Config → Divisions first.";

        string valid = string.Join(", ", candidates.Select(d => d.Name).OrderBy(n => n));
        return $"Division '{supplied}' was not found. Valid divisions are: {valid}.";
    }

    // ── GetAllAsync ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<PRSummaryDto>> GetAllAsync(
        User requester,
        PRStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        // Division scope — office users (Staff/Observer with no division) see nothing,
        // never the all-divisions list. See DivisionScope.
        DivisionScope scope = DivisionScope.Resolve(requester);
        if (scope.SeeNothing)
            return Array.Empty<PRSummaryDto>();

        IReadOnlyList<PurchaseRequest> all = scope.SeeAll
            ? await _prs.GetAllAsync(cancellationToken)
            : await _prs.GetByDivisionAsync(scope.DivisionId!.Value, cancellationToken);

        IEnumerable<PurchaseRequest> filtered = status.HasValue
            ? all.Where(pr => pr.Status == status.Value)
            : all;

        return filtered.OrderByDescending(pr => pr.PRDate)
                       .Select(MapToSummary)
                       .ToList();
    }

    // ── SearchAsync ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<PRSearchResultDto> SearchAsync(
        User requester,
        PRSearchFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        DivisionScope scope = DivisionScope.Resolve(requester);
        if (scope.SeeNothing)
            return new PRSearchResultDto(
                Array.Empty<PRSummaryDto>(), 0, filter.Page, filter.PageSize,
                new PRStatusCountsDto(0, 0, 0, 0));

        PRSearchCriteria criteria = new(
            Search:          filter.Search,
            DateFrom:        filter.DateFrom,
            DateTo:          filter.DateTo,
            Statuses:        filter.Statuses,
            Division:        filter.Division,
            RequestedBy:     filter.RequestedBy,
            Fund:            filter.Fund,
            AIPCode:         filter.AIPCode,
            AccountNo:       filter.AccountNo,
            AccountTitle:    filter.AccountTitle,
            Program:         filter.Program,
            Project:         filter.Project,
            Activity:        filter.Activity,
            SortBy:          filter.SortBy,
            SortDescending:  filter.SortDescending,
            Page:            filter.Page,
            PageSize:        filter.PageSize);

        PRSearchResult result = await _prs.SearchAsync(criteria, scope.DivisionId, cancellationToken);

        return new PRSearchResultDto(
            result.Items.Select(MapToSummary).ToList(),
            result.TotalCount,
            filter.Page,
            filter.PageSize,
            new PRStatusCountsDto(
                result.Counts.Open, result.Counts.PartiallyDelivered,
                result.Counts.FullyDelivered, result.Counts.Completed));
    }

    // ── GetByIdAsync ───────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ServiceResult<PRResponseDto>> GetByIdAsync(
        User requester,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        PurchaseRequest? pr = await _prs.GetWithItemsAsync(id, cancellationToken);
        if (pr is null)
            return ServiceResult<PRResponseDto>.NotFound($"Purchase Request {id} not found.");

        if (requester.Role is UserRole.Staff
            && pr.DivisionId != requester.DivisionId)
        {
            _logger.LogWarning(
                "Permission denied — user {UserId} attempted to view PR {PRNo} from division {DivisionId}. Feature: ViewPR",
                requester.Id, pr.PRNo, pr.DivisionId);
            return ServiceResult<PRResponseDto>.Forbidden(
                "You can only view Purchase Requests from your own division.");
        }

        return ServiceResult<PRResponseDto>.Ok(MapToResponse(pr));
    }

    // ── CreateAsync ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ServiceResult<PRResponseDto>> CreateAsync(
        User requester,
        CreatePRDto dto,
        CancellationToken cancellationToken = default)
    {
        if (!await _permissions.CanAccessInventoryAsync(requester, cancellationToken))
        {
            _logger.LogWarning(
                "Permission denied — user {UserId} attempted to create a PR without CanAccessInventory.",
                requester.Id);
            return ServiceResult<PRResponseDto>.Forbidden(
                "You do not have permission to create Purchase Requests.");
        }

        // Resolve the division name to a configurable division id (v1.2 — RAL-97).
        Division? division = await ResolveDivisionByNameAsync(dto.Division, cancellationToken);
        if (division is null)
            return ServiceResult<PRResponseDto>.BadRequest(
                await DivisionNotFoundMessageAsync(dto.Division, cancellationToken));

        // Staff can only submit PRs for their own division.
        if (requester.Role is UserRole.Staff
            && division.Id != requester.DivisionId)
        {
            _logger.LogWarning(
                "Permission denied — user {UserId} attempted to create a PR for division {DivisionId}.",
                requester.Id, division.Id);
            return ServiceResult<PRResponseDto>.Forbidden(
                "You can only create Purchase Requests for your own division.");
        }

        if (dto.Items is null || dto.Items.Count == 0)
            return ServiceResult<PRResponseDto>.BadRequest(
                "A Purchase Request must have at least one line item.");

        DateTime manilaNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ManilaZone);
        string prNo = !string.IsNullOrWhiteSpace(dto.PrNo)
            ? dto.PrNo.Trim()
            : await GeneratePRNoAsync(manilaNow, cancellationToken);
        DateTime utcNow = DateTime.UtcNow;

        PurchaseRequest pr = new()
        {
            Id                = Guid.NewGuid(),
            PRNo              = prNo,
            PRDate            = dto.PRDate,
            DateCreated       = utcNow,
            Department        = string.IsNullOrWhiteSpace(dto.Department) ? "PPDO" : dto.Department.Trim(),
            DivisionId        = division.Id,
            Fund              = dto.Fund.Trim(),
            RequestedBy       = dto.RequestedBy.Trim(),
            Position          = dto.Position.Trim(),
            ApprovedBy        = dto.ApprovedBy?.Trim(),
            ApprovingPosition = dto.ApprovingPosition?.Trim(),
            AIPCode           = dto.AIPCode?.Trim(),
            AccountNo         = dto.AccountNo?.Trim(),
            AccountTitle      = dto.AccountTitle?.Trim(),
            Program           = dto.Program?.Trim(),
            Project           = dto.Project?.Trim(),
            Activity          = dto.Activity?.Trim(),
            SAINo             = dto.SAINo?.Trim(),
            ALOBSNo           = dto.ALOBSNo?.Trim(),
            Status            = PRStatus.Open,
            CreatedById       = requester.Id,
            CreatedAt         = utcNow,
            UpdatedAt         = utcNow,
        };

        IReadOnlyList<PRItem> items =
            await BuildItemsAsync(pr.Id, dto.Items, manilaNow, cancellationToken);

        pr.TotalAmount = items.Sum(i => i.TotalCost);
        pr.Items       = items.ToList();

        await _prs.AddAsync(pr, cancellationToken);
        await _prs.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "PR submitted. PRNo: {PRNo}, DivisionId: {DivisionId}, UserId: {UserId}",
            pr.PRNo, pr.DivisionId, requester.Id);

        return ServiceResult<PRResponseDto>.Ok(MapToResponse(pr));
    }

    // ── UpdateAsync ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ServiceResult<PRResponseDto>> UpdateAsync(
        User requester,
        Guid id,
        UpdatePRDto dto,
        CancellationToken cancellationToken = default)
    {
        // Only Admin/SuperAdmin can update PRs.
        if (requester.Role is not (UserRole.Admin or UserRole.SuperAdmin))
        {
            _logger.LogWarning(
                "Permission denied — user {UserId} (Role: {Role}) attempted to update PR {PRId}. Feature: UpdatePR",
                requester.Id, requester.Role, id);
            return ServiceResult<PRResponseDto>.Forbidden(
                "Only Admin users can update Purchase Requests.");
        }

        PurchaseRequest? pr = await _prs.GetWithItemsAsync(id, cancellationToken);
        if (pr is null)
            return ServiceResult<PRResponseDto>.NotFound($"Purchase Request {id} not found.");

        if (pr.Status != PRStatus.Open)
            return ServiceResult<PRResponseDto>.BadRequest(
                "Only Open Purchase Requests can be updated.");

        DateTime utcNow     = DateTime.UtcNow;
        DateTime manilaNow  = TimeZoneInfo.ConvertTimeFromUtc(utcNow, ManilaZone);

        if (dto.PRDate is not null)     pr.PRDate     = dto.PRDate.Value;
        if (dto.Department is not null) pr.Department = dto.Department.Trim();
        if (dto.Division is not null)
        {
            Division? updatedDivision = await ResolveDivisionByNameAsync(dto.Division, cancellationToken);
            if (updatedDivision is null)
                return ServiceResult<PRResponseDto>.BadRequest(
                    await DivisionNotFoundMessageAsync(dto.Division, cancellationToken));
            pr.DivisionId = updatedDivision.Id;
        }
        if (dto.Fund is not null)              pr.Fund              = dto.Fund.Trim();
        if (dto.RequestedBy is not null)       pr.RequestedBy       = dto.RequestedBy.Trim();
        if (dto.Position is not null)          pr.Position          = dto.Position.Trim();
        if (dto.ApprovedBy is not null)        pr.ApprovedBy        = string.IsNullOrWhiteSpace(dto.ApprovedBy) ? null : dto.ApprovedBy.Trim();
        if (dto.ApprovingPosition is not null) pr.ApprovingPosition = string.IsNullOrWhiteSpace(dto.ApprovingPosition) ? null : dto.ApprovingPosition.Trim();
        if (dto.AIPCode is not null)           pr.AIPCode           = string.IsNullOrWhiteSpace(dto.AIPCode) ? null : dto.AIPCode.Trim();
        if (dto.AccountNo is not null)         pr.AccountNo         = string.IsNullOrWhiteSpace(dto.AccountNo) ? null : dto.AccountNo.Trim();
        if (dto.AccountTitle is not null)      pr.AccountTitle      = string.IsNullOrWhiteSpace(dto.AccountTitle) ? null : dto.AccountTitle.Trim();
        if (dto.Program is not null)           pr.Program           = string.IsNullOrWhiteSpace(dto.Program) ? null : dto.Program.Trim();
        if (dto.Project is not null)           pr.Project           = string.IsNullOrWhiteSpace(dto.Project) ? null : dto.Project.Trim();
        if (dto.Activity is not null)          pr.Activity          = string.IsNullOrWhiteSpace(dto.Activity) ? null : dto.Activity.Trim();
        if (dto.SAINo is not null)             pr.SAINo             = string.IsNullOrWhiteSpace(dto.SAINo) ? null : dto.SAINo.Trim();
        if (dto.ALOBSNo is not null)           pr.ALOBSNo           = string.IsNullOrWhiteSpace(dto.ALOBSNo) ? null : dto.ALOBSNo.Trim();

        if (dto.Items is not null)
        {
            if (dto.Items.Count == 0)
                return ServiceResult<PRResponseDto>.BadRequest(
                    "A Purchase Request must have at least one line item.");

            IReadOnlyList<PRItem> newItems =
                await BuildItemsAsync(pr.Id, dto.Items, manilaNow, cancellationToken);

            pr.Items       = newItems.ToList();
            pr.TotalAmount = newItems.Sum(i => i.TotalCost);
        }

        pr.UpdatedAt = utcNow;

        await _prs.UpdateAsync(pr, cancellationToken);
        await _prs.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "PR updated. PRNo: {PRNo}, UserId: {UserId}",
            pr.PRNo, requester.Id);

        return ServiceResult<PRResponseDto>.Ok(MapToResponse(pr));
    }

    // ── MarkCompletedAsync ─────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ServiceResult<PRSummaryDto>> MarkCompletedAsync(
        User requester,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!await _permissions.CanAccessInventoryAsync(requester, cancellationToken))
        {
            _logger.LogWarning(
                "Permission denied — user {UserId} attempted to mark PR {PRId} as Completed without CanAccessInventory.",
                requester.Id, id);
            return ServiceResult<PRSummaryDto>.Forbidden(
                "You do not have permission to access Inventory.");
        }

        PurchaseRequest? pr = await _prs.GetByIdAsync(id, cancellationToken);
        if (pr is null)
            return ServiceResult<PRSummaryDto>.NotFound($"Purchase Request {id} not found.");

        if (pr.Status != PRStatus.FullyDelivered)
            return ServiceResult<PRSummaryDto>.BadRequest(
                $"Only Fully Delivered PRs can be marked as Completed. Current status: {pr.Status}.");

        pr.Status    = PRStatus.Completed;
        pr.UpdatedAt = DateTime.UtcNow;

        await _prs.UpdateAsync(pr, cancellationToken);
        await _prs.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "PR status changed. PRNo: {PRNo}, OldStatus: {OldStatus}, NewStatus: {NewStatus}",
            pr.PRNo, PRStatus.FullyDelivered, PRStatus.Completed);

        return ServiceResult<PRSummaryDto>.Ok(MapToSummary(pr));
    }

    // ── UnmarkCompletedAsync ───────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ServiceResult<PRSummaryDto>> UnmarkCompletedAsync(
        User requester,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!await _permissions.CanAccessInventoryAsync(requester, cancellationToken))
        {
            _logger.LogWarning(
                "Permission denied — user {UserId} attempted to unmark PR {PRId} without CanAccessInventory.",
                requester.Id, id);
            return ServiceResult<PRSummaryDto>.Forbidden(
                "You do not have permission to access Inventory.");
        }

        PurchaseRequest? pr = await _prs.GetByIdAsync(id, cancellationToken);
        if (pr is null)
            return ServiceResult<PRSummaryDto>.NotFound($"Purchase Request {id} not found.");

        if (pr.Status != PRStatus.Completed)
            return ServiceResult<PRSummaryDto>.BadRequest(
                $"Only Completed PRs can be reverted. Current status: {pr.Status}.");

        pr.Status    = PRStatus.FullyDelivered;
        pr.UpdatedAt = DateTime.UtcNow;

        await _prs.UpdateAsync(pr, cancellationToken);
        await _prs.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "PR status changed. PRNo: {PRNo}, OldStatus: {OldStatus}, NewStatus: {NewStatus}",
            pr.PRNo, PRStatus.Completed, PRStatus.FullyDelivered);

        return ServiceResult<PRSummaryDto>.Ok(MapToSummary(pr));
    }

    // ── GetTemplateAsync ───────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ServiceResult<byte[]>> GetTemplateAsync(
        User requester,
        CancellationToken cancellationToken = default)
    {
        if (!await _permissions.CanAccessInventoryAsync(requester, cancellationToken))
            return ServiceResult<byte[]>.Forbidden(
                "You do not have permission to access Inventory.");

        byte[] bytes = _excel.GeneratePRTemplate();
        return ServiceResult<byte[]>.Ok(bytes);
    }

    // ── ImportFromExcelAsync ───────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<PRResponseDto>>> ImportFromExcelAsync(
        User requester,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        if (!await _permissions.CanAccessInventoryAsync(requester, cancellationToken))
            return ServiceResult<IReadOnlyList<PRResponseDto>>.Forbidden(
                "You do not have permission to access Inventory.");

        IReadOnlyList<PurchaseRequestImportRow> rows;
        try
        {
            rows = _excel.ParsePRImport(stream);
        }
        catch (ExcelParseException ex)
        {
            return ServiceResult<IReadOnlyList<PRResponseDto>>.BadRequest(
                $"Excel import failed: {string.Join("; ", ex.Errors)}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error parsing PR import file for user {UserId}.", requester.Id);
            return ServiceResult<IReadOnlyList<PRResponseDto>>.BadRequest(
                "The uploaded file could not be read. Ensure it is a valid .xlsx file.");
        }

        if (rows.Count == 0)
            return ServiceResult<IReadOnlyList<PRResponseDto>>.BadRequest(
                "No PR worksheets found in the uploaded file.");

        // Enforce division scope before creating anything (Staff only).
        //
        // Compare RESOLVED division ids, not raw strings: the sheet's Division cell is free
        // text and may legitimately hold the division's short code ("ADMIN") rather than its
        // full name. A raw string comparison against requester.Division.Name rejected every
        // such file — and rejected valid ones outright once the seeded division names stopped
        // matching the retired v1.2 enum labels.
        if (requester.Role is UserRole.Staff)
        {
            foreach (PurchaseRequestImportRow row in rows)
            {
                Division? rowDivision =
                    await ResolveDivisionByNameAsync(row.DivisionName, cancellationToken);

                if (rowDivision is null)
                    return ServiceResult<IReadOnlyList<PRResponseDto>>.BadRequest(
                        $"Sheet '{row.SheetName}': " +
                        await DivisionNotFoundMessageAsync(row.DivisionName, cancellationToken));

                if (rowDivision.Id != requester.DivisionId)
                    return ServiceResult<IReadOnlyList<PRResponseDto>>.Forbidden(
                        "You can only import Purchase Requests for your own division.");
            }
        }

        List<PRResponseDto> created = new(rows.Count);

        foreach (PurchaseRequestImportRow row in rows)
        {
            CreatePRDto dto = MapImportRowToDto(row);
            ServiceResult<PRResponseDto> result = await CreateAsync(requester, dto, cancellationToken);

            if (!result.IsSuccess)
                return ServiceResult<IReadOnlyList<PRResponseDto>>.BadRequest(
                    $"Sheet '{row.SheetName}': {result.Error}");

            created.Add(result.Value!);
        }

        _logger.LogInformation(
            "Excel import complete. PRsCreated: {Count}, UserId: {UserId}",
            created.Count, requester.Id);

        return ServiceResult<IReadOnlyList<PRResponseDto>>.Ok(created);
    }

    // ── PreviewGsoImportAsync ──────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ServiceResult<GsoPRImportPreviewDto>> PreviewGsoImportAsync(
        User requester,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        if (!await _permissions.CanAccessInventoryAsync(requester, cancellationToken))
            return ServiceResult<GsoPRImportPreviewDto>.Forbidden(
                "You do not have permission to access Inventory.");

        GsoPRImportRow row;
        try
        {
            row = _excel.ParseGsoPRImport(stream);
        }
        catch (ExcelParseException ex)
        {
            return ServiceResult<GsoPRImportPreviewDto>.BadRequest(
                $"Could not read the file: {string.Join("; ", ex.Errors)}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error parsing GSO PR import file for user {UserId}.", requester.Id);
            return ServiceResult<GsoPRImportPreviewDto>.BadRequest(
                "The uploaded file could not be read. Ensure it is a valid .xlsx file.");
        }

        // Resolve Account Title from the Config Accounts table — exact match on AccountNumber.
        // Sequential awaits — never Task.WhenAll over queries sharing one DbContext.
        string? accountTitle = null;
        if (!string.IsNullOrWhiteSpace(row.AccountNo))
        {
            IReadOnlyList<AccountDto> matches = await _accounts.GetAllAsync(
                search: row.AccountNo, accountType: null, active: ActiveFilter.Active,
                cancellationToken: cancellationToken);
            accountTitle = matches.FirstOrDefault(a =>
                a.AccountNumber.Equals(row.AccountNo, StringComparison.OrdinalIgnoreCase))?.AccountTitle;
        }

        // Flag StockNos not found in Items Master — the same "needs review" signal manual
        // entry surfaces, via a single IN-list query (never a per-item lookup).
        List<string> stockNos = row.Items
            .Where(i => !string.IsNullOrWhiteSpace(i.StockNo))
            .Select(i => i.StockNo!)
            .ToList();
        IReadOnlyList<ItemMaster> known = stockNos.Count > 0
            ? await _items.GetByStockNosAsync(stockNos, cancellationToken)
            : Array.Empty<ItemMaster>();
        HashSet<string> knownStockNos = known
            .Select(i => i.StockNo)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        GsoPRImportPreviewDto dto = new(
            PrNo:         row.PrNo,
            Fund:         row.Fund,
            PRDate:       row.PRDate,
            Purpose:      row.Purpose,
            AIPCode:      row.AIPCode,
            AccountNo:    row.AccountNo,
            AccountTitle: accountTitle,
            Program:      row.Program,
            Project:      row.Project,
            Activity:     row.Activity,
            Items: row.Items.Select(i => new GsoPRImportItemDto(
                    StockNo:        i.StockNo,
                    Description:    i.Description,
                    Unit:           i.Unit,
                    Quantity:       i.Quantity,
                    UnitCost:       i.UnitCost,
                    IsUnknownStock: i.StockNo is not null && !knownStockNos.Contains(i.StockNo)))
                .ToList());

        return ServiceResult<GsoPRImportPreviewDto>.Ok(dto);
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Generates the next PR number: 101-1041-GF-YYYY-MM-DD-XXX.
    /// The sequence number (XXX) is global — it reads the highest existing
    /// sequence across all PRs and increments by 1, zero-padded to 3 digits.
    /// YYYY-MM-DD uses Manila time.
    /// </summary>
    private async Task<string> GeneratePRNoAsync(
        DateTime manilaNow,
        CancellationToken cancellationToken)
    {
        int? maxSeq = await _prs.GetMaxPrSequenceAsync(cancellationToken);
        int nextSeq = (maxSeq ?? 0) + 1;

        string dateSegment = manilaNow.ToString("yyyy-MM-dd");
        return $"101-1041-GF-{dateSegment}-{nextSeq:D3}";
    }

    /// <summary>
    /// Builds PRItem entities from the submitted DTO items.
    /// For each item with a StockNo, looks up the Items Master.
    /// If the StockNo is unknown, auto-creates a new ItemMaster entry with IsNewItem = true.
    /// </summary>
    private async Task<IReadOnlyList<PRItem>> BuildItemsAsync(
        Guid prId,
        IReadOnlyList<CreatePRItemDto> dtoItems,
        DateTime manilaNow,
        CancellationToken cancellationToken)
    {
        List<PRItem> items = new(dtoItems.Count);

        for (int i = 0; i < dtoItems.Count; i++)
        {
            CreatePRItemDto d = dtoItems[i];

            decimal unitCost  = d.UnitCost;
            string? itemType  = d.ItemType;
            string  stockNo   = d.StockNo?.Trim() ?? string.Empty;

            if (!string.IsNullOrEmpty(stockNo))
            {
                ItemMaster? master = await _items.GetByStockNoAsync(stockNo, cancellationToken);

                if (master is null)
                {
                    // Auto-create the unknown stock number and flag for admin review.
                    master = new ItemMaster
                    {
                        Id          = Guid.NewGuid(),
                        StockNo     = stockNo,
                        Description = d.Description.Trim(),
                        Unit        = d.Unit.Trim(),
                        UnitCost    = d.UnitCost,
                        ItemType    = d.ItemType?.Trim(),
                        IsNewItem   = true,
                        ReorderQty  = 0,
                        CreatedAt   = DateTime.UtcNow,
                        UpdatedAt   = DateTime.UtcNow,
                    };

                    await _items.AddAsync(master, cancellationToken);

                    _logger.LogWarning(
                        "Unknown StockNo auto-created. StockNo: {StockNo}, flagged IsNewItem = true.",
                        stockNo);
                }
                else
                {
                    // Prefer Items Master values for unit cost and type.
                    unitCost = master.UnitCost;
                    itemType = master.ItemType;
                }
            }

            decimal totalCost = d.Quantity * unitCost;

            items.Add(new PRItem
            {
                Id          = Guid.NewGuid(),
                PRId        = prId,
                ItemNo      = i + 1,
                StockNo     = string.IsNullOrEmpty(stockNo) ? null : stockNo,
                Description = d.Description.Trim(),
                Unit        = d.Unit.Trim(),
                Quantity    = d.Quantity,
                UnitCost    = unitCost,
                TotalCost   = totalCost,
                ItemType    = itemType?.Trim(),
            });
        }

        return items;
    }

    /// <summary>Maps a parsed Excel import row to a CreatePRDto.</summary>
    private static CreatePRDto MapImportRowToDto(PurchaseRequestImportRow row) => new()
    {
        PrNo              = row.PrNo,
        PRDate            = row.PRDate,
        Department        = row.Department,
        Division          = row.DivisionName,
        Fund              = row.Fund ?? "General Fund",
        RequestedBy       = row.RequestedBy,
        Position          = row.Position ?? string.Empty,
        ApprovedBy        = row.ApprovedBy,
        ApprovingPosition = row.ApprovingPosition,
        AIPCode           = row.AIPCode,
        AccountNo         = row.AccountNo,
        AccountTitle      = row.AccountTitle,
        Program           = row.Program,
        Project           = row.Project,
        Activity          = row.Activity,
        SAINo             = row.SAINo,
        ALOBSNo           = row.ALOBSNo,
        Items             = row.Items.Select(i => new CreatePRItemDto
        {
            StockNo     = i.StockNo,
            Description = i.Description,
            Unit        = i.Unit,
            Quantity    = i.Quantity,
            UnitCost    = i.UnitCost,
        }).ToList(),
    };

    // ── Mapping ────────────────────────────────────────────────────────────────

    private static PRSummaryDto MapToSummary(PurchaseRequest pr) => new(
        pr.Id, pr.PRNo, pr.PRDate, pr.Division?.Name ?? "",
        pr.RequestedBy, pr.TotalAmount, pr.Status.ToString(), pr.CreatedAt,
        pr.Fund, pr.AIPCode, pr.AccountNo, pr.AccountTitle,
        pr.Program, pr.Project, pr.Activity);

    private static PRResponseDto MapToResponse(PurchaseRequest pr) => new(
        pr.Id, pr.PRNo, pr.PRDate, pr.DateCreated,
        pr.Department, pr.Division?.Name ?? "", pr.Fund,
        pr.RequestedBy, pr.Position,
        pr.ApprovedBy, pr.ApprovingPosition,
        pr.AIPCode, pr.AccountNo, pr.AccountTitle,
        pr.Program, pr.Project, pr.Activity,
        pr.SAINo, pr.ALOBSNo,
        pr.TotalAmount, pr.Status.ToString(),
        pr.CreatedById, pr.CreatedAt, pr.UpdatedAt,
        pr.Items.OrderBy(i => i.ItemNo)
                .Select(i => new PRItemDto(
                    i.Id, i.PRId, i.ItemNo, i.StockNo,
                    i.Description, i.Unit, i.Quantity,
                    i.UnitCost, i.TotalCost, i.ItemType))
                .ToList());
}
