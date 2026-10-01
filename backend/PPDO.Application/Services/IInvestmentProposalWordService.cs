using PPDO.Application.DTOs.InvestmentProposal.Document;

namespace PPDO.Application.Services;

/// <summary>
/// Writes an investment proposal as a Word document (v1.8.0 Demo 2.15 — PPDO-158,
/// Investment_Proposal_Spec.md decision 27). Implemented in
/// <c>PPDO.Infrastructure/Services/InvestmentProposalWordService.cs</c> with the Open XML SDK, on
/// the checked-in template that carries the letterhead, the "Page X of Y" footer and the page setup.
///
/// <para>
/// ⚠️ <b>A writer, not a calculator.</b> Order, numbering, totals, merged cells and wording all
/// arrive decided in the <see cref="ProposalDocument"/> from
/// <c>InvestmentProposalDocumentBuilder</c>. The writer chooses fonts, borders and widths only.
/// </para>
/// </summary>
public interface IInvestmentProposalWordService
{
    byte[] Export(ProposalDocument document);
}
