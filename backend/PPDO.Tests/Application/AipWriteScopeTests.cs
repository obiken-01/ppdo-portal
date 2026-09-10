using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Application.Services;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Office ownership on the AIP <b>write</b> paths.
///
/// <para>
/// V18-39 (PPDO-38) closed this on the reads. The writes were left with no ownership check at all:
/// <c>UpdateOfficeAsync</c> and its siblings verified that the node existed and that its record was
/// Draft, and nothing else — so any caller with Budget Planning access could edit or delete another
/// office's AIP node by supplying its id.
/// </para>
///
/// <para>
/// ⚠️ <b>The refusal is <see cref="ServiceErrorCode.NotFound"/>, with the same message a genuinely
/// missing node produces — deliberately, and it is the decision worth arguing with.</b> Three
/// options were on the table:
/// </para>
/// <list type="number">
///   <item><description>
///     <b>Clamp</b>, as the read paths do — meaningless here. A read clamps by narrowing a result
///     set; a write names one node, and "clamping" it would mean redirecting the update to a
///     different node. Silently writing to the wrong row is worse than any refusal.
///   </description></item>
///   <item><description>
///     <b>403 Forbidden</b> — honest about what happened, but it confirms the node exists and
///     belongs to someone else. That is precisely what the read side clamps to avoid, so a write
///     that answers 403 hands back the existence check the reads deny.
///   </description></item>
///   <item><description>
///     <b>404, indistinguishable from missing</b> — chosen. A caller learns nothing about another
///     office's data, and the two cases are genuinely equivalent from where they sit: a node they
///     may not touch and a node that does not exist are the same node to them.
///   </description></item>
/// </list>
///
/// <para>
/// The cost is a confusing error for a PPDO admin who mistypes an id, which is a support question
/// rather than a data disclosure.
/// </para>
/// </summary>
public sealed partial class AipServiceTests
{
    private const int HostOfficeId  = 1;   // PPDO
    private const int GuestOfficeId = 2;   // e.g. GSO
    private const int AipRecordId   = 100;

