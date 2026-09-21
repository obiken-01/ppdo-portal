using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// CSV export/import for the climate-change typology config page (PPDO-19).
///
/// Unlike eSRE, this vocabulary is open-ended — its ~60 codes were derived from the province's
/// FY2027 data rather than seeded from a fixed list — so import creating new codes is the normal
/// case here, not the exception. What the import still must not do is accept a row the create
/// form would have refused: a category outside the three, or a pasted multi-code value like
/// "A222-03, A224-05" (18 such values exist in the FY2027 file), which would recreate the free
/// text field this table replaced.
/// </summary>
public sealed class ClimateChangeTypologyCsvTests
{
    private const string Crlf = "\r\n";

    private static string CsvText(params string[] lines) => string.Join(Crlf, lines) + Crlf;

    private static ClimateChangeTypology Typology(
        int id, string code, string category = "Adaptation", bool active = true) => new()
    {
        Id = id, Code = code, Name = code, Category = category, IsActive = active,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static (ClimateChangeTypologyService sut,
                    Mock<IClimateChangeTypologyRepository> repo,
                    Mock<IAuditService> audit)
        Build(List<ClimateChangeTypology> seed)
    {
        Mock<IClimateChangeTypologyRepository> repo = new();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(seed);
        repo.Setup(r => r.AddAsync(It.IsAny<ClimateChangeTypology>(), It.IsAny<CancellationToken>()))
            .Callback<ClimateChangeTypology, CancellationToken>((e, _) => seed.Add(e))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateAsync(It.IsAny<ClimateChangeTypology>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        Mock<IAuditService> audit = new();
        return (new ClimateChangeTypologyService(
            repo.Object, NullLogger<ClimateChangeTypologyService>.Instance, audit.Object), repo, audit);
    }

    private static void VerifyAudits(Mock<IAuditService> audit, string action, int times) =>
        audit.Verify(a => a.LogAsync("climate_change_typologies", It.IsAny<int>(), action,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Exactly(times));

    private static void VerifyNoAudits(Mock<IAuditService> audit) =>
        audit.Verify(a => a.LogAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);

    // ── ExportCsvAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportCsvAsync_WritesTheFiveColumnHeaderOrderedByCode()
    {
        (ClimateChangeTypologyService sut, _, _) =
            Build([Typology(2, "M314-03", "Mitigation"), Typology(1, "A113-08")]);

        string csv = await sut.ExportCsvAsync();

        string[] lines = csv.Split(Crlf, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("code,name,category,description,is_active", lines[0]);
        Assert.StartsWith("A113-08,", lines[1]);
        Assert.StartsWith("M314-03,", lines[2]);
    }

    [Fact]
    public async Task ExportCsvAsync_IncludesInactiveRows_SoTheFileIsAUsableBackup()
    {
        (ClimateChangeTypologyService sut, _, _) = Build([Typology(1, "A113-08", active: false)]);

        string csv = await sut.ExportCsvAsync();

        Assert.Contains("A113-08,A113-08,Adaptation,,false", csv);
    }

    // ── ImportCsvAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task ImportCsvAsync_WithItsOwnExport_IsANoOpAndWritesNoAudit()
    {
        List<ClimateChangeTypology> seed =
            [Typology(1, "A113-08"), Typology(2, "M314-03", "Mitigation", active: false)];
        (ClimateChangeTypologyService sut, Mock<IClimateChangeTypologyRepository> repo,
         Mock<IAuditService> audit) = Build(seed);
        string csv = await sut.ExportCsvAsync();

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(csv);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.New);
        Assert.Equal(0, result.Value.Updated);
        Assert.Equal(2, result.Value.Skipped);
        repo.Verify(r => r.UpdateAsync(It.IsAny<ClimateChangeTypology>(), It.IsAny<CancellationToken>()),
            Times.Never);
        VerifyNoAudits(audit);
    }

    [Fact]
    public async Task ImportCsvAsync_WithANewCode_CreatesItAndAuditsTheCreate()
    {
        List<ClimateChangeTypology> seed = [];
        (ClimateChangeTypologyService sut, _, Mock<IAuditService> audit) = Build(seed);

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(CsvText(
            "code,name,category,description,is_active",
            "A113-08,Flood control,Adaptation,,true"));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.New);
        Assert.Equal("Flood control", seed[0].Name);
        VerifyAudits(audit, AuditAction.Create, 1);
    }

    [Fact]
    public async Task ImportCsvAsync_WithAChangedCategory_UpdatesItAndAuditsTheChange()
    {
        List<ClimateChangeTypology> seed = [Typology(1, "M314-03")];
        (ClimateChangeTypologyService sut, _, Mock<IAuditService> audit) = Build(seed);

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(CsvText(
            "code,name,category,description,is_active", "M314-03,M314-03,Mitigation,,true"));

        Assert.Equal(1, result.Value!.Updated);
        Assert.Equal("Mitigation", seed[0].Category);
        VerifyAudits(audit, AuditAction.Update, 1);
    }

    /// <summary>
    /// The import reuses the create/update validator rather than carrying a looser rule set of
    /// its own — a second set of rules here is how a CSV ends up able to write a row the form
    /// would have refused.
    /// </summary>
    [Fact]
    public async Task ImportCsvAsync_WithAnUnknownCategory_SkipsTheRowAndReportsIt()
    {
        List<ClimateChangeTypology> seed = [];
        (ClimateChangeTypologyService sut, Mock<IClimateChangeTypologyRepository> repo,
         Mock<IAuditService> audit) = Build(seed);

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(CsvText(
            "code,name,category,description,is_active", "A113-08,Flood control,Resilience,,true"));

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.New);
        Assert.Equal(1, result.Value.Skipped);
        Assert.Contains("Row 2", result.Value.Errors[0]);
        Assert.Contains("Category", result.Value.Errors[0]);
        repo.Verify(r => r.AddAsync(It.IsAny<ClimateChangeTypology>(), It.IsAny<CancellationToken>()),
            Times.Never);
        VerifyNoAudits(audit);
    }

    /// <summary>
    /// 18 of the 167 tagged FY2027 activities hold two comma-separated codes in one field. A CSV
    /// is exactly where such a value would be pasted back in, so the guard has to hold here too.
    /// </summary>
    [Fact]
    public async Task ImportCsvAsync_WithAMultiCodeValue_SkipsTheRow()
    {
        List<ClimateChangeTypology> seed = [];
        (ClimateChangeTypologyService sut, _, _) = Build(seed);

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(CsvText(
            "code,name,category,description,is_active",
            "\"A222-03, A224-05\",Two codes,Adaptation,,true"));

        Assert.Equal(0, result.Value!.New);
        Assert.Equal(1, result.Value.Skipped);
        Assert.Empty(seed);
    }

    [Fact]
    public async Task ImportCsvAsync_WithLowercaseCategory_StoresTheCanonicalCasing()
    {
        List<ClimateChangeTypology> seed = [];
        (ClimateChangeTypologyService sut, _, _) = Build(seed);

        await sut.ImportCsvAsync(CsvText(
            "code,name,category,description,is_active", "A113-08,Flood control,adaptation,,true"));

        Assert.Equal("Adaptation", seed[0].Category);
    }

    [Fact]
    public async Task ImportCsvAsync_NormalisesCodeCase_SoALowercaseFileUpdatesRatherThanDuplicates()
    {
        List<ClimateChangeTypology> seed = [Typology(1, "A113-08")];
        (ClimateChangeTypologyService sut, _, _) = Build(seed);

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(CsvText(
            "code,name,category,description,is_active", "a113-08,Flood control,Adaptation,,true"));

        Assert.Equal(1, result.Value!.Updated);
        Assert.Equal(0, result.Value.New);
        Assert.Single(seed);
    }

    [Fact]
    public async Task ImportCsvAsync_WithEmptyText_ReturnsBadRequest()
    {
        (ClimateChangeTypologyService sut, _, _) = Build([]);

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync("   ");

        Assert.False(result.IsSuccess);
    }

    /// <summary>Audit rows are emitted after SaveChangesAsync so a created row has a real Id.</summary>
    [Fact]
    public async Task ImportCsvAsync_EmitsAuditRowsAfterSaveChanges()
    {
        List<ClimateChangeTypology> seed = [];
        (ClimateChangeTypologyService sut, Mock<IClimateChangeTypologyRepository> repo,
         Mock<IAuditService> audit) = Build(seed);

        bool saved = false;
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => saved = true).ReturnsAsync(1);

        bool auditRanAfterSave = false;
        audit.Setup(a => a.LogAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Callback(() => auditRanAfterSave = saved).Returns(Task.CompletedTask);

        await sut.ImportCsvAsync(CsvText(
            "code,name,category,description,is_active", "A113-08,Flood control,Adaptation,,true"));

        Assert.True(auditRanAfterSave);
    }
}
