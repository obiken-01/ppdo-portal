namespace PPDO.Application.DTOs.ResourceLinks;

/// <summary>
/// Request body for <c>PUT /api/resource-links/{id}</c>.
/// Admin / SuperAdmin only — Staff may not edit links.
/// </summary>
public sealed record UpdateResourceLinkDto(
    string Title,
    string Url,
    string Category,
    int    CategoryOrder,
    int    LinkOrder);
