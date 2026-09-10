namespace PPDO.Application.DTOs.Auth;

/// <summary>
/// One selectable option for the recovery-answer setup screen (RAL-266). <see cref="Key"/> is
/// the enum name — the same wire format <c>SetRecoveryAnswerDto.QuestionKey</c> expects back.
/// </summary>
public sealed record RecoveryQuestionOptionDto(string Key, string Text);
