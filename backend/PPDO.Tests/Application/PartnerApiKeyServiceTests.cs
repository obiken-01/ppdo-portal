using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="PartnerApiKeyService"/> (v1.8.0 — PPDO-15, build spec §3.2, §11).
/// Covers issuing (plaintext-once, hash-only storage, prefix collision retry, name/scope/expiry
/// validation), revoke (including the terminal revoke-twice 409), computed status, and the
/// audit_log rows on both mutations.
/// </summary>
public sealed class PartnerApiKeyServiceTests
{
    private static readonly Guid Admin = Guid.NewGuid();

    private static Office MakeOffice(int id, string code = "PPDO", bool active = true) => new()
    {
        Id = id, OfficeCode = code, OfficeName = $"{code} Office", IsActive = active,
    };

    private static PartnerApiKey MakeKey(
        int id = 1,
        string partnerName = "GSO WFP system",
        bool allOffices = true,
        DateTime? expiresAt = null,
        DateTime? revokedAt = null,
        List<PartnerApiKeyOffice>? offices = null) => new()
    {
        Id           = id,
        PartnerName  = partnerName,
        KeyPrefix    = "AbCdEfGh",
        KeyHash      = "irrelevant-for-these-tests",
        AllOffices   = allOffices,
        ExpiresAt    = expiresAt,
        RevokedAt    = revokedAt,
        CreatedAt    = DateTime.UtcNow,
        CreatedById  = Admin,
        CreatedBy    = new User { Id = Admin, FullName = "Admin User" },
        Offices      = offices ?? [],
    };