    private static Office ConfigOffice(int id, bool isHost) => new()
    {
        Id = id, OfficeCode = $"O{id}", OfficeName = $"Office {id}", IsHostOffice = isHost,
        IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static User CallerFor(int officeId, bool isHost) => new()
    {
        Id = Guid.NewGuid(), Username = "u", PasswordHash = "h", FullName = "U",
        Role = UserRole.Staff, OfficeId = officeId, DivisionId = null,
        Office = ConfigOffice(officeId, isHost),
        IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static User WriteGuestCaller() => CallerFor(GuestOfficeId, isHost: false);
    private static User WriteHostCaller()  => CallerFor(HostOfficeId, isHost: true);

    /// <summary>An AIP hierarchy owned end-to-end by the HOST office — the thing a guest must not touch.</summary>
    private static (List<AipRecord> recs, List<AipOffice> offices, List<AipProgram> programs,
                    List<AipProject> projects, List<AipActivity> acts) HostOwnedTree()
    {
        List<AipRecord> recs =
        [
            new() { Id = AipRecordId, FiscalYear = 2027, EntrySource = "Manual", Status = "Draft",
                    UploadedById = Guid.NewGuid(), UploadedAt = DateTime.UtcNow },
        ];
        List<AipOffice> offices =
        [
            new() { Id = 10, AipRecordId = AipRecordId, RefCode = "1000-000-1-01-010",
                    Name = "PPDO", Sector = "GENERAL", OfficeId = HostOfficeId },
        ];
        List<AipProgram> programs =
        [
            new() { Id = 20, OfficeId = 10, RefCode = "1000-000-1-01-010-001", Name = "Program" },
        ];
        List<AipProject> projects =
        [
            new() { Id = 30, ProgramId = 20, RefCode = "1000-000-1-01-010-001-001", Name = "Project" },
        ];
        List<AipActivity> acts =
        [
            new() { Id = 40, ProjectId = 30, RefCode = "1000-000-1-01-010-001-001-001", Name = "Activity" },
        ];
        return (recs, offices, programs, projects, acts);
    }

    private static AipService BuildSut(List<AipOffice>? officesOverride = null)
    {
        var (recs, offices, programs, projects, acts) = HostOwnedTree();
        var built = Build(recs, [], officeSeed: officesOverride ?? offices,
            programSeed: programs, projectSeed: projects, actSeed: acts,
            officeConfigSeed: [ConfigOffice(HostOfficeId, true), ConfigOffice(GuestOfficeId, false)]);
        return built.Item1;
    }

    // ── AipOffice level ───────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateOffice_GuestOfficeTargetingAnotherOfficesNode_IsRefused()
    {
        AipService sut = BuildSut();

        ServiceResult<AipOfficeDto> result = await sut.UpdateOfficeAsync(
            10, new UpdateAipOfficeDto("Renamed"), WriteGuestCaller(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task UpdateOffice_RefusalIsIndistinguishableFromAMissingNode()
    {
        // ⚠️ The privacy property, asserted rather than assumed. Both answers are the same sentence
        // with the caller's own id substituted, so nothing in the response separates "you may not
        // touch node 10" from "there is no node 10". If these ever diverge, the endpoint becomes an
        // existence oracle for other offices' AIP ids.
        AipService sut = BuildSut();
        UpdateAipOfficeDto dto = new("Renamed");

        ServiceResult<AipOfficeDto> foreignNode =
            await sut.UpdateOfficeAsync(10, dto, WriteGuestCaller(), CancellationToken.None);
        ServiceResult<AipOfficeDto> missingNode =
            await sut.UpdateOfficeAsync(9999, dto, WriteGuestCaller(), CancellationToken.None);

        Assert.Equal(missingNode.Code, foreignNode.Code);
        Assert.Equal("AIP office 10 not found.",   foreignNode.Error);
        Assert.Equal("AIP office 9999 not found.", missingNode.Error);
    }

    [Fact]
    public async Task UpdateOffice_HostOfficeCaller_IsAllowed()
    {
        AipService sut = BuildSut();

        ServiceResult<AipOfficeDto> result = await sut.UpdateOfficeAsync(
            10, new UpdateAipOfficeDto("Renamed"), WriteHostCaller(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task DeleteOffice_GuestOfficeTargetingAnotherOfficesNode_IsRefused()
    {
        AipService sut = BuildSut();

        ServiceResult<bool> result =
            await sut.DeleteOfficeAsync(10, WriteGuestCaller(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    // ── Deeper levels: the owner is resolved by walking up ────────────────────

    [Fact]
    public async Task UpdateProgram_GuestOfficeTargetingAnotherOfficesProgram_IsRefused()
    {
        AipService sut = BuildSut();

        ServiceResult<AipProgramDto> result = await sut.UpdateProgramAsync(
            20, new UpdateAipProgramDto("Renamed", null), WriteGuestCaller(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task UpdateActivity_GuestOfficeTargetingAnotherOfficesActivity_IsRefused()
    {
        // The deepest walk: activity → project → program → AipOffice → owning config office. Each
        // hop is a chance to lose the thread, which is why the leaf is tested and not just the root.
        AipService sut = BuildSut();

        ServiceResult<AipActivityDto> result = await sut.UpdateActivityAsync(
            AipRecordId, 40, UpdateActivity(), WriteGuestCaller(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task UpdateActivity_HostOfficeCaller_IsAllowed()
    {
        AipService sut = BuildSut();

        ServiceResult<AipActivityDto> result = await sut.UpdateActivityAsync(
            AipRecordId, 40, UpdateActivity(), WriteHostCaller(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    // ── The unowned row ───────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateOffice_UnownedAipOffice_IsWritableOnlyByTheHostOffice()
    {
        // A row V18-32's backfill could not match belongs to nobody. It must not fall to whoever
        // asks — the same rule the read path applies, and for the same reason.
        var (_, offices, _, _, _) = HostOwnedTree();
        offices[0].OfficeId = null;
        AipService sut = BuildSut(offices);

        UpdateAipOfficeDto dto = new("Renamed");

        Assert.False((await sut.UpdateOfficeAsync(10, dto, WriteGuestCaller(), CancellationToken.None)).IsSuccess);
        Assert.True((await sut.UpdateOfficeAsync(10, dto, WriteHostCaller(), CancellationToken.None)).IsSuccess);
    }

    private static UpdateAipActivityDto UpdateActivity() =>
        new("Renamed", null, null, null, null, null, null, null, null, null, null, null, null);
}
