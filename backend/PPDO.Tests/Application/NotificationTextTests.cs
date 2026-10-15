using PPDO.Application.Common;

namespace PPDO.Tests.Application;

/// <summary>The frozen sentences of the notification feed (PPDO-221, spec §3.1, decision 9).</summary>
public sealed class NotificationTextTests
{
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
    public void ALongProjectName_IsCutToTheColumn_NeverOverflowingIt()
    {
        string text = NotificationText.ProposalResolved("Ana", "001", new string('p', 600));
        Assert.Equal(NotificationText.MaxLength, text.Length);
        Assert.EndsWith("…", text);
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
