namespace PPDO.Application.DTOs.PurchaseRequest;

/// <summary>Full PR detail response — includes all header fields and line items.</summary>
public sealed record PRResponseDto(
    Guid Id,
    string PRNo,
    DateOnly PRDate,
    DateTime DateCreated,
    string Department,
    /// <summary>Division name string e.g. "Admin" — not the enum integer.</summary>
    string Division,
    string Fund,
    string RequestedBy,
    string Position,
    string? ApprovedBy,
    string? ApprovingPosition,
    string? AIPCode,
    string? AccountNo,
    string? AccountTitle,
    string? Program,
    string? Project,
    string? Activity,
    string? SAINo,
    string? ALOBSNo,
    decimal TotalAmount,
    string Status,
    Guid CreatedById,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<PRItemDto> Items);
