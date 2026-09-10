using PPDO.Application.Common;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="PasswordGenerator"/> (RAL-254).
/// The generator replaced a single shared default password, so the properties that
/// matter are uniqueness, policy compliance, and the unambiguous alphabet.
/// </summary>
public class PasswordGeneratorTests
{
    [Fact]
    public void Generate_ByDefault_ReturnsDefaultLength()
    {
        Assert.Equal(PasswordGenerator.DefaultLength, PasswordGenerator.Generate().Length);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(32)]
    public void Generate_WithLength_ReturnsThatLength(int length)
    {
        Assert.Equal(length, PasswordGenerator.Generate(length).Length);
    }

    [Fact]
    public void Generate_BelowPolicyMinimum_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PasswordGenerator.Generate(7));
    }

    [Fact]
    public void Generate_Always_SatisfiesChangePasswordPolicy()
    {
        // ChangePasswordAsync requires 8+ chars, one uppercase and one digit. A generated
        // password that failed this would be rejected the moment the user tried to reuse it.
        for (int i = 0; i < 200; i++)
        {
            string password = PasswordGenerator.Generate();

            Assert.True(password.Length >= 8);
            Assert.Contains(password, char.IsUpper);
            Assert.Contains(password, char.IsDigit);
        }
    }

    [Fact]
    public void Generate_Always_ContainsALowercaseLetter()
    {
        for (int i = 0; i < 200; i++)
            Assert.Contains(PasswordGenerator.Generate(), char.IsLower);
    }

    [Fact]
    public void Generate_Always_OmitsAmbiguousCharacters()
    {
        // O/0 and I/l/1 are dropped so a password can be read off a screen or relayed
        // by phone without transcription errors.
        for (int i = 0; i < 200; i++)
        {
            string password = PasswordGenerator.Generate();
            Assert.DoesNotContain(password, c => c is 'O' or '0' or 'I' or 'l' or '1');
        }
    }

    [Fact]
    public void Generate_CalledRepeatedly_ProducesDistinctPasswords()
    {
        // The whole point of RAL-254: two accounts must never land on the same password.
        HashSet<string> issued = [];
        for (int i = 0; i < 500; i++)
            Assert.True(issued.Add(PasswordGenerator.Generate()), "Generated a duplicate password.");
    }

    [Fact]
    public void Generate_DoesNotAlwaysSeedTheSamePositions()
    {
        // Without the shuffle the first three characters would always be upper/lower/digit,
        // which would leak structure and shrink the effective search space.
        HashSet<int> digitPositions = [];
        for (int i = 0; i < 200; i++)
        {
            string password = PasswordGenerator.Generate();
            for (int j = 0; j < password.Length; j++)
                if (char.IsDigit(password[j])) digitPositions.Add(j);
        }

        Assert.True(digitPositions.Count > 3, "Digits never moved out of the seeded positions.");
    }
}
