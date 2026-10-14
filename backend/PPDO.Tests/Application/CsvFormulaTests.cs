using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Spreadsheet formulas in the config CSV exports (PPDO-199, item 3).
///
/// A cell starting with <c>= + - @</c> (or a tab / CR) is run as a formula when the downloaded CSV
/// is opened in Excel, so <c>Csv.Write</c> prefixes it with <c>'</c>. The same files are imported
/// back, so <c>Csv.Parse</c> must remove exactly that prefix again — a round trip may not change
/// the data. Plain numbers (<c>-500</c>, <c>+3.5</c>) are not formulas and stay numbers.
/// </summary>
public sealed class CsvFormulaTests
{
    private static string Cell(string value) =>
        Csv.Write(["h"], [[value]]).Split("\r\n")[1];

    private static string RoundTrip(string value) =>
        Csv.Parse(Csv.Write(["h"], [[value]]))[1][0];

    // ── Write ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+A1", "'+A1")]
    [InlineData("-2+3", "'-2+3")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\t=1", "'\t=1")]
    [InlineData("-", "'-")]
    public void Write_FormulaLikeCell_IsPrefixedWithAQuote(string value, string expected)
        => Assert.Equal(expected, Cell(value));

    [Fact]
    public void Write_FormulaWithAComma_IsPrefixedInsideTheCsvQuotes()
        => Assert.Equal("\"'=HYPERLINK(\"\"https://x\"\",\"\"Click\"\")\"",
                        Cell("=HYPERLINK(\"https://x\",\"Click\")"));

    [Theory]
    [InlineData("-500")]
    [InlineData("+3.5")]
    [InlineData("-0.25")]
    [InlineData("1250.00")]
    public void Write_PlainNumber_IsLeftAsANumber(string value)
        => Assert.Equal(value, Cell(value));

    [Theory]
    [InlineData("Office supplies")]
    [InlineData("a=b")]
    [InlineData("'quoted text")]
    [InlineData("")]
    public void Write_OrdinaryText_IsUnchanged(string value)
        => Assert.Equal(value, Cell(value));

    [Fact]
    public void Write_TextAlreadyStartingWithQuoteThenFormula_GetsOneMoreQuote()
        => Assert.Equal("''=1", Cell("'=1"));

    // ── Round trip ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("=1+1")]
    [InlineData("=HYPERLINK(\"https://x\",\"Click\")")]
    [InlineData("+A1")]
    [InlineData("-")]
    [InlineData("@x")]
    [InlineData("\t=1")]
    [InlineData("'=1")]
    [InlineData("''=1")]
    [InlineData("'quoted text")]
    [InlineData("-500")]
    [InlineData("Office supplies")]
    public void WriteThenParse_GivesBackExactlyTheOriginal(string value)
        => Assert.Equal(value, RoundTrip(value));

    // ── Parse ────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_QuoteNotFollowedByAFormulaCharacter_IsKept()
        => Assert.Equal("'quoted text", Csv.Parse("h\r\n'quoted text\r\n")[1][0]);

    [Fact]
    public void Parse_HandWrittenQuotedFormula_DropsTheQuote()
        => Assert.Equal("=1+1", Csv.Parse("h\r\n'=1+1\r\n")[1][0]);

    // ── Through a real service ───────────────────────────────────────────────

    [Fact]
    public async Task ExportThenImport_FormulaLikeName_IsNeutralisedAndComesBackUnchanged()
    {
        List<EsreCode> seed =
        [
            new() { Id = 1, Code = "ES", Name = "=HYPERLINK(\"https://x\",\"Click\")", IsActive = true,
                    CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
        ];
        Mock<IEsreCodeRepository> repo = new();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(seed);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
        EsreCodeService sut = new(repo.Object, NullLogger<EsreCodeService>.Instance, new Mock<IAuditService>().Object);

        string csv = await sut.ExportCsvAsync();
        Assert.Contains("\"'=HYPERLINK(", csv);

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(csv);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.New);
        Assert.Equal(0, result.Value.Updated);
        Assert.Equal("=HYPERLINK(\"https://x\",\"Click\")", seed[0].Name);
        repo.Verify(r => r.UpdateAsync(It.IsAny<EsreCode>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
