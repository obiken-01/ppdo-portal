using PPDO.Domain.Enums;

namespace PPDO.Application.DTOs.Announcements;

/// <summary>Full announcement payload for admin management views.</summary>
public sealed record AnnouncementDto(
    Guid Id,
    string Title,
    string Content,
    AnnouncementStatus Status,
    DateTime? PublishedAt,
    Guid CreatedById,
    string CreatedByName,
    DateTime CreatedAt,
    DateTime UpdatedAt);
