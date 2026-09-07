namespace PPDO.Application.Common;

/// <summary>
/// The AIP form's rounding rule, in one place (v1.8.0 — DECISION 9 / tracker A2-1 / A3).
///
/// <b>Round every figure UP to the thousand, then sum the rounded figures — across and down.</b>
/// The FY2027 file does not round at all: it carries thousands to two decimals
/// (<c>8798.65</c> = ₱8,798,650). FY2028+ rounds.
///
/// <h3>⚠️ Round first, then add. Not the other way round.</h3>
/// The province builds the form upward — a row total is <c>SUM(L:N)</c>, an office subtotal sums
/// its rows, the sheet total sums the office rows — so every printed figure is a sum of already
/// rounded figures. Summing first and rounding once produces a different, smaller number, and it
/// would disagree with the printed document at every level above the row.
///
/// Worked example, three ₱1,200 MOOE figures: rounded-then-summed is
/// <c>2,000 + 2,000 + 2,000 = 6,000</c>; summed-then-rounded is <c>ceil(3,600) = 4,000</c>. The
/// ceiling check must use the first, because that is what the office's document will print.
///
/// <h3>⚠️ This is the BASE figure, and the ceiling never sees the uplift</h3>
/// DECISION G's +30% uplift on MOOE and CO is <b>presentation-only</b> (tracker G3). Two numbers
/// exist by design and neither is wrong:
/// <list type="bullet">
/// <item><b>Base, rounded</b> — what <see cref="UpToThousand"/> produces, and what the ceiling
/// check compares.</item>
/// <item><b>Uplifted</b> — base × 1.3, what the printed columns show.</item>
/// </list>
/// <b>They are not exactly 1.3× each other once rounded, and nothing may assume they are.</b>
/// Before rounding the ratio is exactly 1.3; after rounding it is not, and by how much depends on
/// tracker G5 (uplift-then-round vs round-then-uplift), which is still open. ₱1,000,400 base → the
/// ceiling sees 1,001; the form shows 1,301 or 1,302 depending on that answer. <b>Never write a
/// test or a reconciliation asserting <c>printed == 1.3 × checked</c>.</b>
///
/// A consequence that is intended and reads like a bug: an office encoding exactly to its ceiling
/// passes every check and prints a document about 30% over it (AIP_Form_Spec §6.2).
/// </summary>
public static class AipRounding
{
    /// <summary>Pesos in one printed unit. The AIP form is denominated in thousands.</summary>
    public const decimal PesosPerThousand = 1000m;

    /// <summary>
    /// Rounds one peso figure <b>up</b> to the next whole thousand pesos. ₱1 becomes ₱1,000;
    /// ₱1,000 stays ₱1,000; ₱1,000.01 becomes ₱2,000.
    ///
    /// ⚠️ Apply this to each figure <i>before</i> summing — see the class summary. Callers that
    /// sum first and call this once are computing a different number from the one the form prints.
    ///
    /// AIP amounts are non-negative, so the "up" direction is unambiguous here. For a negative
    /// input <see cref="Math.Ceiling(decimal)"/> rounds toward zero, which is still numerically
    /// up; no caller relies on that and none should.
    /// </summary>
    public static decimal UpToThousand(decimal pesos)
        => Math.Ceiling(pesos / PesosPerThousand) * PesosPerThousand;
}
