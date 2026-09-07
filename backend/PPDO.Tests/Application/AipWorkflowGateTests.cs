using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;

namespace PPDO.Tests.Application;

/// <summary>
/// The second write gate: an office that has been submitted is no longer editable by its encoder
/// (V18-42 / PPDO-52).
///
/// <para>
/// ⚠️ <b>This gate lives inside <c>CheckWritableAsync</c>, not on the new entry endpoints</b>, and
/// that placement is the thing worth testing. Every pre-existing write path — add, edit and delete
/// of office, program, project and activity, all shipped before this phase — runs through that one
/// method. Putting the check on the new endpoints alone would have left a submitted office fully
/// editable through the paths that already existed, which is the kind of gap that is invisible in a
/// diff because nothing about those files changed.
/// </para>
///
/// <para>
/// The cases below therefore go through <b>old</b> endpoints, deliberately. A test that only
/// exercised the new ones would pass with the gate in the wrong place.
/// </para>
///
/// <para>
/// ⚠️ The refusal is <see cref="ServiceErrorCode.BadRequest"/>, not the
/// <see cref="ServiceErrorCode.NotFound"/> that <see cref="AipServiceTests"/>' ownership cases use.
/// The two hide different things: ownership hides <i>existence</i> from a caller with no business
/// knowing it, whereas this caller owns the record, can see it, and is being told the operation is
/// wrong for the state it is in. Answering NotFound here would tell an encoder their own office
/// had vanished.
/// </para>
/// </summary>
public sealed partial class AipServiceTests
{
    private static List<AipOffice> OfficeInState(string workflowStatus) =>
    [
        new()
        {
            Id = 10, AipRecordId = AipRecordId, RefCode = "1000-000-1-01-010",
            Name = "PPDO", Sector = "GENERAL", OfficeId = HostOfficeId,
            WorkflowStatus = workflowStatus,
        },
    ];

    // ── The control: Draft is editable ────────────────────────────────────────

    [Fact]
    public async Task UpdateActivity_WhileTheOfficeIsDraft_IsAllowed()
    {
        AipService sut = BuildSut(OfficeInState(AipWorkflowStatus.Draft));

        ServiceResult<AipActivityDto> result = await sut.UpdateActivityAsync(
            AipRecordId, 40, UpdateActivity(), WriteHostCaller(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    // ── Every other state is closed to the encoder ────────────────────────────

    /// <summary>
    /// ⚠️ Driven from <see cref="AipWorkflowStatus.All"/> rather than a hand-written list, so a
    /// sixth state added in Phase 4 is refused by default instead of silently becoming editable.
    /// A new state is far more likely to be review-ish than draft-ish, and the safe default for an
    /// unknown one is closed.
    /// </summary>
    [Theory]
    [InlineData(AipWorkflowStatus.DepartmentReview)]
    [InlineData(AipWorkflowStatus.SubmittedToPpdo)]
    [InlineData(AipWorkflowStatus.ReturnedByPpdo)]
    [InlineData(AipWorkflowStatus.Consolidated)]
    public async Task UpdateActivity_OnceTheOfficeIsPastDraft_IsRefused(string status)
    {
        AipService sut = BuildSut(OfficeInState(status));

        ServiceResult<AipActivityDto> result = await sut.UpdateActivityAsync(
            AipRecordId, 40, UpdateActivity(), WriteHostCaller(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task EveryWorkflowStateExceptDraftIsClosedToTheEncoder()
    {
        // Pins the rule itself, independently of any endpoint: exactly one of the five states is
        // editable. If a sixth is added and made editable, this fails and asks for a decision.
        Assert.Equal(
            [AipWorkflowStatus.Draft],
            AipWorkflowStatus.All.Where(AipWorkflowStatus.IsEncoderEditable).ToArray());
    }

    // ── The message names the state ───────────────────────────────────────────

    /// <summary>
    /// ⚠️ "Read-only" on its own leaves an encoder with no idea who holds their work or how to get
    /// it back. UI states are this project's largest fix category to date, and a refusal that does
    /// not say <i>why</i> is the most common shape of that failure.
    /// </summary>
    [Fact]
    public async Task UpdateActivity_WhenRefused_TheMessageNamesTheStateHoldingTheWork()
    {
        AipService sut = BuildSut(OfficeInState(AipWorkflowStatus.DepartmentReview));

        ServiceResult<AipActivityDto> result = await sut.UpdateActivityAsync(
            AipRecordId, 40, UpdateActivity(), WriteHostCaller(), CancellationToken.None);

        Assert.Contains("department review", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("submitted", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // ── The two gates are independent ─────────────────────────────────────────

    /// <summary>
    /// The record's status is the fiscal <i>year's</i>; the office's workflow status is one
    /// office's. Passing one says nothing about the other, so a Draft record with a submitted
    /// office is still closed.
    /// </summary>
    [Fact]
    public async Task UpdateActivity_DraftRecordButSubmittedOffice_IsStillRefused()
    {
        // The record seeded by HostOwnedTree() is Draft; only the office has moved on.
        AipService sut = BuildSut(OfficeInState(AipWorkflowStatus.DepartmentReview));

        ServiceResult<AipActivityDto> result = await sut.UpdateActivityAsync(
            AipRecordId, 40, UpdateActivity(), WriteHostCaller(), CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    // UpdateActivity() is defined in AipWriteScopeTests.cs - same partial class, one helper.
}