    private static (
        PartnerApiKeyService sut,
        Mock<IPartnerApiKeyRepository> keys,
        Mock<IOfficeRepository> offices,
        Mock<IAuditService> audit) Build(
            List<Office>? officeSeed = null, PartnerApiKey? refetchResult = null)
    {
        Mock<IPartnerApiKeyRepository> keys = new();
        Mock<IOfficeRepository> offices = new();
        Mock<IAuditService> audit = new();

        offices.Setup(o => o.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(officeSeed ?? []);

        keys.Setup(k => k.GetByPrefixAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PartnerApiKey?)null);
        keys.Setup(k => k.AddAsync(It.IsAny<PartnerApiKey>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        keys.Setup(k => k.UpdateAsync(It.IsAny<PartnerApiKey>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        keys.Setup(k => k.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        if (refetchResult is not null)
            keys.Setup(k => k.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(refetchResult);

        PartnerApiKeyService sut = new(
            keys.Object, offices.Object, audit.Object, NullLogger<PartnerApiKeyService>.Instance);
        return (sut, keys, offices, audit);
    }

    private static CreateApiKeyDto Dto(
        string partnerName = "GSO WFP system",
        bool allOffices = true,
        IReadOnlyList<int>? officeIds = null,
        DateOnly? expiresAt = null) => new(partnerName, allOffices, officeIds, expiresAt);

    // ── IssueAsync — success shape ───────────────────────────────────────────

    [Fact]
    public async Task IssueAsync_Valid_ReturnsPlaintextKeyOnce_AndStoresOnlyTheHash()
    {
        PartnerApiKey refetched = MakeKey();
        (PartnerApiKeyService sut, Mock<IPartnerApiKeyRepository> keys, _, _) = Build(refetchResult: refetched);

        PartnerApiKey? captured = null;
        keys.Setup(k => k.AddAsync(It.IsAny<PartnerApiKey>(), It.IsAny<CancellationToken>()))
            .Callback<PartnerApiKey, CancellationToken>((k, _) => captured = k)
            .Returns(Task.CompletedTask);

        ServiceResult<CreateApiKeyResultDto> result = await sut.IssueAsync(Admin, Dto());

        Assert.True(result.IsSuccess);
        Assert.StartsWith("ppdo_", result.Value!.PlaintextKey);
        Assert.NotNull(captured);
        // The hash stored on the entity is a function of the plaintext just returned — never the
        // plaintext itself.
        Assert.Equal(ApiKeyGenerator.Hash(result.Value.PlaintextKey), captured!.KeyHash);
        Assert.NotEqual(result.Value.PlaintextKey, captured.KeyHash);
    }

    [Fact]
    public async Task IssueAsync_PrefixCollision_RetriesUntilUnique()
    {
        PartnerApiKey refetched = MakeKey();
        (PartnerApiKeyService sut, Mock<IPartnerApiKeyRepository> keys, _, _) = Build(refetchResult: refetched);

        // First prefix is "taken"; the second attempt must succeed.
        keys.SetupSequence(k => k.GetByPrefixAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeKey(id: 99))
            .ReturnsAsync((PartnerApiKey?)null);

        ServiceResult<CreateApiKeyResultDto> result = await sut.IssueAsync(Admin, Dto());

        Assert.True(result.IsSuccess);
        keys.Verify(k => k.GetByPrefixAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task IssueAsync_AllOffices_ScopesNoOfficeRows()
    {
        PartnerApiKey refetched = MakeKey();
        (PartnerApiKeyService sut, Mock<IPartnerApiKeyRepository> keys, _, _) = Build(refetchResult: refetched);

        PartnerApiKey? captured = null;
        keys.Setup(k => k.AddAsync(It.IsAny<PartnerApiKey>(), It.IsAny<CancellationToken>()))
            .Callback<PartnerApiKey, CancellationToken>((k, _) => captured = k)
            .Returns(Task.CompletedTask);

        await sut.IssueAsync(Admin, Dto(allOffices: true, officeIds: [1, 2]));

        Assert.True(captured!.AllOffices);
        Assert.Empty(captured.Offices);
    }

    [Fact]
    public async Task IssueAsync_ScopedOffices_AttachesEachOffice()
    {
        List<Office> offices = [MakeOffice(1, "PPDO"), MakeOffice(2, "PEO")];
        PartnerApiKey refetched = MakeKey();
        (PartnerApiKeyService sut, Mock<IPartnerApiKeyRepository> keys, _, _) =
            Build(officeSeed: offices, refetchResult: refetched);

        PartnerApiKey? captured = null;
        keys.Setup(k => k.AddAsync(It.IsAny<PartnerApiKey>(), It.IsAny<CancellationToken>()))
            .Callback<PartnerApiKey, CancellationToken>((k, _) => captured = k)
            .Returns(Task.CompletedTask);

        ServiceResult<CreateApiKeyResultDto> result =
            await sut.IssueAsync(Admin, Dto(allOffices: false, officeIds: [1, 2]));

        Assert.True(result.IsSuccess);
        Assert.False(captured!.AllOffices);
        Assert.Equal(2, captured.Offices.Count);
    }

    // ── IssueAsync — validation (build spec §3.2) ────────────────────────────

    [Fact]
    public async Task IssueAsync_NoOfficeAndNotAllOffices_ReturnsBadRequest()
    {
        (PartnerApiKeyService sut, _, _, _) = Build();

        ServiceResult<CreateApiKeyResultDto> result =
            await sut.IssueAsync(Admin, Dto(allOffices: false, officeIds: []));

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("Choose at least one office", result.Error);
    }

    [Fact]
    public async Task IssueAsync_NullOfficeIdsAndNotAllOffices_ReturnsBadRequest()
    {
        (PartnerApiKeyService sut, _, _, _) = Build();

        ServiceResult<CreateApiKeyResultDto> result =
            await sut.IssueAsync(Admin, Dto(allOffices: false, officeIds: null));

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task IssueAsync_PastExpiry_ReturnsBadRequest()
    {
        (PartnerApiKeyService sut, _, _, _) = Build();

        ServiceResult<CreateApiKeyResultDto> result =
            await sut.IssueAsync(Admin, Dto(expiresAt: new DateOnly(2020, 1, 1)));

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("future date", result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task IssueAsync_BlankPartnerName_ReturnsBadRequest(string name)
    {
        (PartnerApiKeyService sut, _, _, _) = Build();

        ServiceResult<CreateApiKeyResultDto> result = await sut.IssueAsync(Admin, Dto(partnerName: name));

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("Partner name is required", result.Error);
    }

    [Fact]
    public async Task IssueAsync_PartnerNameOver100Characters_ReturnsBadRequest()
    {
        (PartnerApiKeyService sut, _, _, _) = Build();

        ServiceResult<CreateApiKeyResultDto> result =
            await sut.IssueAsync(Admin, Dto(partnerName: new string('x', 101)));

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task IssueAsync_UnknownOfficeId_ReturnsBadRequest()
    {
        (PartnerApiKeyService sut, _, _, _) = Build(officeSeed: [MakeOffice(1)]);

        ServiceResult<CreateApiKeyResultDto> result =
            await sut.IssueAsync(Admin, Dto(allOffices: false, officeIds: [999]));

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("not found", result.Error);
    }

    [Fact]
    public async Task IssueAsync_InactiveOffice_ReturnsBadRequest()
    {
        (PartnerApiKeyService sut, _, _, _) = Build(officeSeed: [MakeOffice(1, active: false)]);

        ServiceResult<CreateApiKeyResultDto> result =
            await sut.IssueAsync(Admin, Dto(allOffices: false, officeIds: [1]));

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("inactive", result.Error);
    }

    [Fact]
    public async Task IssueAsync_Valid_LogsAuditCreate_WithoutKeyHash()
    {
        PartnerApiKey refetched = MakeKey(id: 7);
        (PartnerApiKeyService sut, _, _, Mock<IAuditService> audit) = Build(refetchResult: refetched);

        await sut.IssueAsync(Admin, Dto());

        audit.Verify(a => a.LogAsync(
            "partner_api_keys",
            refetched.Id,
            AuditAction.Create,
            null,
            It.Is<object>(v => v.GetType().GetProperty("KeyHash") == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── RevokeAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RevokeAsync_ActiveKey_SetsRevokedAtAndRevokedBy()
    {
        PartnerApiKey active = MakeKey();
        (PartnerApiKeyService sut, Mock<IPartnerApiKeyRepository> keys, _, _) = Build();
        keys.Setup(k => k.GetByIdAsync(active.Id, It.IsAny<CancellationToken>())).ReturnsAsync(active);

        ServiceResult<ApiKeyListItemDto> result = await sut.RevokeAsync(Admin, active.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal("Revoked", result.Value!.Status);
        keys.Verify(k => k.UpdateAsync(
            It.Is<PartnerApiKey>(k => k.RevokedAt != null && k.RevokedById == Admin),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevokeAsync_AlreadyRevoked_ReturnsConflict()
    {
        PartnerApiKey revoked = MakeKey(revokedAt: DateTime.UtcNow.AddDays(-1));
        (PartnerApiKeyService sut, Mock<IPartnerApiKeyRepository> keys, _, _) = Build();
        keys.Setup(k => k.GetByIdAsync(revoked.Id, It.IsAny<CancellationToken>())).ReturnsAsync(revoked);

        ServiceResult<ApiKeyListItemDto> result = await sut.RevokeAsync(Admin, revoked.Id);

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
        Assert.Contains("already revoked", result.Error);
        keys.Verify(k => k.UpdateAsync(It.IsAny<PartnerApiKey>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RevokeAsync_UnknownId_ReturnsNotFound()
    {
        (PartnerApiKeyService sut, Mock<IPartnerApiKeyRepository> keys, _, _) = Build();
        keys.Setup(k => k.GetByIdAsync(404, It.IsAny<CancellationToken>())).ReturnsAsync((PartnerApiKey?)null);

        ServiceResult<ApiKeyListItemDto> result = await sut.RevokeAsync(Admin, 404);

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task RevokeAsync_Success_LogsAuditUpdate()
    {
        PartnerApiKey active = MakeKey(id: 3);
        (PartnerApiKeyService sut, Mock<IPartnerApiKeyRepository> keys, _, Mock<IAuditService> audit) = Build();
        keys.Setup(k => k.GetByIdAsync(3, It.IsAny<CancellationToken>())).ReturnsAsync(active);

        await sut.RevokeAsync(Admin, 3);

        audit.Verify(a => a.LogAsync(
            "partner_api_keys", 3, AuditAction.Update,
            It.IsAny<object>(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Computed status (GetAllAsync mapping) ────────────────────────────────

    [Fact]
    public async Task GetAllAsync_MapsComputedStatus_ActiveExpiredRevoked()
    {
        List<PartnerApiKey> seed =
        [
            MakeKey(id: 1),
            MakeKey(id: 2, expiresAt: DateTime.UtcNow.AddDays(-1)),
            MakeKey(id: 3, revokedAt: DateTime.UtcNow.AddMinutes(-5)),
        ];
        (PartnerApiKeyService sut, Mock<IPartnerApiKeyRepository> keys, _, _) = Build();
        keys.Setup(k => k.GetAllWithOfficesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(seed);

        IReadOnlyList<ApiKeyListItemDto> result = await sut.GetAllAsync();

        Assert.Equal("Active", result.Single(r => r.Id == 1).Status);
        Assert.Equal("Expired", result.Single(r => r.Id == 2).Status);
        Assert.Equal("Revoked", result.Single(r => r.Id == 3).Status);
    }

    [Fact]
    public async Task GetAllAsync_NeverExposesKeyHash()
    {
        (PartnerApiKeyService sut, Mock<IPartnerApiKeyRepository> keys, _, _) = Build();
        keys.Setup(k => k.GetAllWithOfficesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([MakeKey()]);

        IReadOnlyList<ApiKeyListItemDto> result = await sut.GetAllAsync();

        Assert.DoesNotContain("KeyHash", typeof(ApiKeyListItemDto).GetProperties().Select(p => p.Name));
        Assert.Single(result);
    }
}
