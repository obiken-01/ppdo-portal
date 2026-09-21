using PPDO.Domain.Enums;

namespace PPDO.Application.Common;

/// <summary>
/// Display text for each <see cref="RecoveryQuestion"/> (RAL-253).
///
/// The single place both the recovery-answer setup screen (RAL-266) and the forgot-password
/// flow (RAL-265/269) read question text from, so the two never drift.
/// </summary>
public static class RecoveryQuestionCatalog
{
    /// <summary>
    /// The default question shown by <c>forgot-password</c> when the account doesn't exist, or
    /// exists but hasn't set a recovery question yet — see RAL-265's enumeration-guard note.
    /// Picking the first fixed member keeps the response indistinguishable from a real account.
    /// </summary>
    public const RecoveryQuestion Default = RecoveryQuestion.BirthTown;

    private static readonly IReadOnlyDictionary<RecoveryQuestion, string> Questions =
        new Dictionary<RecoveryQuestion, string>
        {
            [RecoveryQuestion.BirthTown]             = "What town or municipality were you born in?",
            [RecoveryQuestion.MotherMaidenName]       = "What is your mother's maiden name?",
            [RecoveryQuestion.FirstElementarySchool]  = "What was the name of your elementary school?",
            [RecoveryQuestion.FirstPetName]           = "What was the name of your first pet?",
            [RecoveryQuestion.ChildhoodBestFriend]    = "What is the first name of your childhood best friend?",
        };

    /// <summary>Display text for a question key. Throws if a new enum member is added without a catalog entry.</summary>
    public static string TextFor(RecoveryQuestion question) => Questions[question];

    /// <summary>All questions, in the fixed order offered on the setup screen.</summary>
    public static IReadOnlyDictionary<RecoveryQuestion, string> All => Questions;
}
