using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;
using PPDO.Tests.TestHelpers;

namespace PPDO.Tests.Application;

/// <summary>
/// One pass of the bulk export worker (v1.9.0 — PPDO-229, <c>Bulk_Initial_WFP_Export_Spec.md</c>
/// decisions 7–17, §11): an office that fails is skipped and noted, the README says what is in the ZIP,
/// progress moves after each office, stuck jobs are recovered or failed, expired ZIPs are cleaned up,
/// and every line logged for a job carries its id.
///
/// <para>
/// The job tables and the file store are in-memory fakes (<see cref="FakeJobTables"/>,
/// <see cref="FakeFileStore"/>) that keep state across the pass, so a test reads the job and the ZIP
/// as they were left. The SQL behind the real ones is pinned in <c>BackgroundJobRepositoryTests</c>
/// (Sqlite) and <c>BackgroundJobClaimSqlServerTests</c> (the claim).
/// </para>
/// </summary>
public sealed class ExportJobWorkerTests
{
    /// <summary>14:30 Manila.</summary>
    private static readonly DateTime Now = new(2026, 10, 15, 6, 30, 0, DateTimeKind.Utc);
    private const int Fy = AipFiscalYears.FirstEnteredFiscalYear;

    private static readonly WfpReportOfficeDto Opa = new(15, "OPA", "Office of the Provincial Administrator", "Consolidated");
    private static readonly WfpReportOfficeDto Pho = new(21, "PHO", "Provincial Health Office", "Consolidated");
    private static readonly WfpReportOfficeDto Peo = new(30, "PEO", "Provincial Engineering Office", "Consolidated");

    private readonly FakeJobTables _jobs = new();
    private readonly FakeFileStore _files = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IPermissionService> _permissions = new();
    private readonly Mock<IWfpReportService> _report = new();
    private readonly Mock<IWfpReportExcelService> _excel = new();
    private readonly RecordingNotificationService _notifications = new();
    private readonly MovingClock _clock = new(Now);
    private readonly RecordingLogger<ExportJobWorker> _logger = new();

    private readonly User _requester = new() { Id = Guid.NewGuid(), FullName = "Jose Santos", IsActive = true };

