namespace PPDO.Application.DTOs.PurchaseRequest;

/// <summary>
/// Request body for POST /api/purchase-requests.
/// Division is accepted as a string name (e.g. "Admin") — parsed to the Division enum
/// in PurchaseRequestService, matching the pattern used by CreateUserDto.
/// Staff can only submit a PR for their own division — enforced in PurchaseRequestService.
/// </summary>
public sealed record CreatePRDto
{
    public required DateOnly PRDate { get; init; }
    /// <summary>
    /// Optional. When provided and non-empty the supplied value is used as the PR No.
    /// If null or empty the backend auto-generates using the standard format.
    /// </summary>
    public string? PrNo { get; init; }
    public string Department { get; init; } = "PPDO";
    /// <summary>"Admin" | "Planning" | "RM" | "MIS" | "SPD"</summary>
    public required string Division { get; init; }
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
