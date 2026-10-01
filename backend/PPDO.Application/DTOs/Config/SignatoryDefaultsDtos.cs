namespace PPDO.Application.DTOs.Config;

/// <summary>
/// The investment proposal signatory defaults (PPDO-155, Investment_Proposal_Spec.md decision 25).
/// Copied into slots 2 and 3 when a proposal is created. Null = unset: the slot is created with its
/// label only.
/// </summary>
public record SignatoryDefaultsDto(
    string? PpdcName,
    string? PpdcPosition,
    string? LceName,
    string? LcePosition);

/// <summary>PUT body. Each value ≤ 200; blank clears it.</summary>
public record UpdateSignatoryDefaultsDto(
    string? PpdcName,
    string? PpdcPosition,
    string? LceName,
    string? LcePosition);
