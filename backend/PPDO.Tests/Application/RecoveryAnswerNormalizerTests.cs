using PPDO.Application.Common;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="RecoveryAnswerNormalizer"/> (RAL-253).
/// </summary>
public class RecoveryAnswerNormalizerTests
{
    [Theory]
    [InlineData("Manila", "manila")]
    [InlineData("manila ", "manila")]
    [InlineData(" Manila ", "manila")]
    [InlineData("MANILA", "manila")]
    [InlineData("San Jose", "san jose")]
    public void Normalize_TrimsAndLowercases(string input, string expected)
    {
        Assert.Equal(expected, RecoveryAnswerNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_IsIdempotent()
    {
        string once = RecoveryAnswerNormalizer.Normalize(" Manila ");
        string twice = RecoveryAnswerNormalizer.Normalize(once);
        Assert.Equal(once, twice);
    }
}
