using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.InvestmentProposal;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Tests.TestHelpers;

namespace PPDO.Tests.Application;

/// <summary>
/// The proposal comment events of the notification feed (v1.9.0 — PPDO-221, spec §3.1 as revised
/// 2026-10-15 for threads). Same harness as <see cref="InvestmentProposalServiceTests"/>.
///
/// <para>
/// A thread started by PPDO goes to everyone who can open the proposal
/// (<see cref="INotificationAudience.ProposalReadersAsync"/>); one started by the office stays in the office
/// (<see cref="INotificationAudience.ProposalOfficeReadersAsync"/>, PPDO-239). Both rules are pinned in
/// <see cref="NotificationAudienceTests"/>. A reply and a resolve go to the thread's
/// starter and repliers, and only while they can still open it. The actor is dropped by
/// <see cref="NotificationService"/> (<see cref="NotificationServiceTests"/>).
/// </para>
/// </summary>
public sealed partial class InvestmentProposalServiceTests
{
    private readonly Mock<INotificationAudience> _audience = new();

    /// <summary>The audience answers <paramref name="readers"/> for this proposal's office and program.</summary>
    private void Readers(params User[] readers)
        => _audience.Setup(a => a.ProposalReadersAsync(
                It.Is<AipOffice>(o => o.Id == AipOfficeId), It.Is<AipProgram>(p => p.Id == ProgramId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(readers);

    /// <summary>The audience answers <paramref name="readers"/> as the office's own readers of this proposal.</summary>
    private void OfficeReaders(params User[] readers)
        => _audience.Setup(a => a.ProposalOfficeReadersAsync(
                It.Is<AipOffice>(o => o.Id == AipOfficeId), It.Is<AipProgram>(p => p.Id == ProgramId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(readers);

    private async Task<NotificationBatch> BatchOf(string type)
        => (await _notifications.Single(type).Build(_audience.Object, default))!;

    [Fact]
    public async Task ThreadStarted_GoesToEveryoneWhoCanOpenTheProposal_WithTheFrozenSentence()
    {
        int id = await CreateAsync();
        Readers(_encoder, _deptHead, _reviewer);

        await _sut.AddCommentAsync(id, "Check the budget line\nfor seeds", null, _reviewer);

        RecordingNotificationService.Call call = _notifications.Single(NotificationTypes.ProposalComment);
        Assert.Same(_reviewer, call.Actor);
        NotificationBatch batch = await BatchOf(NotificationTypes.ProposalComment);
        Assert.Equal("PPDO commented on the proposal for 001 — Rice Seed Support: \"Check the budget line for seeds\"", batch.Text);
        Assert.Equal($"/budget-planning/proposals/edit?id={id}&comments=open", batch.Link);
        Assert.Equal([_encoder, _deptHead, _reviewer], batch.Recipients);
    }

    [Fact]
    public async Task ThreadStarted_ByTheOffice_NamesTheOffice()
    {
        int id = await CreateAsync();
        OfficeReaders(_deptHead);

        await _sut.AddCommentAsync(id, "Done", null, _encoder);

        Assert.StartsWith("O7 commented on the proposal", (await BatchOf(NotificationTypes.ProposalComment)).Text);
    }

    /// <summary>
    /// PPDO-239: the office's own discussion stays in the office. ⚠️ Not _reviewer or _hostAdmin, who
    /// can open the proposal: the office readers are asked, never everyone who can open it.
    /// </summary>
    [Fact]
    public async Task ThreadStarted_ByTheOffice_GoesToTheOfficesReadersOnly_NotToPpdo()
    {
        int id = await CreateAsync();
        Readers(_encoder, _deptHead, _reviewer, _hostAdmin);
        OfficeReaders(_encoder, _deptHead);

        await _sut.AddCommentAsync(id, "Is the cost right?", null, _encoder);

        NotificationBatch batch = await BatchOf(NotificationTypes.ProposalComment);
        Assert.Equal([_encoder, _deptHead], batch.Recipients);
        Assert.Equal($"/budget-planning/proposals/edit?id={id}&comments=open", batch.Link);
    }

    /// <summary>PPDO-239: a PPDO thread is unchanged — every reader of the proposal, PPDO included.</summary>
    [Fact]
    public async Task ThreadStarted_ByPpdo_StillGoesToEveryReader_NotJustTheOffice()
    {
        int id = await CreateAsync();
        Readers(_encoder, _deptHead, _reviewer, _hostAdmin);
        OfficeReaders(_encoder, _deptHead);

        await _sut.AddCommentAsync(id, "Check the budget line", null, _reviewer);

        Assert.Equal([_encoder, _deptHead, _reviewer, _hostAdmin], (await BatchOf(NotificationTypes.ProposalComment)).Recipients);
    }

    [Fact]
    public async Task Reply_GoesToTheStarterAndRepliers_NotToEveryReader()
    {
        int id = await CreateAsync();
        int head = (await _sut.AddCommentAsync(id, "Fix the objective", null, _encoder)).Value!.Id;
        await _sut.AddCommentAsync(id, "Which part?", head, _reviewer);
        Readers(_encoder, _deptHead, _reviewer, _hostAdmin);

        await _sut.AddCommentAsync(id, "The second sentence", head, _encoder);

        Assert.Equal(2, _notifications.Calls.Count(c => c.Type == NotificationTypes.ProposalReply));
        NotificationBatch batch = (await _notifications.Calls.Last().Build(_audience.Object, default))!;
        Assert.Equal(NotificationTypes.ProposalReply, _notifications.Calls.Last().Type);
        Assert.Equal("Office Encoder replied on the proposal for 001 — Rice Seed Support: \"The second sentence\"", batch.Text);
        // The starter (who is the actor here, and is dropped later by StageAsync) and the replier.
        // ⚠️ Not _deptHead or _hostAdmin: they can open the proposal but never took part.
        Assert.Equal([_encoder, _reviewer], batch.Recipients);
    }

    [Fact]
    public async Task Reply_DoesNotReachAParticipantWhoCanNoLongerOpenTheProposal()
    {
        int id = await CreateAsync();
        int head = (await _sut.AddCommentAsync(id, "Fix the objective", null, _encoder)).Value!.Id;
        Readers(_reviewer);   // the encoder has since lost access

        await _sut.AddCommentAsync(id, "Done on our side", head, _reviewer);

        Assert.Equal([_reviewer], (await BatchOf(NotificationTypes.ProposalReply)).Recipients);
    }

    [Fact]
    public async Task Resolved_GoesToTheStarterAndRepliers()
    {
        int id = await CreateAsync();
        int head = (await _sut.AddCommentAsync(id, "Fix the objective", null, _encoder)).Value!.Id;
        await _sut.AddCommentAsync(id, "Looks fine now", head, _reviewer);
        Readers(_encoder, _deptHead, _reviewer);

        await _sut.ResolveCommentAsync(id, head, _deptHead);

        RecordingNotificationService.Call call = _notifications.Single(NotificationTypes.ProposalResolved);
        Assert.Same(_deptHead, call.Actor);
        NotificationBatch batch = (await call.Build(_audience.Object, default))!;
        Assert.Equal("Office Head resolved your comment on the proposal for 001 — Rice Seed Support", batch.Text);
        Assert.Equal([_encoder, _reviewer], batch.Recipients);
    }

    [Fact]
    public async Task ARefusedComment_NotifiesNobody()
    {
        int id = await CreateAsync();

        await _sut.AddCommentAsync(id, "   ", null, _encoder);
        await _sut.AddCommentAsync(id, "Hi", null, _stranger);   // out of scope: 404

        Assert.Empty(_notifications.Calls);
    }

    [Fact]
    public async Task WhenResolvingRecipientsFails_TheCommentIsStillPostedAndResolved()
    {
        Mock<INotificationAudience> broken = new();
        broken.Setup(a => a.ProposalReadersAsync(It.IsAny<AipOffice>(), It.IsAny<AipProgram>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("user query failed"));
        broken.Setup(a => a.ProposalOfficeReadersAsync(It.IsAny<AipOffice>(), It.IsAny<AipProgram>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("user query failed"));
        _notifications.ForwardTo = new NotificationService(
            new Mock<INotificationRepository>().Object, broken.Object, NullLogger<NotificationService>.Instance);
        int id = await CreateAsync();

        ServiceResult<ProposalCommentDto> posted = await _sut.AddCommentAsync(id, "Fix the objective", null, _encoder);
        ServiceResult<ProposalCommentDto> replied = await _sut.AddCommentAsync(id, "Which part?", posted.Value!.Id, _reviewer);
        ServiceResult<ProposalCommentDto> resolved = await _sut.ResolveCommentAsync(id, posted.Value.Id, _encoder);

        Assert.True(posted.IsSuccess, posted.Error);
        Assert.True(replied.IsSuccess, replied.Error);
        Assert.True(resolved.IsSuccess, resolved.Error);
        Assert.Equal(2, _commentStore.Rows.Count);
        Assert.NotNull(_commentStore.Rows[0].ResolvedAt);
    }
}
