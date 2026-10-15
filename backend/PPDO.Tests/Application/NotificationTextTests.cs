using PPDO.Application.Common;

namespace PPDO.Tests.Application;

/// <summary>The frozen sentences of the notification feed (PPDO-221, spec §3.1, decision 9).</summary>
public sealed class NotificationTextTests
{
    [Fact]
    public void AipReviewerEdited_WithAMultiLineActivityName_KeepsTheChange()
    {
        // An activity name can be a whole paragraph (PPDO-222 live check): the change must survive.
        string activity = "Manpower augmentation through the creation of the following positions:\n\n"
            + string.Concat(Enumerable.Repeat("Provision for the payment of Salaries/Wages and allowances.\n", 20));

        string text = NotificationText.AipReviewerEdited(activity, "1-09-01-020 Computer Software CO ₱90,000.00 → ₱100,000.00");

        Assert.EndsWith("CO ₱90,000.00 → ₱100,000.00", text);
        Assert.DoesNotContain('\n', text);
        Assert.True(text.Length <= NotificationText.MaxLength);
    }

    [Fact]
    public void AipReviewerAdded_WithAMultiLineActivityName_KeepsTheAccountAndStaysOnOneLine()
    {
        string text = NotificationText.AipReviewerAdded("5-02-01-010 Traveling Expenses - Local", new string('x', 500) + "\nsecond line");

        Assert.StartsWith("PPDO added 5-02-01-010 Traveling Expenses - Local to ", text);
        Assert.DoesNotContain('\n', text);
    }

    [Fact]
    public void ProposalThreadStarted_WithALongProjectName_StillQuotesTheComment()
    {
        string text = NotificationText.ProposalThreadStarted("PPDO", "8000-1", new string('p', 600), "Fix the objective");

        Assert.Contains("\"Fix the objective\"", text);
    }

    [Fact]
    public void Quote_IsTheFirst80CharactersOnOneLine_WithAnEllipsisOnlyWhenCut()
    {
        Assert.Equal("Fix the objective", NotificationText.Quote("  Fix the\n\n objective  "));

        string quoted = NotificationText.Quote(new string('a', 81));
        Assert.Equal(new string('a', 80) + "…", quoted);
        Assert.Equal(new string('a', 80), NotificationText.Quote(new string('a', 80)));
    }

    [Theory]
    [InlineData(0, "PPDO returned your FY 2028 AIP")]
    [InlineData(1, "PPDO returned your FY 2028 AIP with 1 comment")]
    [InlineData(4, "PPDO returned your FY 2028 AIP with 4 comments")]
    public void AipReturnedByPpdo_CountsTheComments(int open, string expected)
        => Assert.Equal(expected, NotificationText.AipReturnedByPpdo(2028, open));

    [Fact]
    public void ProposalThreadStarted_WithoutARefCode_NamesTheProjectAlone()
        => Assert.Equal("PPDO commented on the proposal for Rice Seed: \"Hi\"",
            NotificationText.ProposalThreadStarted("PPDO", "", "Rice Seed", "Hi"));

    [Fact]
    public void ALongProjectName_IsCutShort_NeverOverflowingTheColumn()
    {
        // Since the PPDO-222 live check a name is cut like a quote (80 characters), well inside the
        // column, so the rest of the sentence is never the part that gets lost.
        string text = NotificationText.ProposalResolved("Ana", "001", new string('p', 600));
        Assert.InRange(text.Length, 1, NotificationText.MaxLength);
        Assert.Contains(new string('p', NotificationText.QuoteLength) + "…", text);
        Assert.DoesNotContain(new string('p', NotificationText.QuoteLength + 1), text);
    }

    [Fact]
    public void EveryTypeFitsItsColumn()
    {
        foreach (string type in typeof(NotificationTypes).GetFields().Select(f => (string)f.GetValue(null)!))
            Assert.InRange(type.Length, 1, 40);
    }

    [Fact]
    public void Links_AreWithinTheColumnAndPortalRelative()
    {
        foreach (string link in new[]
        {
            NotificationLinks.Proposal(int.MaxValue), NotificationLinks.AipEntry(2028),
            NotificationLinks.AipReview(int.MaxValue, 2028),
            NotificationLinks.AipActivity(2028, int.MaxValue, int.MaxValue, int.MaxValue),
        })
        {
            Assert.StartsWith("/budget-planning/", link);
            Assert.True(link.Length <= 400);
        }
    }
}
