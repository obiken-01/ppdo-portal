namespace PPDO.Application.DTOs.PurchaseRequest;

/// <summary>
/// A single row in PR Report Section 3 — Distribution.
/// Each row represents one distribution line (one division's allocation
/// for one delivery item in one delivery event).
/// Division is a string name (e.g. "Admin") — not the enum integer.
/// </summary>
public sealed record PRReportDistributionDto(
    int ItemNo,
    string Description,
    string Unit,
    decimal QtyDelivered,
    string DeliveryRef,
    DateOnly DeliveryDate,
    string Division,
    decimal QtyIssued,
    string IssueRef,
    DateOnly DateIssued,
    string IssuedBy,
    string? Remarks);
