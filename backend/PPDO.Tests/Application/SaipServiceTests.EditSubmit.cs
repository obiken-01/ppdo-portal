using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Common;
using PPDO.Domain.Entities;
using PPDO.Tests.TestHelpers;

namespace PPDO.Tests.Application;

/// <summary>
/// SAIP T2 (v1.10.0 — PPDO-247): the request letter, the send to PPDO and the live changes.
///
/// <para>
/// ⚠️ <b>Red-tested.</b> Each gate of <see cref="SaipService.SubmitAsync"/> was removed in turn and its
/// test watched fail: the head check, the letter, the proposal, the division, the work, the ceiling
/// and the already-sent check. Likewise the letter's head check, its lock and its type sniff, and
/// the changes read's open-only check.
/// </para>
/// </summary>
public sealed partial class SaipServiceTests
{
    private static readonly byte[] PdfBytes = "%PDF-1.7\n%test letter\n"u8.ToArray();
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];

    private AipSaip OpenSaip(string status = SaipStatus.DepartmentReview, string? letterKey = "saip-letters/77/old.pdf")
    {
        AipSaip saip = Saip(status);
        saip.LetterFileKey = letterKey;
        saip.LetterFileName = letterKey is null ? null : "old.pdf";
        _saipRepo.Setup(r => r.GetByIntIdAsync(saip.Id, It.IsAny<CancellationToken>())).ReturnsAsync(saip);
        return saip;
    }

    // ── Letter: upload ────────────────────────────────────────────────────────

    [Fact]
    public async Task PutLetter_HeadUploadsAPdf_StoresItUnderSaipLetters_AndRecordsIt()
    {
        AipSaip saip = OpenSaip(SaipStatus.Draft, letterKey: null);
        string? storedKey = null;
        _files.Setup(f => f.SaveAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback((string key, Stream _, CancellationToken _) => storedKey = key).Returns(Task.CompletedTask);

        ServiceResult<SaipDto> result = await Sut().PutLetterAsync(Record, Pto, saip.Id, "Request letter.pdf", PdfBytes, Head());

        Assert.True(result.IsSuccess, result.Error);
        Assert.StartsWith("saip-letters/77/", storedKey);
        Assert.EndsWith(".pdf", storedKey);
        Assert.Equal(storedKey, saip.LetterFileKey);
        Assert.Equal("Request letter.pdf", saip.LetterFileName);
        Assert.Equal("application/pdf", saip.LetterContentType);
        Assert.Equal(PdfBytes.Length, saip.LetterSizeBytes);
        Assert.Equal("Request letter.pdf", result.Value!.LetterFileName);
        _audit.Verify(a => a.LogAsync("aip_saips", saip.Id, AuditAction.SaipLetter,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PutLetter_Replacing_DeletesTheOldFileAfterTheNewOneIsSaved()
    {
        AipSaip saip = OpenSaip(SaipStatus.ReturnedByPpdo);

        ServiceResult<SaipDto> result = await Sut().PutLetterAsync(Record, Pto, saip.Id, "scan.png", PngBytes, Head());

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("image/png", saip.LetterContentType);
        _files.Verify(f => f.DeleteAsync("saip-letters/77/old.pdf", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(SaipStatus.SubmittedToPpdo)]
    [InlineData(SaipStatus.Approved)]
    [InlineData(SaipStatus.Cancelled)]
    public async Task PutLetter_OnceSentOrClosed_IsLocked409_AndStoresNothing(string status)
    {
        AipSaip saip = OpenSaip(status);

        ServiceResult<SaipDto> result = await Sut().PutLetterAsync(Record, Pto, saip.Id, "x.pdf", PdfBytes, Head());

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
        Assert.Equal("saip-letters/77/old.pdf", saip.LetterFileKey);
        _files.Verify(f => f.SaveAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PutLetter_Encoder_Is403()
    {
        AipSaip saip = OpenSaip(SaipStatus.Draft);

        ServiceResult<SaipDto> result = await Sut().PutLetterAsync(Record, Pto, saip.Id, "x.pdf", PdfBytes, Encoder());

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }

    [Fact]
    public async Task PutLetter_AnotherOfficesUser_Is404()
    {
        AipSaip saip = OpenSaip(SaipStatus.Draft);

        ServiceResult<SaipDto> result = await Sut().PutLetterAsync(Record, Pto, saip.Id, "x.pdf", PdfBytes, Head(Gso));

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Theory]
    [InlineData("letter.pdf", "not a pdf at all", SaipLetterRules.TypeMessage)]   // name says PDF, bytes do not
    [InlineData("letter.docx", "%PDF-1.4", SaipLetterRules.TypeMessage)]          // bytes are PDF, name is not
    [InlineData("letter.pdf", "", SaipLetterRules.EmptyMessage)]
    public async Task PutLetter_BadFile_Is400(string name, string body, string message)
    {
        AipSaip saip = OpenSaip(SaipStatus.Draft);

        ServiceResult<SaipDto> result = await Sut().PutLetterAsync(
            Record, Pto, saip.Id, name, System.Text.Encoding.ASCII.GetBytes(body), Head());

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Equal(message, result.Error);
    }

    [Fact]
    public async Task PutLetter_OverTenMegabytes_Is400()
    {
        AipSaip saip = OpenSaip(SaipStatus.Draft);
        byte[] big = new byte[SaipLetterRules.MaxBytes + 1];
        PdfBytes.CopyTo(big, 0);

        ServiceResult<SaipDto> result = await Sut().PutLetterAsync(Record, Pto, saip.Id, "big.pdf", big, Head());

        Assert.Equal(SaipLetterRules.TooLargeMessage, result.Error);
    }

    // ── Letter: download ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetLetter_PpdoReviewer_GetsTheBytes()
    {
        AipSaip saip = OpenSaip();
        saip.LetterContentType = "application/pdf";
        _files.Setup(f => f.OpenReadAsync("saip-letters/77/old.pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(PdfBytes));

        ServiceResult<SaipLetterFile> result = await Sut().GetLetterAsync(Record, Pto, saip.Id, PpdoReviewer());

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(PdfBytes, result.Value!.Content);
        Assert.Equal("old.pdf", result.Value.FileName);
    }

    [Fact]
    public async Task GetLetter_NoneAttached_Is404()
    {
        AipSaip saip = OpenSaip(letterKey: null);

        Assert.Equal(ServiceErrorCode.NotFound, (await Sut().GetLetterAsync(Record, Pto, saip.Id, Encoder())).Code);
    }

    [Fact]
    public async Task GetLetter_AnotherOfficesUser_Is404_AndNeverTouchesStorage()
    {
        AipSaip saip = OpenSaip();

        Assert.Equal(ServiceErrorCode.NotFound, (await Sut().GetLetterAsync(Record, Pto, saip.Id, Encoder(Gso))).Code);
        _files.Verify(f => f.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Send to PPDO ──────────────────────────────────────────────────────────

    private readonly List<AipOffice> _copyGroups =
    [
        new() { Id = 110, AipRecordId = Record, OfficeId = Pto, RefCode = "1000-000-1-01-020", Name = "PTO",
                Sector = "GENERAL", WorkflowStatus = AipWorkflowStatus.DepartmentReview, SaipId = 77, SourceId = 10 },
    ];

    /// <summary>A checklist that passes, which each test then breaks in one place.</summary>
    private SaipSendChecklist Checklist(
        IReadOnlyList<string>? divisions = null, IReadOnlyList<string>? work = null, string? ceiling = null)
    {
        SaipSendChecklist c = new(
            new AipReadinessDto(Record, Pto, _copyGroups[0].WorkflowStatus, true, 1, [], null, ceiling, true, false, []),
            _copyGroups, divisions ?? [], work ?? [], ceiling);
        _submit.Setup(s => s.GetSaipChecklistAsync(Record, Pto, 77, It.IsAny<CancellationToken>())).ReturnsAsync(c);
        return c;
    }

    /// <summary>One project added in the SAIP (no source), with the given proposal status (null: none).</summary>
    private void NewProject(string? proposalStatus)
    {
        _aipRepo.Setup(r => r.GetProjectsByProgramIdsNoTrackingAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new AipProject { Id = 900, ProgramId = 1100, RefCode = "1000-000-1-01-020-001-002", Name = "Revenue mapping" }]);
        _proposals.Setup(p => p.GetByProjectIdAsync(900, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposalStatus is null ? null : new InvestmentProposal { Id = 5, AipProjectId = 900, Status = proposalStatus });
    }

    [Fact]
    public async Task Submit_EveryGatePasses_SendsIt_LocksTheCopy_AndTellsPpdo()
    {
        AipSaip saip = OpenSaip();
        Checklist();
        NewProject(InvestmentProposalStatus.Final);
        User head = Head();

        ServiceResult<SaipDto> result = await Sut().SubmitAsync(Record, Pto, saip.Id, head);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(SaipStatus.SubmittedToPpdo, saip.Status);
        Assert.Equal(head.Id, saip.SubmittedById);
        Assert.NotNull(saip.SubmittedAt);
        Assert.All(_copyGroups, g => Assert.Equal(AipWorkflowStatus.SubmittedToPpdo, g.WorkflowStatus));
        Assert.Equal(AipWorkflowStatus.Consolidated, _groups[0].WorkflowStatus);   // approved untouched

        RecordingNotificationService.Call sent = _notifications.Single(NotificationTypes.SaipSentToPpdo);
        Assert.Equal(head, sent.Actor);
        _saipRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _audit.Verify(a => a.LogAsync("aip_saips", saip.Id, AuditAction.SaipSubmit,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Submit_NoLetter_Is409_WithTheSpecMessage()
    {
        AipSaip saip = OpenSaip(letterKey: null);
        Checklist();

        ServiceResult<SaipDto> result = await Sut().SubmitAsync(Record, Pto, saip.Id, Head());

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
        Assert.Contains(SaipService.LetterMissingMessage, result.Error);
        AssertNotSent(saip);
    }

    [Theory]
    [InlineData(InvestmentProposalStatus.Draft)]
    [InlineData(null)]
    public async Task Submit_NewProjectWithoutAFinalizedProposal_Is409_NamingTheProject(string? status)
    {
        AipSaip saip = OpenSaip();
        Checklist();
        NewProject(status);

        ServiceResult<SaipDto> result = await Sut().SubmitAsync(Record, Pto, saip.Id, Head());

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
        Assert.Contains("Revenue mapping", result.Error);
        AssertNotSent(saip);
    }

    [Fact]
    public async Task Submit_ADivisionStillOut_Is409()
    {
        AipSaip saip = OpenSaip();
        Checklist(divisions: ["Assessment Division has not submitted yet."]);

        ServiceResult<SaipDto> result = await Sut().SubmitAsync(Record, Pto, saip.Id, Head());

        Assert.Contains("Assessment Division has not submitted yet.", result.Error);
        AssertNotSent(saip);
    }

    [Fact]
    public async Task Submit_OverAnEnforcedCeilingOnTheAmendedTotals_Is409()
    {
        AipSaip saip = OpenSaip();
        Checklist(ceiling: "General Fund MOOE + CO totals ₱2,000.00, which is ₱500.00 over the ceiling.");

        ServiceResult<SaipDto> result = await Sut().SubmitAsync(Record, Pto, saip.Id, Head());

        Assert.Contains("over the ceiling", result.Error);
        AssertNotSent(saip);
    }

    [Fact]
    public async Task Submit_IncompleteWork_Is409()
    {
        AipSaip saip = OpenSaip();
        Checklist(work: ["'Revenue mapping' has not been costed yet. Add at least one expenditure line."]);

        ServiceResult<SaipDto> result = await Sut().SubmitAsync(Record, Pto, saip.Id, Head());

        Assert.Contains("has not been costed yet", result.Error);
        AssertNotSent(saip);
    }

    [Fact]
    public async Task Submit_SeveralGatesFail_NamesEveryOneInOneAnswer()
    {
        AipSaip saip = OpenSaip(letterKey: null);
        Checklist(ceiling: "over the ceiling");
        NewProject(InvestmentProposalStatus.Draft);

        ServiceResult<SaipDto> result = await Sut().SubmitAsync(Record, Pto, saip.Id, Head());

        Assert.Contains(SaipService.LetterMissingMessage, result.Error);
        Assert.Contains("Revenue mapping", result.Error);
        Assert.Contains("over the ceiling", result.Error);
    }

    [Fact]
    public async Task Submit_CopyStillInDraft_WithNoDivisions_SaysItWasNotSubmittedForReview()
    {
        AipSaip saip = OpenSaip(SaipStatus.Draft);
        _copyGroups[0].WorkflowStatus = AipWorkflowStatus.Draft;
        Checklist();

        ServiceResult<SaipDto> result = await Sut().SubmitAsync(Record, Pto, saip.Id, Head());

        Assert.Contains("has not been submitted for department review yet", result.Error);
        AssertNotSent(saip);
    }

    [Fact]
    public async Task Submit_AlreadyWithPpdo_Is409()
    {
        AipSaip saip = OpenSaip(SaipStatus.SubmittedToPpdo);

        ServiceResult<SaipDto> result = await Sut().SubmitAsync(Record, Pto, saip.Id, Head());

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
        _submit.Verify(s => s.GetSaipChecklistAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Submit_Encoder_Is403()
    {
        AipSaip saip = OpenSaip();
        Checklist();

        Assert.Equal(ServiceErrorCode.Forbidden, (await Sut().SubmitAsync(Record, Pto, saip.Id, Encoder())).Code);
        AssertNotSent(saip);
    }

    [Fact]
    public async Task Submit_HostAdmin_OnAnotherOffice_Is403()
    {
        // Reads every office, but is not PTO's department head.
        AipSaip saip = OpenSaip();
        Checklist();

        Assert.Equal(ServiceErrorCode.Forbidden, (await Sut().SubmitAsync(Record, Pto, saip.Id, HostAdmin())).Code);
    }

    private void AssertNotSent(AipSaip saip)
    {
        Assert.NotEqual(SaipStatus.SubmittedToPpdo, saip.Status);
        Assert.All(_copyGroups, g => Assert.NotEqual(AipWorkflowStatus.SubmittedToPpdo, g.WorkflowStatus));
        _saipRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(_notifications.Calls);
    }

    // ── Changes so far ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetChanges_OpenSaip_DiffsTheCopyAgainstTheApprovedTree_WithTheCeilingAfter()
    {
        AipSaip saip = OpenSaip();
        _saipRepo.Setup(r => r.GetDraftOfficesAsync(saip.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_copyGroups);
        // Programs come back per group id (fixture); the copy's program is new — no source — so the
        // diff reports it Added, and the approved PTO programs (100, 110) as Removed.
        AipCeilingStatusDto ceiling = new(1, true, 1_000_000m, 300_000m, 700_000m, true, []);
        _ceiling.Setup(c => c.GetStatusAsync(110, It.IsAny<CancellationToken>())).ReturnsAsync(ceiling);
        _divRepo.Setup(d => d.GetDivisionsByOfficeIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        ServiceResult<SaipChangesDto> result = await Sut().GetChangesAsync(Record, Pto, saip.Id, Encoder());

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(result.Value!.IsLive);
        Assert.Same(ceiling, result.Value.CeilingAfter);
        Assert.Contains(result.Value.Changes, c => c.NodeType == SaipNodeType.Program && c.Change == SaipChangeKind.Added);
        // The approved side is PTO's two groups, never GSO's.
        _aipRepo.Verify(r => r.GetProgramsByOfficeIdsNoTrackingAsync(
            It.Is<IReadOnlyList<int>>(ids => ids.SequenceEqual(new[] { 10, 11 })), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(SaipStatus.Approved)]
    [InlineData(SaipStatus.Cancelled)]
    public async Task GetChanges_ClosedSaip_Is409_NotALiveDiff(string status)
    {
        AipSaip saip = OpenSaip(status);

        ServiceResult<SaipChangesDto> result = await Sut().GetChangesAsync(Record, Pto, saip.Id, Encoder());

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
        Assert.Contains("only while an SAIP is open", result.Error);
        _saipRepo.Verify(r => r.GetDraftOfficesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetChanges_AnotherOfficesUser_Is404()
    {
        AipSaip saip = OpenSaip();

        Assert.Equal(ServiceErrorCode.NotFound, (await Sut().GetChangesAsync(Record, Pto, saip.Id, Encoder(Gso))).Code);
    }
}