    public ExportJobWorkerTests()
    {
        _users.Setup(u => u.GetByIdWithDivisionAsync(_requester.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_requester);
        _permissions.Setup(p => p.CanAccessWfpAsync(_requester, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        OfficesEligible(Opa, Pho, Peo);
        foreach (WfpReportOfficeDto office in new[] { Opa, Pho, Peo }) OfficeBuilds(office);

        // The workbook's bytes name the office, so a test can tell which file is which.
        _excel.Setup(e => e.Export(It.IsAny<WfpReportDto>()))
            .Returns((WfpReportDto r) => Encoding.UTF8.GetBytes($"xlsx for {r.OfficeCode}"));
    }

    private ExportJobWorker Worker() => new(
        _jobs, _jobs, _users.Object, _permissions.Object, _report.Object, _excel.Object,
        _files, _notifications, _clock, _logger);

    // ── A job that runs to the end ──────────────────────────────────────────────

    [Fact]
    public async Task RunOnce_EveryOfficeBuilds_ZipHasOneFilePerOfficeAndTheReadme_AndTheJobIsDone()
    {
        InitialWfpExportJob job = _jobs.Queue(Fy, _requester.Id);

        await Worker().RunOnceAsync();

        InitialWfpExportJob done = _jobs[job.Id];
        Assert.Equal(BackgroundJobStatus.Done, done.Status);
        Assert.Equal((3, 3, null), (done.DoneCount, done.TotalCount, done.ProblemsJson));
        Assert.Equal(_clock.Now, done.FinishedAt);
        Assert.Equal(_clock.Now + InitialWfpExportJob.KeepFor, done.ExpiresAt);   // decision 6: 7 days

        // Saved once, under the key the download reads, named in Manila time (decision 13).
        Assert.Equal($"initial-wfp-exports/{job.Id}/Initial WFP FY{Fy} - 2026-10-15 1430.zip", done.FileKey);
        Assert.Equal([done.FileKey], _files.Saved.Keys);

        Dictionary<string, string> zip = OpenZip(done.FileKey!);
        Assert.Equal(
            ["README.txt", $"WFP_OPA_{Fy}.xlsx", $"WFP_PEO_{Fy}.xlsx", $"WFP_PHO_{Fy}.xlsx"],
            zip.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("xlsx for PHO", zip[$"WFP_PHO_{Fy}.xlsx"]);
    }

    [Fact]
    public async Task RunOnce_BuildsEachOfficeAsTheRequester_WithNoDivisionChosen()
    {
        _jobs.Queue(Fy, _requester.Id);

        await Worker().RunOnceAsync();

        // Decision 8: the same scope as if the requester had clicked each office's export.
        foreach (WfpReportOfficeDto office in new[] { Opa, Pho, Peo })
            _report.Verify(r => r.GetInitialReportAsync(office.OfficeId, Fy, null, _requester, It.IsAny<CancellationToken>()), Times.Once);
        _report.Verify(r => r.GetInitialEligibleOfficesAsync(Fy, _requester, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunOnce_Finished_TellsTheRequesterThroughTheBell()
    {
        _jobs.Queue(Fy, _requester.Id);

        await Worker().RunOnceAsync();

        RecordingNotificationService.Notified bell = Assert.Single(_notifications.NotifyCalls);
        Assert.Equal(
            (NotificationTypes.InitialWfpExportReady, _requester.Id,
             $"Your FY {Fy} initial WFP export is ready (3 offices)", $"/budget-planning/report?fiscalYear={Fy}"),
            (bell.Type, bell.Recipient.Id, bell.Text, bell.Link));
    }

    // ── Failure isolation (decision 7, §3 case 5) ───────────────────────────────

    [Fact]
    public async Task RunOnce_OneOfficeThrows_ItIsSkippedAndNoted_TheRestAreStillBuilt()
    {
        _report.Setup(r => r.GetInitialReportAsync(Pho.OfficeId, Fy, null, _requester, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("bad data in PHO's AIP"));
        InitialWfpExportJob job = _jobs.Queue(Fy, _requester.Id);

        await Worker().RunOnceAsync();

        InitialWfpExportJob done = _jobs[job.Id];
        Assert.Equal(BackgroundJobStatus.DoneWithProblems, done.Status);
        Assert.Equal((3, 3), (done.DoneCount, done.TotalCount));      // a skipped office still counts as processed
        Assert.Equal(
            [new InitialWfpExportProblemDto("PHO", ExportJobWorker.OfficeFailedReason)],
            InitialWfpExportFiles.ParseProblems(done.ProblemsJson));

        Dictionary<string, string> zip = OpenZip(done.FileKey!);
        Assert.Equal(["README.txt", $"WFP_OPA_{Fy}.xlsx", $"WFP_PEO_{Fy}.xlsx"], zip.Keys.Order(StringComparer.Ordinal));

        Assert.Equal($"Your FY {Fy} initial WFP export is ready (2 of 3 offices)", Assert.Single(_notifications.NotifyCalls).Text);
    }

    [Fact]
    public async Task RunOnce_OfficeWorkbookThrows_IsSkippedTheSameWay()
    {
        _excel.Setup(e => e.Export(It.Is<WfpReportDto>(r => r.OfficeCode == "OPA"))).Throws(new IndexOutOfRangeException());
        InitialWfpExportJob job = _jobs.Queue(Fy, _requester.Id);

        await Worker().RunOnceAsync();

        Assert.Equal(BackgroundJobStatus.DoneWithProblems, _jobs[job.Id].Status);
        Assert.Equal(["OPA"], InitialWfpExportFiles.ParseProblems(_jobs[job.Id].ProblemsJson).Select(p => p.OfficeCode));
    }

    [Fact]
    public async Task RunOnce_OfficeNoLongerConsolidated_IsSkippedWithTheReportsOwnReason()
    {
        // Eligible when listed, then returned for edits before its turn came.
        _report.Setup(r => r.GetInitialReportAsync(Peo.OfficeId, Fy, null, _requester, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<WfpReportDto>.Conflict("This office's AIP is not consolidated yet."));
        InitialWfpExportJob job = _jobs.Queue(Fy, _requester.Id);

        await Worker().RunOnceAsync();

        Assert.Equal(
            [new InitialWfpExportProblemDto("PEO", "This office's AIP is not consolidated yet.")],
            InitialWfpExportFiles.ParseProblems(_jobs[job.Id].ProblemsJson));
    }

    [Fact]
    public async Task RunOnce_EveryOfficeFails_IsStillDoneWithProblems_WithOnlyTheReadme()
    {
        // Decision 7: only a failure of the job itself ends it Failed; each office failing is noted.
        _excel.Setup(e => e.Export(It.IsAny<WfpReportDto>())).Throws(new InvalidOperationException());
        InitialWfpExportJob job = _jobs.Queue(Fy, _requester.Id);

        await Worker().RunOnceAsync();

        Assert.Equal(BackgroundJobStatus.DoneWithProblems, _jobs[job.Id].Status);
        Assert.Equal(["README.txt"], OpenZip(_jobs[job.Id].FileKey!).Keys);
    }

    // ── README (decision 7) ─────────────────────────────────────────────────────

    [Fact]
    public async Task RunOnce_Readme_SaysWhenAndForWhom_ListsTheFiles_AndEachSkippedOfficeWithItsReason()
    {
        _report.Setup(r => r.GetInitialReportAsync(Pho.OfficeId, Fy, null, _requester, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException());
        InitialWfpExportJob job = _jobs.Queue(Fy, _requester.Id);

        await Worker().RunOnceAsync();

        string readme = OpenZip(_jobs[job.Id].FileKey!)["README.txt"];
        Assert.Equal(
            $"Initial WFP - FY {Fy}\r\n" +
            "Built 2026-10-15 14:30 (Manila time) for Jose Santos.\r\n" +
            $"Offices whose FY {Fy} AIP was marked as done at that time: 3.\r\n" +
            "\r\n" +
            "Included (2):\r\n" +
            $"  WFP_OPA_{Fy}.xlsx  OPA - Office of the Provincial Administrator\r\n" +
            $"  WFP_PEO_{Fy}.xlsx  PEO - Provincial Engineering Office\r\n" +
            "\r\n" +
            "Skipped (1):\r\n" +
            $"  PHO - Provincial Health Office: {ExportJobWorker.OfficeFailedReason}\r\n",
            readme);
    }

    [Fact]
    public async Task RunOnce_Readme_NothingSkipped_SaysNone()
    {
        InitialWfpExportJob job = _jobs.Queue(Fy, _requester.Id);

        await Worker().RunOnceAsync();

        Assert.EndsWith("Skipped (0):\r\n  none\r\n", OpenZip(_jobs[job.Id].FileKey!)["README.txt"]);
    }

    // ── Progress and heartbeat (§3 case 2, decision 11) ─────────────────────────

    [Fact]
    public async Task RunOnce_RecordsTheTotalFirst_ThenProgressAfterEachOffice_WithAFreshHeartbeat()
    {
        _clock.StepPerRead = TimeSpan.FromSeconds(5);
        _report.Setup(r => r.GetInitialReportAsync(Pho.OfficeId, Fy, null, _requester, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException());
        InitialWfpExportJob job = _jobs.Queue(Fy, _requester.Id);

        await Worker().RunOnceAsync();

        // Skipped offices move the count too: "3 of 3" means every office has had its turn.
        Assert.Equal([(0, 3), (1, 3), (2, 3), (3, 3)], _jobs.Progress[job.Id].Select(p => (p.Done, p.Total)));
        IReadOnlyList<DateTime> beats = _jobs.Progress[job.Id].Select(p => p.At).ToList();
        Assert.Equal(beats.Order(), beats);
        Assert.Equal(beats.Count, beats.Distinct().Count());
    }

    [Fact]
    public async Task RunOnce_JobTakenAwayMidRun_StopsWithoutSavingOrFailingIt()
    {
        // Another worker re-queued it as stuck while this one was paused (decision 11).
        _report.Setup(r => r.GetInitialReportAsync(Pho.OfficeId, Fy, null, _requester, It.IsAny<CancellationToken>()))
            .Callback(() => _jobs.ForceStatus(1, BackgroundJobStatus.Queued))
            .ReturnsAsync(ServiceResult<WfpReportDto>.Ok(Report(Pho)));
        InitialWfpExportJob job = _jobs.Queue(Fy, _requester.Id, id: 1);

        await Worker().RunOnceAsync();

        Assert.Equal(BackgroundJobStatus.Queued, _jobs[job.Id].Status);
        Assert.Empty(_files.Saved);
        Assert.Empty(_notifications.NotifyCalls);
        _report.Verify(r => r.GetInitialReportAsync(Peo.OfficeId, It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Job-level failures (decision 7, decision 8) ─────────────────────────────

    [Fact]
    public async Task RunOnce_RequesterHasLostAccess_FailsTheJob_AndBuildsNothing()
    {
        _permissions.Setup(p => p.CanAccessWfpAsync(_requester, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        InitialWfpExportJob job = _jobs.Queue(Fy, _requester.Id);

        await Worker().RunOnceAsync();

        AssertFailed(job.Id, ExportJobWorker.NoAccessMessage);
        _report.Verify(r => r.GetInitialEligibleOfficesAsync(It.IsAny<int>(), It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunOnce_RequesterDeactivated_FailsTheJob()
    {
        _requester.IsActive = false;
        InitialWfpExportJob job = _jobs.Queue(Fy, _requester.Id);

        await Worker().RunOnceAsync();

        AssertFailed(job.Id, ExportJobWorker.NoAccessMessage);
    }

    [Fact]
    public async Task RunOnce_NoOfficeEligibleAnyMore_FailsTheJob()
    {
        OfficesEligible();
        InitialWfpExportJob job = _jobs.Queue(Fy, _requester.Id);

        await Worker().RunOnceAsync();

        AssertFailed(job.Id, InitialWfpExportService.NoneEligibleMessage(Fy));
    }

    [Fact]
    public async Task RunOnce_StorageDown_FailsTheJob_WithAPlainMessage()
    {
        _files.ThrowOnSave = true;
        InitialWfpExportJob job = _jobs.Queue(Fy, _requester.Id);

        await Worker().RunOnceAsync();

        AssertFailed(job.Id, ExportJobWorker.JobFailedMessage);
        Assert.Contains(_logger.Lines, l => l.Level == LogLevel.Error && l.Exception is IOException);
    }

    [Fact]
    public async Task RunOnce_HostShuttingDown_LetsTheCancellationOut_AndLeavesTheJobRunning()
    {
        using CancellationTokenSource shutdown = new();
        _report.Setup(r => r.GetInitialReportAsync(Pho.OfficeId, Fy, null, _requester, It.IsAny<CancellationToken>()))
            .Callback(shutdown.Cancel)
            .ThrowsAsync(new OperationCanceledException());
        InitialWfpExportJob job = _jobs.Queue(Fy, _requester.Id);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Worker().RunOnceAsync(shutdown.Token));

        // Not skipped as a "failed office", not Failed: stuck recovery picks it up after 10 minutes.
        Assert.Equal(BackgroundJobStatus.Running, _jobs[job.Id].Status);
        Assert.Empty(_files.Saved);
    }

    // ── One job per pass ────────────────────────────────────────────────────────

    [Fact]
    public async Task RunOnce_RunsTheOldestQueuedJob_Only()
    {
        InitialWfpExportJob older = _jobs.Queue(Fy, _requester.Id, createdAt: Now.AddMinutes(-2));
        InitialWfpExportJob newer = _jobs.Queue(Fy + 1, _requester.Id, createdAt: Now.AddMinutes(-1));
        _report.Setup(r => r.GetInitialEligibleOfficesAsync(Fy + 1, _requester, It.IsAny<CancellationToken>())).ReturnsAsync([Opa]);

        await Worker().RunOnceAsync();

        Assert.Equal(BackgroundJobStatus.Done, _jobs[older.Id].Status);
        Assert.Equal(BackgroundJobStatus.Queued, _jobs[newer.Id].Status);
    }

    [Fact]
    public async Task RunOnce_NothingQueued_BuildsNothing()
    {
        await Worker().RunOnceAsync();

        Assert.Empty(_files.Saved);
        _report.VerifyNoOtherCalls();
        Assert.Contains(_logger.Lines, l => l.Level == LogLevel.Debug && l.Message.StartsWith("Export worker pass."));
    }

    // ── Stuck jobs (decision 11, §3 case 7) ─────────────────────────────────────

    [Fact]
    public async Task RunOnce_StuckJob_IsQueuedAgain_ThenClaimedAndRunAsTheNextAttempt()
    {
        InitialWfpExportJob stuck = _jobs.Running(Fy, _requester.Id, attempts: 1, heartbeatAt: Now.AddMinutes(-11));

        await Worker().RunOnceAsync();

        Assert.Equal((BackgroundJobStatus.Done, 2), (_jobs[stuck.Id].Status, _jobs[stuck.Id].Attempts));
        Assert.Contains(_logger.Lines, l => l.Level == LogLevel.Warning && l.Message.StartsWith("Export job recovered")
                                         && l.Scope("JobId") is int id && id == stuck.Id);
    }

    [Fact]
    public async Task RunOnce_StuckOnItsThirdAttempt_IsFailed_AndNotRunAgain()
    {
        InitialWfpExportJob stuck = _jobs.Running(Fy, _requester.Id, attempts: BackgroundJob.MaxAttempts, heartbeatAt: Now.AddMinutes(-11));

        await Worker().RunOnceAsync();

        Assert.Equal(
            (BackgroundJobStatus.Failed, BackgroundJob.StoppedUnexpectedlyMessage, BackgroundJob.MaxAttempts),
            (_jobs[stuck.Id].Status, _jobs[stuck.Id].Message, _jobs[stuck.Id].Attempts));
        _report.VerifyNoOtherCalls();
        Assert.Contains(_logger.Lines, l => l.Level == LogLevel.Error && l.Message.StartsWith("Export job ran out of attempts"));
    }

    [Fact]
    public async Task RunOnce_RunningJobWithARecentHeartbeat_IsLeftAlone()
    {
        InitialWfpExportJob live = _jobs.Running(Fy, _requester.Id, attempts: 1, heartbeatAt: Now.AddMinutes(-9));

        await Worker().RunOnceAsync();

        Assert.Equal((BackgroundJobStatus.Running, 1), (_jobs[live.Id].Status, _jobs[live.Id].Attempts));
        _report.VerifyNoOtherCalls();
    }

    // ── Cleanup (decision 12, §3 case 8) ────────────────────────────────────────

    [Fact]
    public async Task RunOnce_ExpiredZip_IsDeleted_AndItsJobMarkedExpired_ButAFreshOneIsKept()
    {
        InitialWfpExportJob old   = _jobs.Finished(Fy, _requester.Id, "initial-wfp-exports/1/old.zip", expiresAt: Now.AddMinutes(-1));
        InitialWfpExportJob fresh = _jobs.Finished(Fy, _requester.Id, "initial-wfp-exports/2/new.zip", expiresAt: Now.AddDays(6));
        _files.Put(old.FileKey!);
        _files.Put(fresh.FileKey!);

        await Worker().RunOnceAsync();

        Assert.Equal(["initial-wfp-exports/2/new.zip"], _files.Saved.Keys);
        Assert.Equal(BackgroundJobStatus.Expired, _jobs[old.Id].Status);
        Assert.Equal("initial-wfp-exports/1/old.zip", _jobs[old.Id].FileKey);   // the row is history
        Assert.Equal(BackgroundJobStatus.Done, _jobs[fresh.Id].Status);
    }

    [Fact]
    public async Task RunOnce_CleanupFails_TheJobStaysDone_AndThePassStillRunsQueuedWork()
    {
        InitialWfpExportJob old = _jobs.Finished(Fy, _requester.Id, "initial-wfp-exports/1/old.zip", expiresAt: Now.AddMinutes(-1));
        _files.ThrowOnDelete = true;
        InitialWfpExportJob queued = _jobs.Queue(Fy, _requester.Id);

        await Worker().RunOnceAsync();

        Assert.Equal(BackgroundJobStatus.Done, _jobs[old.Id].Status);       // tried again next pass
        Assert.Equal(BackgroundJobStatus.Done, _jobs[queued.Id].Status);
    }

    // ── Logging (decision 15) ───────────────────────────────────────────────────

    [Fact]
    public async Task RunOnce_EveryLineForAJob_CarriesItsJobIdAndKind()
    {
        _report.Setup(r => r.GetInitialReportAsync(Pho.OfficeId, Fy, null, _requester, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException());
        InitialWfpExportJob job = _jobs.Queue(Fy, _requester.Id);

        await Worker().RunOnceAsync();

        List<RecordingLogger<ExportJobWorker>.Line> jobLines = _logger.Lines.Where(l => l.Level >= LogLevel.Information).ToList();
        Assert.Equal(
            ["Export job claimed.", "Office built.", "Office skipped.", "Office built.", "Export job finished."],
            jobLines.Select(l => l.Message[..(l.Message.IndexOf('.') + 1)]));
        Assert.All(jobLines, l =>
        {
            Assert.Equal(job.Id, l.Scope("JobId"));
            Assert.Equal(nameof(InitialWfpExportJob), l.Scope("JobKind"));
        });

        // The skipped office's exception is logged, though the README only gives the plain reason.
        Assert.IsType<InvalidOperationException>(Assert.Single(jobLines, l => l.Level == LogLevel.Warning).Exception);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private void OfficesEligible(params WfpReportOfficeDto[] offices)
        => _report.Setup(r => r.GetInitialEligibleOfficesAsync(Fy, _requester, It.IsAny<CancellationToken>()))
            .ReturnsAsync(offices);

    private void OfficeBuilds(WfpReportOfficeDto office)
        => _report.Setup(r => r.GetInitialReportAsync(office.OfficeId, Fy, null, _requester, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<WfpReportDto>.Ok(Report(office)));

    private static WfpReportDto Report(WfpReportOfficeDto office)
        => new(Fy, office.OfficeCode, office.OfficeName, ReserveRate: 0m, FundSourceReports: [], WfpReportSources.Aip);

    private void AssertFailed(int jobId, string message)
    {
        Assert.Equal((BackgroundJobStatus.Failed, message), (_jobs[jobId].Status, _jobs[jobId].Message));
        Assert.NotNull(_jobs[jobId].FinishedAt);
        Assert.Null(_jobs[jobId].FileKey);
        Assert.Empty(_files.Saved);
        Assert.Empty(_notifications.NotifyCalls);
    }

    /// <summary>The saved ZIP's entries and their text.</summary>
    private Dictionary<string, string> OpenZip(string key)
    {
        using ZipArchive zip = new(new MemoryStream(_files.Saved[key]), ZipArchiveMode.Read);
        return zip.Entries.ToDictionary(
            e => e.FullName,
            e => { using StreamReader reader = new(e.Open(), Encoding.UTF8); return reader.ReadToEnd(); });
    }

    // ── Fakes ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>initial_wfp_export_jobs</c> in memory, behind both repositories the worker uses. Each method
    /// does what the real one's SQL does, including "only while Running" on every write.
    /// </summary>
    private sealed class FakeJobTables : IBackgroundJobRepository, IInitialWfpExportJobRepository
    {
        private readonly Dictionary<int, InitialWfpExportJob> _rows = [];
        private int _nextId = 1;

        public Dictionary<int, List<(int Done, int Total, DateTime At)>> Progress { get; } = [];

        public InitialWfpExportJob this[int id] => _rows[id];

        public InitialWfpExportJob Queue(int fiscalYear, Guid requester, DateTime? createdAt = null, int? id = null)
            => Add(new InitialWfpExportJob
            {
                Id = id ?? _nextId, FiscalYear = fiscalYear, RequestedById = requester,
                Status = BackgroundJobStatus.Queued, CreatedAt = createdAt ?? Now.AddMinutes(-1),
            });

        public InitialWfpExportJob Running(int fiscalYear, Guid requester, int attempts, DateTime heartbeatAt)
            => Add(new InitialWfpExportJob
            {
                Id = _nextId, FiscalYear = fiscalYear, RequestedById = requester, Status = BackgroundJobStatus.Running,
                CreatedAt = Now.AddHours(-1), ClaimedAt = heartbeatAt, HeartbeatAt = heartbeatAt, Attempts = attempts,
            });

        public InitialWfpExportJob Finished(int fiscalYear, Guid requester, string fileKey, DateTime expiresAt)
            => Add(new InitialWfpExportJob
            {
                Id = _nextId, FiscalYear = fiscalYear, RequestedById = requester, Status = BackgroundJobStatus.Done,
                CreatedAt = Now.AddDays(-7), FinishedAt = expiresAt - InitialWfpExportJob.KeepFor,
                FileKey = fileKey, ExpiresAt = expiresAt, Attempts = 1,
            });

        public void ForceStatus(int id, BackgroundJobStatus status) => _rows[id].Status = status;

        private InitialWfpExportJob Add(InitialWfpExportJob job)
        {
            _rows[job.Id] = job;
            _nextId = Math.Max(_nextId, job.Id + 1);
            return job;
        }

        private bool IsRunning(int id) => _rows.TryGetValue(id, out InitialWfpExportJob? j) && j.Status == BackgroundJobStatus.Running;

        // IBackgroundJobRepository

        public Task<TJob?> ClaimNextAsync<TJob>(DateTime now, CancellationToken ct = default) where TJob : BackgroundJob
        {
            InitialWfpExportJob? next = _rows.Values
                .Where(j => j.Status == BackgroundJobStatus.Queued)
                .OrderBy(j => j.CreatedAt).ThenBy(j => j.Id)
                .FirstOrDefault();
            if (next is null) return Task.FromResult<TJob?>(null);

            next.Status = BackgroundJobStatus.Running;
            next.ClaimedAt = next.HeartbeatAt = now;
            next.Attempts++;
            return Task.FromResult<TJob?>(Copy(next) as TJob);
        }

        public Task<IReadOnlyList<RecoveredJob>> RecoverStuckAsync<TJob>(DateTime now, CancellationToken ct = default) where TJob : BackgroundJob
        {
            List<RecoveredJob> recovered = [];
            foreach (InitialWfpExportJob j in _rows.Values.Where(j => j.Status == BackgroundJobStatus.Running
                                                                    && (j.HeartbeatAt ?? j.ClaimedAt) < now - BackgroundJob.StuckAfter))
            {
                bool failed = j.Attempts >= BackgroundJob.MaxAttempts;
                j.Status = failed ? BackgroundJobStatus.Failed : BackgroundJobStatus.Queued;
                if (failed) { j.Message = BackgroundJob.StoppedUnexpectedlyMessage; j.FinishedAt = now; }
                recovered.Add(new RecoveredJob(j.Id, j.Attempts, failed));
            }
            return Task.FromResult<IReadOnlyList<RecoveredJob>>(recovered);
        }

        public Task<IReadOnlyList<TJob>> GetExpiredAsync<TJob>(DateTime now, CancellationToken ct = default) where TJob : BackgroundJob, IExpiringJob
            => Task.FromResult<IReadOnlyList<TJob>>(_rows.Values
                .Where(j => j.Status is BackgroundJobStatus.Done or BackgroundJobStatus.DoneWithProblems && j.ExpiresAt <= now)
                .Select(j => (TJob)(object)Copy(j)).ToList());

        public Task MarkExpiredAsync<TJob>(int id, CancellationToken ct = default) where TJob : BackgroundJob
        {
            if (_rows[id].Status is BackgroundJobStatus.Done or BackgroundJobStatus.DoneWithProblems)
                _rows[id].Status = BackgroundJobStatus.Expired;
            return Task.CompletedTask;
        }

        public Task<bool> HeartbeatAsync<TJob>(int id, DateTime now, CancellationToken ct = default) where TJob : BackgroundJob
        {
            if (!IsRunning(id)) return Task.FromResult(false);
            _rows[id].HeartbeatAt = now;
            return Task.FromResult(true);
        }

        public Task<bool> FailAsync<TJob>(int id, string message, DateTime now, CancellationToken ct = default) where TJob : BackgroundJob
        {
            if (!IsRunning(id)) return Task.FromResult(false);
            (_rows[id].Status, _rows[id].Message, _rows[id].FinishedAt) = (BackgroundJobStatus.Failed, message, now);
            return Task.FromResult(true);
        }

        // IInitialWfpExportJobRepository (the worker only writes progress and the outcome)

        public Task<bool> UpdateProgressAsync(int id, int doneCount, int totalCount, DateTime now, CancellationToken ct = default)
        {
            if (!IsRunning(id)) return Task.FromResult(false);
            (_rows[id].DoneCount, _rows[id].TotalCount, _rows[id].HeartbeatAt) = (doneCount, totalCount, now);
            if (!Progress.TryGetValue(id, out List<(int, int, DateTime)>? list)) Progress[id] = list = [];
            list.Add((doneCount, totalCount, now));
            return Task.FromResult(true);
        }

        public Task<bool> CompleteAsync(int id, InitialWfpExportCompletion c, DateTime now, CancellationToken ct = default)
        {
            if (!IsRunning(id)) return Task.FromResult(false);
            InitialWfpExportJob j = _rows[id];
            (j.Status, j.DoneCount, j.TotalCount, j.ProblemsJson, j.FileKey, j.ExpiresAt, j.FinishedAt, j.HeartbeatAt)
                = (c.Status, c.DoneCount, c.TotalCount, c.ProblemsJson, c.FileKey, c.ExpiresAt, now, now);
            return Task.FromResult(true);
        }

        public Task AddAsync(InitialWfpExportJob job, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<InitialWfpExportJob?> GetByIdAsync(int id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<InitialWfpExportJob?> GetLatestByFiscalYearAsync(int fiscalYear, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<InitialWfpExportJob?> GetActiveByFiscalYearAsync(int fiscalYear, CancellationToken ct = default) => throw new NotSupportedException();

        /// <summary>Reads hand out a copy, like an untracked EF read: the worker cannot change a row except through a write.</summary>
        private static InitialWfpExportJob Copy(InitialWfpExportJob j) => new()
        {
            Id = j.Id, Status = j.Status, RequestedById = j.RequestedById, CreatedAt = j.CreatedAt,
            ClaimedAt = j.ClaimedAt, HeartbeatAt = j.HeartbeatAt, Attempts = j.Attempts, Message = j.Message,
            FinishedAt = j.FinishedAt, FiscalYear = j.FiscalYear, DoneCount = j.DoneCount, TotalCount = j.TotalCount,
            ProblemsJson = j.ProblemsJson, FileKey = j.FileKey, ExpiresAt = j.ExpiresAt,
        };
    }

    private sealed class FakeFileStore : IFileStore
    {
        public Dictionary<string, byte[]> Saved { get; } = [];
        public bool ThrowOnSave { get; set; }
        public bool ThrowOnDelete { get; set; }

        public void Put(string key) => Saved[key] = [1];

        public async Task SaveAsync(string key, Stream content, CancellationToken ct = default)
        {
            if (ThrowOnSave) throw new IOException("storage unavailable");
            using MemoryStream copy = new();
            await content.CopyToAsync(copy, ct);
            Saved[key] = copy.ToArray();
        }

        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default)
            => Task.FromResult<Stream?>(Saved.TryGetValue(key, out byte[]? b) ? new MemoryStream(b) : null);

        public Task DeleteAsync(string key, CancellationToken ct = default)
        {
            if (ThrowOnDelete) throw new IOException("storage unavailable");
            Saved.Remove(key);
            return Task.CompletedTask;
        }
    }

    /// <summary>A clock that stands still, or moves <see cref="StepPerRead"/> each time it is read.</summary>
    private sealed class MovingClock(DateTime start) : TimeProvider
    {
        public DateTime Now { get; private set; } = start;
        public TimeSpan StepPerRead { get; set; } = TimeSpan.Zero;

        public override DateTimeOffset GetUtcNow()
        {
            DateTime now = Now;
            Now += StepPerRead;
            return new DateTimeOffset(now);
        }
    }
}
