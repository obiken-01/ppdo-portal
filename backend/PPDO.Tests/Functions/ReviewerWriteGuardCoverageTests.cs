using System.Net;
using System.Reflection;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Moq;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;
using PPDO.Functions.Functions;

namespace PPDO.Tests.Functions;

/// <summary>
/// Every budget-planning WRITE endpoint must refuse a cross-office (comment-only) reviewer, and
/// must NOT refuse a department-head reviewer (v1.8.0 — RAL-256).
///
/// <b>Why this is reflective rather than 40 hand-written tests.</b> RAL-256 asks for the endpoint
/// list to be "covered by tests rather than by inspection". A fixed list of tests only covers the
/// endpoints someone remembered to add to it — and the failure mode this guard exists to prevent
/// is precisely *one endpoint getting missed*. Discovering the endpoints from the
/// <c>[Function]</c>/<c>[HttpTrigger]</c> attributes means a write endpoint added next year is
/// covered the day it is written, and an endpoint that quietly drops the guard fails the build.
///
/// Each case is a real invocation of the real handler: mocked JWT and permissions, then assert on
/// the returned <see cref="HttpResponseData"/>. Handlers authorize before touching the body or the
/// route arguments, so the arguments can be defaults and no request body is needed.
///
/// The dev-only <c>BudgetPlanningCleanupFunctions</c> is deliberately out of scope: it is gated by
/// a <c>DevCleanupKey</c> header and never authenticates a caller, so there is no reviewer to deny.
/// </summary>
public sealed class ReviewerWriteGuardCoverageTests
{
    /// <summary>
    /// The budget-planning Function classes that own content writes. Adding a new one here is the
    /// single maintenance step when a feature area is added.
    /// </summary>
    private static readonly Type[] BudgetPlanningFunctionTypes =
    [
        typeof(AipFunctions),
        typeof(LdipFunctions),
        typeof(WfpFunctions),
        typeof(AllocationFunctions),
        typeof(WfpExpenditureFunctions),
        typeof(WfpProcurementPresetFunctions),
    ];

    private static readonly string[] WriteVerbs = ["post", "put", "delete", "patch"];

    public static TheoryData<string, string> WriteEndpoints()
    {
        TheoryData<string, string> data = new();
        foreach ((Type type, MethodInfo method, _) in DiscoverWriteEndpoints())
            data.Add(type.FullName!, method.Name);
        return data;
    }

    /// <summary>
    /// Guards the discovery itself. If a refactor changes the attribute shape and this returns
    /// nothing, every theory below would vacuously pass — so assert the count is in the expected
    /// range instead. The exact number is allowed to grow; it must never collapse.
    /// </summary>
    [Fact]
    public void Discovery_FindsTheBudgetPlanningWriteEndpoints()
    {
        List<(Type, MethodInfo, string)> found = DiscoverWriteEndpoints();

        Assert.True(found.Count >= 40,
            $"Expected at least 40 budget-planning write endpoints, found {found.Count}. " +
            "If endpoints were legitimately removed, lower this floor deliberately — do not " +
            "delete the assertion, or the coverage theories start passing vacuously.");
    }

