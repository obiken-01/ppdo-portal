using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
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
        ILogger<PurchaseRequestService> logger)
    {
        _prs         = prs;
        _items       = items;
        _permissions = permissions;
        _excel       = excel;
        _logger      = logger;
    }

    // ── GetAllAsync ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<PRSummaryDto>> GetAllAsync(
        User requester,
        PRStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PurchaseRequest> all;

        if (requester.Role is UserRole.Staff or UserRole.Observer)
            all = await _prs.GetByDivisionAsync(requester.Division, cancellationToken);
        else
            all = await _prs.GetAllAsync(cancellationToken);

        IEnumerable<PurchaseRequest> filtered = status.HasValue
            ? all.Where(pr => pr.Status == status.Value)
            : all;

        return filtered.OrderByDescending(pr => pr.PRDate)
                       .Select(MapToSummary)
                       .ToList();
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

        if (requester.Role is UserRole.Staff or UserRole.Observer
            && pr.Division != requester.Division)
        {
            _logger.LogWarning(
                "Permission denied — user {UserId} attempted to view PR {PRNo} from division {Division}.",
                requester.Id, pr.PRNo, pr.Division);
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

        // Parse Division string → enum (matches CreateUserDto pattern).
        if (!Enum.TryParse<Division>(dto.Division, ignoreCase: true, out Division division))
            return ServiceResult<PRResponseDto>.BadRequest(
                $"Invalid division '{dto.Division}'. Must be one of: Admin, Planning, RM, MIS, SPD.");

        // Staff can only submit PRs for their own division.
        if (requester.Role is UserRole.Staff or UserRole.Observer
            && division != requester.Division)
        {
            _logger.LogWarning(
                "Permission denied — user {UserId} attempted to create a PR for division {Division}.",
                requester.Id, division);
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
            Division          = division,
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
            "PR submitted. PRNo: {PRNo}, Division: {Division}, UserId: {UserId}",
            pr.PRNo, pr.Division, requester.Id);

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
            return ServiceResult<PRResponseDto>.Forbidden(
                "Only Admin users can update Purchase Requests.");

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
            if (!Enum.TryParse<Division>(dto.Division, ignoreCase: true, out Division updatedDivision))
                return ServiceResult<PRResponseDto>.BadRequest(
                    $"Invalid division '{dto.Division}'. Must be one of: Admin, Planning, RM, MIS, SPD.");
            pr.Division = updatedDivision;
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

        // Enforce division scope before creating anything.
        if (requester.Role is UserRole.Staff or UserRole.Observer)
        {
            IEnumerable<PurchaseRequestImportRow> wrongDivision =
                rows.Where(r => r.Division != requester.Division);

            if (wrongDivision.Any())
                return ServiceResult<IReadOnlyList<PRResponseDto>>.Forbidden(
                    "You can only import Purchase Requests for your own division.");
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
        int nextSeq = 1;

        IReadOnlyList<PurchaseRequest> allPRs = await _prs.GetAllAsync(cancellationToken);

        if (allPRs.Count > 0)
        {
            int maxSeq = allPRs
                .Select(pr => ParseSequence(pr.PRNo))
                .Where(n => n.HasValue)
                .Select(n => n!.Value)
                .DefaultIfEmpty(0)
                .Max();

            nextSeq = maxSeq + 1;
        }

        string dateSegment = manilaNow.ToString("yyyy-MM-dd");
        return $"101-1041-GF-{dateSegment}-{nextSeq:D3}";
    }

    /// <summary>Extracts the 3-digit sequence number from a PR number string.</summary>
    private static int? ParseSequence(string prNo)
    {
        // Format: 101-1041-GF-YYYY-MM-DD-XXX  → split by '-' gives 7 parts, index 6 = XXX
        string[] parts = prNo.Split('-');
        if (parts.Length >= 7 && int.TryParse(parts[^1], out int seq))
            return seq;
        return null;
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
        PRDate            = row.PRDate,
        Department        = row.Department,
        Division          = row.Division.ToString(),
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
        pr.Id, pr.PRNo, pr.PRDate, pr.Division.ToString(),
        pr.RequestedBy, pr.TotalAmount, pr.Status.ToString(), pr.CreatedAt);

    private static PRResponseDto MapToResponse(PurchaseRequest pr) => new(
        pr.Id, pr.PRNo, pr.PRDate, pr.DateCreated,
        pr.Department, pr.Division.ToString(), pr.Fund,
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
