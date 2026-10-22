using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;

namespace PPDO.Tests.Application;

/// <summary>
/// Comment threads while an SAIP is open (v1.10.0 — PPDO-247, spec §4): the thread list reads the
/// copy's groups; the AIP's own history stays on the approved groups.
///
/// <para>
/// ⚠️ <b>Red-tested.</b> Without the <c>WorkingGroupsAsync</c> swap the copy's thread disappears from
/// the list; with <c>workingCopy: true</c> on the history, the AIP's hand-offs disappear from it.
/// </para>
/// </summary>
public sealed partial class AipReviewCommentServiceTests
{
    private const int CopyGroupId = 951;

    private void OpenSaipCopy()
        => _aipRepo.Setup(r => r.GetDraftGroupsForOfficeAsync(RecordId, OfficeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new AipOffice
            {
                Id = CopyGroupId, AipRecordId = RecordId, OfficeId = OfficeId, RefCode = "1000-000-1-01-010",
                Name = "PPDO", Sector = "GENERAL", WorkflowStatus = AipWorkflowStatus.Draft, SaipId = 9, SourceId = GroupA,
            }]);

    [Fact]
    public async Task GetForOffice_WithAnSaipOpen_ListsTheCopysThreads_NotTheApprovedTreesOld_Ones()
    {
        CommentAt(0).AipOfficeId = GroupA;                 // from the AIP's own review, long closed
        AipReviewComment onCopy = CommentAt(10);
        onCopy.AipOfficeId = CopyGroupId;
        AipReviewCommentService sut = Build();
        OpenSaipCopy();

        ServiceResult<AipReviewCommentsDto> result = await sut.GetForOfficeAsync(RecordId, OfficeId, PpdoReviewer());

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(onCopy.Id, Assert.Single(result.Value!.Comments).Id);
    }

    [Fact]
    public async Task GetHistory_WithAnSaipOpen_StaysOnTheAipsOwnHandOffs()
    {
        HandOff(1, AuditAction.SubmitToPpdo, AipWorkflowStatus.DepartmentReview, 0);
        HandOff(2, AuditAction.SubmitToDeptHead, AipWorkflowStatus.Draft, 24, groupId: CopyGroupId);   // the SAIP's
        AipReviewCommentService sut = Build();
        OpenSaipCopy();

        ServiceResult<AipOfficeHistoryDto> result = await sut.GetHistoryAsync(RecordId, OfficeId, PpdoReviewer());

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1L, Assert.Single(result.Value!.Entries).Id);
    }
}
