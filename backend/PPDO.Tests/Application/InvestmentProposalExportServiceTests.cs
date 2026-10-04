using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.InvestmentProposal;
using PPDO.Application.DTOs.InvestmentProposal.Document;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Functions.Functions;

namespace PPDO.Tests.Application;

/// <summary>
/// <see cref="InvestmentProposalExportService"/> (PPDO-158): it reads through
/// <see cref="IInvestmentProposalService.GetAsync"/>, so scope and the 404 are the read's, and it
/// never writes a file for a refused read.
/// </summary>
public sealed class InvestmentProposalExportServiceTests
{
    private readonly Mock<IInvestmentProposalService>     _proposals = new();
    private readonly Mock<IInvestmentProposalWordService> _word      = new();
    private readonly User _caller = new() { Id = Guid.NewGuid(), FullName = "Ana" };

    private InvestmentProposalExportService Sut() => new(_proposals.Object, _word.Object, NullLogger<InvestmentProposalExportService>.Instance);

    private static ProposalDto Dto() => new(
        7, InvestmentProposalStatus.Final, "AAAAAAAAB9E=", null, null, new DateTime(2026, 10, 1), null, false, false, false,
        new ProposalHeaderDto(10, 20, 30, 15, 2028, "Program", "1000-000-1", "Roads", "PPDO", null, null, 0m, [], "N/A", null),
        new ProposalWarningsDto([]),
        new ProposalContentDto(null, null, null, [], null, null,
            InvestmentProposalSector.All.Select(s => new ProposalBenefitDto(s, null, null)).ToList(), null,
            InvestmentProposalLogframeLevel.All.Select(l => new ProposalLogframeDto(l, null, null)).ToList(),
            true, [], [], [], null, null, [], null, [], [], null,
            [new(1, null, null, null), new(2, null, null, null), new(3, null, null, null), new(4, null, null, null)]),
        []);

    [Fact]
    public async Task ExportAsync_ReadableProposal_ReturnsTheDocumentAndItsFileName()
    {
        _proposals.Setup(p => p.GetAsync(7, _caller, It.IsAny<CancellationToken>())).ReturnsAsync(ServiceResult<ProposalDto>.Ok(Dto()));
        _word.Setup(w => w.Export(It.Is<ProposalDocument>(d => d.SectionA.ProjectTitle == "Roads"))).Returns([1, 2, 3]);

        ServiceResult<ProposalExportFileDto> result = await Sut().ExportAsync(7, _caller);

        Assert.True(result.IsSuccess);
        Assert.Equal("Investment Proposal - 1000-000-1 - Roads.docx", result.Value!.FileName);
        Assert.Equal([1, 2, 3], result.Value.Content);
    }

    [Fact]
    public async Task ExportAsync_OutOfScopeOrMissing_Is404_AndNothingIsWritten()
    {
        _proposals.Setup(p => p.GetAsync(7, _caller, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<ProposalDto>.NotFound("Proposal not found."));

        ServiceResult<ProposalExportFileDto> result = await Sut().ExportAsync(7, _caller);

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
        Assert.Equal("Proposal not found.", result.Error);
        _word.Verify(w => w.Export(It.IsAny<ProposalDocument>()), Times.Never);
    }

    [Theory]
    [InlineData("Investment Proposal - 1000 - Roads.docx",
                "attachment; filename=\"Investment Proposal - 1000 - Roads.docx\"; filename*=UTF-8''Investment%20Proposal%20-%201000%20-%20Roads.docx")]
    [InlineData("Investment Proposal - 1 - Pañg–Roads.docx",
                "attachment; filename=\"Investment Proposal - 1 - Pa_g_Roads.docx\"; filename*=UTF-8''Investment%20Proposal%20-%201%20-%20Pa%C3%B1g%E2%80%93Roads.docx")]
    public void ContentDisposition_AsciiFallbackAndRfc5987Name(string fileName, string expected)
        => Assert.Equal(expected, InvestmentProposalFunctions.ContentDisposition(fileName));
}
