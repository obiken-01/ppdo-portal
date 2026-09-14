using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// <see cref="AipNotificationService"/> — the sidebar count and the returned notice (V18-58 / PPDO-75,
/// <c>AIP_Review_Spec.md</c> §6.5).
///
/// <para>
/// ⚠️ <b>Two things matter most here.</b> Who is counted for what — the PPDO count must never run for
/// a caller without the cross-office flag, since this read fires on every portal page — and what
/// "returned" means: a state the office sits in, not a message, so a Draft that was never submitted
/// must not read as returned.
/// </para>
/// </summary>
public sealed class AipNotificationServiceTests
{
    private const int OwnOffice = 15;
    private const int Record28  = 44;
    private const int Record29  = 45;

    private readonly Mock<IAipRepository>     _aipRepo     = new();
    private readonly Mock<IAuditRepository>   _auditRepo   = new();
    private readonly Mock<IPermissionService> _permissions = new();

    private readonly List<AipOfficeStatusRow> _ownRows = [];
    private readonly Dictionary<int, int> _ppdoCounts = [];
    private string? _latestHandOff;

    private AipNotificationService Build(bool crossOffice = false, bool deptHead = false)
    {
        _permissions.Setup(p => p.CanReviewAllOfficesAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(crossOffice);
        _permissions.Setup(p => p.CanReviewBudgetPlanningAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(deptHead);

        _aipRepo.Setup(r => r.CountOfficesAtStatusByFiscalYearAsync(
                AipWorkflowStatus.SubmittedToPpdo, PlanningStatus.Draft,
                AipFiscalYears.FirstEnteredFiscalYear, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _ppdoCounts);
        _aipRepo.Setup(r => r.GetOfficeStatusesAsync(
                OwnOffice, PlanningStatus.Draft, AipFiscalYears.FirstEnteredFiscalYear,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _ownRows);

        _auditRepo.Setup(a => a.GetLatestActionAsync(
                "aip_offices", It.IsAny<IReadOnlyList<int>>(), It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _latestHandOff);

        return new AipNotificationService(_aipRepo.Object, _auditRepo.Object, _permissions.Object);
    }

    private static User Caller(int? officeId = OwnOffice) => new()
    {
        Id = Guid.NewGuid(), FullName = "Caller", Username = "caller", PasswordHash = "x",
        OfficeId = officeId,
    };

    private void OwnGroup(int recordId, int fiscalYear, int groupId, string status)
        => _ownRows.Add(new AipOfficeStatusRow(recordId, fiscalYear, groupId, status));

    private async Task<AipReviewNotificationsDto> ReadAsync(AipNotificationService sut, User? caller = null)
    {
        ServiceResult<AipReviewNotificationsDto> result = await sut.GetForCallerAsync(caller ?? Caller());
        Assert.True(result.IsSuccess);
        return result.Value!;
    }

    // ── The PPDO reviewer's count ──────────────────────────────────────────────

    [Fact]
    public async Task PpdoReviewer_CountsOfficesAtPpdoAcrossOpenYears_AndLinksToTheEarliest()
    {
        _ppdoCounts[2029] = 1;
        _ppdoCounts[2028] = 3;

        AipReviewNotificationsDto dto = await ReadAsync(Build(crossOffice: true));

        Assert.Equal(4, dto.PendingForPpdo);
        Assert.Equal(2028, dto.PpdoFiscalYear);
    }

    [Fact]
    public async Task PpdoReviewer_NothingAtPpdo_IsZeroWithNoYear()
    {
        AipReviewNotificationsDto dto = await ReadAsync(Build(crossOffice: true));

        Assert.Equal(0, dto.PendingForPpdo);
        Assert.Null(dto.PpdoFiscalYear);
    }

    [Fact]
    public async Task WithoutTheCrossOfficeFlag_ThePpdoCountIsNeverQueried()
    {
        _ppdoCounts[2028] = 3;

        AipReviewNotificationsDto dto = await ReadAsync(Build(deptHead: true));

        Assert.Equal(0, dto.PendingForPpdo);
        _aipRepo.Verify(r => r.CountOfficesAtStatusByFiscalYearAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── The department head's count ────────────────────────────────────────────

    [Fact]
    public async Task DepartmentHead_OwnOfficeInDepartmentReview_CountsOne()
    {
        OwnGroup(Record28, 2028, 1, AipWorkflowStatus.DepartmentReview);
        OwnGroup(Record28, 2028, 2, AipWorkflowStatus.DepartmentReview); // a second sector group

        AipReviewNotificationsDto dto = await ReadAsync(Build(deptHead: true));

        Assert.Equal(1, dto.PendingForDepartmentHead);
        Assert.Equal(2028, dto.DepartmentHeadFiscalYear);
    }

    [Theory]
    [InlineData(AipWorkflowStatus.Draft)]
    [InlineData(AipWorkflowStatus.SubmittedToPpdo)]
    [InlineData(AipWorkflowStatus.ReturnedByPpdo)]
    [InlineData(AipWorkflowStatus.Consolidated)]
    public async Task DepartmentHead_OwnOfficeInAnyOtherState_CountsNothing(string status)
    {
        OwnGroup(Record28, 2028, 1, status);

        AipReviewNotificationsDto dto = await ReadAsync(Build(deptHead: true));

        Assert.Equal(0, dto.PendingForDepartmentHead);
        Assert.Null(dto.DepartmentHeadFiscalYear);
    }

    [Fact]
    public async Task Encoder_OwnOfficeInDepartmentReview_HasNoCount()
    {
        OwnGroup(Record28, 2028, 1, AipWorkflowStatus.DepartmentReview);

        AipReviewNotificationsDto dto = await ReadAsync(Build());

        Assert.Equal(0, dto.PendingForDepartmentHead);
        Assert.Equal(0, dto.PendingForPpdo);
    }

    [Fact]
    public async Task BothFlags_GetBothCounts()
    {
        _ppdoCounts[2028] = 2;
        OwnGroup(Record28, 2028, 1, AipWorkflowStatus.DepartmentReview);

        AipReviewNotificationsDto dto = await ReadAsync(Build(crossOffice: true, deptHead: true));

        Assert.Equal(2, dto.PendingForPpdo);
        Assert.Equal(1, dto.PendingForDepartmentHead);
    }

    [Fact]
    public async Task GroupsThatDisagree_ReadAsTheLeastAdvanced()
    {
        OwnGroup(Record28, 2028, 1, AipWorkflowStatus.SubmittedToPpdo);
        OwnGroup(Record28, 2028, 2, AipWorkflowStatus.DepartmentReview);

        AipReviewNotificationsDto dto = await ReadAsync(Build(deptHead: true));

        Assert.Equal(1, dto.PendingForDepartmentHead);
    }

    // ── Returned ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReturnedByPpdo_IsANoticeForEncoderAndDepartmentHeadAlike(bool deptHead)
    {
        OwnGroup(Record28, 2028, 1, AipWorkflowStatus.ReturnedByPpdo);

        AipReviewNotificationsDto dto = await ReadAsync(Build(deptHead: deptHead));

        AipReturnedNoticeDto notice = Assert.Single(dto.Returned);
        Assert.Equal(2028, notice.FiscalYear);
        Assert.Equal(AipNotificationService.ReturnedByPpdo, notice.ReturnedBy);
    }

    [Fact]
    public async Task DraftAfterReturnToEncoder_IsANoticeForTheEncoder()
    {
        OwnGroup(Record28, 2028, 1, AipWorkflowStatus.Draft);
        _latestHandOff = AuditAction.ReturnToEncoder;

        AipReviewNotificationsDto dto = await ReadAsync(Build());

        AipReturnedNoticeDto notice = Assert.Single(dto.Returned);
        Assert.Equal(AipNotificationService.ReturnedByDepartmentHead, notice.ReturnedBy);
    }

    [Fact]
    public async Task DraftAfterReturnToEncoder_IsNotANoticeForTheDepartmentHead_WhoDidIt()
    {
        OwnGroup(Record28, 2028, 1, AipWorkflowStatus.Draft);
        _latestHandOff = AuditAction.ReturnToEncoder;

        AipReviewNotificationsDto dto = await ReadAsync(Build(deptHead: true));

        Assert.Empty(dto.Returned);
        _auditRepo.Verify(a => a.GetLatestActionAsync(
            It.IsAny<string>(), It.IsAny<IReadOnlyList<int>>(), It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(null)]                         // never submitted
    [InlineData(AuditAction.SubmitToDeptHead)] // cannot really leave it Draft, but a later hand-off is not a return
    public async Task DraftWhoseLatestHandOffIsNotAReturn_IsNotANotice(string? latest)
    {
        OwnGroup(Record28, 2028, 1, AipWorkflowStatus.Draft);
        _latestHandOff = latest;

        AipReviewNotificationsDto dto = await ReadAsync(Build());

        Assert.Empty(dto.Returned);
    }

    [Fact]
    public async Task ReturnedNotices_AreEarliestYearFirst()
    {
        OwnGroup(Record29, 2029, 3, AipWorkflowStatus.ReturnedByPpdo);
        OwnGroup(Record28, 2028, 1, AipWorkflowStatus.ReturnedByPpdo);

        AipReviewNotificationsDto dto = await ReadAsync(Build());

        Assert.Equal([2028, 2029], dto.Returned.Select(r => r.FiscalYear));
    }

    [Theory]
    [InlineData(AipWorkflowStatus.DepartmentReview)]
    [InlineData(AipWorkflowStatus.SubmittedToPpdo)]
    [InlineData(AipWorkflowStatus.Consolidated)]
    public async Task AnOfficeNotHandedBack_IsNotANotice(string status)
    {
        OwnGroup(Record28, 2028, 1, status);

        AipReviewNotificationsDto dto = await ReadAsync(Build());

        Assert.Empty(dto.Returned);
    }

    // ── No office ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task CallerWithNoOffice_GetsNoOwnOfficeReads_ButKeepsThePpdoCount()
    {
        _ppdoCounts[2028] = 2;

        AipReviewNotificationsDto dto = await ReadAsync(Build(crossOffice: true, deptHead: true), Caller(officeId: null));

        Assert.Equal(2, dto.PendingForPpdo);
        Assert.Equal(0, dto.PendingForDepartmentHead);
        Assert.Empty(dto.Returned);
        _aipRepo.Verify(r => r.GetOfficeStatusesAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
