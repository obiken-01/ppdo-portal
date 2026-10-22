using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Common;
using PPDO.Domain.Entities;

namespace PPDO.Tests.Application;

/// <summary>
/// The office's division step on an SAIP's working copy (v1.10.0 — PPDO-247, spec decision 6): the
/// same <see cref="AipSubmitService"/> paths, pointed at the copy by <c>AipGroupLookup</c>.
///
/// <para>
/// ⚠️ <b>Red-tested.</b> Removing <c>SaipId = c.SaipId</c> on the new row fails
/// <see cref="Saip_DivisionSubmit_WritesTheSaipsOwnRow_AndLeavesTheAipsRowsAlone"/>; removing the
/// mirror call fails <see cref="Saip_LastDivisionIn_MovesTheCopyAndTheSaip_NotTheApprovedGroups"/>;
/// removing the guard on the AIP's own send fails
/// <see cref="Saip_TheAipsOwnSendToPpdo_IsRefusedWhileAnSaipIsOpen"/>.
/// </para>
/// </summary>
public sealed partial class AipDivisionSubmitServiceTests
{
    private const int SaipId    = 77;
    private const int CopyGroup = 410;

    private AipSaip _saip = null!;
    private AipOffice _copy = null!;

    /// <summary>The approved office Consolidated, SAIP 1 open on it with a Draft copy.</summary>
    private void OpenSaip()
    {
        _group.WorkflowStatus = AipWorkflowStatus.Consolidated;
        _copy = new AipOffice
        {
            Id = CopyGroup, AipRecordId = RecordId, OfficeId = OfficeId, RefCode = "R", Name = "GSO",
            Sector = "GENERAL", WorkflowStatus = AipWorkflowStatus.Draft, SaipId = SaipId, SourceId = Group,
        };
        _saip = new AipSaip
        {
            Id = SaipId, AipRecordId = RecordId, OfficeId = OfficeId, Number = 1, Status = SaipStatus.Draft,
        };
        _aipRepo.Setup(r => r.GetDraftGroupsForOfficeAsync(RecordId, OfficeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => [_copy]);
        _saipRepo.Setup(r => r.GetByIntIdAsync(SaipId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _saip);
    }

    [Fact]
    public async Task Saip_DivisionSubmit_WritesTheSaipsOwnRow_AndLeavesTheAipsRowsAlone()
    {
        OpenSaip();
        // The AIP's own round: every division submitted long ago. None of it may count for the SAIP.
        _divisions.Submit(RecordId, OfficeId, Planning);

        ServiceResult<AipDivisionSubmitResultDto> result =
            await Sut().SubmitDivisionAsync(RecordId, Planning, EncoderIn(Planning));

        Assert.True(result.IsSuccess, result.Error);
        AipDivisionSubmission saipRow = Assert.Single(_divisions.Submissions, s => s.SaipId == SaipId);
        Assert.Equal(Planning, saipRow.DivisionId);
        Assert.Equal(AipDivisionStatus.Submitted, saipRow.Status);
        Assert.Single(_divisions.Submissions, s => s.SaipId is null);   // the AIP's row, untouched
    }

    [Fact]
    public async Task Saip_DivisionAlreadyInOnTheAip_CanStillSubmitOnTheSaip()
    {
        // Without its own rows the copy would read the AIP's "Submitted" and refuse as a duplicate.
        OpenSaip();
        _divisions.Submit(RecordId, OfficeId, Planning);

        ServiceResult<AipDivisionSubmitResultDto> result =
            await Sut().SubmitDivisionAsync(RecordId, Planning, EncoderIn(Planning));

        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public async Task Saip_LastDivisionIn_MovesTheCopyAndTheSaip_NotTheApprovedGroups()
    {
        OpenSaip();
        _divisions.Submit(RecordId, OfficeId, Planning, saipId: SaipId);

        ServiceResult<AipDivisionSubmitResultDto> result =
            await Sut().SubmitDivisionAsync(RecordId, Engineering, EncoderIn(Engineering));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(AipWorkflowStatus.DepartmentReview, _copy.WorkflowStatus);
        Assert.Equal(SaipStatus.DepartmentReview, _saip.Status);
        Assert.Equal(AipWorkflowStatus.Consolidated, _group.WorkflowStatus);
        _saipRepo.Verify(r => r.UpdateAsync(_saip, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Saip_DivisionReturn_TakesTheCopyBackToDraft_AndTheSaipWithIt()
    {
        OpenSaip();
        _copy.WorkflowStatus = AipWorkflowStatus.DepartmentReview;
        _saip.Status = SaipStatus.DepartmentReview;
        _divisions.Submit(RecordId, OfficeId, Planning, saipId: SaipId);
        _divisions.Submit(RecordId, OfficeId, Engineering, saipId: SaipId);

        ServiceResult<AipDivisionSubmitResultDto> result =
            await Sut().ReturnDivisionAsync(RecordId, Planning, DepartmentHead());

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(AipWorkflowStatus.Draft, _copy.WorkflowStatus);
        Assert.Equal(SaipStatus.Draft, _saip.Status);
        Assert.Equal(AipWorkflowStatus.Consolidated, _group.WorkflowStatus);
    }

    [Fact]
    public async Task Saip_TheAipsOwnSendToPpdo_IsRefusedWhileAnSaipIsOpen()
    {
        // Otherwise the copy (DepartmentReview, every division in) would go to PPDO here with no
        // letter and no proposal check.
        OpenSaip();
        _copy.WorkflowStatus = AipWorkflowStatus.DepartmentReview;
        _divisions.Submit(RecordId, OfficeId, Planning, saipId: SaipId);
        _divisions.Submit(RecordId, OfficeId, Engineering, saipId: SaipId);

        ServiceResult<AipSubmitResultDto> result =
            await Sut().SubmitToPpdoAsync(RecordId, OfficeId, DepartmentHead());

        Assert.False(result.IsSuccess);
        Assert.Equal(AipSubmitService.SaipSendElsewhereMessage, result.Error);
        Assert.Equal(AipWorkflowStatus.DepartmentReview, _copy.WorkflowStatus);
    }

    [Fact]
    public async Task Saip_DivisionPanel_ShowsTheCopysStateAndRows()
    {
        OpenSaip();
        _divisions.Submit(RecordId, OfficeId, Planning);                  // the AIP's
        _divisions.Submit(RecordId, OfficeId, Engineering, saipId: SaipId);

        ServiceResult<AipDivisionStatusListDto> result =
            await Sut().GetDivisionsAsync(RecordId, OfficeId, DepartmentHead());

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(AipWorkflowStatus.Draft, result.Value!.OfficeWorkflowStatus);
        Assert.Equal(AipDivisionStatus.Draft, result.Value.Divisions.Single(d => d.DivisionId == Planning).Status);
        Assert.Equal(AipDivisionStatus.Submitted, result.Value.Divisions.Single(d => d.DivisionId == Engineering).Status);
    }

    [Fact]
    public async Task Saip_Checklist_NamesTheDivisionsStillOut_OnTheCopy()
    {
        OpenSaip();
        _divisions.Submit(RecordId, OfficeId, Planning, saipId: SaipId);

        SaipSendChecklist? checklist = await Sut().GetSaipChecklistAsync(RecordId, OfficeId, SaipId);

        Assert.NotNull(checklist);
        Assert.Equal(CopyGroup, Assert.Single(checklist!.Groups).Id);
        Assert.Contains(checklist.DivisionBlockers, b => b.Contains("Engineering Division"));
        Assert.Empty(checklist.WorkBlockers);
        Assert.Null(checklist.CeilingBlocker);
    }
}
