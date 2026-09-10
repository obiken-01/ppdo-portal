using PPDO.Domain.Entities;
using PPDO.Domain.Enums;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Resolves which page a user lands on after signing in (RAL-251).
/// </summary>
public interface ILandingPageResolver
{
    /// <summary>
    /// Returns the landing page for <paramref name="user"/>, walking
    /// user → division → office → first reachable page, ending at
    /// <see cref="LandingPage.Profile"/>.
    ///
    /// Never returns a page the user cannot actually reach, so the result is always safe to
    /// redirect to.
    /// </summary>
    Task<LandingPage> ResolveAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when <paramref name="page"/> is reachable for this user. Used by the resolver and by
    /// the selector UIs, which must only offer pages the target can actually open.
    /// </summary>
    Task<bool> IsReachableAsync(User user, LandingPage page, CancellationToken cancellationToken = default);
}
