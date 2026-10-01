using PPDO.Application.Common;
using PPDO.Application.DTOs.InvestmentProposal;

namespace PPDO.Application.Validators.InvestmentProposal;

/// <summary>
/// The PUT body rules of Investment_Proposal_Spec.md §4, messages word for word (PPDO-155).
///
/// <para>
/// Errors are keyed by the client's own path, e.g. <c>teamMembers[2].sex</c>, so the editor can put
/// each message under its field. Paths use the indices of the array as sent. A row left completely
/// blank is skipped rather than reported, and the service drops it on save, so an empty "+ Add row"
/// never blocks a save. A row with anything in it must be complete.
/// </para>
///
/// <para>
/// Pure and static: the only outside fact it needs is which activities belong to the project, and
/// the service passes that in.
/// </para>
/// </summary>
public static class UpdateProposalValidator
{
    public const string TooLong            = "Too long.";
    public const string ScoreRange         = "HGDG score must be between 0 and 20.";
    public const string UnknownChecklist   = "Choose a checklist from the list.";
    public const string WholeNumber        = "Enter a whole number, 0 or more.";
    public const string ChooseSex          = "Choose M or F.";
    public const string ActivityNotInProject = "This activity is not part of the project.";
    public const string StepName           = "Enter the step's name.";
    public const string GroupName          = "Enter a group name.";
    public const string SectionTooLong     = "This section is too long.";
    public const string Required           = "This is required.";

    public const int MaxRichText  = 100_000;
    public const int MaxPlainCell = 4_000;
    public const int MaxCount     = 1_000_000;

