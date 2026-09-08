using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;

namespace PPDO.Tests.Application;

/// <summary>
/// The second write gate: an office that has gone to PPDO is no longer editable by anyone in it
/// (V18-42 / PPDO-52, boundary moved by V18-52 / PPDO-70).
///
/// <para>
/// ↩️ <b>The boundary moved on 2026-09-08.</b> Phase 3 closed the office at the encoder's first
/// submit; the workflow says it closes at <see cref="AipWorkflowStatus.SubmittedToPpdo"/>
/// (<c>AIP_Review_Spec.md</c> decision 4). During department review the encoder and the department
/// head <b>both</b> still edit — what the encoder loses at the first submit is the ability to move
/// the work on, not the ability to work on it.
/// </para>
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
    /// ⚠️ The two states in which the office has handed its work upward. Once at PPDO nobody in
    /// the office edits — including the department head, who could a moment earlier.
    /// </summary>
    [Theory]
    [InlineData(AipWorkflowStatus.SubmittedToPpdo)]
    [InlineData(AipWorkflowStatus.Consolidated)]
    public async Task UpdateActivity_OnceTheOfficeHasGoneToPpdo_IsRefused(string status)
    {
        AipService sut = BuildSut(OfficeInState(status));

        ServiceResult<AipActivityDto> result = await sut.UpdateActivityAsync(
            AipRecordId, 40, UpdateActivity(), WriteHostCaller(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    /// <summary>
    /// ⚠️ <b>The test this ticket exists for</b> (PPDO-70). Against Phase 3's <c>Draft</c>-only
    /// rule both of these are refused — which is the behaviour the workflow says is wrong. The
    /// department head's remit is to fix minor details during review; freezing the encoder out
    /// makes the department head retype every one of them personally.
    /// </summary>
    [Theory]
    [InlineData(AipWorkflowStatus.DepartmentReview)]
    [InlineData(AipWorkflowStatus.ReturnedByPpdo)]
    public async Task UpdateActivity_WhileTheOfficeStillHoldsIt_IsAllowed(string status)
    {
        AipService sut = BuildSut(OfficeInState(status));

        ServiceResult<AipActivityDto> result = await sut.UpdateActivityAsync(
            AipRecordId, 40, UpdateActivity(), WriteHostCaller(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// Pins the rule itself, independently of any endpoint.
    ///
    /// ⚠️ Written as the exact expected set rather than a count, so a sixth state added later is
    /// refused by default: it fails here and asks for a decision instead of silently inheriting
    /// whichever side of the boundary the implementation happened to put it on. A new state is far
    /// more likely to be review-ish than draft-ish, and closed is the safe default.
    /// </summary>
    [Fact]
    public void OnlyTheOfficeHeldStatesAreEditable()
    {
        Assert.Equal(
            [AipWorkflowStatus.Draft, AipWorkflowStatus.DepartmentReview,
             AipWorkflowStatus.ReturnedByPpdo],
            AipWorkflowStatus.All.Where(AipWorkflowStatus.IsOfficeEditable).ToArray());
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
        AipService sut = BuildSut(OfficeInState(AipWorkflowStatus.SubmittedToPpdo));

        ServiceResult<AipActivityDto> result = await sut.UpdateActivityAsync(
            AipRecordId, 40, UpdateActivity(), WriteHostCaller(), CancellationToken.None);

        Assert.Contains("review by PPDO", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sent on", result.Error, StringComparison.OrdinalIgnoreCase);
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
        // ⚠️ SubmittedToPpdo, not DepartmentReview — since PPDO-70 the latter is editable, so it
        // would no longer demonstrate anything about the two gates being independent.
        AipService sut = BuildSut(OfficeInState(AipWorkflowStatus.SubmittedToPpdo));

        ServiceResult<AipActivityDto> result = await sut.UpdateActivityAsync(
            AipRecordId, 40, UpdateActivity(), WriteHostCaller(), CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    // UpdateActivity() is defined in AipWriteScopeTests.cs - same partial class, one helper.
}
