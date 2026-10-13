using System.Reflection;
using Microsoft.Azure.Functions.Worker;
using PPDO.Functions.Functions;

namespace PPDO.Tests.Functions;

/// <summary>
/// Pins PPDO-199: the module-wiping cleanup endpoints exist in Debug builds only.
///
/// <c>PPDO.Functions.csproj</c> removes their files from every non-Debug build, so the Release
/// publish CI deploys to UAT and production carries no such function — no app setting can switch
/// them on there. These tests read the configuration the Functions assembly was actually built in,
/// so CI (which builds and tests in Release) proves the endpoints are absent, and a local Debug run
/// proves the exclusion did not also remove them from local live-testing.
/// </summary>
public sealed class DevOnlyEndpointsTests
{
    private static readonly string[] DevOnlyTypeNames =
    [
        "PPDO.Functions.Functions.InventoryCleanupFunctions",
        "PPDO.Functions.Functions.BudgetPlanningCleanupFunctions",
    ];

    private static Assembly FunctionsAssembly => typeof(HealthFunctions).Assembly;

    private static bool BuiltInDebug =>
        FunctionsAssembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration == "Debug";

    /// <summary>Every [Function]'s HTTP route in the built assembly.</summary>
    private static IEnumerable<string> Routes() =>
        FunctionsAssembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Where(m => m.GetCustomAttribute<FunctionAttribute>() is not null)
            .SelectMany(m => m.GetParameters())
            .Select(p => p.GetCustomAttribute<HttpTriggerAttribute>()?.Route)
            .OfType<string>();

    [Fact]
    public void CleanupFunctions_ExistOnlyInDebugBuilds()
    {
        foreach (string typeName in DevOnlyTypeNames)
        {
            bool present = FunctionsAssembly.GetType(typeName) is not null;

            Assert.True(present == BuiltInDebug,
                $"{typeName} is {(present ? "present" : "absent")} in a " +
                $"{(BuiltInDebug ? "Debug" : "non-Debug")} build. It must exist in Debug only — " +
                "see the Compile Remove in PPDO.Functions.csproj.");
        }
    }

    [Fact]
    public void NonDebugBuild_ExposesNoCleanupRoute()
    {
        if (BuiltInDebug) return;   // Debug keeps them on purpose; the test above covers that side.

        string[] cleanupRoutes = Routes().Where(r => r.Contains("cleanup", StringComparison.OrdinalIgnoreCase)).ToArray();

        Assert.True(cleanupRoutes.Length == 0,
            $"Cleanup routes in a non-Debug build: {string.Join(", ", cleanupRoutes)}");
    }
}
