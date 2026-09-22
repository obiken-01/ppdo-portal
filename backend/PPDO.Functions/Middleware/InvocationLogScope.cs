using PPDO.Application.Common;

namespace PPDO.Functions.Middleware;

/// <summary>
/// Log-scope state for one function invocation: the function name, the invocation id, and the
/// authenticated caller's user id (PPDO-43).
///
/// <para><b>Why this is a class and not a Dictionary.</b> The obvious implementation —
/// <c>BeginScope(new Dictionary&lt;string, object&gt; { ["UserId"] = caller.UserId })</c> — captures
/// the value at the moment the scope opens. The scope has to open <i>before</i>
/// <c>await next(context)</c> so it covers the whole invocation, but
/// <see cref="CallerContext.UserId"/> is not set until partway through the handler, when
/// <c>_jwt.ValidateAsync</c> succeeds. A dictionary would therefore record <c>null</c> on every
/// request forever, and the feature would look present while carrying nothing.</para>
///
/// <para>Logging providers enumerate scope state at <b>log time</b>, not at scope-open time. So
/// holding the <see cref="CallerContext"/> itself and reading <c>UserId</c> during enumeration
/// gives each log line the value as it stood when that line was written: <c>(anonymous)</c> for
/// anything logged before the token was validated, and the real id for everything after.</para>
///
/// <para>Implements <see cref="IReadOnlyList{T}"/> of <see cref="KeyValuePair{TKey,TValue}"/> —
/// the shape both the console formatter and Application Insights probe for when deciding whether
/// a scope carries structured values rather than just a string.</para>
/// </summary>
internal sealed class InvocationLogScope : IReadOnlyList<KeyValuePair<string, object>>
{
    /// <summary>Stand-in for an unauthenticated (or not-yet-authenticated) caller. A literal
    /// reads better in a log query than an absent field, which is indistinguishable from a
    /// provider that dropped it.</summary>
    internal const string Anonymous = "(anonymous)";

    private readonly string _functionName;
    private readonly string _invocationId;
    private readonly CallerContext? _caller;

    internal InvocationLogScope(string functionName, string invocationId, CallerContext? caller)
    {
        _functionName = functionName;
        _invocationId = invocationId;
        _caller       = caller;
    }

    public int Count => 3;

    public KeyValuePair<string, object> this[int index] => index switch
    {
        0 => new("FunctionName", _functionName),
        1 => new("InvocationId", _invocationId),
        // Read on access, not in the constructor — see the class remarks.
        2 => new("UserId", _caller?.UserId?.ToString() ?? Anonymous),
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
    {
        for (int i = 0; i < Count; i++) yield return this[i];
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Rendered when a provider treats the scope as a plain string rather than enumerating it
    /// (the console formatter does this). Keep it terse — it prefixes every line.
    /// </summary>
    public override string ToString() =>
        $"{_functionName} [{_invocationId}] {_caller?.UserId?.ToString() ?? Anonymous}";
}
