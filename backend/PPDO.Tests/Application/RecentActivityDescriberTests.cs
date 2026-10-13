using Moq;
using PPDO.Application.Common;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// PPDO-181 / B4 — the Recent activity band reads sentences, not table names. These pin what each
/// audit row says and, as important, how the labels behind it are fetched: one query per table
/// touched by the page of entries, never one per row.
/// </summary>
public sealed class RecentActivityDescriberTests
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    private static AuditLog Row(
        long id, string table, string action, int? recordId = null,
        string? oldValues = null, string? newValues = null) => new()
    {
        Id = id, TableName = table, Action = action, RecordId = recordId,
        OldValues = oldValues, NewValues = newValues,
        ChangedById = Guid.NewGuid(), ChangedAt = DateTime.UtcNow,
    };

    private sealed class Labels
    {
        public Dictionary<int, CeilingLabel>     Ceilings    { get; } = [];
        public Dictionary<int, AipOfficeLabel>   AipOffices  { get; } = [];
        public Dictionary<int, AipProgramLabel>  Programs    { get; } = [];
        public Dictionary<int, AipActivityLabel> Activities  { get; } = [];
        public Dictionary<int, string>           OfficeCodes { get; } = [];
        public Dictionary<int, string>           Divisions   { get; } = [];
        public Dictionary<int, string>           Funds       { get; } = [];
        /// <summary>PPDO-110 — aip_projects by id (the only kind the describer asks for).</summary>
        public Dictionary<int, AipRecordLabel>   Projects    { get; } = [];

        public Mock<IActivityLabelRepository> Repo { get; } = new();

        public RecentActivityDescriber Build()
        {
            Repo.Setup(r => r.GetCeilingLabelsAsync(It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyCollection<int> ids, CancellationToken _) =>
                    (IReadOnlyDictionary<int, CeilingLabel>)Ceilings.Where(kv => ids.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));
            Repo.Setup(r => r.GetAipOfficeLabelsAsync(It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyCollection<int> ids, CancellationToken _) =>
                    (IReadOnlyDictionary<int, AipOfficeLabel>)AipOffices.Where(kv => ids.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));
            Repo.Setup(r => r.GetAipProgramLabelsAsync(It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyCollection<int> ids, CancellationToken _) =>
                    (IReadOnlyDictionary<int, AipProgramLabel>)Programs.Where(kv => ids.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));
            Repo.Setup(r => r.GetAipActivityLabelsAsync(It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyCollection<int> ids, CancellationToken _) =>
                    (IReadOnlyDictionary<int, AipActivityLabel>)Activities.Where(kv => ids.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));
            Repo.Setup(r => r.GetOfficeCodesAsync(It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyCollection<int> ids, CancellationToken _) =>
                    (IReadOnlyDictionary<int, string>)OfficeCodes.Where(kv => ids.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));
            Repo.Setup(r => r.GetDivisionNamesAsync(It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyCollection<int> ids, CancellationToken _) =>
                    (IReadOnlyDictionary<int, string>)Divisions.Where(kv => ids.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));
            Repo.Setup(r => r.GetFundingSourceNamesAsync(It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyCollection<int> ids, CancellationToken _) =>
                    (IReadOnlyDictionary<int, string>)Funds.Where(kv => ids.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));
            Repo.Setup(r => r.GetAipRecordLabelsAsync(AipRecordKind.Project, It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((AipRecordKind _, IReadOnlyCollection<int> ids, CancellationToken _) =>
                    (IReadOnlyDictionary<int, AipRecordLabel>)Projects.Where(kv => ids.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));
            return new RecentActivityDescriber(Repo.Object);
        }
    }

    private static async Task<string> One(Labels labels, AuditLog row)
        => Assert.Single(await labels.Build().DescribeAsync([row]));

    // ── Budget ceilings ───────────────────────────────────────────────────────

    [Fact]
    public async Task CeilingUpdate_NamesTheOfficeTheYearAndTheAmount()
    {
        Labels labels = new();
        labels.Ceilings[13] = new CeilingLabel("PTO", 2028);

        string text = await One(labels, Row(1, "budget_ceilings", "UPDATE", 13, "{\"amount\":500.00}", "{\"amount\":10000000}"));

        Assert.Equal("updated PTO's FY 2028 ceiling to ₱10,000,000.00.", text);
    }

    [Fact]
    public async Task CeilingCreate_SaysSet()
    {
        Labels labels = new();
        labels.Ceilings[14] = new CeilingLabel("OPA", 2028);

        string text = await One(labels, Row(1, "budget_ceilings", "CREATE", 14, null, "{\"amount\":50000000}"));

        Assert.Equal("set OPA's FY 2028 ceiling to ₱50,000,000.00.", text);
    }

    [Fact]
    public async Task CeilingWhoseRecordIsGone_FallsBackToAGenericSentence()
    {
        // The row was deleted after the audit entry was written: nothing to name, but still a sentence.
        string text = await One(new Labels(), Row(1, "budget_ceilings", "UPDATE", 999, null, "{\"amount\":1}"));

        Assert.Equal("updated a budget ceiling.", text);
    }

    // ── Division allocations ──────────────────────────────────────────────────

    [Fact]
    public async Task DivisionAllocation_NamesTheAmountTheFundAndTheDivision()
    {
        Labels labels = new();
        labels.Divisions[14] = "Cash Division";
        labels.Funds[1] = "General Fund";

        string text = await One(labels, Row(1, "division_allocations", "UPDATE", 38,
            "{\"amount\":1000000.00}",
            "{\"divisionId\":14,\"fiscalYear\":2028,\"fundingSourceId\":1,\"amount\":5000000}"));

        Assert.Equal("allocated ₱5,000,000.00 of the General Fund to Cash Division.", text);
    }

    [Fact]
    public async Task DivisionAllocationWithAnOldStyleSnapshot_FallsBackToAGenericSentence()
    {
        // Early rows only carried the amount, so there is no division to name.
        string text = await One(new Labels(), Row(1, "division_allocations", "UPDATE", 38,
            "{\"amount\":1000000.00}", "{\"amount\":2000000}"));

        Assert.Equal("updated a division allocation.", text);
    }

    // ── Activities ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ActivityRetag_NamesTheProjectTheOfficeAndBothDivisions()
    {
        Labels labels = new();
        labels.Activities[18499] = new AipActivityLabel("OPA", "Rice Project 1");
        labels.Divisions[14] = "Cash";
        labels.Divisions[13] = "Admin";

        string text = await One(labels, Row(1, "aip_activities", "RETAG_DIV", 18499,
            "{\"divisionId\":14}", "{\"divisionId\":13}"));

        Assert.Equal("moved an activity in Rice Project 1 (OPA) from Cash to Admin.", text);
    }

    [Fact]
    public async Task ActivityRetagWhoseRecordIsGone_StillReadsAsAMove()
    {
        Labels labels = new();
        labels.Divisions[14] = "Cash";
        labels.Divisions[13] = "Admin";

        string text = await One(labels, Row(1, "aip_activities", "RETAG_DIV", 18499,
            "{\"divisionId\":14}", "{\"divisionId\":13}"));

        Assert.Equal("moved an activity from Cash to Admin.", text);
    }

    [Fact]
    public async Task ActivityEditedAndAdded_NameTheProjectAndOffice()
    {
        Labels labels = new();
        labels.Activities[7] = new AipActivityLabel("OPA", "Rice Project 1");

        IReadOnlyList<string> text = await labels.Build().DescribeAsync(
        [
            Row(1, "aip_activities", "UPDATE", 7, "{}", "{}"),
            Row(2, "aip_activities", "CREATE", 7, null, "{}"),
        ]);

        Assert.Equal("edited an activity in Rice Project 1 (OPA).", text[0]);
        Assert.Equal("added an activity in Rice Project 1 (OPA).", text[1]);
    }

    [Fact]
    public async Task ActivityDelete_NamesTheActivityFromItsSnapshot_BecauseTheRowIsGone()
    {
        Labels labels = new();
        labels.AipOffices[582] = new AipOfficeLabel("OPA", 2028);

        string text = await One(labels, Row(1, "aip_activities", "DELETE", 18519,
            "{\"nodeType\":\"Activity\",\"name\":\"Buy rice seed\",\"aipRecordId\":44,\"aipOfficeId\":582}", null));

        Assert.Equal("deleted the activity \"Buy rice seed\" (OPA).", text);
    }

    [Fact]
    public async Task ActivityDeleteWithNoUsableSnapshot_FallsBackToAGenericSentence()
    {
        string text = await One(new Labels(), Row(1, "aip_activities", "DELETE", 18519, null, null));

        Assert.Equal("deleted an activity.", text);
    }

    // ── Hand-offs: submit / return / accept ───────────────────────────────────

    [Theory]
    [InlineData("SUBMIT_DH",  "submitted OPA's FY 2028 AIP for department review.")]
    [InlineData("RETURN_DH",  "returned OPA's FY 2028 AIP to its encoders.")]
    [InlineData("SUBMIT_PPD", "sent OPA's FY 2028 AIP to PPDO.")]
    [InlineData("RETURN_PPD", "returned OPA's FY 2028 AIP for changes.")]
    [InlineData("ACCEPT_PPD", "accepted OPA's FY 2028 AIP.")]
    [InlineData("REOPEN_PPD", "re-opened OPA's accepted FY 2028 AIP.")]
    public async Task OfficeHandOffs_NameTheOfficeAndTheYear(string action, string expected)
    {
        Labels labels = new();
        labels.AipOffices[559] = new AipOfficeLabel("OPA", 2028);

        string text = await One(labels, Row(1, "aip_offices", action, 559,
            "{\"workflowStatus\":\"X\"}", "{\"workflowStatus\":\"Y\",\"groupIds\":[559]}"));

        Assert.Equal(expected, text);
    }

    [Fact]
    public async Task OfficeHandOffWhoseGroupIsGone_StillReadsAsAHandOff()
    {
        string text = await One(new Labels(), Row(1, "aip_offices", "SUBMIT_PPD", 559, null, null));

        Assert.Equal("sent an office's AIP to PPDO.", text);
    }

    [Fact]
    public async Task DivisionSubmit_NamesTheDivisionAndTheOfficeFromItsSnapshot()
    {
        Labels labels = new();
        labels.OfficeCodes[3] = "PTO";
        labels.Divisions[13] = "Admin Division";

        string text = await One(labels, Row(1, "aip_division_submissions", "SUBMIT_DIV", 5,
            "{\"status\":\"Draft\"}", "{\"status\":\"Submitted\",\"aipRecordId\":44,\"officeId\":3,\"divisionId\":13}"));

        Assert.Equal("submitted the Admin Division's work to the department head (PTO).", text);
    }

    [Fact]
    public async Task DivisionReturn_NamesTheDivisionAndTheOffice()
    {
        Labels labels = new();
        labels.OfficeCodes[3] = "PTO";
        labels.Divisions[14] = "Cash Division";

        string text = await One(labels, Row(1, "aip_division_submissions", "RETURN_DIV", 4,
            "{\"status\":\"Submitted\"}", "{\"status\":\"Draft\",\"officeId\":3,\"divisionId\":14,\"cause\":\"ppdo-RETURN_PPD\"}"));

        Assert.Equal("returned the Cash Division's work to its encoders (PTO).", text);
    }

    // ── Everything else ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("wfp_records",     "UPDATE", "updated a work and financial plan.")]
    [InlineData("aip_records",     "UPDATE", "updated an AIP record.")]
    [InlineData("ldip_records",    "CREATE", "created an LDIP record.")]
    [InlineData("program_divisions", "DELETE", "removed a program's division assignment.")]
    public async Task TablesWithoutALabelQuery_GetAGenericSentence(string table, string action, string expected)
    {
        string text = await One(new Labels(), Row(1, table, action, 1, "{}", "{}"));

        Assert.Equal(expected, text);
    }

    [Fact]
    public async Task AnArchivedAipRecord_SaysArchived()
    {
        string text = await One(new Labels(), Row(1, "aip_records", "UPDATE", 43, "{\"status\":\"Draft\"}", "{\"status\":\"Archived\"}"));

        Assert.Equal("archived an AIP record.", text);
    }

    [Theory]
    [InlineData("budget_ceilings",        "UPDATE")]
    [InlineData("division_allocations",   "CREATE")]
    [InlineData("aip_activities",         "RETAG_DIV")]
    [InlineData("aip_activities",         "DELETE")]
    [InlineData("aip_programs",           "UPDATE")]
    [InlineData("aip_offices",            "ACCEPT_PPD")]
    [InlineData("aip_offices",            "UPDATE")]
    [InlineData("aip_division_submissions", "SUBMIT_DIV")]
    [InlineData("program_divisions",      "CREATE")]
    [InlineData("wfp_expenditures",       "DELETE")]
    [InlineData("a_table_nobody_mapped",  "SOMETHING_NEW")]
    public async Task NoSentenceEverShowsATableNameARecordIdOrAnActionCode(string table, string action)
    {
        // The records are all gone, so every one of these takes its fallback path.
        string text = await One(new Labels(), Row(1, table, action, 18501, "{}", "{}"));

        Assert.DoesNotContain(table, text);
        Assert.DoesNotContain("#", text);
        Assert.DoesNotContain("_", text);
        Assert.DoesNotContain("18501", text);
        Assert.EndsWith(".", text);
    }

    [Fact]
    public async Task AMalformedSnapshot_DoesNotBreakTheBand()
    {
        string text = await One(new Labels(), Row(1, "budget_ceilings", "UPDATE", 1, "not json", "{{{"));

        Assert.Equal("updated a budget ceiling.", text);
    }

    [Fact]
    public async Task LongAndMultiLineNames_AreCollapsedAndTruncated()
    {
        Labels labels = new();
        labels.Activities[7] = new AipActivityLabel("OPA", "Creation of Plantilla Positions:\n(1) Photographer SG 7\nAKAP-Hub\n(1) Community Affairs Officer I SG 11 and a great many more words");

        string text = await One(labels, Row(1, "aip_activities", "UPDATE", 7));

        Assert.DoesNotContain("\n", text);
        Assert.Contains("…", text);
        Assert.True(text.Length < 100, text);
    }

    // ── Set-based label loading ───────────────────────────────────────────────

    [Fact]
    public async Task LabelsAreFetchedOncePerTable_NotOncePerRow()
    {
        Labels labels = new();
        for (int i = 1; i <= 4; i++) labels.Ceilings[i] = new CeilingLabel("PTO", 2028);
        for (int i = 1; i <= 3; i++) labels.Activities[i] = new AipActivityLabel("OPA", "P");
        labels.Divisions[1] = "Cash"; labels.Divisions[2] = "Admin";

        List<AuditLog> page =
        [
            Row(1, "budget_ceilings", "UPDATE", 1, null, "{\"amount\":1}"),
            Row(2, "budget_ceilings", "UPDATE", 2, null, "{\"amount\":1}"),
            Row(3, "budget_ceilings", "UPDATE", 3, null, "{\"amount\":1}"),
            Row(4, "budget_ceilings", "CREATE", 4, null, "{\"amount\":1}"),
            Row(5, "aip_activities", "RETAG_DIV", 1, "{\"divisionId\":1}", "{\"divisionId\":2}"),
            Row(6, "aip_activities", "RETAG_DIV", 2, "{\"divisionId\":2}", "{\"divisionId\":1}"),
            Row(7, "aip_activities", "UPDATE", 3, "{}", "{}"),
        ];

        IReadOnlyList<string> text = await labels.Build().DescribeAsync(page);

        Assert.Equal(7, text.Count);
        labels.Repo.Verify(r => r.GetCeilingLabelsAsync(
            It.Is<IReadOnlyCollection<int>>(ids => ids.OrderBy(x => x).SequenceEqual(new[] { 1, 2, 3, 4 })), It.IsAny<CancellationToken>()), Times.Once);
        labels.Repo.Verify(r => r.GetAipActivityLabelsAsync(
            It.Is<IReadOnlyCollection<int>>(ids => ids.OrderBy(x => x).SequenceEqual(new[] { 1, 2, 3 })), It.IsAny<CancellationToken>()), Times.Once);
        labels.Repo.Verify(r => r.GetDivisionNamesAsync(
            It.Is<IReadOnlyCollection<int>>(ids => ids.OrderBy(x => x).SequenceEqual(new[] { 1, 2 })), It.IsAny<CancellationToken>()), Times.Once);

        // A table the page never touched is never queried.
        labels.Repo.Verify(r => r.GetAipOfficeLabelsAsync(It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()), Times.Never);
        labels.Repo.Verify(r => r.GetAipProgramLabelsAsync(It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()), Times.Never);
        labels.Repo.Verify(r => r.GetFundingSourceNamesAsync(It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()), Times.Never);
        labels.Repo.Verify(r => r.GetOfficeCodesAsync(It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnEmptyPage_QueriesNothing()
    {
        Labels labels = new();

        IReadOnlyList<string> text = await labels.Build().DescribeAsync([]);

        Assert.Empty(text);
        labels.Repo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OneDescriptionPerRow_InTheOrderGiven()
    {
        Labels labels = new();
        labels.Ceilings[1] = new CeilingLabel("PTO", 2028);

        IReadOnlyList<string> text = await labels.Build().DescribeAsync(
        [
            Row(10, "wfp_records", "UPDATE", 1),
            Row(11, "budget_ceilings", "UPDATE", 1, null, "{\"amount\":5}"),
        ]);

        Assert.Equal("updated a work and financial plan.", text[0]);
        Assert.Equal("updated PTO's FY 2028 ceiling to ₱5.00.", text[1]);
    }

    // ── AIP projects (PPDO-110: projects now reach the feed) ─────────────────

    [Fact]
    public async Task ProjectCreate_NamesTheProjectAndItsProgramsOffice()
    {
        Labels labels = new();
        labels.Programs[30] = new AipProgramLabel("OPA", "Agricultural Production Program");

        string text = await One(labels, Row(1, "aip_projects", "CREATE", 40, null,
            """{"programId":30,"refCode":"8000-000-1-01-016-001-002","name":"Rice Project"}"""));

        Assert.Equal("added the project \"Rice Project\" to OPA.", text);
    }

    [Fact]
    public async Task ProjectEdit_NamesTheProjectAndItsOffice()
    {
        Labels labels = new();
        labels.Projects[40] = new AipRecordLabel("8000-000-1-01-016-001-002", "Rice Project", "OPA");

        string text = await One(labels, Row(1, "aip_projects", "UPDATE", 40,
            """{"name":"Rice"}""", """{"name":"Rice Project"}"""));

        Assert.Equal("edited the project \"Rice Project\" (OPA).", text);
    }

    [Fact]
    public async Task ProjectDelete_NamesItFromItsSnapshot()
    {
        // The row is gone; the delete snapshot (PPDO-88's DeleteNodeAsync) still carries its name
        // and its sector group.
        Labels labels = new();
        labels.AipOffices[559] = new AipOfficeLabel("OPA", 2028);

        string text = await One(labels, Row(1, "aip_projects", "DELETE", 40,
            """{"nodeType":"Project","refCode":"8000-000-1-01-016-001-002","name":"Rice Project","aipOfficeId":559}"""));

        Assert.Equal("deleted the project \"Rice Project\" (OPA).", text);
    }

    [Theory]
    [InlineData("CREATE", null, "{}", "added a project.")]
    [InlineData("UPDATE", null, null, "edited a project.")]
    [InlineData("DELETE", "{}", null, "deleted a project.")]
    public async Task Project_WithNothingToNameItBy_StillReadsAsASentence(
        string action, string? oldValues, string? newValues, string expected)
    {
        string text = await One(new Labels(), Row(1, "aip_projects", action, 40, oldValues, newValues));

        Assert.Equal(expected, text);
    }

    [Fact]
    public async Task ProjectEdits_AreLabelledInOneQuery()
    {
        Labels labels = new();
        labels.Projects[40] = new AipRecordLabel("P-1", "Rice Project", "OPA");
        labels.Projects[41] = new AipRecordLabel("P-2", "Corn Project", "OPA");

        await labels.Build().DescribeAsync(
        [
            Row(1, "aip_projects", "UPDATE", 40),
            Row(2, "aip_projects", "UPDATE", 41),
            Row(3, "aip_projects", "UPDATE", 40),
        ]);

        labels.Repo.Verify(r => r.GetAipRecordLabelsAsync(AipRecordKind.Project,
            It.Is<IReadOnlyCollection<int>>(ids => ids.OrderBy(x => x).SequenceEqual(new[] { 40, 41 })),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
