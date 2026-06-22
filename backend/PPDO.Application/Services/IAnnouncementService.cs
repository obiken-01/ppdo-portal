using PPDO.Application.Common;
using PPDO.Application.DTOs.Announcements;
using PPDO.Domain.Entities;

namespace PPDO.Application.Services;

/// <summary>
/// Announcement CRUD and lifecycle management (RAL-83).
/// Write operations require Admin or SuperAdmin role.
/// GetPublishedAsync is public — no caller required.
/// </summary>
public interface IAnnouncementService
{
    /// <summary>Returns all Published announcements ordered by PublishedAt DESC. No auth required.</summary>
    Task<IReadOnlyList<AnnouncementPublicDto>> GetPublishedAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all announcements (all statuses). Admin/SuperAdmin only.</summary>
    Task<ServiceResult<IReadOnlyList<AnnouncementDto>>> GetAllAsync(User caller, CancellationToken cancellationToken = default);

    /// <summary>Creates a new announcement as Draft. Sanitizes HTML content. Admin/SuperAdmin only.</summary>
    Task<ServiceResult<AnnouncementDto>> CreateAsync(User caller, CreateAnnouncementDto dto, CancellationToken cancellationToken = default);

    /// <summary>Updates title and content. Sanitizes HTML. Admin/SuperAdmin only.</summary>
    Task<ServiceResult<AnnouncementDto>> UpdateAsync(User caller, Guid id, UpdateAnnouncementDto dto, CancellationToken cancellationToken = default);

    /// <summary>Transitions to Published. Sets PublishedAt on first publish only. Admin/SuperAdmin only.</summary>
    Task<ServiceResult<AnnouncementDto>> PublishAsync(User caller, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Transitions Published → Draft (unpublish). Admin/SuperAdmin only.</summary>
    Task<ServiceResult<AnnouncementDto>> UnpublishAsync(User caller, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Transitions Published → Archived. Admin/SuperAdmin only.</summary>
    Task<ServiceResult<AnnouncementDto>> ArchiveAsync(User caller, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Hard deletes Draft or Archived announcements. Conflict if Published. Admin/SuperAdmin only.</summary>
    Task<ServiceResult<bool>> DeleteAsync(User caller, Guid id, CancellationToken cancellationToken = default);
}