    [Theory]
    [MemberData(nameof(WriteEndpoints))]
    public async Task WriteEndpoint_RefusesACrossOfficeReviewer(string typeName, string methodName)
    {
        HttpResponseData response = await InvokeAsync(
            typeName, methodName, crossOfficeReviewer: true, departmentHeadReviewer: false);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// The other half of the B11 split, and the assertion that catches the intuitive-but-wrong
    /// implementation: a department-head reviewer edits values during review, so no write endpoint
    /// may treat them differently from anyone else.
    ///
    /// Stated as "same outcome as an ordinary user" rather than "not 403", because some of these
    /// handlers legitimately answer 403 for their own reasons — AipUnlock is Admin-only — and
    /// others run on past the guard into mocked services that cannot produce a real
    /// ServiceResult. Comparing the two runs is immune to both: after the guard the code paths
    /// are identical, so the outcomes must be too. A guard that fired on the department head
    /// would show up here as 403-vs-something-else.
    /// </summary>
    [Theory]
    [MemberData(nameof(WriteEndpoints))]
    public async Task WriteEndpoint_TreatsADepartmentHeadReviewerLikeAnyOtherUser(
        string typeName, string methodName)
    {
        string departmentHead = await OutcomeAsync(
            typeName, methodName, crossOfficeReviewer: false, departmentHeadReviewer: true);
        string ordinaryUser = await OutcomeAsync(
            typeName, methodName, crossOfficeReviewer: false, departmentHeadReviewer: false);

        Assert.Equal(ordinaryUser, departmentHead);
    }

    /// <summary>
    /// Runs one endpoint and reduces it to a comparable outcome: the HTTP status, or the name of
    /// whatever it threw. Handlers that reach a mocked service throw rather than returning a
    /// status — ServiceResult is sealed with a private constructor, so Moq cannot fabricate one —
    /// and that is fine here, because the comparison only needs both runs to end the same way.
    /// </summary>
    private static async Task<string> OutcomeAsync(
        string typeName, string methodName, bool crossOfficeReviewer, bool departmentHeadReviewer)
    {
        try
        {
            HttpResponseData response = await InvokeAsync(
                typeName, methodName, crossOfficeReviewer, departmentHeadReviewer);
            return $"status:{response.StatusCode}";
        }
        catch (Exception ex)
        {
            return $"threw:{(ex is TargetInvocationException tie ? tie.InnerException! : ex).GetType().Name}";
        }
    }

    /// <summary>
    /// Proves the theories above are not passing vacuously. If every caller got 403 from these
    /// endpoints for unrelated reasons, "the cross-office reviewer is refused" would be true
    /// without the guard existing at all. Here the two callers differ only in that one flag, and
    /// only one of them is refused — so the 403 is attributable to the guard and nothing else.
    /// </summary>
    [Theory]
    [MemberData(nameof(WriteEndpoints))]
    public async Task WriteEndpoint_OrdinaryUserIsNotRefused_SoTheGuardIsWhatCausesThe403(
        string typeName, string methodName)
    {
        string ordinary = await OutcomeAsync(
            typeName, methodName, crossOfficeReviewer: false, departmentHeadReviewer: false);
        string reviewer = await OutcomeAsync(
            typeName, methodName, crossOfficeReviewer: true, departmentHeadReviewer: false);

        Assert.NotEqual($"status:{HttpStatusCode.Forbidden}", ordinary);
        Assert.Equal($"status:{HttpStatusCode.Forbidden}", reviewer);
    }

    // ── Discovery + invocation ────────────────────────────────────────────────

    private static List<(Type Type, MethodInfo Method, string FunctionName)> DiscoverWriteEndpoints()
    {
        List<(Type, MethodInfo, string)> found = [];

        foreach (Type type in BudgetPlanningFunctionTypes)
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                FunctionAttribute? function = method.GetCustomAttribute<FunctionAttribute>();
                if (function is null) continue;

                HttpTriggerAttribute? trigger = method.GetParameters()
                    .Select(p => p.GetCustomAttribute<HttpTriggerAttribute>())
                    .FirstOrDefault(t => t is not null);
                if (trigger is null) continue;

                string[] verbs = trigger.Methods ?? [];
                if (verbs.Any(m => WriteVerbs.Contains(m.ToLowerInvariant())))
                    found.Add((type, method, function.Name));
            }
        }

