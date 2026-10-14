namespace PPDO.Tests.Application;

/// <summary>
/// A <c>rowVersion</c> for tests whose subject is something other than the concurrent-edit guard.
/// PPDO-121 made the version required on every AIP activity and expenditure write, so a test that
/// omits it gets a 400 — which would also satisfy a test that EXPECTS a 400 for another reason, and
/// pass for the wrong one. Passing this keeps those tests about what they say they are about.
/// </summary>
internal static class AipTestVersion
{
    public static readonly byte[] Any = [0, 0, 0, 0, 0, 0, 0, 7];
}
