using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Dashboard;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="DashboardService"/> — written first (TDD).
/// ICalendarEventRepository, IHolidayProvider, IRepository&lt;PurchaseRequest&gt;,
/// and IRepository&lt;ItemMaster&gt; are all mocked.
/// </summary>
public sealed class DashboardServiceTests
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    private static User MakeUser(UserRole role = UserRole.Staff) => new()
    {
        Id       = Guid.NewGuid(),
        FullName = "Test User",
        Email    = "test@ppdo.gov.ph",
        PasswordHash = "hash",
        Role     = role,
        DivisionId = null,
        IsActive = true,
    };

    private static User AdminUser() => MakeUser(UserRole.Admin);
    private static User SuperAdminUser() => MakeUser(UserRole.SuperAdmin);

    /// <summary>Approved Office event (pre-existing / visible to all).</summary>
    private static CalendarEvent MakeOfficeEvent(DateTime start, Guid? createdById = null) => new()
    {
        Id          = Guid.NewGuid(),
        Title       = "Office Meeting",
        StartDate   = start,
        IsAllDay    = false,
        EventType   = "Office",
        Status      = CalendarEventStatus.Approved,   // approved = visible to all
        CreatedById = createdById ?? Guid.NewGuid(),
        CreatedAt   = DateTime.UtcNow,
    };

    private static CalendarEvent MakePersonalEvent(DateTime start, Guid userId) => new()
    {
        Id          = Guid.NewGuid(),
        Title       = "Personal Appointment",
        StartDate   = start,
        IsAllDay    = false,
        EventType   = "Personal",
        Status      = CalendarEventStatus.Approved,
        CreatedById = userId,
        CreatedAt   = DateTime.UtcNow,
    };

    private static CalendarEvent MakePendingOfficeEvent(DateTime start, Guid createdById) => new()
    {
        Id          = Guid.NewGuid(),
        Title       = "Pending Office Event",
        StartDate   = start,
        IsAllDay    = false,
        EventType   = "Office",
        Status      = CalendarEventStatus.Pending,
        CreatedById = createdById,
        CreatedAt   = DateTime.UtcNow,
        CreatedBy   = new User { FullName = "Staff User", Username = "staff", PasswordHash = "x", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
    };

    private DashboardService BuildSut(
        Mock<ICalendarEventRepository>   eventRepo,
        Mock<IHolidayProvider>           holidays,
        Mock<IRepository<PurchaseRequest>> prRepo,
        Mock<IRepository<ItemMaster>>    itemRepo)
        => new(eventRepo.Object, holidays.Object, prRepo.Object, itemRepo.Object);

    private static (Mock<ICalendarEventRepository> eventRepo, Mock<IHolidayProvider> holidays,
        Mock<IRepository<PurchaseRequest>> prRepo, Mock<IRepository<ItemMaster>> itemRepo) EmptyMocks()
    {
        Mock<ICalendarEventRepository>     eventRepo = new();
        Mock<IHolidayProvider>             holidays  = new();
        Mock<IRepository<PurchaseRequest>> prRepo    = new();
        Mock<IRepository<ItemMaster>>      itemRepo  = new();

        eventRepo.Setup(r => r.GetByDateRangeAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        eventRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        eventRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);
        eventRepo.Setup(r => r.AddAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        eventRepo.Setup(r => r.UpdateAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        eventRepo.Setup(r => r.DeleteAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        eventRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        holidays.Setup(h => h.GetPhHolidaysAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        return (eventRepo, holidays, prRepo, itemRepo);
    }

    // ── GetEventsAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetEventsAsync_ReturnsMergedOfficePersonalAndHolidays()
    {
        User user = MakeUser();
        DateTime start = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        CalendarEvent officeEvent   = MakeOfficeEvent(start.AddDays(5));
        CalendarEvent personalEvent = MakePersonalEvent(start.AddDays(10), user.Id);

        CalendarEventDto holiday = new(null, "Independence Day", null,
            new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc), null, true, "Holiday", "Static", null, null);

        var (eventRepo, holidays, prRepo, itemRepo) = EmptyMocks();

        eventRepo.Setup(r => r.GetByDateRangeAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([officeEvent, personalEvent]);

        holidays.Setup(h => h.GetPhHolidaysAsync(2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync([holiday]);

        IReadOnlyList<CalendarEventDto> result =
            await BuildSut(eventRepo, holidays, prRepo, itemRepo)
                .GetEventsAsync(2026, 6, user.Id);

        Assert.Equal(3, result.Count);
        Assert.Contains(result, e => e.EventType == "Office");
        Assert.Contains(result, e => e.EventType == "Personal");
        Assert.Contains(result, e => e.EventType == "Holiday");
    }

    [Fact]
    public async Task GetEventsAsync_FiltersHolidaysToRequestedMonth()
    {
        User user = MakeUser();

        CalendarEventDto juneHoliday = new(null, "Independence Day", null,
            new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc), null, true, "Holiday", "Static", null, null);
        CalendarEventDto julyHoliday = new(null, "Ninoy Aquino Day", null,
            new DateTime(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc), null, true, "Holiday", "Static", null, null);

        var (eventRepo, holidays, prRepo, itemRepo) = EmptyMocks();

        holidays.Setup(h => h.GetPhHolidaysAsync(2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync([juneHoliday, julyHoliday]);

        IReadOnlyList<CalendarEventDto> result =
            await BuildSut(eventRepo, holidays, prRepo, itemRepo)
                .GetEventsAsync(2026, 6, user.Id);

        Assert.Single(result);
        Assert.Equal("Independence Day", result[0].Title);
    }

    [Fact]
    public async Task GetEventsAsync_WhenHolidayProviderFails_StillReturnsDbEvents()
    {
        User user = MakeUser();
        CalendarEvent officeEvent = MakeOfficeEvent(new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc));

        var (eventRepo, holidays, prRepo, itemRepo) = EmptyMocks();

        eventRepo.Setup(r => r.GetByDateRangeAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([officeEvent]);

        holidays.Setup(h => h.GetPhHolidaysAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Nager.Date unavailable"));

        IReadOnlyList<CalendarEventDto> result =
            await BuildSut(eventRepo, holidays, prRepo, itemRepo)
                .GetEventsAsync(2026, 6, user.Id);

        Assert.Single(result);
        Assert.Equal("Office Meeting", result[0].Title);
    }

    [Fact]
    public async Task GetEventsAsync_PendingOfficeByOtherUser_NotVisible()
    {
        User user = MakeUser();
        Guid otherId = Guid.NewGuid();
        CalendarEvent pending = MakePendingOfficeEvent(new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc), otherId);

        var (eventRepo, holidays, prRepo, itemRepo) = EmptyMocks();
        eventRepo.Setup(r => r.GetByDateRangeAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([pending]);

        IReadOnlyList<CalendarEventDto> result =
            await BuildSut(eventRepo, holidays, prRepo, itemRepo)
                .GetEventsAsync(2026, 6, user.Id);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetEventsAsync_OwnPendingOfficeEvent_IsVisible()
    {
        User user = MakeUser();
        CalendarEvent pending = MakePendingOfficeEvent(new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc), user.Id);

        var (eventRepo, holidays, prRepo, itemRepo) = EmptyMocks();
        eventRepo.Setup(r => r.GetByDateRangeAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([pending]);

        IReadOnlyList<CalendarEventDto> result =
            await BuildSut(eventRepo, holidays, prRepo, itemRepo)
                .GetEventsAsync(2026, 6, user.Id);

        Assert.Single(result);
        Assert.Equal(CalendarEventStatus.Pending, result[0].Status);
    }

    // ── CreateEventAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreateEventAsync_WithValidOfficeEvent_ReturnsOk()
    {
        User user = MakeUser();
        CreateCalendarEventDto dto = new(
            "Team Meeting", null,
            new DateTime(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc), null,
            false, "Office");

        var (eventRepo, holidays, prRepo, itemRepo) = EmptyMocks();

        ServiceResult<CalendarEventDto> result =
            await BuildSut(eventRepo, holidays, prRepo, itemRepo)
                .CreateEventAsync(user, dto);

        Assert.True(result.IsSuccess);
        Assert.Equal("Team Meeting", result.Value!.Title);
        Assert.Equal("Office", result.Value.EventType);
    }

    [Fact]
    public async Task CreateEventAsync_NonAdmin_OfficeEvent_SetsPending()
    {
        User staff = MakeUser(UserRole.Staff);
        CreateCalendarEventDto dto = new(
            "Team Meeting", null,
            new DateTime(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc), null,
            false, "Office");

        CalendarEvent? saved = null;
        var (eventRepo, holidays, prRepo, itemRepo) = EmptyMocks();
        eventRepo.Setup(r => r.AddAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .Callback<CalendarEvent, CancellationToken>((e, _) => saved = e)
            .Returns(Task.CompletedTask);

        await BuildSut(eventRepo, holidays, prRepo, itemRepo).CreateEventAsync(staff, dto);

        Assert.NotNull(saved);
        Assert.Equal(CalendarEventStatus.Pending, saved!.Status);
    }

    [Fact]
    public async Task CreateEventAsync_Admin_OfficeEvent_SetsApproved()
    {
        User admin = AdminUser();
        CreateCalendarEventDto dto = new(
            "Admin Event", null,
            new DateTime(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc), null,
            false, "Office");

        CalendarEvent? saved = null;
        var (eventRepo, holidays, prRepo, itemRepo) = EmptyMocks();
        eventRepo.Setup(r => r.AddAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .Callback<CalendarEvent, CancellationToken>((e, _) => saved = e)
            .Returns(Task.CompletedTask);

        await BuildSut(eventRepo, holidays, prRepo, itemRepo).CreateEventAsync(admin, dto);

        Assert.NotNull(saved);
        Assert.Equal(CalendarEventStatus.Approved, saved!.Status);
    }

    [Fact]
    public async Task CreateEventAsync_PersonalEvent_SetsCreatorId()
    {
        // Personal events bypass approval — status irrelevant (not set by create)
        User staff = MakeUser(UserRole.Staff);
        CreateCalendarEventDto dto = new(
            "Doctor Appointment", null,
            new DateTime(2026, 6, 20, 8, 0, 0, DateTimeKind.Utc), null,
            false, "Personal");

        CalendarEvent? saved = null;
        var (eventRepo, holidays, prRepo, itemRepo) = EmptyMocks();
        eventRepo.Setup(r => r.AddAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .Callback<CalendarEvent, CancellationToken>((e, _) => saved = e)
            .Returns(Task.CompletedTask);

        await BuildSut(eventRepo, holidays, prRepo, itemRepo).CreateEventAsync(staff, dto);

        Assert.NotNull(saved);
        Assert.Equal(staff.Id, saved!.CreatedById);
    }

    [Fact]
    public async Task CreateEventAsync_WithInvalidEventType_ReturnsBadRequest()
    {
        User user = MakeUser();
        CreateCalendarEventDto dto = new(
            "Unknown", null,
            new DateTime(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc), null,
            false, "Holiday"); // "Holiday" is not a valid create type

        var (eventRepo, holidays, prRepo, itemRepo) = EmptyMocks();

        ServiceResult<CalendarEventDto> result =
            await BuildSut(eventRepo, holidays, prRepo, itemRepo)
                .CreateEventAsync(user, dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task CreateEventAsync_WithEmptyTitle_ReturnsBadRequest()
    {
        User user = MakeUser();
        CreateCalendarEventDto dto = new(
            "", null,
            new DateTime(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc), null,
            false, "Office");

        var (eventRepo, holidays, prRepo, itemRepo) = EmptyMocks();

        ServiceResult<CalendarEventDto> result =
            await BuildSut(eventRepo, holidays, prRepo, itemRepo)
                .CreateEventAsync(user, dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    // ── GetPendingEventsAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetPendingEventsAsync_Admin_ReturnsPendingOrderedByCreatedAt()
    {
        User admin = AdminUser();
        DateTime older = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime newer = new(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc);

        Guid authorId = Guid.NewGuid();
        CalendarEvent p1 = MakePendingOfficeEvent(older, authorId);
        p1.CreatedAt = older;
        CalendarEvent p2 = MakePendingOfficeEvent(newer, authorId);
        p2.CreatedAt = newer;
        CalendarEvent approved = MakeOfficeEvent(older);  // Approved — should NOT appear

        var (eventRepo, holidays, prRepo, itemRepo) = EmptyMocks();
        eventRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([p2, approved, p1]);  // out of order — service must sort

        ServiceResult<IReadOnlyList<PendingCalendarEventDto>> result =
            await BuildSut(eventRepo, holidays, prRepo, itemRepo)
                .GetPendingEventsAsync(admin);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.Equal(p1.StartDate, result.Value[0].StartDate);  // older first
    }

    [Fact]
    public async Task GetPendingEventsAsync_NonAdmin_ReturnsForbidden()
    {
        User staff = MakeUser(UserRole.Staff);
        var (eventRepo, holidays, prRepo, itemRepo) = EmptyMocks();

        ServiceResult<IReadOnlyList<PendingCalendarEventDto>> result =
            await BuildSut(eventRepo, holidays, prRepo, itemRepo)
                .GetPendingEventsAsync(staff);

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }

    // ── ReviewEventAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task ReviewEventAsync_Approve_SetsApprovedAndReviewedFields()
    {
        User admin = AdminUser();
        CalendarEvent pending = MakePendingOfficeEvent(DateTime.UtcNow, Guid.NewGuid());

        var (eventRepo, holidays, prRepo, itemRepo) = EmptyMocks();
        eventRepo.Setup(r => r.GetByIdAsync(pending.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pending);

        ServiceResult<CalendarEventDto> result =
            await BuildSut(eventRepo, holidays, prRepo, itemRepo)
                .ReviewEventAsync(admin, pending.Id, new ReviewCalendarEventDto(true, null));

        Assert.True(result.IsSuccess);
        Assert.Equal(CalendarEventStatus.Approved, result.Value!.Status);
        Assert.Equal(admin.Id, pending.ReviewedById);
        Assert.NotNull(pending.ReviewedAt);
        Assert.Null(pending.RejectionReason);
    }

    [Fact]
    public async Task ReviewEventAsync_Reject_SetsRejectedAndStoresReason()
    {
        User admin = AdminUser();
        CalendarEvent pending = MakePendingOfficeEvent(DateTime.UtcNow, Guid.NewGuid());

        var (eventRepo, holidays, prRepo, itemRepo) = EmptyMocks();
        eventRepo.Setup(r => r.GetByIdAsync(pending.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pending);

        ServiceResult<CalendarEventDto> result =
            await BuildSut(eventRepo, holidays, prRepo, itemRepo)
                .ReviewEventAsync(admin, pending.Id, new ReviewCalendarEventDto(false, "Event conflicts with another."));

        Assert.True(result.IsSuccess);
        Assert.Equal(CalendarEventStatus.Rejected, result.Value!.Status);
        Assert.Equal("Event conflicts with another.", pending.RejectionReason);
        Assert.Equal(admin.Id, pending.ReviewedById);
    }

    [Fact]
    public async Task ReviewEventAsync_Reject_WithoutReason_ReturnsBadRequest()
    {
        User admin = AdminUser();
        CalendarEvent pending = MakePendingOfficeEvent(DateTime.UtcNow, Guid.NewGuid());

        var (eventRepo, holidays, prRepo, itemRepo) = EmptyMocks();
        eventRepo.Setup(r => r.GetByIdAsync(pending.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pending);

        ServiceResult<CalendarEventDto> result =
            await BuildSut(eventRepo, holidays, prRepo, itemRepo)
                .ReviewEventAsync(admin, pending.Id, new ReviewCalendarEventDto(false, null));

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task ReviewEventAsync_NonAdmin_ReturnsForbidden()
    {
        User staff = MakeUser(UserRole.Staff);
        var (eventRepo, holidays, prRepo, itemRepo) = EmptyMocks();

        ServiceResult<CalendarEventDto> result =
            await BuildSut(eventRepo, holidays, prRepo, itemRepo)
                .ReviewEventAsync(staff, Guid.NewGuid(), new ReviewCalendarEventDto(true, null));

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }

    [Fact]
    public async Task ReviewEventAsync_NotFound_ReturnsNotFound()
    {
        User admin = AdminUser();
        var (eventRepo, holidays, prRepo, itemRepo) = EmptyMocks();
        // GetByIdAsync returns null (default from EmptyMocks)

        ServiceResult<CalendarEventDto> result =
            await BuildSut(eventRepo, holidays, prRepo, itemRepo)
                .ReviewEventAsync(admin, Guid.NewGuid(), new ReviewCalendarEventDto(true, null));

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    // ── DeleteEventAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteEventAsync_Creator_CanDeleteOwnEvent()
    {
        User user = MakeUser();
        CalendarEvent ev = MakePendingOfficeEvent(DateTime.UtcNow, user.Id);

        CalendarEvent? deleted = null;
        var (eventRepo, holidays, prRepo, itemRepo) = EmptyMocks();
        eventRepo.Setup(r => r.GetByIdAsync(ev.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ev);
        eventRepo.Setup(r => r.DeleteAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .Callback<CalendarEvent, CancellationToken>((e, _) => deleted = e)
            .Returns(Task.CompletedTask);

        ServiceResult<bool> result =
            await BuildSut(eventRepo, holidays, prRepo, itemRepo)
                .DeleteEventAsync(user, ev.Id);

        Assert.True(result.IsSuccess);
        Assert.NotNull(deleted);
    }

    [Fact]
    public async Task DeleteEventAsync_Admin_NonOwner_ReturnsForbidden()
    {
        // RAL-168: delete became owner-only, same rule as UpdateEventAsync -- no admin override.
        User admin = AdminUser();
        CalendarEvent ev = MakePendingOfficeEvent(DateTime.UtcNow, Guid.NewGuid()); // different creator

        var (eventRepo, holidays, prRepo, itemRepo) = EmptyMocks();
        eventRepo.Setup(r => r.GetByIdAsync(ev.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ev);

        ServiceResult<bool> result =
            await BuildSut(eventRepo, holidays, prRepo, itemRepo)
                .DeleteEventAsync(admin, ev.Id);

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }

    [Fact]
    public async Task DeleteEventAsync_OtherUser_ReturnsForbidden()
    {
        User user = MakeUser();
        User other = MakeUser();
        CalendarEvent ev = MakePendingOfficeEvent(DateTime.UtcNow, other.Id); // created by 'other'

        var (eventRepo, holidays, prRepo, itemRepo) = EmptyMocks();
        eventRepo.Setup(r => r.GetByIdAsync(ev.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ev);

        ServiceResult<bool> result =
            await BuildSut(eventRepo, holidays, prRepo, itemRepo)
                .DeleteEventAsync(user, ev.Id);

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }

    [Fact]
    public async Task DeleteEventAsync_NotFound_ReturnsNotFound()
    {
        User user = MakeUser();
        var (eventRepo, holidays, prRepo, itemRepo) = EmptyMocks();
        // GetByIdAsync returns null (default from EmptyMocks)

        ServiceResult<bool> result =
            await BuildSut(eventRepo, holidays, prRepo, itemRepo)
                .DeleteEventAsync(user, Guid.NewGuid());

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    // ── UpdateEventAsync (RAL-168) ────────────────────────────────────────────

    [Fact]
    public async Task UpdateEventAsync_Owner_CanEditOwnEvent()
    {
        User user = MakeUser();
        CalendarEvent ev = MakePersonalEvent(DateTime.UtcNow, user.Id);
        UpdateCalendarEventDto dto = new("Updated Title", "Updated desc",
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), null, true);

        var (eventRepo, holidays, prRepo, itemRepo) = EmptyMocks();
        eventRepo.Setup(r => r.GetByIdAsync(ev.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ev);

        ServiceResult<CalendarEventDto> result =
            await BuildSut(eventRepo, holidays, prRepo, itemRepo)
                .UpdateEventAsync(user, ev.Id, dto);

        Assert.True(result.IsSuccess);
        Assert.Equal("Updated Title", result.Value!.Title);
        Assert.Equal("Updated desc", ev.Description);
    }

    [Fact]
    public async Task UpdateEventAsync_NonOwner_ReturnsForbidden_EvenForAdmin()
    {
        // Deliberate difference from DeleteEventAsync: no admin override.
        User admin = AdminUser();
        CalendarEvent ev = MakePersonalEvent(DateTime.UtcNow, Guid.NewGuid()); // different creator
        UpdateCalendarEventDto dto = new("Hijacked", null, DateTime.UtcNow, null, true);

        var (eventRepo, holidays, prRepo, itemRepo) = EmptyMocks();
        eventRepo.Setup(r => r.GetByIdAsync(ev.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ev);

        ServiceResult<CalendarEventDto> result =
            await BuildSut(eventRepo, holidays, prRepo, itemRepo)
                .UpdateEventAsync(admin, ev.Id, dto);

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }

    [Fact]
    public async Task UpdateEventAsync_NotFound_ReturnsNotFound()
    {
        User user = MakeUser();
        UpdateCalendarEventDto dto = new("Title", null, DateTime.UtcNow, null, true);
        var (eventRepo, holidays, prRepo, itemRepo) = EmptyMocks();
        // GetByIdAsync returns null (default from EmptyMocks)

        ServiceResult<CalendarEventDto> result =
            await BuildSut(eventRepo, holidays, prRepo, itemRepo)
                .UpdateEventAsync(user, Guid.NewGuid(), dto);

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task UpdateEventAsync_EmptyTitle_ReturnsBadRequest()
    {
        User user = MakeUser();
        UpdateCalendarEventDto dto = new("   ", null, DateTime.UtcNow, null, true);
        var (eventRepo, holidays, prRepo, itemRepo) = EmptyMocks();

        ServiceResult<CalendarEventDto> result =
            await BuildSut(eventRepo, holidays, prRepo, itemRepo)
                .UpdateEventAsync(user, Guid.NewGuid(), dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task UpdateEventAsync_NonAdminOwner_ApprovedOfficeEvent_ResetsToPending()
    {
        User staff = MakeUser(UserRole.Staff);
        CalendarEvent ev = MakeOfficeEvent(DateTime.UtcNow, staff.Id); // Approved by construction
        ev.ReviewedById = Guid.NewGuid();
        ev.ReviewedAt   = DateTime.UtcNow;
        UpdateCalendarEventDto dto = new("Revised Title", null, DateTime.UtcNow, null, true);

        var (eventRepo, holidays, prRepo, itemRepo) = EmptyMocks();
        eventRepo.Setup(r => r.GetByIdAsync(ev.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ev);

        ServiceResult<CalendarEventDto> result =
            await BuildSut(eventRepo, holidays, prRepo, itemRepo)
                .UpdateEventAsync(staff, ev.Id, dto);

        Assert.True(result.IsSuccess);
        Assert.Equal(CalendarEventStatus.Pending, result.Value!.Status);
        Assert.Null(ev.ReviewedById);
        Assert.Null(ev.ReviewedAt);
    }

    [Fact]
    public async Task UpdateEventAsync_NonAdminOwner_RejectedOfficeEvent_ResetsToPending()
    {
        User staff = MakeUser(UserRole.Staff);
        CalendarEvent ev = MakePendingOfficeEvent(DateTime.UtcNow, staff.Id);
        ev.Status          = CalendarEventStatus.Rejected;
        ev.RejectionReason = "Conflicts with another event.";
        UpdateCalendarEventDto dto = new("Fixed Title", null, DateTime.UtcNow, null, true);

        var (eventRepo, holidays, prRepo, itemRepo) = EmptyMocks();
        eventRepo.Setup(r => r.GetByIdAsync(ev.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ev);

        ServiceResult<CalendarEventDto> result =
            await BuildSut(eventRepo, holidays, prRepo, itemRepo)
                .UpdateEventAsync(staff, ev.Id, dto);

        Assert.True(result.IsSuccess);
        Assert.Equal(CalendarEventStatus.Pending, result.Value!.Status);
        Assert.Null(ev.RejectionReason);
    }

    [Fact]
    public async Task UpdateEventAsync_AdminOwner_ApprovedOfficeEvent_StaysApproved()
    {
        User admin = AdminUser();
        CalendarEvent ev = MakeOfficeEvent(DateTime.UtcNow, admin.Id);
        UpdateCalendarEventDto dto = new("Admin Revised Title", null, DateTime.UtcNow, null, true);

        var (eventRepo, holidays, prRepo, itemRepo) = EmptyMocks();
        eventRepo.Setup(r => r.GetByIdAsync(ev.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ev);

        ServiceResult<CalendarEventDto> result =
            await BuildSut(eventRepo, holidays, prRepo, itemRepo)
                .UpdateEventAsync(admin, ev.Id, dto);

        Assert.True(result.IsSuccess);
        Assert.Equal(CalendarEventStatus.Approved, result.Value!.Status);
    }

    [Fact]
    public async Task UpdateEventAsync_PersonalEvent_NeverResetsStatus()
    {
        User user = MakeUser(UserRole.Staff);
        CalendarEvent ev = MakePersonalEvent(DateTime.UtcNow, user.Id); // Approved by construction
        UpdateCalendarEventDto dto = new("Renamed Appointment", null, DateTime.UtcNow, null, true);

        var (eventRepo, holidays, prRepo, itemRepo) = EmptyMocks();
        eventRepo.Setup(r => r.GetByIdAsync(ev.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ev);

        ServiceResult<CalendarEventDto> result =
            await BuildSut(eventRepo, holidays, prRepo, itemRepo)
                .UpdateEventAsync(user, ev.Id, dto);

        Assert.True(result.IsSuccess);
        Assert.Equal(CalendarEventStatus.Approved, result.Value!.Status);
    }

    [Fact]
    public async Task UpdateEventAsync_GetEventsAsync_IncludesCreatedById()
    {
        User user = MakeUser();
        CalendarEvent ev = MakeOfficeEvent(new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc), user.Id);

        var (eventRepo, holidays, prRepo, itemRepo) = EmptyMocks();
        eventRepo.Setup(r => r.GetByDateRangeAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([ev]);

        IReadOnlyList<CalendarEventDto> result =
            await BuildSut(eventRepo, holidays, prRepo, itemRepo)
                .GetEventsAsync(2026, 6, user.Id);

        Assert.Single(result);
        Assert.Equal(user.Id, result[0].CreatedById);
    }

    // ── GetStatsAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetStatsAsync_ReturnsPRCountsByStatus()
    {
        List<PurchaseRequest> prs =
        [
            new() { Id = Guid.NewGuid(), Status = PRStatus.Open },
            new() { Id = Guid.NewGuid(), Status = PRStatus.Open },
            new() { Id = Guid.NewGuid(), Status = PRStatus.PartiallyDelivered },
            new() { Id = Guid.NewGuid(), Status = PRStatus.FullyDelivered },
        ];

        var (eventRepo, holidays, prRepo, itemRepo) = EmptyMocks();
        prRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(prs);
        itemRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ItemMaster>());

        DashboardStatsDto stats =
            await BuildSut(eventRepo, holidays, prRepo, itemRepo).GetStatsAsync();

        Assert.Equal(4, stats.TotalPRs);
        Assert.Equal(2, stats.OpenPRs);
        Assert.Equal(1, stats.PartiallyDeliveredPRs);
        Assert.Equal(1, stats.FullyDeliveredPRs);
    }

    [Fact]
    public async Task GetStatsAsync_ReturnsItemCounts()
    {
        List<ItemMaster> items =
        [
            new() { Id = Guid.NewGuid(), IsNewItem = false },
            new() { Id = Guid.NewGuid(), IsNewItem = false },
            new() { Id = Guid.NewGuid(), IsNewItem = true },
        ];

        var (eventRepo, holidays, prRepo, itemRepo) = EmptyMocks();
        prRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PurchaseRequest>());
        itemRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        DashboardStatsDto stats =
            await BuildSut(eventRepo, holidays, prRepo, itemRepo).GetStatsAsync();

        Assert.Equal(3, stats.TotalItems);
        Assert.Equal(1, stats.NewItemsPendingReview);
    }
}
