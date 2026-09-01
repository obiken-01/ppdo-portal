using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;

namespace PPDO.Application.Services
{
    /// <summary>
    /// CRUD for the eSRE classification vocabulary (RAL-248).
    /// Soft delete only — AIP activities reference these codes on an audited document.
    /// </summary>
    public interface IEsreCodeService
    {
        Task<IReadOnlyList<EsreCodeDto>> GetAllAsync(
            string? search, ActiveFilter active, CancellationToken cancellationToken = default);

        Task<ServiceResult<EsreCodeDto>> GetByIdAsync(
            int id, CancellationToken cancellationToken = default);

        Task<ServiceResult<EsreCodeDto>> CreateAsync(
            UpsertEsreCodeDto dto, CancellationToken cancellationToken = default);

        Task<ServiceResult<EsreCodeDto>> UpdateAsync(
            int id, UpsertEsreCodeDto dto, CancellationToken cancellationToken = default);

        /// <summary>Soft delete — sets IsActive = false. Never removes the row.</summary>
        Task<ServiceResult<EsreCodeDto>> DeleteAsync(
            int id, CancellationToken cancellationToken = default);
    }
}