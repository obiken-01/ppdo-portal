using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// The bulk export's request rules (v1.9.0 — PPDO-228, <c>Bulk_Initial_WFP_Export_Spec.md</c> §3–§4):
/// the PPDO report-reader gate, the entered-year rule, one active job per year, nothing eligible, and the
/// download's 409 / 410 / 404.
/// </summary>
public sealed class InitialWfpExportServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 6, 30, 0, DateTimeKind.Utc);
    private const int Fy = AipFiscalYears.FirstEnteredFiscalYear;

    private readonly Mock<IInitialWfpExportJobRepository> _jobs = new();
    private readonly Mock<IWfpReportService> _report = new();
    private readonly Mock<IPermissionService> _permissions = new();
    private readonly Mock<IFileStore> _files = new();
    private readonly User _ppdo = new() { Id = Guid.NewGuid(), FullName = "Jose Santos" };

    public InitialWfpExportServiceTests()
    {
        _permissions.Setup(p => p.CanAccessWfpAsync(_ppdo, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _report.Setup(r => r.GetInitialEligibleOfficesAsync(Fy, _ppdo, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new WfpReportOfficeDto(15, "OPA", "Office of the Provincial Administrator", "Consolidated")]);
    }

    private InitialWfpExportService Service() => new(
        _jobs.Object, _report.Object, _permissions.Object, _files.Object,
        new FixedClock(Now), NullLogger<InitialWfpExportService>.Instance);

    // ── Who (decision 4, case 10) ───────────────────────────────────────────────

    [Fact]
    public async Task EveryMethod_CallerWithoutCanAccessWfp_IsForbidden_AndTouchesNoJob()
    {
        User office = new() { Id = Guid.NewGuid() };   // CanAccessWfp not set up: false

        InitialWfpExportService service = Service();
        Assert.Equal(ServiceErrorCode.Forbidden, (await service.RequestAsync(Fy, office)).Code);
        Assert.Equal(ServiceErrorCode.Forbidden, (await service.GetLatestAsync(Fy, office)).Code);
        Assert.Equal(ServiceErrorCode.Forbidden, (await service.GetAsync(1, office)).Code);
        Assert.Equal(ServiceErrorCode.Forbidden, (await service.OpenDownloadAsync(1, office)).Code);

        _jobs.VerifyNoOtherCalls();
        _files.VerifyNoOtherCalls();
    }

    // ── Request ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RequestAsync_NoActiveJob_QueuesOne_AsTheRequester()
    {
        InitialWfpExportJob? added = null;
        _jobs.Setup(j => j.AddAsync(It.IsAny<InitialWfpExportJob>(), It.IsAny<CancellationToken>()))
            .Callback<InitialWfpExportJob, CancellationToken>((job, _) => { job.Id = 42; added = job; });

        ServiceResult<InitialWfpExportRequestResult> result = await Service().RequestAsync(Fy, _ppdo);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Created);                         // the endpoint's 202
        Assert.Equal((42, Fy, "Queued", "Jose Santos"),
            (result.Value.Job.Id, result.Value.Job.FiscalYear, result.Value.Job.Status, result.Value.Job.RequestedByName));
        Assert.Equal((BackgroundJobStatus.Queued, _ppdo.Id), (added!.Status, added.RequestedById));
    }

    [Fact]
    public async Task RequestAsync_YearAlreadyHasAQueuedOrRunningJob_ReturnsIt_AndQueuesNothing()
    {
        InitialWfpExportJob running = Job(7, BackgroundJobStatus.Running, requester: "Maria Cruz");
        running.DoneCount = 3; running.TotalCount = 12;
        _jobs.Setup(j => j.GetActiveByFiscalYearAsync(Fy, It.IsAny<CancellationToken>())).ReturnsAsync(running);

        ServiceResult<InitialWfpExportRequestResult> result = await Service().RequestAsync(Fy, _ppdo);

        Assert.False(result.Value!.Created);                        // the endpoint's 200
        Assert.Equal((7, "Running", 3, 12, "Maria Cruz"),
            (result.Value.Job.Id, result.Value.Job.Status, result.Value.Job.Done, result.Value.Job.Total,
             result.Value.Job.RequestedByName));
        _jobs.Verify(j => j.AddAsync(It.IsAny<InitialWfpExportJob>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RequestAsync_LosesTheRaceToAnotherRequest_ReturnsTheWinnersJob()
    {
        // Nothing active when we look; by the time we insert, the other request's job exists.
        InitialWfpExportJob winner = Job(8, BackgroundJobStatus.Queued);
        _jobs.SetupSequence(j => j.GetActiveByFiscalYearAsync(Fy, It.IsAny<CancellationToken>()))
            .ReturnsAsync((InitialWfpExportJob?)null)
            .ReturnsAsync(winner);
        _jobs.Setup(j => j.AddAsync(It.IsAny<InitialWfpExportJob>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UniqueConstraintViolationException("dup", "IX_initial_wfp_export_jobs_fiscal_year_active", new Exception()));

        ServiceResult<InitialWfpExportRequestResult> result = await Service().RequestAsync(Fy, _ppdo);

        Assert.Equal((8, false), (result.Value!.Job.Id, result.Value.Created));
    }

    [Fact]
    public async Task RequestAsync_UploadedYear_Is400_FromTheBreakYear()
    {
        ServiceResult<InitialWfpExportRequestResult> result = await Service().RequestAsync(Fy - 1, _ppdo);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Equal($"Bulk export is for FY {Fy} and later.", result.Error);
        _jobs.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RequestAsync_NoOfficeConsolidated_Is409_AndQueuesNothing()
    {
        _report.Setup(r => r.GetInitialEligibleOfficesAsync(Fy, _ppdo, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        ServiceResult<InitialWfpExportRequestResult> result = await Service().RequestAsync(Fy, _ppdo);

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
        Assert.Equal($"No office's FY {Fy} AIP is marked as done yet.", result.Error);
        _jobs.Verify(j => j.AddAsync(It.IsAny<InitialWfpExportJob>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Read ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetLatestAsync_YearWithNoJob_IsANullValue()
    {
        ServiceResult<InitialWfpExportJobDto?> result = await Service().GetLatestAsync(Fy, _ppdo);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);   // the endpoint's 204
    }

    [Fact]
    public async Task GetAsync_AnotherPpdoUsersJob_IsReadable_WithItsProblems()
    {
        InitialWfpExportJob job = Job(9, BackgroundJobStatus.DoneWithProblems, requester: "Maria Cruz");
        job.ProblemsJson = """[{"officeCode":"PEO","reason":"No AIP hierarchy found."}]""";
        _jobs.Setup(j => j.GetByIdAsync(9, It.IsAny<CancellationToken>())).ReturnsAsync(job);

        ServiceResult<InitialWfpExportJobDto> result = await Service().GetAsync(9, _ppdo);

        Assert.Equal("Maria Cruz", result.Value!.RequestedByName);
        Assert.Equal([new InitialWfpExportProblemDto("PEO", "No AIP hierarchy found.")], result.Value.Problems);
    }

    [Fact]
    public async Task GetAsync_NoSuchJob_Is404()
        => Assert.Equal(ServiceErrorCode.NotFound, (await Service().GetAsync(404, _ppdo)).Code);

    // ── Download ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(BackgroundJobStatus.Queued)]
    [InlineData(BackgroundJobStatus.Running)]
    [InlineData(BackgroundJobStatus.Failed)]
    public async Task OpenDownloadAsync_NotFinished_Is409(BackgroundJobStatus status)
    {
        _jobs.Setup(j => j.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Job(1, status));

        ServiceResult<InitialWfpExportDownload> result = await Service().OpenDownloadAsync(1, _ppdo);

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
        _files.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OpenDownloadAsync_MarkedExpired_Is410()
    {
        _jobs.Setup(j => j.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Job(1, BackgroundJobStatus.Expired, fileKey: "k.zip", expiresAt: Now.AddDays(-1)));

        ServiceResult<InitialWfpExportDownload> result = await Service().OpenDownloadAsync(1, _ppdo);

        Assert.Equal((ServiceErrorCode.Gone, "This export has expired. Run it again."), (result.Code, result.Error));
        _files.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OpenDownloadAsync_PastExpiryButNotYetCleanedUp_Is410()
    {
        _jobs.Setup(j => j.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Job(1, BackgroundJobStatus.Done, fileKey: "k.zip", expiresAt: Now));

        Assert.Equal(ServiceErrorCode.Gone, (await Service().OpenDownloadAsync(1, _ppdo)).Code);
        _files.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OpenDownloadAsync_FinishedButTheFileIsMissing_Is410()
    {
        _jobs.Setup(j => j.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Job(1, BackgroundJobStatus.Done, fileKey: "k.zip", expiresAt: Now.AddDays(6)));

        Assert.Equal(ServiceErrorCode.Gone, (await Service().OpenDownloadAsync(1, _ppdo)).Code);
    }

    [Fact]
    public async Task OpenDownloadAsync_Finished_OpensTheFile_NamedInManilaTime()
    {
        InitialWfpExportJob job = Job(1, BackgroundJobStatus.DoneWithProblems, fileKey: "k.zip", expiresAt: Now.AddDays(6));
        job.FinishedAt = new DateTime(2026, 10, 15, 6, 30, 0, DateTimeKind.Utc);   // 14:30 in Manila
        _jobs.Setup(j => j.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        _files.Setup(f => f.OpenReadAsync("k.zip", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(Encoding.UTF8.GetBytes("PK")));

        ServiceResult<InitialWfpExportDownload> result = await Service().OpenDownloadAsync(1, _ppdo);

        Assert.Equal($"Initial WFP FY{Fy} - 2026-10-15 1430.zip", result.Value!.FileName);
        await using Stream content = result.Value.Content;
        Assert.Equal("PK", await new StreamReader(content).ReadToEndAsync());
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static InitialWfpExportJob Job(
        int id, BackgroundJobStatus status, string requester = "Jose Santos",
        string? fileKey = null, DateTime? expiresAt = null) => new()
    {
        Id = id, FiscalYear = Fy, Status = status, CreatedAt = Now.AddMinutes(-10),
        FinishedAt = status is BackgroundJobStatus.Done or BackgroundJobStatus.DoneWithProblems or BackgroundJobStatus.Expired
            ? Now.AddMinutes(-5) : null,
        FileKey = fileKey, ExpiresAt = expiresAt,
        RequestedBy = new User { FullName = requester },
    };

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
