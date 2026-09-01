using Moq;
using PPDO.Application.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="ReviewerWriteGuard"/> — the codebase's first subtractive permission
/// (v1.8.0 — RAL-256).
///
/// The rule is NOT "reviewers cannot write". There are two reviewer kinds (tracker B11):
///   department-head reviewer (CanReviewBudgetPlanning) — MAY edit during review
///   PPDO consolidated reviewer (CanReviewAllOffices)   — comment only, denied here
///
/// The two cases most worth pinning are the department head (who must NOT be denied — the
/// intuitive-but-wrong implementation denies them) and SuperAdmin (who resolves every flag true
/// and would otherwise be locked out of every write in budget planning).
/// </summary>
public sealed class ReviewerWriteGuardTests
{
    private readonly Mock<IPermissionService> _permissions = new(MockBehavior.Strict);

    private static User MakeUser(UserRole role = UserRole.Staff) => new()
    {
        Id       = Guid.NewGuid(),
        Role     = role,
        OfficeId = 7,
        Office   = new Office { Id = 7, OfficeCode = "GSO", IsHostOffice = false },
    };

    private void CrossOfficeReviewer(User user, bool value) =>
        _permissions
            .Setup(p => p.CanReviewAllOfficesAsync(user, It.IsAny<CancellationToken>()))
            .ReturnsAsync(value);

    // ── Denied ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeniesWriteAsync_CrossOfficeReviewer_IsDenied()
    {
        User user = MakeUser();
        CrossOfficeReviewer(user, true);

        Assert.True(await ReviewerWriteGuard.DeniesWriteAsync(user, _permissions.Object));
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Staff)]
    public async Task DeniesWriteAsync_CrossOfficeReviewer_IsDeniedRegardlessOfRole(UserRole role)
    {
        User user = MakeUser(role);
        CrossOfficeReviewer(user, true);

        Assert.True(await ReviewerWriteGuard.DeniesWriteAsync(user, _permissions.Object));
    }

    // ── Not denied ────────────────────────────────────────────────────────────

    /// <summary>
    /// The department head edits values during review — "to update any minor details they found".
    /// Denying them freezes them out of the edits the review exists to make. This is the case the
    /// intuitive implementation ("is a reviewer → read-only") gets wrong, and the reason the guard
    /// keys on the cross-office flag rather than on "reviewer".
    /// </summary>
    [Fact]
    public async Task DeniesWriteAsync_DepartmentHeadReviewer_IsNotDenied()
    {
        User user = MakeUser();
        CrossOfficeReviewer(user, false);   // holds CanReviewBudgetPlanning, not this one

        Assert.False(await ReviewerWriteGuard.DeniesWriteAsync(user, _permissions.Object));
    }

    [Fact]
    public async Task DeniesWriteAsync_OrdinaryUser_IsNotDenied()
    {
        User user = MakeUser();
        CrossOfficeReviewer(user, false);

        Assert.False(await ReviewerWriteGuard.DeniesWriteAsync(user, _permissions.Object));
    }

    /// <summary>
    /// The decision RAL-256 required to be made explicitly. CanReviewAllOfficesAsync resolves TRUE
    /// for SuperAdmin — as every flag does, so support access always works — so a guard that
    /// simply asked "is this a cross-office reviewer?" would lock SuperAdmin out of every write in
    /// budget planning. The blanket bypass exists to grant access, never to impose a restriction.
    ///
    /// Strict mock with no setup: if the guard consults the permission service at all for a
    /// SuperAdmin, this test fails rather than passing for the wrong reason.
    /// </summary>
    [Fact]
    public async Task DeniesWriteAsync_SuperAdmin_IsNeverDenied()
    {
        User superAdmin = MakeUser(UserRole.SuperAdmin);

        Assert.False(await ReviewerWriteGuard.DeniesWriteAsync(superAdmin, _permissions.Object));

        _permissions.Verify(
            p => p.CanReviewAllOfficesAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// A host-office (PPDO) cross-office reviewer is still denied. The grant is what matters, not
    /// which office the holder sits in — otherwise the PPDO reviewers the flag was created for
    /// would be the one group it never applied to.
    /// </summary>
    [Fact]
    public async Task DeniesWriteAsync_CrossOfficeReviewerInTheHostOffice_IsStillDenied()
    {
        User user = new()
        {
            Id       = Guid.NewGuid(),
            Role     = UserRole.Staff,
            OfficeId = 1,
            Office   = new Office { Id = 1, OfficeCode = "PPDO", IsHostOffice = true },
        };
        CrossOfficeReviewer(user, true);

        Assert.True(await ReviewerWriteGuard.DeniesWriteAsync(user, _permissions.Object));
    }
}
