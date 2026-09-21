using PPDO.Application.Common;
using PPDO.Domain.Enums;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="RecoveryQuestionCatalog"/> (RAL-253).
/// </summary>
public class RecoveryQuestionCatalogTests
{
    [Theory]
    [InlineData(RecoveryQuestion.BirthTown)]
    [InlineData(RecoveryQuestion.MotherMaidenName)]
    [InlineData(RecoveryQuestion.FirstElementarySchool)]
    [InlineData(RecoveryQuestion.FirstPetName)]
    [InlineData(RecoveryQuestion.ChildhoodBestFriend)]
    public void TextFor_EveryEnumValue_HasACatalogEntry(RecoveryQuestion question)
    {
        string text = RecoveryQuestionCatalog.TextFor(question);
        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    [Fact]
    public void All_CoversEveryEnumMember()
    {
        // Guards against someone adding a RecoveryQuestion member without a catalog entry —
        // TextFor would throw a KeyNotFoundException at runtime instead of failing a build.
        IEnumerable<RecoveryQuestion> allMembers = Enum.GetValues<RecoveryQuestion>();
        Assert.Equal(allMembers.Count(), RecoveryQuestionCatalog.All.Count);

        foreach (RecoveryQuestion question in allMembers)
            Assert.True(RecoveryQuestionCatalog.All.ContainsKey(question));
    }

    [Fact]
    public void Default_IsAMemberOfTheCatalog()
    {
        // The forgot-password enumeration guard (RAL-265) relies on this always resolving.
        Assert.True(RecoveryQuestionCatalog.All.ContainsKey(RecoveryQuestionCatalog.Default));
    }
}
