namespace PPDO.Application.DTOs.ResourceLinks;

/// <summary>
/// A single resource link returned in API responses.
/// </summary>
public sealed record ResourceLinkDto(
    Guid    Id,
    string  Title,
    string  Url,
    string  Category,
    int     CategoryOrder,
    int     LinkOrder,
    bool    IsActive,
    bool    IsAdminCreated,
    Guid?   SubmittedById);
