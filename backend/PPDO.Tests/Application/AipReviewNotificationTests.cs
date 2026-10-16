using Microsoft.Extensions.Logging.Abstractions;
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
/// "AIP returned by PPDO" in the notification feed (v1.9.0 — PPDO-221, spec §3.1): every user of the
/// office who can open its AIP, with PPDO's open comments counted. Same harness as
/// <see cref="AipReviewServiceTests"/>.
/// </summary>
public sealed partial class AipReviewServiceTests
{
    private readonly Mock<INotificationAudience> _audience = new();

    [Fact]
    public async Task ReturnToOffice_NotifiesTheOfficesAipReaders_WithPpdosOpenCommentCount()
    {
        User reviewer = PpdoReviewer();
        User encoder = Encoder();
        _audience.Setup(a => a.OfficeAipReadersAsync(OfficeId, It.IsAny<CancellationToken>())).ReturnsAsync([encoder]);
        _reviewComments.Setup(r => r.CountUnresolvedBySideAsync(
                It.Is<IReadOnlyList<int>>(ids => ids.OrderBy(i => i).SequenceEqual(new[] { GroupA, GroupB })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<AipCommentSide, int> { [AipCommentSide.Ppdo] = 3, [AipCommentSide.DepartmentHead] = 5 });

        await Build().ReturnToOfficeAsync(RecordId, OfficeId, reviewer);

        RecordingNotificationService.Call call = _notifications.Single(NotificationTypes.AipReturnedByPpdo);
        Assert.Same(reviewer, call.Actor);
        NotificationBatch batch = (await call.Build(_audience.Object, default))!;
        Assert.Equal("PPDO returned your FY 2028 AIP with 3 comments", batch.Text);   // PPDO's, not the office's 5
        Assert.Equal("/budget-planning/aip/entry?fiscalYear=2028", batch.Link);
        Assert.Equal([encoder], batch.Recipients);
        // ⚠️ The office being returned, never the one the reviewer sits in.
        _audience.Verify(a => a.OfficeAipReadersAsync(OtherOffice, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReopenAccepted_IsAReturnToo()
    {
        GivenEveryGroupIs(AipWorkflowStatus.Consolidated);

        await Build().ReopenOfficeAsync(RecordId, OfficeId, PpdoReviewer());

        _notifications.Single(NotificationTypes.AipReturnedByPpdo);
    }

    [Fact]
    public async Task Accept_NotifiesNobody()
    {
        await Build().AcceptOfficeAsync(RecordId, OfficeId, PpdoReviewer());
        Assert.Empty(_notifications.Calls);
    }

    [Fact]
    public async Task ARefusedReturn_NotifiesNobody()
    {
        GivenEveryGroupIs(AipWorkflowStatus.Draft);

        ServiceResult<AipSubmitResultDto> result = await Build().ReturnToOfficeAsync(RecordId, OfficeId, PpdoReviewer());

        Assert.False(result.IsSuccess);
        Assert.Empty(_notifications.Calls);
    }

    [Fact]
    public async Task ReturnToOffice_WhenResolvingRecipientsFails_StillReturnsTheOffice()
    {
        Mock<INotificationAudience> broken = new();
        broken.Setup(a => a.OfficeAipReadersAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("user query failed"));
        _notifications.ForwardTo = new NotificationService(
            new Mock<INotificationRepository>().Object, broken.Object, NullLogger<NotificationService>.Instance);

        ServiceResult<AipSubmitResultDto> result = await Build().ReturnToOfficeAsync(RecordId, OfficeId, PpdoReviewer());

        Assert.True(result.IsSuccess, result.Error);
        Assert.All(_groups, g => Assert.Equal(AipWorkflowStatus.ReturnedByPpdo, g.WorkflowStatus));
        _officeRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
