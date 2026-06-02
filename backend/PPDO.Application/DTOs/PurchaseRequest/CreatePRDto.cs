using PPDO.Domain.Enums;

namespace PPDO.Application.DTOs.PurchaseRequest;

/// <summary>
/// Request body for POST /api/purchase-requests.
/// Staff can only submit a PR for their own division — enforced in PurchaseRequestService.
/// </summary>
public sealed record CreatePRDto
{
    public required DateOnly PRDate { get; init; }
    public string Department { get; init; } = "PPDO";
    public required Division Division { get; init; }
    public required string Fund { get; init; }
    public required string RequestedBy { get; init; }
    public required string Position { get; init; }
    public string? ApprovedBy { get; init; }
    public string? ApprovingPosition { get; init; }
    public string? AIPCode { get; init; }
    public string? AccountNo { get; init; }
    public string? AccountTitle { get; init; }
    public string? Program { get; init; }
    public string? Project { get; init; }
    public string? Activity { get; init; }
    public string? SAINo { get; init; }
    public string? ALOBSNo { get; init; }
    public required IReadOnlyList<CreatePRItemDto> Items { get; init; }
}