        return found;
    }

    /// <summary>
    /// Builds the Function class with mocked dependencies and invokes one handler.
    ///
    /// Permissions are mocked permissive — every Can*Async returns true — so the additive
    /// predicate always passes and a 403 can only have come from the reviewer guard. The one
    /// exception is CanReviewAllOfficesAsync, which is the variable under test.
    /// </summary>
    private static async Task<HttpResponseData> InvokeAsync(
        string typeName, string methodName, bool crossOfficeReviewer, bool departmentHeadReviewer)
    {
        Type type = BudgetPlanningFunctionTypes.Single(t => t.FullName == typeName);
        MethodInfo method = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(m => m.Name == methodName);

        // Admin, not Staff: a couple of these handlers carry their own role check (AipUnlock is
        // Admin-only), and a caller who trips that would make the 403 assertion pass for the wrong
        // reason. Admin is not auto-granted either reviewer flag, so the mocks below stay in
        // control of the variable actually under test.
        User caller = new()
        {
            Id       = Guid.NewGuid(),
            Role     = UserRole.Admin,
            OfficeId = 7,
            Office   = new Office { Id = 7, OfficeCode = "GSO", IsHostOffice = false },
        };

        Mock<IJwtMiddleware> jwt = new();
        jwt.Setup(j => j.ValidateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(caller);

        Mock<IPermissionService> permissions = new();
        permissions.Setup(p => p.CanAccessBudgetPlanningAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(true);
        permissions.Setup(p => p.CanUploadAipAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(true);
        permissions.Setup(p => p.CanManagePpdoAllocationAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(true);
        permissions.Setup(p => p.CanManagePboCeilingAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(true);
        permissions.Setup(p => p.CanManageConfigAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(true);
        permissions.Setup(p => p.CanReviewBudgetPlanningAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(departmentHeadReviewer);
        permissions.Setup(p => p.CanReviewAllOfficesAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(crossOfficeReviewer);

        object instance = Construct(type, jwt.Object, permissions.Object);

        object?[] args = BuildArguments(method);
        object? result = method.Invoke(instance, args);

        return await UnwrapAsync(result);
    }

    /// <summary>
    /// Instantiates a Function class, supplying the JWT and permission mocks by type and a bare
    /// Moq stub for every other interface dependency. Handlers short-circuit at authorization, so
    /// the service mocks are never called on the 403 path — and on the not-forbidden path a
    /// default-returning mock is enough, since only "is it 403?" is asserted.
    /// </summary>
    private static object Construct(Type type, IJwtMiddleware jwt, IPermissionService permissions)
    {
        ConstructorInfo ctor = type.GetConstructors().Single();

        object?[] args = ctor.GetParameters().Select(p =>
        {
            if (p.ParameterType == typeof(IJwtMiddleware))     return jwt;
            if (p.ParameterType == typeof(IPermissionService)) return permissions;
            return StubOf(p.ParameterType);
        }).ToArray();

        return ctor.Invoke(args);
    }

    /// <summary>
    /// A bare Moq stub for an arbitrary dependency type. Mock&lt;T&gt; has to be built
    /// reflectively here because the type is only known at runtime - the non-generic Mock is
    /// abstract, so Activator must close the generic first.
    /// </summary>
    private static object StubOf(Type dependencyType)
    {
        Type mockType = typeof(Mock<>).MakeGenericType(dependencyType);
        Mock mock = (Mock)Activator.CreateInstance(mockType)!;
        mock.DefaultValue = DefaultValue.Mock;
        return mock.Object;
    }

    private static object?[] BuildArguments(MethodInfo method)
        => method.GetParameters().Select(p =>
        {
            if (p.ParameterType == typeof(HttpRequestData))
                return FunctionHttp.Get(query: string.Empty, path: "budget-planning/write-guard-probe");
            if (p.ParameterType == typeof(CancellationToken))
                return (object?)CancellationToken.None;
            if (p.ParameterType == typeof(string))
                return "0";
            return p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null;
        }).ToArray();

    /// <summary>Awaits the handler's Task&lt;HttpResponseData&gt; without knowing its exact type.</summary>
    private static async Task<HttpResponseData> UnwrapAsync(object? result)
    {
        Task<HttpResponseData> task = Assert.IsAssignableFrom<Task<HttpResponseData>>(result);
        return await task;
    }
}
