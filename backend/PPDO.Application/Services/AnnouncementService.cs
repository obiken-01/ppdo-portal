using Ganss.Xss;
using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Announcements;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Announcement lifecycle management (RAL-83).
/// Write operations are restricted to Admin/SuperAdmin.
/// HTML content is sanitized via Ganss.Xss on every create/update.
/// </summary>
public sealed class AnnouncementService : IAnnouncementService
{
    private static readonly HtmlSanitizer Sanitizer = new();

    private readonly IRepository<Announcement> _repo;
    private readonly ILogger<AnnouncementService> _logger;

    public AnnouncementService(IRepository<Announcement> repo, ILogger<AnnouncementService> logger)
    {
        _repo   = repo;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AnnouncementPublicDto>> GetPublishedAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Announcement> all = await _repo.GetAllAsync(cancellationToken);
        return all
            .Where(a => a.Status == AnnouncementStatus.Published && a.PublishedAt.HasValue)
            .OrderByDescending(a => a.PublishedAt)
            .Select(a => new AnnouncementPublicDto(a.Id, a.Title, a.Content, a.PublishedAt!.Value))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<AnnouncementDto>>> GetAllAsync(
        User caller,
        CancellationToken cancellationToken = default)
    {
        if (!IsAdmin(caller))
            return ServiceResult<IReadOnlyList<AnnouncementDto>>.Forbidden("Admin or SuperAdmin role required.");

        IReadOnlyList<Announcement> all = await _repo.GetAllAsync(cancellationToken);
        IReadOnlyList<AnnouncementDto> dtos = all
            .OrderByDescending(a => a.UpdatedAt)
            .Select(MapToDto)
            .ToList();

        return ServiceResult<IReadOnlyList<AnnouncementDto>>.Ok(dtos);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<AnnouncementDto>> CreateAsync(
        User caller,
        CreateAnnouncementDto dto,
        CancellationToken cancellationToken = default)
    {
        if (!IsAdmin(caller))
            return ServiceResult<AnnouncementDto>.Forbidden("Admin or SuperAdmin role required.");

        if (string.IsNullOrWhiteSpace(dto.Title))
            return ServiceResult<AnnouncementDto>.BadRequest("Title is required.");

        Announcement entity = new()
        {
            Id          = Guid.NewGuid(),
            Title       = dto.Title.Trim(),
            Content     = Sanitizer.Sanitize(dto.Content ?? string.Empty),
            Status      = AnnouncementStatus.Draft,
            CreatedById = caller.Id,
        };

        await _repo.AddAsync(entity, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Announcement created. Id: {Id}, Title: {Title}", entity.Id, entity.Title);

        entity.CreatedBy = caller;
        return ServiceResult<AnnouncementDto>.Ok(MapToDto(entity));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<AnnouncementDto>> UpdateAsync(
        User caller,
        Guid id,
        UpdateAnnouncementDto dto,
        CancellationToken cancellationToken = default)
    {
        if (!IsAdmin(caller))
            return ServiceResult<AnnouncementDto>.Forbidden("Admin or SuperAdmin role required.");

        Announcement? entity = await _repo.GetByIdAsync(id, cancellationToken);
        if (entity is null)
            return ServiceResult<AnnouncementDto>.NotFound($"Announcement {id} not found.");

        if (string.IsNullOrWhiteSpace(dto.Title))
            return ServiceResult<AnnouncementDto>.BadRequest("Title is required.");

        entity.Title   = dto.Title.Trim();
        entity.Content = Sanitizer.Sanitize(dto.Content ?? string.Empty);

        await _repo.UpdateAsync(entity, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);

        return ServiceResult<AnnouncementDto>.Ok(MapToDto(entity));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<AnnouncementDto>> PublishAsync(
        User caller,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!IsAdmin(caller))
            return ServiceResult<AnnouncementDto>.Forbidden("Admin or SuperAdmin role required.");

        Announcement? entity = await _repo.GetByIdAsync(id, cancellationToken);
        if (entity is null)
            return ServiceResult<AnnouncementDto>.NotFound($"Announcement {id} not found.");

        entity.Status = AnnouncementStatus.Published;
        // Set PublishedAt only on first publish — never overwrite
        entity.PublishedAt ??= DateTime.UtcNow;

        await _repo.UpdateAsync(entity, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);

        return ServiceResult<AnnouncementDto>.Ok(MapToDto(entity));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<AnnouncementDto>> UnpublishAsync(
        User caller,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!IsAdmin(caller))
            return ServiceResult<AnnouncementDto>.Forbidden("Admin or SuperAdmin role required.");

        Announcement? entity = await _repo.GetByIdAsync(id, cancellationToken);
        if (entity is null)
            return ServiceResult<AnnouncementDto>.NotFound($"Announcement {id} not found.");

        if (entity.Status != AnnouncementStatus.Published)
            return ServiceResult<AnnouncementDto>.BadRequest("Only Published announcements can be unpublished.");

        entity.Status = AnnouncementStatus.Draft;

        await _repo.UpdateAsync(entity, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);

        return ServiceResult<AnnouncementDto>.Ok(MapToDto(entity));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<AnnouncementDto>> ArchiveAsync(
        User caller,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!IsAdmin(caller))
            return ServiceResult<AnnouncementDto>.Forbidden("Admin or SuperAdmin role required.");

        Announcement? entity = await _repo.GetByIdAsync(id, cancellationToken);
        if (entity is null)
            return ServiceResult<AnnouncementDto>.NotFound($"Announcement {id} not found.");

        if (entity.Status != AnnouncementStatus.Published)
            return ServiceResult<AnnouncementDto>.BadRequest("Only Published announcements can be archived.");

        entity.Status = AnnouncementStatus.Archived;

        await _repo.UpdateAsync(entity, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);

        return ServiceResult<AnnouncementDto>.Ok(MapToDto(entity));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<bool>> DeleteAsync(
        User caller,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!IsAdmin(caller))
            return ServiceResult<bool>.Forbidden("Admin or SuperAdmin role required.");

        Announcement? entity = await _repo.GetByIdAsync(id, cancellationToken);
        if (entity is null)
            return ServiceResult<bool>.NotFound($"Announcement {id} not found.");

        if (entity.Status == AnnouncementStatus.Published)
            return ServiceResult<bool>.Conflict("Cannot delete a Published announcement. Archive it first.");

        await _repo.DeleteAsync(entity, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Announcement deleted. Id: {Id}", id);
        return ServiceResult<bool>.Ok(true);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static bool IsAdmin(User caller) =>
        caller.Role is UserRole.Admin or UserRole.SuperAdmin;

    private static AnnouncementDto MapToDto(Announcement a) => new(
        a.Id,
        a.Title,
        a.Content,
        a.Status,
        a.PublishedAt,
        a.CreatedById,
        a.CreatedBy?.FullName ?? string.Empty,
        a.CreatedAt,
        a.UpdatedAt);
}
