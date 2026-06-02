using PPDO.Application.Common;
using PPDO.Application.DTOs.ResourceLinks;
using PPDO.Domain.Entities;

namespace PPDO.Application.Services;

/// <summary>
/// Business logic for Resource Links — the office hub of Google Drive / Sheet URLs.
/// Implemented in <c>ResourceLinkService.cs</c>.
///
/// Permission summary:
///   GET    — all authenticated roles
///   POST   — Admin/SuperAdmin (IsAdminCreated=true) OR Staff with CanManageResourceLinks (IsAdminCreated=false)
///   PUT    — Admin/SuperAdmin only (Staff cannot edit)
///   DELETE — Admin/SuperAdmin only (Staff cannot delete)
/// </summary>
public interface IResourceLinkService
{
    /// <summary>
    /// Returns all active resource links grouped by category (sorted by CategoryOrder,
    /// then LinkOrder within each category). Available to all authenticated users.
    /// </summary>
    Task<IReadOnlyList<ResourceLinkCategoryDto>> GetAllAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new resource link.
    ///
    /// Returns:
    ///   <see cref="ServiceErrorCode.Forbidden"/>  — caller lacks CanManageResourceLinks.
    ///   <see cref="ServiceErrorCode.BadRequest"/>  — Title or Url is empty.
    /// </summary>
    Task<ServiceResult<ResourceLinkDto>> CreateAsync(
        User requester,
        CreateResourceLinkDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing link. Admin / SuperAdmin only.
    ///
    /// Returns:
    ///   <see cref="ServiceErrorCode.Forbidden"/>  — caller is not Admin/SuperAdmin.
    ///   <see cref="ServiceErrorCode.NotFound"/>   — link not found.
    ///   <see cref="ServiceErrorCode.BadRequest"/>  — Title or Url is empty.
    /// </summary>
    Task<ServiceResult<ResourceLinkDto>> UpdateAsync(
        User requester,
        Guid id,
        UpdateResourceLinkDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deletes a link (sets IsActive = false). Admin / SuperAdmin only.
    ///
    /// Returns:
    ///   <see cref="ServiceErrorCode.Forbidden"/>  — caller is not Admin/SuperAdmin.
    ///   <see cref="ServiceErrorCode.NotFound"/>   — link not found.
    /// </summary>
    Task<ServiceResult<ResourceLinkDto>> DeleteAsync(
        User requester,
        Guid id,
        CancellationToken cancellationToken = default);
}
