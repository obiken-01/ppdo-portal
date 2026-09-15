using PPDO.Application.Common;
using PPDO.Domain.Entities;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="PartnerApiScope"/> (v1.8.0 — PPDO-13, build spec §3.1). Every case
/// below is one row of that table — see the class remarks for the mapping.
/// </summary>
public sealed class PartnerApiScopeTests
{
    private static Office MakeOffice(int id, string code, bool active = true) =>
        new() { Id = id, OfficeCode = code, OfficeName = code, IsActive = active };

    private static PartnerApiKey ScopedKey(params int[] officeIds) => new()
    {
        Id = 1, PartnerName = "GSO", KeyPrefix = "AbCdEfGh", KeyHash = "x",
        AllOffices = false,
        Offices = officeIds.Select(id => new PartnerApiKeyOffice { KeyId = 1, OfficeId = id }).ToList(),
    };

    private static PartnerApiKey AllOfficesKey() => new()
    {
        Id = 2, PartnerName = "PBO", KeyPrefix = "IjKlMnOp", KeyHash = "x",
        AllOffices = true, Offices = [],
    };

    [Fact]
    public void Authorize_ScopedKey_OfficeInScope_ReturnsOk()
    {
        Office ppdo = MakeOffice(1, "PPDO");

        ServiceResult<bool> result = PartnerApiScope.Authorize(ScopedKey(1), "PPDO", ppdo);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Authorize_ScopedKey_OfficeOutOfScope_ReturnsForbidden()
    {
        Office peo = MakeOffice(2, "PEO");

        ServiceResult<bool> result = PartnerApiScope.Authorize(ScopedKey(1), "PEO", peo);

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
        Assert.Contains("not authorized", result.Error);
    }

    [Fact]
    public void Authorize_ScopedKey_WholeYearRequest_ReturnsForbidden()
    {
        ServiceResult<bool> result = PartnerApiScope.Authorize(ScopedKey(1), requestedOfficeCode: null, resolvedOffice: null);

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
        Assert.Contains("request one office", result.Error);
    }

    [Fact]
    public void Authorize_AllOfficesKey_WholeYearRequest_ReturnsOk()
    {
        ServiceResult<bool> result = PartnerApiScope.Authorize(AllOfficesKey(), requestedOfficeCode: null, resolvedOffice: null);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Authorize_AllOfficesKey_UnknownOfficeCode_ReturnsBadRequest()
    {
        ServiceResult<bool> result = PartnerApiScope.Authorize(AllOfficesKey(), "NOPE", resolvedOffice: null);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("Unknown officeCode 'NOPE'", result.Error);
    }

    [Fact]
    public void Authorize_ScopedKey_UnknownOfficeCode_ReturnsForbidden_NotBadRequest()
    {
        // An unknown code is never "in scope" for a scoped key — it must read exactly like an
        // out-of-scope real office, never leaking that the code doesn't exist at all.
        ServiceResult<bool> result = PartnerApiScope.Authorize(ScopedKey(1), "NOPE", resolvedOffice: null);

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }

    [Fact]
    public void Authorize_AllOfficesKey_InactiveOffice_ReturnsForbidden()
    {
        Office inactive = MakeOffice(3, "OLD", active: false);

        ServiceResult<bool> result = PartnerApiScope.Authorize(AllOfficesKey(), "OLD", inactive);

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }

    [Fact]
    public void Authorize_ScopedKey_InactiveOfficeInScope_ReturnsForbidden()
    {
        Office inactive = MakeOffice(1, "PPDO", active: false);

        ServiceResult<bool> result = PartnerApiScope.Authorize(ScopedKey(1), "PPDO", inactive);

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }

    [Fact]
    public void Authorize_AllOfficesKey_ActiveKnownOffice_ReturnsOk()
    {
        Office ppdo = MakeOffice(1, "PPDO");

        ServiceResult<bool> result = PartnerApiScope.Authorize(AllOfficesKey(), "PPDO", ppdo);

        Assert.True(result.IsSuccess);
    }
}
