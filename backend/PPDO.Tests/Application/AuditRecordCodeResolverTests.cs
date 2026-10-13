using Moq;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// PPDO-110 — the Audit Log page shows an AIP row's ref code instead of <c>#id</c>. These pin the
/// order it is found in (the live row, then the audit row's own snapshot, then nothing) and that
/// it costs at most one query per ref-coded table on the page, never one per row.
/// </summary>
public sealed class AuditRecordCodeResolverTests
{
    private static AuditLog Row(
        long id, string table, string action, int? recordId,
        string? oldValues = null, string? newValues = null) => new()
    {
        Id = id, TableName = table, Action = action, RecordId = recordId,
        OldValues = oldValues, NewValues = newValues,
        ChangedById = Guid.NewGuid(), ChangedAt = DateTime.UtcNow,
    };

    private static (AuditRecordCodeResolver sut, Mock<IActivityLabelRepository> repo) Build(
        Dictionary<AipRecordKind, Dictionary<int, AipRecordLabel>>? live = null)
    {
        live ??= [];
        Mock<IActivityLabelRepository> repo = new();
        repo.Setup(r => r.GetAipRecordLabelsAsync(
                It.IsAny<AipRecordKind>(), It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AipRecordKind kind, IReadOnlyCollection<int> ids, CancellationToken _) =>
                (IReadOnlyDictionary<int, AipRecordLabel>)(live.TryGetValue(kind, out Dictionary<int, AipRecordLabel>? rows)
                    ? rows.Where(kv => ids.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value)
                    : new Dictionary<int, AipRecordLabel>()));
        return (new AuditRecordCodeResolver(repo.Object), repo);
    }

    [Fact]
    public async Task ResolveAsync_LiveRow_GivesItsRefCodeAndName()
    {
        var (sut, _) = Build(new()
        {
            [AipRecordKind.Activity] = new() { [5] = new AipRecordLabel("1000-000-1-01-010-001-001-001", "Conduct of LDC meetings", "PPDO") },
        });

        AuditRecordCode code = Assert.Single(await sut.ResolveAsync([Row(1, "aip_activities", "UPDATE", 5)]));

        Assert.Equal("1000-000-1-01-010-001-001-001", code.Code);
        Assert.Equal("Conduct of LDC meetings", code.Name);
    }

    [Fact]
    public async Task ResolveAsync_MixedPage_ResolvesOnlyTheRefCodedTables_OneQueryEach()
    {
        var (sut, repo) = Build(new()
        {
            [AipRecordKind.Program]  = new() { [7] = new AipRecordLabel("P-1", "Program", null) },
            [AipRecordKind.Activity] = new()
            {
                [5] = new AipRecordLabel("A-5", "Activity five", null),
                [6] = new AipRecordLabel("A-6", "Activity six", null),
            },
        });

        IReadOnlyList<AuditRecordCode> codes = await sut.ResolveAsync(
        [
            Row(1, "aip_activities", "UPDATE", 5),
            Row(2, "accounts", "UPDATE", 9),
            Row(3, "aip_programs", "UPDATE", 7),
            Row(4, "aip_activities", "UPDATE", 6),
            Row(5, "budget_ceilings", "UPDATE", 13),
        ]);

        Assert.Equal(["A-5", null, "P-1", "A-6", null], codes.Select(c => c.Code));
        // ⚠️ No N+1: one query per ref-coded table the page touches, none for the table it does not.
        repo.Verify(r => r.GetAipRecordLabelsAsync(AipRecordKind.Activity,
            It.Is<IReadOnlyCollection<int>>(ids => ids.OrderBy(i => i).SequenceEqual(new[] { 5, 6 })),
            It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.GetAipRecordLabelsAsync(AipRecordKind.Program,
            It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.GetAipRecordLabelsAsync(AipRecordKind.Project,
            It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_DeletedRow_FallsBackToTheRefCodeInItsSnapshot()
    {
        // The row is gone, so the lookup misses — the DELETE row's own snapshot still names it.
        var (sut, _) = Build();

        AuditRecordCode code = Assert.Single(await sut.ResolveAsync(
        [
            Row(1, "aip_activities", "DELETE", 5,
                oldValues: """{"nodeType":"Activity","refCode":"1000-000-1-01-010-001-001-002","name":"Old activity"}"""),
        ]));

        Assert.Equal("1000-000-1-01-010-001-001-002", code.Code);
        Assert.Equal("Old activity", code.Name);
    }

    [Fact]
    public async Task ResolveAsync_CreateSnapshot_IsUsedWhenTheRowIsGone()
    {
        var (sut, _) = Build();

        AuditRecordCode code = Assert.Single(await sut.ResolveAsync(
            [Row(1, "aip_projects", "CREATE", 40, newValues: """{"programId":30,"refCode":"P-1-001","name":"Rice Project"}""")]));

        Assert.Equal("P-1-001", code.Code);
    }

    [Fact]
    public async Task ResolveAsync_LiveRowWinsOverTheSnapshot()
    {
        // A renumber (PPDO-88) moves the code; the page shows what the row is called now.
        var (sut, _) = Build(new()
        {
            [AipRecordKind.Project] = new() { [40] = new AipRecordLabel("P-1-002", "Rice Project", null) },
        });

        AuditRecordCode code = Assert.Single(await sut.ResolveAsync(
            [Row(1, "aip_projects", "CREATE", 40, newValues: """{"refCode":"P-1-003","name":"Rice Project"}""")]));

        Assert.Equal("P-1-002", code.Code);
    }

    [Fact]
    public async Task ResolveAsync_NoRowAndNoSnapshotCode_GivesNoCode_SoThePageKeepsTheId()
    {
        // An update written before PPDO-110 carried no refCode. Never blank, never an error: null,
        // and the page falls back to #id exactly as before.
        var (sut, _) = Build();

        AuditRecordCode code = Assert.Single(await sut.ResolveAsync(
            [Row(1, "aip_activities", "UPDATE", 5, newValues: """{"isCreation":true}""")]));

        Assert.Null(code.Code);
    }

    [Fact]
    public async Task ResolveAsync_SnapshotThatWillNotParse_GivesNoCode()
    {
        var (sut, _) = Build();

        AuditRecordCode code = Assert.Single(await sut.ResolveAsync(
            [Row(1, "aip_activities", "DELETE", 5, oldValues: "{not json")]));

        Assert.Null(code.Code);
    }

    [Fact]
    public async Task ResolveAsync_EmptyPage_RunsNoQuery()
    {
        var (sut, repo) = Build();

        Assert.Empty(await sut.ResolveAsync([]));
        repo.Verify(r => r.GetAipRecordLabelsAsync(
            It.IsAny<AipRecordKind>(), It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
