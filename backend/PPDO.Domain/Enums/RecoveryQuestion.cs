namespace PPDO.Domain.Enums;

/// <summary>
/// The fixed set of password-recovery questions a user can choose from (RAL-253).
///
/// A fixed list, not free text — a self-authored question invites an unanswerable one
/// ("what's my favorite thing?"). Display text lives in
/// <see cref="PPDO.Application.Common.RecoveryQuestionCatalog"/>, not here — Domain has no
/// business owning user-facing copy.
///
/// Values are pinned explicitly and persisted as integers (matching <see cref="UserRole"/> and
/// <see cref="LandingPage"/>) — add new members at the end with the next free number, never
/// renumber or reuse a value.
/// </summary>
public enum RecoveryQuestion
{
    /// <summary>What town or municipality were you born in?</summary>
    BirthTown = 1,

    /// <summary>What is your mother's maiden name?</summary>
    MotherMaidenName = 2,

    /// <summary>What was the name of your elementary school?</summary>
    FirstElementarySchool = 3,

    /// <summary>What was the name of your first pet?</summary>
    FirstPetName = 4,

    /// <summary>What is the first name of your childhood best friend?</summary>
    ChildhoodBestFriend = 5,
}
