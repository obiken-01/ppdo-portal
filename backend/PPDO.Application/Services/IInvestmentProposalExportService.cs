using PPDO.Application.Common;
using PPDO.Application.DTOs.InvestmentProposal;
using PPDO.Domain.Entities;

namespace PPDO.Application.Services;

/// <summary>
/// <c>GET /api/budget-planning/proposals/{id}/export</c> (PPDO-158). Read-only: the same scope and
/// 404 rules as reading the proposal, so a cross-office reviewer may export.
/// </summary>
public interface IInvestmentProposalExportService
{
    Task<ServiceResult<ProposalExportFileDto>> ExportAsync(int id, User caller, CancellationToken ct = default);
}
