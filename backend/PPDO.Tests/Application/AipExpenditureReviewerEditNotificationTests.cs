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
/// "PPDO reviewer edit" in the notification feed (v1.9.0 — PPDO-221, spec §3.1): the office's users
/// who can open the activity, linking to it. Same harness as <see cref="AipExpenditureReviewerEditTests"/>.
/// </summary>
public sealed partial class AipExpenditureReviewerEditTests
{
    private readonly Mock<INotificationAudience> _audience = new();

    private void NamedActivity()
        => _aipRepo.Setup(r => r.GetActivityByIdAsync(ActivityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipActivity { Id = ActivityId, ProjectId = ProjectId, Name = "Seed distribution" });

    [Fact]
    public async Task ReviewerEdit_NotifiesTheOfficesActivityReaders_WithTheChange()
    {
        NamedActivity();
        User reviewer = Reviewer();
        User encoder = OwnEncoder();
        _audience.Setup(a => a.ActivityReadersAsync(
                It.Is<AipOffice>(o => o.Id == AipOfficeId), It.Is<AipProgram>(p => p.Id == ProgramId), It.IsAny<CancellationToken>()))
            .ReturnsAsync([encoder]);

        ServiceResult<AipExpenditureWriteResultDto> result = await Build().UpdateAsync(LineId, Annualise(), reviewer, Version);

        Assert.True(result.IsSuccess, result.Error);
        RecordingNotificationService.Call call = _notifications.Single(NotificationTypes.AipReviewerEdit);
        Assert.Same(reviewer, call.Actor);
        NotificationBatch batch = (await call.Build(_audience.Object, default))!;
        Assert.Equal("PPDO edited Seed distribution: 5-02-03-010 Office Supplies Expenses MOOE ₱60,000.00 → ₱120,000.00", batch.Text);
        Assert.Equal(
            $"/budget-planning/aip/entry?fiscalYear=2028&programId={ProgramId}&projectId={ProjectId}&activityId={ActivityId}",
            batch.Link);
        Assert.Equal([encoder], batch.Recipients);
    }

    [Fact]
    public async Task ReviewerAdd_NotifiesWithTheAccountAdded()
    {
        NamedActivity();

        Assert.True((await Build().AddAsync(ActivityId, AddTravel(), Reviewer())).IsSuccess);

        NotificationBatch batch = (await _notifications.Single(NotificationTypes.AipReviewerEdit)
            .Build(_audience.Object, default))!;
        Assert.Equal("PPDO added 5-02-01-010 Traveling Expenses - Local to Seed distribution", batch.Text);
    }

    [Fact]
    public async Task ARefusedReviewerEdit_NotifiesNobody()
    {
        await Build().UpdateAsync(LineId, Annualise(reason: null), Reviewer(), Version);   // no reason: 400
        Assert.Empty(_notifications.Calls);
    }

    [Fact]
    public async Task TheOfficesOwnEdit_IsNotAReviewerEdit_AndNotifiesNobody()
    {
        _office.WorkflowStatus = AipWorkflowStatus.Draft;

        ServiceResult<AipExpenditureWriteResultDto> result = await Build().UpdateAsync(LineId, Annualise(reason: null), OwnEncoder(), Version);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(_notifications.Calls);
    }

    [Fact]
    public async Task ReviewerEdit_WhenResolvingRecipientsFails_StillSavesTheEditAndItsComment()
    {
        Mock<INotificationAudience> broken = new();
        broken.Setup(a => a.ActivityReadersAsync(It.IsAny<AipOffice>(), It.IsAny<AipProgram>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("user query failed"));
        _notifications.ForwardTo = new NotificationService(
            new Mock<INotificationRepository>().Object, broken.Object, NullLogger<NotificationService>.Instance);

        ServiceResult<AipExpenditureWriteResultDto> result = await Build().UpdateAsync(LineId, Annualise(), Reviewer(), Version);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(120_000m, _line.Mooe);
        _comments.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
