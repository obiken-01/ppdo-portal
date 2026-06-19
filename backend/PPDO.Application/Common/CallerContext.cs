namespace PPDO.Application.Common;

/// <summary>
/// Scoped holder for the authenticated caller's user ID, set by JwtMiddleware after
/// successful token validation. More reliable than IHttpContextAccessor in the Azure
/// Functions isolated worker model, where HttpContext can be null during middleware execution.
/// Inject CallerContext (concrete) to write; read UserId anywhere in the same DI scope.
/// </summary>
public sealed class CallerContext
{
    public Guid? UserId { get; private set; }

    public void SetUserId(Guid userId) => UserId = userId;
}
