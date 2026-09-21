using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>Implementation of <see cref="IPartnerApiRequestLogger"/>.</summary>
public sealed class PartnerApiRequestLogger : IPartnerApiRequestLogger
{
    private readonly IPartnerApiRequestRepository _requests;
    private readonly IPartnerApiKeyRepository _keys;

    public PartnerApiRequestLogger(IPartnerApiRequestRepository requests, IPartnerApiKeyRepository keys)
    {
        _requests = requests;
        _keys = keys;
    }

    /// <inheritdoc />
    public async Task LogAsync(
        PartnerApiKey key,
        string route,
        string? officeCode,
        int? fiscalYear,
        int statusCode,
        CancellationToken cancellationToken = default)
    {
        DateTime now = DateTime.UtcNow;

        PartnerApiRequest request = new()
        {
            KeyId       = key.Id,
            RequestedAt = now,
            Route       = route,
            OfficeCode  = officeCode,
            FiscalYear  = fiscalYear,
            StatusCode  = statusCode,
        };

        await _requests.AddAsync(request, cancellationToken);

        key.LastUsedAt = now;
        await _keys.UpdateAsync(key, cancellationToken);

        // One SaveChanges flushes both writes — _requests and _keys share the same scoped
        // AppDbContext, so this commits the log row and the LastUsedAt stamp together.
        await _requests.SaveChangesAsync(cancellationToken);
    }
}
