namespace PPDO.Application.DTOs.PurchaseRequest;

/// <summary>
/// PR Report response — the three sections of the official PR Report form.
///
/// Section 1 + 2 are carried by <see cref="PR"/> (header fields + line items).
/// Section 3 is the flat distribution list showing how delivered quantities
/// were split across divisions per delivery event.
/// </summary>
public sealed record PRReportDto(
    /// <summary>Section 1 (header fields) and Section 2 (line items).</summary>
    PRResponseDto PR,
    /// <summary>Section 3 — flat distribution rows, ordered by ItemNo then DeliveryDate.</summary>
    IReadOnlyList<PRReportDistributionDto> Distributions);
