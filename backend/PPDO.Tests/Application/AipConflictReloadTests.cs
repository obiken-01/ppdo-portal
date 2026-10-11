using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Common;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// The 409's <c>Current</c> row carries the division context a successful save carries (PPDO-202).
///
/// <para>
/// "Discard mine and reload" splices <c>conflict.current</c> into the tree in place of the row. The
/// conflict path mapped it without the division context, so the row came back with no
/// <c>DivisionName</c> and <c>CanEdit = false</c>: a department head saw a CASH activity as
/// "No division", locked (found in the PPDO-195 UAT run, T8). The stored tag was intact.
/// </para>
/// </summary>
public sealed partial class AipServiceTests
{
    /// <summary>The division world with every save refused as a concurrency conflict.</summary>
    private static DivisionWorld BuildDivisionConflict()
    {
        DivisionWorld w = BuildDivisionWorld();
        w.Repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrencyConflictException(
                "The row changed since it was loaded.", new InvalidOperationException()));
        w.Repo.Setup(r => r.ReloadAsync(It.IsAny<IRowVersioned>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return w;
    }

    private static AipActivityDto ConflictCurrent(ServiceResult<AipActivityDto> result)
    {
        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
        return Assert.IsType<AipConflictDto<AipActivityDto>>(result.ErrorDetails).Current;
    }

    [Fact]
    public async Task UpdateActivityDetails_OnConflict_ByDepartmentHead_CurrentKeepsItsDivisionAndEditability()
    {
        DivisionWorld w = BuildDivisionConflict();

        AipActivityDto current = ConflictCurrent(
            await w.Sut.UpdateActivityDetailsAsync(ActPlanning, Details(), DepartmentHead()));

        Assert.Equal(DivPlanning, current.DivisionId);
        Assert.Equal("Planning Division", current.DivisionName);
        Assert.True(current.CanEdit);
    }

    [Fact]
    public async Task UpdateActivityDetails_OnConflict_ByTheDivisionsEncoder_CurrentStaysEditable()
    {
        DivisionWorld w = BuildDivisionConflict();

        AipActivityDto current = ConflictCurrent(
            await w.Sut.UpdateActivityDetailsAsync(ActPlanning, Details(), EncoderPlanning()));

        Assert.Equal("Planning Division", current.DivisionName);
        Assert.True(current.CanEdit);
    }
}