    public static Dictionary<string, List<string>> Validate(
        ProposalContentDto content, IReadOnlySet<int> projectActivityIds)
    {
        Errors e = new();

        // ── Section A ─────────────────────────────────────────────────────────
        e.Max("projectLocation", content.ProjectLocation, 500);
        if (!string.IsNullOrWhiteSpace(content.HgdgChecklist) && !HgdgChecklists.IsKnown(content.HgdgChecklist.Trim()))
            e.Add("hgdgChecklist", UnknownChecklist);
        if (content.HgdgScore is decimal score && (score < 0 || score > 20 || decimal.Round(score, 1) != score))
            e.Add("hgdgScore", ScoreRange);

        for (int i = 0; i < content.BeneficiariesSummary.Count; i++)
        {
            ProposalBeneficiaryDto b = content.BeneficiariesSummary[i];
            if (IsBlank(b.Indicator) && b.Male is null && b.Female is null) continue;
            string at = $"beneficiariesSummary[{i}]";
            e.RequiredMax($"{at}.indicator", b.Indicator, 1000);
            e.Count($"{at}.male", b.Male);
            e.Count($"{at}.female", b.Female);
        }

        // ── Narrative sections ────────────────────────────────────────────────
        e.Rich("description", content.Description);
        e.Rich("rationale", content.Rationale);
        e.Rich("generalObjective", content.GeneralObjective);
        e.Rich("partnershipSustainability", content.PartnershipSustainability);
        e.Max("womensImpactStrategy", content.WomensImpactStrategy, MaxPlainCell);

        // ── D: exactly the five sectors (a programming error otherwise) ───────
        List<string?> sectors = content.Benefits.Select(b => b.Sector).ToList();
        if (sectors.Count != InvestmentProposalSector.All.Count
            || !InvestmentProposalSector.All.All(s => sectors.Count(x => x == s) == 1))
            e.Add("benefits", "Section D must list each of the five sectors once.");
        for (int i = 0; i < content.Benefits.Count; i++)
        {
            // D cells are rich text too (decision 20), capped at the column's 4,000.
            if (ProposalRichText.Sanitize(content.Benefits[i].Benefit)?.Length > MaxPlainCell)
                e.Add($"benefits[{i}].benefit", TooLong);
            if (ProposalRichText.Sanitize(content.Benefits[i].Cost)?.Length > MaxPlainCell)
                e.Add($"benefits[{i}].cost", TooLong);
        }

        // ── E: exactly the four levels ────────────────────────────────────────
        List<string?> levels = content.Logframe.Select(l => l.Level).ToList();
        if (levels.Count != InvestmentProposalLogframeLevel.All.Count
            || !InvestmentProposalLogframeLevel.All.All(l => levels.Count(x => x == l) == 1))
            e.Add("logframe", "Section E must list each of the four levels once.");
        for (int i = 0; i < content.Logframe.Count; i++)
        {
            e.Max($"logframe[{i}].target", content.Logframe[i].Target, MaxPlainCell);
            e.Max($"logframe[{i}].verification", content.Logframe[i].Verification, MaxPlainCell);
        }

        // ── F ─────────────────────────────────────────────────────────────────
        for (int i = 0; i < content.TargetBeneficiaries.Count; i++)
        {
            ProposalTargetBeneficiaryDto t = content.TargetBeneficiaries[i];
            if (IsBlank(t.Name) && t.Male is null && t.Female is null) continue;
            string at = $"targetBeneficiaries[{i}]";
            if (t.Kind is not (InvestmentProposalBeneficiarySection.Direct or InvestmentProposalBeneficiarySection.Indirect))
                e.Add($"{at}.kind", "Choose Direct or Indirect.");
            e.RequiredMax($"{at}.name", t.Name, 1000);
            e.Count($"{at}.male", t.Male);
            e.Count($"{at}.female", t.Female);
        }

        // ── G: groups, then rows ──────────────────────────────────────────────
        HashSet<string> groupKeys = new(StringComparer.Ordinal);
        for (int i = 0; i < content.Groups.Count; i++)
        {
            ProposalGroupDto g = content.Groups[i];
            string at = $"groups[{i}]";
            if (string.IsNullOrWhiteSpace(g.Label)) e.Add($"{at}.label", GroupName);
            else e.Max($"{at}.label", g.Label, 300);
            if (string.IsNullOrWhiteSpace(g.ClientKey) || !groupKeys.Add(g.ClientKey))
                e.Add($"{at}.clientKey", "Each group needs its own key.");
        }

        HashSet<int> seenActivities = [];
        for (int i = 0; i < content.WorkPlan.Count; i++)
        {
            ProposalWorkPlanRowDto r = content.WorkPlan[i];
            string at = $"workPlan[{i}]";
            if (r.AipActivityId is int activityId)
            {
                if (!projectActivityIds.Contains(activityId) || !seenActivities.Add(activityId))
                    e.Add($"{at}.aipActivityId", ActivityNotInProject);
            }
            else
            {
                if (IsBlank(r.Name) && IsBlank(r.PerformanceTarget) && IsBlank(r.GenderIssues)
                    && IsBlank(r.Timeline) && IsBlank(r.Opr)) continue;
                if (string.IsNullOrWhiteSpace(r.Name)) e.Add($"{at}.name", StepName);
                else e.Max($"{at}.name", r.Name, 500);
                e.Max($"{at}.timeline", r.Timeline, 200);
                e.Max($"{at}.opr", r.Opr, 300);
            }
            if (!string.IsNullOrWhiteSpace(r.GroupKey) && !groupKeys.Contains(r.GroupKey))
                e.Add($"{at}.groupKey", GroupName);
            e.Max($"{at}.performanceTarget", r.PerformanceTarget, MaxPlainCell);
            e.Max($"{at}.genderIssues", r.GenderIssues, MaxPlainCell);
        }

        // ── I ─────────────────────────────────────────────────────────────────
        e.Max("projectSupervisor", content.ProjectSupervisor, 200);
        e.Max("projectManager", content.ProjectManager, 200);
        for (int i = 0; i < content.TeamMembers.Count; i++)
        {
            ProposalTeamMemberDto m = content.TeamMembers[i];
            if (IsBlank(m.Name) && IsBlank(m.Sex) && IsBlank(m.GadTrainings) && IsBlank(m.Expertise)
                && IsBlank(m.RequiredTraining)) continue;
            string at = $"teamMembers[{i}]";
            e.RequiredMax($"{at}.name", m.Name, 200);
            if (m.Sex is not (InvestmentProposalSex.Male or InvestmentProposalSex.Female)) e.Add($"{at}.sex", ChooseSex);
            e.Max($"{at}.gadTrainings", m.GadTrainings, 1000);
            e.Max($"{at}.expertise", m.Expertise, 500);
            e.Max($"{at}.requiredTraining", m.RequiredTraining, 1000);
        }

        // ── K, L ──────────────────────────────────────────────────────────────
        for (int i = 0; i < content.Monitoring.Count; i++)
        {
            ProposalMonitoringDto m = content.Monitoring[i];
            if (IsBlank(m.Activity) && IsBlank(m.Schedule) && IsBlank(m.Tools)) continue;
            string at = $"monitoring[{i}]";
            if (m.Phase is null || !InvestmentProposalMonitoringPhase.All.Contains(m.Phase))
                e.Add($"{at}.phase", "Choose a phase.");
            e.RequiredMax($"{at}.activity", m.Activity, 1000);
            e.Max($"{at}.schedule", m.Schedule, 500);
            e.Max($"{at}.tools", m.Tools, 1000);
        }

        for (int i = 0; i < content.Risks.Count; i++)
        {
            ProposalRiskDto r = content.Risks[i];
            if (IsBlank(r.Risk) && IsBlank(r.Prevention) && IsBlank(r.Monitoring)) continue;
            string at = $"risks[{i}]";
            e.RequiredMax($"{at}.risk", r.Risk, 1000);
            e.Max($"{at}.prevention", r.Prevention, 2000);
            e.Max($"{at}.monitoring", r.Monitoring, 2000);
        }

        // ── Signatories: exactly slots 1–4 ────────────────────────────────────
        List<int> slots = content.Signatories.Select(s => s.Slot).OrderBy(s => s).ToList();
        if (!slots.SequenceEqual([1, 2, 3, 4]))
            e.Add("signatories", "Signatories must be slots 1 to 4.");
        for (int i = 0; i < content.Signatories.Count; i++)
        {
            ProposalSignatoryDto s = content.Signatories[i];
            e.Max($"signatories[{i}].label", s.Label, 100);
            e.Max($"signatories[{i}].name", s.Name, 200);
            e.Max($"signatories[{i}].position", s.Position, 200);
        }

        return e.Map;
    }

    private static bool IsBlank(string? value) => string.IsNullOrWhiteSpace(value);

    private sealed class Errors
    {
        public Dictionary<string, List<string>> Map { get; } = new(StringComparer.Ordinal);

        public void Add(string path, string message)
        {
            if (!Map.TryGetValue(path, out List<string>? list)) Map[path] = list = [];
            list.Add(message);
        }

        public void Max(string path, string? value, int max)
        {
            if (value is not null && value.Trim().Length > max) Add(path, TooLong);
        }

        public void RequiredMax(string path, string? value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) Add(path, Required);
            else Max(path, value, max);
        }

        public void Count(string path, int? value)
        {
            if (value is int v && (v < 0 || v > MaxCount)) Add(path, WholeNumber);
        }

        public void Rich(string path, string? html)
        {
            if (ProposalRichText.Sanitize(html)?.Length > MaxRichText) Add(path, SectionTooLong);
        }
    }
}
