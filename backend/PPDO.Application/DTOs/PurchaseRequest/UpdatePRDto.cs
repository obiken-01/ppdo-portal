using PPDO.Domain.Enums;

namespace PPDO.Application.DTOs.PurchaseRequest;

/// <summary>
/// Request body for PUT /api/purchase-requests/{id}.
/// Null properties are ignored (patch-style update).
/// Only allowed when PR Status = Open; Admin/SuperAdmin only.
/// </summary>
public sealed record UpdatePRDto
{
    public DateOnly? PRDate { get; init; }
    public string? Department { get; init; }
    public Division? Division { get; init; }
    public string? Fund { get; init; }
    public string? RequestedBy { get; init; }
    public string? Position { get; init; }
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
    public IReadOnlyList<CreatePRItemDto>? Items { get; init; }
}
