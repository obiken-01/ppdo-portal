namespace PPDO.Domain.Enums;

/// <summary>
/// PPDO organisational divisions. Used for scoping PR data and permission group assignment.
/// Staff and Observer users can only write data for their own Division.
/// SuperAdmin and Admin can read and write across all Divisions.
///
/// IMPORTANT: Never represent divisions as plain strings in code — always use this enum.
/// </summary>
public enum Division
{
    /// <summary>Administrative Division</summary>
    Admin = 0,

    /// <summary>Planning Division</summary>
    Planning = 1,

    /// <summary>Research Monitoring and Evaluation Division</summary>
    RM = 2,

    /// <summary>Management Information System Division</summary>
    MIS = 3,

    /// <summary>Special Program Division</summary>
    SPD = 4,
}
