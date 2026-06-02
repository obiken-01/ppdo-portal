namespace PPDO.Application.DTOs.ResourceLinks;

/// <summary>
/// A category group returned by <c>GET /api/resource-links</c>.
/// Links within each category are sorted by <see cref="ResourceLinkDto.LinkOrder"/>.
/// </summary>
public sealed record ResourceLinkCategoryDto(
    string                     Category,
    int                        CategoryOrder,
    List<ResourceLinkDto>      Links);
