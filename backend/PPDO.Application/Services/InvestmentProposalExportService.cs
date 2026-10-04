using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
using PPDO.Application.DTOs.InvestmentProposal;
using PPDO.Application.DTOs.InvestmentProposal.Document;
using PPDO.Domain.Entities;

namespace PPDO.Application.Services;

/// <summary>
/// The proposal's Word export (PPDO-158): read it exactly as <c>GET /proposals/{id}</c> does, build
/// the printed model, write the document.
///
/// <para>
/// Reading through <see cref="IInvestmentProposalService.GetAsync"/> is deliberate: the export can
/// never show more than the screen does. Scope, the 404 for out-of-scope, Draft-live versus
/// Final-snapshot and the frozen Section G rows are all that one method's, and stay in one place.
/// The export always prints <b>saved</b> data (decision 28).
/// </para>
/// </summary>
public sealed class InvestmentProposalExportService : IInvestmentProposalExportService
{
    private readonly IInvestmentProposalService              _proposals;
    private readonly IInvestmentProposalWordService          _word;
    private readonly ILogger<InvestmentProposalExportService> _logger;

    public InvestmentProposalExportService(
        IInvestmentProposalService proposals,
        IInvestmentProposalWordService word,
        ILogger<InvestmentProposalExportService> logger)
    {
        _proposals = proposals;
        _word      = word;
        _logger    = logger;
    }

    public async Task<ServiceResult<ProposalExportFileDto>> ExportAsync(int id, User caller, CancellationToken ct = default)
    {
        ServiceResult<ProposalDto> read = await _proposals.GetAsync(id, caller, ct);
        if (!read.IsSuccess)
            return ServiceResult<ProposalExportFileDto>.FromError(read);

        ProposalDocument document = InvestmentProposalDocumentBuilder.Build(read.Value!);
        byte[] content;
        try
        {
            content = _word.Export(document);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Investment proposal export failed. ProposalId: {ProposalId}, UserId: {UserId}", id, caller.Id);
            throw;
        }

        _logger.LogInformation(
            "Investment proposal exported. ProposalId: {ProposalId}, Status: {Status}, UserId: {UserId}",
            id, read.Value!.Status, caller.Id);
        return ServiceResult<ProposalExportFileDto>.Ok(new ProposalExportFileDto(document.FileName, content));
    }
}
