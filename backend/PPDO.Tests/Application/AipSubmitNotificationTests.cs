using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Tests.TestHelpers;

namespace PPDO.Tests.Application;

/// <summary>
/// The office hand-offs in the notification feed (v1.9.0 — PPDO-221, spec §3.1) for an office
/// <b>without</b> divisions: sent to PPDO, and the department head's return.
/// Same harness as <see cref="AipSubmitGateTests"/>. The division flow is
/// <see cref="AipDivisionSubmitServiceTests"/>.
/// </summary>
public sealed partial class AipSubmitGateTests
{
    private readonly Mock<INotificationAudience> _audience = new();

    [Fact]
    public async Task SubmitToPpdo_NotifiesPpdosCrossOfficeReviewers_LinkingToTheOfficesReview()
    {
        AipSubmitService sut = Build(GoodActivity());
        GivenLines(700);
        GivenGroupsAt(AipWorkflowStatus.DepartmentReview);
        User head = DeptHead();
        User reviewer = new() { Id = Guid.NewGuid(), FullName = "PPDO reviewer" };
        _audience.Setup(a => a.CrossOfficeReviewersAsync(It.IsAny<CancellationToken>())).ReturnsAsync([reviewer]);

        await sut.SubmitToPpdoAsync(RecordId, OfficeId, head);

        RecordingNotificationService.Call call = _notifications.Single(NotificationTypes.AipSentToPpdo);
        Assert.Same(head, call.Actor);
        NotificationBatch batch = (await call.Build(_audience.Object, default))!;
        Assert.Equal("O7 sent its FY 2028 AIP to PPDO", batch.Text);
        Assert.Equal($"/budget-planning/aip/review?officeId={OfficeId}&fiscalYear=2028", batch.Link);
        Assert.Equal([reviewer], batch.Recipients);
    }

    [Fact]
    public async Task SubmitToPpdo_Refused_NotifiesNobody()
    {
        AipSubmitService sut = Build(GoodActivity());
        GivenGroupsAt(AipWorkflowStatus.Draft);

        Assert.False((await sut.SubmitToPpdoAsync(RecordId, OfficeId, DeptHead())).IsSuccess);
        Assert.Empty(_notifications.Calls);
    }

    [Fact]
    public async Task ReturnToEncoder_InAnOfficeWithoutDivisions_NotifiesTheWholeOffice()
    {
        GivenGroupsAt(AipWorkflowStatus.DepartmentReview);
        AipSubmitService sut = Build();
        User head = DeptHead();
        User encoder = Encoder();
        _audience.Setup(a => a.OfficeAipReadersAsync(OfficeId, It.IsAny<CancellationToken>())).ReturnsAsync([encoder, head]);

        await sut.ReturnToEncoderAsync(RecordId, OfficeId, head);

        RecordingNotificationService.Call call = _notifications.Single(NotificationTypes.AipReturnedByDepartmentHead);
        Assert.Same(head, call.Actor);   // dropped from the recipients by StageAsync
        NotificationBatch batch = (await call.Build(_audience.Object, default))!;
        Assert.Equal("Department Head returned your FY 2028 AIP work", batch.Text);
        Assert.Equal("/budget-planning/aip/entry?fiscalYear=2028", batch.Link);
        Assert.Equal([encoder, head], batch.Recipients);
        _audience.Verify(a => a.DivisionMembersAsync(It.IsAny<int>(), It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SubmitToPpdo_WhenResolvingRecipientsFails_StillGoesToPpdo()
    {
        AipSubmitService sut = Build(GoodActivity());
        GivenLines(700);
        GivenGroupsAt(AipWorkflowStatus.DepartmentReview);
        Mock<INotificationAudience> broken = new();
        broken.Setup(a => a.CrossOfficeReviewersAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("user query failed"));
        _notifications.ForwardTo = new NotificationService(
            new Mock<INotificationRepository>().Object, broken.Object, NullLogger<NotificationService>.Instance);

        ServiceResult<AipSubmitResultDto> result = await sut.SubmitToPpdoAsync(RecordId, OfficeId, DeptHead());

        Assert.True(result.IsSuccess, result.Error);
        Assert.All(_groups, g => Assert.Equal(AipWorkflowStatus.SubmittedToPpdo, g.WorkflowStatus));
    }
}

/// <summary>
/// The division flow in the notification feed (PPDO-221, spec §3.1): a division's submit goes to the
/// office's department heads; a return goes to the returned divisions' users only.
/// </summary>
public sealed partial class AipDivisionSubmitServiceTests
{
    private readonly Mock<INotificationAudience> _audience = new();

    [Fact]
    public async Task SubmitDivision_NotifiesTheOfficesDepartmentHeads()
    {
        User encoder = EncoderIn(Planning);
        User head = DepartmentHead();
        _audience.Setup(a => a.DepartmentHeadsAsync(OfficeId, It.IsAny<CancellationToken>())).ReturnsAsync([head]);

        await Sut().SubmitDivisionAsync(RecordId, Planning, encoder);

        RecordingNotificationService.Call call = _notifications.Single(NotificationTypes.AipDivisionSubmitted);
        Assert.Same(encoder, call.Actor);
        NotificationBatch batch = (await call.Build(_audience.Object, default))!;
        Assert.Equal("Planning Division submitted its FY 2028 AIP work", batch.Text);
        Assert.Equal("/budget-planning/aip/entry?fiscalYear=2028", batch.Link);
        Assert.Equal([head], batch.Recipients);
    }

    [Fact]
    public async Task SubmitDivision_Refused_NotifiesNobody()
    {
        await Sut().SubmitDivisionAsync(RecordId, Engineering, EncoderIn(Planning));   // another division: 403
        Assert.Empty(_notifications.Calls);
    }

    [Fact]
    public async Task ReturnDivision_NotifiesOnlyThatDivisionsUsers()
    {
        _divisions.Submit(RecordId, OfficeId, Planning);
        _divisions.Submit(RecordId, OfficeId, Engineering);
        _group.WorkflowStatus = AipWorkflowStatus.DepartmentReview;

        await Sut().ReturnDivisionAsync(RecordId, Planning, DepartmentHead());

        NotificationBatch batch = (await _notifications.Single(NotificationTypes.AipReturnedByDepartmentHead)
            .Build(_audience.Object, default))!;
        _audience.Verify(a => a.DivisionMembersAsync(OfficeId,
            It.Is<IReadOnlyCollection<int>>(ids => ids.SequenceEqual(new[] { Planning })), It.IsAny<CancellationToken>()), Times.Once);
        _audience.Verify(a => a.OfficeAipReadersAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal("U returned your FY 2028 AIP work", batch.Text);
    }

    [Fact]
    public async Task ReturnToEncoder_InAnOfficeWithDivisions_NotifiesEveryReturnedDivisionsUsers()
    {
        _divisions.Submit(RecordId, OfficeId, Planning);
        _divisions.Submit(RecordId, OfficeId, Engineering);
        _group.WorkflowStatus = AipWorkflowStatus.DepartmentReview;

        await Sut().ReturnToEncoderAsync(RecordId, OfficeId, DepartmentHead());

        await _notifications.Single(NotificationTypes.AipReturnedByDepartmentHead).Build(_audience.Object, default);
        _audience.Verify(a => a.DivisionMembersAsync(OfficeId,
            It.Is<IReadOnlyCollection<int>>(ids => ids.OrderBy(i => i).SequenceEqual(new[] { Planning, Engineering })),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
