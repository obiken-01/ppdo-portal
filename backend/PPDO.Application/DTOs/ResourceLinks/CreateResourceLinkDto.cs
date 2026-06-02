namespace PPDO.Application.DTOs.ResourceLinks;

/// <summary>
/// Request body for <c>POST /api/resource-links</c>.
/// Available to Admin/SuperAdmin and Staff with CanManageResourceLinks = true.
/// </summary>
public sealed record CreateResourceLinkDto(
    string Title,
    string Url,
    string Category,
    int    CategoryOrder,
    int    LinkOrder);
