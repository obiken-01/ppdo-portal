using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;

namespace PPDO.Application.Services;

/// <summary>
/// SAIPs — start, cancel, list, get (v1.10.0 — PPDO-246, <c>docs/v1.10/SAIP_Spec.md</c> §3–§4).
/// Editing the copy, the letter and submit are T2; review and approval are T3.
///
/// <para>
/// <b>Scope.</b> Reads: the office's own users, host-office users and cross-office reviewers
/// (<c>OfficeScope.ResolveForReview</c>, the AIP review read). Any other office gets <b>404</b>, the
/// same answer as an office that does not exist. Start and cancel: the office's department head
/// only (Admin, or <c>CanReviewBudgetPlanning</c>, in their <b>own</b> office) or SuperAdmin; anyone
/// else who can see the office gets <b>403</b>.
/// </para>
/// </summary>
public interface ISaipService
{
    /// <summary>
    /// Starts the office's next SAIP and makes its draft copy, in one transaction.
    /// 404 unknown record / office out of scope / office not in the record; 400 not an entered year;
    /// 403 not the department head; 409 record archived, office not Consolidated, or an SAIP open.
    /// </summary>
    Task<ServiceResult<SaipDto>> StartAsync(int aipRecordId, int officeId, User caller, CancellationToken ct = default);

    /// <summary>
    /// Cancels an open SAIP: deletes its draft copy and marks it Cancelled, in one transaction.
    /// 404 not found / out of scope; 403 not the department head; 409 already Approved or Cancelled.
    /// </summary>
    Task<ServiceResult<SaipDto>> CancelAsync(int aipRecordId, int officeId, int saipId, User caller, CancellationToken ct = default);

    /// <summary>Every SAIP of the office in this record, newest first. 404 out of scope.</summary>
    Task<ServiceResult<IReadOnlyList<SaipDto>>> ListAsync(int aipRecordId, int officeId, User caller, CancellationToken ct = default);

    /// <summary>One SAIP. 404 when it is not this office's, or the office is out of scope.</summary>
    Task<ServiceResult<SaipDto>> GetAsync(int aipRecordId, int officeId, int saipId, User caller, CancellationToken ct = default);

    /// <summary>
    /// Attaches or replaces the signed request letter (PPDO-247, decision 7). Head only (403); while
    /// Draft, DepartmentReview or ReturnedByPpdo, else 409; PDF/JPG/PNG up to 10 MB, else 400.
    /// </summary>
    Task<ServiceResult<SaipDto>> PutLetterAsync(
        int aipRecordId, int officeId, int saipId, string? fileName, byte[] content, User caller,
        CancellationToken ct = default);

    /// <summary>The letter's bytes, for the office's readers and PPDO reviewers. 404 when none or out of scope.</summary>
    Task<ServiceResult<SaipLetterFile>> GetLetterAsync(
        int aipRecordId, int officeId, int saipId, User caller, CancellationToken ct = default);

    /// <summary>
    /// Sends the SAIP to PPDO (PPDO-247). Head only (403). 409 naming every gate that fails: divisions
    /// not all in, no letter, a new project's proposal not Finalized, incomplete work, an enforced
    /// ceiling exceeded on the amended totals. On success the copy is locked and PPDO's reviewers
    /// are notified.
    /// </summary>
    Task<ServiceResult<SaipDto>> SubmitAsync(
        int aipRecordId, int officeId, int saipId, User caller, CancellationToken ct = default);

    /// <summary>
    /// The live "changes so far" (PPDO-247, decision 17): the copy against the approved AIP, with the
    /// net per fund and the ceiling after. Open SAIPs only (409 otherwise); 404 out of scope.
    /// </summary>
    Task<ServiceResult<SaipChangesDto>> GetChangesAsync(
        int aipRecordId, int officeId, int saipId, User caller, CancellationToken ct = default);
}
