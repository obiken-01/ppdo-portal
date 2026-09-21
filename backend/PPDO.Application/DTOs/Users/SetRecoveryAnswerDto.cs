namespace PPDO.Application.DTOs.Users;

/// <summary>
/// Request body for <c>PUT /api/users/me/recovery-answer</c> — self-service one-time
/// recovery-question setup (RAL-266). <see cref="QuestionKey"/> is the enum name, e.g.
/// "BirthTown" — see <c>RecoveryQuestionName</c>.
/// </summary>
public sealed record SetRecoveryAnswerDto(string QuestionKey, string Answer);
