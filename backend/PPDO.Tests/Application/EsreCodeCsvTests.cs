using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// CSV export/import for the eSRE code config page (PPDO-19).
///
/// Kept apart from <see cref="EsreCodeServiceTests"/> because the thing under test here is not
/// CRUD: it is the upsert-and-audit contract. The audit half is the reason the ticket exists —
/// PPDO-8 found <c>DivisionService.ImportCsvAsync</c> upserting every division's permission flags
/// with no audit row at all, the widest "who can do what" write in the system and invisible in the
/// log. These tests pin the shape that must not be reintroduced: one row per record actually
/// changed, nothing for a skipped row, and emitted after <c>SaveChangesAsync</c> so a created row
/// has a real Id rather than 0.
/// </summary>
public sealed class EsreCodeCsvTests
{
    /// <summary>The line ending <c>Csv.Write</c> emits, and the one these fixtures feed back in.</summary>
    private const string Crlf = "\r\n";

    /// <summary>Builds CSV fixture text so no test carries escape sequences inline.</summary>
    private static string CsvText(params string[] lines) => string.Join(Crlf, lines) + Crlf;

    private static EsreCode Code(int id, string code, bool active = true) => new()
    {
        Id = id, Code = code, Name = code, IsActive = active,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static (EsreCodeService sut, Mock<IEsreCodeRepository> repo, Mock<IAuditService> audit)
        Build(List<EsreCode> seed)
    {
        Mock<IEsreCodeRepository> repo = new();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(seed);
        repo.Setup(r => r.AddAsync(It.IsAny<EsreCode>(), It.IsAny<CancellationToken>()))
            .Callback<EsreCode, CancellationToken>((e, _) => seed.Add(e))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateAsync(It.IsAny<EsreCode>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        Mock<IAuditService> audit = new();
        return (new EsreCodeService(
            repo.Object, NullLogger<EsreCodeService>.Instance, audit.Object), repo, audit);
    }

    private static void VerifyAudits(Mock<IAuditService> audit, string action, int times) =>
        audit.Verify(a => a.LogAsync("esre_codes", It.IsAny<int>(), action,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Exactly(times));

    /// <summary>Asserts the import wrote no audit row at all — the "changed nothing" case.</summary>
    private static void VerifyNoAudits(Mock<IAuditService> audit) =>
        audit.Verify(a => a.LogAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);

    // ── ExportCsvAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportCsvAsync_WritesHeaderAndRowsOrderedByCode()
    {
        (EsreCodeService sut, _, _) = Build([Code(2, "SS"), Code(1, "ES")]);

        string csv = await sut.ExportCsvAsync();

        string[] lines = csv.Split(Crlf, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("code,name,description,is_active", lines[0]);
        Assert.StartsWith("ES,", lines[1]);
        Assert.StartsWith("SS,", lines[2]);
    }

    [Fact]
    public async Task ExportCsvAsync_IncludesInactiveRows_SoTheFileIsAUsableBackup()
    {
        (EsreCodeService sut, _, _) = Build([Code(1, "SS", active: false)]);

        string csv = await sut.ExportCsvAsync();

        Assert.Contains("SS,SS,,false", csv);
    }

    // ── ImportCsvAsync ────────────────────────────────────────────────────────

    /// <summary>The ticket's headline acceptance case: export then re-import changes nothing.</summary>
    [Fact]
    public async Task ImportCsvAsync_WithItsOwnExport_IsANoOpAndWritesNoAudit()
    {
        List<EsreCode> seed = [Code(1, "SS"), Code(2, "ES", active: false)];
        (EsreCodeService sut, Mock<IEsreCodeRepository> repo, Mock<IAuditService> audit) = Build(seed);
        string csv = await sut.ExportCsvAsync();

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(csv);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.New);
        Assert.Equal(0, result.Value.Updated);
        Assert.Equal(2, result.Value.Skipped);
        repo.Verify(r => r.UpdateAsync(It.IsAny<EsreCode>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNoAudits(audit);
    }

    [Fact]
    public async Task ImportCsvAsync_WithAChangedRow_UpdatesItAndAuditsTheChange()
    {
        List<EsreCode> seed = [Code(1, "SS")];
        (EsreCodeService sut, _, Mock<IAuditService> audit) = Build(seed);

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(
            CsvText("code,name,description,is_active", "SS,Social Services,,true"));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.Updated);
        Assert.Equal("Social Services", seed[0].Name);
        VerifyAudits(audit, AuditAction.Update, 1);
    }

    [Fact]
    public async Task ImportCsvAsync_WithAMixOfChangedAndUnchangedRows_AuditsOnlyTheChangedOne()
    {
        List<EsreCode> seed = [Code(1, "SS"), Code(2, "ES")];
        (EsreCodeService sut, _, Mock<IAuditService> audit) = Build(seed);

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(CsvText(
            "code,name,description,is_active", "SS,SS,,true", "ES,Economic Services,,true"));

        Assert.Equal(1, result.Value!.Updated);
        Assert.Equal(1, result.Value.Skipped);
        VerifyAudits(audit, AuditAction.Update, 1);
    }

    [Fact]
    public async Task ImportCsvAsync_DeactivatingARow_IsAnUpdateAndIsAudited()
    {
        List<EsreCode> seed = [Code(1, "SS")];
        (EsreCodeService sut, _, Mock<IAuditService> audit) = Build(seed);

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(
            CsvText("code,name,description,is_active", "SS,SS,,false"));

        Assert.Equal(1, result.Value!.Updated);
        Assert.False(seed[0].IsActive);
        VerifyAudits(audit, AuditAction.Update, 1);
    }

    /// <summary>
    /// Ralph's call, 2026-09-02: eSRE is a closed vocabulary of four <i>today</i>, not forever, so
    /// an import may introduce a code the seed never had — that is how a newly issued code gets in
    /// without waiting for a release. If this starts failing because someone added a whitelist,
    /// read <c>IEsreCodeService.ImportCsvAsync</c> before "fixing" it.
    /// </summary>
    [Fact]
    public async Task ImportCsvAsync_WithACodeOutsideTheFour_CreatesItAndAuditsTheCreate()
    {
        List<EsreCode> seed = [Code(1, "SS")];
        (EsreCodeService sut, _, Mock<IAuditService> audit) = Build(seed);

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(
            CsvText("code,name,description,is_active", "GG,Governance,,true"));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.New);
        Assert.Contains(seed, c => c.Code == "GG");
        VerifyAudits(audit, AuditAction.Create, 1);
    }

    [Fact]
    public async Task ImportCsvAsync_NormalisesCodeCase_SoALowercaseFileUpdatesRatherThanDuplicates()
    {
        List<EsreCode> seed = [Code(1, "SS")];
        (EsreCodeService sut, _, _) = Build(seed);

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(
            CsvText("code,name,description,is_active", "ss,Social Services,,true"));

        Assert.Equal(1, result.Value!.Updated);
        Assert.Equal(0, result.Value.New);
        Assert.Single(seed);
    }

    [Fact]
    public async Task ImportCsvAsync_WithAMissingName_SkipsTheRowAndReportsIt()
    {
        List<EsreCode> seed = [];
        (EsreCodeService sut, Mock<IEsreCodeRepository> repo, Mock<IAuditService> audit) = Build(seed);

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(
            CsvText("code,name,description,is_active", "GG,,,true"));

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.New);
        Assert.Equal(1, result.Value.Skipped);
        Assert.Single(result.Value.Errors);
        Assert.Contains("Row 2", result.Value.Errors[0]);
        repo.Verify(r => r.AddAsync(It.IsAny<EsreCode>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNoAudits(audit);
    }

    [Fact]
    public async Task ImportCsvAsync_WithEmptyText_ReturnsBadRequest()
    {
        (EsreCodeService sut, _, _) = Build([]);

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync("   ");

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ImportCsvAsync_WithNoHeaderRow_TreatsTheFirstLineAsData()
    {
        List<EsreCode> seed = [];
        (EsreCodeService sut, _, _) = Build(seed);

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(CsvText("GG,Governance,,true"));

        Assert.Equal(1, result.Value!.New);
        Assert.Contains(seed, c => c.Code == "GG");
    }

    /// <summary>
    /// Audit rows are emitted after <c>SaveChangesAsync</c> — a created row has no Id before then,
    /// and an audit entry keyed on 0 is worse than none (PPDO-19).
    /// </summary>
    [Fact]
    public async Task ImportCsvAsync_EmitsAuditRowsAfterSaveChanges()
    {
        List<EsreCode> seed = [];
        (EsreCodeService sut, Mock<IEsreCodeRepository> repo, Mock<IAuditService> audit) = Build(seed);

        bool saved = false;
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => saved = true).ReturnsAsync(1);

        bool auditRanAfterSave = false;
        audit.Setup(a => a.LogAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Callback(() => auditRanAfterSave = saved).Returns(Task.CompletedTask);

        await sut.ImportCsvAsync(CsvText("code,name,description,is_active", "GG,Governance,,true"));

        Assert.True(auditRanAfterSave);
    }
}
