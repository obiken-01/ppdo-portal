namespace PPDO.Application.DTOs.Announcements;

/// <summary>Minimal announcement payload for the public landing page (no auth required).</summary>
public sealed record AnnouncementPublicDto(
    Guid Id,
    string Title,
    string Content,
    DateTime PublishedAt);
