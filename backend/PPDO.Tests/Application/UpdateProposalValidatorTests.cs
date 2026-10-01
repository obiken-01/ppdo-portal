using PPDO.Application.Common;
using PPDO.Application.DTOs.InvestmentProposal;
using PPDO.Application.Validators.InvestmentProposal;

namespace PPDO.Tests.Application;

/// <summary>
/// <see cref="UpdateProposalValidator"/> (PPDO-155): the §4 validation table of
/// Investment_Proposal_Spec.md, messages word for word, keyed by the client's own field paths.
/// </summary>
public sealed class UpdateProposalValidatorTests
{
    private static readonly HashSet<int> ProjectActivities = [401, 402];

    /// <summary>A valid, empty content body: the fixed rows present, everything else blank.</summary>
    private static ProposalContentDto Valid() => new(
        ProjectLocation: null, HgdgChecklist: null, HgdgScore: null,
        BeneficiariesSummary: [],
        Description: null, Rationale: null,
        Benefits: InvestmentProposalSector.All.Select(s => new ProposalBenefitDto(s, null, null)).ToList(),
        GeneralObjective: null,
        Logframe: InvestmentProposalLogframeLevel.All.Select(l => new ProposalLogframeDto(l, null, null)).ToList(),
        DirectSameAsSummary: true,
        TargetBeneficiaries: [],
        Groups: [], WorkPlan: [],
        ProjectSupervisor: null, ProjectManager: null,
        TeamMembers: [],
        PartnershipSustainability: null,
        Monitoring: [], Risks: [],
        WomensImpactStrategy: null,
        Signatories: [new(1, null, null, null), new(2, null, null, null), new(3, null, null, null), new(4, null, null, null)]);

    private static Dictionary<string, List<string>> Validate(ProposalContentDto c)
        => UpdateProposalValidator.Validate(c, ProjectActivities);

    [Fact]
    public void ValidEmptyContent_HasNoErrors() => Assert.Empty(Validate(Valid()));

    [Theory]
    [InlineData("0", true)]
    [InlineData("20", true)]
    [InlineData("15.5", true)]
    [InlineData("-1", false)]
    [InlineData("21", false)]
    [InlineData("20.1", false)]
    [InlineData("7.25", false)]   // two decimals
    public void HgdgScore_ZeroToTwentyOneDecimal(string score, bool valid)
    {
        Dictionary<string, List<string>> errors = Validate(Valid() with { HgdgScore = decimal.Parse(score) });
        if (valid) Assert.Empty(errors);
        else Assert.Equal([UpdateProposalValidator.ScoreRange], errors["hgdgScore"]);
    }

    [Fact]
    public void HgdgChecklist_UnknownCode_IsRefused()
    {
        Assert.Equal([UpdateProposalValidator.UnknownChecklist], Validate(Valid() with { HgdgChecklist = "NOPE" })["hgdgChecklist"]);
        Assert.Empty(Validate(Valid() with { HgdgChecklist = HgdgChecklists.All[0].Code }));
    }

    [Fact]
    public void TeamMember_SexMustBeMOrF_AndBlankRowsAreSkipped()
    {
        Dictionary<string, List<string>> errors = Validate(Valid() with
        {
            TeamMembers =
            [
                new(null, null, null, null, null, null),          // blank: skipped, not reported
                new(null, "Ana", "X", null, null, null),
            ],
        });

        Assert.Equal(["teamMembers[1].sex"], errors.Keys);
        Assert.Equal([UpdateProposalValidator.ChooseSex], errors["teamMembers[1].sex"]);
    }

    [Fact]
    public void Counts_MustBeZeroOrMore()
    {
        Dictionary<string, List<string>> errors = Validate(Valid() with
        {
            BeneficiariesSummary = [new(null, "Farmers", -1, 2_000_000)],
        });

        Assert.Equal([UpdateProposalValidator.WholeNumber], errors["beneficiariesSummary[0].male"]);
        Assert.Equal([UpdateProposalValidator.WholeNumber], errors["beneficiariesSummary[0].female"]);
    }

    [Fact]
    public void WorkPlan_ForeignOrRepeatedActivity_AndNamelessStep_AreRefused()
    {
        Dictionary<string, List<string>> errors = Validate(Valid() with
        {
            WorkPlan =
            [
                new(null, 999, null, null, null, null, null, null),          // not this project's
                new(null, 401, null, null, null, null, null, null),
                new(null, 401, null, null, null, null, null, null),          // twice
                new(null, null, " ", null, "Target only", null, null, null), // step with no name
                new(null, null, null, null, null, null, null, null),         // blank step: skipped
            ],
        });

        Assert.Equal([UpdateProposalValidator.ActivityNotInProject], errors["workPlan[0].aipActivityId"]);
        Assert.Equal([UpdateProposalValidator.ActivityNotInProject], errors["workPlan[2].aipActivityId"]);
        Assert.Equal([UpdateProposalValidator.StepName], errors["workPlan[3].name"]);
        Assert.DoesNotContain("workPlan[4].name", errors.Keys);
    }

    [Fact]
    public void Groups_NeedALabel_AndRowsMustPointAtARealGroup()
    {
        Dictionary<string, List<string>> errors = Validate(Valid() with
        {
            Groups = [new(null, "a", " ")],
            WorkPlan = [new(null, 401, null, "missing", null, null, null, null)],
        });

        Assert.Equal([UpdateProposalValidator.GroupName], errors["groups[0].label"]);
        Assert.Equal([UpdateProposalValidator.GroupName], errors["workPlan[0].groupKey"]);
    }

    [Fact]
    public void Benefits_MustBeTheFiveSectorsOnce()
    {
        Assert.Contains("benefits", Validate(Valid() with { Benefits = [new("Social", null, null)] }).Keys);
    }

    [Fact]
    public void Signatories_MustBeSlotsOneToFour()
    {
        Assert.Contains("signatories", Validate(Valid() with { Signatories = [new(1, null, null, null)] }).Keys);
        Assert.Equal([UpdateProposalValidator.TooLong],
            Validate(Valid() with { Signatories = [new(1, new string('x', 101), null, null), new(2, null, null, null), new(3, null, null, null), new(4, null, null, null)] })
            ["signatories[0].label"]);
    }

    [Fact]
    public void RichText_IsMeasuredAfterSanitizing()
    {
        // 150,000 characters of stripped markup leave a short section: allowed.
        string padding = string.Concat(Enumerable.Repeat("<script>x</script>", 9_000));
        Assert.Empty(Validate(Valid() with { Rationale = padding + "<p>ok</p>" }));

        string tooLong = "<p>" + new string('a', UpdateProposalValidator.MaxRichText + 1) + "</p>";
        Assert.Equal([UpdateProposalValidator.SectionTooLong], Validate(Valid() with { Rationale = tooLong })["rationale"]);
    }
}
