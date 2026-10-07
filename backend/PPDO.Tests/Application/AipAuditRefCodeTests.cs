using System.Text.Json;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;

namespace PPDO.Tests.Application;

/// <summary>
/// PPDO-110 — every AIP activity update audit row carries the activity's <c>refCode</c>, on BOTH
/// sides. The new side is what lets the Audit Log name a row deleted later (its live lookup
/// misses); the old side keeps the description's old-vs-new diff from reporting "Ref Code: — →
/// …" as a change on every edit.
/// </summary>
public sealed partial class AipServiceTests
{
    private static readonly JsonSerializerOptions AuditJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>The scoped tree, with every audit write captured as camelCase JSON.</summary>
    private static (AipService sut, AipActivity activity, List<(string Table, string Action, string? Old, string? New)> audits)
        BuildCapturingAudits()
    {
        var (recs, offices, programs, projects, acts) = HostOwnedTree();
        AipActivity activity = acts.Single(a => a.Id == ConflictActivityId);
        activity.RefCode = "1000-000-1-01-010-001-001-001";

        var built = Build(recs, [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: acts,
            officeConfigSeed: [ConfigOffice(HostOfficeId, true), ConfigOffice(GuestOfficeId, false)]);

        List<(string, string, string?, string?)> audits = [];
        built.Item6.Setup(a => a.LogAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Callback((string table, int _, string action, object? old, object? @new, CancellationToken _) =>
                audits.Add((table, action,
                    old is null ? null : JsonSerializer.Serialize(old, AuditJson),
                    @new is null ? null : JsonSerializer.Serialize(@new, AuditJson))))
            .Returns(Task.CompletedTask);

        return (built.Item1, activity, audits);
    }

    private static void AssertRefCodeOnBothSides((string Table, string Action, string? Old, string? New) audit, string refCode)
    {
        Assert.Equal("aip_activities", audit.Table);
        foreach (string? side in new[] { audit.Old, audit.New })
        {
            Assert.NotNull(side);
            using JsonDocument doc = JsonDocument.Parse(side!);
            Assert.Equal(refCode, doc.RootElement.GetProperty("refCode").GetString());
        }
    }

    [Fact]
    public async Task UpdateActivity_AuditRow_CarriesTheRefCodeOnBothSides()
    {
        var (sut, activity, audits) = BuildCapturingAudits();

        ServiceResult<AipActivityDto> result = await sut.UpdateActivityAsync(
            AipRecordId, ConflictActivityId, UpdateActivity(), WriteHostCaller());

        Assert.True(result.IsSuccess);
        AssertRefCodeOnBothSides(Assert.Single(audits, a => a.Action == AuditAction.Update), activity.RefCode);
    }

    [Fact]
    public async Task UpdateActivityDetails_AuditRow_CarriesTheRefCodeOnBothSides()
    {
        var (sut, activity, audits) = BuildCapturingAudits();

        ServiceResult<AipActivityDto> result = await sut.UpdateActivityDetailsAsync(
            ConflictActivityId,
            new UpdateAipActivityDetailsDto("Renamed", null, null, null, null, null, null, null, null),
            WriteHostCaller());

        Assert.True(result.IsSuccess);
        AssertRefCodeOnBothSides(Assert.Single(audits, a => a.Action == AuditAction.Update), activity.RefCode);
    }

    [Fact]
    public async Task UpdateActivityIsCreation_AuditRow_CarriesTheRefCodeOnBothSides()
    {
        var (sut, activity, audits) = BuildCapturingAudits();

        ServiceResult<AipActivityDto> result =
            await sut.UpdateActivityIsCreationAsync(ConflictActivityId, true, WriteHostCaller());

        Assert.True(result.IsSuccess);
        AssertRefCodeOnBothSides(Assert.Single(audits, a => a.Action == AuditAction.Update), activity.RefCode);
    }

    [Fact]
    public async Task UpdateActivity_AuditDescription_DoesNotReportTheRefCodeAsAChange()
    {
        // The reason for the old side: the Audit Log's description diffs old against new.
        var (sut, _, audits) = BuildCapturingAudits();

        await sut.UpdateActivityIsCreationAsync(ConflictActivityId, true, WriteHostCaller());

        var row = Assert.Single(audits, a => a.Action == AuditAction.Update);
        string description = AuditDescriptionBuilder.Build(row.Action, row.Old, row.New);
        Assert.DoesNotContain("Ref Code", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Is Creation", description, StringComparison.OrdinalIgnoreCase);
    }
}
