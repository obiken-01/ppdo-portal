using PPDO.Application.Common;
using PPDO.Application.DTOs.ResourceLinks;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Resource Links service — CRUD with RBAC enforcement.
///
/// Key ownership rule (from CLAUDE.md):
///   Staff can only ADD. Edit and delete always require Admin/SuperAdmin,
///   regardless of the CanManageResourceLinks flag.
/// </summary>
public sealed class ResourceLinkService : IResourceLinkService
{
    private readonly IRepository<ResourceLink> _links;
    private readonly IPermissionService        _permissions;

    public ResourceLinkService(
        IRepository<ResourceLink> links,
        IPermissionService        permissions)
    {
        _links       = links;
        _permissions = permissions;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ResourceLinkCategoryDto>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ResourceLink> all = await _links.GetAllAsync(cancellationToken);

        return all
            .Where(l => l.IsActive)
            .GroupBy(l => l.Category)
            .OrderBy(g => g.Min(l => l.CategoryOrder))
            .Select(g => new ResourceLinkCategoryDto(
                g.Key,
                g.Min(l => l.CategoryOrder),
                g.OrderBy(l => l.LinkOrder)
                  .Select(MapToDto)
                  .ToList()))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<ServiceResult<ResourceLinkDto>> CreateAsync(
        User requester,
        CreateResourceLinkDto dto,
        CancellationToken cancellationToken = default)
    {
        if (!await _permissions.CanManageResourceLinksAsync(requester, cancellationToken))
            return ServiceResult<ResourceLinkDto>.Forbidden(
                "You do not have permission to add resource links.");

        if (string.IsNullOrWhiteSpace(dto.Title))
            return ServiceResult<ResourceLinkDto>.BadRequest("Title is required.");

        if (string.IsNullOrWhiteSpace(dto.Url))
            return ServiceResult<ResourceLinkDto>.BadRequest("Url is required.");

        bool isAdminCreated = requester.Role is UserRole.SuperAdmin or UserRole.Admin;

        ResourceLink link = new()
        {
            Id             = Guid.NewGuid(),
            Title          = dto.Title.Trim(),
            Url            = dto.Url.Trim(),
            Category       = dto.Category.Trim(),
            CategoryOrder  = dto.CategoryOrder,
            LinkOrder      = dto.LinkOrder,
            IsActive       = true,
            IsAdminCreated = isAdminCreated,
            SubmittedById  = requester.Id,
        };

        await _links.AddAsync(link, cancellationToken);
        await _links.SaveChangesAsync(cancellationToken);

        return ServiceResult<ResourceLinkDto>.Ok(MapToDto(link));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<ResourceLinkDto>> UpdateAsync(
        User requester,
        Guid id,
        UpdateResourceLinkDto dto,
        CancellationToken cancellationToken = default)
    {
        // Edit always requires Admin/SuperAdmin — Staff cannot edit regardless of flag.
        if (requester.Role is not (UserRole.SuperAdmin or UserRole.Admin))
            return ServiceResult<ResourceLinkDto>.Forbidden(
                "Only Admin and SuperAdmin can edit resource links.");

        ResourceLink? link = await _links.GetByIdAsync(id, cancellationToken);
        if (link is null)
            return ServiceResult<ResourceLinkDto>.NotFound($"Resource link {id} not found.");

        if (string.IsNullOrWhiteSpace(dto.Title))
            return ServiceResult<ResourceLinkDto>.BadRequest("Title is required.");

        if (string.IsNullOrWhiteSpace(dto.Url))
            return ServiceResult<ResourceLinkDto>.BadRequest("Url is required.");

        link.Title         = dto.Title.Trim();
        link.Url           = dto.Url.Trim();
        link.Category      = dto.Category.Trim();
        link.CategoryOrder = dto.CategoryOrder;
        link.LinkOrder     = dto.LinkOrder;

        await _links.UpdateAsync(link, cancellationToken);
        await _links.SaveChangesAsync(cancellationToken);

        return ServiceResult<ResourceLinkDto>.Ok(MapToDto(link));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<ResourceLinkDto>> DeleteAsync(
        User requester,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        // Delete always requires Admin/SuperAdmin — Staff cannot delete regardless of flag.
        if (requester.Role is not (UserRole.SuperAdmin or UserRole.Admin))
            return ServiceResult<ResourceLinkDto>.Forbidden(
                "Only Admin and SuperAdmin can delete resource links.");

        ResourceLink? link = await _links.GetByIdAsync(id, cancellationToken);
        if (link is null)
            return ServiceResult<ResourceLinkDto>.NotFound($"Resource link {id} not found.");

        link.IsActive = false;

        await _links.UpdateAsync(link, cancellationToken);
        await _links.SaveChangesAsync(cancellationToken);

        return ServiceResult<ResourceLinkDto>.Ok(MapToDto(link));
    }

    // ── Mapping ────────────────────────────────────────────────────────────────

    private static ResourceLinkDto MapToDto(ResourceLink l) => new(
        l.Id, l.Title, l.Url, l.Category, l.CategoryOrder,
        l.LinkOrder, l.IsActive, l.IsAdminCreated, l.SubmittedById);
}
