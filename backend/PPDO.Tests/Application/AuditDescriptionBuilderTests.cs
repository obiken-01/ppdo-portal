using PPDO.Application.Common;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="AuditDescriptionBuilder"/>. Pure function, no mocks needed.
/// </summary>
public sealed class AuditDescriptionBuilderTests
{
    [Fact]
    public void Build_Create_ListsAllNewValueFields()
    {
        string description = AuditDescriptionBuilder.Build(
            AuditAction.Create,
            oldValuesJson: null,
            newValuesJson: """{"fullName":"Jane Doe","isActive":true}""");

        Assert.StartsWith("Created with:", description);
        Assert.Contains("- Full Name: Jane Doe", description);
        Assert.Contains("- Is Active: Yes", description);
    }

    [Fact]
    public void Build_Delete_ReturnsDeactivated()
    {
        string description = AuditDescriptionBuilder.Build(
            AuditAction.Delete,
            oldValuesJson: """{"isActive":true}""",
            newValuesJson: null);

        Assert.Equal("Deactivated", description);
    }

    [Fact]
    public void Build_Update_OnlyListsChangedFields()
    {
        string description = AuditDescriptionBuilder.Build(
            AuditAction.Update,
            oldValuesJson: """{"fullName":"Jane Doe","position":"Clerk"}""",
            newValuesJson: """{"fullName":"Jane D. Smith","position":"Clerk"}""");

        Assert.StartsWith("Updated:", description);
        Assert.Contains("- Full Name: Jane Doe → Jane D. Smith", description);
        Assert.DoesNotContain("Position", description); // unchanged field must not appear
    }

    [Fact]
    public void Build_Update_NoActualFieldChanges_ReturnsNoChangesMessage()
    {
        string description = AuditDescriptionBuilder.Build(
            AuditAction.Update,
            oldValuesJson: """{"fullName":"Jane Doe"}""",
            newValuesJson: """{"fullName":"Jane Doe"}""");

        Assert.Equal("No visible field changes.", description);
    }

    [Fact]
    public void Build_Update_NullOldValues_TreatsLikeCreate()
    {
        // UserService.ResetPasswordAsync's marker shape: oldValues=null, newValues={PasswordReset:true}.
        string description = AuditDescriptionBuilder.Build(
            AuditAction.Update,
            oldValuesJson: null,
            newValuesJson: """{"passwordReset":true}""");

        Assert.StartsWith("Recorded:", description);
        Assert.Contains("- Password Reset: Yes", description);
    }

    [Fact]
    public void Build_NullField_FormatsAsEmDash()
    {
        string description = AuditDescriptionBuilder.Build(
            AuditAction.Update,
            oldValuesJson: """{"email":"old@ppdo.gov.ph"}""",
            newValuesJson: """{"email":null}""");

        Assert.Contains("- Email: old@ppdo.gov.ph → —", description);
    }

    [Fact]
    public void Build_BothValuesNull_ReturnsNoDetailsRecorded()
    {
        string description = AuditDescriptionBuilder.Build(AuditAction.Update, null, null);

        Assert.Equal("No details recorded.", description);
    }

    [Fact]
    public void Build_NewFieldNotInOldValues_TreatedAsChanged()
    {
        // A field added to a snapshot later (schema drift) shouldn't crash -- old side falls back to "—".
        string description = AuditDescriptionBuilder.Build(
            AuditAction.Update,
            oldValuesJson: """{"fullName":"Jane Doe"}""",
            newValuesJson: """{"fullName":"Jane Doe","contactNo":"09171234567"}""");

        Assert.Contains("- Contact No: — → 09171234567", description);
    }
}
